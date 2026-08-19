using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Balls.Cli;
using Balls.Host;
using Balls.Protocol.Control.V1;

namespace Balls.Cli.Tests;

[TestClass]
[TestCategory("ProcessIntegration")]
public sealed class ProcessAcceptanceTests
{
    [TestMethod]
    public async Task Cli_process_uses_stable_usage_unavailable_and_rejected_exit_codes()
    {
        using var directory = new TemporaryDirectory();
        var endpoint = GetEndpoint(directory.Path);
        var invalidEndpoint = OperatingSystem.IsWindows() ? "bad/name" : "relative.sock";
        var usage = await RunCliAsync(
            "--output",
            "json",
            "--pipe-name",
            invalidEndpoint,
            "status");
        var unavailable = await RunCliAsync(
            "--output",
            "json",
            "--pipe-name",
            GetUnavailableEndpoint(directory.Path),
            "status");
        using var daemon = StartDaemon(
            Path.Combine(directory.Path, "state"),
            endpoint,
            "Process-PC");
        try
        {
            await WaitUntilReadyAsync(daemon, endpoint);
            var rejected = await RunCliAsync(
                "--output",
                "json",
                "--pipe-name",
                endpoint,
                "circle",
                "create",
                "   ",
                "--owner",
                "Alice");

            Assert.AreEqual(CliExitCodes.UsageError, usage.ExitCode);
            Assert.AreEqual(CliExitCodes.DaemonUnavailable, unavailable.ExitCode);
            Assert.AreEqual(CliExitCodes.RequestRejected, rejected.ExitCode);
            AssertJsonError(usage.StandardError, "usage_error", "invalid --pipe-name value.");
            AssertJsonError(
                unavailable.StandardError,
                "daemon_unavailable",
                "ballsd is unavailable");
            AssertJsonError(
                rejected.StandardError,
                "circle_name_required",
                "Circle name is required");
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
        using var directory = new TemporaryDirectory();
        var stateDirectory = Path.Combine(directory.Path, "state");
        var endpoint = GetEndpoint(directory.Path);
        var firstDaemon = StartDaemon(stateDirectory, endpoint, "Process-PC");
        Process? restartedDaemon = null;

        try
        {
            await WaitUntilReadyAsync(firstDaemon, endpoint);
            var firstStatus = await RunCliAsync(
                "--output",
                "json",
                "--pipe-name",
                endpoint,
                "status");
            var create = await RunCliAsync(
                "--output",
                "json",
                "--pipe-name",
                endpoint,
                "circle",
                "create",
                "Process Circle",
                "--owner",
                "Alice",
                "--request-id",
                "0198c2d8-b000-7000-8000-000000000301");

            Assert.AreEqual(CliExitCodes.Success, firstStatus.ExitCode, firstStatus.StandardError);
            Assert.AreEqual(CliExitCodes.Success, create.ExitCode, create.StandardError);
            var statusBeforeRestart = DeserializeResult<StatusResponse>(firstStatus.StandardOutput);
            var created = DeserializeResult<CircleDetailsResponse>(create.StandardOutput);

            await StopProcessAsync(firstDaemon);

            restartedDaemon = StartDaemon(stateDirectory, endpoint, "Changed-PC");
            await WaitUntilReadyAsync(restartedDaemon, endpoint);
            var statusAfterRestart = await RunCliAsync(
                "--output",
                "json",
                "--pipe-name",
                endpoint,
                "status");
            var circles = await RunCliAsync(
                "--output",
                "json",
                "--pipe-name",
                endpoint,
                "circle",
                "list");
            var members = await RunCliAsync(
                "--output",
                "json",
                "--pipe-name",
                endpoint,
                "member",
                "list",
                "--circle",
                created.Circle.Id);
            var nodes = await RunCliAsync(
                "--output",
                "json",
                "--pipe-name",
                endpoint,
                "node",
                "list",
                "--circle",
                created.Circle.Id);

            Assert.AreEqual(CliExitCodes.Success, statusAfterRestart.ExitCode, statusAfterRestart.StandardError);
            Assert.AreEqual(CliExitCodes.Success, circles.ExitCode, circles.StandardError);
            Assert.AreEqual(CliExitCodes.Success, members.ExitCode, members.StandardError);
            Assert.AreEqual(CliExitCodes.Success, nodes.ExitCode, nodes.StandardError);
            var restartedStatus = DeserializeResult<StatusResponse>(statusAfterRestart.StandardOutput);
            Assert.AreEqual(statusBeforeRestart.Node.Id, restartedStatus.Node.Id);
            Assert.AreEqual("Process-PC", restartedStatus.Node.DisplayName);
            var circleList = DeserializeResult<CircleListResponse>(circles.StandardOutput);
            var memberList = DeserializeResult<MemberListResponse>(members.StandardOutput);
            var nodeList = DeserializeResult<NodeListResponse>(nodes.StandardOutput);
            Assert.AreEqual(created.Circle.Id, circleList.Circles.Single().Id);
            Assert.AreEqual(created.Circle.Id, memberList.CircleId);
            Assert.AreEqual("Alice", memberList.Members.Single().DisplayName);
            Assert.AreEqual(created.Circle.Id, nodeList.CircleId);
            Assert.AreEqual(statusBeforeRestart.Node.Id, nodeList.Nodes.Single().Id);
            Assert.AreEqual("Process-PC", nodeList.Nodes.Single().DisplayName);
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

    private static Process StartDaemon(string dataDirectory, string endpoint, string nodeName)
    {
        var executable = Path.Combine(
            Path.GetDirectoryName(typeof(CliApplication).Assembly.Location)!,
            OperatingSystem.IsWindows() ? "ballsd.exe" : "ballsd");
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
        startInfo.ArgumentList.Add(endpoint);
        startInfo.ArgumentList.Add("--node-name");
        startInfo.ArgumentList.Add(nodeName);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ballsd.");
    }

    private static async Task<ProcessResult> RunCliAsync(params string[] arguments)
    {
        var executable = Path.Combine(
            Path.GetDirectoryName(typeof(CliApplication).Assembly.Location)!,
            OperatingSystem.IsWindows() ? "balls.exe" : "balls");
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

    private static async Task WaitUntilReadyAsync(Process process, string endpoint)
    {
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
                var selection = HostPlatformSelector.SelectCurrent();
                var host = ((SupportedHostPlatform)selection).Platform;
                using var client = host.LocalControlClient.CreateClient(
                    endpoint,
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

    private static T DeserializeResult<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(1, document.RootElement.GetProperty("outputVersion").GetInt32());
        return document.RootElement.GetProperty("result").Deserialize<T>(ControlJson.Options)
            ?? throw new AssertFailedException("CLI result was null.");
    }

    private static void AssertJsonError(
        string json,
        string expectedCode,
        string expectedMessageFragment)
    {
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(1, document.RootElement.GetProperty("outputVersion").GetInt32());
        var error = document.RootElement.GetProperty("error");
        Assert.AreEqual(expectedCode, error.GetProperty("code").GetString());
        StringAssert.Contains(error.GetProperty("message").GetString(), expectedMessageFragment);
    }

    private static string GetEndpoint(string root)
    {
        return OperatingSystem.IsWindows()
            ? $"balls-acceptance-{Guid.NewGuid():N}"
            : Path.Combine(root, "runtime", "control.sock");
    }

    private static string GetUnavailableEndpoint(string root)
    {
        return OperatingSystem.IsWindows()
            ? $"balls-tests-{Guid.NewGuid():N}"
            : Path.Combine(root, "unavailable", "control.sock");
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
                OperatingSystem.IsLinux()
                    ? System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".local",
                        "state")
                    : System.IO.Path.GetTempPath(),
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
