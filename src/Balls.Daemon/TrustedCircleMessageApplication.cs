using System.Security.Cryptography;
using Balls.Core;
using Balls.Protocol.Remote.V1;

namespace Balls.Daemon;

internal sealed class TrustedCircleMessageApplication(
    ILocalStateStore localState,
    IIdentityAuthorityStore identities,
    IAdmissionStateStore admissionState,
    IMessageStateStore messages,
    IRemoteTransportConnector connector,
    TimeProvider timeProvider)
{
    private const string AnchorDnsName = "anchor.balls";
    private const string NodeDnsName = "node.balls";
    private static readonly IReadOnlySet<string> NoRevokedKeys =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly SemaphoreSlim authoritativeCommitLock = new(1, 1);

    internal async Task<CircleMessage> SendAsync(
        CircleId circleId,
        MessageId messageId,
        string text,
        RemoteTransportAddress address,
        CancellationToken cancellationToken = default)
    {
        var existing = await messages.GetMessageAsync(circleId, messageId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Text != text)
            {
                throw new LocalStateConflictException(
                    "message_id_conflict",
                    "This message ID already belongs to different content.");
            }

            return existing;
        }

        var draft = await messages.PrepareMessageDraftAsync(
            circleId,
            messageId,
            text,
            Now(),
            cancellationToken).ConfigureAwait(false);
        var request = new CircleMessageRequest(
            RemoteSecurityProtocol.Version,
            circleId.ToString(),
            messageId.ToString(),
            draft.AuthorMemberId.ToString(),
            draft.AuthorNodeId.ToString(),
            IdentityProtocolMapping.ToProtocol(draft.MemberCredential),
            IdentityProtocolMapping.ToProtocol(draft.NodeCredential),
            draft.Text,
            draft.AuthoredAtUtc,
            RemoteSecurityProtocol.SignatureSuite);
        var transcript = CircleMessageSecurity.EncodeRequest(request);
        var signedRequest = new SignedCircleMessageRequest(
            request,
            await messages.SignMessageDraftWithMemberAsync(
                circleId,
                transcript,
                cancellationToken).ConfigureAwait(false),
            await identities.SignWithNodeAsync(transcript, cancellationToken).ConfigureAwait(false));
        var context = await CreateChannelContextAsync(circleId, cancellationToken)
            .ConfigureAwait(false);
        var anchor = context.Peers.SingleOrDefault(peer =>
            peer.SignedBinding.Binding.NodeId == context.Trust.IssuerNodeId.ToString())
            ?? throw new LocalStateException(
                "anchor_transport_binding_missing",
                "The Circle Anchor transport binding is missing.");
        using var certificate = await identities.CreateTransportCertificateAsync(
            NodeDnsName,
            Now(),
            cancellationToken).ConfigureAwait(false);
        await using var connection = await connector.ConnectAsync(address, cancellationToken)
            .ConfigureAwait(false);
        await using var channel = await RemoteAuthenticatedChannel.ConnectAsync(
            connection,
            AnchorDnsName,
            new RemoteChannelIdentity(certificate, context.Local),
            anchor,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.WriteAsync(
            new RemoteFrame(messageId.Value, CircleMessageWireCodec.EncodeRequest(signedRequest)),
            cancellationToken).ConfigureAwait(false);
        var responseFrame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (responseFrame.OperationId != messageId.Value)
        {
            throw new RemoteChannelException("conflict");
        }
        if (CircleMessageWireCodec.TryDecodeRejection(responseFrame.Payload, out var rejection))
        {
            throw new CircleMessageRejectedException(rejection);
        }

        var signedReceipt = CircleMessageWireCodec.DecodeReceipt(responseFrame.Payload);
        var validation = CircleMessageSecurity.ValidateReceipt(
            signedReceipt,
            new CircleMessageReceiptVerificationContext(
                signedRequest,
                IdentityProtocolMapping.ToProtocol(context.Trust.AnchorCredential),
                circleId.ToString(),
                Now(),
                CircleMessageSecurity.DefaultMaximumClockSkew));
        if (!validation.IsAccepted)
        {
            throw Rejected(validation.RejectionCode);
        }

        var message = ToMessage(signedReceipt.Receipt);
        var commit = await messages.CommitReplicatedMessageAsync(
            new AuthoritativeMessageCommit(
                message,
                signedReceipt.Receipt.RequestDigest,
                responseFrame.Payload),
            cancellationToken).ConfigureAwait(false);
        return commit.Status switch
        {
            MessageCommitStatus.Accepted or MessageCommitStatus.IdempotentRetry
                when commit.Message is not null => commit.Message,
            _ => throw new LocalStateConflictException(
                "message_id_conflict",
                "The accepted message conflicts with local durable state."),
        };
    }

    internal async Task HandleAsync(
        UntrustedRemoteConnection connection,
        CancellationToken cancellationToken = default)
    {
        var circles = await localState.ListCirclesAsync(cancellationToken).ConfigureAwait(false);
        var authoritative = new List<ChannelContext>();
        foreach (var circle in circles)
        {
            if (await identities.GetCircleAuthorityAsync(circle.Circle.Id, cancellationToken)
                    .ConfigureAwait(false) is not null)
            {
                authoritative.Add(
                    await CreateChannelContextAsync(circle.Circle.Id, cancellationToken)
                        .ConfigureAwait(false));
            }
        }

        if (authoritative.Count != 1)
        {
            throw new RemoteChannelException("authentication_failed");
        }

        var context = authoritative[0];
        using var certificate = await identities.CreateTransportCertificateAsync(
            AnchorDnsName,
            Now(),
            cancellationToken).ConfigureAwait(false);
        await using var channel = await RemoteAuthenticatedChannel.AcceptAsync(
            connection,
            new RemoteChannelIdentity(certificate, context.Local),
            context.Peers
                .Where(peer => peer.SignedBinding.Binding.NodeId != context.Local.SignedBinding.Binding.NodeId)
                .ToArray(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var frame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var signed = CircleMessageWireCodec.DecodeRequest(frame.Payload);
            if (!Guid.TryParseExact(signed.Request.MessageId, "D", out var parsedMessage)
                || frame.OperationId != parsedMessage
                || !Guid.TryParseExact(signed.Request.AuthorMemberId, "D", out var parsedMember)
                || !Guid.TryParseExact(signed.Request.AuthorNodeId, "D", out var parsedNode))
            {
                throw Rejected(CircleMessageRejectionCode.Malformed);
            }

            var circleId = context.Circle.Circle.Id;
            var memberId = new MemberId(parsedMember);
            var nodeId = new NodeId(parsedNode);
            var member = context.Circle.Members.SingleOrDefault(value => value.Id == memberId);
            var isFoundingPair = member?.Role == MemberRole.Owner
                && nodeId == context.Trust.IssuerNodeId;
            var isAdmittedPair = await admissionState.IsAdmittedMemberNodePairAsync(
                circleId,
                memberId,
                nodeId,
                cancellationToken).ConfigureAwait(false);
            if (member is null
                || !context.Circle.Nodes.Any(node => node.NodeId == nodeId)
                || !(isFoundingPair || isAdmittedPair))
            {
                throw Rejected(CircleMessageRejectionCode.Unauthorized);
            }

            var memberCredential = await messages.GetCircleMemberCredentialAsync(
                circleId,
                memberId,
                cancellationToken).ConfigureAwait(false)
                ?? throw Rejected(CircleMessageRejectionCode.Unauthorized);
            var nodeSecurity = (await admissionState.ListCircleNodeSecurityAsync(
                    circleId,
                    cancellationToken).ConfigureAwait(false))
                .SingleOrDefault(value => value.NodeId == nodeId)
                ?? throw Rejected(CircleMessageRejectionCode.Unauthorized);
            var validation = CircleMessageSecurity.ValidateRequest(
                signed,
                new CircleMessageVerificationContext(
                    circleId.ToString(),
                    channel.PeerNodeId,
                    IdentityProtocolMapping.ToProtocol(memberCredential),
                    IdentityProtocolMapping.ToProtocol(nodeSecurity.NodeCredential),
                    Now(),
                    CircleMessageSecurity.DefaultMaximumClockSkew,
                    NoRevokedKeys));
            if (!validation.IsAccepted)
            {
                throw Rejected(validation.RejectionCode);
            }

            var requestDigest = CircleMessageSecurity.DigestRequest(signed);
            var messageId = new MessageId(parsedMessage);
            await authoritativeCommitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var prior = await messages.GetAuthoritativeMessageResultAsync(
                    circleId,
                    messageId,
                    requestDigest,
                    cancellationToken).ConfigureAwait(false);
                if (prior is not null)
                {
                    if (prior.Status == MessageCommitStatus.IdempotentRetry
                        && prior.EncodedResponse is not null)
                    {
                        await channel.WriteAsync(
                            new RemoteFrame(frame.OperationId, prior.EncodedResponse),
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    throw Rejected(CircleMessageRejectionCode.Conflict);
                }

                var existingMessages = await messages.ListMessagesAsync(circleId, cancellationToken)
                    .ConfigureAwait(false);
                var acceptedAt = Now();
                var message = new CircleMessage(
                    messageId,
                    circleId,
                    existingMessages.Count == 0 ? 1 : existingMessages[^1].Sequence + 1,
                    memberId,
                    nodeId,
                    signed.Request.Text,
                    signed.Request.AuthoredAtUtc,
                    acceptedAt);
                var receipt = new CircleMessageReceipt(
                    RemoteSecurityProtocol.Version,
                    circleId.ToString(),
                    messageId.ToString(),
                    message.Sequence,
                    memberId.ToString(),
                    nodeId.ToString(),
                    message.Text,
                    message.AuthoredAtUtc,
                    message.AcceptedAtUtc,
                    requestDigest);
                var signedReceipt = new SignedCircleMessageReceipt(
                    receipt,
                    RemoteSecurityProtocol.SignatureSuite,
                    await identities.SignWithCircleAnchorAsync(
                        circleId,
                        CircleMessageSecurity.EncodeReceipt(receipt),
                        cancellationToken).ConfigureAwait(false));
                var encoded = CircleMessageWireCodec.EncodeReceipt(signedReceipt);
                var commit = await messages.CommitAuthoritativeMessageAsync(
                    new AuthoritativeMessageCommit(message, requestDigest, encoded),
                    cancellationToken).ConfigureAwait(false);
                if (commit.Status is not
                    (MessageCommitStatus.Accepted or MessageCommitStatus.IdempotentRetry))
                {
                    throw Rejected(CircleMessageRejectionCode.Conflict);
                }

                await channel.WriteAsync(
                    new RemoteFrame(frame.OperationId, encoded),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                authoritativeCommitLock.Release();
            }
        }
        catch (CircleMessageRejectedException exception)
        {
            await admissionState.RecordAdmissionAuditAsync(
                context.Circle.Circle.Id,
                $"message:{exception.Code}",
                Now(),
                cancellationToken).ConfigureAwait(false);
            await channel.WriteAsync(
                new RemoteFrame(frame.OperationId, CircleMessageWireCodec.EncodeRejection(exception.Code)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RemoteChannelException)
        {
            await channel.WriteAsync(
                new RemoteFrame(frame.OperationId, CircleMessageWireCodec.EncodeRejection("malformed")),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ChannelContext> CreateChannelContextAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        var circle = await localState.GetCircleAsync(circleId, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        var trust = await admissionState.GetCircleTrustAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException("circle_trust_missing", "Circle trust state is missing.");
        var localNode = await localState.GetNodeAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("node_identity_missing", "The local Node identity is missing.");
        var root = IdentityProtocolMapping.ToProtocol(trust.RootCredential);
        var peers = (await admissionState.ListCircleNodeSecurityAsync(circleId, cancellationToken)
                .ConfigureAwait(false))
            .Select(state => new RemotePeerExpectation(
                NodeTransportBindingCodec.Decode(state.SignedTransportBinding),
                new NodeTransportVerificationContext(
                    circleId.ToString(),
                    state.NodeId.ToString(),
                    root,
                    Now(),
                    trust.AuthorityGeneration,
                    RemoteSecurityProtocol.Version,
                    RemoteSecurityProtocol.Version,
                    NoRevokedKeys)))
            .ToArray();
        var local = peers.SingleOrDefault(peer =>
            peer.SignedBinding.Binding.NodeId == localNode.Id.ToString())
            ?? throw new LocalStateException(
                "local_transport_binding_missing",
                "The local Circle transport binding is missing.");
        return new ChannelContext(circle, trust, local, peers);
    }

    private DateTimeOffset Now() => timeProvider.GetUtcNow().ToUniversalTime();

    private static CircleMessage ToMessage(CircleMessageReceipt receipt) =>
        new(
            new MessageId(Guid.Parse(receipt.MessageId)),
            new CircleId(Guid.Parse(receipt.CircleId)),
            receipt.Sequence,
            new MemberId(Guid.Parse(receipt.AuthorMemberId)),
            new NodeId(Guid.Parse(receipt.AuthorNodeId)),
            receipt.Text,
            receipt.AuthoredAtUtc,
            receipt.AcceptedAtUtc);

    private static CircleMessageRejectedException Rejected(CircleMessageRejectionCode code) =>
        new(code switch
        {
            CircleMessageRejectionCode.UnsupportedSuite => "unsupported_suite",
            CircleMessageRejectionCode.Unauthorized => "unauthorized",
            CircleMessageRejectionCode.Forged => "forged",
            CircleMessageRejectionCode.Revoked => "revoked",
            CircleMessageRejectionCode.WrongCircle => "wrong_circle",
            CircleMessageRejectionCode.WrongNode => "wrong_node",
            CircleMessageRejectionCode.Stale => "stale",
            CircleMessageRejectionCode.Replayed => "replayed",
            CircleMessageRejectionCode.Conflict => "conflict",
            _ => "malformed",
        });

    private sealed record ChannelContext(
        CircleDetails Circle,
        CircleTrustState Trust,
        RemotePeerExpectation Local,
        IReadOnlyList<RemotePeerExpectation> Peers);
}

internal sealed class CircleMessageRejectedException(string code) : Exception(code)
{
    internal string Code { get; } = code;
}
