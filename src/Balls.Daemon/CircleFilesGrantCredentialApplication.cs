using System.Security.Cryptography;
using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Control.V1;
using Balls.Storage.Sqlite;

namespace Balls.Daemon;

internal sealed class CircleFilesGrantCredentialApplication(
    CircleFilesApplication files,
    SqliteLocalStateStore store,
    ICircleFilesGrantCredentialProvisioner provisioner)
{
    internal async Task<CircleFilesGrantCredentialPlanResponse> PreviewAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var request = await CreateRequestAsync(
            circleId, contributionId, grantId, folderPath, cancellationToken).ConfigureAwait(false);
        return ToResponse(await provisioner.PreviewAsync(request, cancellationToken).ConfigureAwait(false));
    }

    internal async Task<CircleFilesGrantCredentialApplyResponse> ApplyAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string folderPath,
        string planId,
        CancellationToken cancellationToken)
    {
        var request = await CreateRequestAsync(
            circleId, contributionId, grantId, folderPath, cancellationToken).ConfigureAwait(false);
        var plan = await provisioner.PreviewAsync(request, cancellationToken).ConfigureAwait(false);
        if (planId is null
            || planId.Length != plan.PlanId.Length
            || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(plan.PlanId),
                System.Text.Encoding.ASCII.GetBytes(planId)))
        {
            throw new CircleFilesHostingException(
                "grant_plan_changed",
                "The Windows Member credential plan changed; preview it again before approval.");
        }
        var binding = new CircleFilesProviderCredentialBinding(
            request.GrantId,
            request.Host.CircleId,
            request.Host.ContributionId,
            request.MemberId,
            plan.Provider,
            plan.AccountName,
            plan.OwnershipId,
            request.Access,
            request.Generation);
        var candidate = CircleFilesGrantSecret.Generate();
        try
        {
            using var material = await store.PrepareCircleFilesProviderCredentialAsync(
                binding,
                candidate,
                cancellationToken).ConfigureAwait(false);
            var result = await provisioner.ApplyAsync(
                request,
                planId,
                material.Secret,
                cancellationToken).ConfigureAwait(false);
            await store.CompleteCircleFilesProviderCredentialAsync(binding, cancellationToken)
                .ConfigureAwait(false);
            return new CircleFilesGrantCredentialApplyResponse(
                result.Status == CircleFilesGrantCredentialApplyStatus.Applied
                    ? "applied"
                    : "already-applied",
                ToResponse(result.Plan));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    private async Task<CircleFilesGrantCredentialRequest> CreateRequestAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var authorized = await files.GetAuthorizedLocalAccessGrantAsync(
            circleId, contributionId, grantId, cancellationToken).ConfigureAwait(false);
        var contributionProof = ToProof(
            authorized.Contribution.Authorization,
            authorized.OwnerMemberCredential,
            authorized.CircleAuthorityCredential);
        var grantProof = ToProof(
            authorized.Grant.Authorization,
            authorized.OwnerMemberCredential,
            authorized.CircleAuthorityCredential);
        var contribution = authorized.Contribution;
        var host = new CircleFilesHostRequest(
            contribution.CircleId.ToString(),
            contribution.Id.ToString(),
            contribution.Provider.Id.ToString(),
            contribution.Provider.NodeId.ToString(),
            contribution.DisplayName,
            folderPath,
            CircleFilesHostAuthorizationDigest.Compute(contributionProof),
            contributionProof);
        return new CircleFilesGrantCredentialRequest(
            host,
            authorized.Grant.Id.ToString(),
            authorized.Grant.MemberId.ToString(),
            authorized.Grant.Access == MemberAccessMode.ReadOnly ? "read-only" : "read-write",
            authorized.Grant.Generation,
            CircleFilesHostAuthorizationDigest.Compute(grantProof),
            grantProof);
    }

    private static CircleFilesHostAuthorizationProof ToProof(
        CircleFilesOwnerAuthorization authorization,
        PublicIdentityCredential member,
        PublicIdentityCredential authority) =>
        new(
            authorization.Transcript,
            authorization.MemberSignature,
            authorization.CircleAuthoritySignature,
            ToCredential(member),
            ToCredential(authority));

    private static CircleFilesHostPublicCredential ToCredential(PublicIdentityCredential value) =>
        new(
            value.Role == IdentityKeyRole.Member ? "member" : "circle-authority",
            value.Algorithm,
            value.KeyId,
            value.SubjectPublicKeyInfo);

    private static CircleFilesGrantCredentialPlanResponse ToResponse(
        CircleFilesGrantCredentialPlan plan) =>
        new(
            plan.ContractVersion,
            plan.PlanId,
            plan.Provider,
            plan.FolderPath,
            plan.ShareName,
            plan.AccountName,
            plan.OwnershipId,
            plan.Access,
            plan.Generation,
            plan.Actions);
}
