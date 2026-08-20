using System.Net;
using System.Net.Http.Json;
using Balls.Daemon;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;
using Balls.Protocol.Remote.V1;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class InvitationEndpointsTests
{
    [TestMethod]
    public async Task Create_and_concurrent_redeem_exposes_one_bounded_direct_exchange_result()
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
        using var client = WindowsNamedPipeHttpClient.Create(pipeName);
        using var createCircle = await client.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198c837-4000-7000-8000-000000000001",
                "Invitation Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircle.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);

        using var createInvitation = await client.PostAsJsonAsync(
            ControlRoutes.CircleInvitations(circle.Circle.Id),
            new CreateInvitationRequest(60),
            ControlJson.Options);
        var issued = await createInvitation.Content.ReadFromJsonAsync<CreateInvitationResponse>(
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.Created, createInvitation.StatusCode);
        Assert.IsNotNull(issued);
        Assert.AreEqual(circle.Circle.Id, issued.CircleId);
        Assert.IsTrue(issued.Package.Length <= InvitationPackageCodec.MaximumEncodedLength);
        var package = InvitationPackageCodec.Decode(System.Text.Encoding.UTF8.GetBytes(issued.Package));
        Assert.AreEqual(issued.InvitationId, package.Invitation.Invitation.InvitationId);
        Assert.AreEqual(1, package.Invitation.Invitation.MaximumRedemptions);

        var attempts = Enumerable.Range(0, 12)
            .Select(_ => client.PostAsJsonAsync(
                ControlRoutes.Invitations + "/redeem",
                new RedeemInvitationRequest(issued.Package),
                ControlJson.Options))
            .ToArray();
        var responses = await Task.WhenAll(attempts);
        try
        {
            Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(
                11,
                responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var acceptedResponse = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
            var accepted = await acceptedResponse.Content
                .ReadFromJsonAsync<RedeemInvitationResponse>(ControlJson.Options);
            Assert.IsNotNull(accepted);
            Assert.AreEqual("accepted", accepted.Status);
            Assert.AreEqual(circle.Circle.Id, accepted.CircleId);
            Assert.AreEqual(issued.InvitationId, accepted.InvitationId);
            Assert.IsTrue(Guid.TryParseExact(accepted.RedemptionId, "D", out _));

            foreach (var replayResponse in responses.Where(
                         response => response.StatusCode == HttpStatusCode.Conflict))
            {
                var error = await replayResponse.Content.ReadFromJsonAsync<ErrorResponse>(
                    ControlJson.Options);
                Assert.IsNotNull(error);
                Assert.AreEqual("replayed", error.Code);
                Assert.AreEqual("The Circle invitation was rejected.", error.Message);
                Assert.IsFalse(error.Message.Contains(issued.Package, StringComparison.Ordinal));
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task Invitation_endpoints_reject_invalid_bounds_and_malformed_packages_with_typed_errors()
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
        using var client = WindowsNamedPipeHttpClient.Create(pipeName);
        using var createCircle = await client.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198c837-4000-7000-8000-000000000002",
                "Boundary Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircle.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);

        using var invalidValidity = await client.PostAsJsonAsync(
            ControlRoutes.CircleInvitations(circle.Circle.Id),
            new CreateInvitationRequest(0),
            ControlJson.Options);
        var validityError = await invalidValidity.Content.ReadFromJsonAsync<ErrorResponse>(
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidValidity.StatusCode);
        Assert.IsNotNull(validityError);
        Assert.AreEqual("invalid_invitation_validity", validityError.Code);

        using var malformed = await client.PostAsJsonAsync(
            ControlRoutes.Invitations + "/redeem",
            new RedeemInvitationRequest("not-an-invitation"),
            ControlJson.Options);
        var malformedError = await malformed.Content.ReadFromJsonAsync<ErrorResponse>(
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.IsNotNull(malformedError);
        Assert.AreEqual("malformed", malformedError.Code);
        Assert.AreEqual("The Circle invitation was rejected.", malformedError.Message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
