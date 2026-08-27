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
using Balls.Transport.Lan;

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
        var hostProvisioner = new SyncHostProvisioner();
        var grantProvisioner = new SyncGrantCredentialProvisioner();
        var memberMapper = new SyncMemberMapper();
        var memberLauncher = new SyncLocationLauncher();
        await using var owner = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(ownerDirectory.Path, "state"),
                GetEndpoint(ownerDirectory.Path),
                "Owner-PC",
                admissionEndpoint,
                messageEndpoint),
            selected.Platform with
            {
                CircleFilesHosting = hostProvisioner,
                CircleFilesGrantCredentials = grantProvisioner,
                CircleFilesFolderPicker = new StubFolderPicker(
                    @"C:\BallsShares\Pilot",
                    "Pilot Projects"),
            },
            selected.PrivateMaterialProtector);
        using var ownerClient = CreateIpcClient(GetEndpoint(ownerDirectory.Path));
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
        string memberId;
        await using (var initialMember = await DaemonHost.StartAsync(
                         new DaemonOptions(
                             Path.Combine(memberDirectory.Path, "state"),
                             GetEndpoint(memberDirectory.Path),
                             "Member-PC"),
                         selected.Platform with
                         {
                             CircleFilesMemberMapping = memberMapper,
                             CircleFilesLocationLauncher = memberLauncher,
                         },
                         selected.PrivateMaterialProtector))
        using (var initialMemberClient = CreateIpcClient(GetEndpoint(memberDirectory.Path)))
        {
            var initialLaunch = await IssueLaunchAsync(initialMemberClient);
            var initialBrowserBaseUri = GetBrowserBaseUri(initialLaunch);
            using var initialBrowserClient = CreateBrowserClient(initialBrowserBaseUri);
            var initialAuthenticated = await ExchangeAsync(initialBrowserClient, initialLaunch);
            using var join = CreateJsonRequest(
                HttpMethod.Post,
                BrowserRoutes.CircleJoin,
                new JoinBrowserCircleRequest(
                    invitation.Package,
                    LanTcpEndpoint.ProviderName,
                    admissionEndpoint,
                    messageEndpoint,
                    "Bob"),
                GetOrigin(initialBrowserBaseUri),
                initialAuthenticated.Cookie,
                initialAuthenticated.Session.AntiforgeryToken);
            using var joinResponse = await initialBrowserClient.SendAsync(join);
            Assert.AreEqual(HttpStatusCode.OK, joinResponse.StatusCode);
            var joined = await joinResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
                ControlJson.Options);
            Assert.IsNotNull(joined);
            memberId = joined.Members.Single(person => person.DisplayName == "Bob").Id;
        }

        await using var relaunchedMember = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(memberDirectory.Path, "state"),
                GetEndpoint(memberDirectory.Path),
                "Member-PC"),
            selected.Platform with
            {
                CircleFilesMemberMapping = memberMapper,
                CircleFilesLocationLauncher = memberLauncher,
            },
            selected.PrivateMaterialProtector);
        using var memberClient = CreateIpcClient(GetEndpoint(memberDirectory.Path));
        var launch = await IssueLaunchAsync(memberClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);

        ownerClient.Dispose();
        await owner.DisposeAsync();
        using (var offlineSync = CreateJsonRequest(
                   HttpMethod.Post,
                   BrowserRoutes.CircleFilesSync(circle.Circle.Id),
                   new { },
                   GetOrigin(browserBaseUri),
                   authenticated.Cookie,
                   authenticated.Session.AntiforgeryToken))
        using (var offlineResponse = await browserClient.SendAsync(offlineSync))
        {
            var offlineJson = await offlineResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.BadGateway, offlineResponse.StatusCode, offlineJson);
            AssertSafeSyncResponse(offlineJson);
            Assert.IsFalse(offlineJson.Contains(admissionEndpoint, StringComparison.Ordinal));
            Assert.IsFalse(offlineJson.Contains(messageEndpoint, StringComparison.Ordinal));
        }

        await using var relaunchedOwner = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(ownerDirectory.Path, "state"),
                GetEndpoint(ownerDirectory.Path),
                "Owner-PC",
                admissionEndpoint,
                messageEndpoint),
            selected.Platform with
            {
                CircleFilesHosting = hostProvisioner,
                CircleFilesGrantCredentials = grantProvisioner,
                CircleFilesFolderPicker = new StubFolderPicker(
                    @"C:\BallsShares\Pilot",
                    "Pilot Projects"),
            },
            selected.PrivateMaterialProtector);
        using var relaunchedOwnerClient = CreateIpcClient(GetEndpoint(ownerDirectory.Path));
        var ownerLaunch = await IssueLaunchAsync(relaunchedOwnerClient);
        var ownerBrowserBaseUri = GetBrowserBaseUri(ownerLaunch);
        using var ownerBrowserClient = CreateBrowserClient(ownerBrowserBaseUri);
        var ownerAuthenticated = await ExchangeAsync(ownerBrowserClient, ownerLaunch);
        using var selectFolder = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderSelection(circle.Circle.Id),
            new { },
            GetOrigin(ownerBrowserBaseUri),
            ownerAuthenticated.Cookie,
            ownerAuthenticated.Session.AntiforgeryToken);
        using var selectFolderResponse = await ownerBrowserClient.SendAsync(selectFolder);
        var folderSelection = await selectFolderResponse.Content
            .ReadFromJsonAsync<BrowserCircleFilesFolderSelectionResponse>(ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, selectFolderResponse.StatusCode);
        Assert.IsNotNull(folderSelection?.SelectionId);
        using var contribute = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            new ApplyBrowserCircleFilesFolderRequest(
                "0198d000-6100-7000-8000-000000000002",
                folderSelection!.SelectionId!),
            GetOrigin(ownerBrowserBaseUri),
            ownerAuthenticated.Cookie,
            ownerAuthenticated.Session.AntiforgeryToken);
        using var contributeResponse = await ownerBrowserClient.SendAsync(contribute);
        Assert.AreEqual(HttpStatusCode.OK, contributeResponse.StatusCode);
        var contribution = await contributeResponse.Content
            .ReadFromJsonAsync<BrowserCircleFilesContributionResponse>(ControlJson.Options);
        Assert.IsNotNull(contribution);
        hostProvisioner.ApplyFailure = new CircleFilesHostingException(
            "circle_files_host_collision",
            "The selected folder has a different ownership marker.");
        using var duplicateSelectFolder = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderSelection(circle.Circle.Id),
            new { },
            GetOrigin(ownerBrowserBaseUri),
            ownerAuthenticated.Cookie,
            ownerAuthenticated.Session.AntiforgeryToken);
        using var duplicateSelectFolderResponse = await ownerBrowserClient.SendAsync(
            duplicateSelectFolder);
        var duplicateFolderSelection = await duplicateSelectFolderResponse.Content
            .ReadFromJsonAsync<BrowserCircleFilesFolderSelectionResponse>(ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, duplicateSelectFolderResponse.StatusCode);
        Assert.IsNotNull(duplicateFolderSelection?.SelectionId);
        using var duplicateContribution = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            new ApplyBrowserCircleFilesFolderRequest(
                "0198d000-6100-7000-8000-000000000004",
                duplicateFolderSelection!.SelectionId!),
            GetOrigin(ownerBrowserBaseUri),
            ownerAuthenticated.Cookie,
            ownerAuthenticated.Session.AntiforgeryToken);
        using var duplicateContributionResponse = await ownerBrowserClient.SendAsync(
            duplicateContribution);
        var duplicateContributionJson = await duplicateContributionResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(
            HttpStatusCode.Conflict,
            duplicateContributionResponse.StatusCode,
            duplicateContributionJson);
        StringAssert.Contains(duplicateContributionJson, "different ownership marker");
        hostProvisioner.ApplyFailure = null;
        var contributionsAfterCollision = await relaunchedOwnerClient
            .GetFromJsonAsync<CircleFilesContributionListResponse>(
                ControlRoutes.CircleFilesContributions(circle.Circle.Id),
                ControlJson.Options);
        Assert.IsNotNull(contributionsAfterCollision);
        Assert.HasCount(2, contributionsAfterCollision.Contributions);
        await using (var inspectionStore = await Balls.Storage.Sqlite.SqliteLocalStateStore
                         .OpenAsync(
                             Path.Combine(ownerDirectory.Path, "state"),
                             selected.PrivateMaterialProtector))
        {
            var hostedBindingCount = 0;
            foreach (var candidate in contributionsAfterCollision.Contributions)
            {
                var hosted = await inspectionStore.GetCircleFilesHostedFolderAsync(
                    new Balls.Core.CircleId(Guid.Parse(circle.Circle.Id)),
                    new Balls.Core.CircleFilesContributionId(Guid.Parse(candidate.Id)));
                if (hosted is not null)
                {
                    hostedBindingCount += 1;
                }
            }

            Assert.AreEqual(1, hostedBindingCount);
        }
        _ = await CreateSyncGrantAsync(
            relaunchedOwnerClient,
            circle.Circle.Id,
            contribution.ContributionId,
            "0198d000-6100-7000-8000-000000000003",
            circle.Members.Single().Id);

        using var previewGrant = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesGrantPreview(circle.Circle.Id),
            new PreviewBrowserCircleFilesGrantRequest(
                "Pilot Projects",
                "Bob",
                "read-write"),
            GetOrigin(ownerBrowserBaseUri),
            ownerAuthenticated.Cookie,
            ownerAuthenticated.Session.AntiforgeryToken);
        using var previewGrantResponse = await ownerBrowserClient.SendAsync(previewGrant);
        var previewGrantJson = await previewGrantResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, previewGrantResponse.StatusCode, previewGrantJson);
        var grantSummary = System.Text.Json.JsonSerializer
            .Deserialize<BrowserCircleFilesGrantPreviewResponse>(
                previewGrantJson,
                ControlJson.Options);
        Assert.IsNotNull(grantSummary);
        Assert.AreEqual("Pilot Projects", grantSummary.FolderName);
        Assert.AreEqual("Bob", grantSummary.MemberName);
        Assert.AreEqual("Read/write", grantSummary.Access);
        StringAssert.Contains(grantSummary.Summary, @"C:\BallsShares\Pilot");
        AssertSafeBrowserGrantResponse(previewGrantJson);

        using (var failedApply = CreateJsonRequest(
                   HttpMethod.Post,
                   BrowserRoutes.CircleFilesGrantApply(circle.Circle.Id),
                   new { },
                   GetOrigin(ownerBrowserBaseUri),
                   ownerAuthenticated.Cookie,
                   ownerAuthenticated.Session.AntiforgeryToken))
        using (var failedApplyResponse = await ownerBrowserClient.SendAsync(failedApply))
        {
            var failedJson = await failedApplyResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.Conflict, failedApplyResponse.StatusCode, failedJson);
            AssertSafeBrowserGrantResponse(failedJson);
        }

        using var retryApply = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesGrantApply(circle.Circle.Id),
            new { },
            GetOrigin(ownerBrowserBaseUri),
            ownerAuthenticated.Cookie,
            ownerAuthenticated.Session.AntiforgeryToken);
        using var retryApplyResponse = await ownerBrowserClient.SendAsync(retryApply);
        var retryJson = await retryApplyResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retryApplyResponse.StatusCode, retryJson);
        AssertSafeBrowserGrantResponse(retryJson);

        using var staleApply = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesGrantApply(circle.Circle.Id),
            new { },
            GetOrigin(ownerBrowserBaseUri),
            ownerAuthenticated.Cookie,
            ownerAuthenticated.Session.AntiforgeryToken);
        using var staleApplyResponse = await ownerBrowserClient.SendAsync(staleApply);
        var staleJson = await staleApplyResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Conflict, staleApplyResponse.StatusCode, staleJson);
        AssertSafeBrowserGrantResponse(staleJson);

        var ownerGrants = await relaunchedOwnerClient
            .GetFromJsonAsync<MemberAccessGrantListResponse>(
                ControlRoutes.CircleFilesAccessGrants(
                    circle.Circle.Id,
                    contribution.ContributionId),
                ControlJson.Options);
        Assert.IsNotNull(ownerGrants);
        var memberGrant = ownerGrants.Grants.Single(value => value.MemberId == memberId);
        Assert.AreEqual(2, ownerGrants.Grants.Count);
        Assert.IsNotNull(grantProvisioner.IssuedSecretDigest);
        Assert.AreEqual(2, grantProvisioner.ApplyCount);
        Assert.IsTrue(CryptographicOperations.FixedTimeEquals(
            grantProvisioner.FirstAttemptSecretDigest!,
            grantProvisioner.IssuedSecretDigest!));

        using var synchronize = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesSync(circle.Circle.Id),
            new { },
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

        using var unauthorizedPreview = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesGrantPreview(circle.Circle.Id),
            new PreviewBrowserCircleFilesGrantRequest(
                "Pilot Projects",
                "Bob",
                "read-write"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var unauthorizedPreviewResponse = await browserClient.SendAsync(unauthorizedPreview);
        var unauthorizedJson = await unauthorizedPreviewResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Forbidden, unauthorizedPreviewResponse.StatusCode);
        AssertSafeBrowserGrantResponse(unauthorizedJson);

        using var contributionsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BrowserRoutes.CircleFilesContributions(circle.Circle.Id));
        contributionsRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var contributionsResponse = await browserClient.SendAsync(contributionsRequest);
        Assert.AreEqual(HttpStatusCode.OK, contributionsResponse.StatusCode);
        var contributions = await contributionsResponse.Content
            .ReadFromJsonAsync<CircleFilesContributionListResponse>(ControlJson.Options);
        Assert.IsNotNull(contributions);
        Assert.AreEqual(contribution.ContributionId, contributions.Contributions.Single().Id);

        using var grantsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BrowserRoutes.CircleFilesAccessGrants(
                circle.Circle.Id,
                contribution.ContributionId));
        grantsRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var grantsResponse = await browserClient.SendAsync(grantsRequest);
        Assert.AreEqual(HttpStatusCode.OK, grantsResponse.StatusCode);
        var grants = await grantsResponse.Content
            .ReadFromJsonAsync<MemberAccessGrantListResponse>(ControlJson.Options);
        Assert.IsNotNull(grants);
        Assert.AreEqual(memberGrant.Id, grants.Grants.Single().Id);
        Assert.AreEqual(memberId, grants.Grants.Single().MemberId);

        memberMapper.MapFailure = new CircleFilesHostingException(
            "mapping_endpoint_unreachable",
            "injected endpoint detail");
        using (var offlineOpenRequest = CreateJsonRequest(
                   HttpMethod.Post,
                   BrowserRoutes.CircleFilesOpen(circle.Circle.Id),
                   new { },
                   GetOrigin(browserBaseUri),
                   authenticated.Cookie,
                   authenticated.Session.AntiforgeryToken))
        using (var offlineOpenResponse = await browserClient.SendAsync(offlineOpenRequest))
        {
            var offlineOpenJson = await offlineOpenResponse.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.BadGateway, offlineOpenResponse.StatusCode, offlineOpenJson);
            StringAssert.Contains(offlineOpenJson, "shared_folder_offline");
            AssertSafeOpenResponse(offlineOpenJson);
            Assert.IsNull(memberMapper.MappedDrive);
            Assert.IsEmpty(memberLauncher.DriveLetters);
        }

        memberMapper.MapFailure = null;
        memberLauncher.Fail = true;
        using var failedOpenRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesOpen(circle.Circle.Id),
            new { },
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var failedOpenResponse = await browserClient.SendAsync(failedOpenRequest);
        var failedOpenJson = await failedOpenResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.BadGateway, failedOpenResponse.StatusCode, failedOpenJson);
        StringAssert.Contains(failedOpenJson, "explorer_launch_failed");
        AssertSafeOpenResponse(failedOpenJson);
        Assert.AreEqual("P", memberMapper.MappedDrive);

        memberLauncher.Fail = false;
        using var openRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesOpen(circle.Circle.Id),
            new { },
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var openResponse = await browserClient.SendAsync(openRequest);
        var openJson = await openResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, openResponse.StatusCode, openJson);
        var opened = System.Text.Json.JsonSerializer.Deserialize<BrowserCircleFilesOpenResponse>(
            openJson,
            ControlJson.Options);
        Assert.IsNotNull(opened);
        Assert.AreEqual("opened", opened.Status);
        Assert.AreEqual("Pilot Projects", opened.FolderName);
        AssertSafeOpenResponse(openJson);
        Assert.IsNotNull(memberMapper.MappedSecretDigest);
        Assert.IsTrue(CryptographicOperations.FixedTimeEquals(
            grantProvisioner.IssuedSecretDigest,
            memberMapper.MappedSecretDigest));
        Assert.AreEqual(memberId, memberMapper.MappedMemberId);
        Assert.AreEqual(2, memberMapper.MapCount);
        CollectionAssert.AreEqual(new[] { "P", "P" }, memberLauncher.DriveLetters.ToArray());
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

    private static void AssertSafeBrowserGrantResponse(string response)
    {
        foreach (var forbidden in new[]
                 {
                     "provider", "account", "password", "plan", "secret", "signature",
                     "transcript", "authorization", "address", "port", "grantId", "memberId",
                     "contributionId",
                 })
        {
            Assert.IsFalse(
                response.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                forbidden);
        }
    }

    private static void AssertSafeOpenResponse(string response)
    {
        foreach (var forbidden in new[]
                 {
                     "provider", "account", "password", "plan", "secret", "signature",
                     "transcript", "authorization", "address", "port", "grantId", "memberId",
                     "contributionId", "endpoint", "driveLetter", "uncPath", "credentialTarget",
                 })
        {
            Assert.IsFalse(
                response.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                forbidden);
        }
    }

    private sealed class SyncHostProvisioner : ICircleFilesHostProvisioner
    {
        internal CircleFilesHostingException? ApplyFailure { get; set; }

        public ValueTask<CircleFilesHostPlan> PreviewAsync(
            CircleFilesHostRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreatePlan(request));
        }

        public ValueTask<CircleFilesHostApplyResult> ApplyAsync(
            CircleFilesHostRequest request,
            string expectedPlanId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ApplyFailure is not null)
            {
                throw ApplyFailure;
            }
            var plan = CreatePlan(request);
            Assert.AreEqual(expectedPlanId, plan.PlanId);
            return ValueTask.FromResult(new CircleFilesHostApplyResult(
                CircleFilesHostApplyStatus.Applied,
                plan));
        }

        private static CircleFilesHostPlan CreatePlan(CircleFilesHostRequest request) => new(
            1,
            new string('a', 64),
            CircleFilesReadinessProviders.WindowsSmb311,
            request.FolderPath,
            "balls-pilot",
            "Balls-SMB-pilot",
            new string('b', 64),
            true,
            ["Preserve and host the selected folder."]);
    }

    private sealed class SyncGrantCredentialProvisioner : ICircleFilesGrantCredentialProvisioner
    {
        internal byte[]? IssuedSecretDigest { get; private set; }
        internal byte[]? FirstAttemptSecretDigest { get; private set; }
        internal int ApplyCount { get; private set; }

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
            ApplyCount += 1;
            var digest = SHA256.HashData(secret.Span);
            if (ApplyCount == 1)
            {
                FirstAttemptSecretDigest = digest;
                throw new CircleFilesHostingException(
                    "grant_apply_failed",
                    "Provider account password plan address port must stay internal.");
            }
            IssuedSecretDigest = digest;
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

        internal string? MappedDrive { get; private set; }

        internal int MapCount { get; private set; }

        internal CircleFilesHostingException? MapFailure { get; set; }

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
                request.DriveLetter == MappedDrive ? "mapped" : "unmapped",
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
            if (MapFailure is not null)
            {
                throw MapFailure;
            }
            MappedSecretDigest = SHA256.HashData(secret.Span);
            MappedMemberId = request.MemberId;
            MapCount++;
            var status = MappedDrive is null ? "mapped" : "already-mapped";
            if (MappedDrive is not null)
            {
                Assert.AreEqual(MappedDrive, request.DriveLetter);
            }
            MappedDrive = request.DriveLetter;
            return ValueTask.FromResult(new CircleFilesMemberMappingResult(status, plan));
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

        private CircleFilesMemberMappingPlan CreatePlan(
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
                MappedDrive is null ? ["M", "P"] : ["M"],
                ["Map the exact authorized share."]);
    }

    private sealed class SyncLocationLauncher : ICircleFilesLocationLauncher
    {
        internal List<string> DriveLetters { get; } = [];
        internal bool Fail { get; set; }

        public ValueTask OpenAsync(
            CircleFilesMappedLocation location,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DriveLetters.Add(location.DriveLetter);
            return Fail
                ? ValueTask.FromException(new CircleFilesHostingException(
                    "explorer_launch_failed",
                    "injected native detail"))
                : ValueTask.CompletedTask;
        }
    }
}
