using System.Security.Cryptography;

namespace Balls.Core;

public sealed class CircleFilesApplication(
    ICircleFilesStateStore state,
    IIdentityAuthorityStore identities,
    TimeProvider timeProvider)
{
    public async Task<CircleFilesContribution> CreateContributionAsync(
        CreateCircleFilesContributionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.RequestId.Value == Guid.Empty || command.CircleId.Value == Guid.Empty)
        {
            throw new InputValidationException(
                "invalid_request_id",
                "Circle and contribution request IDs must be non-empty UUIDs.");
        }

        var displayName = command.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName))
        {
            throw new InputValidationException(
                "contribution_name_required",
                "Contribution name is required.");
        }

        if (displayName.Length > 100)
        {
            throw new InputValidationException(
                "contribution_name_too_long",
                "Contribution name cannot exceed 100 characters.");
        }

        var context = await GetOwnerAuthorizationContextAsync(
            command.CircleId,
            cancellationToken)
            .ConfigureAwait(false);
        var now = GetCurrentTimestamp();
        var unsignedAuthorization = CreateUnsignedAuthorization(context, now);
        var unsigned = new CircleFilesContribution(
            CircleFilesContributionId.New(),
            command.CircleId,
            new CircleFilesProviderIdentity(CircleFilesProviderId.New(), context.NodeId),
            displayName,
            CircleFilesContributionLifecycle.Defined,
            1,
            now,
            unsignedAuthorization);
        var transcript = CircleFilesAuthorizationTranscript.EncodeContribution(
            command.RequestId,
            unsigned);
        var contribution = unsigned with
        {
            Authorization = await AuthorizeAsync(
                command.CircleId,
                context,
                unsignedAuthorization,
                transcript,
                cancellationToken).ConfigureAwait(false),
        };
        return await state.CreateContributionAsync(
            command.RequestId,
            contribution,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<CircleFilesContribution>> ListContributionsAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        state.ListContributionsAsync(circleId, cancellationToken);

    public async Task<AuthorizedCircleFilesContribution> GetAuthorizedLocalContributionAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        CancellationToken cancellationToken = default)
    {
        var context = await GetOwnerAuthorizationContextAsync(circleId, cancellationToken)
            .ConfigureAwait(false);
        var contribution = (await state.ListContributionsAsync(circleId, cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault(value => value.Id == contributionId)
            ?? throw new LocalStateException(
                "circle_files_contribution_not_found",
                "The requested Circle Files contribution was not found.");
        var authorization = contribution.Authorization;
        if (contribution.Provider.NodeId != context.NodeId)
        {
            throw new LocalStateException(
                "circle_files_host_not_local",
                "This contribution is not hosted by the local Node.");
        }

        if (authorization.OwnerMemberId != context.MemberId
            || authorization.AuthorityGeneration != context.AuthorityGeneration
            || authorization.Transcript.Length == 0
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.MemberSignature,
                context.MemberCredential)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.CircleAuthoritySignature,
                context.RootCredential))
        {
            throw new LocalStateException(
                "circle_files_authorization_failed",
                "The Circle Files contribution authorization is invalid or stale.");
        }

        return new AuthorizedCircleFilesContribution(
            contribution,
            context.MemberCredential,
            context.RootCredential);
    }

    public async Task<MemberAccessGrant> CreateAccessGrantAsync(
        CreateMemberAccessGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.RequestId.Value == Guid.Empty
            || command.CircleId.Value == Guid.Empty
            || command.ContributionId.Value == Guid.Empty
            || command.MemberId.Value == Guid.Empty)
        {
            throw new InputValidationException(
                "invalid_request_id",
                "Circle, contribution, Member, and grant request IDs must be non-empty UUIDs.");
        }

        if (!Enum.IsDefined(command.Access))
        {
            throw new InputValidationException(
                "invalid_member_access",
                "Member access must be Read-only or Read/write.");
        }

        var context = await GetOwnerAuthorizationContextAsync(
            command.CircleId,
            cancellationToken)
            .ConfigureAwait(false);
        var now = GetCurrentTimestamp();
        var unsignedAuthorization = CreateUnsignedAuthorization(context, now);
        var unsigned = new MemberAccessGrant(
            MemberAccessGrantId.New(),
            command.CircleId,
            command.ContributionId,
            command.MemberId,
            command.Access,
            MemberAccessGrantLifecycle.Defined,
            1,
            now,
            unsignedAuthorization);
        var transcript = CircleFilesAuthorizationTranscript.EncodeGrant(command.RequestId, unsigned);
        var grant = unsigned with
        {
            Authorization = await AuthorizeAsync(
                command.CircleId,
                context,
                unsignedAuthorization,
                transcript,
                cancellationToken).ConfigureAwait(false),
        };
        return await state.CreateAccessGrantAsync(
            command.RequestId,
            grant,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MemberAccessGrant>> ListAccessGrantsAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        CancellationToken cancellationToken = default) =>
        state.ListAccessGrantsAsync(circleId, contributionId, cancellationToken);

    public async Task<RevokedMemberAccessGrant> RevokeAccessGrantAsync(
        RevokeMemberAccessGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.RequestId.Value == Guid.Empty
            || command.CircleId.Value == Guid.Empty
            || command.ContributionId.Value == Guid.Empty
            || command.GrantId.Value == Guid.Empty
            || command.ExpectedGeneration <= 0)
        {
            throw new InputValidationException(
                "invalid_request_id",
                "Circle, contribution, grant, revocation request, and generation must be valid.");
        }

        var context = await GetOwnerAuthorizationContextAsync(command.CircleId, cancellationToken)
            .ConfigureAwait(false);
        var contribution = await GetAuthorizedLocalContributionAsync(
            command.CircleId,
            command.ContributionId,
            cancellationToken).ConfigureAwait(false);
        var grant = (await state.ListAccessGrantsAsync(
                command.CircleId,
                command.ContributionId,
                cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(value => value.Id == command.GrantId)
            ?? throw new LocalStateException(
                "circle_files_grant_not_found",
                "The requested Circle Files Access Grant was not found.");
        ValidateGrantAuthorization(grant, contribution);
        if (grant.Generation != command.ExpectedGeneration)
        {
            throw new LocalStateConflictException(
                "circle_files_grant_generation_changed",
                "The Access Grant generation changed before revocation.");
        }

        if (grant.Lifecycle == MemberAccessGrantLifecycle.Revoked)
        {
            var existing = await state.GetAccessGrantRevocationAsync(
                command.CircleId,
                command.ContributionId,
                command.GrantId,
                cancellationToken).ConfigureAwait(false)
                ?? throw new LocalStateException(
                    "circle_files_revocation_missing",
                    "The revoked Access Grant is missing its authorization record.");
            ValidateRevocationAuthorization(existing, contribution, command.ExpectedGeneration);
            return existing;
        }

        if (grant.Lifecycle is not (MemberAccessGrantLifecycle.Defined
            or MemberAccessGrantLifecycle.Active))
        {
            throw new LocalStateConflictException(
                "circle_files_grant_generation_changed",
                "The Access Grant cannot be revoked from its current lifecycle.");
        }

        var now = GetCurrentTimestamp();
        var unsignedAuthorization = CreateUnsignedAuthorization(context, now);
        var unsignedRevocation = new MemberAccessGrantRevocation(
            command.RequestId,
            command.CircleId,
            command.ContributionId,
            command.GrantId,
            command.ExpectedGeneration,
            now,
            unsignedAuthorization);
        var transcript = CircleFilesAuthorizationTranscript.EncodeGrantRevocation(unsignedRevocation);
        var revocation = unsignedRevocation with
        {
            Authorization = await AuthorizeAsync(
                command.CircleId,
                context,
                unsignedAuthorization,
                transcript,
                cancellationToken).ConfigureAwait(false),
        };
        return await state.RevokeAccessGrantAsync(
            command.RequestId,
            grant with { Lifecycle = MemberAccessGrantLifecycle.Revoked },
            revocation,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthorizedMemberAccessGrant> GetAuthorizedLocalAccessGrantAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        CancellationToken cancellationToken = default)
    {
        var contribution = await GetAuthorizedLocalContributionAsync(
            circleId,
            contributionId,
            cancellationToken).ConfigureAwait(false);
        var grant = (await state.ListAccessGrantsAsync(
                circleId,
                contributionId,
                cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(value => value.Id == grantId)
            ?? throw new LocalStateException(
                "circle_files_grant_not_found",
                "The requested Circle Files Access Grant was not found.");
        if (grant.Lifecycle != MemberAccessGrantLifecycle.Defined)
        {
            throw new LocalStateException(
                "circle_files_grant_authorization_failed",
                "The Circle Files Access Grant authorization is invalid or stale.");
        }

        ValidateGrantAuthorization(grant, contribution);

        return new AuthorizedMemberAccessGrant(
            grant,
            contribution.Contribution,
            contribution.MemberCredential,
            contribution.CircleAuthorityCredential);
    }

    public async Task<AuthorizedRevokedMemberAccessGrant> GetAuthorizedRevokedLocalAccessGrantAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        CancellationToken cancellationToken = default)
    {
        var contribution = await GetAuthorizedLocalContributionAsync(
            circleId,
            contributionId,
            cancellationToken).ConfigureAwait(false);
        var revoked = await state.GetAccessGrantRevocationAsync(
            circleId,
            contributionId,
            grantId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_files_grant_not_revoked",
                "Revoke the exact Access Grant generation before removing provider state.");
        ValidateGrantAuthorization(revoked.Grant, contribution);
        ValidateRevocationAuthorization(revoked, contribution, revoked.Grant.Generation);
        return new AuthorizedRevokedMemberAccessGrant(
            revoked,
            contribution.Contribution,
            contribution.MemberCredential,
            contribution.CircleAuthorityCredential);
    }

    private static void ValidateGrantAuthorization(
        MemberAccessGrant grant,
        AuthorizedCircleFilesContribution contribution)
    {
        var authorization = grant.Authorization;
        if (grant.CircleId != contribution.Contribution.CircleId
            || grant.ContributionId != contribution.Contribution.Id
            || grant.Generation <= 0
            || authorization.OwnerMemberId != contribution.Contribution.Authorization.OwnerMemberId
            || authorization.AuthorityGeneration
                != contribution.Contribution.Authorization.AuthorityGeneration
            || authorization.Transcript.Length == 0
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.MemberSignature,
                contribution.MemberCredential)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.CircleAuthoritySignature,
                contribution.CircleAuthorityCredential))
        {
            throw new LocalStateException(
                "circle_files_grant_authorization_failed",
                "The Circle Files Access Grant authorization is invalid or stale.");
        }
    }

    private static void ValidateRevocationAuthorization(
        RevokedMemberAccessGrant revoked,
        AuthorizedCircleFilesContribution contribution,
        long expectedGeneration)
    {
        var revocation = revoked.Revocation;
        var authorization = revocation.Authorization;
        var expectedTranscript = CircleFilesAuthorizationTranscript.EncodeGrantRevocation(revocation);
        if (revoked.Grant.Lifecycle != MemberAccessGrantLifecycle.Revoked
            || revoked.Grant.Generation != expectedGeneration
            || revocation.CircleId != revoked.Grant.CircleId
            || revocation.ContributionId != revoked.Grant.ContributionId
            || revocation.GrantId != revoked.Grant.Id
            || revocation.RevokedGeneration != expectedGeneration
            || authorization.OwnerMemberId != contribution.Contribution.Authorization.OwnerMemberId
            || authorization.AuthorityGeneration
                != contribution.Contribution.Authorization.AuthorityGeneration
            || authorization.Transcript.Length == 0
            || !CryptographicOperations.FixedTimeEquals(
                authorization.Transcript,
                expectedTranscript)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.MemberSignature,
                contribution.MemberCredential)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.CircleAuthoritySignature,
                contribution.CircleAuthorityCredential))
        {
            throw new LocalStateException(
                "circle_files_revocation_authorization_failed",
                "The Access Grant revocation authorization is invalid or stale.");
        }
    }

    private async Task<CircleFilesAuthorizationContext> GetOwnerAuthorizationContextAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        var context = await state.GetAuthorizationContextAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known to this Node.");
        if (context.MemberRole != MemberRole.Owner)
        {
            throw new LocalStateException(
                "circle_files_owner_required",
                "Circle Files changes require the local Circle Owner.");
        }

        var authority = await identities.GetCircleAuthorityAsync(circleId, cancellationToken)
            .ConfigureAwait(false);
        if (!MatchesCurrentAuthority(context, authority))
        {
            throw new LocalStateException(
                "circle_files_authority_unavailable",
                "The current Circle authority is unavailable for this mutation.");
        }

        return context;
    }

    private async Task<CircleFilesOwnerAuthorization> AuthorizeAsync(
        CircleId circleId,
        CircleFilesAuthorizationContext context,
        CircleFilesOwnerAuthorization unsigned,
        byte[] transcript,
        CancellationToken cancellationToken)
    {
        var memberSignature = await state.SignWithLocalMemberAsync(
            circleId,
            transcript,
            cancellationToken).ConfigureAwait(false);
        var authoritySignature = await identities.SignWithCircleAuthorityAsync(
            circleId,
            transcript,
            cancellationToken).ConfigureAwait(false);
        if (!IdentityCryptography.Verify(transcript, memberSignature, context.MemberCredential)
            || !IdentityCryptography.Verify(transcript, authoritySignature, context.RootCredential))
        {
            throw new LocalStateException(
                "circle_files_authorization_failed",
                "The Circle Files mutation could not be authorized.");
        }

        return unsigned with
        {
            Transcript = transcript,
            MemberSignature = memberSignature,
            CircleAuthoritySignature = authoritySignature,
        };
    }

    private DateTimeOffset GetCurrentTimestamp() =>
        DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());

    private static CircleFilesOwnerAuthorization CreateUnsignedAuthorization(
        CircleFilesAuthorizationContext context,
        DateTimeOffset now) =>
        new(
            context.MemberId,
            context.AuthorityGeneration,
            now,
            [],
            [],
            []);

    private static bool MatchesCurrentAuthority(
        CircleFilesAuthorizationContext context,
        CircleAuthorityIdentity? authority) =>
        authority is not null
        && authority.CircleId == context.CircleId
        && authority.AuthorityGeneration == context.AuthorityGeneration
        && authority.RootCredential.KeyId == context.RootCredential.KeyId
        && CryptographicOperations.FixedTimeEquals(
            authority.RootCredential.SubjectPublicKeyInfo,
            context.RootCredential.SubjectPublicKeyInfo);
}
