using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Balls.Protocol.Remote.V1;

public sealed record CircleMessage(
    int Version,
    string MessageId,
    string CircleId,
    string AuthorMemberId,
    string AuthorNodeId,
    DateTimeOffset AuthoredAtUtc,
    string Text);

public sealed record SignedCircleMessage(
    CircleMessage Message,
    string SignatureSuite,
    byte[] MemberSignature,
    byte[] NodeSignature);

public sealed record CircleMessageReceipt(
    CircleMessage Message,
    long Sequence,
    DateTimeOffset AcceptedAtUtc,
    byte[] MessageDigest,
    string SignatureSuite,
    byte[] AnchorSignature);

public sealed record CircleMessageVerificationContext(
    string ExpectedCircleId,
    string ExpectedMemberId,
    string ExpectedNodeId,
    PublicKeyCredential MemberCredential,
    PublicKeyCredential NodeCredential,
    bool IsAuthorizedAuthor,
    DateTimeOffset NowUtc);

public enum CircleMessageRejectionCode
{
    None,
    Malformed,
    UnsupportedSuite,
    Unauthorized,
    Forged,
    WrongCircle,
    WrongNode,
    Replayed,
    Conflict,
}

public sealed record CircleMessageValidationResult(
    bool IsAccepted,
    CircleMessageRejectionCode RejectionCode)
{
    public static CircleMessageValidationResult Accepted() =>
        new(true, CircleMessageRejectionCode.None);

    public static CircleMessageValidationResult Rejected(CircleMessageRejectionCode code) =>
        new(false, code);
}

public sealed class CircleMessageProtocolException : Exception
{
    public CircleMessageProtocolException(string code)
        : base("The remote Circle message is invalid.")
    {
        Code = code;
    }

    public string Code { get; }
}

public static class CircleMessageSecurity
{
    public const int MaximumTextBytes = 4096;
    private static readonly byte[] MessageDomain =
        "balls/trusted-circle/message/v1\0"u8.ToArray();
    private static readonly byte[] ReceiptDomain =
        "balls/trusted-circle/message-receipt/v1\0"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] EncodeMessage(CircleMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var stream = new MemoryStream();
        stream.Write(MessageDomain);
        WriteInt32(stream, message.Version);
        WriteGuid(stream, message.MessageId);
        WriteGuid(stream, message.CircleId);
        WriteGuid(stream, message.AuthorMemberId);
        WriteGuid(stream, message.AuthorNodeId);
        WriteInt64(stream, message.AuthoredAtUtc.ToUnixTimeSeconds());
        WriteText(stream, message.Text);
        return stream.ToArray();
    }

    public static CircleMessageValidationResult Validate(
        SignedCircleMessage signed,
        CircleMessageVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(context);
        if (!IsStructurallyValid(signed.Message)
            || context.NowUtc.Offset != TimeSpan.Zero
            || context.MemberCredential.Role != KeyRole.Member
            || context.NodeCredential.Role != KeyRole.Node
            || !RemoteIdentity.IsValidCredential(context.MemberCredential)
            || !RemoteIdentity.IsValidCredential(context.NodeCredential))
        {
            return CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.Malformed);
        }

        if (signed.SignatureSuite != RemoteSecurityProtocol.SignatureSuite)
        {
            return CircleMessageValidationResult.Rejected(
                CircleMessageRejectionCode.UnsupportedSuite);
        }

        if (signed.Message.CircleId != context.ExpectedCircleId)
        {
            return CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.WrongCircle);
        }

        if (signed.Message.AuthorNodeId != context.ExpectedNodeId)
        {
            return CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.WrongNode);
        }

        if (signed.Message.AuthorMemberId != context.ExpectedMemberId
            || !context.IsAuthorizedAuthor)
        {
            return CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.Unauthorized);
        }

        if (signed.Message.AuthoredAtUtc > context.NowUtc.AddMinutes(5))
        {
            return CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.Malformed);
        }

        var transcript = EncodeMessage(signed.Message);
        return RemoteIdentity.Verify(transcript, signed.MemberSignature, context.MemberCredential)
            && RemoteIdentity.Verify(transcript, signed.NodeSignature, context.NodeCredential)
            ? CircleMessageValidationResult.Accepted()
            : CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.Forged);
    }

    public static bool IsValidText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            return StrictUtf8.GetByteCount(text) <= MaximumTextBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public static CircleMessageReceipt SignReceipt(
        SignedCircleMessage signed,
        long sequence,
        DateTimeOffset acceptedAtUtc,
        ECDsa anchorKey)
    {
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(anchorKey);
        var receipt = CreateReceipt(signed, sequence, acceptedAtUtc, []);
        return receipt with
        {
            AnchorSignature = RemoteIdentity.Sign(EncodeReceiptTranscript(receipt), anchorKey),
        };
    }

    public static CircleMessageReceipt CreateReceipt(
        SignedCircleMessage signed,
        long sequence,
        DateTimeOffset acceptedAtUtc,
        byte[] anchorSignature) =>
        new(
            signed.Message,
            sequence,
            acceptedAtUtc,
            DigestSignedMessage(signed),
            RemoteSecurityProtocol.SignatureSuite,
            anchorSignature);

    public static byte[] DigestSignedMessage(SignedCircleMessage signed) =>
        SHA256.HashData(EncodeMessage(signed.Message));

    public static byte[] EncodeReceiptTranscript(CircleMessageReceipt receipt) =>
        EncodeReceipt(receipt);

    public static CircleMessageValidationResult ValidateReceipt(
        CircleMessageReceipt receipt,
        SignedCircleMessage expectedMessage,
        PublicKeyCredential anchorCredential)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(expectedMessage);
        ArgumentNullException.ThrowIfNull(anchorCredential);
        if (!IsStructurallyValid(receipt.Message)
            || receipt.Sequence <= 0
            || !IsProtocolSecond(receipt.AcceptedAtUtc)
            || receipt.MessageDigest is not { Length: SHA256.HashSizeInBytes }
            || receipt.AnchorSignature is not { Length: 64 }
            || anchorCredential.Role != KeyRole.Anchor
            || !RemoteIdentity.IsValidCredential(anchorCredential))
        {
            return CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.Malformed);
        }

        if (receipt.SignatureSuite != RemoteSecurityProtocol.SignatureSuite)
        {
            return CircleMessageValidationResult.Rejected(
                CircleMessageRejectionCode.UnsupportedSuite);
        }

        var expectedDigest = DigestSignedMessage(expectedMessage);
        if (receipt.Message != expectedMessage.Message
            || !CryptographicOperations.FixedTimeEquals(receipt.MessageDigest, expectedDigest))
        {
            return CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.Conflict);
        }

        return RemoteIdentity.Verify(
                EncodeReceiptTranscript(receipt),
                receipt.AnchorSignature,
                anchorCredential)
            ? CircleMessageValidationResult.Accepted()
            : CircleMessageValidationResult.Rejected(CircleMessageRejectionCode.Forged);
    }

    internal static byte[] EncodeSignedMessage(SignedCircleMessage signed)
    {
        using var stream = new MemoryStream();
        WriteBytes(stream, EncodeMessage(signed.Message));
        WriteText(stream, signed.SignatureSuite);
        WriteBytes(stream, signed.MemberSignature);
        WriteBytes(stream, signed.NodeSignature);
        return stream.ToArray();
    }

    internal static byte[] EncodeReceipt(CircleMessageReceipt receipt)
    {
        using var stream = new MemoryStream();
        stream.Write(ReceiptDomain);
        WriteBytes(stream, EncodeMessage(receipt.Message));
        WriteInt64(stream, receipt.Sequence);
        WriteInt64(stream, receipt.AcceptedAtUtc.ToUnixTimeSeconds());
        WriteBytes(stream, receipt.MessageDigest);
        WriteText(stream, receipt.SignatureSuite);
        return stream.ToArray();
    }

    internal static bool IsStructurallyValid(CircleMessage message)
    {
        if (message is null
            || message.Version != RemoteSecurityProtocol.Version
            || !IsCanonicalGuid(message.MessageId)
            || !IsCanonicalGuid(message.CircleId)
            || !IsCanonicalGuid(message.AuthorMemberId)
            || !IsCanonicalGuid(message.AuthorNodeId)
            || !IsProtocolSecond(message.AuthoredAtUtc)
            || !IsValidText(message.Text))
        {
            return false;
        }

        return true;
    }

    private static bool IsCanonicalGuid(string value) =>
        Guid.TryParseExact(value, "D", out var parsed)
        && parsed != Guid.Empty
        && value == parsed.ToString("D");

    private static bool IsProtocolSecond(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero
        && value == DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());

    internal static void WriteGuid(Stream stream, string value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!Guid.TryParseExact(value, "D", out var identifier)
            || identifier == Guid.Empty
            || !identifier.TryWriteBytes(bytes, bigEndian: true, out var written)
            || written != bytes.Length)
        {
            throw new CircleMessageProtocolException("malformed");
        }

        stream.Write(bytes);
    }

    internal static void WriteText(Stream stream, string value)
    {
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            throw new CircleMessageProtocolException("malformed");
        }

        WriteBytes(stream, bytes);
    }

    internal static void WriteBytes(Stream stream, ReadOnlySpan<byte> value)
    {
        WriteInt32(stream, value.Length);
        stream.Write(value);
    }

    internal static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    internal static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

public static class CircleMessageWireCodec
{
    private const int MaximumPayloadBytes = 64 * 1024;
    private const byte RequestKind = 1;
    private const byte ReceiptKind = 2;
    private const byte RejectionKind = 3;
    private static readonly byte[] Magic = "BMSG"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] EncodeRequest(SignedCircleMessage signed)
    {
        ArgumentNullException.ThrowIfNull(signed);
        using var stream = Start(RequestKind);
        WriteMessage(stream, signed.Message);
        CircleMessageSecurity.WriteText(stream, signed.SignatureSuite);
        CircleMessageSecurity.WriteBytes(stream, signed.MemberSignature);
        CircleMessageSecurity.WriteBytes(stream, signed.NodeSignature);
        return Finish(stream);
    }

    public static SignedCircleMessage DecodeRequest(ReadOnlySpan<byte> encoded)
    {
        var reader = new Reader(encoded, RequestKind);
        var value = new SignedCircleMessage(
            reader.ReadMessage(),
            reader.ReadText(128),
            reader.ReadBytes(64),
            reader.ReadBytes(64));
        reader.EnsureComplete();
        return value;
    }

    public static byte[] EncodeReceipt(CircleMessageReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        using var stream = Start(ReceiptKind);
        WriteMessage(stream, receipt.Message);
        CircleMessageSecurity.WriteInt64(stream, receipt.Sequence);
        CircleMessageSecurity.WriteInt64(stream, receipt.AcceptedAtUtc.ToUnixTimeSeconds());
        CircleMessageSecurity.WriteBytes(stream, receipt.MessageDigest);
        CircleMessageSecurity.WriteText(stream, receipt.SignatureSuite);
        CircleMessageSecurity.WriteBytes(stream, receipt.AnchorSignature);
        return Finish(stream);
    }

    public static CircleMessageReceipt DecodeReceipt(ReadOnlySpan<byte> encoded)
    {
        var reader = new Reader(encoded, ReceiptKind);
        var value = new CircleMessageReceipt(
            reader.ReadMessage(),
            reader.ReadInt64(),
            reader.ReadTimestamp(),
            reader.ReadBytes(SHA256.HashSizeInBytes),
            reader.ReadText(128),
            reader.ReadBytes(64));
        reader.EnsureComplete();
        return value;
    }

    public static byte[] EncodeRejection(string code)
    {
        using var stream = Start(RejectionKind);
        CircleMessageSecurity.WriteText(stream, code);
        return Finish(stream);
    }

    public static bool TryDecodeRejection(ReadOnlySpan<byte> encoded, out string? code)
    {
        code = null;
        if (encoded.Length is < 5 or > MaximumPayloadBytes
            || !encoded[..4].SequenceEqual(Magic)
            || encoded[4] != RejectionKind)
        {
            return false;
        }

        var reader = new Reader(encoded, RejectionKind);
        code = reader.ReadText(64);
        reader.EnsureComplete();
        return true;
    }

    private static MemoryStream Start(byte kind)
    {
        var stream = new MemoryStream();
        stream.Write(Magic);
        stream.WriteByte(kind);
        return stream;
    }

    private static byte[] Finish(MemoryStream stream)
    {
        if (stream.Length > MaximumPayloadBytes)
        {
            throw new CircleMessageProtocolException("oversized");
        }

        return stream.ToArray();
    }

    private static void WriteMessage(Stream stream, CircleMessage message)
    {
        CircleMessageSecurity.WriteInt32(stream, message.Version);
        CircleMessageSecurity.WriteGuid(stream, message.MessageId);
        CircleMessageSecurity.WriteGuid(stream, message.CircleId);
        CircleMessageSecurity.WriteGuid(stream, message.AuthorMemberId);
        CircleMessageSecurity.WriteGuid(stream, message.AuthorNodeId);
        CircleMessageSecurity.WriteInt64(stream, message.AuthoredAtUtc.ToUnixTimeSeconds());
        CircleMessageSecurity.WriteText(stream, message.Text);
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> encoded;
        private int offset;

        internal Reader(ReadOnlySpan<byte> encoded, byte expectedKind)
        {
            if (encoded.Length is < 5 or > MaximumPayloadBytes
                || !encoded[..4].SequenceEqual(Magic)
                || encoded[4] != expectedKind)
            {
                throw new CircleMessageProtocolException(
                    encoded.Length > MaximumPayloadBytes ? "oversized" : "malformed");
            }

            this.encoded = encoded;
            offset = 5;
        }

        internal CircleMessage ReadMessage() => new(
            ReadInt32(),
            ReadGuid(),
            ReadGuid(),
            ReadGuid(),
            ReadGuid(),
            ReadTimestamp(),
            ReadText(CircleMessageSecurity.MaximumTextBytes));

        internal DateTimeOffset ReadTimestamp()
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(ReadInt64());
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new CircleMessageProtocolException("malformed");
            }
        }

        internal int ReadInt32()
        {
            EnsureAvailable(4);
            var value = BinaryPrimitives.ReadInt32BigEndian(encoded.Slice(offset, 4));
            offset += 4;
            return value;
        }

        internal long ReadInt64()
        {
            EnsureAvailable(8);
            var value = BinaryPrimitives.ReadInt64BigEndian(encoded.Slice(offset, 8));
            offset += 8;
            return value;
        }

        internal string ReadGuid()
        {
            EnsureAvailable(16);
            var value = new Guid(encoded.Slice(offset, 16), bigEndian: true).ToString("D");
            offset += 16;
            return value;
        }

        internal byte[] ReadBytes(int maximum)
        {
            var length = ReadInt32();
            if (length < 0 || length > maximum)
            {
                throw new CircleMessageProtocolException(
                    length > maximum ? "oversized" : "malformed");
            }

            EnsureAvailable(length);
            var value = encoded.Slice(offset, length).ToArray();
            offset += length;
            return value;
        }

        internal string ReadText(int maximum)
        {
            try
            {
                return StrictUtf8.GetString(ReadBytes(maximum));
            }
            catch (DecoderFallbackException)
            {
                throw new CircleMessageProtocolException("malformed");
            }
        }

        internal void EnsureComplete()
        {
            if (offset != encoded.Length)
            {
                throw new CircleMessageProtocolException("malformed");
            }
        }

        private void EnsureAvailable(int length)
        {
            if (length < 0 || offset > encoded.Length - length)
            {
                throw new CircleMessageProtocolException("malformed");
            }
        }
    }
}
