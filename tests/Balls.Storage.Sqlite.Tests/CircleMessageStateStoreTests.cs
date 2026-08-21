using System.Security.Cryptography;
using Balls.Core;
using Balls.Storage.Sqlite;

namespace Balls.Storage.Sqlite.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleMessageStateStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Created_Circle_has_a_local_author_and_persists_idempotent_ordered_messages()
    {
        using var directory = new TemporaryDirectory();
        CircleDetails circle;
        CircleMessageCommit commit;
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var application = new CircleApplication(store, new FixedTimeProvider(Now), "Alice-PC");
            circle = await application.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Example Studio",
                    "Alice"));
            var author = await store.GetLocalCircleMessageAuthorAsync(circle.Circle.Id);
            Assert.IsNotNull(author);
            Assert.AreEqual(circle.Members.Single().Id, author.MemberId);
            Assert.AreEqual(circle.Nodes.Single().NodeId, author.NodeId);

            var outgoingId = CircleMessageId.New();
            var prepared = await store.PrepareOutgoingCircleMessageAsync(
                outgoingId,
                circle.Circle.Id,
                "Hello from Alice.",
                Now);
            var preparedRetry = await store.PrepareOutgoingCircleMessageAsync(
                outgoingId,
                circle.Circle.Id,
                "Hello from Alice.",
                Now.AddMinutes(1));
            Assert.AreEqual(prepared, preparedRetry);
            await Assert.ThrowsExactlyAsync<LocalStateConflictException>(() =>
                store.PrepareOutgoingCircleMessageAsync(
                    outgoingId,
                    circle.Circle.Id,
                    "different",
                    Now));

            var signature = await store.SignWithLocalCircleMemberAsync(
                circle.Circle.Id,
                "message transcript"u8.ToArray());
            Assert.IsTrue(IdentityCryptography.Verify(
                "message transcript"u8,
                signature,
                author.MemberCredential));

            var next = await store.GetNextCircleMessageSequenceAsync(circle.Circle.Id);
            Assert.AreEqual(1, next);
            commit = new CircleMessageCommit(
                new PersistedCircleMessage(
                    CircleMessageId.New(),
                    circle.Circle.Id,
                    author.MemberId,
                    author.NodeId,
                    "Hello from Alice.",
                    Now,
                    next,
                    Now.AddSeconds(1)),
                SHA256.HashData("signed request"u8),
                "signed request"u8.ToArray(),
                "signed receipt"u8.ToArray());
            var accepted = await store.CommitCircleMessageAsync(commit);
            var retry = await store.CommitCircleMessageAsync(commit);

            Assert.AreEqual(CircleMessageCommitStatus.Accepted, accepted.Status);
            Assert.AreEqual(CircleMessageCommitStatus.IdempotentRetry, retry.Status);
            Assert.AreEqual(2, await store.GetNextCircleMessageSequenceAsync(circle.Circle.Id));
        }

        await using var reopened = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        var messages = await reopened.ListCircleMessagesAsync(circle.Circle.Id);
        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual(commit.Message, messages[0]);
    }

    [TestMethod]
    public async Task Conflicting_reuse_of_a_message_identity_is_rejected_without_overwrite()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        var application = new CircleApplication(store, new FixedTimeProvider(Now), "Alice-PC");
        var circle = await application.CreateCircleAsync(
            new CreateCircleCommand(
                new CreationRequestId(Guid.CreateVersion7()),
                "Example Studio",
                "Alice"));
        var author = await store.GetLocalCircleMessageAuthorAsync(circle.Circle.Id);
        Assert.IsNotNull(author);
        var original = CreateCommit(circle.Circle.Id, author, "first", 1);
        var conflicting = CreateCommit(circle.Circle.Id, author, "different", 2) with
        {
            Message = CreateCommit(circle.Circle.Id, author, "different", 2).Message with
            {
                Id = original.Message.Id,
            },
        };

        await store.CommitCircleMessageAsync(original);
        var result = await store.CommitCircleMessageAsync(conflicting);

        Assert.AreEqual(CircleMessageCommitStatus.Conflict, result.Status);
        Assert.AreEqual("first", (await store.ListCircleMessagesAsync(circle.Circle.Id)).Single().Text);
    }

    private static CircleMessageCommit CreateCommit(
        CircleId circleId,
        LocalCircleMessageAuthor author,
        string text,
        long sequence)
    {
        var encoded = System.Text.Encoding.UTF8.GetBytes(text);
        return new CircleMessageCommit(
            new PersistedCircleMessage(
                CircleMessageId.New(),
                circleId,
                author.MemberId,
                author.NodeId,
                text,
                Now,
                sequence,
                Now.AddSeconds(sequence)),
            SHA256.HashData(encoded),
            encoded,
            [.. encoded, 0x01]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-message-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
