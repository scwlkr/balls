using Balls.Platform;

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
                "Autodesk Revit 2027",
                "27.0.0.0",
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

    private const string MediaPath = @"C:\Media\Revit_Server_2027_win_db.sfx.exe";
}
