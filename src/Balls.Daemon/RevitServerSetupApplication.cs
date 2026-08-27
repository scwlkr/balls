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
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, BoundSelection> selections = new(StringComparer.Ordinal);

    public RevitServerSetupApplication(
        IRevitServerMediaPicker mediaPicker,
        IRevitServerReadinessInspector inspector,
        TimeProvider? timeProvider = null)
    {
        this.mediaPicker = mediaPicker;
        this.inspector = inspector;
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

    public async ValueTask<RevitServerSetupInspectionResponse> InspectPathAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var report = await inspector.InspectAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        RevitServerSetupPlanResponse? plan = null;
        if (report.Status == RevitServerReadinessStatus.Ready && report.Snapshot is { } snapshot)
        {
            var corePlan = RevitServerSetupPlanFactory.Create(snapshot);
            plan = new RevitServerSetupPlanResponse(
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

    private sealed record BoundSelection(
        string SessionIdentity,
        string Path,
        string FileName,
        DateTimeOffset ExpiresAtUtc);
}
