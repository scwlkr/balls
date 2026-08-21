using System.Text.Json;
using Balls.Cli;
using Balls.Daemon;
using Balls.Host;
using Balls.Platform;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;

namespace Balls.Cli.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CliApplicationTests
{
    [TestMethod]
    public async Task Unsupported_host_fails_closed_through_the_typed_platform_result()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Supported hosts do not exercise this path.");
            return;
        }

        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["status"], output, error);

        Assert.AreEqual(CliExitCodes.PlatformUnsupported, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "local host platform");
        StringAssert.Contains(error.ToString(), "is not supported yet");
    }

    [TestMethod]
    public async Task Cli_rejects_an_invalid_pipe_name_as_usage_without_a_stack_trace()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var invalidEndpoint = OperatingSystem.IsWindows() ? "bad/name" : "relative.sock";

        var exitCode = await CliApplication.RunAsync(
            ["--pipe-name", invalidEndpoint, "status"],
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
        var output = new StringWriter();
        var error = new StringWriter();

        var endpoint = OperatingSystem.IsWindows()
            ? $"balls-tests-{Guid.NewGuid():N}"
            : Path.Combine(Path.GetTempPath(), $"balls-tests-{Guid.NewGuid():N}.sock");

        var exitCode = await CliApplication.RunAsync(
            [
                "--pipe-name",
                endpoint,
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
    public async Task Cli_rejects_unsupported_output_and_misplaced_global_options_as_usage()
    {
        var unsupportedOutput = new StringWriter();
        var unsupportedError = new StringWriter();
        var misplacedOutput = new StringWriter();
        var misplacedError = new StringWriter();

        var unsupportedExit = await CliApplication.RunAsync(
            ["--output", "yaml", "status"],
            unsupportedOutput,
            unsupportedError);
        var misplacedExit = await CliApplication.RunAsync(
            ["status", "--output", "json"],
            misplacedOutput,
            misplacedError);

        Assert.AreEqual(CliExitCodes.UsageError, unsupportedExit);
        Assert.AreEqual(CliExitCodes.UsageError, misplacedExit);
        Assert.AreEqual(string.Empty, unsupportedOutput.ToString());
        Assert.AreEqual(string.Empty, misplacedOutput.ToString());
        Assert.AreEqual(
            "balls: --output must be either 'text' or 'json'." + Environment.NewLine
                + "commands: ui | status | circle create | circle join | circle list | member list | node list | message send | message list | invitation create | invitation redeem"
                + Environment.NewLine,
            unsupportedError.ToString());
        StringAssert.StartsWith(misplacedError.ToString(), "balls: unknown command.");
    }

    [TestMethod]
    public void Cli_output_has_stable_golden_text_and_versioned_json()
    {
        var status = new StatusResponse(
            "0.2.0-alpha.1",
            1,
            new NodeResponse(
                "0198f2cc-6a50-7a08-aacb-298f4ebdf616",
                "Alice-PC",
                DateTimeOffset.Parse(
                    "2026-08-19T12:34:56.1234567+00:00",
                    System.Globalization.CultureInfo.InvariantCulture)));

        Assert.AreEqual(
            "Node: Alice-PC" + Environment.NewLine
                + "Node ID: 0198f2cc-6a50-7a08-aacb-298f4ebdf616" + Environment.NewLine
                + "Control protocol: v1",
            CliOutput.RenderStatus(status));
        Assert.AreEqual(
            "{\"outputVersion\":1,\"result\":{\"productVersion\":\"0.2.0-alpha.1\",\"protocolVersion\":1,\"node\":{\"id\":\"0198f2cc-6a50-7a08-aacb-298f4ebdf616\",\"displayName\":\"Alice-PC\",\"createdAtUtc\":\"2026-08-19T12:34:56.1234567+00:00\"}}}",
            CliOutput.SerializeResult(status));
        Assert.AreEqual(
            "{\"outputVersion\":1,\"error\":{\"code\":\"circle_not_found\",\"message\":\"The requested Circle is not known to this Node.\"}}",
            CliOutput.SerializeError(
                "circle_not_found",
                "The requested Circle is not known to this Node."));
    }

    [TestMethod]
    public void Cli_output_ignores_unknown_additive_protocol_response_fields()
    {
        const string responseJson =
            """
            {
              "productVersion": "0.2.0-alpha.1",
              "protocolVersion": 1,
              "futureStatus": "ignored",
              "node": {
                "id": "0198f2cc-6a50-7a08-aacb-298f4ebdf616",
                "displayName": "Alice-PC",
                "createdAtUtc": "2026-08-19T12:34:56.1234567+00:00",
                "futureNode": true
              }
            }
            """;

        var response = JsonSerializer.Deserialize<StatusResponse>(
            responseJson,
            ControlJson.Options);

        Assert.IsNotNull(response);
        var rendered = CliOutput.SerializeResult(response);
        Assert.IsFalse(rendered.Contains("futureStatus", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("futureNode", StringComparison.Ordinal));
        StringAssert.Contains(rendered, "\"outputVersion\":1");
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

        var status = await RunAsync(pipeName, "--output", "json", "status");
        Assert.AreEqual(CliExitCodes.Success, status.ExitCode);
        var statusResponse = DeserializeResult<StatusResponse>(status.StandardOutput);
        Assert.IsNotNull(statusResponse);
        Assert.AreEqual("Alice-PC", statusResponse.Node.DisplayName);

        var create = await RunAsync(
            pipeName,
            "--output",
            "json",
            "circle",
            "create",
            "Example Studio",
            "--owner",
            "Alice",
            "--request-id",
            "0198c2d8-b000-7000-8000-000000000201");
        Assert.AreEqual(CliExitCodes.Success, create.ExitCode);
        var created = DeserializeResult<CircleDetailsResponse>(create.StandardOutput);
        Assert.IsNotNull(created);
        var circleId = created.Circle.Id;

        var circles = await RunAsync(pipeName, "--output", "json", "circle", "list");
        var members = await RunAsync(
            pipeName,
            "--output",
            "json",
            "member",
            "list",
            "--circle",
            circleId);
        var nodes = await RunAsync(
            pipeName,
            "--output",
            "json",
            "node",
            "list",
            "--circle",
            circleId);

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
    public async Task Cli_creates_a_canonical_invitation_file_and_redeems_it_once()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This local-control transport test is Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(directory.Path, pipeName, "Alice-PC"));
        var createCircle = await RunAsync(
            pipeName,
            "--output",
            "json",
            "circle",
            "create",
            "Invitation Circle",
            "--owner",
            "Alice");
        var circle = DeserializeResult<CircleDetailsResponse>(createCircle.StandardOutput);
        var packagePath = Path.Combine(directory.Path, "invite.balls-invitation");

        var issue = await RunAsync(
            pipeName,
            "--output",
            "json",
            "invitation",
            "create",
            "--circle",
            circle.Circle.Id,
            "--valid-for-minutes",
            "30",
            "--out",
            packagePath);
        Assert.AreEqual(CliExitCodes.Success, issue.ExitCode);
        var issued = DeserializeResult<CreateInvitationResponse>(issue.StandardOutput);
        CollectionAssert.AreEqual(
            System.Text.Encoding.UTF8.GetBytes(issued.Package),
            await File.ReadAllBytesAsync(packagePath));

        var redeem = await RunAsync(
            pipeName,
            "--output",
            "json",
            "invitation",
            "redeem",
            "--file",
            packagePath);
        Assert.AreEqual(CliExitCodes.Success, redeem.ExitCode);
        var redeemed = DeserializeResult<RedeemInvitationResponse>(redeem.StandardOutput);
        Assert.AreEqual("accepted", redeemed.Status);
        Assert.AreEqual(issued.InvitationId, redeemed.InvitationId);

        var replay = await RunAsync(
            pipeName,
            "invitation",
            "redeem",
            "--file",
            packagePath);
        Assert.AreEqual(CliExitCodes.RequestRejected, replay.ExitCode);
        StringAssert.Contains(replay.StandardError, "replayed");
        Assert.IsFalse(replay.StandardError.Contains(issued.Package, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Cli_joins_a_second_Node_and_lists_the_shared_roster_on_both_daemons()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This local-control transport test is Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var anchorPipe = $"balls-tests-{Guid.NewGuid():N}";
        var joinerPipe = $"balls-tests-{Guid.NewGuid():N}";
        var port = ReservePort();
        await using var anchor = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(directory.Path, "anchor"),
                anchorPipe,
                "Anchor-PC",
                $"127.0.0.1:{port}"));
        await using var joiner = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(directory.Path, "joiner"),
                joinerPipe,
                "Joiner-PC"));
        var created = DeserializeResult<CircleDetailsResponse>((await RunAsync(
            anchorPipe,
            "--output",
            "json",
            "circle",
            "create",
            "Shared Circle",
            "--owner",
            "Alice")).StandardOutput);
        var invitationPath = Path.Combine(directory.Path, "shared.balls-invitation");
        var issued = await RunAsync(
            anchorPipe,
            "invitation",
            "create",
            "--circle",
            created.Circle.Id,
            "--out",
            invitationPath);
        Assert.AreEqual(CliExitCodes.Success, issued.ExitCode);

        var admitted = await RunAsync(
            joinerPipe,
            "--output",
            "json",
            "circle",
            "join",
            "--file",
            invitationPath,
            "--endpoint",
            $"127.0.0.1:{port}",
            "--member",
            "Bob");
        Assert.AreEqual(CliExitCodes.Success, admitted.ExitCode);
        var joined = DeserializeResult<CircleDetailsResponse>(admitted.StandardOutput);
        Assert.AreEqual(2, joined.Circle.MemberCount);
        Assert.AreEqual(2, joined.Circle.NodeCount);

        foreach (var pipe in new[] { anchorPipe, joinerPipe })
        {
            var members = await RunAsync(
                pipe,
                "member",
                "list",
                "--circle",
                created.Circle.Id);
            var nodes = await RunAsync(
                pipe,
                "node",
                "list",
                "--circle",
                created.Circle.Id);
            Assert.AreEqual(CliExitCodes.Success, members.ExitCode);
            Assert.AreEqual(CliExitCodes.Success, nodes.ExitCode);
            StringAssert.Contains(members.StandardOutput, "Alice");
            StringAssert.Contains(members.StandardOutput, "Bob");
            StringAssert.Contains(nodes.StandardOutput, "Anchor-PC");
            StringAssert.Contains(nodes.StandardOutput, "Joiner-PC");
        }
    }

    [TestMethod]
    public async Task Cli_exchanges_one_durable_Circle_message_and_preserves_it_after_both_daemons_restart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This local-control transport test is Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var anchorPath = Path.Combine(directory.Path, "anchor");
        var joinerPath = Path.Combine(directory.Path, "joiner");
        var anchorPipe = $"balls-tests-{Guid.NewGuid():N}";
        var joinerPipe = $"balls-tests-{Guid.NewGuid():N}";
        var admissionPort = ReservePort();
        var messagePort = ReservePort();
        var messageId = "0198c2d8-b000-7000-8000-000000000239";
        string circleId;

        await using (var anchor = await DaemonHost.StartAsync(
            new DaemonOptions(
                anchorPath,
                anchorPipe,
                "Anchor-PC",
                $"127.0.0.1:{admissionPort}",
                $"127.0.0.1:{messagePort}")))
        await using (var joiner = await DaemonHost.StartAsync(
            new DaemonOptions(joinerPath, joinerPipe, "Joiner-PC")))
        {
            var created = DeserializeResult<CircleDetailsResponse>((await RunAsync(
                anchorPipe,
                "--output",
                "json",
                "circle",
                "create",
                "Message Circle",
                "--owner",
                "Alice")).StandardOutput);
            circleId = created.Circle.Id;
            var invitationPath = Path.Combine(directory.Path, "message.balls-invitation");
            Assert.AreEqual(CliExitCodes.Success, (await RunAsync(
                anchorPipe,
                "invitation",
                "create",
                "--circle",
                circleId,
                "--out",
                invitationPath)).ExitCode);
            Assert.AreEqual(CliExitCodes.Success, (await RunAsync(
                joinerPipe,
                "circle",
                "join",
                "--file",
                invitationPath,
                "--endpoint",
                $"127.0.0.1:{admissionPort}",
                "--member",
                "Bob")).ExitCode);

            var sent = await RunAsync(
                joinerPipe,
                "--output",
                "json",
                "message",
                "send",
                "--circle",
                circleId,
                "--endpoint",
                $"127.0.0.1:{messagePort}",
                "--text",
                "Hello from Bob's Node.",
                "--message-id",
                messageId);
            Assert.AreEqual(CliExitCodes.Success, sent.ExitCode);
            var accepted = DeserializeResult<CircleMessageResponse>(sent.StandardOutput);
            Assert.AreEqual(1L, accepted.Sequence);
            Assert.AreEqual("Hello from Bob's Node.", accepted.Text);

            foreach (var pipe in new[] { anchorPipe, joinerPipe })
            {
                var listed = DeserializeResult<CircleMessageListResponse>((await RunAsync(
                    pipe,
                    "--output",
                    "json",
                    "message",
                    "list",
                    "--circle",
                    circleId)).StandardOutput);
                Assert.HasCount(1, listed.Messages);
                Assert.AreEqual(messageId, listed.Messages[0].Id);
                Assert.AreEqual(accepted.AuthorMemberId, listed.Messages[0].AuthorMemberId);
                Assert.AreEqual(accepted.AuthorNodeId, listed.Messages[0].AuthorNodeId);
                Assert.AreEqual(accepted.AuthoredAtUtc, listed.Messages[0].AuthoredAtUtc);
                Assert.AreEqual(accepted.AcceptedAtUtc, listed.Messages[0].AcceptedAtUtc);
            }
        }

        await using var restartedAnchor = await DaemonHost.StartAsync(
            new DaemonOptions(
                anchorPath,
                anchorPipe,
                "Anchor-PC",
                $"127.0.0.1:{admissionPort}",
                $"127.0.0.1:{messagePort}"));
        await using var restartedJoiner = await DaemonHost.StartAsync(
            new DaemonOptions(joinerPath, joinerPipe, "Joiner-PC"));
        foreach (var pipe in new[] { anchorPipe, joinerPipe })
        {
            var listed = DeserializeResult<CircleMessageListResponse>((await RunAsync(
                pipe,
                "--output",
                "json",
                "message",
                "list",
                "--circle",
                circleId)).StandardOutput);
            Assert.HasCount(1, listed.Messages);
            Assert.AreEqual(messageId, listed.Messages[0].Id);
            Assert.AreEqual(1L, listed.Messages[0].Sequence);
            Assert.AreEqual("Hello from Bob's Node.", listed.Messages[0].Text);
        }
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
    public async Task Ui_requests_a_one_time_launch_and_opens_it_without_printing_the_capability()
    {
        using var directory = new TemporaryDirectory();
        var endpoint = OperatingSystem.IsWindows()
            ? $"balls-ui-{Guid.NewGuid():N}"
            : Path.Combine(directory.Path, "runtime", "control.sock");
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(Path.Combine(directory.Path, "state"), endpoint, "Browser-PC"));
        var selection = HostPlatformSelector.SelectCurrent();
        var host = ((SupportedHostPlatform)selection).Platform;
        var browser = new RecordingBrowserLauncher();
        host = host with { SystemBrowser = browser };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["--pipe-name", endpoint, "ui"],
            output,
            error,
            host);

        Assert.AreEqual(CliExitCodes.Success, exitCode);
        Assert.IsNotNull(browser.OpenedUri);
        Assert.AreEqual("http", browser.OpenedUri.Scheme);
        Assert.IsTrue(System.Net.IPAddress.IsLoopback(
            System.Net.IPAddress.Parse(browser.OpenedUri.Host)));
        Assert.AreEqual(string.Empty, browser.OpenedUri.Query);
        StringAssert.StartsWith(browser.OpenedUri.Fragment, "#launch=");
        Assert.AreEqual("Opened the local Balls workspace." + Environment.NewLine, output.ToString());
        Assert.IsFalse(output.ToString().Contains("launch=", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("launch=", StringComparison.Ordinal));
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

    private static T DeserializeResult<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(1, document.RootElement.GetProperty("outputVersion").GetInt32());
        return document.RootElement.GetProperty("result").Deserialize<T>(ControlJson.Options)
            ?? throw new AssertFailedException("CLI result was null.");
    }

    private static int ReservePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class RecordingBrowserLauncher : ISystemBrowserLauncher
    {
        public Uri? OpenedUri { get; private set; }

        public void Open(Uri uri)
        {
            Assert.IsNull(OpenedUri);
            OpenedUri = uri;
        }
    }

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
