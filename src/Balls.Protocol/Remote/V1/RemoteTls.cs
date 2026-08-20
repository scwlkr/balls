using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Balls.Protocol.Remote.V1;

public static class RemoteTls
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    public static SslClientAuthenticationOptions CreateAdmissionClientOptions(
        string targetHost,
        string expectedServerTransportKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);
        if (!expectedServerTransportKeyId.StartsWith(
                "transport:p256-sha256:",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Admission requires a remote v1 transport-key pin.",
                nameof(expectedServerTransportKeyId));
        }

        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = new X509CertificateCollection(),
            EnabledSslProtocols = SslProtocols.Tls13,
            ApplicationProtocols = [ProtocolApplication],
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                ValidateCertificate(
                    certificate,
                    expectedServerTransportKeyId,
                    expectedSubjectPublicKeyInfo: null,
                    ServerAuthenticationOid,
                    targetHost),
        };
    }

    public static SslClientAuthenticationOptions CreateClientOptions(
        string targetHost,
        X509Certificate2? clientCertificate,
        PublicKeyCredential expectedServerTransportCredential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);
        EnsureTransportCredential(expectedServerTransportCredential);
        var clientCertificates = new X509CertificateCollection();
        if (clientCertificate is not null)
        {
            clientCertificates.Add(clientCertificate);
        }

        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = clientCertificates,
            EnabledSslProtocols = SslProtocols.Tls13,
            ApplicationProtocols = [ProtocolApplication],
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                ValidateCertificate(
                    certificate,
                    expectedServerTransportCredential.KeyId,
                    expectedServerTransportCredential.SubjectPublicKeyInfo,
                    ServerAuthenticationOid,
                    targetHost),
        };
    }

    public static SslServerAuthenticationOptions CreateServerOptions(
        X509Certificate2 serverCertificate,
        PublicKeyCredential expectedClientTransportCredential)
    {
        ArgumentNullException.ThrowIfNull(serverCertificate);
        EnsureTransportCredential(expectedClientTransportCredential);
        return new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCertificate,
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls13,
            ApplicationProtocols = [ProtocolApplication],
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                ValidateCertificate(
                    certificate,
                    expectedClientTransportCredential.KeyId,
                    expectedClientTransportCredential.SubjectPublicKeyInfo,
                    ClientAuthenticationOid,
                    expectedDnsName: null),
        };
    }

    private static SslApplicationProtocol ProtocolApplication =>
        new(Encoding.ASCII.GetBytes(RemoteSecurityProtocol.Alpn));

    private static bool ValidateCertificate(
        X509Certificate? certificate,
        string expectedKeyId,
        byte[]? expectedSubjectPublicKeyInfo,
        string requiredEnhancedKeyUsage,
        string? expectedDnsName)
    {
        if (certificate is null)
        {
            return false;
        }

        using var certificate2 = new X509Certificate2(certificate);
        var now = DateTime.UtcNow;
        if (now < certificate2.NotBefore.ToUniversalTime()
            || now >= certificate2.NotAfter.ToUniversalTime()
            || !HasEnhancedKeyUsage(certificate2, requiredEnhancedKeyUsage)
            || !HasDigitalSignatureUsage(certificate2)
            || (expectedDnsName is not null
                && !string.Equals(
                    certificate2.GetNameInfo(X509NameType.DnsName, forIssuer: false),
                    expectedDnsName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            using var publicKey = certificate2.GetECDsaPublicKey();
            if (publicKey is null)
            {
                return false;
            }

            var actual = RemoteIdentity.CreateCredential(KeyRole.Transport, publicKey);
            return string.Equals(actual.KeyId, expectedKeyId, StringComparison.Ordinal)
                && (expectedSubjectPublicKeyInfo is null
                    || CryptographicOperations.FixedTimeEquals(
                        actual.SubjectPublicKeyInfo,
                        expectedSubjectPublicKeyInfo));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool HasEnhancedKeyUsage(X509Certificate2 certificate, string requiredOid)
    {
        var enhancedKeyUsage = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SingleOrDefault();
        return enhancedKeyUsage is not null
            && enhancedKeyUsage.EnhancedKeyUsages
                .Cast<Oid>()
                .Any(oid => string.Equals(oid.Value, requiredOid, StringComparison.Ordinal));
    }

    private static bool HasDigitalSignatureUsage(X509Certificate2 certificate)
    {
        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        return keyUsage is not null
            && (keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0;
    }

    private static void EnsureTransportCredential(PublicKeyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.Role != KeyRole.Transport || !RemoteIdentity.IsValidCredential(credential))
        {
            throw new ArgumentException(
                "TLS peer binding requires a valid remote v1 transport credential.",
                nameof(credential));
        }
    }
}
