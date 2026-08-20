using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Balls.Protocol.Remote.V1;

public static class AdmissionSecurity
{
    private static readonly byte[] InvitationDomain =
        "balls/trusted-circle/invitation/v1\0"u8.ToArray();
    private static readonly byte[] SignedInvitationDomain =
        "balls/trusted-circle/signed-invitation/v1\0"u8.ToArray();
    private static readonly byte[] AdmissionDomain =
        "balls/trusted-circle/admission-request/v1\0"u8.ToArray();

    public static SignedCircleInvitation SignInvitation(
        CircleInvitation invitation,
        ECDsa issuerKey)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(issuerKey);
        var role = invitation.IssuerKeyId.StartsWith("circle-authority:", StringComparison.Ordinal)
            ? KeyRole.CircleAuthority
            : KeyRole.Anchor;
        var credential = RemoteIdentity.CreateCredential(role, issuerKey);
        if (!string.Equals(credential.KeyId, invitation.IssuerKeyId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The invitation issuer key does not match its issuer key identifier.",
                nameof(issuerKey));
        }

        return new SignedCircleInvitation(
            invitation,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteIdentity.Sign(EncodeInvitation(invitation), issuerKey));
    }

    public static SignedAdmissionRequest SignAdmission(
        SignedCircleInvitation invitation,
        AdmissionRequest request,
        ECDsa memberKey,
        ECDsa nodeKey)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(request);
        EnsureSigningKey(request.MemberCredential, KeyRole.Member, memberKey);
        EnsureSigningKey(request.NodeCredential, KeyRole.Node, nodeKey);
        var transcript = EncodeAdmission(request);
        return new SignedAdmissionRequest(
            invitation,
            request,
            RemoteIdentity.Sign(transcript, memberKey),
            RemoteIdentity.Sign(transcript, nodeKey));
    }

    public static byte[] EncodeInvitation(CircleInvitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        using var stream = new MemoryStream();
        stream.Write(InvitationDomain);
        WriteInt32(stream, RemoteSecurityProtocol.Version);
        WriteText(stream, invitation.CircleId);
        WriteText(stream, invitation.InvitationId);
        WriteText(stream, invitation.IssuerId);
        WriteText(stream, invitation.IssuerKeyId);
        WriteText(stream, invitation.AnchorTransportKeyId);
        WriteInt64(stream, invitation.AuthorityGeneration);
        WriteInt64(stream, invitation.NotBeforeUtc.ToUnixTimeSeconds());
        WriteInt64(stream, invitation.ExpiresAtUtc.ToUnixTimeSeconds());
        WriteInt32(stream, invitation.MaximumRedemptions);
        WriteInt32(stream, invitation.MinimumProtocolVersion);
        WriteInt32(stream, invitation.MaximumProtocolVersion);
        WriteBytes(stream, invitation.InvitationNonce);
        return stream.ToArray();
    }

    public static byte[] DigestInvitation(SignedCircleInvitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        using var stream = new MemoryStream();
        stream.Write(SignedInvitationDomain);
        WriteBytes(stream, EncodeInvitation(invitation.Invitation));
        WriteText(stream, invitation.SignatureSuite);
        WriteBytes(stream, invitation.IssuerSignature);
        return SHA256.HashData(stream.ToArray());
    }

    public static AdmissionValidationResult Validate(
        SignedAdmissionRequest signedRequest,
        AdmissionVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(signedRequest);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var invitation = signedRequest.Invitation.Invitation;
            var request = signedRequest.Request;
            if (!HasValidShape(signedRequest, context))
            {
                return Rejected(AdmissionRejectionCode.Malformed);
            }

            if (signedRequest.Invitation.SignatureSuite != RemoteSecurityProtocol.SignatureSuite
                || request.SignatureSuite != RemoteSecurityProtocol.SignatureSuite
                || request.Alpn != RemoteSecurityProtocol.Alpn)
            {
                return Rejected(AdmissionRejectionCode.UnsupportedSuite);
            }

            if (!string.Equals(invitation.IssuerId, context.TrustedIssuerId, StringComparison.Ordinal)
                || !string.Equals(
                    invitation.IssuerKeyId,
                    context.TrustedIssuerCredential.KeyId,
                    StringComparison.Ordinal)
                || context.TrustedIssuerCredential.Role is not
                    (KeyRole.Anchor or KeyRole.CircleAuthority))
            {
                return Rejected(AdmissionRejectionCode.UnauthorizedIssuer);
            }

            var admissionBytes = EncodeAdmission(request);
            if (!RemoteIdentity.Verify(
                    EncodeInvitation(invitation),
                    signedRequest.Invitation.IssuerSignature,
                    context.TrustedIssuerCredential)
                || !RemoteIdentity.Verify(
                    admissionBytes,
                    signedRequest.MemberSignature,
                    request.MemberCredential)
                || !RemoteIdentity.Verify(
                    admissionBytes,
                    signedRequest.NodeSignature,
                    request.NodeCredential)
                || !CryptographicOperations.FixedTimeEquals(
                    DigestInvitation(signedRequest.Invitation),
                    request.InvitationDigest))
            {
                return Rejected(AdmissionRejectionCode.Forged);
            }

            if (context.RevokedKeyIds.Contains(invitation.IssuerKeyId)
                || context.RevokedKeyIds.Contains(request.MemberCredential.KeyId)
                || context.RevokedKeyIds.Contains(request.NodeCredential.KeyId)
                || context.RevokedKeyIds.Contains(request.TransportCredential.KeyId)
                || context.InvitationState == InvitationUseState.Revoked)
            {
                return Rejected(AdmissionRejectionCode.Revoked);
            }

            if (invitation.AuthorityGeneration < context.MinimumAuthorityGeneration)
            {
                return Rejected(AdmissionRejectionCode.StaleAuthorityState);
            }

            if (!string.Equals(request.CircleId, invitation.CircleId, StringComparison.Ordinal)
                || !string.Equals(request.CircleId, context.ExpectedCircleId, StringComparison.Ordinal))
            {
                return Rejected(AdmissionRejectionCode.WrongCircle);
            }

            if (!CredentialsEqual(request.TransportCredential, context.PeerTransportCredential))
            {
                return Rejected(AdmissionRejectionCode.WrongNode);
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    request.AnchorChallenge,
                    context.ExpectedAnchorChallenge))
            {
                return Rejected(AdmissionRejectionCode.Forged);
            }

            if (context.NowUtc < invitation.NotBeforeUtc)
            {
                return Rejected(AdmissionRejectionCode.NotYetValid);
            }

            if (context.NowUtc >= invitation.ExpiresAtUtc)
            {
                return Rejected(AdmissionRejectionCode.Expired);
            }

            if (context.InvitationState == InvitationUseState.Consumed)
            {
                return Rejected(AdmissionRejectionCode.Replayed);
            }

            var negotiatedVersion = HighestCommonVersion(invitation, request, context);
            if (negotiatedVersion is null || request.SelectedProtocolVersion != negotiatedVersion)
            {
                return Rejected(AdmissionRejectionCode.Downgraded);
            }

            return AdmissionValidationResult.Accepted(negotiatedVersion.Value);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            CryptographicException or
            InvalidOperationException or
            OverflowException)
        {
            return Rejected(AdmissionRejectionCode.Malformed);
        }
    }

    private static byte[] EncodeAdmission(AdmissionRequest request)
    {
        using var stream = new MemoryStream();
        stream.Write(AdmissionDomain);
        WriteInt32(stream, RemoteSecurityProtocol.Version);
        WriteText(stream, request.CircleId);
        WriteText(stream, request.InvitationId);
        WriteText(stream, request.MemberId);
        WriteCredential(stream, request.MemberCredential);
        WriteText(stream, request.NodeId);
        WriteCredential(stream, request.NodeCredential);
        WriteCredential(stream, request.TransportCredential);
        WriteInt32(stream, request.MinimumProtocolVersion);
        WriteInt32(stream, request.MaximumProtocolVersion);
        WriteInt32(stream, request.SelectedProtocolVersion);
        WriteText(stream, request.SignatureSuite);
        WriteText(stream, request.Alpn);
        WriteBytes(stream, request.InvitationDigest);
        WriteBytes(stream, request.AnchorChallenge);
        WriteBytes(stream, request.ApplicantChallenge);
        return stream.ToArray();
    }

    private static bool HasValidShape(
        SignedAdmissionRequest signedRequest,
        AdmissionVerificationContext context)
    {
        var invitation = signedRequest.Invitation.Invitation;
        var request = signedRequest.Request;
        return !string.IsNullOrWhiteSpace(invitation.CircleId)
            && !string.IsNullOrWhiteSpace(invitation.InvitationId)
            && !string.IsNullOrWhiteSpace(invitation.IssuerId)
            && invitation.AnchorTransportKeyId.StartsWith(
                "transport:p256-sha256:",
                StringComparison.Ordinal)
            && invitation.MaximumRedemptions == 1
            && invitation.NotBeforeUtc < invitation.ExpiresAtUtc
            && invitation.InvitationNonce is { Length: 32 }
            && invitation.MinimumProtocolVersion > 0
            && invitation.MaximumProtocolVersion >= invitation.MinimumProtocolVersion
            && !string.IsNullOrWhiteSpace(request.CircleId)
            && string.Equals(request.InvitationId, invitation.InvitationId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(request.MemberId)
            && !string.IsNullOrWhiteSpace(request.NodeId)
            && request.InvitationDigest is { Length: 32 }
            && request.AnchorChallenge is { Length: 32 }
            && request.ApplicantChallenge is { Length: 32 }
            && signedRequest.Invitation.IssuerSignature is { Length: 64 }
            && signedRequest.MemberSignature is { Length: 64 }
            && signedRequest.NodeSignature is { Length: 64 }
            && RemoteIdentity.IsValidCredential(context.TrustedIssuerCredential)
            && RemoteIdentity.IsValidCredential(request.MemberCredential)
            && request.MemberCredential.Role == KeyRole.Member
            && RemoteIdentity.IsValidCredential(request.NodeCredential)
            && request.NodeCredential.Role == KeyRole.Node
            && RemoteIdentity.IsValidCredential(request.TransportCredential)
            && request.TransportCredential.Role == KeyRole.Transport
            && RemoteIdentity.IsValidCredential(context.PeerTransportCredential)
            && context.PeerTransportCredential.Role == KeyRole.Transport
            && context.ExpectedAnchorChallenge is { Length: 32 }
            && context.SupportedMinimumProtocolVersion > 0
            && context.SupportedMaximumProtocolVersion >= context.SupportedMinimumProtocolVersion;
    }

    private static int? HighestCommonVersion(
        CircleInvitation invitation,
        AdmissionRequest request,
        AdmissionVerificationContext context)
    {
        var minimum = Math.Max(
            invitation.MinimumProtocolVersion,
            Math.Max(request.MinimumProtocolVersion, context.SupportedMinimumProtocolVersion));
        var maximum = Math.Min(
            invitation.MaximumProtocolVersion,
            Math.Min(request.MaximumProtocolVersion, context.SupportedMaximumProtocolVersion));
        return maximum >= minimum ? maximum : null;
    }

    private static bool CredentialsEqual(
        PublicKeyCredential left,
        PublicKeyCredential right) =>
        left.Role == right.Role
        && string.Equals(left.Algorithm, right.Algorithm, StringComparison.Ordinal)
        && string.Equals(left.KeyId, right.KeyId, StringComparison.Ordinal)
        && CryptographicOperations.FixedTimeEquals(
            left.SubjectPublicKeyInfo,
            right.SubjectPublicKeyInfo);

    private static void EnsureSigningKey(
        PublicKeyCredential credential,
        KeyRole expectedRole,
        ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var actual = RemoteIdentity.CreateCredential(expectedRole, key);
        if (credential.Role != expectedRole || !CredentialsEqual(credential, actual))
        {
            throw new ArgumentException($"The {expectedRole} signing key does not match its credential.");
        }
    }

    private static AdmissionValidationResult Rejected(AdmissionRejectionCode code) =>
        AdmissionValidationResult.Rejected(code);

    private static void WriteCredential(Stream stream, PublicKeyCredential credential)
    {
        WriteInt32(stream, (int)credential.Role);
        WriteText(stream, credential.Algorithm);
        WriteText(stream, credential.KeyId);
        WriteBytes(stream, credential.SubjectPublicKeyInfo);
    }

    private static void WriteText(Stream stream, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Length > 1024 || value.Any(character => character > 0x7f))
        {
            throw new ArgumentException("Signed text must be non-empty bounded ASCII.", nameof(value));
        }

        WriteBytes(stream, Encoding.ASCII.GetBytes(value));
    }

    private static void WriteBytes(Stream stream, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 16 * 1024)
        {
            throw new ArgumentException("Signed byte strings must be bounded.", nameof(value));
        }

        WriteInt32(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
