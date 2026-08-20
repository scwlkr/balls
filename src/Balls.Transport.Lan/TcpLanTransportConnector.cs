using System.Net.Sockets;
using Balls.Protocol.Remote.V1;

namespace Balls.Transport.Lan;

public sealed class TcpLanTransportConnector : IRemoteTransportConnector
{
    private readonly TimeSpan connectTimeout;

    public TcpLanTransportConnector(TimeSpan? connectTimeout = null)
    {
        this.connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);
        if (this.connectTimeout < TimeSpan.FromMilliseconds(100)
            || this.connectTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectTimeout),
                "The LAN connection timeout must be between 100 milliseconds and one minute.");
        }
    }

    public async ValueTask<UntrustedRemoteConnection> ConnectAsync(
        RemoteTransportAddress address,
        CancellationToken cancellationToken = default)
    {
        var endpoint = LanTcpEndpoint.Parse(address);
        var client = new TcpClient(endpoint.AddressFamily)
        {
            NoDelay = true,
        };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(connectTimeout);
            await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            return new UntrustedRemoteConnection(
                client.GetStream(),
                LanTcpEndpoint.ProviderName,
                client.Client.RemoteEndPoint?.ToString() ?? endpoint.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException("The LAN transport connection timed out.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
