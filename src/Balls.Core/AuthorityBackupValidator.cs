using System.Security.Cryptography;
using System.Text.Json;

namespace Balls.Core;

public static class AuthorityBackupValidator
{
    public static AuthorityBackupValidationResult Validate(
        ReadOnlySpan<byte> envelope,
        CircleId expectedCircleId)
    {
        if (envelope.Length is 0 or > 256 * 1024)
        {
            return Rejected(AuthorityBackupRejectionCode.Malformed);
        }

        try
        {
            using var outer = JsonDocument.Parse(envelope.ToArray());
            var root = outer.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.GetProperty("format").GetString() != "balls-circle-authority-backup"
                || root.GetProperty("version").GetInt32() != 1)
            {
                return Rejected(AuthorityBackupRejectionCode.Malformed);
            }

            var manifestBytes = root.GetProperty("manifest").GetBytesFromBase64();
            var signature = root.GetProperty("manifestSignature").GetBytesFromBase64();
            var encryptedRoot = root.GetProperty("encryptedRootPrivateKeyPkcs8").GetBytesFromBase64();
            var encryptedAnchor = root.GetProperty("encryptedAnchorPrivateKeyPkcs8").GetBytesFromBase64();
            if (manifestBytes.Length is 0 or > 64 * 1024
                || signature.Length != 64
                || encryptedRoot.Length is 0 or > 64 * 1024
                || encryptedAnchor.Length is 0 or > 64 * 1024)
            {
                return Rejected(AuthorityBackupRejectionCode.Malformed);
            }

            using var manifest = JsonDocument.Parse(manifestBytes);
            var metadata = manifest.RootElement;
            if (metadata.ValueKind != JsonValueKind.Object
                || metadata.GetProperty("format").GetString()
                    != "balls-circle-authority-backup-manifest"
                || metadata.GetProperty("version").GetInt32() != 1
                || metadata.GetProperty("authorityGeneration").GetInt64() < 1
                || metadata.GetProperty("privateKeyEncoding").GetString() != "encrypted-pkcs8"
                || metadata.GetProperty("pbeEncryption").GetString() != "aes-256-cbc"
                || metadata.GetProperty("pbeKdf").GetString() != "pbkdf2-hmac-sha256"
                || metadata.GetProperty("pbeIterations").GetInt32() != 600_000
                || !DateTimeOffset.TryParse(
                    metadata.GetProperty("createdAtUtc").GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out _))
            {
                return Rejected(AuthorityBackupRejectionCode.Malformed);
            }

            var circleId = new CircleId(
                Guid.ParseExact(metadata.GetProperty("circleId").GetString()!, "D"));
            var rootCredential = ReadCredential(
                metadata.GetProperty("rootCredential"),
                IdentityKeyRole.CircleAuthority,
                "circle-authority");
            var anchorCredential = ReadCredential(
                metadata.GetProperty("anchorCredential"),
                IdentityKeyRole.Anchor,
                "anchor");
            if (rootCredential is null
                || anchorCredential is null
                || !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(encryptedRoot),
                    metadata.GetProperty("encryptedRootSha256").GetBytesFromBase64())
                || !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(encryptedAnchor),
                    metadata.GetProperty("encryptedAnchorSha256").GetBytesFromBase64()))
            {
                return Rejected(AuthorityBackupRejectionCode.Forged);
            }

            if (!IdentityCryptography.Verify(manifestBytes, signature, rootCredential))
            {
                return Rejected(AuthorityBackupRejectionCode.Forged);
            }

            if (circleId != expectedCircleId)
            {
                return Rejected(AuthorityBackupRejectionCode.WrongCircle);
            }

            return AuthorityBackupValidationResult.Valid(rootCredential.KeyId);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            FormatException or
            InvalidOperationException or
            JsonException or
            OverflowException)
        {
            return Rejected(AuthorityBackupRejectionCode.Malformed);
        }
    }

    private static PublicIdentityCredential? ReadCredential(
        JsonElement element,
        IdentityKeyRole expectedRole,
        string expectedRoleName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || element.GetProperty("role").GetString() != expectedRoleName)
        {
            return null;
        }

        var credential = new PublicIdentityCredential(
            expectedRole,
            element.GetProperty("algorithm").GetString()!,
            element.GetProperty("keyId").GetString()!,
            element.GetProperty("subjectPublicKeyInfo").GetBytesFromBase64());
        return IdentityCryptography.IsValidCredential(credential) ? credential : null;
    }

    private static AuthorityBackupValidationResult Rejected(AuthorityBackupRejectionCode code) =>
        AuthorityBackupValidationResult.Rejected(code);
}
