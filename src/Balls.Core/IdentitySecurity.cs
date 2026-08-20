using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Balls.Core;

public enum IdentityKeyRole
{
    CircleAuthority,
    Anchor,
    Member,
    Node,
    Transport,
}

public sealed record PublicIdentityCredential(
    IdentityKeyRole Role,
    string Algorithm,
    string KeyId,
    byte[] SubjectPublicKeyInfo);

public sealed record NodeCryptographicIdentity(
    NodeId NodeId,
    PublicIdentityCredential Credential);

public sealed record CircleAuthorityIdentity(
    CircleId CircleId,
    long AuthorityGeneration,
    PublicIdentityCredential RootCredential,
    PublicIdentityCredential AnchorCredential);

public interface IPrivateMaterialProtector
{
    string Scheme { get; }

    byte[] Protect(ReadOnlySpan<byte> privateMaterial);

    byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial);
}

public interface IIdentityAuthorityStore
{
    Task<NodeCryptographicIdentity?> GetNodeCryptographicIdentityAsync(
        CancellationToken cancellationToken = default);

    Task<CircleAuthorityIdentity?> GetCircleAuthorityAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<byte[]> SignWithNodeAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<byte[]> SignWithCircleAuthorityAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<byte[]> SignWithCircleAnchorAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<AuthorityBackupEnvelope> ExportCircleAuthorityAsync(
        CircleId circleId,
        ReadOnlyMemory<char> passphrase,
        CancellationToken cancellationToken = default);
}

public sealed class AuthorityBackupEnvelope
{
    private readonly byte[] content;

    public AuthorityBackupEnvelope(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length is 0 or > 256 * 1024)
        {
            throw new ArgumentException(
                "An authority backup envelope must be non-empty and bounded.",
                nameof(content));
        }

        this.content = content.ToArray();
    }

    [JsonPropertyName("format")]
    public string Format => "balls-circle-authority-backup";

    [JsonPropertyName("version")]
    public int Version => 1;

    public void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(content);
    }

    public override string ToString() => "Balls Circle authority backup v1 (sensitive)";
}

public enum AuthorityBackupRejectionCode
{
    None,
    Malformed,
    Forged,
    WrongCircle,
}

public sealed record AuthorityBackupValidationResult(
    bool IsValid,
    AuthorityBackupRejectionCode RejectionCode,
    string? RootKeyId)
{
    public static AuthorityBackupValidationResult Valid(string rootKeyId) =>
        new(true, AuthorityBackupRejectionCode.None, rootKeyId);

    public static AuthorityBackupValidationResult Rejected(AuthorityBackupRejectionCode code) =>
        new(false, code, null);
}

public static class IdentityCryptography
{
    public const string Algorithm = "p256-sha256";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    public static PublicIdentityCredential CreateCredential(IdentityKeyRole role, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!IsP256(key))
        {
            throw new ArgumentException("Identity v1 requires a P-256 key.", nameof(key));
        }

        var subjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo();
        return new PublicIdentityCredential(
            role,
            Algorithm,
            CreateKeyId(role, subjectPublicKeyInfo),
            subjectPublicKeyInfo);
    }

    public static byte[] Sign(ReadOnlySpan<byte> data, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!IsP256(key))
        {
            throw new ArgumentException("Identity v1 requires a P-256 key.", nameof(key));
        }

        return key.SignData(
            data,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static bool Verify(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature,
        PublicIdentityCredential credential)
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

    public static bool IsValidCredential(PublicIdentityCredential credential)
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

            var canonical = key.ExportSubjectPublicKeyInfo();
            return CryptographicOperations.FixedTimeEquals(
                    canonical,
                    credential.SubjectPublicKeyInfo)
                && string.Equals(
                    CreateKeyId(credential.Role, canonical),
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

    private static string CreateKeyId(IdentityKeyRole role, ReadOnlySpan<byte> spki)
    {
        var roleName = role switch
        {
            IdentityKeyRole.CircleAuthority => "circle-authority",
            IdentityKeyRole.Anchor => "anchor",
            IdentityKeyRole.Member => "member",
            IdentityKeyRole.Node => "node",
            IdentityKeyRole.Transport => "transport",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        return $"{roleName}:{Algorithm}:{Base64Url(SHA256.HashData(spki))}";
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
