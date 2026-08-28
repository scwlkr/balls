using Balls.Platform;
using Balls.Protocol.Browser.V1;

namespace Balls.Daemon;

internal sealed class BallsWizardApplication : IAsyncDisposable
{
    private const int MaxMessages = 12;
    private const int MaxMessageCharacters = 2_000;
    private const int MaxConversationCharacters = 12_000;
    private readonly object stateLock = new();
    private readonly IBallsWizardPlatform platform;
    private readonly WizardKnowledge? knowledge;
    private readonly string wizardDirectory;
    private readonly string productVersion;
    private readonly Dictionary<string, long> progressByArtifact = new(StringComparer.Ordinal);
    private CancellationTokenSource? installCancellation;
    private Task? installTask;
    private string stage = "idle";
    private string? failureCode;
    private string? failureMessage;
    private int disposed;

    public BallsWizardApplication(
        IBallsWizardPlatform platform,
        WizardKnowledge? knowledge,
        string dataDirectory,
        string productVersion)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        this.platform = platform;
        this.knowledge = knowledge;
        wizardDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "wizard");
        this.productVersion = productVersion;
    }

    public async Task<BrowserBallsWizardStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (knowledge is null)
        {
            return UnavailableStatus(
                "wizard_guide_unavailable",
                "The packaged Wizard Guide is unavailable. Your Balls workspace is still available.");
        }
        try
        {
            var inspection = await platform.InspectAsync(wizardDirectory, cancellationToken)
                .ConfigureAwait(false);
            string currentStage;
            string? currentFailureCode;
            string? currentFailureMessage;
            long progressBytes;
            bool installing;
            lock (stateLock)
            {
                installing = installTask is { IsCompleted: false };
                currentStage = stage;
                currentFailureCode = failureCode;
                currentFailureMessage = failureMessage;
                progressBytes = progressByArtifact.Values.Sum();
            }

            var downloaded = Math.Max(inspection.DownloadedBytes, progressBytes);
            var statusStage = installing
                ? currentStage
                : currentFailureCode is not null
                    ? "failed"
                    : inspection.Installation == BallsWizardInstallationStatus.Installed
                        ? "ready"
                        : inspection.Installation == BallsWizardInstallationStatus.Partial
                            ? "paused"
                            : "idle";
            var message = currentFailureMessage ?? inspection.Message;
            var code = currentFailureCode ?? inspection.Code;
            return MapStatus(inspection, statusStage, code, message, downloaded, installing);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return UnavailableStatus(
                "wizard_inspection_failed",
                "The local Wizard could not inspect this Node. Your Balls workspace is still available.");
        }
    }

    public async Task<BrowserBallsWizardStatusResponse> StartInstallAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (knowledge is null)
        {
            return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        var inspection = await platform.InspectAsync(wizardDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (inspection.Support != BallsWizardSupportStatus.Supported
            || inspection.Installation == BallsWizardInstallationStatus.Installed)
        {
            return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (stateLock)
        {
            if (installTask is { IsCompleted: false })
            {
                return MapStatus(
                    inspection,
                    stage,
                    inspection.Code,
                    inspection.Message,
                    Math.Max(inspection.DownloadedBytes, progressByArtifact.Values.Sum()),
                    installing: true);
            }

            installCancellation?.Dispose();
            installCancellation = new CancellationTokenSource();
            progressByArtifact.Clear();
            failureCode = null;
            failureMessage = null;
            stage = "starting";
            var progress = new Progress<BallsWizardInstallProgress>(UpdateProgress);
            installTask = RunInstallAsync(progress, installCancellation.Token);
        }

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrowserBallsWizardStatusResponse> CancelInstallAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task? task;
        lock (stateLock)
        {
            installCancellation?.Cancel();
            stage = "cancelling";
            task = installTask;
        }

        if (task is not null)
        {
            try
            {
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrowserBallsWizardStatusResponse> RemoveAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await CancelInstallAsync(cancellationToken).ConfigureAwait(false);
        await platform.RemoveAsync(wizardDirectory, cancellationToken).ConfigureAwait(false);
        lock (stateLock)
        {
            progressByArtifact.Clear();
            failureCode = null;
            failureMessage = null;
            stage = "idle";
        }
        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrowserBallsWizardChatResponse> ChatAsync(
        BrowserBallsWizardChatRequest request,
        string localRole,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (knowledge is null)
        {
            throw new BallsWizardApplicationException(
                "wizard_guide_unavailable",
                "The packaged Wizard Guide is unavailable. Balls is still ready for ordinary work.");
        }
        var messages = ValidateMessages(request.Messages);
        localRole = localRole switch
        {
            "owner" => "owner",
            "member" => "member",
            "none" => "none",
            _ => throw new BallsWizardApplicationException(
                "wizard_role_invalid",
                "Wizard context requires owner, member, or none."),
        };
        var inspection = await platform.InspectAsync(wizardDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (inspection.Support != BallsWizardSupportStatus.Supported
            || inspection.Installation != BallsWizardInstallationStatus.Installed)
        {
            throw new BallsWizardApplicationException(
                "wizard_not_ready",
                "Install Balls Wizard before asking it a question.");
        }

        var latestQuestion = messages.Last(message => message.Role == "user").Content;
        var selected = knowledge.Select(latestQuestion);
        var systemPrompt = WizardPromptBuilder.Build(
            productVersion,
            inspection.WizardVersion,
            localRole,
            inspection.SystemContext,
            selected);
        try
        {
            var answer = await platform.CompleteAsync(
                wizardDirectory,
                systemPrompt,
                messages,
                cancellationToken).ConfigureAwait(false);
            return new BrowserBallsWizardChatResponse(
                answer,
                selected.Select(
                        section => new BrowserBallsWizardSourceResponse(section.Id, section.Title))
                    .ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BallsWizardIntegrityException)
        {
            lock (stateLock)
            {
                stage = "failed";
                failureCode = "wizard_integrity_failed";
                failureMessage =
                    "A Wizard artifact failed verification and was not executed. Retry the download.";
            }
            throw new BallsWizardApplicationException(
                "wizard_integrity_failed",
                "A Wizard artifact failed verification and was not executed. Retry the download.");
        }
        catch
        {
            throw new BallsWizardApplicationException(
                "wizard_answer_failed",
                "The local Wizard dropped its wand while answering. Try again; your Circle was not changed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Task? task;
        lock (stateLock)
        {
            installCancellation?.Cancel();
            task = installTask;
        }
        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        installCancellation?.Dispose();
        await platform.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunInstallAsync(
        IProgress<BallsWizardInstallProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await platform.InstallAsync(wizardDirectory, progress, cancellationToken)
                .ConfigureAwait(false);
            lock (stateLock)
            {
                stage = "ready";
                failureCode = null;
                failureMessage = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (stateLock)
            {
                stage = "paused";
                failureCode = null;
                failureMessage = null;
            }
        }
        catch (InvalidDataException)
        {
            lock (stateLock)
            {
                stage = "failed";
                failureCode = "wizard_integrity_failed";
                failureMessage =
                    "A Wizard download failed verification and was not activated. Retry the download.";
            }
        }
        catch
        {
            lock (stateLock)
            {
                stage = "failed";
                failureCode = "wizard_install_failed";
                failureMessage =
                    "Wizard installation did not finish. Retry when this Node is online; Balls still works.";
            }
        }
    }

    private void UpdateProgress(BallsWizardInstallProgress progress)
    {
        lock (stateLock)
        {
            progressByArtifact[progress.ArtifactId] = Math.Clamp(
                progress.DownloadedBytes,
                0,
                progress.TotalBytes);
            stage = progress.Stage;
        }
    }

    private static IReadOnlyList<BallsWizardChatMessage> ValidateMessages(
        IReadOnlyList<BrowserBallsWizardChatMessageRequest>? requests)
    {
        if (requests is null || requests.Count == 0 || requests.Count > MaxMessages)
        {
            throw new BallsWizardApplicationException(
                "wizard_messages_invalid",
                $"Wizard chat requires between 1 and {MaxMessages} messages.");
        }

        var totalCharacters = 0;
        var messages = new List<BallsWizardChatMessage>(requests.Count);
        foreach (var request in requests)
        {
            var role = request.Role switch
            {
                "user" => "user",
                "assistant" => "assistant",
                _ => throw new BallsWizardApplicationException(
                    "wizard_message_role_invalid",
                    "Wizard messages may be user or assistant messages only."),
            };
            var content = request.Content?.Trim() ?? string.Empty;
            if (content.Length == 0 || content.Length > MaxMessageCharacters)
            {
                throw new BallsWizardApplicationException(
                    "wizard_message_invalid",
                    $"Each Wizard message must contain 1 to {MaxMessageCharacters} characters.");
            }
            totalCharacters = checked(totalCharacters + content.Length);
            messages.Add(new BallsWizardChatMessage(role, content));
        }

        if (totalCharacters > MaxConversationCharacters
            || messages[^1].Role != "user")
        {
            throw new BallsWizardApplicationException(
                "wizard_conversation_invalid",
                "Wizard chat is too long or does not end with a user question.");
        }

        return messages;
    }

    private static BrowserBallsWizardStatusResponse MapStatus(
        BallsWizardInspection inspection,
        string stage,
        string code,
        string message,
        long downloaded,
        bool installing)
    {
        var installed = inspection.Installation == BallsWizardInstallationStatus.Installed;
        return new BrowserBallsWizardStatusResponse(
            inspection.Support.ToString().ToLowerInvariant(),
            inspection.Installation.ToString().ToLowerInvariant(),
            stage,
            code,
            message,
            inspection.WizardVersion,
            downloaded,
            inspection.Artifacts.Sum(artifact => artifact.SizeBytes),
            inspection.RequiredStorageBytes,
            inspection.Support == BallsWizardSupportStatus.Supported && !installed && !installing,
            installing,
            installed && !installing,
            inspection.Installation != BallsWizardInstallationStatus.Absent || downloaded > 0,
            new BrowserBallsWizardSystemContextResponse(
                inspection.SystemContext.OperatingSystem,
                inspection.SystemContext.OperatingSystemArchitecture,
                inspection.SystemContext.ProcessArchitecture,
                inspection.SystemContext.Cpu,
                inspection.SystemContext.Gpus,
                inspection.SystemContext.TotalMemoryBytes,
                inspection.SystemContext.AvailableMemoryBytes,
                inspection.SystemContext.FreeStorageBytes),
            inspection.Artifacts.Select(
                    artifact => new BrowserBallsWizardArtifactResponse(
                        artifact.Id,
                        artifact.DisplayName,
                        artifact.Version,
                        artifact.Source.AbsoluteUri,
                        artifact.SizeBytes,
                        artifact.Sha256,
                        artifact.License))
                .ToArray());
    }

    private static BrowserBallsWizardStatusResponse UnavailableStatus(string code, string message)
    {
        return new BrowserBallsWizardStatusResponse(
            "blocked",
            "absent",
            "failed",
            code,
            message,
            "wizard-v0",
            0,
            0,
            0,
            false,
            false,
            false,
            false,
            new BrowserBallsWizardSystemContextResponse(
                "Unavailable",
                "Unavailable",
                "Unavailable",
                "Unavailable",
                [],
                0,
                0,
                0),
            []);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }
}

internal sealed class BallsWizardApplicationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
