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
    private static readonly byte[] SignedAdmissionDomain =
        "balls/trusted-circle/signed-admission-request/v1\0"u8.ToArray();
    private static readonly byte[] AdmissionResponseDomain =
        "balls/trusted-circle/admission-response/v1\0"u8.ToArray();

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

    public static byte[] EncodeAdmission(AdmissionRequest request)
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

    public static byte[] DigestAdmission(SignedAdmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var stream = new MemoryStream();
        stream.Write(SignedAdmissionDomain);
        WriteBytes(stream, EncodeAdmission(request.Request));
        WriteBytes(stream, request.MemberSignature);
        WriteBytes(stream, request.NodeSignature);
        return SHA256.HashData(stream.ToArray());
    }

    public static byte[] EncodeResponse(AdmissionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        using var stream = new MemoryStream();
        stream.Write(AdmissionResponseDomain);
        WriteInt32(stream, response.Version);
        WriteText(stream, response.CircleId);
        WriteText(stream, response.InvitationId);
        WriteInt64(stream, response.AuthorityGeneration);
        WriteInt64(stream, response.AuthoritySequence);
        WriteInt32(stream, response.SelectedProtocolVersion);
        WriteText(stream, response.CircleName);
        WriteInt64(stream, response.CircleCreatedAtUtc.ToUnixTimeSeconds());
        WriteText(stream, response.AdmittedMemberId);
        WriteCredential(stream, response.AdmittedMemberCredential);
        WriteText(stream, response.GrantedMemberRole);
        WriteInt32(stream, response.GrantedCapabilities.Count);
        foreach (var capability in response.GrantedCapabilities)
        {
            WriteText(stream, capability);
        }

        WriteText(stream, response.AdmittedNodeId);
        WriteCredential(stream, response.AdmittedNodeCredential);
        WriteTransportBinding(stream, response.AdmittedTransportBinding);
        WriteBytes(stream, response.RequestDigest);
        WriteBytes(stream, response.AnchorChallenge);
        WriteBytes(stream, response.ApplicantChallenge);
        WriteInt32(stream, response.Members.Count);
        foreach (var member in response.Members)
        {
            WriteText(stream, member.MemberId);
            WriteText(stream, member.DisplayName);
            WriteText(stream, member.Role);
            WriteInt64(stream, member.JoinedAtUtc.ToUnixTimeSeconds());
        }

        WriteInt32(stream, response.Nodes.Count);
        foreach (var node in response.Nodes)
        {
            WriteText(stream, node.NodeId);
            WriteText(stream, node.DisplayName);
            WriteInt64(stream, node.JoinedAtUtc.ToUnixTimeSeconds());
            WriteTransportBinding(stream, node.TransportBinding);
        }

        return stream.ToArray();
    }

    public static SignedAdmissionResponse SignResponse(
        AdmissionResponse response,
        PublicKeyCredential anchorCredential,
        ECDsa anchorKey)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(anchorCredential);
        ArgumentNullException.ThrowIfNull(anchorKey);
        var actual = RemoteIdentity.CreateCredential(KeyRole.Anchor, anchorKey);
        if (!CredentialsEqual(actual, anchorCredential))
        {
            throw new ArgumentException(
                "The signing key does not match the delegated Anchor credential.",
                nameof(anchorKey));
        }

        return new SignedAdmissionResponse(
            response,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteIdentity.Sign(EncodeResponse(response), anchorKey));
    }

    public static AdmissionValidationResult ValidateResponse(
        SignedAdmissionResponse signedResponse,
        AdmissionResponseVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(signedResponse);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var response = signedResponse.Response;
            var request = context.Request.Request;
            if (!HasValidResponseShape(signedResponse, context))
            {
                return Rejected(AdmissionRejectionCode.Malformed);
            }

            if (signedResponse.SignatureSuite != RemoteSecurityProtocol.SignatureSuite)
            {
                return Rejected(AdmissionRejectionCode.UnsupportedSuite);
            }

            if (!RemoteIdentity.Verify(
                    EncodeResponse(response),
                    signedResponse.AnchorSignature,
                    context.TrustedAnchorCredential)
                || !CryptographicOperations.FixedTimeEquals(
                    response.RequestDigest,
                    DigestAdmission(context.Request)))
            {
                return Rejected(AdmissionRejectionCode.Forged);
            }

            if (context.RevokedKeyIds.Contains(context.TrustedAnchorCredential.KeyId)
                || context.RevokedKeyIds.Contains(response.AdmittedMemberCredential.KeyId)
                || context.RevokedKeyIds.Contains(response.AdmittedNodeCredential.KeyId)
                || context.RevokedKeyIds.Contains(
                    response.AdmittedTransportBinding.Binding.TransportCredential.KeyId))
            {
                return Rejected(AdmissionRejectionCode.Revoked);
            }

            if (response.AuthorityGeneration < context.MinimumAuthorityGeneration)
            {
                return Rejected(AdmissionRejectionCode.StaleAuthorityState);
            }

            if (response.CircleId != request.CircleId)
            {
                return Rejected(AdmissionRejectionCode.WrongCircle);
            }

            if (response.AdmittedNodeId != request.NodeId
                || !CredentialsEqual(response.AdmittedNodeCredential, request.NodeCredential)
                || !CredentialsEqual(
                    response.AdmittedTransportBinding.Binding.TransportCredential,
                    request.TransportCredential))
            {
                return Rejected(AdmissionRejectionCode.WrongNode);
            }

            if (response.SelectedProtocolVersion != request.SelectedProtocolVersion)
            {
                return Rejected(AdmissionRejectionCode.Downgraded);
            }

            foreach (var node in response.Nodes)
            {
                var binding = NodeTransportSecurity.Validate(
                    node.TransportBinding,
                    new NodeTransportVerificationContext(
                        response.CircleId,
                        node.NodeId,
                        context.TrustedRootCredential,
                        context.NowUtc,
                        context.MinimumAuthorityGeneration,
                        response.SelectedProtocolVersion,
                        response.SelectedProtocolVersion,
                        context.RevokedKeyIds));
                if (!binding.IsValid)
                {
                    return Rejected(binding.RejectionCode switch
                    {
                        NodeTransportRejectionCode.Revoked => AdmissionRejectionCode.Revoked,
                        NodeTransportRejectionCode.StaleAuthorityState =>
                            AdmissionRejectionCode.StaleAuthorityState,
                        NodeTransportRejectionCode.WrongCircle => AdmissionRejectionCode.WrongCircle,
                        NodeTransportRejectionCode.WrongNode => AdmissionRejectionCode.WrongNode,
                        NodeTransportRejectionCode.NotYetValid =>
                            AdmissionRejectionCode.NotYetValid,
                        NodeTransportRejectionCode.Expired => AdmissionRejectionCode.Expired,
                        NodeTransportRejectionCode.Downgraded => AdmissionRejectionCode.Downgraded,
                        _ => AdmissionRejectionCode.Forged,
                    });
                }
            }

            return AdmissionValidationResult.Accepted(response.SelectedProtocolVersion);
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

    private static bool HasValidResponseShape(
        SignedAdmissionResponse signedResponse,
        AdmissionResponseVerificationContext context)
    {
        var response = signedResponse.Response;
        var request = context.Request.Request;
        return response.Version == RemoteSecurityProtocol.Version
            && IsCanonicalUuid(response.CircleId)
            && response.InvitationId == request.InvitationId
            && response.AuthorityGeneration > 0
            && response.AuthoritySequence > 0
            && response.SelectedProtocolVersion > 0
            && IsBoundedText(response.CircleName, 100)
            && IsUtc(response.CircleCreatedAtUtc)
            && response.AdmittedMemberId == request.MemberId
            && CredentialsEqual(response.AdmittedMemberCredential, request.MemberCredential)
            && response.AdmittedMemberCredential.Role == KeyRole.Member
            && response.GrantedMemberRole == "member"
            && response.GrantedCapabilities is { Count: > 0 and <= 32 }
            && response.GrantedCapabilities.SequenceEqual(
                response.GrantedCapabilities.Order(StringComparer.Ordinal))
            && response.GrantedCapabilities.Distinct(StringComparer.Ordinal).Count()
                == response.GrantedCapabilities.Count
            && response.GrantedCapabilities.All(value => IsBoundedText(value, 100))
            && response.AdmittedNodeId == request.NodeId
            && response.AdmittedNodeCredential.Role == KeyRole.Node
            && response.RequestDigest is { Length: 32 }
            && response.AnchorChallenge is { Length: 32 }
            && response.ApplicantChallenge is { Length: 32 }
            && CryptographicOperations.FixedTimeEquals(
                response.AnchorChallenge,
                request.AnchorChallenge)
            && CryptographicOperations.FixedTimeEquals(
                response.ApplicantChallenge,
                request.ApplicantChallenge)
            && response.Members is { Count: > 0 and <= 128 }
            && response.Members.All(member =>
                IsCanonicalUuid(member.MemberId)
                && IsBoundedText(member.DisplayName, 100)
                && member.Role is "owner" or "member"
                && IsUtc(member.JoinedAtUtc))
            && response.Members.Select(member => member.MemberId)
                .SequenceEqual(response.Members.Select(member => member.MemberId)
                    .Order(StringComparer.Ordinal))
            && response.Members.Select(member => member.MemberId).Distinct(StringComparer.Ordinal)
                .Count() == response.Members.Count
            && response.Members.Any(member =>
                member.MemberId == response.AdmittedMemberId && member.Role == "member")
            && response.Nodes is { Count: > 0 and <= 128 }
            && response.Nodes.All(node =>
                IsCanonicalUuid(node.NodeId)
                && IsBoundedText(node.DisplayName, 100)
                && IsUtc(node.JoinedAtUtc)
                && node.TransportBinding.Binding.NodeId == node.NodeId)
            && response.Nodes.Select(node => node.NodeId)
                .SequenceEqual(response.Nodes.Select(node => node.NodeId)
                    .Order(StringComparer.Ordinal))
            && response.Nodes.Select(node => node.NodeId).Distinct(StringComparer.Ordinal).Count()
                == response.Nodes.Count
            && response.Nodes.Any(node => node.NodeId == response.AdmittedNodeId)
            && context.TrustedRootCredential.Role == KeyRole.CircleAuthority
            && RemoteIdentity.IsValidCredential(context.TrustedRootCredential)
            && context.TrustedAnchorCredential.Role == KeyRole.Anchor
            && RemoteIdentity.IsValidCredential(context.TrustedAnchorCredential)
            && signedResponse.AnchorSignature is { Length: 64 }
            && IsUtc(context.NowUtc)
            && context.MinimumAuthorityGeneration > 0;
    }

    private static void WriteTransportBinding(
        Stream stream,
        SignedNodeTransportBinding signedBinding)
    {
        WriteBytes(stream, NodeTransportSecurity.Encode(signedBinding.Binding));
        WriteCredential(stream, signedBinding.AuthorityCredential);
        WriteText(stream, signedBinding.SignatureSuite);
        WriteBytes(stream, signedBinding.AuthoritySignature);
    }

    private static bool IsBoundedText(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(character => character <= 0x7f);

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static bool HasValidShape(
        SignedAdmissionRequest signedRequest,
        AdmissionVerificationContext context)
    {
        var invitation = signedRequest.Invitation.Invitation;
        var request = signedRequest.Request;
        return IsCanonicalUuid(invitation.CircleId)
            && IsCanonicalUuid(invitation.InvitationId)
            && IsCanonicalUuid(invitation.IssuerId)
            && invitation.AnchorTransportKeyId.StartsWith(
                "transport:p256-sha256:",
                StringComparison.Ordinal)
            && invitation.MaximumRedemptions == 1
            && invitation.NotBeforeUtc < invitation.ExpiresAtUtc
            && invitation.InvitationNonce is { Length: 32 }
            && invitation.MinimumProtocolVersion > 0
            && invitation.MaximumProtocolVersion >= invitation.MinimumProtocolVersion
            && IsCanonicalUuid(request.CircleId)
            && string.Equals(request.InvitationId, invitation.InvitationId, StringComparison.Ordinal)
            && IsCanonicalUuid(request.MemberId)
            && IsCanonicalUuid(request.NodeId)
            && request.MinimumProtocolVersion > 0
            && request.MaximumProtocolVersion >= request.MinimumProtocolVersion
            && request.SelectedProtocolVersion > 0
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

    private static bool IsCanonicalUuid(string value) =>
        Guid.TryParseExact(value, "D", out var identifier)
        && string.Equals(identifier.ToString("D"), value, StringComparison.Ordinal);

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
