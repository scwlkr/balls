using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal sealed class CircleFilesMemberMappingApplication(
    CircleApplication circles,
    CircleFilesApplication files,
    ICircleFilesProviderCredentialStore store,
    ICircleFilesMemberMapper mapper)
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    internal async Task<CircleFilesMemberMappingPlanResponse> PreviewAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken)
    {
        var (request, material) = await CreateRequestAsync(
            circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken)
            .ConfigureAwait(false);
        using (material)
        {
            return ToResponse(await mapper.PreviewAsync(request, cancellationToken)
                .ConfigureAwait(false));
        }
    }

    internal async Task<CircleFilesMemberMappingInspectionResponse> InspectAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken)
    {
        var (request, material) = await CreateRequestAsync(
            circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken)
            .ConfigureAwait(false);
        using (material)
        {
            var result = await mapper.InspectAsync(request, material.Secret, cancellationToken)
                .ConfigureAwait(false);
            return new CircleFilesMemberMappingInspectionResponse(
                result.Status,
                ToResponse(result.Plan));
        }
    }

    internal Task<CircleFilesMemberMappingResultResponse> MapAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        string planId,
        CancellationToken cancellationToken) =>
        MutateAsync(
            circleId, contributionId, grantId, endpoint, driveLetter,
            (request, material, token) => mapper.MapAsync(request, planId, material.Secret, token),
            cancellationToken);

    internal Task<CircleFilesMemberMappingResultResponse> UnmapAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken) =>
        MutateAsync(
            circleId, contributionId, grantId, endpoint, driveLetter,
            (request, material, token) => mapper.UnmapAsync(request, material.Secret, token),
            cancellationToken);

    private async Task<CircleFilesMemberMappingResultResponse> MutateAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        Func<CircleFilesMemberMappingRequest, CircleFilesProviderCredentialMaterial,
            CancellationToken, ValueTask<CircleFilesMemberMappingResult>> operation,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (request, material) = await CreateRequestAsync(
                circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken)
                .ConfigureAwait(false);
            using (material)
            {
                var result = await operation(request, material, cancellationToken)
                    .ConfigureAwait(false);
                return new CircleFilesMemberMappingResultResponse(
                    result.Status,
                    ToResponse(result.Plan));
            }
        }
        finally { mutationGate.Release(); }
    }

    private async Task<(CircleFilesMemberMappingRequest Request,
        CircleFilesProviderCredentialMaterial Material)> CreateRequestAsync(
            CircleId circleId,
            CircleFilesContributionId contributionId,
            MemberAccessGrantId grantId,
            string endpoint,
            string driveLetter,
            CancellationToken cancellationToken)
    {
        var authorized = await files.GetAuthorizedLocalAccessGrantAsync(
            circleId, contributionId, grantId, cancellationToken).ConfigureAwait(false);
        var circle = await circles.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        var material = await store.GetActiveCircleFilesProviderCredentialAsync(
            grantId.ToString(), cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_files_provider_credential_missing",
                "Issue the exact Windows grant credential before mapping this Circle folder.");
        var binding = material.Binding;
        var grant = authorized.Grant;
        var contribution = authorized.Contribution;
        var expectedAccess = grant.Access == MemberAccessMode.ReadOnly ? "read-only" : "read-write";
        if (binding.GrantId != grant.Id.ToString()
            || binding.CircleId != circleId.ToString()
            || binding.ContributionId != contributionId.ToString()
            || binding.MemberId != grant.MemberId.ToString()
            || binding.Provider != "windows-smb-3.1.1-v1"
            || binding.Access != expectedAccess
            || binding.Generation != grant.Generation
            || !material.IsActive)
        {
            material.Dispose();
            throw new LocalStateConflictException(
                "circle_files_provider_credential_conflict",
                "The protected Windows grant credential does not match the authorized grant.");
        }

        return (
            new CircleFilesMemberMappingRequest(
                circleId.ToString(),
                contributionId.ToString(),
                contribution.Provider.Id.ToString(),
                grantId.ToString(),
                grant.MemberId.ToString(),
                binding.AccountName,
                binding.OwnershipId,
                expectedAccess,
                grant.Generation,
                circle.Circle.Name,
                endpoint,
                driveLetter.ToUpperInvariant()),
            material);
    }

    private static CircleFilesMemberMappingPlanResponse ToResponse(
        CircleFilesMemberMappingPlan plan) =>
        new(
            plan.ContractVersion,
            plan.PlanId,
            plan.Endpoint,
            plan.UncPath,
            plan.CredentialTarget,
            plan.DriveLetter,
            plan.FriendlyName,
            plan.OwnershipId,
            plan.AvailableDriveLetters,
            plan.Actions);
}
