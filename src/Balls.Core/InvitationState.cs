namespace Balls.Core;

public readonly record struct InvitationId(Guid Value)
{
    public static InvitationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct RedemptionId(Guid Value)
{
    public static RedemptionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

public sealed record LocalTransportIdentity(
    NodeId NodeId,
    PublicIdentityCredential Credential);

public sealed record PersistedCircleInvitation(
    InvitationId InvitationId,
    CircleId CircleId,
    byte[] PackageSha256,
    byte[] EncodedPackage,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);

public enum InvitationRedemptionStatus
{
    Accepted,
    Replayed,
    Revoked,
    Expired,
}

public sealed record InvitationRedemptionResult(
    InvitationRedemptionStatus Status,
    RedemptionId? RedemptionId);

public interface IInvitationStateStore
{
    Task StoreCircleInvitationAsync(
        PersistedCircleInvitation invitation,
        CancellationToken cancellationToken = default);

    Task<PersistedCircleInvitation?> GetCircleInvitationAsync(
        InvitationId invitationId,
        CancellationToken cancellationToken = default);

    Task<InvitationRedemptionResult> RedeemCircleInvitationAsync(
        InvitationId invitationId,
        ReadOnlyMemory<byte> packageSha256,
        RedemptionId redemptionId,
        DateTimeOffset redeemedAtUtc,
        CancellationToken cancellationToken = default);

    Task RevokeCircleInvitationAsync(
        InvitationId invitationId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken = default);
}
