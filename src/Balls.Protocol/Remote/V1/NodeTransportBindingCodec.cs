using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balls.Protocol.Remote.V1;

public static class NodeTransportBindingCodec
{
    public const int MaximumEncodedLength = 16 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static byte[] Encode(SignedNodeTransportBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(binding, Options);
        if (encoded.Length is 0 or > MaximumEncodedLength)
        {
            throw new RemoteChannelException("oversized");
        }

        return encoded;
    }

    public static SignedNodeTransportBinding Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length is 0 or > MaximumEncodedLength)
        {
            throw new RemoteChannelException("malformed");
        }

        try
        {
            var value = JsonSerializer.Deserialize<SignedNodeTransportBinding>(encoded, Options)
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
