namespace Balls.Core;

public sealed record CircleMessage(
    MessageId Id,
    CircleId CircleId,
    long Sequence,
    MemberId AuthorMemberId,
    NodeId AuthorNodeId,
    string Text,
    DateTimeOffset AuthoredAtUtc,
    DateTimeOffset AcceptedAtUtc);

public sealed record MessageDraft(
    MessageId Id,
    CircleId CircleId,
    MemberId AuthorMemberId,
    NodeId AuthorNodeId,
    PublicIdentityCredential MemberCredential,
    PublicIdentityCredential NodeCredential,
    string Text,
    DateTimeOffset AuthoredAtUtc);

public enum MessageCommitStatus
{
    Accepted,
    IdempotentRetry,
    Conflict,
}

public sealed record MessageCommitResult(
    MessageCommitStatus Status,
    CircleMessage? Message,
    byte[]? EncodedResponse);

public sealed record AuthoritativeMessageCommit(
    CircleMessage Message,
    byte[] RequestSha256,
    byte[] EncodedResponse);

public interface IMessageStateStore
{
    Task<MessageDraft> PrepareMessageDraftAsync(
        CircleId circleId,
        MessageId messageId,
        string text,
        DateTimeOffset authoredAtUtc,
        CancellationToken cancellationToken = default);

    Task<byte[]> SignMessageDraftWithMemberAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task<PublicIdentityCredential?> GetCircleMemberCredentialAsync(
        CircleId circleId,
        MemberId memberId,
        CancellationToken cancellationToken = default);

    Task<MessageCommitResult> CommitAuthoritativeMessageAsync(
        AuthoritativeMessageCommit commit,
        CancellationToken cancellationToken = default);

    Task<MessageCommitResult> CommitReplicatedMessageAsync(
        AuthoritativeMessageCommit commit,
        CancellationToken cancellationToken = default);

    Task<CircleMessage?> GetMessageAsync(
        CircleId circleId,
        MessageId messageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CircleMessage>> ListMessagesAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);
}

public sealed class CircleMessageApplication(IMessageStateStore store)
{
    public Task<IReadOnlyList<CircleMessage>> ListAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default)
    {
        return store.ListMessagesAsync(circleId, cancellationToken);
    }
}
