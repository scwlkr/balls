namespace Balls.Platform;

public static class CircleFilesHostingContract
{
    public const int Version = 1;
}

public sealed record CircleFilesHostRequest(
    string CircleId,
    string ContributionId,
    string ProviderId,
    string NodeId,
    string DisplayName,
    string FolderPath,
    string AuthorizationDigest);

public sealed record CircleFilesHostPlan(
    int ContractVersion,
    string PlanId,
    string Provider,
    string FolderPath,
    string ShareName,
    string FirewallRuleName,
    string OwnershipId,
    bool TargetExists,
    IReadOnlyList<string> Actions);

public enum CircleFilesHostApplyStatus
{
    Applied,
    AlreadyApplied,
}

public sealed record CircleFilesHostApplyResult(
    CircleFilesHostApplyStatus Status,
    CircleFilesHostPlan Plan);

public interface ICircleFilesHostProvisioner
{
    ValueTask<CircleFilesHostPlan> PreviewAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesHostApplyResult> ApplyAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        CancellationToken cancellationToken);
}

public sealed class CircleFilesHostingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class UnsupportedCircleFilesHostProvisioner : ICircleFilesHostProvisioner
{
    public ValueTask<CircleFilesHostPlan> PreviewAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesHostPlan>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesHostApplyResult> ApplyAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesHostApplyResult>(Unsupported(cancellationToken));

    private static Exception Unsupported(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new CircleFilesHostingException(
            "windows_required",
            "A dedicated Windows Circle Files host is supported only on Windows.");
    }
}
