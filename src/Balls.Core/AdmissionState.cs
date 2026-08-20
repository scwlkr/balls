namespace Balls.Core;

public sealed record AdmissionApplicantState(
    InvitationId InvitationId,
    CircleId CircleId,
    MemberId MemberId,
    string MemberDisplayName,
    PublicIdentityCredential MemberCredential,
    byte[] ApplicantChallenge,
    byte[] PackageSha256,
    bool IsCompleted,
    byte[] EncodedResponse);

public sealed record CircleTrustState(
    CircleId CircleId,
    long AuthorityGeneration,
    long AuthoritySequence,
    NodeId IssuerNodeId,
    PublicIdentityCredential RootCredential,
    PublicIdentityCredential AnchorCredential,
    byte[] SignedAdmissionReceipt);

public sealed record CircleNodeSecurityState(
    CircleId CircleId,
    NodeId NodeId,
    PublicIdentityCredential NodeCredential,
    PublicIdentityCredential TransportCredential,
    byte[] SignedTransportBinding);

public enum AnchorAdmissionCommitStatus
{
    Accepted,
    IdempotentRetry,
    Replayed,
    Revoked,
    Expired,
}

public sealed record AnchorAdmissionCommitResult(
    AnchorAdmissionCommitStatus Status,
    byte[]? EncodedResponse);

public sealed record AnchorAdmissionCommit(
    InvitationId InvitationId,
    CircleId CircleId,
    byte[] PackageSha256,
    byte[] RequestSha256,
    byte[] EncodedResponse,
    Member Member,
    CircleNode Node,
    PublicIdentityCredential MemberCredential,
    PublicIdentityCredential NodeCredential,
    PublicIdentityCredential TransportCredential,
    byte[] SignedTransportBinding,
    long AuthoritySequence,
    DateTimeOffset AdmittedAtUtc);

public sealed record JoinedCircleCommit(
    InvitationId InvitationId,
    byte[] PackageSha256,
    CircleDetails Circle,
    CircleTrustState Trust,
    PublicIdentityCredential LocalMemberCredential,
    IReadOnlyList<CircleNodeSecurityState> NodeSecurity,
    DateTimeOffset JoinedAtUtc);

public interface IAdmissionStateStore
{
    Task<AdmissionApplicantState> PrepareAdmissionApplicantAsync(
        InvitationId invitationId,
        CircleId circleId,
        ReadOnlyMemory<byte> packageSha256,
        string memberDisplayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);

    Task<byte[]> SignWithAdmissionMemberAsync(
        InvitationId invitationId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<byte[]> GetOrCreateAdmissionChallengeAsync(
        InvitationId invitationId,
        CancellationToken cancellationToken = default);

    Task<CircleTrustState?> GetCircleTrustAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task StoreCircleNodeSecurityAsync(
        CircleNodeSecurityState state,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CircleNodeSecurityState>> ListCircleNodeSecurityAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<long> ReserveAuthoritySequenceAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<AnchorAdmissionCommitResult?> GetAnchorAdmissionResultAsync(
        InvitationId invitationId,
        ReadOnlyMemory<byte> requestSha256,
        CancellationToken cancellationToken = default);

    Task<AnchorAdmissionCommitResult> CommitAnchorAdmissionAsync(
        AnchorAdmissionCommit commit,
        CancellationToken cancellationToken = default);

    Task CommitJoinedCircleAsync(
        JoinedCircleCommit commit,
        CancellationToken cancellationToken = default);

    Task RecordAdmissionAuditAsync(
        CircleId circleId,
        string outcome,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);
}
