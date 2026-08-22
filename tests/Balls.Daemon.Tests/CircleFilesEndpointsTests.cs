using System.Net;
using System.Net.Http.Json;
using Balls.Daemon;
using Balls.Platform;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;
using Balls.Security.Windows;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleFilesEndpointsTests
{
    [TestMethod]
    public async Task Owner_previews_and_applies_one_exact_host_plan_without_exposing_authorization_material()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The local-control transport contract is Windows-only in this test.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        var hosting = new StubHostProvisioner();
        var host = WindowsHostPlatform.Create() with { CircleFilesHosting = hosting };
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(directory.Path, pipeName, "Alice-PC"),
            host,
            new WindowsCurrentUserPrivateMaterialProtector());
        using var client = WindowsNamedPipeHttpClient.Create(pipeName);
        using var createCircle = await client.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-3000-7000-8000-000000000011",
                "Example Studio",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircle.Content.ReadFromJsonAsync<CircleDetailsResponse>(ControlJson.Options);
        Assert.IsNotNull(circle);
        using var createContribution = await client.PostAsJsonAsync(
            ControlRoutes.CircleFilesContributions(circle.Circle.Id),
            new CreateCircleFilesContributionRequest(
                "0198d000-3000-7000-8000-000000000012",
                "Project Files"),
            ControlJson.Options);
        var contribution = await createContribution.Content.ReadFromJsonAsync<CircleFilesContributionResponse>(
            ControlJson.Options);
        Assert.IsNotNull(contribution);

        const string folder = @"C:\BallsShares\Example";
        using var previewResponse = await client.PostAsJsonAsync(
            ControlRoutes.CircleFilesHostPreview(circle.Circle.Id, contribution.Id),
            new PreviewCircleFilesHostRequest(folder),
            ControlJson.Options);
        var previewJson = await previewResponse.Content.ReadAsStringAsync();
        var preview = System.Text.Json.JsonSerializer.Deserialize<CircleFilesHostPlanResponse>(
            previewJson,
            ControlJson.Options);
        Assert.IsNotNull(preview);
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);

        using var applyResponse = await client.PostAsJsonAsync(
            ControlRoutes.CircleFilesHostApply(circle.Circle.Id, contribution.Id),
            new ApplyCircleFilesHostRequest(folder, preview.PlanId),
            ControlJson.Options);
        var appliedJson = await applyResponse.Content.ReadAsStringAsync();
        var applied = System.Text.Json.JsonSerializer.Deserialize<CircleFilesHostApplyResponse>(
            appliedJson,
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.OK, applyResponse.StatusCode);
        Assert.IsNotNull(applied);
        Assert.AreEqual("applied", applied.Status);
        Assert.AreEqual(2, hosting.Requests.Count);
        Assert.IsTrue(hosting.Requests.All(request => request.AuthorizationDigest.Length == 64));
        foreach (var json in new[] { previewJson, appliedJson })
        {
            Assert.IsFalse(json.Contains("authorization", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("signature", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("S-1-", StringComparison.Ordinal));
        }
    }

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

    [TestMethod]
    public async Task Owner_issues_a_grant_credential_without_returning_secret_or_authorization_material()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The local-control transport contract is Windows-only in this test.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var pipeName = $"balls-tests-{Guid.NewGuid():N}";
        var grantProvisioner = new StubGrantCredentialProvisioner();
        var host = WindowsHostPlatform.Create() with
        {
            CircleFilesGrantCredentials = grantProvisioner,
        };
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(directory.Path, pipeName, "Alice-PC"),
            host,
            new WindowsCurrentUserPrivateMaterialProtector());
        using var client = WindowsNamedPipeHttpClient.Create(pipeName);
        var circle = await (await client.PostAsJsonAsync(
                ControlRoutes.Circles,
                new CreateCircleRequest(
                    "0198d000-3000-7000-8000-000000000091",
                    "Credential Studio",
                    "Alice"),
                ControlJson.Options))
            .Content.ReadFromJsonAsync<CircleDetailsResponse>(ControlJson.Options);
        Assert.IsNotNull(circle);
        var contribution = await (await client.PostAsJsonAsync(
                ControlRoutes.CircleFilesContributions(circle.Circle.Id),
                new CreateCircleFilesContributionRequest(
                    "0198d000-3000-7000-8000-000000000092",
                    "Credential Files"),
                ControlJson.Options))
            .Content.ReadFromJsonAsync<CircleFilesContributionResponse>(ControlJson.Options);
        Assert.IsNotNull(contribution);
        var grant = await (await client.PostAsJsonAsync(
                ControlRoutes.CircleFilesAccessGrants(circle.Circle.Id, contribution.Id),
                new CreateMemberAccessGrantRequest(
                    "0198d000-3000-7000-8000-000000000093",
                    circle.Members.Single().Id,
                    "read-write"),
                ControlJson.Options))
            .Content.ReadFromJsonAsync<MemberAccessGrantResponse>(ControlJson.Options);
        Assert.IsNotNull(grant);
        const string folder = @"C:\BallsShares\Credential";

        using var previewResponse = await client.PostAsJsonAsync(
            ControlRoutes.CircleFilesGrantCredentialPreview(
                circle.Circle.Id, contribution.Id, grant.Id),
            new PreviewCircleFilesGrantCredentialRequest(folder),
            ControlJson.Options);
        var previewJson = await previewResponse.Content.ReadAsStringAsync();
        var preview = System.Text.Json.JsonSerializer.Deserialize<CircleFilesGrantCredentialPlanResponse>(
            previewJson,
            ControlJson.Options);
        Assert.IsNotNull(preview);
        using var applyResponse = await client.PostAsJsonAsync(
            ControlRoutes.CircleFilesGrantCredentialApply(
                circle.Circle.Id, contribution.Id, grant.Id),
            new ApplyCircleFilesGrantCredentialRequest(folder, preview.PlanId),
            ControlJson.Options);
        var applyJson = await applyResponse.Content.ReadAsStringAsync();
        var applied = System.Text.Json.JsonSerializer.Deserialize<CircleFilesGrantCredentialApplyResponse>(
            applyJson,
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, applyResponse.StatusCode);
        Assert.IsNotNull(applied);
        Assert.AreEqual("applied", applied.Status);
        Assert.AreEqual(1, grantProvisioner.SecretUseCount);
        Assert.IsTrue(grantProvisioner.SecretLength >= 24);
        foreach (var json in new[] { previewJson, applyJson })
        {
            foreach (var forbidden in new[] { "password", "secret", "signature", "transcript", "authorization" })
            {
                Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
            }
        }
    }

    private static void AssertSafeProjection(string json)
    {
        foreach (var forbidden in new[] { "signature", "transcript", "credential", "private", "secret" })
        {
            Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class StubHostProvisioner : ICircleFilesHostProvisioner
    {
        internal List<CircleFilesHostRequest> Requests { get; } = [];

        public ValueTask<CircleFilesHostPlan> PreviewAsync(
            CircleFilesHostRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(CreatePlan(request));
        }

        public ValueTask<CircleFilesHostApplyResult> ApplyAsync(
            CircleFilesHostRequest request,
            string expectedPlanId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var plan = CreatePlan(request);
            Assert.AreEqual(expectedPlanId, plan.PlanId);
            return ValueTask.FromResult(
                new CircleFilesHostApplyResult(CircleFilesHostApplyStatus.Applied, plan));
        }

        private static CircleFilesHostPlan CreatePlan(CircleFilesHostRequest request) =>
            new(
                1,
                new string('a', 64),
                CircleFilesReadinessProviders.WindowsSmb311,
                request.FolderPath,
                "balls-test",
                "Balls-SMB-test",
                new string('b', 64),
                false,
                ["Create exact owned resources."]);
    }

    private sealed class StubGrantCredentialProvisioner : ICircleFilesGrantCredentialProvisioner
    {
        internal int SecretUseCount { get; private set; }
        internal int SecretLength { get; private set; }

        public ValueTask<CircleFilesGrantCredentialPlan> PreviewAsync(
            CircleFilesGrantCredentialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreatePlan(request));
        }

        public ValueTask<CircleFilesGrantCredentialApplyResult> ApplyAsync(
            CircleFilesGrantCredentialRequest request,
            string expectedPlanId,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = CreatePlan(request);
            Assert.AreEqual(expectedPlanId, plan.PlanId);
            SecretUseCount++;
            SecretLength = secret.Length;
            return ValueTask.FromResult(new CircleFilesGrantCredentialApplyResult(
                CircleFilesGrantCredentialApplyStatus.Applied,
                plan));
        }

        private static CircleFilesGrantCredentialPlan CreatePlan(
            CircleFilesGrantCredentialRequest request) =>
            new(
                1,
                new string('c', 64),
                CircleFilesReadinessProviders.WindowsSmb311,
                request.Host.FolderPath,
                "balls-test",
                "BallsG-abcdef0123456",
                new string('d', 64),
                request.Access,
                request.Generation,
                ["Create one exact limited account."]);
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
