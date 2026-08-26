using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal sealed class CircleFilesMemberMappingApplication(
    CircleApplication circles,
    CircleFilesApplication files,
    ICircleFilesProviderCredentialStore store,
    ICircleFilesLifecycleAuditStore audit,
    ICircleFilesMemberMapper mapper,
    ICircleFilesLocationLauncher locationLauncher,
    TimeProvider timeProvider,
    ICircleMessageStateStore? authorities = null)
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

    internal Task<BrowserCircleFilesOpenResponse> OpenAsync(
        CircleId circleId,
        string endpoint,
        CancellationToken cancellationToken) =>
        OpenLockedAsync(circleId, endpoint, cancellationToken);

    private async Task<BrowserCircleFilesOpenResponse> OpenLockedAsync(
        CircleId circleId,
        string endpoint,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var capability = await SelectMemberCapabilityAsync(circleId, cancellationToken)
                .ConfigureAwait(false);
            var (unselectedRequest, material) = await CreateMapRequestAsync(
                circleId,
                capability.Contribution.Id,
                capability.Grant.Id,
                endpoint,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
            using (material)
            {
                var driveLetter = await FindExistingDriveAsync(
                    unselectedRequest,
                    cancellationToken).ConfigureAwait(false);
                if (driveLetter is null)
                {
                    var discovery = await mapper.PreviewAsync(
                        unselectedRequest,
                        cancellationToken).ConfigureAwait(false);
                    driveLetter = SelectPreferredDrive(discovery.AvailableDriveLetters);
                }
                if (driveLetter is null)
                {
                    throw new CircleFilesHostingException(
                        "mapping_drive_unavailable",
                        "No supported drive letter is available for this shared folder.");
                }

                var exactRequest = unselectedRequest with { DriveLetter = driveLetter };
                var exactPlan = await mapper.PreviewAsync(exactRequest, cancellationToken)
                    .ConfigureAwait(false);
                _ = await mapper.MapAsync(
                    exactRequest,
                    exactPlan.PlanId,
                    material.Secret,
                    cancellationToken).ConfigureAwait(false);
                await locationLauncher.OpenAsync(
                    new CircleFilesMappedLocation(driveLetter),
                    cancellationToken).ConfigureAwait(false);
            }

            return new BrowserCircleFilesOpenResponse(
                "opened",
                capability.Contribution.DisplayName,
                $"Opened {capability.Contribution.DisplayName} in File Explorer.");
        }
        finally { mutationGate.Release(); }
    }

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
            await RecordUnmapAsync(
                circleId,
                contributionId,
                grantId,
                "requested",
                cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await mapper.UnmapAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                await RecordUnmapAsync(
                    circleId,
                    contributionId,
                    grantId,
                    result.Status,
                    CancellationToken.None).ConfigureAwait(false);
                return new CircleFilesMemberMappingResultResponse(
                    result.Status,
                    ToResponse(result.Plan));
            }
            catch (OperationCanceledException)
            {
                await RecordUnmapAsync(
                    circleId,
                    contributionId,
                    grantId,
                    "cancelled",
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is LocalStateException
                or InputValidationException
                or CircleFilesHostingException)
            {
                await RecordUnmapAsync(
                    circleId,
                    contributionId,
                    grantId,
                    "refused",
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch
            {
                await RecordUnmapAsync(
                    circleId,
                    contributionId,
                    grantId,
                    "failed",
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally { mutationGate.Release(); }
    }

    private Task RecordUnmapAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        string outcome,
        CancellationToken cancellationToken) =>
        audit.RecordCircleFilesLifecycleAuditEventAsync(
            new CircleFilesLifecycleAuditEvent(
                Guid.CreateVersion7(),
                circleId,
                contributionId,
                grantId,
                "mapping-unmap",
                outcome,
                0,
                timeProvider.GetUtcNow()),
            cancellationToken);

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
        var localAuthorization = await files.GetLocalAuthorizationContextAsync(
            circleId,
            cancellationToken).ConfigureAwait(false);
        if (localAuthorization?.MemberRole == MemberRole.Member)
        {
            authorized = await GetAuthorizedMappingGrantAsync(
                circleId,
                contributionId,
                grantId,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
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
            catch (LocalStateException exception)
                when (exception.Code == "circle_files_grant_not_revoked")
            {
                authorized = await files.GetAuthorizedLocalAccessGrantAsync(
                    circleId,
                    contributionId,
                    grantId,
                    cancellationToken).ConfigureAwait(false);
            }
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
        var authorized = await GetAuthorizedMappingGrantAsync(
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
        var authorized = await GetAuthorizedMappingGrantAsync(
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

    private async Task<AuthorizedMemberAccessGrant> GetAuthorizedMappingGrantAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        CancellationToken cancellationToken)
    {
        var context = await files.GetLocalAuthorizationContextAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        if (context.MemberRole == MemberRole.Owner)
        {
            return await files.GetAuthorizedLocalAccessGrantAsync(
                circleId,
                contributionId,
                grantId,
                cancellationToken).ConfigureAwait(false);
        }

        var contribution = (await files.ListContributionsAsync(circleId, cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault(value => value.Id == contributionId)
            ?? throw new LocalStateException(
                "circle_files_contribution_not_found",
                "The requested Circle Files contribution was not found.");
        var grant = (await files.ListAccessGrantsAsync(
                circleId,
                contributionId,
                cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(value => value.Id == grantId && value.MemberId == context.MemberId)
            ?? throw new LocalStateException(
                "circle_files_grant_not_found",
                "No authorized Circle Files grant exists for the local Member.");
        var memberAuthorities = authorities
            ?? throw new LocalStateException(
                "circle_files_authorization_failed",
                "The remote Circle Files Owner credentials are unavailable.");
        var owner = await memberAuthorities.GetCircleMessageAuthorAsync(
            circleId,
            contribution.Authorization.OwnerMemberId,
            contribution.Provider.NodeId,
            cancellationToken).ConfigureAwait(false);
        var circle = await circles.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false);
        if (owner is null
            || !owner.IsAuthorized
            || circle is null
            || !circle.Members.Any(member =>
                member.Id == owner.MemberId && member.Role == MemberRole.Owner))
        {
            throw new LocalStateException(
                "circle_files_authorization_failed",
                "The remote Circle Files Owner is not trusted by this Circle.");
        }

        _ = CircleFilesRemoteAuthorization.Validate(
            contribution,
            grant,
            owner.MemberCredential,
            context);
        return new AuthorizedMemberAccessGrant(
            grant,
            contribution,
            owner.MemberCredential,
            context.RootCredential);
    }

    private async Task<MemberCapability> SelectMemberCapabilityAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        var context = await files.GetLocalAuthorizationContextAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known.");
        if (context.MemberRole != MemberRole.Member)
        {
            throw new LocalStateConflictException(
                "circle_files_member_required",
                "Only a joined Circle Member can open this shared folder.");
        }

        var candidates = new List<MemberCapability>();
        var contributions = await files.ListContributionsAsync(circleId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var contribution in contributions.Where(value =>
                     value.Lifecycle is CircleFilesContributionLifecycle.Defined
                         or CircleFilesContributionLifecycle.Active))
        {
            var grants = await files.ListAccessGrantsAsync(
                circleId,
                contribution.Id,
                cancellationToken).ConfigureAwait(false);
            foreach (var grant in grants.Where(value =>
                         value.MemberId == context.MemberId
                         && value.Lifecycle is MemberAccessGrantLifecycle.Defined
                             or MemberAccessGrantLifecycle.Active))
            {
                if (await store.GetActiveCircleFilesProviderCredentialBindingAsync(
                        grant.Id.ToString(),
                        cancellationToken).ConfigureAwait(false) is not null)
                {
                    candidates.Add(new MemberCapability(contribution, grant));
                }
            }
        }

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new LocalStateConflictException(
                "circle_files_capability_unavailable",
                "The shared folder is not ready yet. Ask the Circle owner to finish sharing it, then try again."),
            _ => throw new LocalStateConflictException(
                "circle_files_capability_ambiguous",
                "More than one shared folder is ready. This version can open one at a time."),
        };
    }

    private async Task<string?> FindExistingDriveAsync(
        CircleFilesMemberMappingRequest request,
        CancellationToken cancellationToken)
    {
        string? existing = null;
        foreach (var drive in Enumerable.Range('D', 'Z' - 'D' + 1)
                     .Select(value => ((char)value).ToString()))
        {
            CircleFilesMemberMappingInspection? inspection = null;
            try
            {
                inspection = await mapper.InspectAsync(
                    request with { DriveLetter = drive },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (CircleFilesHostingException exception)
                when (exception.Code.Contains("collision", StringComparison.Ordinal))
            {
                // A different current-user resource occupies this candidate letter. Keep looking
                // for the exact Balls-owned mapping without changing the unrelated resource.
            }
            if (inspection is null || inspection.Status == "unmapped")
            {
                continue;
            }
            if (existing is not null)
            {
                throw new CircleFilesHostingException(
                    "mapping_resource_collision",
                    "More than one existing mapping matches this shared folder.");
            }
            existing = drive;
        }
        return existing;
    }

    internal static string? SelectPreferredDrive(IReadOnlyList<string> availableDriveLetters)
    {
        var supported = availableDriveLetters.Where(value =>
            value.Length == 1 && value[0] is >= 'D' and <= 'Z');
        return supported.Contains("P", StringComparer.Ordinal)
            ? "P"
            : supported.FirstOrDefault();
    }

    private sealed record MemberCapability(
        CircleFilesContribution Contribution,
        MemberAccessGrant Grant);

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
