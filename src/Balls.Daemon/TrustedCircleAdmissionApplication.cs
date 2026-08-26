using System.Security.Cryptography;
using System.Text;
using Balls.Core;
using Balls.Protocol.Remote.V1;

namespace Balls.Daemon;

internal sealed class TrustedCircleAdmissionApplication(
    ILocalStateStore localState,
    IIdentityAuthorityStore identities,
    IInvitationStateStore invitations,
    IAdmissionStateStore admissionState,
    IRemoteTransportConnector connector,
    TimeProvider timeProvider)
{
    private const string AnchorDnsName = "anchor.balls";
    private const string ApplicantDnsName = "node.balls";
    private static readonly IReadOnlySet<string> NoRevokedKeys =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly SemaphoreSlim anchorAdmissionLock = new(1, 1);

    internal async Task<CircleDetails> JoinAsync(
        string encodedPackage,
        RemoteTransportAddress address,
        string memberDisplayName,
        CancellationToken cancellationToken = default) =>
        await JoinAsync(
            encodedPackage,
            address,
            syncAddress: null,
            memberDisplayName,
            cancellationToken).ConfigureAwait(false);

    internal async Task<CircleDetails> JoinWithConnectionAsync(
        string encodedPackage,
        RemoteTransportAddress admissionAddress,
        RemoteTransportAddress syncAddress,
        string memberDisplayName,
        CancellationToken cancellationToken = default) =>
        await JoinAsync(
            encodedPackage,
            admissionAddress,
            syncAddress,
            memberDisplayName,
            cancellationToken).ConfigureAwait(false);

    private async Task<CircleDetails> JoinAsync(
        string encodedPackage,
        RemoteTransportAddress address,
        RemoteTransportAddress? syncAddress,
        string memberDisplayName,
        CancellationToken cancellationToken = default)
    {
        ValidateDisplayName(memberDisplayName, "member_display_name");
        var packageBytes = EncodePackage(encodedPackage);
        var package = DecodeAndValidatePackage(packageBytes);
        var invitation = package.Invitation.Invitation;
        var circleId = ParseCircleId(invitation.CircleId);
        var invitationId = ParseInvitationId(invitation.InvitationId);
        var now = Now();
        var connectionState = syncAddress is null
            ? null
            : new CircleConnectionState(
                circleId,
                1,
                address.Provider,
                address.Value,
                syncAddress.Value,
                now);
        var validation = InvitationSecurity.Validate(
            package,
            new InvitationVerificationContext(
                invitation.CircleId,
                package.RootCredential,
                now,
                invitation.AuthorityGeneration,
                InvitationUseState.Available,
                NoRevokedKeys));
        if (!validation.IsValid)
        {
            throw Rejected(validation.RejectionCode);
        }

        var applicant = await admissionState.PrepareAdmissionApplicantAsync(
            invitationId,
            circleId,
            SHA256.HashData(packageBytes),
            memberDisplayName.Trim(),
            now,
            cancellationToken).ConfigureAwait(false);
        if (applicant.IsCompleted)
        {
            if (connectionState is not null)
            {
                await admissionState.StoreCircleConnectionAsync(connectionState, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await localState.GetCircleAsync(circleId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new LocalStateException(
                    "joined_circle_missing",
                    "The completed Circle admission is missing local membership state.");
        }

        var node = await localState.GetNodeAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "node_identity_missing",
                "The local Node identity is missing.");
        ValidateDisplayName(node.DisplayName, "node_display_name");
        var nodeIdentity = await identities.GetNodeCryptographicIdentityAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "node_identity_missing",
                "The local Node cryptographic identity is missing.");
        var transport = await identities.GetLocalTransportIdentityAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "transport_identity_missing",
                "The local transport identity is missing.");
        using var certificate = await identities.CreateTransportCertificateAsync(
            ApplicantDnsName,
            now,
            cancellationToken).ConfigureAwait(false);
        await using var connection = await connector.ConnectAsync(address, cancellationToken)
            .ConfigureAwait(false);
        await using var channel = await RemoteAdmissionChannel.ConnectAsync(
            connection,
            AnchorDnsName,
            invitation.AnchorTransportKeyId,
            certificate,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var helloOperation = Guid.CreateVersion7();
        await channel.WriteAsync(
            new RemoteFrame(
                helloOperation,
                AdmissionWireCodec.EncodeHello(
                    new AdmissionHello(package, memberDisplayName.Trim(), node.DisplayName.Trim()))),
            cancellationToken).ConfigureAwait(false);
        var challengeFrame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfRejected(challengeFrame.Payload);
        var challenge = AdmissionWireCodec.DecodeChallenge(challengeFrame.Payload);
        var request = new AdmissionRequest(
            invitation.CircleId,
            invitation.InvitationId,
            applicant.MemberId.ToString(),
            IdentityProtocolMapping.ToProtocol(applicant.MemberCredential),
            node.Id.ToString(),
            IdentityProtocolMapping.ToProtocol(nodeIdentity.Credential),
            IdentityProtocolMapping.ToProtocol(transport.Credential),
            RemoteSecurityProtocol.Version,
            RemoteSecurityProtocol.Version,
            RemoteSecurityProtocol.Version,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteSecurityProtocol.Alpn,
            AdmissionSecurity.DigestInvitation(package.Invitation),
            challenge.AnchorChallenge,
            applicant.ApplicantChallenge);
        var transcript = AdmissionSecurity.EncodeAdmission(request);
        var signedRequest = new SignedAdmissionRequest(
            package.Invitation,
            request,
            await admissionState.SignWithAdmissionMemberAsync(
                invitationId,
                transcript,
                cancellationToken).ConfigureAwait(false),
            await identities.SignWithNodeAsync(transcript, cancellationToken).ConfigureAwait(false));
        var requestOperation = Guid.CreateVersion7();
        await channel.WriteAsync(
            new RemoteFrame(
                requestOperation,
                AdmissionWireCodec.EncodeRequest(
                    new AdmissionRequestEnvelope(
                        signedRequest,
                        memberDisplayName.Trim(),
                        node.DisplayName.Trim()))),
            cancellationToken).ConfigureAwait(false);
        var responseFrame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfRejected(responseFrame.Payload);
        var signedResponse = AdmissionWireCodec.DecodeResponse(responseFrame.Payload).Response;
        var responseValidation = AdmissionSecurity.ValidateResponse(
            signedResponse,
            new AdmissionResponseVerificationContext(
                signedRequest,
                package.RootCredential,
                package.IssuerDelegation.Delegation.IssuerCredential,
                Now(),
                invitation.AuthorityGeneration,
                NoRevokedKeys));
        if (!responseValidation.IsAccepted)
        {
            throw Rejected(responseValidation.RejectionCode);
        }

        var joined = ToJoinedCommit(
            invitationId,
            SHA256.HashData(packageBytes),
            applicant,
            signedResponse,
            responseFrame.Payload,
            package.RootCredential,
            package.IssuerDelegation.Delegation.IssuerCredential,
            package.Invitation.Invitation.IssuerId,
            connectionState,
            Now());
        await admissionState.CommitJoinedCircleAsync(joined, cancellationToken)
            .ConfigureAwait(false);
        return await localState.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "joined_circle_missing",
                "The accepted Circle membership was not persisted.");
    }

    internal async Task HandleAsync(
        UntrustedRemoteConnection connection,
        CancellationToken cancellationToken = default)
    {
        using var serverCertificate = await identities.CreateTransportCertificateAsync(
            AnchorDnsName,
            Now(),
            cancellationToken).ConfigureAwait(false);
        await using var channel = await RemoteAdmissionChannel.AcceptAsync(
            connection,
            serverCertificate,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var helloFrame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        var responseOperation = helloFrame.OperationId;
        CircleId? auditCircleId = null;
        try
        {
            var hello = AdmissionWireCodec.DecodeHello(helloFrame.Payload);
            ValidateDisplayName(hello.MemberDisplayName, "member_display_name");
            ValidateDisplayName(hello.NodeDisplayName, "node_display_name");
            var invitation = hello.Package.Invitation.Invitation;
            var invitationId = ParseInvitationId(invitation.InvitationId);
            var circleId = ParseCircleId(invitation.CircleId);
            auditCircleId = circleId;
            var stored = await invitations.GetCircleInvitationAsync(invitationId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw Rejected(AdmissionRejectionCode.Malformed);
            var packageBytes = InvitationPackageCodec.Encode(hello.Package);
            if (stored.CircleId != circleId
                || !CryptographicOperations.FixedTimeEquals(
                    stored.PackageSha256,
                    SHA256.HashData(packageBytes)))
            {
                throw Rejected(AdmissionRejectionCode.Forged);
            }

            var trust = await admissionState.GetCircleTrustAsync(circleId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw Rejected(AdmissionRejectionCode.UnauthorizedIssuer);
            var packageValidation = InvitationSecurity.Validate(
                hello.Package,
                new InvitationVerificationContext(
                    circleId.ToString(),
                    IdentityProtocolMapping.ToProtocol(trust.RootCredential),
                    Now(),
                    trust.AuthorityGeneration,
                    InvitationUseState.Available,
                    NoRevokedKeys));
            if (!packageValidation.IsValid)
            {
                throw Rejected(packageValidation.RejectionCode);
            }

            var challenge = await admissionState.GetOrCreateAdmissionChallengeAsync(
                invitationId,
                cancellationToken).ConfigureAwait(false);
            await channel.WriteAsync(
                new RemoteFrame(
                    helloFrame.OperationId,
                    AdmissionWireCodec.EncodeChallenge(new AdmissionChallenge(challenge))),
                cancellationToken).ConfigureAwait(false);
            var requestFrame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
            responseOperation = requestFrame.OperationId;
            var envelope = AdmissionWireCodec.DecodeRequest(requestFrame.Payload);
            if (envelope.MemberDisplayName != hello.MemberDisplayName
                || envelope.NodeDisplayName != hello.NodeDisplayName)
            {
                throw Rejected(AdmissionRejectionCode.Forged);
            }

            var localNode = await localState.GetNodeAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new LocalStateException(
                    "node_identity_missing",
                    "The local Node identity is missing.");
            var requestValidation = AdmissionSecurity.Validate(
                envelope.Request,
                new AdmissionVerificationContext(
                    circleId.ToString(),
                    localNode.Id.ToString(),
                    IdentityProtocolMapping.ToProtocol(trust.AnchorCredential),
                    channel.PeerTransportCredential,
                    challenge,
                    Now(),
                    InvitationUseState.Available,
                    RemoteSecurityProtocol.Version,
                    RemoteSecurityProtocol.Version,
                    trust.AuthorityGeneration,
                    NoRevokedKeys));
            if (!requestValidation.IsAccepted)
            {
                throw Rejected(requestValidation.RejectionCode);
            }

            await anchorAdmissionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var requestDigest = AdmissionSecurity.DigestAdmission(envelope.Request);
                var prior = await admissionState.GetAnchorAdmissionResultAsync(
                    invitationId,
                    requestDigest,
                    cancellationToken).ConfigureAwait(false);
                if (prior is not null)
                {
                    if (prior.Status == AnchorAdmissionCommitStatus.IdempotentRetry
                        && prior.EncodedResponse is not null)
                    {
                        await channel.WriteAsync(
                            new RemoteFrame(requestFrame.OperationId, prior.EncodedResponse),
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    throw Rejected(AdmissionRejectionCode.Replayed);
                }

                var encodedResponse = await AdmitAsync(
                    circleId,
                    invitationId,
                    stored,
                    trust,
                    localNode,
                    envelope,
                    requestDigest,
                    cancellationToken).ConfigureAwait(false);
                await channel.WriteAsync(
                    new RemoteFrame(requestFrame.OperationId, encodedResponse),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                anchorAdmissionLock.Release();
            }
        }
        catch (AdmissionRejectedException exception)
        {
            if (auditCircleId is not null)
            {
                await admissionState.RecordAdmissionAuditAsync(
                    auditCircleId.Value,
                    exception.Code,
                    Now(),
                    cancellationToken).ConfigureAwait(false);
            }

            await channel.WriteAsync(
                new RemoteFrame(
                    responseOperation,
                    AdmissionWireCodec.EncodeRejection(new AdmissionRejection(exception.Code))),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InputValidationException)
        {
            if (auditCircleId is not null)
            {
                await admissionState.RecordAdmissionAuditAsync(
                    auditCircleId.Value,
                    "malformed",
                    Now(),
                    cancellationToken).ConfigureAwait(false);
            }

            await channel.WriteAsync(
                new RemoteFrame(
                    responseOperation,
                    AdmissionWireCodec.EncodeRejection(new AdmissionRejection("malformed"))),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvitationPackageException)
        {
            if (auditCircleId is not null)
            {
                await admissionState.RecordAdmissionAuditAsync(
                    auditCircleId.Value,
                    "malformed",
                    Now(),
                    cancellationToken).ConfigureAwait(false);
            }

            await channel.WriteAsync(
                new RemoteFrame(
                    responseOperation,
                    AdmissionWireCodec.EncodeRejection(new AdmissionRejection("malformed"))),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<byte[]> AdmitAsync(
        CircleId circleId,
        InvitationId invitationId,
        PersistedCircleInvitation stored,
        CircleTrustState trust,
        NodeIdentity localNode,
        AdmissionRequestEnvelope envelope,
        byte[] requestDigest,
        CancellationToken cancellationToken)
    {
        var now = Now();
        var request = envelope.Request.Request;
        var nodeIdentity = await identities.GetNodeCryptographicIdentityAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "node_identity_missing",
                "The local Node cryptographic identity is missing.");
        var transport = await identities.GetLocalTransportIdentityAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "transport_identity_missing",
                "The local transport identity is missing.");
        var root = IdentityProtocolMapping.ToProtocol(trust.RootCredential);
        var anchorSecurity = await GetOrCreateAnchorSecurityAsync(
            circleId,
            trust,
            localNode,
            nodeIdentity.Credential,
            transport.Credential,
            root,
            now,
            cancellationToken).ConfigureAwait(false);
        var admittedBinding = await SignBindingAsync(
            circleId,
            new NodeId(Guid.Parse(request.NodeId)),
            request.TransportCredential,
            trust,
            root,
            now,
            cancellationToken).ConfigureAwait(false);
        var admittedSecurity = new CircleNodeSecurityState(
            circleId,
            new NodeId(Guid.Parse(request.NodeId)),
            IdentityProtocolMapping.ToCore(request.NodeCredential),
            IdentityProtocolMapping.ToCore(request.TransportCredential),
            NodeTransportBindingCodec.Encode(admittedBinding));
        var details = await localState.GetCircleAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_not_found",
                "The requested Circle is not known to this Node.");
        var member = new Member(
            new MemberId(Guid.Parse(request.MemberId)),
            circleId,
            envelope.MemberDisplayName,
            MemberRole.Member,
            now);
        var node = new CircleNode(
            circleId,
            new NodeId(Guid.Parse(request.NodeId)),
            envelope.NodeDisplayName,
            now);
        var members = details.Members.Append(member)
            .OrderBy(value => value.Id.ToString(), StringComparer.Ordinal)
            .Select(value => new AdmissionMemberSnapshot(
                value.Id.ToString(),
                value.DisplayName,
                value.Role == MemberRole.Owner ? "owner" : "member",
                value.JoinedAtUtc))
            .ToArray();
        var security = (await admissionState.ListCircleNodeSecurityAsync(
                circleId,
                cancellationToken).ConfigureAwait(false))
            .Where(value => value.NodeId != admittedSecurity.NodeId)
            .Append(admittedSecurity)
            .ToDictionary(value => value.NodeId);
        security[anchorSecurity.NodeId] = anchorSecurity;
        var nodes = details.Nodes.Append(node)
            .OrderBy(value => value.NodeId.ToString(), StringComparer.Ordinal)
            .Select(value =>
            {
                var state = security[value.NodeId];
                return new AdmissionNodeSnapshot(
                    value.NodeId.ToString(),
                    value.DisplayName,
                    value.JoinedAtUtc,
                    IdentityProtocolMapping.ToProtocol(state.NodeCredential),
                    NodeTransportBindingCodec.Decode(state.SignedTransportBinding));
            })
            .ToArray();
        var sequence = await admissionState.ReserveAuthoritySequenceAsync(
            circleId,
            cancellationToken).ConfigureAwait(false);
        var response = new AdmissionResponse(
            RemoteSecurityProtocol.Version,
            circleId.ToString(),
            invitationId.ToString(),
            trust.AuthorityGeneration,
            sequence,
            RemoteSecurityProtocol.Version,
            details.Circle.Name,
            details.Circle.CreatedAtUtc,
            request.MemberId,
            request.MemberCredential,
            "member",
            ["circle.read"],
            request.NodeId,
            request.NodeCredential,
            admittedBinding,
            requestDigest,
            request.AnchorChallenge,
            request.ApplicantChallenge,
            members,
            nodes);
        var signed = new SignedAdmissionResponse(
            response,
            RemoteSecurityProtocol.SignatureSuite,
            await identities.SignWithCircleAnchorAsync(
                circleId,
                AdmissionSecurity.EncodeResponse(response),
                cancellationToken).ConfigureAwait(false));
        var encodedResponse = AdmissionWireCodec.EncodeResponse(new AdmissionResponseEnvelope(signed));
        var commit = await admissionState.CommitAnchorAdmissionAsync(
            new AnchorAdmissionCommit(
                invitationId,
                circleId,
                stored.PackageSha256,
                requestDigest,
                encodedResponse,
                member,
                node,
                IdentityProtocolMapping.ToCore(request.MemberCredential),
                admittedSecurity.NodeCredential,
                admittedSecurity.TransportCredential,
                admittedSecurity.SignedTransportBinding,
                sequence,
                now),
            cancellationToken).ConfigureAwait(false);
        return commit.Status switch
        {
            AnchorAdmissionCommitStatus.Accepted or AnchorAdmissionCommitStatus.IdempotentRetry
                when commit.EncodedResponse is not null => commit.EncodedResponse,
            AnchorAdmissionCommitStatus.Revoked => throw Rejected(AdmissionRejectionCode.Revoked),
            AnchorAdmissionCommitStatus.Expired => throw Rejected(AdmissionRejectionCode.Expired),
            _ => throw Rejected(AdmissionRejectionCode.Replayed),
        };
    }

    private async Task<CircleNodeSecurityState> GetOrCreateAnchorSecurityAsync(
        CircleId circleId,
        CircleTrustState trust,
        NodeIdentity localNode,
        PublicIdentityCredential nodeCredential,
        PublicIdentityCredential transportCredential,
        PublicKeyCredential root,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = (await admissionState.ListCircleNodeSecurityAsync(
                circleId,
                cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(value => value.NodeId == localNode.Id);
        if (existing is not null)
        {
            var binding = NodeTransportBindingCodec.Decode(existing.SignedTransportBinding);
            var validation = NodeTransportSecurity.Validate(
                binding,
                new NodeTransportVerificationContext(
                    circleId.ToString(),
                    localNode.Id.ToString(),
                    root,
                    now,
                    trust.AuthorityGeneration,
                    RemoteSecurityProtocol.Version,
                    RemoteSecurityProtocol.Version,
                    NoRevokedKeys));
            if (!validation.IsValid
                || existing.NodeCredential.KeyId != nodeCredential.KeyId
                || existing.TransportCredential.KeyId != transportCredential.KeyId)
            {
                throw new LocalStateConflictException(
                    "anchor_transport_binding_invalid",
                    "The Anchor Node transport binding is missing, stale, or inconsistent.");
            }

            return existing;
        }

        var signed = await SignBindingAsync(
            circleId,
            localNode.Id,
            IdentityProtocolMapping.ToProtocol(transportCredential),
            trust,
            root,
            now,
            cancellationToken).ConfigureAwait(false);
        var state = new CircleNodeSecurityState(
            circleId,
            localNode.Id,
            nodeCredential,
            transportCredential,
            NodeTransportBindingCodec.Encode(signed));
        await admissionState.StoreCircleNodeSecurityAsync(state, cancellationToken)
            .ConfigureAwait(false);
        return state;
    }

    private async Task<SignedNodeTransportBinding> SignBindingAsync(
        CircleId circleId,
        NodeId nodeId,
        PublicKeyCredential transportCredential,
        CircleTrustState trust,
        PublicKeyCredential root,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var binding = new NodeTransportBinding(
            RemoteSecurityProtocol.Version,
            circleId.ToString(),
            nodeId.ToString(),
            trust.AuthorityGeneration,
            transportCredential,
            now.AddMinutes(-5),
            now.AddDays(365),
            RemoteSecurityProtocol.Version,
            RemoteSecurityProtocol.Version);
        return new SignedNodeTransportBinding(
            binding,
            root,
            RemoteSecurityProtocol.SignatureSuite,
            await identities.SignWithCircleAuthorityAsync(
                circleId,
                NodeTransportSecurity.Encode(binding),
                cancellationToken).ConfigureAwait(false));
    }

    private static JoinedCircleCommit ToJoinedCommit(
        InvitationId invitationId,
        byte[] packageSha256,
        AdmissionApplicantState applicant,
        SignedAdmissionResponse signed,
        byte[] encodedResponse,
        PublicKeyCredential rootCredential,
        PublicKeyCredential anchorCredential,
        string issuerNodeId,
        CircleConnectionState? connection,
        DateTimeOffset joinedAtUtc)
    {
        var response = signed.Response;
        var circleId = new CircleId(Guid.Parse(response.CircleId));
        var details = new CircleDetails(
            new Circle(circleId, response.CircleName, response.CircleCreatedAtUtc),
            response.Members.Select(value => new Member(
                new MemberId(Guid.Parse(value.MemberId)),
                circleId,
                value.DisplayName,
                value.Role == "owner" ? MemberRole.Owner : MemberRole.Member,
                value.JoinedAtUtc)).ToArray(),
            response.Nodes.Select(value => new CircleNode(
                circleId,
                new NodeId(Guid.Parse(value.NodeId)),
                value.DisplayName,
                value.JoinedAtUtc)).ToArray());
        var security = response.Nodes.Select(value => new CircleNodeSecurityState(
            circleId,
            new NodeId(Guid.Parse(value.NodeId)),
            IdentityProtocolMapping.ToCore(value.NodeCredential),
            IdentityProtocolMapping.ToCore(value.TransportBinding.Binding.TransportCredential),
            NodeTransportBindingCodec.Encode(value.TransportBinding))).ToArray();
        return new JoinedCircleCommit(
            invitationId,
            packageSha256,
            details,
            new CircleTrustState(
                circleId,
                response.AuthorityGeneration,
                response.AuthoritySequence,
                new NodeId(Guid.Parse(issuerNodeId)),
                IdentityProtocolMapping.ToCore(rootCredential),
                IdentityProtocolMapping.ToCore(anchorCredential),
                encodedResponse),
            applicant.MemberCredential,
            security,
            connection,
            joinedAtUtc);
    }

    private CircleInvitationPackage DecodeAndValidatePackage(byte[] packageBytes)
    {
        try
        {
            return InvitationPackageCodec.Decode(packageBytes);
        }
        catch (InvitationPackageException)
        {
            throw Rejected(AdmissionRejectionCode.Malformed);
        }
    }

    private static byte[] EncodePackage(string encodedPackage)
    {
        if (string.IsNullOrEmpty(encodedPackage))
        {
            throw Rejected(AdmissionRejectionCode.Malformed);
        }

        try
        {
            return new UTF8Encoding(false, true).GetBytes(encodedPackage);
        }
        catch (EncoderFallbackException)
        {
            throw Rejected(AdmissionRejectionCode.Malformed);
        }
    }

    private static void ThrowIfRejected(byte[] payload)
    {
        if (AdmissionWireCodec.ReadKind(payload) == AdmissionWireKind.Rejection)
        {
            throw new AdmissionRejectedException(
                AdmissionWireCodec.DecodeRejection(payload).Code);
        }
    }

    private static CircleId ParseCircleId(string value) =>
        Guid.TryParseExact(value, "D", out var id)
            ? new CircleId(id)
            : throw Rejected(AdmissionRejectionCode.Malformed);

    private static InvitationId ParseInvitationId(string value) =>
        Guid.TryParseExact(value, "D", out var id)
            ? new InvitationId(id)
            : throw Rejected(AdmissionRejectionCode.Malformed);

    private static void ValidateDisplayName(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim().Length > 100
            || value.Trim().Any(character => character > 0x7f))
        {
            throw new InputValidationException(code, "Admission display names must be bounded ASCII text.");
        }
    }

    private DateTimeOffset Now() => IdentityProtocolMapping.ToProtocolSecond(timeProvider.GetUtcNow());

    private static AdmissionRejectedException Rejected(InvitationRejectionCode code) =>
        new(code.ToString().ToLowerInvariant());

    private static AdmissionRejectedException Rejected(AdmissionRejectionCode code) =>
        new(code.ToString().ToLowerInvariant());
}

internal sealed class AdmissionRejectedException(string code)
    : Exception("The Circle admission was rejected.")
{
    internal string Code { get; } = Normalize(code);

    private static string Normalize(string value) => value switch
    {
        "unsupportedversion" => "unsupported_version",
        "unsupportedsuite" => "unsupported_suite",
        "unauthorizedissuer" => "unauthorized_issuer",
        "staleauthoritystate" => "stale_authority_state",
        "wrongcircle" => "wrong_circle",
        "wrongnode" => "wrong_node",
        "notyetvalid" => "not_yet_valid",
        _ when value is "malformed" or "forged" or "revoked" or "expired" or "replayed"
            or "downgraded" => value,
        _ => "malformed",
    };
}
