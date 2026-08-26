using System.Net;
using System.Text;
using Balls.Core;
using Balls.Daemon;
using Balls.Platform;
using Balls.Protocol.Remote.V1;
using Balls.Storage.Sqlite;
using Balls.Transport.Lan;
using Microsoft.Data.Sqlite;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class TrustedCircleAdmissionApplicationTests
{
    [TestMethod]
    public async Task Two_nodes_admit_once_and_reopen_the_same_signed_roster()
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive(
                ".NET 10 supports TLS 1.3 on macOS clients, but not macOS SslStream servers.");
            return;
        }

        using var anchorDirectory = new TemporaryDirectory();
        using var joinerDirectory = new TemporaryDirectory();
        var protector = new PassthroughProtector();
        string package;
        CircleId circleId;
        CircleFilesContributionId contributionId;
        MemberAccessGrantId memberGrantId;
        CircleDetails joined;
        DateTimeOffset joinedAtUtc;
        RemoteTransportAddress admissionAddress;
        RemoteTransportAddress syncAddress;
        CircleMessageId messageId = new(Guid.CreateVersion7());
        var memberSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        await using (var anchorStore = await SqliteLocalStateStore.OpenAsync(
                         anchorDirectory.Path,
                         protector))
        await using (var joinerStore = await SqliteLocalStateStore.OpenAsync(
                         joinerDirectory.Path,
                         protector))
        await using (var listener = new TcpLanTransportListener(
                         new IPEndPoint(IPAddress.Loopback, 0)))
        await using (var messageListener = new TcpLanTransportListener(
                         new IPEndPoint(IPAddress.Loopback, 0)))
        {
            // This exercises real TLS, whose certificate validation uses the system clock.
            // Keep the application clock aligned so the 24-hour transport certificates
            // cannot expire merely because this integration test's fixture date gets old.
            var now = TimeProvider.System.GetUtcNow();
            joinedAtUtc = now;
            var time = new FixedTimeProvider(now);
            var anchorCircles = new CircleApplication(anchorStore, time, "Anchor-PC");
            var joinerCircles = new CircleApplication(joinerStore, time, "Joiner-PC");
            var created = await anchorCircles.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Example Circle",
                    "Alice"));
            await joinerCircles.GetLocalNodeAsync();
            circleId = created.Circle.Id;
            package = (await new InvitationApplication(
                    anchorStore,
                    anchorStore,
                    anchorStore,
                    time)
                .CreateAsync(circleId, 60)).Package;
            var anchorAdmission = new TrustedCircleAdmissionApplication(
                anchorStore,
                anchorStore,
                anchorStore,
                anchorStore,
                new TcpLanTransportConnector(),
                time);
            var joinerAdmission = new TrustedCircleAdmissionApplication(
                joinerStore,
                joinerStore,
                joinerStore,
                joinerStore,
                new TcpLanTransportConnector(),
                time);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            admissionAddress = listener.BoundAddress;
            syncAddress = messageListener.BoundAddress;
            var serve = ServeOneAsync(
                listener,
                anchorAdmission,
                timeout.Token);
            joined = await joinerAdmission.JoinWithConnectionAsync(
                package,
                listener.BoundAddress,
                messageListener.BoundAddress,
                "Bob",
                timeout.Token);
            await serve;

            Assert.AreEqual(2, joined.Members.Count);
            Assert.AreEqual(2, joined.Nodes.Count);
            Assert.AreEqual(MemberRole.Member, joined.Members.Single(member =>
                member.DisplayName == "Bob").Role);
            var anchorView = await anchorCircles.GetCircleAsync(circleId);
            Assert.AreEqual(2, anchorView!.Members.Count);
            Assert.AreEqual(2, anchorView.Nodes.Count);

            var retried = await joinerAdmission.JoinWithConnectionAsync(
                package,
                listener.BoundAddress,
                messageListener.BoundAddress,
                "Bob",
                timeout.Token);
            Assert.AreEqual(2, retried.Members.Count);
            Assert.AreEqual(2, retried.Nodes.Count);
            var savedConnection = await joinerStore.GetCircleConnectionAsync(circleId);
            Assert.IsNotNull(savedConnection);
            Assert.AreEqual(circleId, savedConnection.CircleId);
            Assert.AreEqual(1, savedConnection.Version);
            Assert.AreEqual(listener.BoundAddress.Provider, savedConnection.Provider);
            Assert.AreEqual(listener.BoundAddress.Value, savedConnection.AdmissionEndpoint);
            Assert.AreEqual(messageListener.BoundAddress.Value, savedConnection.SyncEndpoint);
            Assert.AreEqual(now.ToUnixTimeSeconds(), savedConnection.StoredAtUtc.ToUnixTimeSeconds());

            var anchorMessages = new TrustedCircleMessageApplication(
                anchorStore,
                anchorStore,
                anchorStore,
                anchorStore,
                new TcpLanTransportConnector(),
                time);
            var joinerMessages = new TrustedCircleMessageApplication(
                joinerStore,
                joinerStore,
                joinerStore,
                joinerStore,
                new TcpLanTransportConnector(),
                time);
            var serveForged = ServeMessageOnceAsync(
                messageListener,
                anchorMessages,
                timeout.Token);
            var rejection = await SendTamperedMessageAsync(
                joinerStore,
                circleId,
                messageListener.BoundAddress,
                now,
                timeout.Token);
            await serveForged;
            Assert.AreEqual("forged", rejection);
            Assert.AreEqual(0, (await anchorStore.ListCircleMessagesAsync(circleId)).Count);

            var serveMessage = ServeMessageOnceAsync(
                messageListener,
                anchorMessages,
                timeout.Token);
            var sent = await joinerMessages.SendAsync(
                messageId,
                circleId,
                messageListener.BoundAddress,
                "Hello from Bob's Node.",
                timeout.Token);
            await serveMessage;

            Assert.AreEqual(messageId, sent.Id);
            Assert.AreEqual(1, sent.Sequence);
            Assert.AreEqual("Hello from Bob's Node.", sent.Text);
            Assert.AreEqual("Bob", joined.Members.Single(member => member.Id == sent.AuthorMemberId).DisplayName);
            Assert.AreEqual(1, (await anchorStore.ListCircleMessagesAsync(circleId)).Count);
            Assert.AreEqual(1, (await joinerStore.ListCircleMessagesAsync(circleId)).Count);

            var serveRetry = ServeMessageOnceAsync(
                messageListener,
                anchorMessages,
                timeout.Token);
            var retriedMessage = await joinerMessages.SendAsync(
                messageId,
                circleId,
                messageListener.BoundAddress,
                "Hello from Bob's Node.",
                timeout.Token);
            await serveRetry;
            Assert.AreEqual(sent, retriedMessage);
            Assert.AreEqual(1, (await anchorStore.ListCircleMessagesAsync(circleId)).Count);
            Assert.AreEqual(1, (await joinerStore.ListCircleMessagesAsync(circleId)).Count);

            var anchorFiles = new CircleFilesApplication(anchorStore, anchorStore, time);
            var contribution = await anchorFiles.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    new CircleFilesContributionRequestId(Guid.CreateVersion7()),
                    circleId,
                    "Project Files"));
            contributionId = contribution.Id;
            var bob = joined.Members.Single(member => member.DisplayName == "Bob");
            var memberGrant = await anchorFiles.CreateAccessGrantAsync(
                new CreateMemberAccessGrantCommand(
                    new MemberAccessGrantRequestId(Guid.CreateVersion7()),
                    circleId,
                    contribution.Id,
                    bob.Id,
                    MemberAccessMode.ReadWrite));
            memberGrantId = memberGrant.Id;
            var ownerGrant = await anchorFiles.CreateAccessGrantAsync(
                new CreateMemberAccessGrantCommand(
                    new MemberAccessGrantRequestId(Guid.CreateVersion7()),
                    circleId,
                    contribution.Id,
                    created.Members.Single().Id,
                    MemberAccessMode.ReadWrite));
            var ownerAuthorization = await anchorStore.GetAuthorizationContextAsync(circleId);
            Assert.IsNotNull(ownerAuthorization);
            var crossMemberImport = await Assert.ThrowsExactlyAsync<LocalStateException>(() =>
                joinerStore.ImportAuthorizedCircleFilesAccessAsync(
                    contribution,
                    ownerGrant,
                    ownerAuthorization.MemberCredential));
            Assert.AreEqual("circle_files_authorization_failed", crossMemberImport.Code);
            var forgedGrant = memberGrant with { Access = MemberAccessMode.ReadOnly };
            var forgedImport = await Assert.ThrowsExactlyAsync<LocalStateException>(() =>
                joinerStore.ImportAuthorizedCircleFilesAccessAsync(
                    contribution,
                    forgedGrant,
                    ownerAuthorization.MemberCredential));
            Assert.AreEqual("circle_files_authorization_failed", forgedImport.Code);
            Assert.IsEmpty(await joinerStore.ListContributionsAsync(circleId));

            foreach (var (grant, account, ownership, secret) in new[]
                     {
                         (memberGrant, "BallsG-bob", new string('b', 64), memberSecret),
                         (ownerGrant, "BallsG-alice", new string('a', 64), new byte[32]),
                     })
            {
                var binding = new CircleFilesProviderCredentialBinding(
                    grant.Id.ToString(),
                    circleId.ToString(),
                    contribution.Id.ToString(),
                    grant.MemberId.ToString(),
                    "windows-smb-3.1.1-v1",
                    account,
                    ownership,
                    "read-write",
                    grant.Generation);
                using var prepared = await anchorStore.PrepareCircleFilesProviderCredentialAsync(
                    binding,
                    secret);
                await anchorStore.CompleteCircleFilesProviderCredentialAsync(binding);
            }

            var anchorSync = new TrustedCircleFilesSyncApplication(
                anchorStore,
                anchorStore,
                anchorStore,
                anchorStore,
                anchorStore,
                anchorStore,
                anchorStore,
                new TcpLanTransportConnector(),
                time);
            var joinerSync = new TrustedCircleFilesSyncApplication(
                joinerStore,
                joinerStore,
                joinerStore,
                joinerStore,
                joinerStore,
                joinerStore,
                joinerStore,
                new TcpLanTransportConnector(),
                time);
            var anchorSyncListener = new TrustedCircleMessageApplication(
                anchorStore,
                anchorStore,
                anchorStore,
                anchorStore,
                new TcpLanTransportConnector(),
                time,
                anchorSync);
            await AssertFilesSyncRequestRejectedAsync(
                messageListener,
                anchorSyncListener,
                joinerStore,
                circleId,
                bob.Id,
                tamperMemberSignature: true,
                now,
                timeout.Token);
            await AssertFilesSyncRequestRejectedAsync(
                messageListener,
                anchorSyncListener,
                joinerStore,
                circleId,
                created.Members.Single().Id,
                tamperMemberSignature: false,
                now,
                timeout.Token);
            var serveSync = ServeMessageOnceAsync(
                messageListener,
                anchorSyncListener,
                timeout.Token);
            var synchronization = await joinerSync.SynchronizeAsync(
                circleId,
                messageListener.BoundAddress.Value,
                timeout.Token);
            await serveSync;

            Assert.AreEqual(1, synchronization.ImportedGrantCount);
            Assert.AreEqual(contribution.Id, (await joinerStore.ListContributionsAsync(circleId)).Single().Id);
            var imported = (await joinerStore.ListAccessGrantsAsync(circleId, contribution.Id)).Single();
            Assert.AreEqual(memberGrant.Id, imported.Id);
            Assert.AreEqual(bob.Id, imported.MemberId);
            Assert.AreNotEqual(ownerGrant.Id, imported.Id);
            using var credential = await joinerStore.GetActiveCircleFilesProviderCredentialAsync(
                memberGrant.Id.ToString());
            Assert.IsNotNull(credential);
            CollectionAssert.AreEqual(memberSecret, credential.Secret.ToArray());
            Assert.IsNull(await joinerStore.GetActiveCircleFilesProviderCredentialAsync(
                ownerGrant.Id.ToString()));

            var mapper = new RecordingMemberMapper();
            var mapping = new CircleFilesMemberMappingApplication(
                joinerCircles,
                new CircleFilesApplication(joinerStore, joinerStore, time),
                joinerStore,
                joinerStore,
                mapper,
                time,
                joinerStore);
            var preview = await mapping.PreviewAsync(
                circleId,
                contribution.Id,
                memberGrant.Id,
                "192.168.50.20",
                "P",
                timeout.Token);
            var applied = await mapping.MapAsync(
                circleId,
                contribution.Id,
                memberGrant.Id,
                "192.168.50.20",
                "P",
                preview.PlanId,
                timeout.Token);
            Assert.AreEqual("mapped", applied.Status);
            Assert.AreEqual(bob.Id.ToString(), mapper.LastMemberId);
            CollectionAssert.AreEqual(memberSecret, mapper.LastSecret);

            var serveRepeatSync = ServeMessageOnceAsync(
                messageListener,
                anchorSyncListener,
                timeout.Token);
            var repeated = await joinerSync.SynchronizeAsync(
                circleId,
                messageListener.BoundAddress.Value,
                timeout.Token);
            await serveRepeatSync;
            Assert.AreEqual(1, repeated.ImportedGrantCount);
        }

        await using (var connection = new SqliteConnection(
                         $"Data Source={Path.Combine(joinerDirectory.Path, "balls.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            using var removeConnection = connection.CreateCommand();
            removeConnection.CommandText = "DELETE FROM circle_connections;";
            Assert.AreEqual(1, await removeConnection.ExecuteNonQueryAsync());
        }

        await using var reopenedAnchor = await SqliteLocalStateStore.OpenAsync(
            anchorDirectory.Path,
            protector);
        await using var reopenedJoiner = await SqliteLocalStateStore.OpenAsync(
            joinerDirectory.Path,
            protector);
        var repaired = await new TrustedCircleAdmissionApplication(
                reopenedJoiner,
                reopenedJoiner,
                reopenedJoiner,
                reopenedJoiner,
                new TcpLanTransportConnector(),
                new FixedTimeProvider(joinedAtUtc))
            .JoinWithConnectionAsync(
                package,
                admissionAddress,
                syncAddress,
                "Bob");
        var anchorRestart = await reopenedAnchor.GetCircleAsync(circleId);
        var joinerRestart = await reopenedJoiner.GetCircleAsync(circleId);

        Assert.AreEqual(circleId, repaired.Circle.Id);
        Assert.AreEqual(2, anchorRestart!.Members.Count);
        Assert.AreEqual(2, anchorRestart.Nodes.Count);
        Assert.AreEqual(2, joinerRestart!.Members.Count);
        Assert.AreEqual(2, joinerRestart.Nodes.Count);
        CollectionAssert.AreEqual(
            anchorRestart.Members.Select(value => value.Id.ToString())
                .Order(StringComparer.Ordinal).ToArray(),
            joinerRestart.Members.Select(value => value.Id.ToString())
                .Order(StringComparer.Ordinal).ToArray());
        Assert.IsNull(await reopenedJoiner.GetCircleAuthorityAsync(circleId));
        var reopenedConnection = await reopenedJoiner.GetCircleConnectionAsync(circleId);
        Assert.IsNotNull(reopenedConnection);
        Assert.AreEqual(LanTcpEndpoint.ProviderName, reopenedConnection.Provider);
        var anchorMessagesAfterRestart = await reopenedAnchor.ListCircleMessagesAsync(circleId);
        var joinerMessagesAfterRestart = await reopenedJoiner.ListCircleMessagesAsync(circleId);
        Assert.AreEqual(1, anchorMessagesAfterRestart.Count);
        Assert.AreEqual(anchorMessagesAfterRestart.Single(), joinerMessagesAfterRestart.Single());
        Assert.AreEqual(contributionId, (await reopenedJoiner.ListContributionsAsync(circleId)).Single().Id);
        Assert.AreEqual(
            memberGrantId,
            (await reopenedJoiner.ListAccessGrantsAsync(circleId, contributionId)).Single().Id);
        using var reopenedCredential =
            await reopenedJoiner.GetActiveCircleFilesProviderCredentialAsync(memberGrantId.ToString());
        Assert.IsNotNull(reopenedCredential);
        CollectionAssert.AreEqual(memberSecret, reopenedCredential.Secret.ToArray());
    }

    [TestMethod]
    public async Task Saved_provider_mismatch_fails_with_a_bounded_secret_free_error()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            new PassthroughProtector());
        var now = TimeProvider.System.GetUtcNow();
        var circle = await new CircleApplication(
                store,
                new FixedTimeProvider(now),
                "Bob-PC")
            .CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Provider Mismatch Circle",
                    "Bob"));
        const string mismatchedProvider = "unavailable-private-provider-v2";
        const string admissionEndpoint = "192.168.50.10:43120";
        const string syncEndpoint = "192.168.50.10:43155";
        await store.StoreCircleConnectionAsync(
            new CircleConnectionState(
                circle.Circle.Id,
                1,
                mismatchedProvider,
                admissionEndpoint,
                syncEndpoint,
                now));

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => BrowserCircleConnections.LoadAsync(
                store,
                circle.Circle.Id,
                CancellationToken.None));

        Assert.AreEqual("invalid_circle_connection", error.Code);
        Assert.IsTrue(error.Message.Length <= 160);
        Assert.IsFalse(error.Message.Contains(mismatchedProvider, StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains(admissionEndpoint, StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains(syncEndpoint, StringComparison.Ordinal));
    }

    private static async Task<string> SendTamperedMessageAsync(
        SqliteLocalStateStore store,
        CircleId circleId,
        RemoteTransportAddress address,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var author = await store.GetLocalCircleMessageAuthorAsync(circleId, cancellationToken);
        Assert.IsNotNull(author);
        var messageId = Guid.CreateVersion7();
        var message = new CircleMessage(
            RemoteSecurityProtocol.Version,
            messageId.ToString("D"),
            circleId.ToString(),
            author.MemberId.ToString(),
            author.NodeId.ToString(),
            now,
            "Original text.");
        var transcript = CircleMessageSecurity.EncodeMessage(message);
        var signed = new SignedCircleMessage(
            message,
            RemoteSecurityProtocol.SignatureSuite,
            await store.SignWithLocalCircleMemberAsync(circleId, transcript, cancellationToken),
            await store.SignWithNodeAsync(transcript, cancellationToken)) with
        {
            Message = message with { Text = "Tampered text." },
        };
        var trust = await store.GetCircleTrustAsync(circleId, cancellationToken);
        Assert.IsNotNull(trust);
        var security = await store.ListCircleNodeSecurityAsync(circleId, cancellationToken);
        var local = security.Single(value => value.NodeId == author.NodeId);
        var anchor = security.Single(value => value.NodeId == trust.IssuerNodeId);
        using var certificate = await store.CreateTransportCertificateAsync(
            "node.balls",
            now,
            cancellationToken);
        await using var connection = await new TcpLanTransportConnector().ConnectAsync(
            address,
            cancellationToken);
        await using var channel = await RemoteAuthenticatedChannel.ConnectAsync(
            connection,
            "anchor.balls",
            new RemoteChannelIdentity(certificate, ToExpectation(local, trust, now)),
            ToExpectation(anchor, trust, now),
            cancellationToken: cancellationToken);
        await channel.WriteAsync(
            new RemoteFrame(messageId, CircleMessageWireCodec.EncodeRequest(signed)),
            cancellationToken);
        var response = await channel.ReadAsync(cancellationToken);
        Assert.AreEqual(messageId, response.OperationId);
        Assert.IsTrue(CircleMessageWireCodec.TryDecodeRejection(response.Payload, out var rejection));
        return rejection!;
    }

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
                new HashSet<string>(StringComparer.Ordinal)));

    private static async Task AssertFilesSyncRequestRejectedAsync(
        TcpLanTransportListener listener,
        TrustedCircleMessageApplication owner,
        SqliteLocalStateStore member,
        CircleId circleId,
        MemberId claimedMemberId,
        bool tamperMemberSignature,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var serve = ServeMessageOnceAsync(listener, owner, cancellationToken);
        var request = SendRejectedFilesSyncRequestAsync(
            member,
            circleId,
            listener.BoundAddress,
            claimedMemberId,
            tamperMemberSignature,
            now,
            cancellationToken);
        var serverRejection = await Assert.ThrowsExactlyAsync<RemoteChannelException>(() => serve);
        Assert.AreEqual("authentication_failed", serverRejection.Code);
        var clientRejection = await Assert.ThrowsExactlyAsync<RemoteChannelException>(() => request);
        Assert.AreEqual("interrupted", clientRejection.Code);
    }

    private static async Task SendRejectedFilesSyncRequestAsync(
        SqliteLocalStateStore store,
        CircleId circleId,
        RemoteTransportAddress address,
        MemberId claimedMemberId,
        bool tamperMemberSignature,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var author = await store.GetLocalCircleMessageAuthorAsync(circleId, cancellationToken);
        Assert.IsNotNull(author);
        var operationId = Guid.CreateVersion7();
        var transcript = Encoding.UTF8.GetBytes(
            $"balls-circle-files-sync-v1|{circleId}|{claimedMemberId}|{author.NodeId}|{operationId:D}");
        var memberSignature = await store.SignWithLocalCircleMemberAsync(
            circleId,
            transcript,
            cancellationToken);
        if (tamperMemberSignature)
        {
            memberSignature[0] ^= 0x01;
        }

        var request = new SignedCircleFilesSyncRequest(
            circleId.ToString(),
            claimedMemberId.ToString(),
            author.NodeId.ToString(),
            operationId.ToString("D"),
            memberSignature,
            await store.SignWithNodeAsync(transcript, cancellationToken));
        var trust = await store.GetCircleTrustAsync(circleId, cancellationToken);
        Assert.IsNotNull(trust);
        var security = await store.ListCircleNodeSecurityAsync(circleId, cancellationToken);
        var local = security.Single(value => value.NodeId == author.NodeId);
        var anchor = security.Single(value => value.NodeId == trust.IssuerNodeId);
        using var certificate = await store.CreateTransportCertificateAsync(
            "node.balls",
            now,
            cancellationToken);
        await using var connection = await new TcpLanTransportConnector().ConnectAsync(
            address,
            cancellationToken);
        await using var channel = await RemoteAuthenticatedChannel.ConnectAsync(
            connection,
            "anchor.balls",
            new RemoteChannelIdentity(certificate, ToExpectation(local, trust, now)),
            ToExpectation(anchor, trust, now),
            cancellationToken: cancellationToken);
        await channel.WriteAsync(
            new RemoteFrame(operationId, TrustedCircleFilesWireCodec.EncodeRequest(request)),
            cancellationToken);
        _ = await channel.ReadAsync(cancellationToken);
    }

    private static async Task ServeMessageOnceAsync(
        TcpLanTransportListener listener,
        TrustedCircleMessageApplication application,
        CancellationToken cancellationToken)
    {
        await foreach (var connection in listener.AcceptAsync(cancellationToken))
        {
            await application.HandleAsync(connection, cancellationToken);
            return;
        }
    }

    private static async Task ServeOneAsync(
        TcpLanTransportListener listener,
        TrustedCircleAdmissionApplication application,
        CancellationToken cancellationToken)
    {
        await foreach (var connection in listener.AcceptAsync(cancellationToken))
        {
            await application.HandleAsync(connection, cancellationToken);
            return;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class RecordingMemberMapper : ICircleFilesMemberMapper
    {
        internal string? LastMemberId { get; private set; }

        internal byte[] LastSecret { get; private set; } = [];

        public ValueTask<CircleFilesMemberMappingPlan> PreviewAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(CreatePlan(request));

        public ValueTask<CircleFilesMemberMappingInspection> InspectAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CircleFilesMemberMappingInspection("mapped", CreatePlan(request)));

        public ValueTask<CircleFilesMemberMappingResult> MapAsync(
            CircleFilesMemberMappingRequest request,
            string expectedPlanId,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken)
        {
            LastMemberId = request.MemberId;
            LastSecret = secret.ToArray();
            var plan = CreatePlan(request);
            Assert.AreEqual(expectedPlanId, plan.PlanId);
            return ValueTask.FromResult(new CircleFilesMemberMappingResult("mapped", plan));
        }

        public ValueTask<CircleFilesMemberMappingResult> UnmapAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CircleFilesMemberMappingResult("unmapped", CreatePlan(request)));

        private static CircleFilesMemberMappingPlan CreatePlan(
            CircleFilesMemberMappingRequest request) =>
            new(
                CircleFilesMemberMappingContract.Version,
                new string('e', 64),
                request.Endpoint,
                $@"\\{request.Endpoint}\balls-project-files",
                request.Endpoint,
                request.DriveLetter,
                request.CircleName,
                request.GrantOwnershipId,
                [request.DriveLetter],
                ["Map the exact imported Member grant."]);
    }

    private sealed class PassthroughProtector : IPrivateMaterialProtector
    {
        public string Scheme => "test-passthrough-v1";

        public byte[] Protect(ReadOnlySpan<byte> privateMaterial) => privateMaterial.ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial) => protectedMaterial.ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"balls-daemon-admission-{Guid.CreateVersion7():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
