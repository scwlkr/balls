using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Balls.Transport.Lan;

public sealed record LanNetworkInterfaceSnapshot(
    string Id,
    bool IsOperational,
    IReadOnlyList<IPAddress> UnicastAddresses);

public sealed record PrivateIPv4AddressSelection(
    IPAddress? Address,
    string? ErrorCode,
    string Message)
{
    public bool IsAvailable => Address is not null;
}

public static class PrivateIPv4AddressSelector
{
    public static PrivateIPv4AddressSelection Select(
        IEnumerable<LanNetworkInterfaceSnapshot> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);

        var candidates = interfaces
            .Where(network => network.IsOperational)
            .SelectMany(network => network.UnicastAddresses)
            .Where(LanTcpEndpoint.IsPrivateIPv4)
            .Distinct()
            .Take(2)
            .ToArray();

        return candidates.Length switch
        {
            1 => new PrivateIPv4AddressSelection(
                candidates[0],
                null,
                "One private network connection is ready."),
            0 => new PrivateIPv4AddressSelection(
                null,
                "private_network_unavailable",
                "Balls could not find a private network connection for invitations on this device."),
            _ => new PrivateIPv4AddressSelection(
                null,
                "private_network_ambiguous",
                "Balls found more than one private network connection and cannot safely choose one for invitations."),
        };
    }

    public static PrivateIPv4AddressSelection SelectCurrent()
    {
        try
        {
            return Select(
                NetworkInterface.GetAllNetworkInterfaces().Select(
                    network => new LanNetworkInterfaceSnapshot(
                        network.Id,
                        network.OperationalStatus == OperationalStatus.Up
                            && network.NetworkInterfaceType != NetworkInterfaceType.Loopback,
                        network.GetIPProperties()
                            .UnicastAddresses
                            .Select(unicast => unicast.Address)
                            .ToArray())));
        }
        catch (Exception exception) when (exception is NetworkInformationException or SocketException)
        {
            return new PrivateIPv4AddressSelection(
                null,
                "private_network_unavailable",
                "Balls could not inspect private network connections on this device.");
        }
    }
}
