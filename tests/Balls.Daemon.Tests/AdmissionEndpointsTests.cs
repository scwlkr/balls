using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Balls.Daemon;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class AdmissionEndpointsTests
{
    [TestMethod]
    public async Task Local_control_joins_through_the_Anchor_listener_and_exposes_both_views()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This local-control transport test is Windows-only.");
            return;
        }

        using var anchorDirectory = new TemporaryDirectory();
        using var joinerDirectory = new TemporaryDirectory();
        var anchorPipe = $"balls-tests-{Guid.NewGuid():N}";
        var joinerPipe = $"balls-tests-{Guid.NewGuid():N}";
        var port = ReservePort();
        await using var anchor = await DaemonHost.StartAsync(
            new DaemonOptions(
                anchorDirectory.Path,
                anchorPipe,
                "Anchor-PC",
                $"127.0.0.1:{port}"));
        await using var joiner = await DaemonHost.StartAsync(
            new DaemonOptions(joinerDirectory.Path, joinerPipe, "Joiner-PC"));
        Assert.AreEqual($"127.0.0.1:{port}", anchor.AdmissionAddress!.Value);
        using var anchorClient = WindowsNamedPipeHttpClient.Create(anchorPipe);
        using var joinerClient = WindowsNamedPipeHttpClient.Create(joinerPipe);
        using var create = await anchorClient.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                Guid.CreateVersion7().ToString("D"),
                "Shared Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await create.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);
        using var invite = await anchorClient.PostAsJsonAsync(
            ControlRoutes.CircleInvitations(circle.Circle.Id),
            new CreateInvitationRequest(60),
            ControlJson.Options);
        var issued = await invite.Content.ReadFromJsonAsync<CreateInvitationResponse>(
            ControlJson.Options);
        Assert.IsNotNull(issued);

        using var admission = await joinerClient.PostAsJsonAsync(
            ControlRoutes.CircleJoin,
            new JoinCircleRequest(issued.Package, $"127.0.0.1:{port}", "Bob"),
            ControlJson.Options);
        var joined = await admission.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.OK, admission.StatusCode);
        Assert.IsNotNull(joined);
        Assert.AreEqual(2, joined.Circle.MemberCount);
        Assert.AreEqual(2, joined.Circle.NodeCount);
        Assert.AreEqual("member", joined.Members.Single(value => value.DisplayName == "Bob").Role);
        using var anchorViewResponse = await anchorClient.GetAsync(
            ControlRoutes.Circle(circle.Circle.Id));
        var anchorView = await anchorViewResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.AreEqual(2, anchorView!.Circle.MemberCount);
        Assert.AreEqual(2, anchorView.Circle.NodeCount);
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"balls-admission-endpoints-{Guid.CreateVersion7():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
