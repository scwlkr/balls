using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class RevitServerSetupApplicationTests
{
    [TestMethod]
    public async Task Browser_selection_is_session_bound_and_projects_the_same_platform_preview()
    {
        var inspector = new RecordingInspector(ReadyReport());
        var application = new RevitServerSetupApplication(
            new StubPicker(new RevitServerMediaSelection(MediaPath, "Revit_Server_2027_win_db.sfx.exe")),
            inspector);

        var selected = await application.SelectMediaAsync("session-a", CancellationToken.None);
        var wrongSession = await application.InspectSelectedAsync(
            "session-b",
            selected!.Value.Id,
            CancellationToken.None);
        var result = await application.InspectSelectedAsync(
            "session-a",
            selected.Value.Id,
            CancellationToken.None);

        Assert.AreEqual("blocked", wrongSession.Status);
        Assert.AreEqual("media_selection_expired", wrongSession.Checks.Single().Code);
        Assert.AreEqual("ready", result.Status);
        Assert.IsNotNull(result.Plan);
        CollectionAssert.AreEqual(new[] { "Host", "Admin" }, result.Plan.EnabledRoles.ToArray());
        CollectionAssert.AreEqual(new[] { "Accelerator" }, result.Plan.ForbiddenRoles.ToArray());
        Assert.AreEqual(MediaPath, inspector.MediaPaths.Single());
        Assert.IsFalse(result.Plan.Media.Contains(@"C:\Media", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Cancelled_selection_does_not_inspect_or_create_a_plan()
    {
        var inspector = new RecordingInspector(ReadyReport());
        var application = new RevitServerSetupApplication(new StubPicker(null), inspector);

        var selected = await application.SelectMediaAsync("session-a", CancellationToken.None);

        Assert.IsNull(selected);
        Assert.AreEqual(0, inspector.MediaPaths.Count);
    }

    [TestMethod]
    public async Task Blocked_platform_result_never_contains_an_approvable_plan()
    {
        var application = new RevitServerSetupApplication(
            new StubPicker(null),
            new RecordingInspector(new RevitServerInspectionReport(
                RevitServerReadinessStatus.Blocked,
                "Setup is blocked. Nothing was changed.",
                [new RevitServerReadinessCheck("network", RevitServerReadinessStatus.Blocked, "public_network_refused", "Disconnect the Public network.")],
                null)));

        var result = await application.InspectPathAsync(MediaPath, CancellationToken.None);

        Assert.AreEqual("blocked", result.Status);
        Assert.IsNull(result.Plan);
        Assert.AreEqual("public_network_refused", result.Checks.Single().Code);
    }

    [TestMethod]
    public async Task Begin_requires_explicit_consent_and_commits_Autodesk_handoff_before_launch()
    {
        var setup = new StubSetupOperator();
        var store = new MemoryRevitServerSetupStateStore();
        var application = CreateApplication(setup, HealthyInspector(), store);
        var selected = await application.SelectMediaAsync("session-a", CancellationToken.None);
        var plan = RevitServerSetupPlanFactory.Create(ReadyReport().Snapshot!);

        var refusal = await Assert.ThrowsExactlyAsync<RevitServerSetupException>(() =>
            application.BeginSelectedAsync(
                "session-a",
                new BeginRevitServerSetupRequest(selected!.Value.Id, plan.PlanDigest, false),
                CancellationToken.None).AsTask());
        Assert.AreEqual("setup_consent_required", refusal.Code);

        var beginning = await application.BeginSelectedAsync(
            "session-a",
            new BeginRevitServerSetupRequest(selected!.Value.Id, plan.PlanDigest, true),
            CancellationToken.None);
        await setup.Launched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(RevitServerSetupStages.ApplyingPrerequisites, beginning.Stage);
        Assert.AreEqual(RevitServerSetupStages.AwaitingAutodesk, setup.StageObservedAtLaunch);
        Assert.AreEqual(MediaPath, setup.MediaPath);
        Assert.AreEqual(0, setup.ArgumentCount);
        Assert.AreEqual(RevitServerSetupStages.AwaitingAutodesk, application.GetStatus().Stage);
    }

    [TestMethod]
    public async Task Restart_required_is_blocked_and_never_launches_Autodesk()
    {
        var setup = new StubSetupOperator
        {
            PreparationResult = new(
                RevitServerSetupMutationStatus.RestartRequired,
                "Restart Windows, then inspect again."),
        };
        var application = CreateApplication(setup, HealthyInspector(), new MemoryRevitServerSetupStateStore());
        var selected = await application.SelectMediaAsync("session-a", CancellationToken.None);
        var digest = RevitServerSetupPlanFactory.Create(ReadyReport().Snapshot!).PlanDigest;

        await application.BeginSelectedAsync(
            "session-a",
            new BeginRevitServerSetupRequest(selected!.Value.Id, digest, true),
            CancellationToken.None);
        await WaitUntilAsync(() => application.GetStatus().Stage == RevitServerSetupStages.Blocked);

        Assert.IsFalse(setup.Launched.Task.IsCompleted);
        StringAssert.Contains(application.GetStatus().Summary, "Restart");
    }

    [TestMethod]
    public async Task Verify_refuses_incomplete_health_and_persists_exact_healthy_result()
    {
        var setup = new StubSetupOperator();
        var health = new StubHealthInspector(new RevitServerHealthReport(
            RevitServerHealthStatus.Incomplete,
            "Setup is incomplete.",
            [new RevitServerHealthCheck("roles", RevitServerHealthStatus.Incomplete, "roles_incorrect", "Choose Host + Admin.")]));
        var application = CreateApplication(setup, health, new MemoryRevitServerSetupStateStore());
        var selected = await application.SelectMediaAsync("session-a", CancellationToken.None);
        var digest = RevitServerSetupPlanFactory.Create(ReadyReport().Snapshot!).PlanDigest;
        await application.BeginSelectedAsync(
            "session-a",
            new BeginRevitServerSetupRequest(selected!.Value.Id, digest, true),
            CancellationToken.None);
        await setup.Launched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var incomplete = await application.VerifyAsync(CancellationToken.None);
        Assert.AreEqual(RevitServerSetupStages.Incomplete, incomplete.Stage);
        Assert.AreEqual("roles_incorrect", incomplete.Checks.Single().Code);

        health.Result = HealthyReport();
        var healthy = await application.VerifyAsync(CancellationToken.None);
        Assert.AreEqual(RevitServerSetupStages.ReadyForHandoff, healthy.Stage);
        StringAssert.Contains(healthy.Summary, "healthy");
    }

    [TestMethod]
    public void Interrupted_mutation_is_not_replayed_after_daemon_restart()
    {
        var store = new MemoryRevitServerSetupStateStore();
        store.Save(State(RevitServerSetupStages.ApplyingPrerequisites));
        var application = CreateApplication(new StubSetupOperator(), HealthyInspector(), store);

        var status = application.GetStatus();

        Assert.AreEqual(RevitServerSetupStages.Blocked, status.Stage);
        StringAssert.Contains(status.Summary, "interrupted");
    }

    [TestMethod]
    public void Corrupt_persisted_state_is_blocked_and_not_overwritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"balls-revit-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.json");
        File.WriteAllText(path, "{}");
        try
        {
            var application = CreateApplication(
                new StubSetupOperator(),
                HealthyInspector(),
                new FileRevitServerSetupStateStore(path));

            var status = application.GetStatus();

            Assert.AreEqual(RevitServerSetupStages.Blocked, status.Stage);
            StringAssert.Contains(status.Summary, "unreadable");
            Assert.AreEqual("{}", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RevitServerSetupApplication CreateApplication(
        StubSetupOperator setup,
        StubHealthInspector health,
        IRevitServerSetupStateStore store)
    {
        setup.State = store;
        return new RevitServerSetupApplication(
            new StubPicker(new RevitServerMediaSelection(MediaPath, "Revit_Server_2027_win_db.sfx.exe")),
            new RecordingInspector(ReadyReport()),
            setupOperator: setup,
            healthInspector: health,
            stateStore: store);
    }

    private static StubHealthInspector HealthyInspector() => new(HealthyReport());

    private static RevitServerHealthReport HealthyReport() => new(
        RevitServerHealthStatus.Healthy,
        "Revit Server 2027 Host + Admin is healthy.",
        [new RevitServerHealthCheck("roles", RevitServerHealthStatus.Healthy, "roles_exact", "Host + Admin are healthy.")]);

    private static RevitServerSetupState State(string stage)
    {
        var core = RevitServerSetupPlanFactory.Create(ReadyReport().Snapshot!);
        return new RevitServerSetupState(
            1,
            1,
            Guid.NewGuid().ToString("D"),
            stage,
            "In progress.",
            MediaPath,
            core.PlanDigest,
            new RevitServerSetupPlanResponse(
                core.PlanDigest,
                core.Machine,
                core.Windows,
                core.Media,
                core.MediaSha256,
                core.EnabledRoles,
                core.ForbiddenRoles,
                core.DataPaths,
                core.WindowsPrerequisites,
                core.AclIntent,
                core.DefaultWebSiteEffects,
                core.RsnIni,
                core.FirewallEffects,
                core.VerificationActions,
                core.BallsOwnedState,
                core.AutodeskOwnedState),
            [],
            DateTimeOffset.UtcNow);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static RevitServerInspectionReport ReadyReport() => new(
        RevitServerReadinessStatus.Ready,
        "Ready. Nothing changed.",
        [new RevitServerReadinessCheck("windows-server", RevitServerReadinessStatus.Ready, "ready", "Ready.")],
        new RevitServerInspectionSnapshot(
            "BALLS-RS27",
            "Windows Server 2022 Standard",
            20348,
            "Server",
            "D:",
            100,
            @"D:\RevitServer\2027",
            "location-fingerprint",
            new RevitServerMediaIdentity(
                "Revit_Server_2027_win_db.sfx.exe",
                "Autodesk, Inc.",
                "Autodesk Revit Server 2027",
                "27.0.4.412",
                new string('a', 64)),
            false,
            []));

    private sealed class StubPicker(RevitServerMediaSelection? result) : IRevitServerMediaPicker
    {
        public ValueTask<RevitServerMediaSelection?> SelectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingInspector(RevitServerInspectionReport result) : IRevitServerReadinessInspector
    {
        public List<string> MediaPaths { get; } = [];

        public ValueTask<RevitServerInspectionReport> InspectAsync(
            string mediaPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MediaPaths.Add(mediaPath);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubSetupOperator : IRevitServerSetupOperator
    {
        public IRevitServerSetupStateStore? State { get; set; }
        public RevitServerSetupPreparationResult PreparationResult { get; set; } = new(
            RevitServerSetupMutationStatus.Applied,
            "Prepared.");
        public TaskCompletionSource Launched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? StageObservedAtLaunch { get; private set; }
        public string? MediaPath { get; private set; }
        public int ArgumentCount { get; private set; }

        public ValueTask<RevitServerSetupPreparationResult> PrepareAsync(
            RevitServerSetupPreparationRequest request,
            CancellationToken cancellationToken) => ValueTask.FromResult(PreparationResult);

        public ValueTask LaunchAutodeskAsync(string mediaPath, CancellationToken cancellationToken)
        {
            StageObservedAtLaunch = State?.Load()?.Stage;
            MediaPath = mediaPath;
            ArgumentCount = 0;
            Launched.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHealthInspector(RevitServerHealthReport result) : IRevitServerHealthInspector
    {
        public RevitServerHealthReport Result { get; set; } = result;

        public ValueTask<RevitServerHealthReport> InspectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result);
    }

    private const string MediaPath = @"C:\Media\Revit_Server_2027_win_db.sfx.exe";
}
