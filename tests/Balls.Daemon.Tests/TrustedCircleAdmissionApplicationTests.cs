using System.Net;
using Balls.Core;
using Balls.Daemon;
using Balls.Protocol.Remote.V1;
using Balls.Storage.Sqlite;
using Balls.Transport.Lan;

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
        CircleDetails joined;
        CircleMessageId messageId = new(Guid.CreateVersion7());
        await using (var anchorStore = await SqliteLocalStateStore.OpenAsync(
                         anchorDirectory.Path,
                         protector))
        await using (var joinerStore = await SqliteLocalStateStore.OpenAsync(
                         joinerDirectory.Path,
                         protector))
        await using (var listener = new TcpLanTransportListener(
                         new IPEndPoint(IPAddress.Loopback, 0)))
        {
            var now = new DateTimeOffset(2026, 8, 20, 20, 0, 0, TimeSpan.Zero);
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
            var serve = ServeOneAsync(
                listener,
                anchorAdmission,
                timeout.Token);
            joined = await joinerAdmission.JoinAsync(
                package,
                listener.BoundAddress,
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

            var retried = await joinerAdmission.JoinAsync(
                package,
                listener.BoundAddress,
                "Bob",
                timeout.Token);
            Assert.AreEqual(2, retried.Members.Count);
            Assert.AreEqual(2, retried.Nodes.Count);

            await using var messageListener = new TcpLanTransportListener(
                new IPEndPoint(IPAddress.Loopback, 0));
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
        }

        await using var reopenedAnchor = await SqliteLocalStateStore.OpenAsync(
            anchorDirectory.Path,
            protector);
        await using var reopenedJoiner = await SqliteLocalStateStore.OpenAsync(
            joinerDirectory.Path,
            protector);
        var anchorRestart = await reopenedAnchor.GetCircleAsync(circleId);
        var joinerRestart = await reopenedJoiner.GetCircleAsync(circleId);

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
        var anchorMessagesAfterRestart = await reopenedAnchor.ListCircleMessagesAsync(circleId);
        var joinerMessagesAfterRestart = await reopenedJoiner.ListCircleMessagesAsync(circleId);
        Assert.AreEqual(1, anchorMessagesAfterRestart.Count);
        Assert.AreEqual(anchorMessagesAfterRestart.Single(), joinerMessagesAfterRestart.Single());
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
