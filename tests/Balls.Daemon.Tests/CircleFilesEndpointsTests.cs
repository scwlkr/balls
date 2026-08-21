using System.Net;
using System.Net.Http.Json;
using Balls.Daemon;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleFilesEndpointsTests
{
    [TestMethod]
    public async Task Owner_creates_and_lists_a_contribution_and_grant_without_exposing_proof_material()
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
        using var client = WindowsNamedPipeHttpClient.Create(pipeName);
        using var createCircle = await client.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-3000-7000-8000-000000000001",
                "Example Studio",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircle.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);
        var circleId = circle.Circle.Id;
        var ownerId = circle.Members.Single().Id;

        using var createContribution = await client.PostAsJsonAsync(
            ControlRoutes.CircleFilesContributions(circleId),
            new CreateCircleFilesContributionRequest(
                "0198d000-3000-7000-8000-000000000002",
                "Project Files"),
            ControlJson.Options);
        var contributionJson = await createContribution.Content.ReadAsStringAsync();
        var contribution = System.Text.Json.JsonSerializer.Deserialize<CircleFilesContributionResponse>(
            contributionJson,
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.Created, createContribution.StatusCode);
        Assert.IsNotNull(contribution);
        Assert.AreEqual(circleId, contribution.CircleId);
        Assert.AreEqual("Project Files", contribution.DisplayName);
        Assert.AreEqual("defined", contribution.Lifecycle);
        Assert.AreEqual(ownerId, contribution.AuthorizedByMemberId);
        AssertSafeProjection(contributionJson);

        using var createGrant = await client.PostAsJsonAsync(
            ControlRoutes.CircleFilesAccessGrants(circleId, contribution.Id),
            new CreateMemberAccessGrantRequest(
                "0198d000-3000-7000-8000-000000000003",
                ownerId,
                "read-only"),
            ControlJson.Options);
        var grantJson = await createGrant.Content.ReadAsStringAsync();
        var grant = System.Text.Json.JsonSerializer.Deserialize<MemberAccessGrantResponse>(
            grantJson,
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.Created, createGrant.StatusCode);
        Assert.IsNotNull(grant);
        Assert.AreEqual(contribution.Id, grant.ContributionId);
        Assert.AreEqual(ownerId, grant.MemberId);
        Assert.AreEqual("read-only", grant.Access);
        Assert.AreEqual("defined", grant.Lifecycle);
        AssertSafeProjection(grantJson);

        var contributions = await client.GetFromJsonAsync<CircleFilesContributionListResponse>(
            ControlRoutes.CircleFilesContributions(circleId),
            ControlJson.Options);
        var grants = await client.GetFromJsonAsync<MemberAccessGrantListResponse>(
            ControlRoutes.CircleFilesAccessGrants(circleId, contribution.Id),
            ControlJson.Options);

        Assert.IsNotNull(contributions);
        Assert.AreEqual(contribution.Id, contributions.Contributions.Single().Id);
        Assert.IsNotNull(grants);
        Assert.AreEqual(grant.Id, grants.Grants.Single().Id);
    }

    private static void AssertSafeProjection(string json)
    {
        foreach (var forbidden in new[] { "signature", "transcript", "credential", "private", "secret" })
        {
            Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
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
