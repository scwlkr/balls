using Balls.Core;
using Balls.Protocol.Remote.V1;

namespace Balls.Daemon;

internal sealed class TrustedCircleMessageApplication(
    ILocalStateStore localState,
    IIdentityAuthorityStore identities,
    IAdmissionStateStore admissionState,
    ICircleMessageStateStore messages,
    IRemoteTransportConnector connector,
    TimeProvider timeProvider)
{
    private const string AnchorDnsName = "anchor.balls";
    private const string NodeDnsName = "node.balls";
    private static readonly IReadOnlySet<string> NoRevokedKeys =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly SemaphoreSlim commitLock = new(1, 1);

    internal async Task<PersistedCircleMessage> SendAsync(
        CircleMessageId messageId,
        CircleId circleId,
        RemoteTransportAddress address,
        string text,
        CancellationToken cancellationToken = default)
    {
        var prepared = await messages.PrepareOutgoingCircleMessageAsync(
            messageId,
            circleId,
            text,
            Now(),
            cancellationToken).ConfigureAwait(false);
        var authorState = await messages.GetCircleMessageAuthorAsync(
            circleId,
            prepared.AuthorMemberId,
            prepared.AuthorNodeId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException(
                "message_author_not_found",
                "The local message author is missing Circle credentials.");
        var message = new CircleMessage(
            RemoteSecurityProtocol.Version,
            messageId.ToString(),
            circleId.ToString(),
            prepared.AuthorMemberId.ToString(),
            prepared.AuthorNodeId.ToString(),
            prepared.AuthoredAtUtc,
            prepared.Text);
        var transcript = CircleMessageSecurity.EncodeMessage(message);
        var signed = new SignedCircleMessage(
            message,
            RemoteSecurityProtocol.SignatureSuite,
            await messages.SignWithLocalCircleMemberAsync(
                circleId,
                transcript,
                cancellationToken).ConfigureAwait(false),
            await identities.SignWithNodeAsync(transcript, cancellationToken).ConfigureAwait(false));
        var localValidation = CircleMessageSecurity.Validate(
            signed,
            ToVerificationContext(authorState, Now()));
        if (!localValidation.IsAccepted)
        {
            throw Rejected(CodeFor(localValidation.RejectionCode));
        }

        var trust = await GetTrustAsync(circleId, cancellationToken).ConfigureAwait(false);
        var security = await admissionState.ListCircleNodeSecurityAsync(circleId, cancellationToken)
            .ConfigureAwait(false);
        var localSecurity = security.Single(state => state.NodeId == prepared.AuthorNodeId);
        var anchorSecurity = security.Single(state => state.NodeId == trust.IssuerNodeId);
        using var certificate = await identities.CreateTransportCertificateAsync(
            NodeDnsName,
            Now(),
            cancellationToken).ConfigureAwait(false);
        await using var connection = await connector.ConnectAsync(address, cancellationToken)
            .ConfigureAwait(false);
        await using var channel = await RemoteAuthenticatedChannel.ConnectAsync(
            connection,
            AnchorDnsName,
            new RemoteChannelIdentity(
                certificate,
                ToExpectation(localSecurity, trust, Now())),
            ToExpectation(anchorSecurity, trust, Now()),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var requestBytes = CircleMessageWireCodec.EncodeRequest(signed);
        await channel.WriteAsync(
            new RemoteFrame(messageId.Value, requestBytes),
            cancellationToken).ConfigureAwait(false);
        var response = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (response.OperationId != messageId.Value)
        {
            throw new RemoteChannelException("malformed");
        }

        if (CircleMessageWireCodec.TryDecodeRejection(response.Payload, out var rejection))
        {
            throw Rejected(rejection ?? "malformed");
        }

        var receipt = CircleMessageWireCodec.DecodeReceipt(response.Payload);
        var receiptValidation = CircleMessageSecurity.ValidateReceipt(
            receipt,
            signed,
            IdentityProtocolMapping.ToProtocol(trust.AnchorCredential));
        if (!receiptValidation.IsAccepted)
        {
            throw Rejected(CodeFor(receiptValidation.RejectionCode));
        }

        var persisted = ToPersisted(receipt);
        var commit = await messages.CommitCircleMessageAsync(
            new CircleMessageCommit(
                persisted,
                CircleMessageSecurity.DigestSignedMessage(signed),
                requestBytes,
                response.Payload),
            cancellationToken).ConfigureAwait(false);
        return commit.Status switch
        {
            CircleMessageCommitStatus.Accepted or CircleMessageCommitStatus.IdempotentRetry
                when commit.Message is not null => commit.Message,
            _ => throw Rejected("conflict"),
        };
    }

    internal async Task HandleAsync(
        UntrustedRemoteConnection connection,
        CancellationToken cancellationToken = default)
    {
        var circles = await localState.ListCirclesAsync(cancellationToken).ConfigureAwait(false);
        var anchorCircles = new List<(CircleDetails Circle, CircleTrustState Trust,
            CircleNodeSecurityState Local, IReadOnlyList<CircleNodeSecurityState> Peers)>();
        var localNode = await localState.GetNodeAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("node_identity_missing", "Local Node identity is missing.");
        foreach (var circle in circles)
        {
            var trust = await admissionState.GetCircleTrustAsync(circle.Circle.Id, cancellationToken)
                .ConfigureAwait(false);
            if (trust is null || trust.IssuerNodeId != localNode.Id)
            {
                continue;
            }

            var security = await admissionState.ListCircleNodeSecurityAsync(
                circle.Circle.Id,
                cancellationToken).ConfigureAwait(false);
            var local = security.SingleOrDefault(state => state.NodeId == localNode.Id);
            var peers = security.Where(state => state.NodeId != localNode.Id).ToArray();
            if (local is not null && peers.Length > 0)
            {
                anchorCircles.Add((circle, trust, local, peers));
            }
        }

        if (anchorCircles.Count != 1)
        {
            throw new LocalStateException(
                "message_listener_context",
                "The v1 message listener requires exactly one admitted Anchor Circle.");
        }

        var context = anchorCircles.Single();
        using var certificate = await identities.CreateTransportCertificateAsync(
            AnchorDnsName,
            Now(),
            cancellationToken).ConfigureAwait(false);
        await using var channel = await RemoteAuthenticatedChannel.AcceptAsync(
            connection,
            new RemoteChannelIdentity(
                certificate,
                ToExpectation(context.Local, context.Trust, Now())),
            context.Peers.Select(peer => ToExpectation(peer, context.Trust, Now())).ToArray(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var frame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var signed = CircleMessageWireCodec.DecodeRequest(frame.Payload);
            if (!Guid.TryParseExact(signed.Message.MessageId, "D", out var requestId)
                || requestId != frame.OperationId)
            {
                throw Rejected("malformed");
            }

            var circleId = new CircleId(Guid.Parse(signed.Message.CircleId));
            var memberId = new MemberId(Guid.Parse(signed.Message.AuthorMemberId));
            var nodeId = new NodeId(Guid.Parse(signed.Message.AuthorNodeId));
            var author = await messages.GetCircleMessageAuthorAsync(
                circleId,
                memberId,
                nodeId,
                cancellationToken).ConfigureAwait(false);
            if (author is null)
            {
                throw Rejected("unauthorized");
            }

            var validation = CircleMessageSecurity.Validate(
                signed,
                ToVerificationContext(author, Now()) with
                {
                    ExpectedCircleId = context.Circle.Circle.Id.ToString(),
                    ExpectedNodeId = channel.PeerNodeId,
                });
            if (!validation.IsAccepted)
            {
                throw Rejected(CodeFor(validation.RejectionCode));
            }

            await commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var sequence = await messages.GetNextCircleMessageSequenceAsync(
                    circleId,
                    cancellationToken).ConfigureAwait(false);
                var unsignedReceipt = CircleMessageSecurity.CreateReceipt(
                    signed,
                    sequence,
                    Now(),
                    []);
                var signature = await identities.SignWithCircleAnchorAsync(
                    circleId,
                    CircleMessageSecurity.EncodeReceiptTranscript(unsignedReceipt),
                    cancellationToken).ConfigureAwait(false);
                var receipt = unsignedReceipt with { AnchorSignature = signature };
                var encodedReceipt = CircleMessageWireCodec.EncodeReceipt(receipt);
                var commit = await messages.CommitCircleMessageAsync(
                    new CircleMessageCommit(
                        ToPersisted(receipt),
                        CircleMessageSecurity.DigestSignedMessage(signed),
                        frame.Payload,
                        encodedReceipt),
                    cancellationToken).ConfigureAwait(false);
                if (commit.Status == CircleMessageCommitStatus.Conflict)
                {
                    throw Rejected("conflict");
                }

                await channel.WriteAsync(
                    new RemoteFrame(
                        frame.OperationId,
                        commit.Status == CircleMessageCommitStatus.IdempotentRetry
                            ? commit.EncodedReceipt!
                            : encodedReceipt),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                commitLock.Release();
            }
        }
        catch (CircleMessageProtocolException exception)
        {
            await channel.WriteAsync(
                new RemoteFrame(frame.OperationId, CircleMessageWireCodec.EncodeRejection(exception.Code)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CircleMessageRejectedException exception)
        {
            await channel.WriteAsync(
                new RemoteFrame(frame.OperationId, CircleMessageWireCodec.EncodeRejection(exception.Code)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CircleTrustState> GetTrustAsync(
        CircleId circleId,
        CancellationToken cancellationToken) =>
        await admissionState.GetCircleTrustAsync(circleId, cancellationToken).ConfigureAwait(false)
        ?? throw new LocalStateException(
            "circle_trust_not_found",
            "The requested Circle trust state is not known to this Node.");

    private static CircleMessageVerificationContext ToVerificationContext(
        CircleMessageAuthorState author,
        DateTimeOffset now) =>
        new(
            author.CircleId.ToString(),
            author.MemberId.ToString(),
            author.NodeId.ToString(),
            IdentityProtocolMapping.ToProtocol(author.MemberCredential),
            IdentityProtocolMapping.ToProtocol(author.NodeCredential),
            author.IsAuthorized,
            now);

    private static RemotePeerExpectation ToExpectation(
        CircleNodeSecurityState state,
        CircleTrustState trust,
        DateTimeOffset now) =>
        new(
            NodeTransportBindingCodec.Decode(state.SignedTransportBinding),
            new NodeTransportVerificationContext(
                state.CircleId.ToString(),
                state.NodeId.ToString(),
                IdentityProtocolMapping.ToProtocol(trust.RootCredential),
                now,
                trust.AuthorityGeneration,
                RemoteSecurityProtocol.Version,
                RemoteSecurityProtocol.Version,
                NoRevokedKeys));

    private static PersistedCircleMessage ToPersisted(CircleMessageReceipt receipt) =>
        new(
            new CircleMessageId(Guid.Parse(receipt.Message.MessageId)),
            new CircleId(Guid.Parse(receipt.Message.CircleId)),
            new MemberId(Guid.Parse(receipt.Message.AuthorMemberId)),
            new NodeId(Guid.Parse(receipt.Message.AuthorNodeId)),
            receipt.Message.Text,
            receipt.Message.AuthoredAtUtc,
            receipt.Sequence,
            receipt.AcceptedAtUtc);

    private DateTimeOffset Now() =>
        DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());

    private static string CodeFor(CircleMessageRejectionCode code) => code switch
    {
        CircleMessageRejectionCode.UnsupportedSuite => "unsupported_suite",
        CircleMessageRejectionCode.Unauthorized => "unauthorized",
        CircleMessageRejectionCode.Forged => "forged",
        CircleMessageRejectionCode.WrongCircle => "wrong_circle",
        CircleMessageRejectionCode.WrongNode => "wrong_node",
        CircleMessageRejectionCode.Replayed => "replayed",
        CircleMessageRejectionCode.Conflict => "conflict",
        _ => "malformed",
    };

    private static CircleMessageRejectedException Rejected(string code) => new(code);
}

internal sealed class CircleMessageRejectedException(string code) : Exception
{
    internal string Code { get; } = code;
}
