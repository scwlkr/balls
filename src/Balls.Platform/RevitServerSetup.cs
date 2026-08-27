namespace Balls.Platform;

public enum RevitServerSetupMutationStatus
{
    Applied,
    AlreadyApplied,
    RestartRequired,
}

public sealed record RevitServerSetupPreparationRequest(
    string MediaPath,
    string PlanDigest);

public sealed record RevitServerSetupPreparationResult(
    RevitServerSetupMutationStatus Status,
    string Summary);

public sealed class RevitServerSetupException : Exception
{
    public RevitServerSetupException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public interface IRevitServerSetupOperator
{
    ValueTask<RevitServerSetupPreparationResult> PrepareAsync(
        RevitServerSetupPreparationRequest request,
        CancellationToken cancellationToken);

    ValueTask LaunchAutodeskAsync(string mediaPath, CancellationToken cancellationToken);
}

public enum RevitServerHealthStatus
{
    Healthy,
    Incomplete,
    Blocked,
}

public sealed record RevitServerHealthCheck(
    string Id,
    RevitServerHealthStatus Status,
    string Code,
    string Summary);

public sealed record RevitServerHealthReport(
    RevitServerHealthStatus Status,
    string Summary,
    IReadOnlyList<RevitServerHealthCheck> Checks);

public interface IRevitServerHealthInspector
{
    ValueTask<RevitServerHealthReport> InspectAsync(CancellationToken cancellationToken);
}

public sealed class UnsupportedRevitServerSetupOperator : IRevitServerSetupOperator
{
    public ValueTask<RevitServerSetupPreparationResult> PrepareAsync(
        RevitServerSetupPreparationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Revit Server setup is available only on Windows Server 2022.");
    }

    public ValueTask LaunchAutodeskAsync(string mediaPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Autodesk setup is available only on Windows Server 2022.");
    }
}

public sealed class UnsupportedRevitServerHealthInspector : IRevitServerHealthInspector
{
    public ValueTask<RevitServerHealthReport> InspectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RevitServerHealthReport(
            RevitServerHealthStatus.Blocked,
            "Revit Server health can be verified only on Windows Server 2022.",
            [new RevitServerHealthCheck(
                "windows-server",
                RevitServerHealthStatus.Blocked,
                "windows_server_2022_required",
                "Open this setup on Windows Server 2022 Desktop Experience.")]));
    }
}
