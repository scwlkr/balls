using System.Text.Json;
using Balls.Cli;
using Balls.Daemon;
using Balls.Platform;
using Balls.Protocol.Control.V1;
using static Balls.Cli.Tests.CliTestSupport;

namespace Balls.Cli.Tests;

[TestClass]
[TestCategory("OSIntegration")]
public sealed class CliWindowsSmbReadinessTests
{
    [TestMethod]
    public async Task Cli_reports_Windows_SMB_readiness_through_the_local_contract()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The real Windows SMB readiness contract requires Windows.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(directory.Path, pipeName, "Alice-PC"));

        var structured = await RunAsync(
            pipeName,
            "--output",
            "json",
            "files",
            "readiness");
        var text = await RunAsync(pipeName, "files", "readiness");
        var report = DeserializeResult<CircleFilesReadinessResponse>(structured.StandardOutput);

        Assert.AreEqual(CliExitCodes.Success, structured.ExitCode);
        Assert.AreEqual(CircleFilesReadinessProviders.WindowsSmb311, report.Provider);
        Assert.IsTrue(report.Status is "ready" or "not-ready" or "unknown");
        Assert.HasCount(9, report.Checks);
        StringAssert.StartsWith(text.StandardOutput, "Circle Files readiness:");
        Assert.AreEqual(string.Empty, structured.StandardError);
        Assert.AreEqual(string.Empty, text.StandardError);
    }

}
