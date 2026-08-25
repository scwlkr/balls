using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Balls.Daemon;
using Balls.Host;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon.Tests;

public sealed partial class BrowserAdapterSecurityTests
{
    [TestMethod]
    public async Task Browser_member_synchronizes_only_its_grant_and_maps_the_protected_credential()
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive(
                ".NET 10 supports TLS 1.3 on macOS clients, but not macOS SslStream servers.");
            return;
        }

        var privateAddress = FindOperationalPrivateAddress();
        if (privateAddress is null)
        {
            Assert.Inconclusive(
                "Authenticated browser synchronization requires an operational private IPv4 interface.");
            return;
        }

        using var ownerDirectory = new TemporaryDirectory();
        using var memberDirectory = new TemporaryDirectory();
        var admissionEndpoint = AllocatePrivateEndpoint(privateAddress);
        var messageEndpoint = AllocatePrivateEndpoint(privateAddress);
        var selected = (SupportedHostPlatform)HostPlatformSelector.SelectCurrent();
        var grantProvisioner = new SyncGrantCredentialProvisioner();
        var memberMapper = new SyncMemberMapper();
        await using var owner = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(ownerDirectory.Path, "state"),
                GetEndpoint(ownerDirectory.Path),
                "Owner-PC",
                admissionEndpoint,
                messageEndpoint),
            selected.Platform with { CircleFilesGrantCredentials = grantProvisioner },
            selected.PrivateMaterialProtector);
        await using var member = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(memberDirectory.Path, "state"),
                GetEndpoint(memberDirectory.Path),
                "Member-PC"),
            selected.Platform with { CircleFilesMemberMapping = memberMapper },
            selected.PrivateMaterialProtector);
        using var ownerClient = CreateIpcClient(GetEndpoint(ownerDirectory.Path));
        using var memberClient = CreateIpcClient(GetEndpoint(memberDirectory.Path));
        using var createCircleResponse = await ownerClient.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-6100-7000-8000-000000000001",
                "Pilot Projects",
                "Alice"),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.Created, createCircleResponse.StatusCode);
        var circle = await createCircleResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);

        using var issueResponse = await ownerClient.PostAsJsonAsync(
            ControlRoutes.CircleInvitations(circle.Circle.Id),
            new CreateInvitationRequest(60),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.Created, issueResponse.StatusCode);
        var invitation = await issueResponse.Content.ReadFromJsonAsync<CreateInvitationResponse>(
            ControlJson.Options);
        Assert.IsNotNull(invitation);
        var launch = await IssueLaunchAsync(memberClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);

        using var join = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleJoin,
            new JoinCircleRequest(invitation.Package, admissionEndpoint, "Bob"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var joinResponse = await browserClient.SendAsync(join);
        Assert.AreEqual(HttpStatusCode.OK, joinResponse.StatusCode);
        var joined = await joinResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(joined);
        var memberId = joined.Members.Single(person => person.DisplayName == "Bob").Id;

        var contribution = await CreateSyncContributionAsync(ownerClient, circle.Circle.Id);
        _ = await CreateSyncGrantAsync(
            ownerClient,
            circle.Circle.Id,
            contribution.Id,
            "0198d000-6100-7000-8000-000000000003",
            circle.Members.Single().Id);
        var memberGrant = await CreateSyncGrantAsync(
            ownerClient,
            circle.Circle.Id,
            contribution.Id,
            "0198d000-6100-7000-8000-000000000004",
            memberId);
        await ProvisionSyncGrantCredentialAsync(
            ownerClient,
            circle.Circle.Id,
            contribution.Id,
            memberGrant.Id);
        Assert.IsNotNull(grantProvisioner.IssuedSecretDigest);

        using var synchronize = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesSync(circle.Circle.Id),
            new SyncBrowserCircleFilesRequest(messageEndpoint),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var syncResponse = await browserClient.SendAsync(synchronize);
        var syncJson = await syncResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, syncResponse.StatusCode, syncJson);
        var synchronized = System.Text.Json.JsonSerializer
            .Deserialize<BrowserCircleFilesSyncResponse>(syncJson, ControlJson.Options);
        Assert.IsNotNull(synchronized);
        Assert.AreEqual(circle.Circle.Id, synchronized.CircleId);
        Assert.AreEqual(1, synchronized.ImportedGrantCount);
        AssertSafeSyncResponse(syncJson);

        using var contributionsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BrowserRoutes.CircleFilesContributions(circle.Circle.Id));
        contributionsRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var contributionsResponse = await browserClient.SendAsync(contributionsRequest);
        Assert.AreEqual(HttpStatusCode.OK, contributionsResponse.StatusCode);
        var contributions = await contributionsResponse.Content
            .ReadFromJsonAsync<CircleFilesContributionListResponse>(ControlJson.Options);
        Assert.IsNotNull(contributions);
        Assert.AreEqual(contribution.Id, contributions.Contributions.Single().Id);

        using var grantsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BrowserRoutes.CircleFilesAccessGrants(circle.Circle.Id, contribution.Id));
        grantsRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var grantsResponse = await browserClient.SendAsync(grantsRequest);
        Assert.AreEqual(HttpStatusCode.OK, grantsResponse.StatusCode);
        var grants = await grantsResponse.Content
            .ReadFromJsonAsync<MemberAccessGrantListResponse>(ControlJson.Options);
        Assert.IsNotNull(grants);
        Assert.AreEqual(memberGrant.Id, grants.Grants.Single().Id);
        Assert.AreEqual(memberId, grants.Grants.Single().MemberId);

        var mappingRoute = BrowserRoutes.CircleFilesMemberMapping(
            circle.Circle.Id,
            contribution.Id,
            memberGrant.Id);
        using var previewRequest = CreateJsonRequest(
            HttpMethod.Post,
            mappingRoute + "/preview",
            new PreviewCircleFilesMemberMappingRequest(privateAddress.ToString(), "P"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var previewResponse = await browserClient.SendAsync(previewRequest);
        var previewJson = await previewResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode, previewJson);
        var plan = System.Text.Json.JsonSerializer
            .Deserialize<CircleFilesMemberMappingPlanResponse>(previewJson, ControlJson.Options);
        Assert.IsNotNull(plan);
        Assert.AreEqual("P", plan.DriveLetter);
        AssertSafeSyncResponse(previewJson);

        using var mappingRequest = CreateJsonRequest(
            HttpMethod.Post,
            mappingRoute + "/map",
            new ApplyCircleFilesMemberMappingRequest(privateAddress.ToString(), "P", plan.PlanId),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var mappingResponse = await browserClient.SendAsync(mappingRequest);
        var mappingJson = await mappingResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, mappingResponse.StatusCode, mappingJson);
        AssertSafeSyncResponse(mappingJson);
        Assert.IsNotNull(memberMapper.MappedSecretDigest);
        Assert.IsTrue(CryptographicOperations.FixedTimeEquals(
            grantProvisioner.IssuedSecretDigest,
            memberMapper.MappedSecretDigest));
        Assert.AreEqual(memberId, memberMapper.MappedMemberId);
    }

    private static IPAddress? FindOperationalPrivateAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up
                && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address =>
            {
                if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    return false;
                }

                var octets = address.GetAddressBytes();
                return octets[0] == 10
                    || (octets[0] == 172 && octets[1] is >= 16 and <= 31)
                    || (octets[0] == 192 && octets[1] == 168);
            });
    }

    private static string AllocatePrivateEndpoint(IPAddress address)
    {
        var listener = new TcpListener(address, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        listener.Stop();
        return endpoint.ToString();
    }

    private static async Task<CircleFilesContributionResponse> CreateSyncContributionAsync(
        HttpClient owner,
        string circleId)
    {
        using var response = await owner.PostAsJsonAsync(
            ControlRoutes.CircleFilesContributions(circleId),
            new CreateCircleFilesContributionRequest(
                "0198d000-6100-7000-8000-000000000002",
                "Pilot Projects"),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<CircleFilesContributionResponse>(
            ControlJson.Options)
            ?? throw new AssertFailedException("Circle Files contribution was empty.");
    }

    private static async Task<MemberAccessGrantResponse> CreateSyncGrantAsync(
        HttpClient owner,
        string circleId,
        string contributionId,
        string requestId,
        string memberId)
    {
        using var response = await owner.PostAsJsonAsync(
            ControlRoutes.CircleFilesAccessGrants(circleId, contributionId),
            new CreateMemberAccessGrantRequest(requestId, memberId, "read-write"),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<MemberAccessGrantResponse>(ControlJson.Options)
            ?? throw new AssertFailedException("Circle Files member grant was empty.");
    }

    private static async Task ProvisionSyncGrantCredentialAsync(
        HttpClient owner,
        string circleId,
        string contributionId,
        string grantId)
    {
        const string folder = @"C:\BallsShares\Pilot";
        using var previewResponse = await owner.PostAsJsonAsync(
            ControlRoutes.CircleFilesGrantCredentialPreview(circleId, contributionId, grantId),
            new PreviewCircleFilesGrantCredentialRequest(folder),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<CircleFilesGrantCredentialPlanResponse>(ControlJson.Options);
        Assert.IsNotNull(preview);
        using var applyResponse = await owner.PostAsJsonAsync(
            ControlRoutes.CircleFilesGrantCredentialApply(circleId, contributionId, grantId),
            new ApplyCircleFilesGrantCredentialRequest(folder, preview.PlanId),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, applyResponse.StatusCode);
    }

    private static void AssertSafeSyncResponse(string response)
    {
        foreach (var forbidden in new[]
                 { "password", "secret", "signature", "transcript", "authorization" })
        {
            Assert.IsFalse(
                response.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                forbidden);
        }
    }

    private sealed class SyncGrantCredentialProvisioner : ICircleFilesGrantCredentialProvisioner
    {
        internal byte[]? IssuedSecretDigest { get; private set; }

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
            Assert.IsTrue(secret.Length >= 24);
            IssuedSecretDigest = SHA256.HashData(secret.Span);
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
                "balls-pilot",
                "BallsG-abcdef0123456",
                new string('d', 64),
                request.Access,
                request.Generation,
                ["Create one exact limited account."]);
    }

    private sealed class SyncMemberMapper : ICircleFilesMemberMapper
    {
        internal byte[]? MappedSecretDigest { get; private set; }

        internal string? MappedMemberId { get; private set; }

        public ValueTask<CircleFilesMemberMappingPlan> PreviewAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreatePlan(request));
        }

        public ValueTask<CircleFilesMemberMappingInspection> InspectAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CircleFilesMemberMappingInspection(
                "mapped",
                CreatePlan(request)));
        }

        public ValueTask<CircleFilesMemberMappingResult> MapAsync(
            CircleFilesMemberMappingRequest request,
            string expectedPlanId,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = CreatePlan(request);
            Assert.AreEqual(expectedPlanId, plan.PlanId);
            Assert.IsTrue(secret.Length >= 24);
            MappedSecretDigest = SHA256.HashData(secret.Span);
            MappedMemberId = request.MemberId;
            return ValueTask.FromResult(new CircleFilesMemberMappingResult("mapped", plan));
        }

        public ValueTask<CircleFilesMemberMappingResult> UnmapAsync(
            CircleFilesMemberMappingRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CircleFilesMemberMappingResult(
                "unmapped",
                CreatePlan(request)));
        }

        private static CircleFilesMemberMappingPlan CreatePlan(
            CircleFilesMemberMappingRequest request) =>
            new(
                1,
                new string('e', 64),
                request.Endpoint,
                $@"\\{request.Endpoint}\balls-pilot",
                request.Endpoint,
                request.DriveLetter,
                request.CircleName,
                new string('f', 64),
                ["P"],
                ["Map the exact authorized share."]);
    }
}
