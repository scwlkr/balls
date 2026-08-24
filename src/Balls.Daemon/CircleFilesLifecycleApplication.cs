using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal sealed class CircleFilesLifecycleApplication(
    CircleFilesApplication files,
    ICircleFilesProviderCredentialStore credentials,
    ICircleFilesLifecycleAuditStore audit,
    ICircleFilesLifecycleManager lifecycle,
    TimeProvider timeProvider)
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    internal async Task<MemberAccessGrantRevocationResponse> RevokeGrantAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        MemberAccessGrantRevocationRequestId requestId,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var revoked = await files.RevokeAccessGrantAsync(
                new RevokeMemberAccessGrantCommand(
                    requestId,
                    circleId,
                    contributionId,
                    grantId,
                    expectedGeneration),
                cancellationToken).ConfigureAwait(false);
            await RecordAsync(
                circleId,
                contributionId,
                grantId,
                "grant-revoke",
                "revoked",
                0,
                cancellationToken).ConfigureAwait(false);
            return new MemberAccessGrantRevocationResponse(
                revoked.Revocation.RequestId.ToString(),
                revoked.Grant.Id.ToString(),
                revoked.Revocation.RevokedGeneration,
                revoked.Revocation.RevokedAtUtc,
                "revoked");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    internal async Task<CircleFilesGrantCleanupPlanResponse> PreviewGrantCleanupAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var request = await CreateGrantCleanupRequestAsync(
            circleId,
            contributionId,
            grantId,
            folderPath,
            cancellationToken).ConfigureAwait(false);
        return ToResponse(await lifecycle.PreviewGrantCleanupAsync(request, cancellationToken)
            .ConfigureAwait(false));
    }

    internal async Task<CircleFilesGrantCleanupResultResponse> RemoveGrantAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string folderPath,
        string planId,
        bool terminateOpenSessions,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = await CreateGrantCleanupRequestAsync(
                circleId,
                contributionId,
                grantId,
                folderPath,
                cancellationToken).ConfigureAwait(false);
            using var material = await credentials.GetCircleFilesProviderCredentialForCleanupAsync(
                grantId.ToString(),
                cancellationToken).ConfigureAwait(false)
                ?? throw new LocalStateException(
                    "circle_files_provider_credential_missing",
                    "The exact Windows grant credential record is unavailable for cleanup.");
            EnsureExactBinding(request.Grant, material.Binding);
            var result = await lifecycle.RemoveGrantAsync(
                request,
                planId,
                material.Secret,
                terminateOpenSessions,
                cancellationToken).ConfigureAwait(false);
            if (result.Status is CircleFilesCleanupStatus.Removed
                or CircleFilesCleanupStatus.AlreadyRemoved)
            {
                await credentials.CompleteCircleFilesProviderCredentialRemovalAsync(
                    material.Binding,
                    cancellationToken).ConfigureAwait(false);
            }

            var outcome = ToStatus(result.Status);
            await RecordAsync(
                circleId,
                contributionId,
                grantId,
                "grant-cleanup",
                outcome,
                result.OpenSessionCount,
                cancellationToken).ConfigureAwait(false);
            return new CircleFilesGrantCleanupResultResponse(
                outcome,
                result.OpenSessionCount,
                ToResponse(result.Plan));
        }
        finally
        {
            mutationGate.Release();
        }
    }

    internal async Task<CircleFilesHostRemovalPlanResponse> PreviewHostRemovalAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var request = await CreateHostRemovalRequestAsync(
            circleId,
            contributionId,
            folderPath,
            cancellationToken).ConfigureAwait(false);
        return ToResponse(await lifecycle.PreviewHostRemovalAsync(request, cancellationToken)
            .ConfigureAwait(false));
    }

    internal async Task<CircleFilesHostRemovalResultResponse> RemoveHostAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        string folderPath,
        string planId,
        bool terminateOpenSessions,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = await CreateHostRemovalRequestAsync(
                circleId,
                contributionId,
                folderPath,
                cancellationToken).ConfigureAwait(false);
            var result = await lifecycle.RemoveHostAsync(
                request,
                planId,
                terminateOpenSessions,
                cancellationToken).ConfigureAwait(false);
            var outcome = ToStatus(result.Status);
            await RecordAsync(
                circleId,
                contributionId,
                null,
                "host-remove",
                outcome,
                result.OpenSessionCount,
                cancellationToken).ConfigureAwait(false);
            return new CircleFilesHostRemovalResultResponse(
                outcome,
                result.OpenSessionCount,
                ToResponse(result.Plan));
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<CircleFilesGrantCleanupRequest> CreateGrantCleanupRequestAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var authorized = await files.GetAuthorizedRevokedLocalAccessGrantAsync(
            circleId,
            contributionId,
            grantId,
            cancellationToken).ConfigureAwait(false);
        var state = await credentials.GetCircleFilesProviderCredentialStateAsync(
            grantId.ToString(),
            cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_files_provider_credential_missing",
                "The exact Windows grant credential record is unavailable for cleanup.");
        var contributionProof = ToProof(
            authorized.Contribution.Authorization,
            authorized.OwnerMemberCredential,
            authorized.CircleAuthorityCredential);
        var grantProof = ToProof(
            authorized.Revoked.Grant.Authorization,
            authorized.OwnerMemberCredential,
            authorized.CircleAuthorityCredential);
        var revocationProof = ToProof(
            authorized.Revoked.Revocation.Authorization,
            authorized.OwnerMemberCredential,
            authorized.CircleAuthorityCredential);
        var contribution = authorized.Contribution;
        var grant = authorized.Revoked.Grant;
        var host = new CircleFilesHostRequest(
            contribution.CircleId.ToString(),
            contribution.Id.ToString(),
            contribution.Provider.Id.ToString(),
            contribution.Provider.NodeId.ToString(),
            contribution.DisplayName,
            folderPath,
            CircleFilesHostAuthorizationDigest.Compute(contributionProof),
            contributionProof);
        var request = new CircleFilesGrantCredentialRequest(
            host,
            grant.Id.ToString(),
            grant.MemberId.ToString(),
            grant.Access == MemberAccessMode.ReadOnly ? "read-only" : "read-write",
            grant.Generation,
            CircleFilesHostAuthorizationDigest.Compute(grantProof),
            grantProof);
        EnsureExactBinding(request, state.Binding);
        return new CircleFilesGrantCleanupRequest(
            request,
            new CircleFilesGrantRevocationProof(
                authorized.Revoked.Revocation.RequestId.ToString(),
                circleId.ToString(),
                contributionId.ToString(),
                grantId.ToString(),
                authorized.Revoked.Revocation.RevokedGeneration,
                CircleFilesHostAuthorizationDigest.Compute(revocationProof),
                revocationProof));
    }

    private async Task<CircleFilesHostRequest> CreateHostRemovalRequestAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var authorized = await files.GetAuthorizedLocalContributionAsync(
            circleId,
            contributionId,
            cancellationToken).ConfigureAwait(false);
        var grants = await files.ListAccessGrantsAsync(
            circleId,
            contributionId,
            cancellationToken).ConfigureAwait(false);
        foreach (var grant in grants)
        {
            if (grant.Lifecycle != MemberAccessGrantLifecycle.Revoked)
            {
                throw new LocalStateConflictException(
                    "circle_files_grants_remain",
                    "Revoke every Access Grant before removing the contribution host.");
            }

            var state = await credentials.GetCircleFilesProviderCredentialStateAsync(
                grant.Id.ToString(),
                cancellationToken).ConfigureAwait(false);
            if (state is { IsRemoved: false })
            {
                throw new LocalStateConflictException(
                    "circle_files_provider_credentials_remain",
                    "Remove every exact grant credential before removing the contribution host.");
            }
        }

        var proof = ToProof(
            authorized.Contribution.Authorization,
            authorized.MemberCredential,
            authorized.CircleAuthorityCredential);
        var contribution = authorized.Contribution;
        return new CircleFilesHostRequest(
            circleId.ToString(),
            contributionId.ToString(),
            contribution.Provider.Id.ToString(),
            contribution.Provider.NodeId.ToString(),
            contribution.DisplayName,
            folderPath,
            CircleFilesHostAuthorizationDigest.Compute(proof),
            proof);
    }

    private static void EnsureExactBinding(
        CircleFilesGrantCredentialRequest request,
        CircleFilesProviderCredentialBinding binding)
    {
        if (binding.GrantId != request.GrantId
            || binding.CircleId != request.Host.CircleId
            || binding.ContributionId != request.Host.ContributionId
            || binding.MemberId != request.MemberId
            || binding.Provider != "windows-smb-3.1.1-v1"
            || binding.Access != request.Access
            || binding.Generation != request.Generation)
        {
            throw new LocalStateConflictException(
                "circle_files_provider_credential_conflict",
                "The protected Windows grant credential does not match the revoked grant.");
        }
    }

    private Task RecordAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId? grantId,
        string operation,
        string outcome,
        int openSessionCount,
        CancellationToken cancellationToken) =>
        audit.RecordCircleFilesLifecycleAuditEventAsync(
            new CircleFilesLifecycleAuditEvent(
                Guid.CreateVersion7(),
                circleId,
                contributionId,
                grantId,
                operation,
                outcome,
                openSessionCount,
                timeProvider.GetUtcNow()),
            cancellationToken);

    private static CircleFilesHostAuthorizationProof ToProof(
        CircleFilesOwnerAuthorization authorization,
        PublicIdentityCredential member,
        PublicIdentityCredential authority) =>
        new(
            authorization.Transcript,
            authorization.MemberSignature,
            authorization.CircleAuthoritySignature,
            new CircleFilesHostPublicCredential(
                "member",
                member.Algorithm,
                member.KeyId,
                member.SubjectPublicKeyInfo),
            new CircleFilesHostPublicCredential(
                "circle-authority",
                authority.Algorithm,
                authority.KeyId,
                authority.SubjectPublicKeyInfo));

    private static string ToStatus(CircleFilesCleanupStatus status) => status switch
    {
        CircleFilesCleanupStatus.Removed => "removed",
        CircleFilesCleanupStatus.AlreadyRemoved => "already-removed",
        CircleFilesCleanupStatus.Busy => "busy",
        CircleFilesCleanupStatus.Partial => "partial",
        _ => throw new InvalidOperationException("Unknown Circle Files cleanup status."),
    };

    private static CircleFilesGrantCleanupPlanResponse ToResponse(
        CircleFilesGrantCleanupPlan plan) =>
        new(
            plan.ContractVersion,
            plan.PlanId,
            plan.Provider,
            plan.FolderPath,
            plan.ShareName,
            plan.AccountName,
            plan.OwnershipId,
            plan.Generation,
            plan.Actions);

    private static CircleFilesHostRemovalPlanResponse ToResponse(
        CircleFilesHostRemovalPlan plan) =>
        new(
            plan.ContractVersion,
            plan.PlanId,
            plan.Provider,
            plan.FolderPath,
            plan.ShareName,
            plan.FirewallRuleName,
            plan.OwnershipId,
            plan.Actions);
}
