using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal static class WindowsCircleFilesGrantAuthorizationVerifier
{
    private const string GrantDomain = "balls-circle-files-access-grant-create-v1";
    private const string ContributionDomain = "balls-circle-files-contribution-create-v1";
    private const string GrantRevocationDomain = "balls-circle-files-access-grant-revoke-v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Validate(CircleFilesGrantCredentialRequest request)
    {
        WindowsCircleFilesHostAuthorizationVerifier.Validate(request.Host);
        var host = ReadContribution(request.Host.Authorization!.Transcript);
        var grant = ReadGrant(request.Authorization.Transcript);
        if (grant.GrantId != request.GrantId
            || grant.CircleId != request.Host.CircleId
            || grant.ContributionId != request.Host.ContributionId
            || grant.MemberId != request.MemberId
            || grant.Access != request.Access
            || grant.Generation != request.Generation
            || grant.OwnerMemberId != host.OwnerMemberId
            || grant.AuthorityGeneration != host.AuthorityGeneration
            || !CredentialsEqual(request.Authorization.MemberCredential, request.Host.Authorization.MemberCredential)
            || !CredentialsEqual(request.Authorization.CircleAuthorityCredential, request.Host.Authorization.CircleAuthorityCredential)
            || !Verify(request.Authorization.MemberCredential, request.Authorization.Transcript, request.Authorization.MemberSignature)
            || !Verify(request.Authorization.CircleAuthorityCredential, request.Authorization.Transcript, request.Authorization.CircleAuthoritySignature))
        {
            throw Invalid();
        }
    }

    internal static void ValidateCleanup(CircleFilesGrantCleanupRequest request)
    {
        Validate(request.Grant);
        var revocation = ReadRevocation(request.Revocation.Authorization.Transcript);
        if (request.Revocation.RequestId != revocation.RequestId
            || request.Revocation.CircleId != request.Grant.Host.CircleId
            || request.Revocation.CircleId != revocation.CircleId
            || request.Revocation.ContributionId != request.Grant.Host.ContributionId
            || request.Revocation.ContributionId != revocation.ContributionId
            || request.Revocation.GrantId != request.Grant.GrantId
            || request.Revocation.GrantId != revocation.GrantId
            || request.Revocation.RevokedGeneration != request.Grant.Generation
            || request.Revocation.RevokedGeneration != revocation.Generation
            || !string.Equals(
                request.Revocation.AuthorizationDigest,
                CircleFilesHostAuthorizationDigest.Compute(request.Revocation.Authorization),
                StringComparison.Ordinal)
            || !CredentialsEqual(
                request.Revocation.Authorization.MemberCredential,
                request.Grant.Authorization.MemberCredential)
            || !CredentialsEqual(
                request.Revocation.Authorization.CircleAuthorityCredential,
                request.Grant.Authorization.CircleAuthorityCredential)
            || !Verify(
                request.Revocation.Authorization.MemberCredential,
                request.Revocation.Authorization.Transcript,
                request.Revocation.Authorization.MemberSignature)
            || !Verify(
                request.Revocation.Authorization.CircleAuthorityCredential,
                request.Revocation.Authorization.Transcript,
                request.Revocation.Authorization.CircleAuthoritySignature))
        {
            throw Invalid();
        }
    }

    private static bool CredentialsEqual(
        CircleFilesHostPublicCredential left,
        CircleFilesHostPublicCredential right) =>
        left.Role == right.Role
        && left.Algorithm == right.Algorithm
        && left.KeyId == right.KeyId
        && CryptographicOperations.FixedTimeEquals(left.SubjectPublicKeyInfo, right.SubjectPublicKeyInfo);

    private static bool Verify(
        CircleFilesHostPublicCredential credential,
        byte[] transcript,
        byte[] signature)
    {
        if (credential.Algorithm != "p256-sha256" || signature.Length != 64)
        {
            return false;
        }
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(credential.SubjectPublicKeyInfo, out var read);
            return read == credential.SubjectPublicKeyInfo.Length
                && key.VerifyData(
                    transcript,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static (string OwnerMemberId, long AuthorityGeneration) ReadContribution(byte[] transcript)
    {
        try
        {
            ReadOnlySpan<byte> remaining = transcript;
            if (ReadString(ref remaining) != ContributionDomain)
            {
                throw new InvalidDataException();
            }
            _ = ReadGuid(ref remaining);
            _ = ReadGuid(ref remaining);
            _ = ReadGuid(ref remaining);
            _ = ReadGuid(ref remaining);
            _ = ReadGuid(ref remaining);
            _ = ReadString(ref remaining);
            _ = ReadInt32(ref remaining);
            _ = ReadInt64(ref remaining);
            var owner = ReadGuid(ref remaining);
            var generation = ReadInt64(ref remaining);
            _ = ReadInt64(ref remaining);
            if (!remaining.IsEmpty)
            {
                throw new InvalidDataException();
            }
            return (owner.ToString("D"), generation);
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static (string GrantId, string CircleId, string ContributionId, string MemberId,
        string Access, long Generation, string OwnerMemberId, long AuthorityGeneration)
        ReadGrant(byte[] transcript)
    {
        try
        {
            ReadOnlySpan<byte> remaining = transcript;
            if (ReadString(ref remaining) != GrantDomain)
            {
                throw new InvalidDataException();
            }
            _ = ReadGuid(ref remaining);
            var grant = ReadGuid(ref remaining);
            var circle = ReadGuid(ref remaining);
            var contribution = ReadGuid(ref remaining);
            var member = ReadGuid(ref remaining);
            var access = ReadInt32(ref remaining) switch
            {
                1 => "read-only",
                2 => "read-write",
                _ => throw new InvalidDataException(),
            };
            if (ReadInt32(ref remaining) != 1)
            {
                throw new InvalidDataException();
            }
            var generation = ReadInt64(ref remaining);
            var owner = ReadGuid(ref remaining);
            var authorityGeneration = ReadInt64(ref remaining);
            _ = ReadInt64(ref remaining);
            if (grant == Guid.Empty || member == Guid.Empty || generation <= 0 || !remaining.IsEmpty)
            {
                throw new InvalidDataException();
            }
            return (
                grant.ToString("D"), circle.ToString("D"), contribution.ToString("D"),
                member.ToString("D"), access, generation, owner.ToString("D"), authorityGeneration);
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static (string RequestId, string CircleId, string ContributionId, string GrantId,
        long Generation) ReadRevocation(byte[] transcript)
    {
        try
        {
            ReadOnlySpan<byte> remaining = transcript;
            if (ReadString(ref remaining) != GrantRevocationDomain)
            {
                throw new InvalidDataException();
            }
            var request = ReadGuid(ref remaining);
            var circle = ReadGuid(ref remaining);
            var contribution = ReadGuid(ref remaining);
            var grant = ReadGuid(ref remaining);
            var generation = ReadInt64(ref remaining);
            _ = ReadInt64(ref remaining);
            _ = ReadGuid(ref remaining);
            _ = ReadInt64(ref remaining);
            _ = ReadInt64(ref remaining);
            if (request == Guid.Empty
                || circle == Guid.Empty
                || contribution == Guid.Empty
                || grant == Guid.Empty
                || generation <= 0
                || !remaining.IsEmpty)
            {
                throw new InvalidDataException();
            }
            return (
                request.ToString("D"),
                circle.ToString("D"),
                contribution.ToString("D"),
                grant.ToString("D"),
                generation);
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static string ReadString(ref ReadOnlySpan<byte> remaining)
    {
        var length = ReadInt32(ref remaining);
        if (length is < 0 or > 512 || remaining.Length < length)
        {
            throw new InvalidDataException();
        }
        var value = StrictUtf8.GetString(remaining[..length]);
        remaining = remaining[length..];
        return value;
    }

    private static Guid ReadGuid(ref ReadOnlySpan<byte> remaining)
    {
        if (remaining.Length < 16)
        {
            throw new InvalidDataException();
        }
        var value = new Guid(remaining[..16], bigEndian: true);
        remaining = remaining[16..];
        return value;
    }

    private static int ReadInt32(ref ReadOnlySpan<byte> remaining)
    {
        if (remaining.Length < 4)
        {
            throw new InvalidDataException();
        }
        var value = BinaryPrimitives.ReadInt32BigEndian(remaining[..4]);
        remaining = remaining[4..];
        return value;
    }

    private static long ReadInt64(ref ReadOnlySpan<byte> remaining)
    {
        if (remaining.Length < 8)
        {
            throw new InvalidDataException();
        }
        var value = BinaryPrimitives.ReadInt64BigEndian(remaining[..8]);
        remaining = remaining[8..];
        return value;
    }

    private static CircleFilesHostingException Invalid() => new(
        "grant_authorization_invalid",
        "The Member Access Grant authorization binding is invalid.");
}
