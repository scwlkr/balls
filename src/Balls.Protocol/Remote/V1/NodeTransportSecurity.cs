using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Balls.Protocol.Remote.V1;

public static class NodeTransportSecurity
{
    private static readonly byte[] Domain =
        "balls/trusted-circle/node-transport-binding/v1\0"u8.ToArray();

    public static SignedNodeTransportBinding Sign(
        NodeTransportBinding binding,
        PublicKeyCredential rootCredential,
        ECDsa rootKey)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(rootCredential);
        ArgumentNullException.ThrowIfNull(rootKey);
        var actual = RemoteIdentity.CreateCredential(KeyRole.CircleAuthority, rootKey);
        if (!CredentialsEqual(actual, rootCredential))
        {
            throw new ArgumentException(
                "The signing key does not match the Circle authority credential.",
                nameof(rootKey));
        }

        return new SignedNodeTransportBinding(
            binding,
            rootCredential,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteIdentity.Sign(Encode(binding), rootKey));
    }

    public static byte[] Encode(NodeTransportBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        using var stream = new MemoryStream();
        stream.Write(Domain);
        WriteInt32(stream, binding.Version);
        WriteText(stream, binding.CircleId);
        WriteText(stream, binding.NodeId);
        WriteInt64(stream, binding.AuthorityGeneration);
        WriteCredential(stream, binding.TransportCredential);
        WriteInt64(stream, binding.NotBeforeUtc.ToUnixTimeSeconds());
        WriteInt64(stream, binding.ExpiresAtUtc.ToUnixTimeSeconds());
        WriteInt32(stream, binding.MinimumProtocolVersion);
        WriteInt32(stream, binding.MaximumProtocolVersion);
        return stream.ToArray();
    }

    public static NodeTransportValidationResult Validate(
        SignedNodeTransportBinding signedBinding,
        NodeTransportVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(signedBinding);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var binding = signedBinding.Binding;
            if (binding.Version != RemoteSecurityProtocol.Version)
            {
                return Rejected(NodeTransportRejectionCode.UnsupportedVersion);
            }

            if (!HasValidShape(signedBinding, context))
            {
                return Rejected(NodeTransportRejectionCode.Malformed);
            }

            if (signedBinding.SignatureSuite != RemoteSecurityProtocol.SignatureSuite)
            {
                return Rejected(NodeTransportRejectionCode.UnsupportedSuite);
            }

            if (!CredentialsEqual(
                    signedBinding.AuthorityCredential,
                    context.TrustedRootCredential))
            {
                return Rejected(NodeTransportRejectionCode.UnauthorizedAuthority);
            }

            if (!RemoteIdentity.Verify(
                    Encode(binding),
                    signedBinding.AuthoritySignature,
                    signedBinding.AuthorityCredential))
            {
                return Rejected(NodeTransportRejectionCode.Forged);
            }

            if (context.RevokedKeyIds.Contains(signedBinding.AuthorityCredential.KeyId)
                || context.RevokedKeyIds.Contains(binding.TransportCredential.KeyId))
            {
                return Rejected(NodeTransportRejectionCode.Revoked);
            }

            if (binding.AuthorityGeneration < context.MinimumAuthorityGeneration)
            {
                return Rejected(NodeTransportRejectionCode.StaleAuthorityState);
            }

            if (binding.CircleId != context.ExpectedCircleId)
            {
                return Rejected(NodeTransportRejectionCode.WrongCircle);
            }

            if (binding.NodeId != context.ExpectedNodeId)
            {
                return Rejected(NodeTransportRejectionCode.WrongNode);
            }

            if (context.NowUtc < binding.NotBeforeUtc)
            {
                return Rejected(NodeTransportRejectionCode.NotYetValid);
            }

            if (context.NowUtc >= binding.ExpiresAtUtc)
            {
                return Rejected(NodeTransportRejectionCode.Expired);
            }

            var minimum = Math.Max(
                binding.MinimumProtocolVersion,
                context.SupportedMinimumProtocolVersion);
            var maximum = Math.Min(
                binding.MaximumProtocolVersion,
                context.SupportedMaximumProtocolVersion);
            if (minimum > maximum)
            {
                return Rejected(NodeTransportRejectionCode.Downgraded);
            }

            return NodeTransportValidationResult.Valid(maximum);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            CryptographicException or
            InvalidOperationException or
            OverflowException)
        {
            return Rejected(NodeTransportRejectionCode.Malformed);
        }
    }

    private static bool HasValidShape(
        SignedNodeTransportBinding signedBinding,
        NodeTransportVerificationContext context)
    {
        var binding = signedBinding.Binding;
        return IsCanonicalUuid(binding.CircleId)
            && IsCanonicalUuid(binding.NodeId)
            && binding.AuthorityGeneration > 0
            && binding.TransportCredential.Role == KeyRole.Transport
            && RemoteIdentity.IsValidCredential(binding.TransportCredential)
            && IsUtc(binding.NotBeforeUtc)
            && IsUtc(binding.ExpiresAtUtc)
            && binding.NotBeforeUtc < binding.ExpiresAtUtc
            && binding.MinimumProtocolVersion > 0
            && binding.MinimumProtocolVersion <= binding.MaximumProtocolVersion
            && signedBinding.AuthorityCredential.Role == KeyRole.CircleAuthority
            && RemoteIdentity.IsValidCredential(signedBinding.AuthorityCredential)
            && signedBinding.AuthoritySignature is { Length: 64 }
            && IsCanonicalUuid(context.ExpectedCircleId)
            && IsCanonicalUuid(context.ExpectedNodeId)
            && context.TrustedRootCredential.Role == KeyRole.CircleAuthority
            && RemoteIdentity.IsValidCredential(context.TrustedRootCredential)
            && IsUtc(context.NowUtc)
            && context.MinimumAuthorityGeneration > 0
            && context.SupportedMinimumProtocolVersion > 0
            && context.SupportedMinimumProtocolVersion
                <= context.SupportedMaximumProtocolVersion;
    }

    private static bool CredentialsEqual(PublicKeyCredential left, PublicKeyCredential right) =>
        left.Role == right.Role
        && left.Algorithm == right.Algorithm
        && left.KeyId == right.KeyId
        && CryptographicOperations.FixedTimeEquals(
            left.SubjectPublicKeyInfo,
            right.SubjectPublicKeyInfo);

    private static bool IsCanonicalUuid(string value) =>
        Guid.TryParseExact(value, "D", out var identifier)
        && identifier.ToString("D") == value;

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static NodeTransportValidationResult Rejected(NodeTransportRejectionCode code) =>
        NodeTransportValidationResult.Rejected(code);

    private static void WriteCredential(Stream stream, PublicKeyCredential credential)
    {
        WriteInt32(stream, (int)credential.Role);
        WriteText(stream, credential.Algorithm);
        WriteText(stream, credential.KeyId);
        WriteBytes(stream, credential.SubjectPublicKeyInfo);
    }

    private static void WriteText(Stream stream, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Length > 1024 || value.Any(character => character > 0x7f))
        {
            throw new ArgumentException("Signed text must be non-empty bounded ASCII.", nameof(value));
        }

        WriteBytes(stream, Encoding.ASCII.GetBytes(value));
    }

    private static void WriteBytes(Stream stream, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 16 * 1024)
        {
            throw new ArgumentException("Signed byte strings must be bounded.", nameof(value));
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
