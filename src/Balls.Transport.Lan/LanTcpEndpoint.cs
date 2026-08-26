using System.Net;
using System.Net.Sockets;
using Balls.Protocol.Remote.V1;

namespace Balls.Transport.Lan;

public static class LanTcpEndpoint
{
    public const string ProviderName = "lan-tcp-v1";

    public static IPEndPoint Parse(RemoteTransportAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!string.Equals(address.Provider, ProviderName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The remote transport provider is unsupported.", nameof(address));
        }

        if (string.IsNullOrWhiteSpace(address.Value)
            || address.Value.Length > 128
            || !IPEndPoint.TryParse(address.Value, out var endpoint))
        {
            throw new ArgumentException(
                "A numeric LAN IP address and port are required.",
                nameof(address));
        }

        Validate(endpoint, allowEphemeralPort: false);
        return endpoint;
    }

    internal static void Validate(IPEndPoint endpoint, bool allowEphemeralPort)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if ((!allowEphemeralPort && endpoint.Port == 0)
            || !IsPrivateOrLoopback(endpoint.Address))
        {
            throw new ArgumentException(
                "The LAN transport requires a private or loopback unicast endpoint.",
                nameof(endpoint));
        }
    }

    public static bool IsPrivateIPv4(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsPrivateIPv4(address);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            && ((bytes[0] & 0xfe) == 0xfc || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80));
    }
}
