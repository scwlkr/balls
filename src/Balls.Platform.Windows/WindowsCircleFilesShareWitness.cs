using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal static class WindowsCircleFilesShareWitness
{
    internal const int MaximumBytes = 4096;

    private const int ContractVersion = 1;
    private const string Domain = "balls-circle-files-share-witness-v1";

    internal static string GetFileName(string grantId, long generation)
    {
        if (!Guid.TryParseExact(grantId, "D", out var parsed)
            || parsed == Guid.Empty
            || parsed.ToString("D") != grantId
            || generation <= 0)
        {
            throw new ArgumentException("The share witness identity is invalid.");
        }

        return $".balls-witness-{grantId}-g{generation.ToString(CultureInfo.InvariantCulture)}-v1.json";
    }

    internal static byte[] CreateForGrant(WindowsCircleFilesGrantHelperPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Create(
            new WitnessIdentity(
                plan.Request.Host.CircleId,
                plan.Request.Host.ContributionId,
                plan.Request.Host.ProviderId,
                plan.Request.GrantId,
                plan.Request.MemberId,
                plan.PublicPlan.AccountName,
                plan.PublicPlan.OwnershipId,
                plan.Request.Access,
                plan.Request.Generation),
            plan.Secret);
    }

    internal static byte[] CreateForMapping(
        CircleFilesMemberMappingRequest request,
        ReadOnlySpan<byte> secret)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Create(ToIdentity(request), secret);
    }

    internal static bool IsValid(
        ReadOnlySpan<byte> content,
        CircleFilesMemberMappingRequest request,
        ReadOnlySpan<byte> secret)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (content.Length is <= 0 or > MaximumBytes
            || secret.Length is < 24 or > 128)
        {
            return false;
        }

        try
        {
            var expected = CreateForMapping(request, secret);
            try
            {
                return CryptographicOperations.FixedTimeEquals(content, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expected);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] Create(WitnessIdentity identity, ReadOnlySpan<byte> secret)
    {
        if (secret.Length is < 24 or > 128)
        {
            throw new ArgumentException("The share witness credential is invalid.", nameof(secret));
        }

        _ = GetFileName(identity.GrantId, identity.Generation);
        var mac = ComputeMac(identity, secret);
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("contractVersion", ContractVersion);
                writer.WriteString("circleId", identity.CircleId);
                writer.WriteString("contributionId", identity.ContributionId);
                writer.WriteString("providerId", identity.ProviderId);
                writer.WriteString("grantId", identity.GrantId);
                writer.WriteString("memberId", identity.MemberId);
                writer.WriteString("accountName", identity.AccountName);
                writer.WriteString("grantOwnershipId", identity.GrantOwnershipId);
                writer.WriteString("access", identity.Access);
                writer.WriteNumber("generation", identity.Generation);
                writer.WriteString("mac", Convert.ToHexStringLower(mac));
                writer.WriteEndObject();
            }

            if (buffer.WrittenCount > MaximumBytes)
            {
                throw new InvalidDataException("The share witness exceeds its size limit.");
            }

            return buffer.WrittenSpan.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mac);
        }
    }

    private static byte[] ComputeMac(WitnessIdentity identity, ReadOnlySpan<byte> secret)
    {
        using var canonical = new MemoryStream();
        WriteField(canonical, Domain);
        WriteField(canonical, ContractVersion.ToString(CultureInfo.InvariantCulture));
        WriteField(canonical, identity.CircleId);
        WriteField(canonical, identity.ContributionId);
        WriteField(canonical, identity.ProviderId);
        WriteField(canonical, identity.GrantId);
        WriteField(canonical, identity.MemberId);
        WriteField(canonical, identity.AccountName);
        WriteField(canonical, identity.GrantOwnershipId);
        WriteField(canonical, identity.Access);
        WriteField(canonical, identity.Generation.ToString(CultureInfo.InvariantCulture));
        return HMACSHA256.HashData(secret, canonical.GetBuffer().AsSpan(0, (int)canonical.Length));
    }

    private static void WriteField(Stream destination, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var encoded = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, encoded.Length);
        destination.Write(length);
        destination.Write(encoded);
    }

    private static WitnessIdentity ToIdentity(CircleFilesMemberMappingRequest request) =>
        new(
            request.CircleId,
            request.ContributionId,
            request.ProviderId,
            request.GrantId,
            request.MemberId,
            request.AccountName,
            request.GrantOwnershipId,
            request.Access,
            request.Generation);

    private sealed record WitnessIdentity(
        string CircleId,
        string ContributionId,
        string ProviderId,
        string GrantId,
        string MemberId,
        string AccountName,
        string GrantOwnershipId,
        string Access,
        long Generation);
}
