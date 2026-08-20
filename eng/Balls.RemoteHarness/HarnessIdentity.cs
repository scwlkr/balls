using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Balls.Protocol.Remote.V1;

namespace Balls.RemoteHarness;

internal static class HarnessIdentity
{
    internal static (HarnessConfiguration Server, HarnessConfiguration Client) CreatePair()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var serverCertificate = CreateCertificate("server.balls", serverKey);
        using var clientCertificate = CreateCertificate("client.balls", clientKey);
        var root = RemoteIdentity.CreateCredential(KeyRole.CircleAuthority, rootKey);
        var circleId = Guid.CreateVersion7().ToString("D");
        var serverBinding = CreateBinding(
            circleId,
            Guid.CreateVersion7().ToString("D"),
            serverKey,
            root,
            rootKey);
        var clientBinding = CreateBinding(
            circleId,
            Guid.CreateVersion7().ToString("D"),
            clientKey,
            root,
            rootKey);
        var serverPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var clientPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        return (
            new HarnessConfiguration(
                "server.balls",
                "client.balls",
                Convert.ToBase64String(serverCertificate.Export(
                    X509ContentType.Pkcs12,
                    serverPassword)),
                serverPassword,
                serverBinding,
                clientBinding,
                root),
            new HarnessConfiguration(
                "client.balls",
                "server.balls",
                Convert.ToBase64String(clientCertificate.Export(
                    X509ContentType.Pkcs12,
                    clientPassword)),
                clientPassword,
                clientBinding,
                serverBinding,
                root));
    }

    internal static X509Certificate2 LoadCertificate(HarnessConfiguration configuration)
    {
        var encoded = Convert.FromBase64String(configuration.Pkcs12Base64);
        if (encoded.Length is 0 or > 128 * 1024)
        {
            throw new InvalidDataException("The harness certificate is outside its bounds.");
        }

        return X509CertificateLoader.LoadPkcs12(
            encoded,
            configuration.Pkcs12Password,
            OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable
                : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    internal static RemoteChannelIdentity CreateLocalIdentity(
        HarnessConfiguration configuration,
        X509Certificate2 certificate) =>
        new(certificate, CreateExpectation(configuration.LocalBinding, configuration));

    internal static RemotePeerExpectation CreatePeerExpectation(
        HarnessConfiguration configuration) =>
        CreateExpectation(configuration.PeerBinding, configuration);

    private static RemotePeerExpectation CreateExpectation(
        SignedNodeTransportBinding binding,
        HarnessConfiguration configuration) =>
        new(
            binding,
            new NodeTransportVerificationContext(
                binding.Binding.CircleId,
                binding.Binding.NodeId,
                configuration.TrustedRootCredential,
                DateTimeOffset.UtcNow,
                binding.Binding.AuthorityGeneration,
                RemoteSecurityProtocol.Version,
                RemoteSecurityProtocol.Version,
                new HashSet<string>(StringComparer.Ordinal)));

    private static SignedNodeTransportBinding CreateBinding(
        string circleId,
        string nodeId,
        ECDsa transportKey,
        PublicKeyCredential root,
        ECDsa rootKey)
    {
        var now = DateTimeOffset.UtcNow;
        return NodeTransportSecurity.Sign(
            new NodeTransportBinding(
                RemoteSecurityProtocol.Version,
                circleId,
                nodeId,
                1,
                RemoteIdentity.CreateCredential(KeyRole.Transport, transportKey),
                DateTimeOffset.FromUnixTimeSeconds(now.AddMinutes(-5).ToUnixTimeSeconds()),
                DateTimeOffset.FromUnixTimeSeconds(now.AddHours(2).ToUnixTimeSeconds()),
                RemoteSecurityProtocol.Version,
                RemoteSecurityProtocol.Version),
            root,
            rootKey);
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
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(2));
    }
}
