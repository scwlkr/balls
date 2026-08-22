using System.Text.Json;
using Balls.Cli;
using Balls.Daemon;
using Balls.Platform;
using Balls.Protocol.Control.V1;

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

    private static async Task<CliResult> RunAsync(string pipeName, params string[] command)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var arguments = new[] { "--pipe-name", pipeName }.Concat(command).ToArray();
        var exitCode = await CliApplication.RunAsync(arguments, output, error);
        return new CliResult(exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    private static T DeserializeResult<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(1, document.RootElement.GetProperty("outputVersion").GetInt32());
        return document.RootElement.GetProperty("result").Deserialize<T>(ControlJson.Options)
            ?? throw new AssertFailedException("CLI result was null.");
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-tests",
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
