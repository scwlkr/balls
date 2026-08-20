using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class RemoteAuthenticatedChannelTests
{
    [TestMethod]
    public async Task Tls13_channel_binds_Circle_Node_transport_and_rejects_replayed_frames()
    {
        using var fixture = new ChannelFixture();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = AcceptChannelAsync(listener, fixture);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var clientChannel = await RemoteAuthenticatedChannel.ConnectAsync(
            new UntrustedRemoteConnection(
                client.GetStream(),
                "test-lan",
                client.Client.RemoteEndPoint!.ToString()!),
            "anchor.balls",
            fixture.ClientIdentity,
            fixture.ServerExpectation,
            fixture.Limits);
        await using var serverChannel = await serverTask;

        Assert.IsTrue(clientChannel.IsEncrypted);
        Assert.IsTrue(serverChannel.IsEncrypted);
        Assert.AreEqual(ChannelFixture.CircleId, clientChannel.CircleId);
        Assert.AreEqual(ChannelFixture.ServerNodeId, clientChannel.PeerNodeId);
        Assert.AreEqual(ChannelFixture.ClientNodeId, serverChannel.PeerNodeId);
        Assert.AreEqual(RemoteSecurityProtocol.Version, clientChannel.NegotiatedProtocolVersion);

        var operationId = Guid.Parse("0198c837-5000-7000-8000-000000000030");
        var frame = new RemoteFrame(operationId, "authenticated"u8.ToArray());
        await clientChannel.WriteAsync(frame);
        var received = await serverChannel.ReadAsync();
        CollectionAssert.AreEqual(frame.Payload, received.Payload);

        await clientChannel.WriteAsync(frame);
        var replay = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => serverChannel.ReadAsync());
        Assert.AreEqual("replayed", replay.Code);
    }

    [TestMethod]
    public async Task Unknown_transport_certificate_fails_closed_during_mutual_authentication()
    {
        using var fixture = new ChannelFixture();
        using var unknownKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var unknownCertificate = CreateCertificate("unknown.balls", unknownKey, isServer: false);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = AcceptChannelAsync(listener, fixture);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => RemoteAuthenticatedChannel.ConnectAsync(
                new UntrustedRemoteConnection(
                    client.GetStream(),
                    "test-lan",
                    client.Client.RemoteEndPoint!.ToString()!),
                "anchor.balls",
                fixture.CreateUnknownIdentity(unknownKey, unknownCertificate),
                fixture.ServerExpectation,
                fixture.Limits));
        var serverError = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => serverTask);
        Assert.AreEqual("authentication_failed", serverError.Code);
    }

    [TestMethod]
    public async Task Silent_and_interrupted_peers_are_bounded_without_weakening_cancellation()
    {
        using var fixture = new ChannelFixture();
        var limits = fixture.Limits with { HandshakeTimeout = TimeSpan.FromMilliseconds(100) };

        using (var silentListener = new TcpListener(IPAddress.Loopback, 0))
        {
            silentListener.Start(1);
            var port = ((IPEndPoint)silentListener.LocalEndpoint).Port;
            var silentServer = Task.Run(async () =>
            {
                using var accepted = await silentListener.AcceptTcpClientAsync();
                await Task.Delay(500);
            });
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var timeout = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
                () => RemoteAuthenticatedChannel.ConnectAsync(
                    new UntrustedRemoteConnection(
                        client.GetStream(),
                        "test-lan",
                        client.Client.RemoteEndPoint!.ToString()!),
                    "anchor.balls",
                    fixture.ClientIdentity,
                    fixture.ServerExpectation,
                    limits));
            Assert.AreEqual("timeout", timeout.Code);
            await silentServer;
        }

        using var interruptedListener = new TcpListener(IPAddress.Loopback, 0);
        interruptedListener.Start(1);
        var interruptedPort = ((IPEndPoint)interruptedListener.LocalEndpoint).Port;
        var interruptedServer = Task.Run(async () =>
        {
            using var accepted = await interruptedListener.AcceptTcpClientAsync();
        });
        using var interruptedClient = new TcpClient();
        await interruptedClient.ConnectAsync(IPAddress.Loopback, interruptedPort);
        var interrupted = await Assert.ThrowsExactlyAsync<RemoteChannelException>(
            () => RemoteAuthenticatedChannel.ConnectAsync(
                new UntrustedRemoteConnection(
                    interruptedClient.GetStream(),
                    "test-lan",
                    interruptedClient.Client.RemoteEndPoint!.ToString()!),
                "anchor.balls",
                fixture.ClientIdentity,
                fixture.ServerExpectation,
                fixture.Limits));
        Assert.AreEqual("authentication_failed", interrupted.Code);
        await interruptedServer;
    }

    private static async Task<RemoteAuthenticatedChannel> AcceptChannelAsync(
        TcpListener listener,
        ChannelFixture fixture)
    {
        var accepted = await listener.AcceptTcpClientAsync();
        return await RemoteAuthenticatedChannel.AcceptAsync(
            new UntrustedRemoteConnection(
                accepted.GetStream(),
                "test-lan",
                accepted.Client.RemoteEndPoint!.ToString()!),
            fixture.ServerIdentity,
            [fixture.ClientExpectation],
            fixture.Limits);
    }

    private static X509Certificate2 CreateCertificate(
        string dnsName,
        ECDsa key,
        bool isServer)
    {
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new(isServer ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2"),
                },
                critical: true));
        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(10));
        return X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    private sealed class ChannelFixture : IDisposable
    {
        private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        internal ChannelFixture()
        {
            var root = RemoteIdentity.CreateCredential(KeyRole.CircleAuthority, rootKey);
            var now = DateTimeOffset.UtcNow;
            var server = CreateBinding(ServerNodeId, serverKey, root, now);
            var client = CreateBinding(ClientNodeId, clientKey, root, now);
            ServerExpectation = CreateExpectation(server, root, now);
            ClientExpectation = CreateExpectation(client, root, now);
            ServerCertificate = CreateCertificate("anchor.balls", serverKey, isServer: true);
            ClientCertificate = CreateCertificate("node.balls", clientKey, isServer: false);
            ServerIdentity = new RemoteChannelIdentity(ServerCertificate, ServerExpectation);
            ClientIdentity = new RemoteChannelIdentity(ClientCertificate, ClientExpectation);
        }

        internal const string CircleId = "0198c837-5000-7000-8000-000000000010";
        internal const string ServerNodeId = "0198c837-5000-7000-8000-000000000011";
        internal const string ClientNodeId = "0198c837-5000-7000-8000-000000000012";

        internal RemotePeerExpectation ServerExpectation { get; }
        internal RemotePeerExpectation ClientExpectation { get; }
        internal X509Certificate2 ServerCertificate { get; }
        internal X509Certificate2 ClientCertificate { get; }
        internal RemoteChannelIdentity ServerIdentity { get; }
        internal RemoteChannelIdentity ClientIdentity { get; }
        internal RemoteChannelLimits Limits { get; } = new(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            1024,
            8);

        public void Dispose()
        {
            ClientCertificate.Dispose();
            ServerCertificate.Dispose();
            clientKey.Dispose();
            serverKey.Dispose();
            rootKey.Dispose();
        }

        internal RemoteChannelIdentity CreateUnknownIdentity(
            ECDsa key,
            X509Certificate2 certificate)
        {
            var root = ServerExpectation.SignedBinding.AuthorityCredential;
            var now = DateTimeOffset.UtcNow;
            var binding = CreateBinding(
                "0198c837-5000-7000-8000-000000000099",
                key,
                root,
                now);
            return new RemoteChannelIdentity(
                certificate,
                CreateExpectation(binding, root, now));
        }

        private SignedNodeTransportBinding CreateBinding(
            string nodeId,
            ECDsa key,
            PublicKeyCredential root,
            DateTimeOffset now)
        {
            var binding = new NodeTransportBinding(
                RemoteSecurityProtocol.Version,
                CircleId,
                nodeId,
                1,
                RemoteIdentity.CreateCredential(KeyRole.Transport, key),
                DateTimeOffset.FromUnixTimeSeconds(now.AddMinutes(-1).ToUnixTimeSeconds()),
                DateTimeOffset.FromUnixTimeSeconds(now.AddHours(1).ToUnixTimeSeconds()),
                RemoteSecurityProtocol.Version,
                RemoteSecurityProtocol.Version);
            return NodeTransportSecurity.Sign(binding, root, rootKey);
        }

        private static RemotePeerExpectation CreateExpectation(
            SignedNodeTransportBinding binding,
            PublicKeyCredential root,
            DateTimeOffset now) =>
            new(
                binding,
                new NodeTransportVerificationContext(
                    CircleId,
                    binding.Binding.NodeId,
                    root,
                    DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()),
                    1,
                    RemoteSecurityProtocol.Version,
                    RemoteSecurityProtocol.Version,
                    new HashSet<string>(StringComparer.Ordinal)));
    }
}
