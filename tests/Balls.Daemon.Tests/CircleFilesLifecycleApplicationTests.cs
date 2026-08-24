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

    [TestMethod]
    public async Task Unknown_lifecycle_targets_fail_before_audit_insert()
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
        var application = new CircleFilesLifecycleApplication(
            files,
            store,
            store,
            new UnsupportedCircleFilesLifecycleManager(),
            time);
        var unknownContributionId = CircleFilesContributionId.New();
        var unknownCircleId = CircleId.New();
        var grantId = MemberAccessGrantId.New();

        await Assert.ThrowsExactlyAsync<LocalStateException>(() => application.RevokeGrantAsync(
            circle.Circle.Id,
            unknownContributionId,
            grantId,
            new MemberAccessGrantRevocationRequestId(Guid.CreateVersion7()),
            1,
            CancellationToken.None));
        await Assert.ThrowsExactlyAsync<LocalStateException>(() => application.RemoveGrantAsync(
            circle.Circle.Id,
            unknownContributionId,
            grantId,
            @"C:\BallsShares\Example",
            new string('a', 64),
            terminateOpenSessions: false,
            CancellationToken.None));
        await Assert.ThrowsExactlyAsync<LocalStateException>(() => application.RemoveHostAsync(
            circle.Circle.Id,
            unknownContributionId,
            @"C:\BallsShares\Example",
            new string('b', 64),
            terminateOpenSessions: false,
            CancellationToken.None));
        await Assert.ThrowsExactlyAsync<LocalStateException>(() => application.RemoveHostAsync(
            unknownCircleId,
            contribution.Id,
            @"C:\BallsShares\Example",
            new string('c', 64),
            terminateOpenSessions: false,
            CancellationToken.None));

        Assert.AreEqual(
            0,
            (await store.ListCircleFilesLifecycleAuditEventsAsync(circle.Circle.Id)).Count);
        Assert.AreEqual(
            0,
            (await store.ListCircleFilesLifecycleAuditEventsAsync(unknownCircleId)).Count);
    }

    [TestMethod]
    public async Task Exact_unmap_audit_survives_restart_and_records_idempotent_retry()
    {
        using var directory = new TemporaryDirectory();
        CircleId circleId;
        CircleFilesContributionId contributionId;
        MemberAccessGrantId grantId;

        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         PassthroughProtector.Instance))
        {
            var time = new FixedTimeProvider(Now);
            var circles = new CircleApplication(store, time, "Alice-PC");
            var circle = await circles.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Example Studio",
                    "Alice"));
            circleId = circle.Circle.Id;
            var files = new CircleFilesApplication(store, store, time);
            var contribution = await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    new CircleFilesContributionRequestId(Guid.CreateVersion7()),
                    circleId,
                    "Project Files"));
            contributionId = contribution.Id;
            var grant = await files.CreateAccessGrantAsync(
                new CreateMemberAccessGrantCommand(
                    new MemberAccessGrantRequestId(Guid.CreateVersion7()),
                    circleId,
                    contributionId,
                    circle.Members.Single().Id,
                    MemberAccessMode.ReadWrite));
            grantId = grant.Id;
            var binding = CreateBinding(circleId, contributionId, grant);
            using (await store.PrepareCircleFilesProviderCredentialAsync(
                       binding,
                       new byte[32]))
            {
                await store.CompleteCircleFilesProviderCredentialAsync(binding);
            }

            var application = new CircleFilesMemberMappingApplication(
                circles,
                files,
                store,
                store,
                new StubMemberMapper("unmapped"),
                time);
            var result = await application.UnmapAsync(
                circleId,
                contributionId,
                grantId,
                "192.168.50.10",
                "M",
                CancellationToken.None);

            Assert.AreEqual("unmapped", result.Status);
        }

        await using var reopened = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            PassthroughProtector.Instance);
        var retryTime = new FixedTimeProvider(Now.AddMinutes(1));
        var retry = new CircleFilesMemberMappingApplication(
            new CircleApplication(reopened, retryTime, "Alice-PC"),
            new CircleFilesApplication(reopened, reopened, retryTime),
            reopened,
            reopened,
            new StubMemberMapper("already-unmapped"),
            retryTime);
        var retryResult = await retry.UnmapAsync(
            circleId,
            contributionId,
            grantId,
            "192.168.50.10",
            "M",
            CancellationToken.None);
        var events = await reopened.ListCircleFilesLifecycleAuditEventsAsync(circleId);

        Assert.AreEqual("already-unmapped", retryResult.Status);
        CollectionAssert.AreEqual(
            new[]
            {
                "mapping-unmap:requested",
                "mapping-unmap:unmapped",
                "mapping-unmap:requested",
                "mapping-unmap:already-unmapped",
            },
            events.Select(value => $"{value.Operation}:{value.Outcome}").ToArray());

        await Assert.ThrowsExactlyAsync<LocalStateException>(() => retry.UnmapAsync(
            circleId,
            CircleFilesContributionId.New(),
            grantId,
            "192.168.50.10",
            "M",
            CancellationToken.None));
        Assert.AreEqual(
            events.Count,
            (await reopened.ListCircleFilesLifecycleAuditEventsAsync(circleId)).Count);
    }

    private static CircleFilesProviderCredentialBinding CreateBinding(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrant grant) =>
        new(
            grant.Id.ToString(),
            circleId.ToString(),
            contributionId.ToString(),
            grant.MemberId.ToString(),
            "windows-smb-3.1.1-v1",
            "BallsG-test",
            new string('a', 64),
            "read-write",
            grant.Generation);

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

    private sealed class StubMemberMapper(string unmapStatus) : ICircleFilesMemberMapper
    {
        public ValueTask<CircleFilesMemberMappingPlan> PreviewAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesMemberMappingPlan>(new NotSupportedException());

        public ValueTask<CircleFilesMemberMappingInspection> InspectAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesMemberMappingInspection>(new NotSupportedException());

        public ValueTask<CircleFilesMemberMappingResult> MapAsync(
            CircleFilesMemberMappingRequest request,
            string expectedPlanId,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesMemberMappingResult>(new NotSupportedException());

        public ValueTask<CircleFilesMemberMappingResult> UnmapAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CircleFilesMemberMappingResult(
                unmapStatus,
                new CircleFilesMemberMappingPlan(
                    CircleFilesMemberMappingContract.Version,
                    new string('b', 64),
                    request.Endpoint,
                    $@"\\{request.Endpoint}\balls-test",
                    request.Endpoint,
                    request.DriveLetter,
                    request.CircleName,
                    new string('c', 64),
                    [request.DriveLetter],
                    ["Unmap the exact owned mapping."])));
        }
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
