using System.Security.Cryptography;

namespace Balls.Protocol.Remote.V1;

public static class RemoteIdentity
{
    public const string Algorithm = "p256-sha256";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    public static PublicKeyCredential CreateCredential(KeyRole role, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!IsP256(key))
        {
            throw new ArgumentException("Remote v1 credentials require a P-256 key.", nameof(key));
        }

        var subjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo();
        var keyId = CreateKeyId(role, subjectPublicKeyInfo);
        return new PublicKeyCredential(role, Algorithm, keyId, subjectPublicKeyInfo);
    }

    public static byte[] Sign(ReadOnlySpan<byte> data, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!IsP256(key))
        {
            throw new ArgumentException("Remote v1 signatures require a P-256 key.", nameof(key));
        }

        return key.SignData(
            data,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static bool Verify(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature,
        PublicKeyCredential credential)
    {
        if (!IsValidCredential(credential) || signature.Length != 64)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(credential.SubjectPublicKeyInfo, out var bytesRead);
            return bytesRead == credential.SubjectPublicKeyInfo.Length
                && IsP256(key)
                && key.VerifyData(
                    data,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static bool IsValidCredential(PublicKeyCredential credential)
    {
        if (credential is null
            || credential.Algorithm != Algorithm
            || credential.SubjectPublicKeyInfo is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(credential.SubjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != credential.SubjectPublicKeyInfo.Length || !IsP256(key))
            {
                return false;
            }

            var canonicalSpki = key.ExportSubjectPublicKeyInfo();
            return CryptographicOperations.FixedTimeEquals(
                    canonicalSpki,
                    credential.SubjectPublicKeyInfo)
                && string.Equals(
                    CreateKeyId(credential.Role, canonicalSpki),
                    credential.KeyId,
                    StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string CreateKeyId(KeyRole role, ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        var roleName = role switch
        {
            KeyRole.CircleAuthority => "circle-authority",
            KeyRole.Anchor => "anchor",
            KeyRole.Member => "member",
            KeyRole.Node => "node",
            KeyRole.Transport => "transport",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        return $"{roleName}:{Algorithm}:{Base64Url(SHA256.HashData(subjectPublicKeyInfo))}";
    }

    private static bool IsP256(ECDsa key)
    {
        try
        {
            return key.KeySize == 256
                && string.Equals(
                    key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value,
                    P256Oid,
                    StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
