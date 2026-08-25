using System.Security.Cryptography;
using System.Text;
using Balls.Core;
using Balls.Protocol.Remote.V1;
using Balls.Storage.Sqlite;
using Balls.Transport.Lan;

namespace Balls.Daemon;

internal sealed record CircleFilesSyncResult(string CircleId, int ImportedGrantCount);

internal sealed class TrustedCircleFilesSyncApplication(
    ILocalStateStore localState,
    IIdentityAuthorityStore identities,
    IAdmissionStateStore admissionState,
    ICircleMessageStateStore members,
    ICircleFilesStateStore files,
    ICircleFilesProviderCredentialStore credentials,
    SqliteLocalStateStore importedState,
    IRemoteTransportConnector connector,
    TimeProvider timeProvider)
{
    private const int MaximumGrantCount = 16;
    private static readonly IReadOnlySet<string> NoRevokedKeys =
        new HashSet<string>(StringComparer.Ordinal);

    internal async Task<CircleFilesSyncResult> SynchronizeAsync(
        CircleId circleId,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var address = new RemoteTransportAddress(LanTcpEndpoint.ProviderName, endpoint);
        _ = LanTcpEndpoint.Parse(address);
        var circle = await localState.GetCircleAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        var author = await members.GetLocalCircleMessageAuthorAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_files_member_required",
                "A local Circle Member identity is required to synchronize shared folders.");
        if (!circle.Members.Any(member =>
                member.Id == author.MemberId && member.Role == MemberRole.Member))
        {
            throw new LocalStateException(
                "circle_files_member_required",
                "Only an ordinary Circle Member can synchronize shared folders.");
        }

        var trust = await admissionState.GetCircleTrustAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_trust_not_found",
                "The requested Circle trust state is not known.");
        var security = await admissionState.ListCircleNodeSecurityAsync(circleId, cancellationToken)
            .ConfigureAwait(false);
        var localSecurity = security.SingleOrDefault(state => state.NodeId == author.NodeId)
            ?? throw new LocalStateException(
                "circle_files_node_untrusted",
                "The local Node has no admitted Circle transport credential.");
        var anchorSecurity = security.SingleOrDefault(state => state.NodeId == trust.IssuerNodeId)
            ?? throw new LocalStateException(
                "circle_files_anchor_untrusted",
                "The Circle Anchor has no admitted transport credential.");

        var requestId = Guid.CreateVersion7();
        var transcript = EncodeRequestTranscript(
            circleId.ToString(),
            author.MemberId.ToString(),
            author.NodeId.ToString(),
            requestId.ToString("D"));
        var request = new SignedCircleFilesSyncRequest(
            circleId.ToString(),
            author.MemberId.ToString(),
            author.NodeId.ToString(),
            requestId.ToString("D"),
            await members.SignWithLocalCircleMemberAsync(
                circleId,
                transcript,
                cancellationToken).ConfigureAwait(false),
            await identities.SignWithNodeAsync(transcript, cancellationToken)
                .ConfigureAwait(false));

        var now = Now();
        using var certificate = await identities.CreateTransportCertificateAsync(
            "node.balls",
            now,
            cancellationToken).ConfigureAwait(false);
        await using var connection = await connector.ConnectAsync(address, cancellationToken)
            .ConfigureAwait(false);
        await using var channel = await RemoteAuthenticatedChannel.ConnectAsync(
            connection,
            "anchor.balls",
            new RemoteChannelIdentity(certificate, ToExpectation(localSecurity, trust, now)),
            ToExpectation(anchorSecurity, trust, now),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.WriteAsync(
            new RemoteFrame(requestId, TrustedCircleFilesWireCodec.EncodeRequest(request)),
            cancellationToken).ConfigureAwait(false);
        var responseFrame = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (responseFrame.OperationId != requestId)
        {
            throw new RemoteChannelException("malformed");
        }

        var response = TrustedCircleFilesWireCodec.DecodeResponse(responseFrame.Payload);
        try
        {
            if (response.CircleId != request.CircleId
                || response.MemberId != request.MemberId
                || response.RequestId != request.RequestId
                || response.Grants is null
                || response.Grants.Length > MaximumGrantCount)
            {
                throw new RemoteChannelException("malformed");
            }

            if (response.ErrorCode is not null)
            {
                throw new LocalStateException(
                    "circle_files_sync_rejected",
                    "The Circle Owner rejected the shared-folder synchronization.");
            }

            foreach (var item in response.Grants)
            {
                if (item is null || item.Secret is null)
                {
                    throw new RemoteChannelException("malformed");
                }

                ValidateCredentialBinding(item, author.MemberId);
                await importedState.ImportAuthorizedCircleFilesAccessAsync(
                    item.Contribution,
                    item.Grant,
                    item.OwnerCredential,
                    cancellationToken).ConfigureAwait(false);
                using var protectedCredential =
                    await credentials.PrepareCircleFilesProviderCredentialAsync(
                        item.Binding,
                        item.Secret,
                        cancellationToken).ConfigureAwait(false);
                await credentials.CompleteCircleFilesProviderCredentialAsync(
                    item.Binding,
                    cancellationToken).ConfigureAwait(false);
            }

            return new CircleFilesSyncResult(circleId.ToString(), response.Grants.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(responseFrame.Payload);
            if (response.Grants is not null)
            {
                foreach (var item in response.Grants)
                {
                    if (item?.Secret is not null)
                    {
                        CryptographicOperations.ZeroMemory(item.Secret);
                    }
                }
            }
        }
    }

    internal async Task HandleAuthenticatedRequestAsync(
        CircleDetails circle,
        RemoteAuthenticatedChannel channel,
        RemoteFrame frame,
        CancellationToken cancellationToken)
    {
        var request = TrustedCircleFilesWireCodec.DecodeRequest(frame.Payload);
        if (!Guid.TryParseExact(request.CircleId, "D", out var parsedCircle)
            || !Guid.TryParseExact(request.MemberId, "D", out var parsedMember)
            || !Guid.TryParseExact(request.NodeId, "D", out var parsedNode)
            || !Guid.TryParseExact(request.RequestId, "D", out var requestId)
            || requestId != frame.OperationId
            || parsedCircle != circle.Circle.Id.Value
            || !string.Equals(request.NodeId, channel.PeerNodeId, StringComparison.Ordinal)
            || request.MemberSignature is not { Length: 64 }
            || request.NodeSignature is not { Length: 64 })
        {
            throw new RemoteChannelException("authentication_failed");
        }

        var circleId = new CircleId(parsedCircle);
        var memberId = new MemberId(parsedMember);
        var nodeId = new NodeId(parsedNode);
        var author = await members.GetCircleMessageAuthorAsync(
            circleId,
            memberId,
            nodeId,
            cancellationToken).ConfigureAwait(false);
        var transcript = EncodeRequestTranscript(
            request.CircleId,
            request.MemberId,
            request.NodeId,
            request.RequestId);
        if (author is null
            || !author.IsAuthorized
            || !circle.Members.Any(member =>
                member.Id == memberId && member.Role == MemberRole.Member)
            || !IdentityCryptography.Verify(
                transcript,
                request.MemberSignature,
                author.MemberCredential)
            || !IdentityCryptography.Verify(
                transcript,
                request.NodeSignature,
                author.NodeCredential))
        {
            throw new RemoteChannelException("authentication_failed");
        }

        var ownerContext = await files.GetAuthorizationContextAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException("circle_not_found", "The requested Circle is not known.");
        if (ownerContext.MemberRole != MemberRole.Owner)
        {
            throw new RemoteChannelException("authentication_failed");
        }

        var values = new List<CircleFilesSyncWireItem>();
        try
        {
            var filesApplication = new CircleFilesApplication(files, identities, timeProvider);
            foreach (var contribution in await files.ListContributionsAsync(
                         circleId,
                         cancellationToken).ConfigureAwait(false))
            {
                foreach (var grant in await files.ListAccessGrantsAsync(
                             circleId,
                             contribution.Id,
                             cancellationToken).ConfigureAwait(false))
                {
                    if (grant.MemberId != memberId
                        || grant.Lifecycle != MemberAccessGrantLifecycle.Defined)
                    {
                        continue;
                    }

                    var authorized = await filesApplication.GetAuthorizedLocalAccessGrantAsync(
                        circleId,
                        contribution.Id,
                        grant.Id,
                        cancellationToken).ConfigureAwait(false);
                    using var material =
                        await credentials.GetActiveCircleFilesProviderCredentialAsync(
                            grant.Id.ToString(),
                            cancellationToken).ConfigureAwait(false);
                    if (material is null)
                    {
                        continue;
                    }

                    values.Add(new CircleFilesSyncWireItem(
                        authorized.Contribution,
                        authorized.Grant,
                        authorized.OwnerMemberCredential,
                        material.Binding,
                        material.Secret.ToArray()));
                    if (values.Count > MaximumGrantCount)
                    {
                        throw new RemoteChannelException("oversized");
                    }
                }
            }

            var response = new CircleFilesSyncWireResponse(
                request.CircleId,
                request.MemberId,
                request.RequestId,
                values.ToArray());
            var payload = TrustedCircleFilesWireCodec.EncodeResponse(response);
            try
            {
                await channel.WriteAsync(
                    new RemoteFrame(frame.OperationId, payload),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        finally
        {
            foreach (var value in values)
            {
                CryptographicOperations.ZeroMemory(value.Secret);
            }
        }
    }

    private static void ValidateCredentialBinding(
        CircleFilesSyncWireItem item,
        MemberId memberId)
    {
        var expectedAccess = item.Grant.Access == MemberAccessMode.ReadOnly
            ? "read-only"
            : "read-write";
        if (item.Secret.Length is < 24 or > 128
            || item.Binding.GrantId != item.Grant.Id.ToString()
            || item.Binding.CircleId != item.Grant.CircleId.ToString()
            || item.Binding.ContributionId != item.Contribution.Id.ToString()
            || item.Binding.MemberId != memberId.ToString()
            || item.Grant.MemberId != memberId
            || item.Binding.Provider != "windows-smb-3.1.1-v1"
            || item.Binding.Access != expectedAccess
            || item.Binding.Generation != item.Grant.Generation)
        {
            throw new LocalStateException(
                "circle_files_provider_credential_conflict",
                "The remote provider credential does not match the authorized Member grant.");
        }
    }

    private static byte[] EncodeRequestTranscript(
        string circleId,
        string memberId,
        string nodeId,
        string requestId) =>
        Encoding.UTF8.GetBytes(
            $"balls-circle-files-sync-v1|{circleId}|{memberId}|{nodeId}|{requestId}");

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

    private DateTimeOffset Now() =>
        DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());
}
