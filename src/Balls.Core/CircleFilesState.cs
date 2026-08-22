using System.Buffers.Binary;
using System.Text;

namespace Balls.Core;

public readonly record struct CircleFilesContributionRequestId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct CircleFilesContributionId(Guid Value)
{
    public static CircleFilesContributionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct CircleFilesProviderId(Guid Value)
{
    public static CircleFilesProviderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct MemberAccessGrantRequestId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct MemberAccessGrantId(Guid Value)
{
    public static MemberAccessGrantId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

public enum CircleFilesContributionLifecycle
{
    Defined = 1,
    Active = 2,
    Retired = 3,
}

public enum MemberAccessMode
{
    ReadOnly = 1,
    ReadWrite = 2,
}

public enum MemberAccessGrantLifecycle
{
    Defined = 1,
    Active = 2,
    Revoked = 3,
}

public sealed record CircleFilesProviderIdentity(
    CircleFilesProviderId Id,
    NodeId NodeId);

public sealed record CircleFilesOwnerAuthorization(
    MemberId OwnerMemberId,
    long AuthorityGeneration,
    DateTimeOffset AuthorizedAtUtc,
    byte[] Transcript,
    byte[] MemberSignature,
    byte[] CircleAuthoritySignature);

public sealed record CircleFilesContribution(
    CircleFilesContributionId Id,
    CircleId CircleId,
    CircleFilesProviderIdentity Provider,
    string DisplayName,
    CircleFilesContributionLifecycle Lifecycle,
    long Generation,
    DateTimeOffset CreatedAtUtc,
    CircleFilesOwnerAuthorization Authorization);

public sealed record AuthorizedCircleFilesContribution(
    CircleFilesContribution Contribution,
    PublicIdentityCredential MemberCredential,
    PublicIdentityCredential CircleAuthorityCredential);

public sealed record AuthorizedMemberAccessGrant(
    MemberAccessGrant Grant,
    CircleFilesContribution Contribution,
    PublicIdentityCredential OwnerMemberCredential,
    PublicIdentityCredential CircleAuthorityCredential);

public sealed record MemberAccessGrant(
    MemberAccessGrantId Id,
    CircleId CircleId,
    CircleFilesContributionId ContributionId,
    MemberId MemberId,
    MemberAccessMode Access,
    MemberAccessGrantLifecycle Lifecycle,
    long Generation,
    DateTimeOffset CreatedAtUtc,
    CircleFilesOwnerAuthorization Authorization);

public sealed record CircleFilesAuthorizationContext(
    CircleId CircleId,
    MemberId MemberId,
    MemberRole MemberRole,
    PublicIdentityCredential MemberCredential,
    NodeId NodeId,
    long AuthorityGeneration,
    PublicIdentityCredential RootCredential);

public sealed record CreateCircleFilesContributionCommand(
    CircleFilesContributionRequestId RequestId,
    CircleId CircleId,
    string? DisplayName);

public sealed record CreateMemberAccessGrantCommand(
    MemberAccessGrantRequestId RequestId,
    CircleId CircleId,
    CircleFilesContributionId ContributionId,
    MemberId MemberId,
    MemberAccessMode Access);

public interface ICircleFilesStateStore
{
    Task<CircleFilesAuthorizationContext?> GetAuthorizationContextAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<byte[]> SignWithLocalMemberAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<CircleFilesContribution> CreateContributionAsync(
        CircleFilesContributionRequestId requestId,
        CircleFilesContribution contribution,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CircleFilesContribution>> ListContributionsAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<MemberAccessGrant> CreateAccessGrantAsync(
        MemberAccessGrantRequestId requestId,
        MemberAccessGrant grant,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberAccessGrant>> ListAccessGrantsAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        CancellationToken cancellationToken = default);
}

public static class CircleFilesAuthorizationTranscript
{
    private const string ContributionDomain = "balls-circle-files-contribution-create-v1";
    private const string GrantDomain = "balls-circle-files-access-grant-create-v1";

    public static byte[] EncodeContribution(
        CircleFilesContributionRequestId requestId,
        CircleFilesContribution contribution)
    {
        using var output = new MemoryStream();
        WriteString(output, ContributionDomain);
        WriteGuid(output, requestId.Value);
        WriteGuid(output, contribution.Id.Value);
        WriteGuid(output, contribution.CircleId.Value);
        WriteGuid(output, contribution.Provider.Id.Value);
        WriteGuid(output, contribution.Provider.NodeId.Value);
        WriteString(output, contribution.DisplayName);
        WriteInt32(output, (int)contribution.Lifecycle);
        WriteInt64(output, contribution.Generation);
        WriteGuid(output, contribution.Authorization.OwnerMemberId.Value);
        WriteInt64(output, contribution.Authorization.AuthorityGeneration);
        WriteInt64(output, contribution.Authorization.AuthorizedAtUtc.ToUnixTimeSeconds());
        return output.ToArray();
    }

    public static byte[] EncodeGrant(
        MemberAccessGrantRequestId requestId,
        MemberAccessGrant grant)
    {
        using var output = new MemoryStream();
        WriteString(output, GrantDomain);
        WriteGuid(output, requestId.Value);
        WriteGuid(output, grant.Id.Value);
        WriteGuid(output, grant.CircleId.Value);
        WriteGuid(output, grant.ContributionId.Value);
        WriteGuid(output, grant.MemberId.Value);
        WriteInt32(output, (int)grant.Access);
        WriteInt32(output, (int)grant.Lifecycle);
        WriteInt64(output, grant.Generation);
        WriteGuid(output, grant.Authorization.OwnerMemberId.Value);
        WriteInt64(output, grant.Authorization.AuthorityGeneration);
        WriteInt64(output, grant.Authorization.AuthorizedAtUtc.ToUnixTimeSeconds());
        return output.ToArray();
    }

    private static void WriteGuid(Stream output, Guid value)
    {
        Span<byte> encoded = stackalloc byte[16];
        if (!value.TryWriteBytes(encoded, bigEndian: true, out var written) || written != encoded.Length)
        {
            throw new InvalidOperationException("A Circle Files identifier could not be encoded.");
        }

        output.Write(encoded);
    }

    private static void WriteString(Stream output, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        WriteInt32(output, encoded.Length);
        output.Write(encoded);
    }

    private static void WriteInt32(Stream output, int value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(encoded, value);
        output.Write(encoded);
    }

    private static void WriteInt64(Stream output, long value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(encoded, value);
        output.Write(encoded);
    }
}
