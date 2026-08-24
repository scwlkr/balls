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
        var request = await CreateRequestAsync(
            circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(await mapper.PreviewAsync(request, cancellationToken)
            .ConfigureAwait(false));
    }

    internal async Task<CircleFilesMemberMappingInspectionResponse> InspectAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken)
    {
        var request = await CreateRequestAsync(
            circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken)
            .ConfigureAwait(false);
        var result = await mapper.InspectAsync(request, cancellationToken).ConfigureAwait(false);
        return new CircleFilesMemberMappingInspectionResponse(
            result.Status,
            ToResponse(result.Plan));
    }

    internal Task<CircleFilesMemberMappingResultResponse> MapAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        string planId,
        CancellationToken cancellationToken) =>
        MapLockedAsync(
            circleId, contributionId, grantId, endpoint, driveLetter, planId, cancellationToken);

    internal Task<CircleFilesMemberMappingResultResponse> UnmapAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken) =>
        UnmapLockedAsync(
            circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken);

    private async Task<CircleFilesMemberMappingResultResponse> MapLockedAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        string planId,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (request, material) = await CreateMapRequestAsync(
                circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken)
                .ConfigureAwait(false);
            using (material)
            {
                var result = await mapper.MapAsync(
                    request, planId, material.Secret, cancellationToken).ConfigureAwait(false);
                return new CircleFilesMemberMappingResultResponse(
                    result.Status,
                    ToResponse(result.Plan));
            }
        }
        finally { mutationGate.Release(); }
    }

    private async Task<CircleFilesMemberMappingResultResponse> UnmapLockedAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = await CreateUnmapRequestAsync(
                circleId, contributionId, grantId, endpoint, driveLetter, cancellationToken)
                .ConfigureAwait(false);
            var result = await mapper.UnmapAsync(request, cancellationToken).ConfigureAwait(false);
            return new CircleFilesMemberMappingResultResponse(result.Status, ToResponse(result.Plan));
        }
        finally { mutationGate.Release(); }
    }

    private async Task<CircleFilesMemberMappingRequest> CreateUnmapRequestAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken)
    {
        var normalizedDrive = ValidateAndNormalizeRequest(endpoint, driveLetter);
        AuthorizedMemberAccessGrant authorized;
        try
        {
            var revoked = await files.GetAuthorizedRevokedLocalAccessGrantAsync(
                circleId,
                contributionId,
                grantId,
                cancellationToken).ConfigureAwait(false);
            authorized = new AuthorizedMemberAccessGrant(
                revoked.Revoked.Grant,
                revoked.Contribution,
                revoked.OwnerMemberCredential,
                revoked.CircleAuthorityCredential);
        }
        catch (LocalStateException exception) when (exception.Code == "circle_files_grant_not_revoked")
        {
            authorized = await files.GetAuthorizedLocalAccessGrantAsync(
                circleId,
                contributionId,
                grantId,
                cancellationToken).ConfigureAwait(false);
        }

        var circle = await circles.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        var state = await store.GetCircleFilesProviderCredentialStateAsync(
            grantId.ToString(),
            cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_files_provider_credential_missing",
                "The exact Windows grant credential record is unavailable for unmapping.");
        return CreateRequest(
            circleId,
            contributionId,
            grantId,
            endpoint,
            normalizedDrive,
            authorized,
            circle,
            state.Binding,
            isActive: true);
    }

    private async Task<CircleFilesMemberMappingRequest> CreateRequestAsync(
        CircleId circleId,
            CircleFilesContributionId contributionId,
            MemberAccessGrantId grantId,
            string endpoint,
        string driveLetter,
        CancellationToken cancellationToken)
    {
        var normalizedDrive = ValidateAndNormalizeRequest(endpoint, driveLetter);
        var authorized = await files.GetAuthorizedLocalAccessGrantAsync(
            circleId, contributionId, grantId, cancellationToken).ConfigureAwait(false);
        var circle = await circles.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        var binding = await store.GetActiveCircleFilesProviderCredentialBindingAsync(
            grantId.ToString(), cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_files_provider_credential_missing",
                "Issue the exact Windows grant credential before mapping this Circle folder.");
        return CreateRequest(
            circleId, contributionId, grantId, endpoint, normalizedDrive,
            authorized, circle, binding, isActive: true);
    }

    private async Task<(CircleFilesMemberMappingRequest Request,
        CircleFilesProviderCredentialMaterial Material)> CreateMapRequestAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string driveLetter,
        CancellationToken cancellationToken)
    {
        var normalizedDrive = ValidateAndNormalizeRequest(endpoint, driveLetter);
        var authorized = await files.GetAuthorizedLocalAccessGrantAsync(
            circleId, contributionId, grantId, cancellationToken).ConfigureAwait(false);
        var circle = await circles.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        var material = await store.GetActiveCircleFilesProviderCredentialAsync(
            grantId.ToString(), cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_files_provider_credential_missing",
                "Issue the exact Windows grant credential before mapping this Circle folder.");
        try
        {
            var request = CreateRequest(
                circleId, contributionId, grantId, endpoint, normalizedDrive,
                authorized, circle, material.Binding, material.IsActive);
            return (request, material);
        }
        catch
        {
            material.Dispose();
            throw;
        }
    }

    private static CircleFilesMemberMappingRequest CreateRequest(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string endpoint,
        string normalizedDrive,
        AuthorizedMemberAccessGrant authorized,
        CircleDetails circle,
        CircleFilesProviderCredentialBinding binding,
        bool isActive)
    {
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
            || !isActive)
        {
            throw new LocalStateConflictException(
                "circle_files_provider_credential_conflict",
                "The protected Windows grant credential does not match the authorized grant.");
        }

        return new CircleFilesMemberMappingRequest(
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
                normalizedDrive);
    }

    private static string ValidateAndNormalizeRequest(string endpoint, string driveLetter)
    {
        if (endpoint is null || driveLetter is null)
        {
            throw new CircleFilesHostingException(
                "mapping_request_invalid",
                "The Circle Files Explorer mapping request is invalid.");
        }
        return driveLetter.ToUpperInvariant();
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
