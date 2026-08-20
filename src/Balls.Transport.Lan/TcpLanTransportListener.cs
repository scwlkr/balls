using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Balls.Protocol.Remote.V1;

namespace Balls.Transport.Lan;

public sealed class TcpLanTransportListener : IRemoteTransportListener
{
    private readonly TcpListener listener;
    private int disposed;

    public TcpLanTransportListener(IPEndPoint localEndpoint)
    {
        LanTcpEndpoint.Validate(localEndpoint, allowEphemeralPort: true);
        listener = new TcpListener(localEndpoint);
        listener.Start(backlog: 128);
        BoundAddress = new RemoteTransportAddress(
            LanTcpEndpoint.ProviderName,
            ((IPEndPoint)listener.LocalEndpoint).ToString());
    }

    public RemoteTransportAddress BoundAddress { get; }

    public async IAsyncEnumerable<UntrustedRemoteConnection> AcceptAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        while (!cancellationToken.IsCancellationRequested && disposed == 0)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (disposed != 0)
            {
                yield break;
            }

            client.NoDelay = true;
            yield return new UntrustedRemoteConnection(
                client.GetStream(),
                LanTcpEndpoint.ProviderName,
                client.Client.RemoteEndPoint?.ToString() ?? "unknown");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            listener.Stop();
        }

        return ValueTask.CompletedTask;
    }
}
