using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Balls.Protocol.Remote.V1;

public sealed record RemotePeerExpectation(
    SignedNodeTransportBinding SignedBinding,
    NodeTransportVerificationContext VerificationContext);

public sealed record RemoteChannelIdentity(
    X509Certificate2 Certificate,
    RemotePeerExpectation Binding);

public sealed class RemoteAuthenticatedChannel : IAsyncDisposable
{
    private readonly SslStream stream;
    private readonly RemoteFrameReader reader;
    private readonly RemoteFrameWriter writer;
    private int disposed;

    private RemoteAuthenticatedChannel(
        SslStream stream,
        UntrustedRemoteConnection connection,
        RemotePeerExpectation peer,
        int negotiatedProtocolVersion,
        RemoteChannelLimits limits)
    {
        this.stream = stream;
        reader = new RemoteFrameReader(limits);
        writer = new RemoteFrameWriter(limits);
        Provider = connection.Provider;
        PeerAddress = connection.PeerAddress;
        CircleId = peer.SignedBinding.Binding.CircleId;
        PeerNodeId = peer.SignedBinding.Binding.NodeId;
        PeerTransportCredential = peer.SignedBinding.Binding.TransportCredential;
        NegotiatedProtocolVersion = negotiatedProtocolVersion;
    }

    public string Provider { get; }

    public string PeerAddress { get; }

    public string CircleId { get; }

    public string PeerNodeId { get; }

    public PublicKeyCredential PeerTransportCredential { get; }

    public int NegotiatedProtocolVersion { get; }

    public bool IsEncrypted => stream.IsEncrypted;

    public static async Task<RemoteAuthenticatedChannel> ConnectAsync(
        UntrustedRemoteConnection connection,
        string targetHost,
        RemoteChannelIdentity localIdentity,
        RemotePeerExpectation expectedServer,
        RemoteChannelLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(expectedServer);
        var channelLimits = limits ?? RemoteChannelLimits.Default;
        channelLimits.Validate();
        ValidateLocalIdentity(localIdentity);
        var validation = ValidateExpectation(expectedServer);
        var tls = new SslStream(connection.Stream, leaveInnerStreamOpen: false);
        try
        {
            using var timeout = RemoteFrameWriter.CreateTimeout(
                channelLimits.HandshakeTimeout,
                cancellationToken);
            await tls.AuthenticateAsClientAsync(
                RemoteTls.CreateClientOptions(
                    targetHost,
                    localIdentity.Certificate,
                    expectedServer.SignedBinding.Binding.TransportCredential),
                timeout.Token).ConfigureAwait(false);
            EnsureNegotiatedPolicy(tls);
            await ConfirmMutualAuthenticationAsync(
                tls,
                localIdentity.Binding,
                expectedServer,
                timeout.Token).ConfigureAwait(false);
            return new RemoteAuthenticatedChannel(
                tls,
                connection,
                expectedServer,
                validation.NegotiatedProtocolVersion!.Value,
                channelLimits);
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

    public static async Task<RemoteAuthenticatedChannel> AcceptAsync(
        UntrustedRemoteConnection connection,
        RemoteChannelIdentity localIdentity,
        IReadOnlyCollection<RemotePeerExpectation> expectedClients,
        RemoteChannelLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(localIdentity);
        ArgumentNullException.ThrowIfNull(expectedClients);
        var channelLimits = limits ?? RemoteChannelLimits.Default;
        channelLimits.Validate();
        ValidateLocalIdentity(localIdentity);
        if (expectedClients.Count is 0 or > 128)
        {
            throw new ArgumentException(
                "Remote v1 requires between one and 128 expected peers.",
                nameof(expectedClients));
        }

        var validated = expectedClients
            .Select(peer => (Peer: peer, Result: ValidateExpectation(peer)))
            .ToArray();
        if (validated.Select(item => item.Peer.SignedBinding.Binding.TransportCredential.KeyId)
                .Distinct(StringComparer.Ordinal).Count()
            != validated.Length)
        {
            throw new ArgumentException("Expected peer transport credentials must be unique.");
        }

        var tls = new SslStream(connection.Stream, leaveInnerStreamOpen: false);
        try
        {
            using var timeout = RemoteFrameWriter.CreateTimeout(
                channelLimits.HandshakeTimeout,
                cancellationToken);
            await tls.AuthenticateAsServerAsync(
                RemoteTls.CreateServerOptions(
                    localIdentity.Certificate,
                    validated.Select(item =>
                        item.Peer.SignedBinding.Binding.TransportCredential).ToArray()),
                timeout.Token).ConfigureAwait(false);
            EnsureNegotiatedPolicy(tls);
            var actual = RemoteTls.ReadTransportCredential(tls.RemoteCertificate)
                ?? throw new RemoteChannelException("authentication_failed");
            var matched = validated.SingleOrDefault(item =>
                CredentialsEqual(
                    item.Peer.SignedBinding.Binding.TransportCredential,
                    actual));
            if (matched.Peer is null)
            {
                throw new RemoteChannelException("authentication_failed");
            }

            await ConfirmMutualAuthenticationAsync(
                tls,
                localIdentity.Binding,
                matched.Peer,
                timeout.Token).ConfigureAwait(false);

            return new RemoteAuthenticatedChannel(
                tls,
                connection,
                matched.Peer,
                matched.Result.NegotiatedProtocolVersion!.Value,
                channelLimits);
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

    private static NodeTransportValidationResult ValidateExpectation(RemotePeerExpectation peer)
    {
        var validation = NodeTransportSecurity.Validate(
            peer.SignedBinding,
            peer.VerificationContext);
        if (!validation.IsValid)
        {
            throw new RemoteChannelException(CodeFor(validation.RejectionCode));
        }

        return validation;
    }

    private static void ValidateLocalIdentity(RemoteChannelIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity.Certificate);
        ArgumentNullException.ThrowIfNull(identity.Binding);
        ValidateExpectation(identity.Binding);
        var certificateCredential = RemoteTls.ReadTransportCredential(identity.Certificate);
        if (!identity.Certificate.HasPrivateKey
            || certificateCredential is null
            || !CredentialsEqual(
                certificateCredential,
                identity.Binding.SignedBinding.Binding.TransportCredential))
        {
            throw new RemoteChannelException("authentication_failed");
        }
    }

    private static async Task ConfirmMutualAuthenticationAsync(
        SslStream stream,
        RemotePeerExpectation localIdentity,
        RemotePeerExpectation peer,
        CancellationToken cancellationToken)
    {
        const int confirmationLength = 56;
        var local = localIdentity.SignedBinding.Binding;
        var expected = peer.SignedBinding.Binding;
        if (local.CircleId != expected.CircleId)
        {
            throw new RemoteChannelException("wrong_circle");
        }

        var outbound = new byte[confirmationLength];
        "BCH1"u8.CopyTo(outbound);
        BinaryPrimitives.WriteInt32BigEndian(
            outbound.AsSpan(4, 4),
            RemoteSecurityProtocol.Version);
        WriteGuid(local.CircleId, outbound.AsSpan(8, 16));
        WriteGuid(local.NodeId, outbound.AsSpan(24, 16));
        WriteGuid(expected.NodeId, outbound.AsSpan(40, 16));

        var inbound = new byte[confirmationLength];
        var write = stream.WriteAsync(outbound, cancellationToken).AsTask();
        var read = ReadExactlyAsync(stream, inbound, cancellationToken);
        await Task.WhenAll(write, read).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var expectedInbound = new byte[confirmationLength];
        "BCH1"u8.CopyTo(expectedInbound);
        BinaryPrimitives.WriteInt32BigEndian(
            expectedInbound.AsSpan(4, 4),
            RemoteSecurityProtocol.Version);
        WriteGuid(expected.CircleId, expectedInbound.AsSpan(8, 16));
        WriteGuid(expected.NodeId, expectedInbound.AsSpan(24, 16));
        WriteGuid(local.NodeId, expectedInbound.AsSpan(40, 16));
        if (!CryptographicOperations.FixedTimeEquals(inbound, expectedInbound))
        {
            throw new RemoteChannelException("authentication_failed");
        }
    }

    private static void WriteGuid(string value, Span<byte> destination)
    {
        if (!Guid.TryParseExact(value, "D", out var identifier)
            || !identifier.TryWriteBytes(destination, bigEndian: true, out var written)
            || written != 16)
        {
            throw new RemoteChannelException("authentication_failed");
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = await stream.ReadAsync(destination[offset..], cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new RemoteChannelException("authentication_failed");
            }

            offset += read;
        }
    }

    private static void EnsureNegotiatedPolicy(SslStream stream)
    {
        if (!stream.IsEncrypted
            || stream.SslProtocol != SslProtocols.Tls13
            || stream.NegotiatedApplicationProtocol
                != new SslApplicationProtocol(RemoteSecurityProtocol.Alpn))
        {
            throw new RemoteChannelException("downgraded");
        }
    }

    private static string CodeFor(NodeTransportRejectionCode code) => code switch
    {
        NodeTransportRejectionCode.UnsupportedVersion => "unsupported_version",
        NodeTransportRejectionCode.Revoked => "revoked",
        NodeTransportRejectionCode.WrongCircle => "wrong_circle",
        NodeTransportRejectionCode.WrongNode => "wrong_node",
        NodeTransportRejectionCode.Downgraded => "downgraded",
        _ => "authentication_failed",
    };

    private static bool CredentialsEqual(PublicKeyCredential left, PublicKeyCredential right) =>
        left.Role == right.Role
        && left.Algorithm == right.Algorithm
        && left.KeyId == right.KeyId
        && CryptographicOperations.FixedTimeEquals(
            left.SubjectPublicKeyInfo,
            right.SubjectPublicKeyInfo);
}
