using System.Collections.Concurrent;
using Balls.Core;
using Balls.Protocol.Browser.V1;
using Balls.Storage.Sqlite;

namespace Balls.Daemon;

internal sealed class BrowserCircleFilesGrantApplication(
    CircleApplication circles,
    CircleFilesApplication files,
    CircleFilesGrantCredentialApplication credentials,
    ICircleFilesHostedFolderStore hostedFolders,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, PendingApproval> approvals = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    internal async Task<BrowserCircleFilesGrantPreviewResponse> PreviewAsync(
        CircleId circleId,
        string sessionToken,
        string folderName,
        string memberName,
        string access,
        CancellationToken cancellationToken)
    {
        var selection = await ResolveSelectionAsync(
            circleId,
            folderName,
            memberName,
            access,
            cancellationToken).ConfigureAwait(false);
        approvals[BrowserSessionKey.Create(sessionToken)] = new PendingApproval(
            selection,
            new MemberAccessGrantRequestId(Guid.CreateVersion7()),
            timeProvider.GetUtcNow() + ApprovalLifetime);
        return new BrowserCircleFilesGrantPreviewResponse(
            selection.Contribution.DisplayName,
            selection.HostedFolder.FolderPath,
            selection.Member.DisplayName,
            "Read/write",
            $"Give {selection.Member.DisplayName} Read/write access to "
                + $"{selection.Contribution.DisplayName} ({selection.HostedFolder.FolderPath}).");
    }

    internal async Task<BrowserCircleFilesGrantApplyResponse> ApplyAsync(
        CircleId circleId,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        var key = BrowserSessionKey.Create(sessionToken);
        if (!approvals.TryGetValue(key, out var approval)
            || approval.Selection.CircleId != circleId
            || approval.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            approvals.TryRemove(key, out _);
            throw new LocalStateConflictException(
                "circle_files_grant_preview_required",
                "Review the folder, Member, and Read/write access again before applying.");
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ResolveSelectionAsync(
                circleId,
                approval.Selection.Contribution.DisplayName,
                approval.Selection.Member.DisplayName,
                "read-write",
                cancellationToken).ConfigureAwait(false);
            if (current.Contribution.Id != approval.Selection.Contribution.Id
                || current.Member.Id != approval.Selection.Member.Id
                || current.Fingerprint != approval.Selection.Fingerprint)
            {
                throw PlanChanged();
            }

            var grants = await files.ListAccessGrantsAsync(
                circleId,
                current.Contribution.Id,
                cancellationToken).ConfigureAwait(false);
            var grant = grants.SingleOrDefault(value => value.MemberId == current.Member.Id);
            if (grant is not null)
            {
                if (grant.Access != MemberAccessMode.ReadWrite
                    || grant.Lifecycle != MemberAccessGrantLifecycle.Defined)
                {
                    throw PlanChanged();
                }

                _ = await files.GetAuthorizedLocalAccessGrantAsync(
                    circleId,
                    current.Contribution.Id,
                    grant.Id,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                grant = await files.CreateAccessGrantAsync(
                    new CreateMemberAccessGrantCommand(
                        approval.RequestId,
                        circleId,
                        current.Contribution.Id,
                        current.Member.Id,
                        MemberAccessMode.ReadWrite),
                    cancellationToken).ConfigureAwait(false);
            }

            var credentialPlan = await credentials.PreviewAsync(
                circleId,
                current.Contribution.Id,
                grant.Id,
                current.HostedFolder.FolderPath,
                cancellationToken).ConfigureAwait(false);
            var result = await credentials.ApplyAsync(
                circleId,
                current.Contribution.Id,
                grant.Id,
                current.HostedFolder.FolderPath,
                credentialPlan.PlanId,
                cancellationToken).ConfigureAwait(false);
            approvals.TryRemove(key, out _);
            return new BrowserCircleFilesGrantApplyResponse(
                result.Status,
                current.Contribution.DisplayName,
                current.Member.DisplayName,
                "Read/write",
                $"{current.Contribution.DisplayName} is now a Circle Capability for "
                    + $"{current.Member.DisplayName}.");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<ResolvedSelection> ResolveSelectionAsync(
        CircleId circleId,
        string folderName,
        string memberName,
        string access,
        CancellationToken cancellationToken)
    {
        var normalizedFolder = NormalizeSelection(folderName, "folder");
        var normalizedMember = NormalizeSelection(memberName, "Member");
        if (!string.Equals(access, "read-write", StringComparison.Ordinal))
        {
            throw new InputValidationException(
                "circle_files_grant_access_invalid",
                "This flow supports Read/write access.");
        }

        var context = await files.GetLocalAuthorizationContextAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known to this device.");
        if (context.MemberRole != MemberRole.Owner)
        {
            throw new InputValidationException(
                "circle_files_owner_required",
                "Only the Circle Owner can share a folder with a Member.");
        }

        var circle = await circles.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known to this device.");
        var members = circle.Members.Where(value =>
            value.Role == MemberRole.Member
            && string.Equals(value.DisplayName, normalizedMember, StringComparison.Ordinal)).ToArray();
        if (members.Length != 1)
        {
            throw new LocalStateConflictException(
                "circle_files_grant_member_unavailable",
                members.Length == 0
                    ? "Choose a joined human Member."
                    : "More than one joined Member has that name; choose an unambiguous Member.");
        }

        var namedContributions = (await files.ListContributionsAsync(circleId, cancellationToken)
                .ConfigureAwait(false))
            .Where(value =>
                value.Lifecycle == CircleFilesContributionLifecycle.Defined
                && string.Equals(value.DisplayName, normalizedFolder, StringComparison.Ordinal))
            .ToArray();
        if (namedContributions.Length == 0)
        {
            throw new LocalStateConflictException(
                "circle_files_grant_folder_unavailable",
                "Choose a contributed folder.");
        }

        var hostedContributions = new List<(
            CircleFilesContribution Contribution,
            CircleFilesHostedFolderBinding HostedFolder)>();
        foreach (var contribution in namedContributions)
        {
            var binding = await hostedFolders.GetCircleFilesHostedFolderAsync(
                circleId,
                contribution.Id,
                cancellationToken).ConfigureAwait(false);
            if (binding is not null)
            {
                hostedContributions.Add((contribution, binding));
            }
        }

        if (hostedContributions.Count == 0)
        {
            throw new LocalStateConflictException(
                "circle_files_hosted_folder_missing",
                "The exact hosted folder is unavailable. Contribute the folder again before sharing it.");
        }
        var selected = hostedContributions
            .OrderBy(value => value.Contribution.CreatedAtUtc)
            .ThenBy(value => value.Contribution.Id.ToString(), StringComparer.Ordinal)
            .First();
        if (hostedContributions.Any(value =>
                value.HostedFolder.NodeId != selected.HostedFolder.NodeId
                || !string.Equals(
                    value.HostedFolder.FolderPath,
                    selected.HostedFolder.FolderPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new LocalStateConflictException(
                "circle_files_grant_folder_unavailable",
                "More than one hosted folder has that name; choose an unambiguous folder.");
        }

        var authorized = await files.GetAuthorizedLocalContributionAsync(
            circleId,
            selected.Contribution.Id,
            cancellationToken).ConfigureAwait(false);
        var hosted = selected.HostedFolder;
        if (hosted.CircleId != authorized.Contribution.CircleId
            || hosted.ContributionId != authorized.Contribution.Id
            || hosted.ProviderId != authorized.Contribution.Provider.Id
            || hosted.NodeId != authorized.Contribution.Provider.NodeId)
        {
            throw new LocalStateException(
                "circle_files_hosted_folder_invalid",
                "The hosted folder binding is invalid and was left unchanged.");
        }

        return new ResolvedSelection(
            circleId,
            authorized.Contribution,
            members[0],
            hosted,
            BrowserCircleFilesGrantApprovalFingerprint.Create(
                authorized.Contribution,
                members[0],
                hosted));
    }

    private static string NormalizeSelection(string? value, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 100)
        {
            throw new InputValidationException(
                "circle_files_grant_selection_invalid",
                $"Choose a valid {label}.");
        }

        return normalized;
    }

    private static LocalStateConflictException PlanChanged() => new(
        "circle_files_grant_approval_changed",
        "The folder, Member, or authorization changed. Review the access again before applying.");

    private sealed record ResolvedSelection(
        CircleId CircleId,
        CircleFilesContribution Contribution,
        Member Member,
        CircleFilesHostedFolderBinding HostedFolder,
        BrowserCircleFilesGrantApprovalFingerprint Fingerprint);

    private sealed record PendingApproval(
        ResolvedSelection Selection,
        MemberAccessGrantRequestId RequestId,
        DateTimeOffset ExpiresAtUtc);
}
