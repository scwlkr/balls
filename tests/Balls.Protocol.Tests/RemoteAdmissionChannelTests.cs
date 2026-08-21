using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("ProcessIntegration")]
public sealed class RemoteAdmissionChannelTests
{
    [TestMethod]
    public async Task Invitation_pinned_TLS_proves_the_proposed_transport_key_before_frames()
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive(
                ".NET 10 supports TLS 1.3 on macOS clients, but not macOS SslStream servers.");
            return;
        }

        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var serverCertificate = CreateCertificate("anchor.balls", serverKey);
        using var clientCertificate = CreateCertificate("applicant.balls", clientKey);
        var expectedServer = RemoteIdentity.CreateCredential(KeyRole.Transport, serverKey);
        var expectedClient = RemoteIdentity.CreateCredential(KeyRole.Transport, clientKey);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var serverTask = AcceptAsync(listener, serverCertificate);
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        await using var connection = new UntrustedRemoteConnection(
            client.GetStream(),
            "test",
            endpoint.ToString());
        await using var channel = await RemoteAdmissionChannel.ConnectAsync(
            connection,
            "anchor.balls",
            expectedServer.KeyId,
            clientCertificate);
        await channel.WriteAsync(new RemoteFrame(Guid.CreateVersion7(), "hello"u8.ToArray()));
        await using var accepted = await serverTask;
        var frame = await accepted.ReadAsync();

        Assert.IsTrue(channel.IsEncrypted);
        Assert.IsTrue(accepted.IsEncrypted);
        Assert.AreEqual(expectedServer.KeyId, channel.PeerTransportCredential.KeyId);
        Assert.AreEqual(expectedClient.KeyId, accepted.PeerTransportCredential.KeyId);
        CollectionAssert.AreEqual("hello"u8.ToArray(), frame.Payload);
    }

    [TestMethod]
    public void Wire_codec_rejects_noncanonical_or_wrong_kind_payloads()
    {
        var challenge = new AdmissionChallenge(RandomNumberGenerator.GetBytes(32));
        var encoded = AdmissionWireCodec.EncodeChallenge(challenge);
        var decoded = AdmissionWireCodec.DecodeChallenge(encoded);
        var noncanonical = encoded.Concat(" "u8.ToArray()).ToArray();

        CollectionAssert.AreEqual(challenge.AnchorChallenge, decoded.AnchorChallenge);
        Assert.ThrowsExactly<RemoteChannelException>(
            () => AdmissionWireCodec.DecodeChallenge(noncanonical));
        Assert.ThrowsExactly<RemoteChannelException>(
            () => AdmissionWireCodec.DecodeRejection(encoded));
    }

    private static async Task<RemoteAdmissionChannel> AcceptAsync(
        TcpListener listener,
        X509Certificate2 certificate)
    {
        var client = await listener.AcceptTcpClientAsync();
        return await RemoteAdmissionChannel.AcceptAsync(
            new UntrustedRemoteConnection(
                client.GetStream(),
                "test",
                client.Client.RemoteEndPoint!.ToString()!),
            certificate);
    }

    private static X509Certificate2 CreateCertificate(string dnsName, ECDsa key)
    {
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new("1.3.6.1.5.5.7.3.1"),
                    new("1.3.6.1.5.5.7.3.2"),
                },
                critical: true));
        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pkcs12),
            password: null,
            OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable
                : OperatingSystem.IsMacOS()
                    ? X509KeyStorageFlags.Exportable
                    : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }
}
