using System.Text.Json;
using Balls.Cli;
using Balls.Daemon;
using Balls.Host;
using Balls.Platform;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;
using static Balls.Cli.Tests.CliTestSupport;

namespace Balls.Cli.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CliApplicationTests
{
    [TestMethod]
    public void Files_readiness_allows_the_bounded_native_inspection_to_finish()
    {
        Assert.AreEqual(
            TimeSpan.FromSeconds(20),
            CliApplication.GetLocalControlTimeout(["files", "readiness"]));
        Assert.IsNull(CliApplication.GetLocalControlTimeout(["status"]));
    }

    [TestMethod]
    public async Task Unsupported_host_fails_closed_through_the_typed_platform_result()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
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
                + "commands: ui | status | circle create | circle join | circle list | member list | node list | invitation create | invitation redeem | message send | message list | files readiness | files contribution create/list | files grant create/list/credential-preview/credential-apply/revoke/cleanup-preview/cleanup-apply | files host preview/apply/remove-preview/remove-apply | files mapping preview/map/inspect/unmap"
                + Environment.NewLine,
            unsupportedError.ToString());
        StringAssert.StartsWith(misplacedError.ToString(), "balls: unknown command.");
    }

    [TestMethod]
    public void Cli_output_has_stable_golden_text_and_versioned_json()
    {
        var status = new StatusResponse(
            "0.3.0-alpha.1",
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
            "{\"outputVersion\":1,\"result\":{\"productVersion\":\"0.3.0-alpha.1\",\"protocolVersion\":1,\"node\":{\"id\":\"0198f2cc-6a50-7a08-aacb-298f4ebdf616\",\"displayName\":\"Alice-PC\",\"createdAtUtc\":\"2026-08-19T12:34:56.1234567+00:00\"}}}",
            CliOutput.SerializeResult(status));
        Assert.AreEqual(
            "{\"outputVersion\":1,\"error\":{\"code\":\"circle_not_found\",\"message\":\"The requested Circle is not known to this Node.\"}}",
            CliOutput.SerializeError(
                "circle_not_found",
                "The requested Circle is not known to this Node."));
    }

    [TestMethod]
    public void Circle_Files_host_preview_and_apply_have_safe_stable_text()
    {
        var plan = new CircleFilesHostPlanResponse(
            1,
            new string('a', 64),
            "windows-smb-3.1.1-v1",
            @"C:\BallsShares\Example",
            "balls-example",
            "Balls-SMB-example",
            new string('b', 64),
            false,
            ["Create the dedicated folder."]);

        var preview = CliOutput.RenderFilesHostPlan(plan);
        var applied = CliOutput.RenderAppliedFilesHost(
            new CircleFilesHostApplyResponse("applied", plan));

        StringAssert.Contains(preview, @"Folder: C:\BallsShares\Example");
        StringAssert.Contains(preview, "Private networks and LocalSubnet only");
        StringAssert.Contains(preview, new string('a', 64));
        StringAssert.Contains(applied, "Created the dedicated Circle Files host.");
        Assert.IsFalse(preview.Contains(new string('b', 64), StringComparison.Ordinal));
    }

    [TestMethod]
    public void Circle_Files_grant_credential_output_never_contains_secret_material()
    {
        var plan = new CircleFilesGrantCredentialPlanResponse(
            1,
            new string('a', 64),
            "windows-smb-3.1.1",
            @"C:\BallsShares\Example",
            "balls-example",
            "BallsG-abcdef0123456",
            new string('b', 64),
            "read-only",
            1,
            ["Create one limited account."]);
        var preview = CliOutput.RenderFilesGrantCredentialPlan(plan);
        var applied = CliOutput.RenderAppliedFilesGrantCredential(
            new CircleFilesGrantCredentialApplyResponse("applied", plan));

        StringAssert.Contains(preview, "BallsG-abcdef0123456");
        StringAssert.Contains(applied, "password remains protected");
        Assert.IsFalse(preview.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(applied.Contains(new string('b', 64), StringComparison.Ordinal));
    }

    [TestMethod]
    public void Circle_Files_mapping_output_is_explicit_and_never_contains_ownership_material()
    {
        var plan = new CircleFilesMemberMappingPlanResponse(
            1,
            new string('a', 64),
            "192.168.50.10",
            @"\\192.168.50.10\balls-example",
            "192.168.50.10",
            "M",
            "Example Studio",
            new string('b', 64),
            ["M", "N"],
            ["Map the exact share."]);

        var preview = CliOutput.RenderFilesMappingPlan(plan);
        var mapped = CliOutput.RenderFilesMappingResult(
            new CircleFilesMemberMappingResultResponse("mapped", plan));

        StringAssert.Contains(preview, "Drive: M:");
        StringAssert.Contains(preview, "Available: M:, N:");
        StringAssert.Contains(mapped, "password remains protected");
        Assert.IsFalse(preview.Contains(new string('b', 64), StringComparison.Ordinal));
    }

    [TestMethod]
    public void Circle_Files_cleanup_output_requires_explicit_session_confirmation_and_preserves_folder()
    {
        var plan = new CircleFilesGrantCleanupPlanResponse(
            1,
            new string('a', 64),
            "windows-smb-3.1.1-v1",
            @"C:\BallsShares\Example",
            "balls-example",
            "BallsG-abcdef0123456",
            new string('b', 64),
            1,
            ["Remove exact owned grant state."]);

        var preview = CliOutput.RenderFilesGrantCleanupPlan(plan);
        var busy = CliOutput.RenderFilesGrantCleanupResult(
            new CircleFilesGrantCleanupResultResponse("busy", 2, plan));

        StringAssert.Contains(preview, "--terminate-open-sessions true");
        StringAssert.Contains(preview, @"Folder preserved: C:\BallsShares\Example");
        StringAssert.Contains(busy, "Grant cleanup: busy");
        StringAssert.Contains(busy, "Open sessions: 2");
        Assert.IsFalse(preview.Contains(new string('b', 64), StringComparison.Ordinal));
    }

    [TestMethod]
    public void Cli_output_ignores_unknown_additive_protocol_response_fields()
    {
        const string responseJson =
            """
            {
              "productVersion": "0.3.0-alpha.1",
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
    public async Task Cli_creates_and_lists_Circle_Files_contributions_and_grants_with_structured_output()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The local-control transport contract is Windows-only in this test.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(directory.Path, pipeName, "Alice-PC"));
        var created = DeserializeResult<CircleDetailsResponse>((await RunAsync(
            pipeName,
            "--output",
            "json",
            "circle",
            "create",
            "Example Studio",
            "--owner",
            "Alice")).StandardOutput);
        var circleId = created.Circle.Id;
        var ownerId = created.Members.Single().Id;

        var createContribution = await RunAsync(
            pipeName,
            "--output",
            "json",
            "files",
            "contribution",
            "create",
            "--circle",
            circleId,
            "--name",
            "Project Files",
            "--request-id",
            "0198d000-4000-7000-8000-000000000001");
        Assert.AreEqual(CliExitCodes.Success, createContribution.ExitCode);
        var contribution = DeserializeResult<CircleFilesContributionResponse>(
            createContribution.StandardOutput);
        Assert.AreEqual("Project Files", contribution.DisplayName);
        Assert.AreEqual("defined", contribution.Lifecycle);

        var createGrant = await RunAsync(
            pipeName,
            "--output",
            "json",
            "files",
            "grant",
            "create",
            "--circle",
            circleId,
            "--contribution",
            contribution.Id,
            "--member",
            ownerId,
            "--access",
            "read-write",
            "--request-id",
            "0198d000-4000-7000-8000-000000000002");
        Assert.AreEqual(CliExitCodes.Success, createGrant.ExitCode);
        var grant = DeserializeResult<MemberAccessGrantResponse>(createGrant.StandardOutput);
        Assert.AreEqual("read-write", grant.Access);
        Assert.AreEqual(contribution.Id, grant.ContributionId);

        var contributions = await RunAsync(
            pipeName,
            "files",
            "contribution",
            "list",
            "--circle",
            circleId);
        var grants = await RunAsync(
            pipeName,
            "files",
            "grant",
            "list",
            "--circle",
            circleId,
            "--contribution",
            contribution.Id);
        var structuredContributions = DeserializeResult<CircleFilesContributionListResponse>(
            (await RunAsync(
                pipeName,
                "--output",
                "json",
                "files",
                "contribution",
                "list",
                "--circle",
                circleId)).StandardOutput);
        var structuredGrants = DeserializeResult<MemberAccessGrantListResponse>(
            (await RunAsync(
                pipeName,
                "--output",
                "json",
                "files",
                "grant",
                "list",
                "--circle",
                circleId,
                "--contribution",
                contribution.Id)).StandardOutput);

        Assert.AreEqual(CliExitCodes.Success, contributions.ExitCode);
        StringAssert.Contains(contributions.StandardOutput, "Project Files");
        Assert.AreEqual(CliExitCodes.Success, grants.ExitCode);
        StringAssert.Contains(grants.StandardOutput, "read-write");
        Assert.HasCount(1, structuredContributions.Contributions);
        Assert.AreEqual(contribution.Id, structuredContributions.Contributions[0].Id);
        Assert.HasCount(1, structuredGrants.Grants);
        Assert.AreEqual(grant.Id, structuredGrants.Grants[0].Id);
        Assert.AreEqual(string.Empty, createContribution.StandardError);
        Assert.AreEqual(string.Empty, createGrant.StandardError);

        var revoke = await RunAsync(
            pipeName,
            "--output",
            "json",
            "files",
            "grant",
            "revoke",
            "--circle",
            circleId,
            "--contribution",
            contribution.Id,
            "--grant",
            grant.Id,
            "--generation",
            grant.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--request-id",
            "0198d000-4000-7000-8000-000000000003");
        var revoked = DeserializeResult<MemberAccessGrantRevocationResponse>(revoke.StandardOutput);
        Assert.AreEqual(CliExitCodes.Success, revoke.ExitCode);
        Assert.AreEqual("revoked", revoked.Status);
        Assert.AreEqual(grant.Id, revoked.GrantId);
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
    public async Task Cli_joins_a_second_Node_and_exchanges_one_durable_message()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This local-control transport test is Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var anchorPipe = $"balls-tests-{Guid.NewGuid():N}";
        var joinerPipe = $"balls-tests-{Guid.NewGuid():N}";
        var admissionPort = ReservePort();
        var messagePort = ReservePort();
        await using var anchor = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(directory.Path, "anchor"),
                anchorPipe,
                "Anchor-PC",
                $"127.0.0.1:{admissionPort}",
                $"127.0.0.1:{messagePort}"));
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
            $"127.0.0.1:{admissionPort}",
            "--member",
            "Bob");
        Assert.AreEqual(CliExitCodes.Success, admitted.ExitCode);
        var joined = DeserializeResult<CircleDetailsResponse>(admitted.StandardOutput);
        Assert.AreEqual(2, joined.Circle.MemberCount);
        Assert.AreEqual(2, joined.Circle.NodeCount);

        var malformedMessage = await RunAsync(
            joinerPipe,
            "--output",
            "json",
            "message",
            "send",
            "--circle",
            created.Circle.Id,
            "--endpoint",
            $"127.0.0.1:{messagePort}",
            "--text",
            "Never persisted.",
            "--request-id",
            Guid.Empty.ToString("D"));
        Assert.AreEqual(CliExitCodes.RequestRejected, malformedMessage.ExitCode);
        StringAssert.Contains(malformedMessage.StandardError, "invalid_request_id");

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

        var sent = await RunAsync(
            joinerPipe,
            "--output",
            "json",
            "message",
            "send",
            "--circle",
            created.Circle.Id,
            "--endpoint",
            $"127.0.0.1:{messagePort}",
            "--text",
            "Hello from Bob's Node.",
            "--request-id",
            "0198c2d8-b000-7000-8000-000000000239");
        Assert.AreEqual(CliExitCodes.Success, sent.ExitCode);
        var message = DeserializeResult<CircleMessageResponse>(sent.StandardOutput);
        Assert.AreEqual(1, message.Sequence);
        Assert.AreEqual("Hello from Bob's Node.", message.Text);

        var conflict = await RunAsync(
            joinerPipe,
            "--output",
            "json",
            "message",
            "send",
            "--circle",
            created.Circle.Id,
            "--endpoint",
            $"127.0.0.1:{messagePort}",
            "--text",
            "Conflicting text.",
            "--request-id",
            "0198c2d8-b000-7000-8000-000000000239");
        Assert.AreEqual(CliExitCodes.RequestRejected, conflict.ExitCode);
        StringAssert.Contains(conflict.StandardError, "message_request_conflict");

        foreach (var pipe in new[] { anchorPipe, joinerPipe })
        {
            var listed = await RunAsync(
                pipe,
                "message",
                "list",
                "--circle",
                created.Circle.Id);
            Assert.AreEqual(CliExitCodes.Success, listed.ExitCode);
            StringAssert.StartsWith(listed.StandardOutput, "1\t");
            StringAssert.Contains(listed.StandardOutput, "Hello from Bob's Node.");
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

    private sealed class RecordingBrowserLauncher : ISystemBrowserLauncher
    {
        public Uri? OpenedUri { get; private set; }

        public void Open(Uri uri)
        {
            Assert.IsNull(OpenedUri);
            OpenedUri = uri;
        }
    }

}
