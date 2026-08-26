using System.Net;
using Balls.Protocol.Remote.V1;

namespace Balls.Transport.Lan.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class LanTcpEndpointTests
{
    [TestMethod]
    [DataRow("127.0.0.1:443", 443)]
    [DataRow("10.20.30.40:8443", 8443)]
    [DataRow("172.16.0.1:1", 1)]
    [DataRow("172.31.255.254:65535", 65535)]
    [DataRow("192.168.10.20:9000", 9000)]
    [DataRow("169.254.20.30:5000", 5000)]
    [DataRow("[::1]:443", 443)]
    [DataRow("[fd00::1234]:8443", 8443)]
    [DataRow("[fe80::1]:9000", 9000)]
    public void Private_numeric_endpoints_are_accepted(string value, int expectedPort)
    {
        var endpoint = LanTcpEndpoint.Parse(
            new RemoteTransportAddress(LanTcpEndpoint.ProviderName, value));

        Assert.AreEqual(expectedPort, endpoint.Port);
    }

    [TestMethod]
    [DataRow("https", "127.0.0.1:443")]
    [DataRow("lan-tcp-v1", "node.local:443")]
    [DataRow("lan-tcp-v1", "0.0.0.0:443")]
    [DataRow("lan-tcp-v1", "8.8.8.8:443")]
    [DataRow("lan-tcp-v1", "224.0.0.1:443")]
    [DataRow("lan-tcp-v1", "[::]:443")]
    [DataRow("lan-tcp-v1", "[2001:4860:4860::8888]:443")]
    [DataRow("lan-tcp-v1", "127.0.0.1:0")]
    [DataRow("lan-tcp-v1", "127.0.0.1:65536")]
    public void Non_LAN_or_ambiguous_endpoints_are_rejected(string provider, string value)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => LanTcpEndpoint.Parse(new RemoteTransportAddress(provider, value)));
    }

    [TestMethod]
    public void Automatic_selection_accepts_exactly_one_operational_private_IPv4_address()
    {
        var selected = PrivateIPv4AddressSelector.Select(
        [
            Snapshot("down-private", false, "10.10.10.10"),
            Snapshot("loopback", true, "127.0.0.1"),
            Snapshot("public", true, "8.8.8.8"),
            Snapshot("wildcard", true, "0.0.0.0"),
            Snapshot("ipv6", true, "fd00::1"),
            Snapshot("private", true, "192.168.50.20"),
        ]);

        Assert.IsTrue(selected.IsAvailable);
        Assert.AreEqual(IPAddress.Parse("192.168.50.20"), selected.Address);
        Assert.IsNull(selected.ErrorCode);
    }

    [TestMethod]
    public void Automatic_selection_fails_bounded_when_no_private_IPv4_address_is_available()
    {
        var selected = PrivateIPv4AddressSelector.Select(
        [
            Snapshot("loopback", true, "127.0.0.1"),
            Snapshot("public", true, "203.0.113.10"),
            Snapshot("down-private", false, "172.20.0.2"),
        ]);

        Assert.IsFalse(selected.IsAvailable);
        Assert.AreEqual("private_network_unavailable", selected.ErrorCode);
        Assert.IsTrue(selected.Message.Length <= 120);
        Assert.IsFalse(selected.Message.Contains("203.0.113.10", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Automatic_selection_fails_bounded_when_private_IPv4_addresses_are_ambiguous()
    {
        var selected = PrivateIPv4AddressSelector.Select(
        [
            Snapshot("ethernet", true, "10.20.30.40"),
            Snapshot("wifi", true, "192.168.1.20"),
        ]);

        Assert.IsFalse(selected.IsAvailable);
        Assert.AreEqual("private_network_ambiguous", selected.ErrorCode);
        Assert.IsTrue(selected.Message.Length <= 120);
        Assert.IsFalse(selected.Message.Contains("10.20.30.40", StringComparison.Ordinal));
        Assert.IsFalse(selected.Message.Contains("192.168.1.20", StringComparison.Ordinal));
    }

    private static LanNetworkInterfaceSnapshot Snapshot(
        string id,
        bool operational,
        params string[] addresses) =>
        new(id, operational, addresses.Select(IPAddress.Parse).ToArray());
}
