using System.Text.Json;
using Balls.Cli;
using Balls.Daemon;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;

namespace Balls.Cli.Tests;

[TestClass]
public sealed class CliApplicationTests
{
    [TestMethod]
    public async Task Cli_rejects_an_invalid_pipe_name_as_usage_without_a_stack_trace()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["--pipe-name", "bad/name", "status"],
            output,
            error);

        Assert.AreEqual(CliExitCodes.UsageError, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "invalid --pipe-name");
        Assert.IsFalse(error.ToString().Contains("Exception", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Cli_rejects_a_request_id_option_without_a_value_before_contacting_the_daemon()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [
                "--pipe-name",
                $"balls-tests-{Guid.NewGuid():N}",
                "circle",
                "create",
                "Example Studio",
                "--owner",
                "Alice",
                "--request-id",
            ],
            output,
            error);

        Assert.AreEqual(CliExitCodes.UsageError, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "--request-id requires a value");
    }

    [TestMethod]
    public async Task Cli_creates_and_lists_a_circle_member_and_node_through_the_daemon_contract()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(directory.Path, pipeName, "Alice-PC"));

        var status = await RunAsync(pipeName, "status", "--output", "json");
        Assert.AreEqual(CliExitCodes.Success, status.ExitCode);
        var statusResponse = JsonSerializer.Deserialize<StatusResponse>(
            status.StandardOutput,
            ControlJson.Options);
        Assert.IsNotNull(statusResponse);
        Assert.AreEqual("Alice-PC", statusResponse.Node.DisplayName);

        var create = await RunAsync(
            pipeName,
            "circle",
            "create",
            "Example Studio",
            "--owner",
            "Alice",
            "--request-id",
            "0198c2d8-b000-7000-8000-000000000201",
            "--output",
            "json");
        Assert.AreEqual(CliExitCodes.Success, create.ExitCode);
        var created = JsonSerializer.Deserialize<CircleDetailsResponse>(
            create.StandardOutput,
            ControlJson.Options);
        Assert.IsNotNull(created);
        var circleId = created.Circle.Id;

        var circles = await RunAsync(pipeName, "circle", "list", "--output", "json");
        var members = await RunAsync(
            pipeName,
            "member",
            "list",
            "--circle",
            circleId,
            "--output",
            "json");
        var nodes = await RunAsync(
            pipeName,
            "node",
            "list",
            "--circle",
            circleId,
            "--output",
            "json");

        Assert.AreEqual(CliExitCodes.Success, circles.ExitCode);
        Assert.AreEqual(CliExitCodes.Success, members.ExitCode);
        Assert.AreEqual(CliExitCodes.Success, nodes.ExitCode);
        StringAssert.Contains(circles.StandardOutput, "Example Studio");
        StringAssert.Contains(members.StandardOutput, "Alice");
        StringAssert.Contains(nodes.StandardOutput, "Alice-PC");
        Assert.AreEqual(string.Empty, status.StandardError);
        Assert.AreEqual(string.Empty, create.StandardError);
    }

    [TestMethod]
    public async Task Cli_returns_daemon_unavailable_when_the_selected_pipe_has_no_daemon()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        var output = new StringWriter();
        var error = new StringWriter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exitCode = await CliApplication.RunAsync(
            ["--pipe-name", $"balls-tests-{Guid.NewGuid():N}", "status"],
            output,
            error,
            timeout.Token);

        Assert.AreEqual(CliExitCodes.DaemonUnavailable, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "ballsd is unavailable");
    }

    [TestMethod]
    public async Task Cli_uses_the_current_users_default_control_pipe_when_none_is_supplied()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = WindowsNamedPipeDefaults.GetCurrentUserPipeName();
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(directory.Path, pipeName, "Alice-PC"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["status"], output, error);

        Assert.AreEqual(CliExitCodes.Success, exitCode);
        StringAssert.Contains(output.ToString(), "Alice-PC");
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task Cli_and_daemon_report_the_same_semantic_version_without_starting_services()
    {
        var cliOutput = new StringWriter();
        var cliError = new StringWriter();
        var daemonOutput = new StringWriter();
        var daemonError = new StringWriter();

        var cliExit = await CliApplication.RunAsync(["--version"], cliOutput, cliError);
        var daemonExit = await DaemonCommand.RunAsync(
            ["--version"],
            daemonOutput,
            daemonError);

        Assert.AreEqual(CliExitCodes.Success, cliExit);
        Assert.AreEqual(DaemonExitCodes.Success, daemonExit);
        Assert.AreEqual(daemonOutput.ToString().Trim(), cliOutput.ToString().Trim());
        StringAssert.Matches(
            cliOutput.ToString().Trim(),
            new System.Text.RegularExpressions.Regex(
                "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$"));
        Assert.AreEqual(string.Empty, cliError.ToString());
        Assert.AreEqual(string.Empty, daemonError.ToString());
    }

    private static async Task<CliResult> RunAsync(string pipeName, params string[] command)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var arguments = new[] { "--pipe-name", pipeName }.Concat(command).ToArray();
        var exitCode = await CliApplication.RunAsync(arguments, output, error);
        return new CliResult(exitCode, output.ToString().Trim(), error.ToString().Trim());
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
