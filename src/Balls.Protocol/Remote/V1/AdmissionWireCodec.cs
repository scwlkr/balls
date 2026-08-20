using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balls.Protocol.Remote.V1;

public enum AdmissionWireKind : byte
{
    Hello = 1,
    Challenge = 2,
    Request = 3,
    Response = 4,
    Rejection = 5,
}

public sealed record AdmissionHello(
    CircleInvitationPackage Package,
    string MemberDisplayName,
    string NodeDisplayName);

public sealed record AdmissionChallenge(byte[] AnchorChallenge);

public sealed record AdmissionRequestEnvelope(
    SignedAdmissionRequest Request,
    string MemberDisplayName,
    string NodeDisplayName);

public sealed record AdmissionResponseEnvelope(SignedAdmissionResponse Response);

public sealed record AdmissionRejection(string Code);

public static class AdmissionWireCodec
{
    public const int MaximumEncodedLength = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static byte[] EncodeHello(AdmissionHello value) =>
        Encode(AdmissionWireKind.Hello, value);

    public static AdmissionHello DecodeHello(ReadOnlySpan<byte> encoded) =>
        Decode<AdmissionHello>(encoded, AdmissionWireKind.Hello);

    public static byte[] EncodeChallenge(AdmissionChallenge value) =>
        Encode(AdmissionWireKind.Challenge, value);

    public static AdmissionChallenge DecodeChallenge(ReadOnlySpan<byte> encoded) =>
        Decode<AdmissionChallenge>(encoded, AdmissionWireKind.Challenge);

    public static byte[] EncodeRequest(AdmissionRequestEnvelope value) =>
        Encode(AdmissionWireKind.Request, value);

    public static AdmissionRequestEnvelope DecodeRequest(ReadOnlySpan<byte> encoded) =>
        Decode<AdmissionRequestEnvelope>(encoded, AdmissionWireKind.Request);

    public static byte[] EncodeResponse(AdmissionResponseEnvelope value) =>
        Encode(AdmissionWireKind.Response, value);

    public static AdmissionResponseEnvelope DecodeResponse(ReadOnlySpan<byte> encoded) =>
        Decode<AdmissionResponseEnvelope>(encoded, AdmissionWireKind.Response);

    public static byte[] EncodeRejection(AdmissionRejection value) =>
        Encode(AdmissionWireKind.Rejection, value);

    public static AdmissionRejection DecodeRejection(ReadOnlySpan<byte> encoded) =>
        Decode<AdmissionRejection>(encoded, AdmissionWireKind.Rejection);

    public static AdmissionWireKind ReadKind(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < 2 || !Enum.IsDefined((AdmissionWireKind)encoded[0]))
        {
            throw new RemoteChannelException("malformed");
        }

        return (AdmissionWireKind)encoded[0];
    }

    private static byte[] Encode<T>(AdmissionWireKind kind, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (json.Length + 1 > MaximumEncodedLength)
        {
            throw new RemoteChannelException("oversized");
        }

        var encoded = new byte[json.Length + 1];
        encoded[0] = (byte)kind;
        json.CopyTo(encoded, 1);
        return encoded;
    }

    private static T Decode<T>(ReadOnlySpan<byte> encoded, AdmissionWireKind expectedKind)
    {
        if (encoded.Length is < 2 or > MaximumEncodedLength
            || ReadKind(encoded) != expectedKind)
        {
            throw new RemoteChannelException("malformed");
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(encoded[1..], Options)
                ?? throw new RemoteChannelException("malformed");
            var canonical = Encode(expectedKind, value);
            if (!encoded.SequenceEqual(canonical))
            {
                throw new RemoteChannelException("malformed");
            }

            return value;
        }
        catch (RemoteChannelException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            JsonException or
            NotSupportedException)
        {
            throw new RemoteChannelException("malformed");
        }
    }
}
