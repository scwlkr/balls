namespace Balls.Platform;

public enum CircleFilesCleanupStatus
{
    Removed = 1,
    AlreadyRemoved = 2,
    Busy = 3,
    Partial = 4,
}

public sealed record CircleFilesCleanupExecution(
    CircleFilesCleanupStatus Status,
    int OpenSessionCount);

public static class CircleFilesLifecycleContract
{
    public const int Version = 1;
}

public sealed record CircleFilesGrantRevocationProof(
    string RequestId,
    string CircleId,
    string ContributionId,
    string GrantId,
    long RevokedGeneration,
    string AuthorizationDigest,
    CircleFilesHostAuthorizationProof Authorization);

public sealed record CircleFilesGrantCleanupRequest(
    CircleFilesGrantCredentialRequest Grant,
    CircleFilesGrantRevocationProof Revocation);

public sealed record CircleFilesGrantCleanupPlan(
    int ContractVersion,
    string PlanId,
    string Provider,
    string FolderPath,
    string ShareName,
    string AccountName,
    string OwnershipId,
    long Generation,
    IReadOnlyList<string> Actions);

public sealed record CircleFilesGrantCleanupResult(
    CircleFilesCleanupStatus Status,
    int OpenSessionCount,
    CircleFilesGrantCleanupPlan Plan);

public sealed record CircleFilesHostRemovalPlan(
    int ContractVersion,
    string PlanId,
    string Provider,
    string FolderPath,
    string ShareName,
    string FirewallRuleName,
    string OwnershipId,
    IReadOnlyList<string> Actions);

public sealed record CircleFilesHostRemovalResult(
    CircleFilesCleanupStatus Status,
    int OpenSessionCount,
    CircleFilesHostRemovalPlan Plan);

public interface ICircleFilesLifecycleManager
{
    ValueTask<CircleFilesGrantCleanupPlan> PreviewGrantCleanupAsync(
        CircleFilesGrantCleanupRequest request,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesGrantCleanupResult> RemoveGrantAsync(
        CircleFilesGrantCleanupRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        bool terminateOpenSessions,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesHostRemovalPlan> PreviewHostRemovalAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesHostRemovalResult> RemoveHostAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        bool terminateOpenSessions,
        CancellationToken cancellationToken);
}

public sealed class UnsupportedCircleFilesLifecycleManager : ICircleFilesLifecycleManager
{
    public ValueTask<CircleFilesGrantCleanupPlan> PreviewGrantCleanupAsync(
        CircleFilesGrantCleanupRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesGrantCleanupPlan>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesGrantCleanupResult> RemoveGrantAsync(
        CircleFilesGrantCleanupRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        bool terminateOpenSessions,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesGrantCleanupResult>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesHostRemovalPlan> PreviewHostRemovalAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesHostRemovalPlan>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesHostRemovalResult> RemoveHostAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        bool terminateOpenSessions,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesHostRemovalResult>(Unsupported(cancellationToken));

    private static Exception Unsupported(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new CircleFilesHostingException(
            "windows_required",
            "Circle Files lifecycle cleanup is available only on Windows.");
    }
}
