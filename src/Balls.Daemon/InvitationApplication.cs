using System.Security.Cryptography;
using System.Text;
using Balls.Core;
using Balls.Protocol.Remote.V1;

namespace Balls.Daemon;

internal sealed class InvitationApplication(
    ILocalStateStore localState,
    IIdentityAuthorityStore identities,
    IInvitationStateStore invitationState,
    TimeProvider timeProvider)
{
    internal const int DefaultValidityMinutes = 60;
    internal const int MinimumValidityMinutes = 1;
    internal const int MaximumValidityMinutes = 7 * 24 * 60;

    internal async Task<IssuedInvitation> CreateAsync(
        CircleId circleId,
        int validForMinutes,
        CancellationToken cancellationToken = default)
    {
        if (validForMinutes is < MinimumValidityMinutes or > MaximumValidityMinutes)
        {
            throw new InputValidationException(
                "invalid_invitation_validity",
                $"Invitation validity must be between {MinimumValidityMinutes} and {MaximumValidityMinutes} minutes.");
        }

        var circle = await localState.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known to this Node.");
        var node = await identities.GetNodeCryptographicIdentityAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "node_identity_missing",
                "The local Node cryptographic identity is missing.");
        var authority = await identities.GetCircleAuthorityAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_authority_not_found",
                "The requested Circle authority is not known to this Node.");
        var transport = await identities.GetLocalTransportIdentityAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "transport_identity_missing",
                "The local transport cryptographic identity is missing.");

        var now = ToProtocolSecond(timeProvider.GetUtcNow());
        var expires = now.AddMinutes(validForMinutes);
        var issuerId = node.NodeId.ToString();
        var root = ToProtocolCredential(authority.RootCredential);
        var anchor = ToProtocolCredential(authority.AnchorCredential);
        var delegation = new InvitationIssuerDelegation(
            circle.Circle.Id.ToString(),
            authority.AuthorityGeneration,
            root.KeyId,
            issuerId,
            anchor,
            InvitationSecurity.SingleUseInvitationAuthorization,
            now,
            expires);
        var delegationSignature = await identities.SignWithCircleAuthorityAsync(
            circleId,
            InvitationSecurity.EncodeDelegation(delegation),
            cancellationToken).ConfigureAwait(false);
        var signedDelegation = new SignedInvitationIssuerDelegation(
            delegation,
            RemoteSecurityProtocol.SignatureSuite,
            delegationSignature);

        var invitationId = InvitationId.New();
        var invitation = new CircleInvitation(
            circle.Circle.Id.ToString(),
            invitationId.ToString(),
            issuerId,
            anchor.KeyId,
            transport.Credential.KeyId,
            authority.AuthorityGeneration,
            now,
            expires,
            1,
            RemoteSecurityProtocol.Version,
            RemoteSecurityProtocol.Version,
            RandomNumberGenerator.GetBytes(32));
        var invitationSignature = await identities.SignWithCircleAnchorAsync(
            circleId,
            AdmissionSecurity.EncodeInvitation(invitation),
            cancellationToken).ConfigureAwait(false);
        var package = new CircleInvitationPackage(
            InvitationPackageCodec.Version,
            root,
            signedDelegation,
            new SignedCircleInvitation(
                invitation,
                RemoteSecurityProtocol.SignatureSuite,
                invitationSignature));
        var encoded = InvitationPackageCodec.Encode(package);
        await invitationState.StoreCircleInvitationAsync(
            new PersistedCircleInvitation(
                invitationId,
                circleId,
                SHA256.HashData(encoded),
                encoded,
                expires,
                now),
            cancellationToken).ConfigureAwait(false);
        return new IssuedInvitation(
            circleId,
            invitationId,
            expires,
            Encoding.UTF8.GetString(encoded));
    }

    internal async Task<RedeemedInvitation> RedeemAsync(
        string encodedPackage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(encodedPackage))
        {
            throw Rejected(InvitationRejectionCode.Malformed);
        }

        byte[] encoded;
        try
        {
            encoded = new UTF8Encoding(false, true).GetBytes(encodedPackage);
        }
        catch (EncoderFallbackException)
        {
            throw Rejected(InvitationRejectionCode.Malformed);
        }

        CircleInvitationPackage package;
        try
        {
            package = InvitationPackageCodec.Decode(encoded);
        }
        catch (InvitationPackageException)
        {
            throw Rejected(InvitationRejectionCode.Malformed);
        }

        if (!Guid.TryParseExact(package.Invitation.Invitation.InvitationId, "D", out var rawInvitationId))
        {
            throw Rejected(InvitationRejectionCode.Malformed);
        }

        var invitationId = new InvitationId(rawInvitationId);
        var stored = await invitationState.GetCircleInvitationAsync(invitationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "invitation_not_found",
                "The requested Circle invitation is not known to this Node.");
        var authority = await identities.GetCircleAuthorityAsync(stored.CircleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_authority_not_found",
                "The requested Circle authority is not known to this Node.");
        var validation = InvitationSecurity.Validate(
            package,
            new InvitationVerificationContext(
                stored.CircleId.ToString(),
                ToProtocolCredential(authority.RootCredential),
                ToProtocolSecond(timeProvider.GetUtcNow()),
                authority.AuthorityGeneration,
                InvitationUseState.Available,
                new HashSet<string>(StringComparer.Ordinal)));
        if (!validation.IsValid)
        {
            throw Rejected(validation.RejectionCode);
        }

        var redemption = await invitationState.RedeemCircleInvitationAsync(
            invitationId,
            SHA256.HashData(encoded),
            RedemptionId.New(),
            ToProtocolSecond(timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        if (redemption.Status != InvitationRedemptionStatus.Accepted
            || redemption.RedemptionId is null)
        {
            throw Rejected(redemption.Status switch
            {
                InvitationRedemptionStatus.Replayed => InvitationRejectionCode.Replayed,
                InvitationRedemptionStatus.Revoked => InvitationRejectionCode.Revoked,
                InvitationRedemptionStatus.Expired => InvitationRejectionCode.Expired,
                _ => InvitationRejectionCode.Malformed,
            });
        }

        return new RedeemedInvitation(
            stored.CircleId,
            invitationId,
            redemption.RedemptionId.Value);
    }

    private static PublicKeyCredential ToProtocolCredential(PublicIdentityCredential credential) =>
        new(
            credential.Role switch
            {
                IdentityKeyRole.CircleAuthority => KeyRole.CircleAuthority,
                IdentityKeyRole.Anchor => KeyRole.Anchor,
                IdentityKeyRole.Member => KeyRole.Member,
                IdentityKeyRole.Node => KeyRole.Node,
                IdentityKeyRole.Transport => KeyRole.Transport,
                _ => throw new ArgumentOutOfRangeException(nameof(credential)),
            },
            credential.Algorithm,
            credential.KeyId,
            credential.SubjectPublicKeyInfo);

    private static DateTimeOffset ToProtocolSecond(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());

    private static InvitationRejectedException Rejected(InvitationRejectionCode code) =>
        new(code);
}

internal sealed record IssuedInvitation(
    CircleId CircleId,
    InvitationId InvitationId,
    DateTimeOffset ExpiresAtUtc,
    string Package);

internal sealed record RedeemedInvitation(
    CircleId CircleId,
    InvitationId InvitationId,
    RedemptionId RedemptionId);

internal sealed class InvitationRejectedException(InvitationRejectionCode rejectionCode)
    : Exception("The Circle invitation was rejected.")
{
    internal InvitationRejectionCode RejectionCode { get; } = rejectionCode;

    internal string Code => RejectionCode switch
    {
        InvitationRejectionCode.UnsupportedVersion => "unsupported_version",
        InvitationRejectionCode.UnsupportedSuite => "unsupported_suite",
        InvitationRejectionCode.UnauthorizedIssuer => "unauthorized_issuer",
        InvitationRejectionCode.Forged => "forged",
        InvitationRejectionCode.Revoked => "revoked",
        InvitationRejectionCode.StaleAuthorityState => "stale_authority_state",
        InvitationRejectionCode.WrongCircle => "wrong_circle",
        InvitationRejectionCode.NotYetValid => "not_yet_valid",
        InvitationRejectionCode.Expired => "expired",
        InvitationRejectionCode.Replayed => "replayed",
        _ => "malformed",
    };
}
