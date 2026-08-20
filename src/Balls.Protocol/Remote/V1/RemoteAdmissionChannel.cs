using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Balls.Protocol.Remote.V1;

public sealed class RemoteAdmissionChannel : IAsyncDisposable
{
    private readonly SslStream stream;
    private readonly RemoteFrameReader reader;
    private readonly RemoteFrameWriter writer;
    private int disposed;

    private RemoteAdmissionChannel(
        SslStream stream,
        UntrustedRemoteConnection connection,
        PublicKeyCredential peerTransportCredential,
        RemoteChannelLimits limits)
    {
        this.stream = stream;
        reader = new RemoteFrameReader(limits);
        writer = new RemoteFrameWriter(limits);
        Provider = connection.Provider;
        PeerAddress = connection.PeerAddress;
        PeerTransportCredential = peerTransportCredential;
    }

    public string Provider { get; }

    public string PeerAddress { get; }

    public PublicKeyCredential PeerTransportCredential { get; }

    public bool IsEncrypted => stream.IsEncrypted;

    public static async Task<RemoteAdmissionChannel> ConnectAsync(
        UntrustedRemoteConnection connection,
        string targetHost,
        string expectedServerTransportKeyId,
        X509Certificate2 proposedClientCertificate,
        RemoteChannelLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(proposedClientCertificate);
        var channelLimits = limits ?? RemoteChannelLimits.Default;
        channelLimits.Validate();
        var tls = new SslStream(connection.Stream, leaveInnerStreamOpen: false);
        try
        {
            using var timeout = RemoteFrameWriter.CreateTimeout(
                channelLimits.HandshakeTimeout,
                cancellationToken);
            await tls.AuthenticateAsClientAsync(
                RemoteTls.CreateAdmissionClientOptions(
                    targetHost,
                    expectedServerTransportKeyId,
                    proposedClientCertificate),
                timeout.Token).ConfigureAwait(false);
            EnsureNegotiatedPolicy(tls);
            var peer = RemoteTls.ReadTransportCredential(tls.RemoteCertificate)
                ?? throw new RemoteChannelException("authentication_failed");
            return new RemoteAdmissionChannel(tls, connection, peer, channelLimits);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw new RemoteChannelException("timeout");
        }
        catch (RemoteChannelException)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is
            AuthenticationException or
            CryptographicException or
            IOException or
            InvalidOperationException)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw new RemoteChannelException("authentication_failed");
        }
    }

    public static async Task<RemoteAdmissionChannel> AcceptAsync(
        UntrustedRemoteConnection connection,
        X509Certificate2 serverCertificate,
        RemoteChannelLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(serverCertificate);
        var channelLimits = limits ?? RemoteChannelLimits.Default;
        channelLimits.Validate();
        var tls = new SslStream(connection.Stream, leaveInnerStreamOpen: false);
        try
        {
            using var timeout = RemoteFrameWriter.CreateTimeout(
                channelLimits.HandshakeTimeout,
                cancellationToken);
            await tls.AuthenticateAsServerAsync(
                RemoteTls.CreateAdmissionServerOptions(serverCertificate),
                timeout.Token).ConfigureAwait(false);
            EnsureNegotiatedPolicy(tls);
            var peer = RemoteTls.ReadTransportCredential(tls.RemoteCertificate)
                ?? throw new RemoteChannelException("authentication_failed");
            return new RemoteAdmissionChannel(tls, connection, peer, channelLimits);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw new RemoteChannelException("timeout");
        }
        catch (RemoteChannelException)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is
            AuthenticationException or
            CryptographicException or
            IOException or
            InvalidOperationException)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw new RemoteChannelException("authentication_failed");
        }
    }

    public Task WriteAsync(RemoteFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        return writer.WriteAsync(stream, frame, cancellationToken);
    }

    public Task<RemoteFrame> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        return reader.ReadAsync(stream, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void EnsureNegotiatedPolicy(SslStream stream)
    {
        if (!stream.IsEncrypted
            || stream.SslProtocol != SslProtocols.Tls13
            || stream.NegotiatedApplicationProtocol != new SslApplicationProtocol(
                RemoteSecurityProtocol.Alpn))
        {
            throw new RemoteChannelException("downgraded");
        }
    }
}
