using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal static class WindowsCircleFilesHostAuthorizationVerifier
{
    private const string Algorithm = "p256-sha256";
    private const string ContributionDomain = "balls-circle-files-contribution-create-v1";
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Validate(CircleFilesHostRequest request)
    {
        var proof = request.Authorization;
        if (proof is null
            || proof.Transcript is not { Length: > 0 and <= 2048 }
            || proof.MemberSignature is not { Length: 64 }
            || proof.CircleAuthoritySignature is not { Length: 64 }
            || !MatchesDigest(request.AuthorizationDigest, proof)
            || !MatchesContribution(request, proof.Transcript)
            || !VerifyCredential(
                "member",
                proof.MemberCredential,
                proof.Transcript,
                proof.MemberSignature)
            || !VerifyCredential(
                "circle-authority",
                proof.CircleAuthorityCredential,
                proof.Transcript,
                proof.CircleAuthoritySignature))
        {
            throw InvalidAuthorization();
        }
    }

    private static bool MatchesDigest(
        string expectedDigest,
        CircleFilesHostAuthorizationProof proof)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, proof.Transcript);
        Append(hash, proof.MemberSignature);
        Append(hash, proof.CircleAuthoritySignature);
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        return expectedDigest.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedDigest),
                Encoding.ASCII.GetBytes(actual));
    }

    private static void Append(IncrementalHash hash, byte[] value)
    {
        hash.AppendData(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value.Length)));
        hash.AppendData(value);
    }

    private static bool VerifyCredential(
        string expectedRole,
        CircleFilesHostPublicCredential credential,
        byte[] transcript,
        byte[] signature)
    {
        if (credential is null
            || credential.Role != expectedRole
            || credential.Algorithm != Algorithm
            || credential.SubjectPublicKeyInfo is not { Length: > 0 and <= 256 })
        {
            return false;
        }

        var keyId = $"{expectedRole}:{Algorithm}:{Base64Url(SHA256.HashData(credential.SubjectPublicKeyInfo))}";
        if (credential.KeyId != keyId)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(credential.SubjectPublicKeyInfo, out var bytesRead);
            return bytesRead == credential.SubjectPublicKeyInfo.Length
                && key.KeySize == 256
                && key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value == P256Oid
                && key.VerifyData(
                    transcript,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static bool MatchesContribution(CircleFilesHostRequest request, byte[] transcript)
    {
        try
        {
            ReadOnlySpan<byte> remaining = transcript;
            return ReadString(ref remaining) == ContributionDomain
                && ReadGuid(ref remaining) != Guid.Empty
                && ReadGuid(ref remaining).ToString("D") == request.ContributionId
                && ReadGuid(ref remaining).ToString("D") == request.CircleId
                && ReadGuid(ref remaining).ToString("D") == request.ProviderId
                && ReadGuid(ref remaining).ToString("D") == request.NodeId
                && ReadString(ref remaining) == request.DisplayName
                && ReadInt32(ref remaining) == 1
                && ReadInt64(ref remaining) == 1
                && ReadGuid(ref remaining) != Guid.Empty
                && ReadInt64(ref remaining) > 0
                && ReadInt64(ref remaining) > 0
                && remaining.IsEmpty;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static string ReadString(ref ReadOnlySpan<byte> remaining)
    {
        var length = ReadInt32(ref remaining);
        if (length is < 0 or > 256 || remaining.Length < length)
        {
            throw new InvalidDataException();
        }

        var result = StrictUtf8.GetString(remaining[..length]);
        remaining = remaining[length..];
        return result;
    }

    private static Guid ReadGuid(ref ReadOnlySpan<byte> remaining)
    {
        const int length = 16;
        if (remaining.Length < length)
        {
            throw new InvalidDataException();
        }

        var result = new Guid(remaining[..length], bigEndian: true);
        remaining = remaining[length..];
        return result;
    }

    private static int ReadInt32(ref ReadOnlySpan<byte> remaining)
    {
        const int length = sizeof(int);
        if (remaining.Length < length)
        {
            throw new InvalidDataException();
        }

        var result = BinaryPrimitives.ReadInt32BigEndian(remaining[..length]);
        remaining = remaining[length..];
        return result;
    }

    private static long ReadInt64(ref ReadOnlySpan<byte> remaining)
    {
        const int length = sizeof(long);
        if (remaining.Length < length)
        {
            throw new InvalidDataException();
        }

        var result = BinaryPrimitives.ReadInt64BigEndian(remaining[..length]);
        remaining = remaining[length..];
        return result;
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static CircleFilesHostingException InvalidAuthorization() =>
        new(
            "hosting_authorization_invalid",
            "The contribution authorization binding is invalid.");
}
