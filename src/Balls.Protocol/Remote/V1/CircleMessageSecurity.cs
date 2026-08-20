using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Balls.Protocol.Remote.V1;

public static class CircleMessageSecurity
{
    public const int MaximumTextUtf8Bytes = 4 * 1024;
    public static readonly TimeSpan DefaultMaximumClockSkew = TimeSpan.FromMinutes(5);
    private static readonly byte[] RequestDomain =
        "balls/trusted-circle/message-request/v1\0"u8.ToArray();
    private static readonly byte[] SignedRequestDomain =
        "balls/trusted-circle/signed-message-request/v1\0"u8.ToArray();
    private static readonly byte[] ReceiptDomain =
        "balls/trusted-circle/message-receipt/v1\0"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] EncodeRequest(CircleMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var stream = new MemoryStream();
        stream.Write(RequestDomain);
        WriteInt32(stream, request.Version);
        WriteText(stream, request.CircleId);
        WriteText(stream, request.MessageId);
        WriteText(stream, request.AuthorMemberId);
        WriteText(stream, request.AuthorNodeId);
        WriteCredential(stream, request.MemberCredential);
        WriteCredential(stream, request.NodeCredential);
        WriteText(stream, request.Text, MaximumTextUtf8Bytes);
        WriteInt64(stream, request.AuthoredAtUtc.ToUnixTimeMilliseconds());
        WriteText(stream, request.SignatureSuite);
        return stream.ToArray();
    }

    public static byte[] DigestRequest(SignedCircleMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var stream = new MemoryStream();
        stream.Write(SignedRequestDomain);
        WriteBytes(stream, EncodeRequest(request.Request));
        WriteBytes(stream, request.MemberSignature);
        WriteBytes(stream, request.NodeSignature);
        return SHA256.HashData(stream.ToArray());
    }

    public static byte[] EncodeReceipt(CircleMessageReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        using var stream = new MemoryStream();
        stream.Write(ReceiptDomain);
        WriteInt32(stream, receipt.Version);
        WriteText(stream, receipt.CircleId);
        WriteText(stream, receipt.MessageId);
        WriteInt64(stream, receipt.Sequence);
        WriteText(stream, receipt.AuthorMemberId);
        WriteText(stream, receipt.AuthorNodeId);
        WriteText(stream, receipt.Text, MaximumTextUtf8Bytes);
        WriteInt64(stream, receipt.AuthoredAtUtc.ToUnixTimeMilliseconds());
        WriteInt64(stream, receipt.AcceptedAtUtc.ToUnixTimeMilliseconds());
        WriteBytes(stream, receipt.RequestDigest);
        return stream.ToArray();
    }

    public static CircleMessageValidationResult ValidateRequest(
        SignedCircleMessageRequest signed,
        CircleMessageVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var request = signed.Request;
            if (!HasValidRequestShape(signed, context))
            {
                return Rejected(CircleMessageRejectionCode.Malformed);
            }

            if (request.SignatureSuite != RemoteSecurityProtocol.SignatureSuite)
            {
                return Rejected(CircleMessageRejectionCode.UnsupportedSuite);
            }

            if (!CredentialsEqual(request.MemberCredential, context.TrustedMemberCredential)
                || !CredentialsEqual(request.NodeCredential, context.TrustedNodeCredential))
            {
                return Rejected(CircleMessageRejectionCode.Unauthorized);
            }

            var transcript = EncodeRequest(request);
            if (!RemoteIdentity.Verify(
                    transcript,
                    signed.MemberSignature,
                    request.MemberCredential)
                || !RemoteIdentity.Verify(
                    transcript,
                    signed.NodeSignature,
                    request.NodeCredential))
            {
                return Rejected(CircleMessageRejectionCode.Forged);
            }

            if (context.RevokedKeyIds.Contains(request.MemberCredential.KeyId)
                || context.RevokedKeyIds.Contains(request.NodeCredential.KeyId))
            {
                return Rejected(CircleMessageRejectionCode.Revoked);
            }

            if (request.CircleId != context.ExpectedCircleId)
            {
                return Rejected(CircleMessageRejectionCode.WrongCircle);
            }

            if (request.AuthorNodeId != context.ExpectedPeerNodeId)
            {
                return Rejected(CircleMessageRejectionCode.WrongNode);
            }

            if ((request.AuthoredAtUtc - context.NowUtc).Duration() > context.MaximumClockSkew)
            {
                return Rejected(CircleMessageRejectionCode.Stale);
            }

            return CircleMessageValidationResult.Accepted();
        }
        catch (Exception exception) when (exception is
            ArgumentException or CryptographicException or EncoderFallbackException
            or InvalidOperationException or OverflowException)
        {
            return Rejected(CircleMessageRejectionCode.Malformed);
        }
    }

    public static CircleMessageValidationResult ValidateReceipt(
        SignedCircleMessageReceipt signed,
        CircleMessageReceiptVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var receipt = signed.Receipt;
            var request = context.Request.Request;
            if (signed.SignatureSuite != RemoteSecurityProtocol.SignatureSuite
                || receipt.Version != RemoteSecurityProtocol.Version
                || receipt.Sequence <= 0
                || receipt.RequestDigest.Length != SHA256.HashSizeInBytes
                || receipt.AcceptedAtUtc.Offset != TimeSpan.Zero
                || !IsCanonicalUuid(receipt.CircleId)
                || !IsCanonicalUuid(receipt.MessageId)
                || !IsCanonicalUuid(receipt.AuthorMemberId)
                || !IsCanonicalUuid(receipt.AuthorNodeId)
                || !HasValidText(receipt.Text)
                || context.TrustedAnchorCredential.Role != KeyRole.Anchor
                || !RemoteIdentity.IsValidCredential(context.TrustedAnchorCredential))
            {
                return Rejected(CircleMessageRejectionCode.Malformed);
            }

            if (!RemoteIdentity.Verify(
                    EncodeReceipt(receipt),
                    signed.AnchorSignature,
                    context.TrustedAnchorCredential)
                || !CryptographicOperations.FixedTimeEquals(
                    receipt.RequestDigest,
                    DigestRequest(context.Request)))
            {
                return Rejected(CircleMessageRejectionCode.Forged);
            }

            if (receipt.CircleId != context.ExpectedCircleId
                || receipt.CircleId != request.CircleId)
            {
                return Rejected(CircleMessageRejectionCode.WrongCircle);
            }

            if (receipt.MessageId != request.MessageId
                || receipt.AuthorMemberId != request.AuthorMemberId
                || receipt.AuthorNodeId != request.AuthorNodeId
                || receipt.Text != request.Text
                || receipt.AuthoredAtUtc != request.AuthoredAtUtc)
            {
                return Rejected(CircleMessageRejectionCode.Conflict);
            }

            if (receipt.AcceptedAtUtc > context.NowUtc + context.MaximumClockSkew
                || receipt.AcceptedAtUtc < receipt.AuthoredAtUtc - context.MaximumClockSkew)
            {
                return Rejected(CircleMessageRejectionCode.Stale);
            }

            return CircleMessageValidationResult.Accepted();
        }
        catch (Exception exception) when (exception is
            ArgumentException or CryptographicException or EncoderFallbackException
            or InvalidOperationException or OverflowException)
        {
            return Rejected(CircleMessageRejectionCode.Malformed);
        }
    }

    private static bool HasValidRequestShape(
        SignedCircleMessageRequest signed,
        CircleMessageVerificationContext context)
    {
        var request = signed.Request;
        return request is not null
            && request.Version == RemoteSecurityProtocol.Version
            && IsCanonicalUuid(request.CircleId)
            && IsCanonicalUuid(request.MessageId)
            && IsCanonicalUuid(request.AuthorMemberId)
            && IsCanonicalUuid(request.AuthorNodeId)
            && request.MemberCredential.Role == KeyRole.Member
            && request.NodeCredential.Role == KeyRole.Node
            && RemoteIdentity.IsValidCredential(request.MemberCredential)
            && RemoteIdentity.IsValidCredential(request.NodeCredential)
            && HasValidText(request.Text)
            && request.AuthoredAtUtc.Offset == TimeSpan.Zero
            && signed.MemberSignature.Length == 64
            && signed.NodeSignature.Length == 64
            && context.NowUtc.Offset == TimeSpan.Zero
            && context.MaximumClockSkew > TimeSpan.Zero
            && context.MaximumClockSkew <= TimeSpan.FromHours(1);
    }

    private static bool HasValidText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextUtf8Bytes)
        {
            return false;
        }

        var bytes = StrictUtf8.GetBytes(value);
        return bytes.Length <= MaximumTextUtf8Bytes
            && !value.Any(character => char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'));
    }

    private static bool IsCanonicalUuid(string value) =>
        Guid.TryParseExact(value, "D", out var parsed)
        && string.Equals(value, parsed.ToString("D"), StringComparison.Ordinal);

    private static bool CredentialsEqual(PublicKeyCredential left, PublicKeyCredential right) =>
        left.Role == right.Role
        && left.Algorithm == right.Algorithm
        && left.KeyId == right.KeyId
        && CryptographicOperations.FixedTimeEquals(
            left.SubjectPublicKeyInfo,
            right.SubjectPublicKeyInfo);

    private static CircleMessageValidationResult Rejected(CircleMessageRejectionCode code) =>
        CircleMessageValidationResult.Rejected(code);

    private static void WriteCredential(Stream stream, PublicKeyCredential credential)
    {
        WriteInt32(stream, (int)credential.Role);
        WriteText(stream, credential.Algorithm);
        WriteText(stream, credential.KeyId);
        WriteBytes(stream, credential.SubjectPublicKeyInfo);
    }

    private static void WriteText(Stream stream, string value, int maximumBytes = 1024)
    {
        var bytes = StrictUtf8.GetBytes(value);
        if (bytes.Length is 0 || bytes.Length > maximumBytes)
        {
            throw new ArgumentException("Signed message text is empty or oversized.");
        }

        WriteBytes(stream, bytes);
    }

    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> value)
    {
        if (value.Length > 16 * 1024)
        {
            throw new ArgumentException("Signed message bytes are oversized.");
        }

        WriteInt32(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
