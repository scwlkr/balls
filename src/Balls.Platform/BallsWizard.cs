namespace Balls.Platform;

public enum BallsWizardSupportStatus
{
    Supported,
    Unsupported,
    Blocked,
}

public enum BallsWizardInstallationStatus
{
    Absent,
    Partial,
    Installed,
}

public sealed record BallsWizardArtifact(
    string Id,
    string DisplayName,
    string Version,
    Uri Source,
    long SizeBytes,
    string Sha256,
    string License);

public sealed record BallsWizardSystemContext(
    string OperatingSystem,
    string OperatingSystemArchitecture,
    string ProcessArchitecture,
    string Cpu,
    IReadOnlyList<string> Gpus,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    long FreeStorageBytes);

public sealed record BallsWizardInspection(
    BallsWizardSupportStatus Support,
    BallsWizardInstallationStatus Installation,
    string Code,
    string Message,
    string WizardVersion,
    long DownloadedBytes,
    long RequiredStorageBytes,
    BallsWizardSystemContext SystemContext,
    IReadOnlyList<BallsWizardArtifact> Artifacts);

public sealed record BallsWizardInstallProgress(
    string ArtifactId,
    string Stage,
    long DownloadedBytes,
    long TotalBytes);

public sealed record BallsWizardChatMessage(string Role, string Content);

public sealed class BallsWizardIntegrityException(string message) : Exception(message);

public interface IBallsWizardPlatform : IAsyncDisposable
{
    Task<BallsWizardInspection> InspectAsync(
        string wizardDirectory,
        CancellationToken cancellationToken);

    Task InstallAsync(
        string wizardDirectory,
        IProgress<BallsWizardInstallProgress> progress,
        CancellationToken cancellationToken);

    Task<string> CompleteAsync(
        string wizardDirectory,
        string systemPrompt,
        IReadOnlyList<BallsWizardChatMessage> messages,
        CancellationToken cancellationToken);

    Task RemoveAsync(string wizardDirectory, CancellationToken cancellationToken);
}

public sealed class UnsupportedBallsWizardPlatform(string platformName) : IBallsWizardPlatform
{
    private static readonly IReadOnlyList<BallsWizardArtifact> NoArtifacts = [];

    public Task<BallsWizardInspection> InspectAsync(
        string wizardDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new BallsWizardInspection(
                BallsWizardSupportStatus.Unsupported,
                BallsWizardInstallationStatus.Absent,
                "windows_11_x64_required",
                $"Balls Wizard v0 needs Windows 11 x64; this Node is {platformName}.",
                "wizard-v0",
                0,
                0,
                new BallsWizardSystemContext(
                    platformName,
                    System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    "Unavailable",
                    [],
                    0,
                    0,
                    0),
                NoArtifacts));
    }

    public Task InstallAsync(
        string wizardDirectory,
        IProgress<BallsWizardInstallProgress> progress,
        CancellationToken cancellationToken)
    {
        throw new PlatformNotSupportedException("Balls Wizard v0 needs Windows 11 x64.");
    }

    public Task<string> CompleteAsync(
        string wizardDirectory,
        string systemPrompt,
        IReadOnlyList<BallsWizardChatMessage> messages,
        CancellationToken cancellationToken)
    {
        throw new PlatformNotSupportedException("Balls Wizard v0 needs Windows 11 x64.");
    }

    public Task RemoveAsync(string wizardDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
