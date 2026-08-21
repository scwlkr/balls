using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class AuthenticatedChannelSpikeTests
{
    [TestMethod]
    public void Admission_bootstrap_rejects_a_server_not_pinned_by_the_invitation()
    {
        using var expectedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var presentedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var presentedCertificate = CreateCertificate(
            "anchor.balls",
            presentedKey,
            isServer: true);
        var expected = RemoteIdentity.CreateCredential(KeyRole.Transport, expectedKey);
        var options = RemoteTls.CreateAdmissionClientOptions("anchor.balls", expected.KeyId);

        var accepted = options.RemoteCertificateValidationCallback!(
            this,
            presentedCertificate,
            null,
            SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.IsFalse(accepted);
        Assert.AreEqual(0, options.ClientCertificates!.Count);
    }

    [TestMethod]
    public async Task Tls_13_mtls_binds_both_transport_credentials_and_alpn()
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive(
                ".NET 10 supports TLS 1.3 on macOS clients, but not macOS SslStream servers.");
            return;
        }

        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var serverCertificate = CreateCertificate("anchor.balls", serverKey, isServer: true);
        using var clientCertificate = CreateCertificate("node.balls", clientKey, isServer: false);
        var serverCredential = RemoteIdentity.CreateCredential(KeyRole.Transport, serverKey);
        var clientCredential = RemoteIdentity.CreateCredential(KeyRole.Transport, clientKey);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunServerAsync(listener, serverCertificate, clientCredential);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var clientTls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await clientTls.AuthenticateAsClientAsync(
                RemoteTls.CreateClientOptions(
                    "anchor.balls",
                    clientCertificate,
                    serverCredential));
        }
        catch
        {
            await serverTask;
            throw;
        }
        var received = new byte[1];
        await clientTls.ReadExactlyAsync(received);
        var serverProtocol = await serverTask;

        Assert.AreEqual(0x2a, received[0]);
        Assert.AreEqual(SslProtocols.Tls13, clientTls.SslProtocol);
        Assert.AreEqual(SslProtocols.Tls13, serverProtocol);
        Assert.AreEqual(
            new SslApplicationProtocol(Encoding.ASCII.GetBytes(RemoteSecurityProtocol.Alpn)),
            clientTls.NegotiatedApplicationProtocol);
    }

    private static async Task<SslProtocols> RunServerAsync(
        TcpListener listener,
        X509Certificate2 serverCertificate,
        PublicKeyCredential clientCredential)
    {
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var serverTls = new SslStream(accepted.GetStream(), leaveInnerStreamOpen: false);
        await serverTls.AuthenticateAsServerAsync(
            RemoteTls.CreateServerOptions(serverCertificate, clientCredential));
        await serverTls.WriteAsync(new byte[] { 0x2a });
        return serverTls.SslProtocol;
    }

    private static X509Certificate2 CreateCertificate(
        string dnsName,
        ECDsa key,
        bool isServer)
    {
        var request = new CertificateRequest(
            $"CN={dnsName}",
            key,
            HashAlgorithmName.SHA256);
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
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }
}
