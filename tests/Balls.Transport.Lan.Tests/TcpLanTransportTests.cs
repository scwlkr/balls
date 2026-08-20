using System.Net;
using Balls.Protocol.Remote.V1;

namespace Balls.Transport.Lan.Tests;

[TestClass]
[TestCategory("ProcessIntegration")]
public sealed class TcpLanTransportTests
{
    [TestMethod]
    public async Task Connector_and_listener_exchange_bytes_without_treating_address_as_identity()
    {
        await using var listener = new TcpLanTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var acceptedTask = AcceptOneAsync(listener, timeout.Token);
        var connector = new TcpLanTransportConnector(TimeSpan.FromSeconds(2));
        await using var client = await connector.ConnectAsync(
            listener.BoundAddress,
            timeout.Token);
        await using var server = await acceptedTask;

        Assert.AreEqual(LanTcpEndpoint.ProviderName, client.Provider);
        Assert.AreEqual(LanTcpEndpoint.ProviderName, server.Provider);
        Assert.AreNotEqual(string.Empty, client.PeerAddress);
        Assert.AreNotEqual(string.Empty, server.PeerAddress);

        await client.Stream.WriteAsync("lan"u8.ToArray(), timeout.Token);
        var received = new byte[3];
        await server.Stream.ReadExactlyAsync(received, timeout.Token);
        CollectionAssert.AreEqual("lan"u8.ToArray(), received);
    }

    [TestMethod]
    public async Task Cancelled_accept_stops_cleanly_and_disposal_is_idempotent()
    {
        var listener = new TcpLanTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        using var cancellation = new CancellationTokenSource();
        var accept = AcceptOneAsync(listener, cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => accept);
        await listener.DisposeAsync();
        await listener.DisposeAsync();
    }

    private static async Task<UntrustedRemoteConnection> AcceptOneAsync(
        TcpLanTransportListener listener,
        CancellationToken cancellationToken)
    {
        await foreach (var connection in listener.AcceptAsync(cancellationToken))
        {
            return connection;
        }

        throw new InvalidOperationException("Listener ended before accepting a connection.");
    }
}
