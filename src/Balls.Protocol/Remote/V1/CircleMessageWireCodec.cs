using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balls.Protocol.Remote.V1;

public sealed record CircleMessageRequestEnvelope(
    string Type,
    SignedCircleMessageRequest Request);

public sealed record CircleMessageReceiptEnvelope(
    string Type,
    SignedCircleMessageReceipt Receipt);

public sealed record CircleMessageRejection(string Type, string Code);

public static class CircleMessageWireCodec
{
    public const int MaximumEncodedLength = 16 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static byte[] EncodeRequest(SignedCircleMessageRequest request) =>
        Encode(new CircleMessageRequestEnvelope("message-request", request));

    public static SignedCircleMessageRequest DecodeRequest(ReadOnlySpan<byte> encoded)
    {
        var envelope = Decode<CircleMessageRequestEnvelope>(encoded);
        if (envelope.Type != "message-request")
        {
            throw new RemoteChannelException("malformed");
        }

        return envelope.Request;
    }

    public static byte[] EncodeReceipt(SignedCircleMessageReceipt receipt) =>
        Encode(new CircleMessageReceiptEnvelope("message-receipt", receipt));

    public static SignedCircleMessageReceipt DecodeReceipt(ReadOnlySpan<byte> encoded)
    {
        var envelope = Decode<CircleMessageReceiptEnvelope>(encoded);
        if (envelope.Type != "message-receipt")
        {
            throw new RemoteChannelException("malformed");
        }

        return envelope.Receipt;
    }

    public static byte[] EncodeRejection(string code) =>
        Encode(new CircleMessageRejection("message-rejection", code));

    public static bool TryDecodeRejection(ReadOnlySpan<byte> encoded, out string code)
    {
        try
        {
            var value = Decode<CircleMessageRejection>(encoded);
            if (value.Type == "message-rejection" && !string.IsNullOrWhiteSpace(value.Code))
            {
                code = value.Code;
                return true;
            }
        }
        catch (RemoteChannelException)
        {
        }

        code = string.Empty;
        return false;
    }

    private static byte[] Encode<T>(T value)
    {
        var encoded = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (encoded.Length is 0 or > MaximumEncodedLength)
        {
            throw new RemoteChannelException("oversized");
        }

        return encoded;
    }

    private static T Decode<T>(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length is 0 or > MaximumEncodedLength)
        {
            throw new RemoteChannelException("malformed");
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(encoded, Options)
                ?? throw new RemoteChannelException("malformed");
            if (!encoded.SequenceEqual(Encode(value)))
            {
                throw new RemoteChannelException("malformed");
            }

            return value;
        }
        catch (RemoteChannelException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new RemoteChannelException("malformed");
        }
    }
}
