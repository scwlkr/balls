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
}
