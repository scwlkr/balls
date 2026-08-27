using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal sealed class RevitServerSetupApplication
{
    private static readonly TimeSpan SelectionLifetime = TimeSpan.FromMinutes(10);
    private readonly IRevitServerMediaPicker mediaPicker;
    private readonly IRevitServerReadinessInspector inspector;
    private readonly IRevitServerSetupOperator setupOperator;
    private readonly IRevitServerHealthInspector healthInspector;
    private readonly IRevitServerSetupStateStore stateStore;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, BoundSelection> selections = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private Task? activeOperation;

    public RevitServerSetupApplication(
        IRevitServerMediaPicker mediaPicker,
        IRevitServerReadinessInspector inspector,
        TimeProvider? timeProvider = null,
        IRevitServerSetupOperator? setupOperator = null,
        IRevitServerHealthInspector? healthInspector = null,
        IRevitServerSetupStateStore? stateStore = null)
    {
        this.mediaPicker = mediaPicker;
        this.inspector = inspector;
        this.setupOperator = setupOperator ?? new UnsupportedRevitServerSetupOperator();
        this.healthInspector = healthInspector ?? new UnsupportedRevitServerHealthInspector();
        this.stateStore = stateStore ?? new MemoryRevitServerSetupStateStore();
        clock = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<(string Id, string FileName)?> SelectMediaAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        var selected = await mediaPicker.SelectAsync(cancellationToken).ConfigureAwait(false);
        if (selected is null)
        {
            return null;
        }

        RemoveExpired();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        selections[id] = new BoundSelection(
            SessionIdentity(sessionToken),
            selected.Path,
            selected.FileName,
            clock.GetUtcNow().Add(SelectionLifetime));
        return (id, selected.FileName);
    }

    public async ValueTask<RevitServerSetupInspectionResponse> InspectSelectedAsync(
        string sessionToken,
        string selectionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        if (string.IsNullOrWhiteSpace(selectionId)
            || !selections.TryGetValue(selectionId, out var selected)
            || selected.ExpiresAtUtc <= clock.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(selected.SessionIdentity),
                Encoding.ASCII.GetBytes(SessionIdentity(sessionToken))))
        {
            return new RevitServerSetupInspectionResponse(
                "blocked",
                "Choose the Autodesk installer again. The previous selection is unavailable or expired.",
                [new RevitServerReadinessCheckResponse("installer", "blocked", "media_selection_expired", "Choose the locally cached official Autodesk installer again.")],
                null);
        }

        return await InspectPathAsync(selected.Path, cancellationToken).ConfigureAwait(false);
    }

    public RevitServerSetupStatusResponse GetStatus()
    {
        var state = stateStore.Load();
        if (state is null)
        {
            return new RevitServerSetupStatusResponse(
                "not-started",
                "Choose the official Autodesk installer and inspect this server.",
                null,
                null,
                []);
        }

        if (state.Stage == RevitServerSetupStages.ApplyingPrerequisites
            && (activeOperation is null || activeOperation.IsCompleted))
        {
            state = Save(state, RevitServerSetupStages.Blocked,
                "Windows preparation was interrupted. Inspect the machine state before starting a fresh attempt.", []);
        }
        else if (state.Stage == RevitServerSetupStages.Verifying)
        {
            state = Save(state, RevitServerSetupStages.Incomplete,
                "Verification was interrupted. Verify the installation again.", []);
        }

        return ToStatus(state);
    }

    public async ValueTask<RevitServerSetupStatusResponse> BeginSelectedAsync(
        string sessionToken,
        BeginRevitServerSetupRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Consent)
        {
            throw new RevitServerSetupException("setup_consent_required", "Review the plan and approve the Windows changes first.");
        }

        var selected = ResolveSelection(sessionToken, request.SelectionId);
        var report = await inspector.InspectAsync(selected.Path, cancellationToken).ConfigureAwait(false);
        if (report.Status != RevitServerReadinessStatus.Ready || report.Snapshot is null)
        {
            throw new RevitServerSetupException("setup_plan_drift", "The server or installer changed. Inspect the setup again.");
        }

        var plan = MapPlan(RevitServerSetupPlanFactory.Create(report.Snapshot));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(plan.PlanDigest),
                Encoding.ASCII.GetBytes(request.PlanDigest)))
        {
            throw new RevitServerSetupException("setup_plan_drift", "The approved setup plan is no longer current. Inspect it again.");
        }

        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeOperation is { IsCompleted: false })
            {
                return GetStatus();
            }

            var prior = stateStore.Load();
            if (prior is not null && prior.Stage is not (RevitServerSetupStages.Failed or RevitServerSetupStages.Blocked or RevitServerSetupStages.Incomplete))
            {
                return ToStatus(prior);
            }

            var state = new RevitServerSetupState(
                1,
                (prior?.Revision ?? 0) + 1,
                Guid.NewGuid().ToString("D"),
                RevitServerSetupStages.ApplyingPrerequisites,
                "Waiting for Windows administrator approval, then preparing Windows and IIS.",
                selected.Path,
                plan.PlanDigest,
                plan,
                [],
                clock.GetUtcNow());
            stateStore.Save(state);
            activeOperation = Task.Run(() => PrepareAndLaunchAsync(state), CancellationToken.None);
            return ToStatus(state);
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async ValueTask<RevitServerSetupStatusResponse> VerifyAsync(CancellationToken cancellationToken)
    {
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        RevitServerSetupState state;
        try
        {
            state = stateStore.Load()
                ?? throw new RevitServerSetupException("setup_not_started", "Inspect and begin Revit Server setup first.");
            if (activeOperation is { IsCompleted: false })
            {
                return ToStatus(state);
            }
            state = Save(state, RevitServerSetupStages.Verifying, "Verifying Revit Server 2027 Host + Admin health.", []);
        }
        finally
        {
            stateGate.Release();
        }

        var report = await healthInspector.InspectAsync(cancellationToken).ConfigureAwait(false);
        var checks = report.Checks.Select(check => new RevitServerReadinessCheckResponse(
            check.Id,
            check.Status == RevitServerHealthStatus.Healthy ? "ready" : "blocked",
            check.Code,
            check.Summary)).ToArray();
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            state = Save(
                stateStore.Load() ?? state,
                report.Status == RevitServerHealthStatus.Healthy
                    ? RevitServerSetupStages.ReadyForHandoff
                    : report.Status == RevitServerHealthStatus.Blocked
                        ? RevitServerSetupStages.Blocked
                        : RevitServerSetupStages.Incomplete,
                report.Summary,
                checks);
            return ToStatus(state);
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async ValueTask<RevitServerSetupStatusResponse> RetryAsync(CancellationToken cancellationToken)
    {
        await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = stateStore.Load()
                ?? throw new RevitServerSetupException("setup_not_started", "Inspect and begin Revit Server setup first.");
            if (activeOperation is { IsCompleted: false })
            {
                return ToStatus(state);
            }
            if (state.Stage == RevitServerSetupStages.Blocked)
            {
                throw new RevitServerSetupException("fresh_inspection_required", "Choose the installer and complete a fresh inspection before retrying.");
            }

            if (!File.Exists(state.MediaPath) || !FileHashMatches(state.MediaPath, state.Plan.MediaSha256))
            {
                state = Save(state, RevitServerSetupStages.Blocked, "The installer changed. Choose it again and complete a fresh inspection.", []);
                return ToStatus(state);
            }

            state = Save(state, RevitServerSetupStages.AwaitingAutodesk,
                "Complete Autodesk Revit Server 2027 setup with Host + Admin and Accelerator off.", []);
            await setupOperator.LaunchAutodeskAsync(state.MediaPath, cancellationToken).ConfigureAwait(false);
            return ToStatus(state);
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async ValueTask<RevitServerSetupInspectionResponse> InspectPathAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var report = await inspector.InspectAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        RevitServerSetupPlanResponse? plan = null;
        if (report.Status == RevitServerReadinessStatus.Ready && report.Snapshot is { } snapshot)
        {
            plan = MapPlan(RevitServerSetupPlanFactory.Create(snapshot));
        }

        return new RevitServerSetupInspectionResponse(
            report.Status == RevitServerReadinessStatus.Ready ? "ready" : "blocked",
            report.Summary,
            report.Checks.Select(check => new RevitServerReadinessCheckResponse(
                check.Id,
                check.Status == RevitServerReadinessStatus.Ready ? "ready" : "blocked",
                check.Code,
                check.Summary)).ToArray(),
            plan);
    }

    private static string SessionIdentity(string sessionToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken))).ToLowerInvariant();

    private void RemoveExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var value in selections)
        {
            if (value.Value.ExpiresAtUtc <= now)
            {
                selections.TryRemove(value.Key, out _);
            }
        }
    }

    private BoundSelection ResolveSelection(string sessionToken, string selectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        if (string.IsNullOrWhiteSpace(selectionId)
            || !selections.TryGetValue(selectionId, out var selected)
            || selected.ExpiresAtUtc <= clock.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(selected.SessionIdentity),
                Encoding.ASCII.GetBytes(SessionIdentity(sessionToken))))
        {
            throw new RevitServerSetupException("media_selection_expired", "Choose the official Autodesk installer again.");
        }

        return selected;
    }

    private async Task PrepareAndLaunchAsync(RevitServerSetupState initial)
    {
        try
        {
            var result = await setupOperator.PrepareAsync(
                new RevitServerSetupPreparationRequest(initial.MediaPath, initial.PlanDigest),
                CancellationToken.None).ConfigureAwait(false);
            var current = stateStore.Load() ?? initial;
            if (result.Status == RevitServerSetupMutationStatus.RestartRequired)
            {
                Save(current, RevitServerSetupStages.Blocked, result.Summary, []);
                return;
            }

            current = Save(current, RevitServerSetupStages.PrerequisitesApplied,
                "Windows and IIS prerequisites are prepared.", []);
            current = Save(current, RevitServerSetupStages.AwaitingAutodesk,
                "Complete Autodesk Revit Server 2027 setup with Host + Admin and Accelerator off.", []);
            await setupOperator.LaunchAutodeskAsync(current.MediaPath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (RevitServerSetupException exception)
        {
            var current = stateStore.Load() ?? initial;
            var stage = exception.Code is "setup_plan_drift" or "setup_helper_authentication_failed"
                ? RevitServerSetupStages.Blocked
                : RevitServerSetupStages.Failed;
            Save(current, stage, exception.Message, []);
        }
        catch
        {
            Save(stateStore.Load() ?? initial, RevitServerSetupStages.Failed,
                "Revit Server setup did not complete. Inspect the server and retry.", []);
        }
    }

    private RevitServerSetupState Save(
        RevitServerSetupState current,
        string stage,
        string summary,
        IReadOnlyList<RevitServerReadinessCheckResponse> checks)
    {
        var updated = current with
        {
            Revision = current.Revision + 1,
            Stage = stage,
            Summary = summary,
            Checks = checks,
            UpdatedAtUtc = clock.GetUtcNow(),
        };
        stateStore.Save(updated);
        return updated;
    }

    private static RevitServerSetupStatusResponse ToStatus(RevitServerSetupState state) =>
        new(state.Stage, state.Summary, state.AttemptId, state.Plan, state.Checks);

    private static bool FileHashMatches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(expected));
    }

    private static RevitServerSetupPlanResponse MapPlan(RevitServerSetupPlan corePlan) =>
        new(
            corePlan.PlanDigest,
            corePlan.Machine,
            corePlan.Windows,
            corePlan.Media,
            corePlan.MediaSha256,
            corePlan.EnabledRoles,
            corePlan.ForbiddenRoles,
            corePlan.DataPaths,
            corePlan.WindowsPrerequisites,
            corePlan.AclIntent,
            corePlan.DefaultWebSiteEffects,
            corePlan.RsnIni,
            corePlan.FirewallEffects,
            corePlan.VerificationActions,
            corePlan.BallsOwnedState,
            corePlan.AutodeskOwnedState);

    private sealed record BoundSelection(
        string SessionIdentity,
        string Path,
        string FileName,
        DateTimeOffset ExpiresAtUtc);
}
