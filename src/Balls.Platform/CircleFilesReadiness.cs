namespace Balls.Platform;

public enum CircleFilesReadinessStatus
{
    Ready,
    NotReady,
    Unknown,
}

public static class CircleFilesReadinessProviders
{
    public const string WindowsSmb311 = "windows-smb-3.1.1-v1";
}

public sealed record CircleFilesReadinessCheck(
    string Id,
    CircleFilesReadinessStatus Status,
    string Code,
    string Summary);

public sealed record CircleFilesReadinessReport(
    string Provider,
    CircleFilesReadinessStatus Status,
    IReadOnlyList<CircleFilesReadinessCheck> Checks);

public interface ICircleFilesReadinessInspector
{
    ValueTask<CircleFilesReadinessReport> InspectAsync(
        CancellationToken cancellationToken);
}

public sealed class UnsupportedCircleFilesReadinessInspector : ICircleFilesReadinessInspector
{
    public ValueTask<CircleFilesReadinessReport> InspectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new CircleFilesReadinessReport(
                CircleFilesReadinessProviders.WindowsSmb311,
                CircleFilesReadinessStatus.Unknown,
                [
                    new CircleFilesReadinessCheck(
                        "windows-platform",
                        CircleFilesReadinessStatus.Unknown,
                        "windows_required",
                        "Windows SMB readiness can be inspected only on a Windows Node."),
                ]));
    }
}
