using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Balls.Core;
using Balls.Daemon;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class DaemonStatusTests
{
    [TestMethod]
    [DoNotParallelize]
    public async Task Ambient_kestrel_configuration_cannot_add_a_tcp_control_endpoint()
    {
        using var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        const string variableName = "Kestrel__Endpoints__Untrusted__Url";
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(
            variableName,
            $"http://127.0.0.1:{port}");

        try
        {
            using var directory = new TemporaryDirectory();
            var endpoint = OperatingSystem.IsWindows()
                ? $"balls-tests-{Guid.NewGuid():N}"
                : Path.Combine(directory.Path, "runtime", "control.sock");
            await using var daemon = await DaemonHost.StartAsync(
                new DaemonOptions(
                    Path.Combine(directory.Path, "state"),
                    endpoint,
                    "Alice-PC"));
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var connected = false;
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                connected = true;
            }
            catch (Exception exception) when (
                exception is SocketException or OperationCanceledException)
            {
            }

            Assert.IsFalse(connected, "ballsd must not expose the local control API over TCP.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }

    [TestMethod]
    public async Task Daemon_rejects_an_invalid_node_name_before_it_starts_listening()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();

        var error = await Assert.ThrowsExactlyAsync<InputValidationException>(
            () => DaemonHost.StartAsync(
                new DaemonOptions(
                    directory.Path,
                    $"balls-tests-{Guid.NewGuid():N}",
                    "   ")));

        Assert.AreEqual("node_display_name_required", error.Code);
    }

    [TestMethod]
    public async Task Status_uses_the_named_pipe_and_preserves_node_identity_after_daemon_restart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        string firstNodeId;

        await using (var firstDaemon = await DaemonHost.StartAsync(
                         new DaemonOptions(directory.Path, pipeName, "Alice-PC")))
        using (var client = WindowsNamedPipeHttpClient.Create(pipeName))
        {
            var response = await client.GetFromJsonAsync<StatusResponse>(
                ControlRoutes.Status,
                ControlJson.Options);

            Assert.IsNotNull(response);
            Assert.AreEqual("0.3.0-alpha.1", response.ProductVersion);
            Assert.AreEqual(ControlProtocol.Version, response.ProtocolVersion);
            Assert.AreEqual("Alice-PC", response.Node.DisplayName);
            Assert.AreNotEqual(Guid.Empty.ToString("D"), response.Node.Id);
            firstNodeId = response.Node.Id;
        }

        await using (var restartedDaemon = await DaemonHost.StartAsync(
                         new DaemonOptions(directory.Path, pipeName, "Renamed-PC")))
        using (var client = WindowsNamedPipeHttpClient.Create(pipeName))
        {
            var response = await client.GetFromJsonAsync<StatusResponse>(
                ControlRoutes.Status,
                ControlJson.Options);

            Assert.IsNotNull(response);
            Assert.AreEqual(firstNodeId, response.Node.Id);
            Assert.AreEqual("Alice-PC", response.Node.DisplayName);
        }
    }

    [TestMethod]
    public async Task A_second_daemon_cannot_write_the_same_data_directory()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        await using var firstDaemon = await DaemonHost.StartAsync(
            new DaemonOptions(
                directory.Path,
                $"balls-tests-{Guid.NewGuid():N}",
                "Alice-PC"));
        DaemonInstance? unexpectedDaemon = null;

        try
        {
            unexpectedDaemon = await DaemonHost.StartAsync(
                new DaemonOptions(
                    directory.Path,
                    $"balls-tests-{Guid.NewGuid():N}",
                    "Alice-PC"));
            Assert.Fail("Two daemon instances must not share one writable data directory.");
        }
        catch (DataDirectoryInUseException exception)
        {
            Assert.AreEqual("data_directory_in_use", exception.Code);
        }
        finally
        {
            if (unexpectedDaemon is not null)
            {
                await unexpectedDaemon.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task OpenApi_describes_the_versioned_local_control_routes()
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
        using var client = WindowsNamedPipeHttpClient.Create(pipeName);

        using var response = await client.GetAsync(ControlRoutes.OpenApi);
        var document = await response.Content.ReadAsStringAsync();
        var contractPath = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "protocol",
            "local-control-v1.openapi.json");
        if (Environment.GetEnvironmentVariable("BALLS_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(contractPath, document);
        }

        var committedDocument = File.ReadAllText(contractPath);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(
            JsonNode.DeepEquals(JsonNode.Parse(committedDocument), JsonNode.Parse(document)),
            "The committed local-control OpenAPI contract must match ballsd.");
        StringAssert.Contains(document, ControlRoutes.Status);
        StringAssert.Contains(document, ControlRoutes.Circles);
        StringAssert.Contains(document, nameof(CreateCircleRequest));
        StringAssert.Contains(document, nameof(JoinCircleRequest));
        StringAssert.Contains(document, nameof(CreateInvitationRequest));
        StringAssert.Contains(document, nameof(RedeemInvitationRequest));
        StringAssert.Contains(document, nameof(SendCircleMessageRequest));
        StringAssert.Contains(document, nameof(CircleMessageListResponse));
        StringAssert.Contains(document, nameof(ErrorResponse));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Balls.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = OperatingSystem.IsMacOS()
                ? System.IO.Path.Combine(
                    GetCanonicalTempPath(),
                    $"bt-{Guid.NewGuid():N}"[..11])
                : System.IO.Path.Combine(
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

        private static string GetCanonicalTempPath()
        {
            var path = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            return path.StartsWith("/var/", StringComparison.Ordinal)
                ? "/private" + path
                : path;
        }
    }
}
