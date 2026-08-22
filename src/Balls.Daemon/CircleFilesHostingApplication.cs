using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal sealed class CircleFilesHostingApplication(
    CircleFilesApplication files,
    ICircleFilesHostProvisioner provisioner)
{
    internal async Task<CircleFilesHostPlanResponse> PreviewAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var request = await CreateRequestAsync(
            circleId,
            contributionId,
            folderPath,
            cancellationToken).ConfigureAwait(false);
        return ToResponse(
            await provisioner.PreviewAsync(request, cancellationToken).ConfigureAwait(false));
    }

    internal async Task<CircleFilesHostApplyResponse> ApplyAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        string folderPath,
        string planId,
        CancellationToken cancellationToken)
    {
        var request = await CreateRequestAsync(
            circleId,
            contributionId,
            folderPath,
            cancellationToken).ConfigureAwait(false);
        var result = await provisioner.ApplyAsync(request, planId, cancellationToken)
            .ConfigureAwait(false);
        return new CircleFilesHostApplyResponse(
            result.Status == CircleFilesHostApplyStatus.Applied ? "applied" : "already-applied",
            ToResponse(result.Plan));
    }

    private async Task<CircleFilesHostRequest> CreateRequestAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var authorized = await files.GetAuthorizedLocalContributionAsync(
            circleId,
            contributionId,
            cancellationToken).ConfigureAwait(false);
        var contribution = authorized.Contribution;
        var authorization = new CircleFilesHostAuthorizationProof(
            contribution.Authorization.Transcript,
            contribution.Authorization.MemberSignature,
            contribution.Authorization.CircleAuthoritySignature,
            ToHostCredential(authorized.MemberCredential),
            ToHostCredential(authorized.CircleAuthorityCredential));
        return new CircleFilesHostRequest(
            contribution.CircleId.ToString(),
            contribution.Id.ToString(),
            contribution.Provider.Id.ToString(),
            contribution.Provider.NodeId.ToString(),
            contribution.DisplayName,
            folderPath,
            CircleFilesHostAuthorizationDigest.Compute(authorization),
            authorization);
    }

    private static CircleFilesHostPublicCredential ToHostCredential(
        PublicIdentityCredential credential) =>
        new(
            credential.Role switch
            {
                IdentityKeyRole.Member => "member",
                IdentityKeyRole.CircleAuthority => "circle-authority",
                _ => throw new InvalidOperationException("The Circle Files hosting credential role is invalid."),
            },
            credential.Algorithm,
            credential.KeyId,
            credential.SubjectPublicKeyInfo);

    private static CircleFilesHostPlanResponse ToResponse(CircleFilesHostPlan plan) =>
        new(
            plan.ContractVersion,
            plan.PlanId,
            plan.Provider,
            plan.FolderPath,
            plan.ShareName,
            plan.FirewallRuleName,
            plan.OwnershipId,
            plan.TargetExists,
            plan.Actions);
}
