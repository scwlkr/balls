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

    public async Task<CircleFilesContribution> GetAuthorizedLocalContributionAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        CancellationToken cancellationToken = default) =>
        (await GetAuthorizedLocalContributionForHostingAsync(
            circleId,
            contributionId,
            cancellationToken).ConfigureAwait(false)).Contribution;

    public async Task<AuthorizedCircleFilesContribution> GetAuthorizedLocalContributionForHostingAsync(
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
