namespace Balls.Core;

public readonly record struct CircleMessageId(Guid Value)
{
    public static CircleMessageId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

public sealed record LocalCircleMessageAuthor(
    CircleId CircleId,
    MemberId MemberId,
    PublicIdentityCredential MemberCredential,
    NodeId NodeId);

public sealed record CircleMessageAuthorState(
    CircleId CircleId,
    MemberId MemberId,
    PublicIdentityCredential MemberCredential,
    NodeId NodeId,
    PublicIdentityCredential NodeCredential,
    bool IsAuthorized);

public sealed record PreparedOutgoingCircleMessage(
    CircleMessageId Id,
    CircleId CircleId,
    MemberId AuthorMemberId,
    NodeId AuthorNodeId,
    string Text,
    DateTimeOffset AuthoredAtUtc);

public sealed record PersistedCircleMessage(
    CircleMessageId Id,
    CircleId CircleId,
    MemberId AuthorMemberId,
    NodeId AuthorNodeId,
    string Text,
    DateTimeOffset AuthoredAtUtc,
    long Sequence,
    DateTimeOffset AcceptedAtUtc);

public sealed record CircleMessageCommit(
    PersistedCircleMessage Message,
    byte[] RequestSha256,
    byte[] EncodedSignedMessage,
    byte[] EncodedReceipt);

public enum CircleMessageCommitStatus
{
    Accepted,
    IdempotentRetry,
    Conflict,
}

public sealed record CircleMessageCommitResult(
    CircleMessageCommitStatus Status,
    PersistedCircleMessage? Message,
    byte[]? EncodedReceipt);

public interface ICircleMessageStateStore
{
    Task<PreparedOutgoingCircleMessage> PrepareOutgoingCircleMessageAsync(
        CircleMessageId messageId,
        CircleId circleId,
        string text,
        DateTimeOffset authoredAtUtc,
        CancellationToken cancellationToken = default);

    Task<LocalCircleMessageAuthor?> GetLocalCircleMessageAuthorAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<CircleMessageAuthorState?> GetCircleMessageAuthorAsync(
        CircleId circleId,
        MemberId memberId,
        NodeId nodeId,
        CancellationToken cancellationToken = default);

    Task<byte[]> SignWithLocalCircleMemberAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<long> GetNextCircleMessageSequenceAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<CircleMessageCommitResult> CommitCircleMessageAsync(
        CircleMessageCommit commit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersistedCircleMessage>> ListCircleMessagesAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);
}
