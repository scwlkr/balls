using System.Collections.Concurrent;
using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Storage.Sqlite;

namespace Balls.Daemon;

internal sealed class BrowserCircleFilesContributionApplication(
    CircleFilesApplication files,
    CircleFilesHostingApplication hosting,
    ICircleFilesFolderPicker folderPicker,
    ICircleFilesHostedFolderStore hostedFolders,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan SelectionLifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, PendingSelection> selections =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    internal async Task<BrowserCircleFilesFolderSelectionResponse> SelectFolderAsync(
        CircleId circleId,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(circleId, cancellationToken).ConfigureAwait(false);
        var sessionKey = BrowserSessionKey.Create(sessionToken);
        CircleFilesFolderSelection? selection;
        try
        {
            selection = await folderPicker.SelectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CircleFilesHostingException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new CircleFilesHostingException(
                "folder_picker_failed",
                "Windows could not open the folder picker. Try choosing the folder again.");
        }

        if (selection is null)
        {
            selections.TryRemove(sessionKey, out _);
            return new BrowserCircleFilesFolderSelectionResponse("cancelled", null, null, null);
        }

        var pending = new PendingSelection(
            circleId,
            Guid.CreateVersion7(),
            selection,
            timeProvider.GetUtcNow() + SelectionLifetime,
            null);
        selections[sessionKey] = pending;
        return new BrowserCircleFilesFolderSelectionResponse(
            "selected",
            pending.SelectionId.ToString("D"),
            selection.FolderPath,
            selection.DisplayName);
    }

    internal async Task<BrowserCircleFilesContributionResponse> ApplyAsync(
        CircleId circleId,
        string sessionToken,
        CircleFilesContributionRequestId requestId,
        Guid selectionId,
        CancellationToken cancellationToken)
    {
        var sessionKey = BrowserSessionKey.Create(sessionToken);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!selections.TryGetValue(sessionKey, out var pending)
                || pending.CircleId != circleId
                || pending.SelectionId != selectionId
                || pending.ExpiresAtUtc <= timeProvider.GetUtcNow()
                || (pending.RequestId is not null && pending.RequestId != requestId))
            {
                if (pending is not null && pending.ExpiresAtUtc <= timeProvider.GetUtcNow())
                {
                    selections.TryRemove(sessionKey, out _);
                }

                throw new LocalStateConflictException(
                    "circle_files_folder_selection_required",
                    "Choose the existing folder again before contributing it.");
            }

            selections[sessionKey] = pending with { RequestId = requestId };
            var contribution = await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    requestId,
                    circleId,
                    pending.Selection.DisplayName),
                cancellationToken).ConfigureAwait(false);
            var preview = await hosting.PreviewAsync(
                circleId,
                contribution.Id,
                pending.Selection.FolderPath,
                cancellationToken).ConfigureAwait(false);
            var applied = await hosting.ApplyAsync(
                circleId,
                contribution.Id,
                pending.Selection.FolderPath,
                preview.PlanId,
                cancellationToken).ConfigureAwait(false);
            await hostedFolders.SaveCircleFilesHostedFolderAsync(
                new CircleFilesHostedFolderBinding(
                    contribution.CircleId,
                    contribution.Id,
                    contribution.Provider.Id,
                    contribution.Provider.NodeId,
                    applied.Plan.FolderPath),
                cancellationToken).ConfigureAwait(false);
            return new BrowserCircleFilesContributionResponse(
                applied.Status,
                contribution.Id.ToString(),
                contribution.DisplayName,
                applied.Plan.FolderPath);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private sealed record PendingSelection(
        CircleId CircleId,
        Guid SelectionId,
        CircleFilesFolderSelection Selection,
        DateTimeOffset ExpiresAtUtc,
        CircleFilesContributionRequestId? RequestId);

    private async Task RequireOwnerAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        var context = await files.GetLocalAuthorizationContextAsync(
            circleId,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            throw new LocalStateException(
                "local_circle_member_not_found",
                "This device does not have an authorized Circle member.");
        }
        if (context.MemberRole != MemberRole.Owner)
        {
            throw new InputValidationException(
                "circle_files_owner_required",
                "Only the Circle Owner can contribute a folder.");
        }
    }
}
