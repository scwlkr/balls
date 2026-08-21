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

        var context = await state.GetAuthorizationContextAsync(command.CircleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known to this Node.");
        if (context.MemberRole != MemberRole.Owner)
        {
            throw new LocalStateException(
                "circle_files_owner_required",
                "Circle Files contribution changes require the local Circle Owner.");
        }

        var authority = await identities.GetCircleAuthorityAsync(command.CircleId, cancellationToken)
            .ConfigureAwait(false);
        if (!MatchesCurrentAuthority(context, authority))
        {
            throw new LocalStateException(
                "circle_files_authority_unavailable",
                "The current Circle authority is unavailable for this mutation.");
        }

        var now = DateTimeOffset.FromUnixTimeSeconds(
            timeProvider.GetUtcNow().ToUnixTimeSeconds());
        var unsignedAuthorization = new CircleFilesOwnerAuthorization(
            context.MemberId,
            context.AuthorityGeneration,
            now,
            [],
            [],
            []);
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
        var memberSignature = await state.SignWithLocalMemberAsync(
            command.CircleId,
            transcript,
            cancellationToken).ConfigureAwait(false);
        var authoritySignature = await identities.SignWithCircleAuthorityAsync(
            command.CircleId,
            transcript,
            cancellationToken).ConfigureAwait(false);
        if (!IdentityCryptography.Verify(transcript, memberSignature, context.MemberCredential)
            || !IdentityCryptography.Verify(transcript, authoritySignature, context.RootCredential))
        {
            throw new LocalStateException(
                "circle_files_authorization_failed",
                "The Circle Files mutation could not be authorized.");
        }

        var contribution = unsigned with
        {
            Authorization = unsignedAuthorization with
            {
                Transcript = transcript,
                MemberSignature = memberSignature,
                CircleAuthoritySignature = authoritySignature,
            },
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

        var context = await state.GetAuthorizationContextAsync(command.CircleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known to this Node.");
        if (context.MemberRole != MemberRole.Owner)
        {
            throw new LocalStateException(
                "circle_files_owner_required",
                "Circle Files grant changes require the local Circle Owner.");
        }

        var authority = await identities.GetCircleAuthorityAsync(command.CircleId, cancellationToken)
            .ConfigureAwait(false);
        if (!MatchesCurrentAuthority(context, authority))
        {
            throw new LocalStateException(
                "circle_files_authority_unavailable",
                "The current Circle authority is unavailable for this mutation.");
        }

        var now = DateTimeOffset.FromUnixTimeSeconds(
            timeProvider.GetUtcNow().ToUnixTimeSeconds());
        var unsignedAuthorization = new CircleFilesOwnerAuthorization(
            context.MemberId,
            context.AuthorityGeneration,
            now,
            [],
            [],
            []);
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
        var memberSignature = await state.SignWithLocalMemberAsync(
            command.CircleId,
            transcript,
            cancellationToken).ConfigureAwait(false);
        var authoritySignature = await identities.SignWithCircleAuthorityAsync(
            command.CircleId,
            transcript,
            cancellationToken).ConfigureAwait(false);
        if (!IdentityCryptography.Verify(transcript, memberSignature, context.MemberCredential)
            || !IdentityCryptography.Verify(transcript, authoritySignature, context.RootCredential))
        {
            throw new LocalStateException(
                "circle_files_authorization_failed",
                "The Circle Files mutation could not be authorized.");
        }

        var grant = unsigned with
        {
            Authorization = unsignedAuthorization with
            {
                Transcript = transcript,
                MemberSignature = memberSignature,
                CircleAuthoritySignature = authoritySignature,
            },
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
