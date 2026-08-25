using System.Text.Json;
using System.Text.Json.Serialization;
using Balls.Core;
using Balls.Protocol.Remote.V1;

namespace Balls.Daemon;

internal sealed record SignedCircleFilesSyncRequest(
    string CircleId,
    string MemberId,
    string NodeId,
    string RequestId,
    byte[] MemberSignature,
    byte[] NodeSignature);

internal sealed record CircleFilesSyncWireItem(
    CircleFilesContribution Contribution,
    MemberAccessGrant Grant,
    PublicIdentityCredential OwnerCredential,
    CircleFilesProviderCredentialBinding Binding,
    byte[] Secret);

internal sealed record CircleFilesSyncWireResponse(
    string CircleId,
    string MemberId,
    string RequestId,
    CircleFilesSyncWireItem[] Grants,
    string? ErrorCode = null);

internal static class TrustedCircleFilesWireCodec
{
    private const byte RequestKind = 0x70;
    private const byte ResponseKind = 0x71;
    private const int MaximumEncodedLength = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static bool IsRequest(ReadOnlySpan<byte> encoded) =>
        encoded.Length > 1 && encoded[0] == RequestKind;

    internal static byte[] EncodeRequest(SignedCircleFilesSyncRequest request) =>
        Encode(RequestKind, request);

    internal static SignedCircleFilesSyncRequest DecodeRequest(ReadOnlySpan<byte> encoded) =>
        Decode<SignedCircleFilesSyncRequest>(encoded, RequestKind);

    internal static byte[] EncodeResponse(CircleFilesSyncWireResponse response) =>
        Encode(ResponseKind, response);

    internal static CircleFilesSyncWireResponse DecodeResponse(ReadOnlySpan<byte> encoded) =>
        Decode<CircleFilesSyncWireResponse>(encoded, ResponseKind);

    private static byte[] Encode<T>(byte kind, T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (json.Length + 1 > MaximumEncodedLength)
        {
            throw new RemoteChannelException("oversized");
        }

        var result = new byte[json.Length + 1];
        result[0] = kind;
        json.CopyTo(result, 1);
        return result;
    }

    private static T Decode<T>(ReadOnlySpan<byte> encoded, byte kind)
    {
        if (encoded.Length is < 2 or > MaximumEncodedLength || encoded[0] != kind)
        {
            throw new RemoteChannelException("malformed");
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(encoded[1..], Options)
                ?? throw new RemoteChannelException("malformed");
            if (!encoded.SequenceEqual(Encode(kind, result)))
            {
                throw new RemoteChannelException("malformed");
            }

            return result;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new RemoteChannelException("malformed");
        }
    }
}
