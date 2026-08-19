using System.Net;
using System.Net.Http.Json;
using System.Text;
using Balls.Daemon;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleEndpointsTests
{
    [TestMethod]
    public async Task CreateCircle_lists_its_owner_and_local_node_after_daemon_restart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        var request = new CreateCircleRequest(
            "0198c2d8-b000-7000-8000-000000000101",
            "Example Studio",
            "Alice");
        string circleId;

        await using (var daemon = await DaemonHost.StartAsync(
                         new DaemonOptions(directory.Path, pipeName, "Alice-PC")))
        using (var client = WindowsNamedPipeHttpClient.Create(pipeName))
        {
            using var createResponse = await client.PostAsJsonAsync(
                ControlRoutes.Circles,
                request,
                ControlJson.Options);
            var created = await createResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
                ControlJson.Options);

            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
            Assert.IsNotNull(created);
            Assert.AreEqual("Example Studio", created.Circle.Name);
            Assert.AreEqual(1, created.Circle.MemberCount);
            Assert.AreEqual(1, created.Circle.NodeCount);
            circleId = created.Circle.Id;
            Assert.AreEqual(ControlRoutes.Circle(circleId), createResponse.Headers.Location?.OriginalString);

            var fetched = await client.GetFromJsonAsync<CircleDetailsResponse>(
                ControlRoutes.Circle(circleId),
                ControlJson.Options);
            Assert.IsNotNull(fetched);
            Assert.AreEqual(circleId, fetched.Circle.Id);
        }

        await using (var restartedDaemon = await DaemonHost.StartAsync(
                         new DaemonOptions(directory.Path, pipeName, "Renamed-PC")))
        using (var client = WindowsNamedPipeHttpClient.Create(pipeName))
        {
            var circles = await client.GetFromJsonAsync<CircleListResponse>(
                ControlRoutes.Circles,
                ControlJson.Options);
            var members = await client.GetFromJsonAsync<MemberListResponse>(
                ControlRoutes.CircleMembers(circleId),
                ControlJson.Options);
            var nodes = await client.GetFromJsonAsync<NodeListResponse>(
                ControlRoutes.CircleNodes(circleId),
                ControlJson.Options);

            Assert.IsNotNull(circles);
            Assert.AreEqual(1, circles.Circles.Count);
            Assert.AreEqual(circleId, circles.Circles[0].Id);

            Assert.IsNotNull(members);
            Assert.AreEqual(circleId, members.CircleId);
            Assert.AreEqual(1, members.Members.Count);
            Assert.AreEqual("Alice", members.Members[0].DisplayName);
            Assert.AreEqual("owner", members.Members[0].Role);

            Assert.IsNotNull(nodes);
            Assert.AreEqual(circleId, nodes.CircleId);
            Assert.AreEqual(1, nodes.Nodes.Count);
            Assert.AreEqual("Alice-PC", nodes.Nodes[0].DisplayName);
        }
    }

    [TestMethod]
    public async Task CreateCircle_returns_a_stable_validation_error_without_partial_circle_state()
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

        using var response = await client.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198c2d8-b000-7000-8000-000000000102",
                "   ",
                "Alice"),
            ControlJson.Options);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ControlJson.Options);
        var circles = await client.GetFromJsonAsync<CircleListResponse>(
            ControlRoutes.Circles,
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsNotNull(error);
        Assert.AreEqual("circle_name_required", error.Code);
        Assert.IsNotNull(circles);
        Assert.AreEqual(0, circles.Circles.Count);
    }

    [TestMethod]
    public async Task Local_control_rejects_request_bodies_larger_than_thirty_two_kibibytes()
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
        var oversizedJson =
            $"{{\"requestId\":\"0198c2d8-b000-7000-8000-000000000103\","
            + $"\"name\":\"{new string('x', 33 * 1024)}\",\"ownerDisplayName\":\"Alice\"}}";
        using var content = new StringContent(oversizedJson, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, ControlRoutes.Circles)
        {
            Content = content,
        };
        request.Headers.ExpectContinue = true;

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateCircle_returns_validation_error_when_a_required_json_field_is_missing()
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
        const string missingNameJson =
            """
            {
              "requestId": "0198c2d8-b000-7000-8000-000000000104",
              "ownerDisplayName": "Alice"
            }
            """;
        using var content = new StringContent(missingNameJson, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(ControlRoutes.Circles, content);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsNotNull(error);
        Assert.AreEqual("circle_name_required", error.Code);
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
