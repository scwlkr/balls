using Balls.Core;
using Balls.Platform;
using Balls.Storage.Sqlite;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleFilesLifecycleApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 17, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Session_termination_requires_a_prior_busy_outcome_and_audits_refusal()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            PassthroughProtector.Instance);
        var time = new FixedTimeProvider(Now);
        var circle = await new CircleApplication(store, time, "Alice-PC").CreateCircleAsync(
            new CreateCircleCommand(
                new CreationRequestId(Guid.CreateVersion7()),
                "Example Studio",
                "Alice"));
        var files = new CircleFilesApplication(store, store, time);
        var contribution = await files.CreateContributionAsync(
            new CreateCircleFilesContributionCommand(
                new CircleFilesContributionRequestId(Guid.CreateVersion7()),
                circle.Circle.Id,
                "Project Files"));
        var grantId = MemberAccessGrantId.New();
        var application = new CircleFilesLifecycleApplication(
            files,
            store,
            store,
            new UnsupportedCircleFilesLifecycleManager(),
            time);

        await Assert.ThrowsExactlyAsync<LocalStateConflictException>(() =>
            application.RemoveGrantAsync(
                circle.Circle.Id,
                contribution.Id,
                grantId,
                @"C:\BallsShares\Example",
                new string('a', 64),
                terminateOpenSessions: true,
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<LocalStateConflictException>(() =>
            application.RemoveHostAsync(
                circle.Circle.Id,
                contribution.Id,
                @"C:\BallsShares\Example",
                new string('b', 64),
                terminateOpenSessions: true,
                CancellationToken.None));

        var events = await store.ListCircleFilesLifecycleAuditEventsAsync(circle.Circle.Id);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "grant-cleanup:requested",
                "grant-cleanup:refused",
                "host-remove:requested",
                "host-remove:refused",
            },
            events.Select(value => $"{value.Operation}:{value.Outcome}").ToArray());
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class PassthroughProtector : IPrivateMaterialProtector
    {
        internal static PassthroughProtector Instance { get; } = new();

        public string Scheme => "test-v1";

        public byte[] Protect(ReadOnlySpan<byte> privateMaterial) => privateMaterial.ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial) => protectedMaterial.ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
