using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Browser.V1;

namespace Balls.Daemon;

internal sealed class BrowserCircleFilesContributionApplication(
    CircleFilesApplication files,
    CircleFilesHostingApplication hosting,
    ICircleFilesFolderPicker folderPicker,
    ICircleFilesHostedFolderStore hostedFolders)
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    internal async Task<BrowserCircleFilesFolderSelectionResponse> SelectFolderAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(circleId, cancellationToken).ConfigureAwait(false);
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

        return selection is null
            ? new BrowserCircleFilesFolderSelectionResponse("cancelled", null, null)
            : new BrowserCircleFilesFolderSelectionResponse(
                "selected",
                selection.FolderPath,
                selection.DisplayName);
    }

    internal async Task<BrowserCircleFilesContributionResponse> ApplyAsync(
        CircleId circleId,
        CircleFilesContributionRequestId requestId,
        string folderPath,
        string displayName,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var contribution = await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(requestId, circleId, displayName),
                cancellationToken).ConfigureAwait(false);
            var preview = await hosting.PreviewAsync(
                circleId,
                contribution.Id,
                folderPath,
                cancellationToken).ConfigureAwait(false);
            var applied = await hosting.ApplyAsync(
                circleId,
                contribution.Id,
                folderPath,
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
