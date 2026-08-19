using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Balls.Cli;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;

namespace Balls.Cli.Tests;

[TestClass]
[TestCategory("ProcessIntegration")]
public sealed class ProcessAcceptanceTests
{
    [TestMethod]
    public async Task Cli_process_uses_stable_usage_unavailable_and_rejected_exit_codes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        var usage = await RunCliAsync("--pipe-name", "bad/name", "status");
        var unavailable = await RunCliAsync(
            "--pipe-name",
            $"balls-tests-{Guid.NewGuid():N}",
            "status");

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-acceptance-{Guid.NewGuid():N}";
        using var daemon = StartDaemon(directory.Path, pipeName, "Process-PC");
        try
        {
            await WaitUntilReadyAsync(daemon, pipeName);
            var rejected = await RunCliAsync(
                "--pipe-name",
                pipeName,
                "circle",
                "create",
                "   ",
                "--owner",
                "Alice");

            Assert.AreEqual(CliExitCodes.UsageError, usage.ExitCode);
            Assert.AreEqual(CliExitCodes.DaemonUnavailable, unavailable.ExitCode);
            Assert.AreEqual(CliExitCodes.RequestRejected, rejected.ExitCode);
            StringAssert.Contains(usage.StandardError, "invalid --pipe-name");
            StringAssert.Contains(unavailable.StandardError, "ballsd is unavailable");
            StringAssert.Contains(rejected.StandardError, "Circle name is required");
            Assert.IsFalse(usage.StandardError.Contains("Exception", StringComparison.Ordinal));
            Assert.IsFalse(rejected.StandardError.Contains("Exception", StringComparison.Ordinal));
        }
        finally
        {
            await StopProcessAsync(daemon);
        }
    }

    [TestMethod]
    public async Task Separate_daemon_and_cli_processes_preserve_the_first_circle_across_restart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-acceptance-{Guid.NewGuid():N}";
        var firstDaemon = StartDaemon(directory.Path, pipeName, "Process-PC");
        Process? restartedDaemon = null;

        try
        {
            await WaitUntilReadyAsync(firstDaemon, pipeName);
            var firstStatus = await RunCliAsync(
                "--pipe-name",
                pipeName,
                "status",
                "--output",
                "json");
            var create = await RunCliAsync(
                "--pipe-name",
                pipeName,
                "circle",
                "create",
                "Process Circle",
                "--owner",
                "Alice",
                "--request-id",
                "0198c2d8-b000-7000-8000-000000000301",
                "--output",
                "json");

            Assert.AreEqual(CliExitCodes.Success, firstStatus.ExitCode, firstStatus.StandardError);
            Assert.AreEqual(CliExitCodes.Success, create.ExitCode, create.StandardError);
            var statusBeforeRestart = JsonSerializer.Deserialize<StatusResponse>(
                firstStatus.StandardOutput,
                ControlJson.Options);
            var created = JsonSerializer.Deserialize<CircleDetailsResponse>(
                create.StandardOutput,
                ControlJson.Options);
            Assert.IsNotNull(statusBeforeRestart);
            Assert.IsNotNull(created);

            await StopProcessAsync(firstDaemon);

            restartedDaemon = StartDaemon(directory.Path, pipeName, "Changed-PC");
            await WaitUntilReadyAsync(restartedDaemon, pipeName);
            var statusAfterRestart = await RunCliAsync(
                "--pipe-name",
                pipeName,
                "status",
                "--output",
                "json");
            var circles = await RunCliAsync(
                "--pipe-name",
                pipeName,
                "circle",
                "list",
                "--output",
                "json");
            var members = await RunCliAsync(
                "--pipe-name",
                pipeName,
                "member",
                "list",
                "--circle",
                created.Circle.Id,
                "--output",
                "json");
            var nodes = await RunCliAsync(
                "--pipe-name",
                pipeName,
                "node",
                "list",
                "--circle",
                created.Circle.Id,
                "--output",
                "json");

            Assert.AreEqual(CliExitCodes.Success, statusAfterRestart.ExitCode, statusAfterRestart.StandardError);
            Assert.AreEqual(CliExitCodes.Success, circles.ExitCode, circles.StandardError);
            Assert.AreEqual(CliExitCodes.Success, members.ExitCode, members.StandardError);
            Assert.AreEqual(CliExitCodes.Success, nodes.ExitCode, nodes.StandardError);
            var restartedStatus = JsonSerializer.Deserialize<StatusResponse>(
                statusAfterRestart.StandardOutput,
                ControlJson.Options);
            Assert.IsNotNull(restartedStatus);
            Assert.AreEqual(statusBeforeRestart.Node.Id, restartedStatus.Node.Id);
            Assert.AreEqual("Process-PC", restartedStatus.Node.DisplayName);
            StringAssert.Contains(circles.StandardOutput, created.Circle.Id);
            StringAssert.Contains(members.StandardOutput, "Alice");
            StringAssert.Contains(nodes.StandardOutput, "Process-PC");
        }
        finally
        {
            await StopProcessAsync(firstDaemon);
            firstDaemon.Dispose();
            if (restartedDaemon is not null)
            {
                await StopProcessAsync(restartedDaemon);
                restartedDaemon.Dispose();
            }
        }
    }

    private static Process StartDaemon(string dataDirectory, string pipeName, string nodeName)
    {
        var executable = Path.Combine(
            Path.GetDirectoryName(typeof(CliApplication).Assembly.Location)!,
            "ballsd.exe");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--data-directory");
        startInfo.ArgumentList.Add(dataDirectory);
        startInfo.ArgumentList.Add("--pipe-name");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--node-name");
        startInfo.ArgumentList.Add(nodeName);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ballsd.");
    }

    private static async Task<ProcessResult> RunCliAsync(params string[] arguments)
    {
        var executable = Path.Combine(
            Path.GetDirectoryName(typeof(CliApplication).Assembly.Location)!,
            "balls.exe");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start balls.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await standardOutput).Trim(),
            (await standardError).Trim());
    }

    private static async Task WaitUntilReadyAsync(Process process, string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                var error = await process.StandardError.ReadToEndAsync();
                Assert.Fail($"ballsd exited before becoming ready: {error}");
            }

            try
            {
                using var client = WindowsNamedPipeHttpClient.Create(
                    pipeName,
                    TimeSpan.FromMilliseconds(200));
                var status = await client.GetFromJsonAsync<StatusResponse>(
                    ControlRoutes.Status,
                    ControlJson.Options,
                    timeout.Token);
                if (status is not null)
                {
                    return;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or IOException
                    or TaskCanceledException
                    or TimeoutException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        Assert.Fail("ballsd did not become ready within five seconds.");
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

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
            for (var attempt = 0; attempt < 20 && Directory.Exists(Path); attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(50));
                }
            }
        }
    }
}
