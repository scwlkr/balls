using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Balls.Daemon;
using Balls.Host;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;
using Balls.Transport.Lan;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed partial class BrowserAdapterSecurityTests
{
    [TestMethod]
    public async Task Browser_listener_is_loopback_only_serves_offline_assets_and_hides_control_plane()
    {
        using var directory = new TemporaryDirectory();
        await using var daemon = await StartDaemonAsync(directory.Path);
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);

        using var indexResponse = await browserClient.GetAsync("/");
        var index = await indexResponse.Content.ReadAsStringAsync();
        using var controlResponse = await browserClient.GetAsync(ControlRoutes.Status);
        var listeners = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == browserBaseUri.Port)
            .ToArray();

        Assert.AreEqual(HttpStatusCode.OK, indexResponse.StatusCode);
        StringAssert.Contains(index, "<div id=\"root\"></div>");
        AssertSecurityHeaders(indexResponse);
        Assert.IsFalse(indexResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.AreEqual(HttpStatusCode.NotFound, controlResponse.StatusCode);
        Assert.HasCount(1, listeners);
        Assert.IsTrue(IPAddress.IsLoopback(listeners[0].Address));

        var assets = AssetPath().Matches(index).Select(match => match.Groups[1].Value).ToArray();
        Assert.IsNotEmpty(assets);
        foreach (var asset in assets)
        {
            using var assetResponse = await browserClient.GetAsync(asset);
            Assert.AreEqual(HttpStatusCode.OK, assetResponse.StatusCode, asset);
            AssertSecurityHeaders(assetResponse);
        }
    }

    [TestMethod]
    public async Task Launch_exchange_is_single_use_and_sets_a_hardened_session_cookie()
    {
        using var directory = new TemporaryDirectory();
        await using var daemon = await StartDaemonAsync(directory.Path);
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var capability = ReadCapability(launch.Url);

        using var ambiguousOrigin = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.Session,
            new ExchangeBrowserSessionRequest(capability),
            GetOrigin(browserBaseUri));
        ambiguousOrigin.Headers.TryAddWithoutValidation("Origin", "http://hostile.example");
        using var ambiguousOriginResponse = await browserClient.SendAsync(ambiguousOrigin);

        using var hostileExchange = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.Session,
            new ExchangeBrowserSessionRequest(capability),
            "http://hostile.example");
        using var hostileResponse = await browserClient.SendAsync(hostileExchange);

        using var exchange = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.Session,
            new ExchangeBrowserSessionRequest(capability),
            GetOrigin(browserBaseUri));
        using var response = await browserClient.SendAsync(exchange);
        var session = await response.Content.ReadFromJsonAsync<BrowserSessionResponse>(
            ControlJson.Options);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();

        using var replay = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.Session,
            new ExchangeBrowserSessionRequest(capability),
            GetOrigin(browserBaseUri));
        using var replayResponse = await browserClient.SendAsync(replay);
        var replayBody = await replayResponse.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Forbidden, ambiguousOriginResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, hostileResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(session);
        StringAssert.Contains(setCookie, "__Host-balls-session=");
        StringAssert.Contains(setCookie, "path=/", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(setCookie, "httponly", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(setCookie, "secure", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(setCookie, "samesite=strict", StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(setCookie.Contains(capability, StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        Assert.IsFalse(replayBody.Contains(capability, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Browser_api_rejects_host_origin_and_missing_antiforgery_before_mutation()
    {
        using var directory = new TemporaryDirectory();
        await using var daemon = await StartDaemonAsync(directory.Path);
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);

        using var hostileHost = new HttpRequestMessage(HttpMethod.Get, "/");
        hostileHost.Headers.Host = "hostile.example";
        using var hostileHostResponse = await browserClient.SendAsync(hostileHost);

        using var ambiguousHost = new HttpRequestMessage(HttpMethod.Get, "/");
        ambiguousHost.Headers.TryAddWithoutValidation(
            "Host",
            [browserBaseUri.Authority, "hostile.example"]);
        using var ambiguousHostResponse = await browserClient.SendAsync(ambiguousHost);

        var createRequest = new CreateCircleRequest(
            "0198c2d8-b000-7000-8000-000000000501",
            "Secure Circle",
            "Alice");
        using var missingAntiforgery = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.Circles,
            createRequest,
            GetOrigin(browserBaseUri),
            authenticated.Cookie);
        using var missingAntiforgeryResponse = await browserClient.SendAsync(missingAntiforgery);

        using var validCreate = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.Circles,
            createRequest,
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var createResponse = await browserClient.SendAsync(validCreate);
        var created = await createResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(created);

        using var missingSyncAntiforgery = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesSync(created.Circle.Id),
            new { },
            GetOrigin(browserBaseUri),
            authenticated.Cookie);
        using var missingSyncAntiforgeryResponse = await browserClient.SendAsync(
            missingSyncAntiforgery);

        using var missingWizardAntiforgery = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.WizardInstall,
            new { },
            GetOrigin(browserBaseUri),
            authenticated.Cookie);
        using var missingWizardAntiforgeryResponse = await browserClient.SendAsync(
            missingWizardAntiforgery);

        using var missingConnectionSync = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesSync(created.Circle.Id),
            new { },
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var missingConnectionSyncResponse = await browserClient.SendAsync(
            missingConnectionSync);

        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, BrowserRoutes.Status);
        statusRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var statusResponse = await browserClient.SendAsync(statusRequest);

        Assert.AreEqual(HttpStatusCode.BadRequest, hostileHostResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, ambiguousHostResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, missingAntiforgeryResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.AreEqual("Secure Circle", created.Circle.Name);
        Assert.AreEqual(HttpStatusCode.Forbidden, missingSyncAntiforgeryResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, missingWizardAntiforgeryResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, missingConnectionSyncResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);
    }

    [TestMethod]
    public async Task Browser_projection_lists_Circle_Files_but_rejects_the_raw_mutation_route()
    {
        using var directory = new TemporaryDirectory();
        await using var daemon = await StartDaemonAsync(directory.Path);
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        using var createCircleResponse = await ipcClient.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-5000-7000-8000-000000000001",
                "Files Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircleResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);
        var controlPath = ControlRoutes.CircleFilesContributions(circle.Circle.Id);
        using var createContributionResponse = await ipcClient.PostAsJsonAsync(
            controlPath,
            new CreateCircleFilesContributionRequest(
                "0198d000-5000-7000-8000-000000000002",
                "Project Files"),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.Created, createContributionResponse.StatusCode);
        var contribution = await createContributionResponse.Content
            .ReadFromJsonAsync<CircleFilesContributionResponse>(ControlJson.Options);
        Assert.IsNotNull(contribution);
        var grantControlPath = ControlRoutes.CircleFilesAccessGrants(
            circle.Circle.Id,
            contribution.Id);
        using var createGrantResponse = await ipcClient.PostAsJsonAsync(
            grantControlPath,
            new CreateMemberAccessGrantRequest(
                "0198d000-5000-7000-8000-000000000003",
                circle.Members.Single().Id,
                "read-only"),
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.Created, createGrantResponse.StatusCode);

        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);
        using var viewerRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BrowserRoutes.CircleViewer(circle.Circle.Id));
        viewerRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var viewerResponse = await browserClient.SendAsync(viewerRequest);
        var viewer = await viewerResponse.Content.ReadFromJsonAsync<BrowserCircleViewerResponse>(
            ControlJson.Options);
        var path = BrowserRoutes.CircleFilesContributions(circle.Circle.Id);
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, path);
        listRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var listResponse = await browserClient.SendAsync(listRequest);
        var listed = await listResponse.Content.ReadFromJsonAsync<CircleFilesContributionListResponse>(
            ControlJson.Options);
        var grantPath = BrowserRoutes.CircleFilesAccessGrants(circle.Circle.Id, contribution.Id);
        using var grantListRequest = new HttpRequestMessage(HttpMethod.Get, grantPath);
        grantListRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var grantListResponse = await browserClient.SendAsync(grantListRequest);
        var listedGrants = await grantListResponse.Content
            .ReadFromJsonAsync<MemberAccessGrantListResponse>(ControlJson.Options);
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            path,
            new CreateCircleFilesContributionRequest(
                "0198d000-5000-7000-8000-000000000004",
                "Must Not Be Created"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);

        using var response = await browserClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, viewerResponse.StatusCode);
        Assert.IsNotNull(viewer);
        Assert.AreEqual(circle.Members.Single().Id, viewer.MemberId);
        Assert.AreEqual("owner", viewer.Role);
        Assert.IsNotNull(listed);
        Assert.AreEqual(circle.Circle.Id, listed.CircleId);
        Assert.HasCount(1, listed.Contributions);
        Assert.AreEqual("Project Files", listed.Contributions[0].DisplayName);
        Assert.AreEqual(HttpStatusCode.OK, grantListResponse.StatusCode);
        Assert.IsNotNull(listedGrants);
        Assert.HasCount(1, listedGrants.Grants);
        Assert.AreEqual("read-only", listedGrants.Grants[0].Access);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [TestMethod]
    public async Task Owner_browser_selects_and_idempotently_contributes_one_exact_existing_folder()
    {
        using var directory = new TemporaryDirectory();
        var selectedHost = (SupportedHostPlatform)HostPlatformSelector.SelectCurrent();
        var picker = new StubFolderPicker(@"C:\BallsDemo\Projects", "Projects");
        var hosting = new StubHostProvisioner();
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        var host = selectedHost.Platform with
        {
            CircleFilesFolderPicker = picker,
            CircleFilesHosting = hosting,
        };
        var endpoint = GetEndpoint(directory.Path);
        await using var daemon = await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(directory.Path, "state"),
                endpoint,
                "Browser-PC"),
            host,
            selectedHost.PrivateMaterialProtector,
            timeProvider: time);
        using var ipcClient = CreateIpcClient(endpoint);
        var circle = await (await ipcClient.PostAsJsonAsync(
                ControlRoutes.Circles,
                new CreateCircleRequest(
                    "0198d000-5000-7000-8000-000000000011",
                    "Files Circle",
                    "Alice"),
                ControlJson.Options))
            .Content.ReadFromJsonAsync<CircleDetailsResponse>(ControlJson.Options);
        Assert.IsNotNull(circle);
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);

        using var missingSelectionRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            new ApplyBrowserCircleFilesFolderRequest(
                "0198d000-5000-7000-8000-000000000012",
                "0198d000-5000-7000-8000-000000000013"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var missingSelectionResponse = await browserClient.SendAsync(missingSelectionRequest);
        Assert.AreEqual(HttpStatusCode.Conflict, missingSelectionResponse.StatusCode);
        Assert.HasCount(0, hosting.Requests);

        using var selectRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderSelection(circle.Circle.Id),
            new { },
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var selectResponse = await browserClient.SendAsync(selectRequest);
        var selection = await selectResponse.Content
            .ReadFromJsonAsync<BrowserCircleFilesFolderSelectionResponse>(ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, selectResponse.StatusCode);
        Assert.IsNotNull(selection);
        Assert.AreEqual("selected", selection.Status);
        Assert.IsNotNull(selection.SelectionId);
        Assert.AreEqual(@"C:\BallsDemo\Projects", selection.FolderPath);

        var otherLaunch = await IssueLaunchAsync(ipcClient);
        using var otherBrowserClient = CreateBrowserClient(browserBaseUri);
        var otherAuthenticated = await ExchangeAsync(otherBrowserClient, otherLaunch);
        using var crossSessionRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            new ApplyBrowserCircleFilesFolderRequest(
                "0198d000-5000-7000-8000-000000000012",
                selection.SelectionId),
            GetOrigin(browserBaseUri),
            otherAuthenticated.Cookie,
            otherAuthenticated.Session.AntiforgeryToken);
        using var crossSessionResponse = await otherBrowserClient.SendAsync(crossSessionRequest);
        Assert.AreEqual(HttpStatusCode.Conflict, crossSessionResponse.StatusCode);
        Assert.HasCount(0, hosting.Requests);

        using var substitutedSelectionRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            new ApplyBrowserCircleFilesFolderRequest(
                "0198d000-5000-7000-8000-000000000012",
                "0198d000-5000-7000-8000-000000000014"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var substitutedSelectionResponse = await browserClient.SendAsync(
            substitutedSelectionRequest);
        Assert.AreEqual(HttpStatusCode.Conflict, substitutedSelectionResponse.StatusCode);
        Assert.HasCount(0, hosting.Requests);

        var applyBody = new ApplyBrowserCircleFilesFolderRequest(
            "0198d000-5000-7000-8000-000000000012",
            selection.SelectionId!);
        time.Advance(TimeSpan.FromMinutes(16));
        using var staleRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            applyBody,
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var staleResponse = await browserClient.SendAsync(staleRequest);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.HasCount(0, hosting.Requests);

        using var replacementSelectRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderSelection(circle.Circle.Id),
            new { },
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var replacementSelectResponse = await browserClient.SendAsync(replacementSelectRequest);
        var replacement = await replacementSelectResponse.Content
            .ReadFromJsonAsync<BrowserCircleFilesFolderSelectionResponse>(ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, replacementSelectResponse.StatusCode);
        Assert.IsNotNull(replacement?.SelectionId);
        applyBody = applyBody with { SelectionId = replacement!.SelectionId! };
        using var applyRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            applyBody,
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var applyResponse = await browserClient.SendAsync(applyRequest);
        var appliedJson = await applyResponse.Content.ReadAsStringAsync();
        var applied = System.Text.Json.JsonSerializer
            .Deserialize<BrowserCircleFilesContributionResponse>(appliedJson, ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, applyResponse.StatusCode);
        Assert.IsNotNull(applied);
        Assert.AreEqual("applied", applied.Status);
        Assert.AreEqual(selection.FolderPath, applied.FolderPath);

        using var retryRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            applyBody,
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var retryResponse = await browserClient.SendAsync(retryRequest);
        var retried = await retryResponse.Content
            .ReadFromJsonAsync<BrowserCircleFilesContributionResponse>(ControlJson.Options);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BrowserRoutes.CircleFilesContributions(circle.Circle.Id));
        listRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var listResponse = await browserClient.SendAsync(listRequest);
        var listed = await listResponse.Content
            .ReadFromJsonAsync<CircleFilesContributionListResponse>(ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.AreEqual("already-applied", retried?.Status);
        Assert.IsNotNull(listed);
        Assert.HasCount(1, listed.Contributions);
        Assert.AreEqual(4, hosting.Requests.Count);
        Assert.IsTrue(hosting.Requests.All(request =>
            request.FolderPath == @"C:\BallsDemo\Projects"));
        Assert.IsFalse(appliedJson.Contains("authorization", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(appliedJson.Contains("shareName", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(appliedJson.Contains("firewall", StringComparison.OrdinalIgnoreCase));

        using var substitutedRequestId = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleFilesFolderApply(circle.Circle.Id),
            applyBody with { RequestId = "0198d000-5000-7000-8000-000000000015" },
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var substitutedRequestIdResponse = await browserClient.SendAsync(substitutedRequestId);
        Assert.AreEqual(HttpStatusCode.Conflict, substitutedRequestIdResponse.StatusCode);
        Assert.AreEqual(4, hosting.Requests.Count);
    }

    [TestMethod]
    public async Task Browser_invitation_requires_antiforgery_and_uses_actual_automatic_listener_addresses()
    {
        using var directory = new TemporaryDirectory();
        var privateAddress = PrivateIPv4AddressSelector.SelectCurrent();
        if (!privateAddress.IsAvailable)
        {
            Assert.Inconclusive("This host does not have exactly one operational private IPv4 address.");
            return;
        }

        await using var daemon = await StartDaemonAsync(
            directory.Path,
            automaticPrivateListeners: true,
            privateAddressSelector: () => privateAddress);
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        using var createCircleResponse = await ipcClient.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-6000-7000-8000-000000000001",
                "Invitation Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircleResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);
        var route = BrowserRoutes.CircleInvitations(circle.Circle.Id);

        using var missingAntiforgery = CreateJsonRequest(
            HttpMethod.Post,
            route,
            new CreateBrowserCircleInvitationRequest(60),
            GetOrigin(browserBaseUri),
            authenticated.Cookie);
        using var missingAntiforgeryResponse = await browserClient.SendAsync(missingAntiforgery);

        using var invitationRequest = CreateJsonRequest(
            HttpMethod.Post,
            route,
            new CreateBrowserCircleInvitationRequest(60),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var invitationResponse = await browserClient.SendAsync(invitationRequest);
        var invitationJson = await invitationResponse.Content.ReadAsStringAsync();
        var invitation = JsonSerializer.Deserialize<BrowserCircleInvitationResponse>(
            invitationJson,
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.Forbidden, missingAntiforgeryResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, invitationResponse.StatusCode);
        Assert.IsNotNull(invitation);
        Assert.AreEqual(circle.Circle.Id, invitation.CircleId);
        Assert.AreEqual(LanTcpEndpoint.ProviderName, invitation.Provider);
        Assert.AreEqual(daemon.AdmissionAddress!.Value, invitation.Endpoint);
        Assert.AreEqual(daemon.MessageAddress!.Value, invitation.SyncEndpoint);
        Assert.AreEqual(privateAddress.Address, IPEndPoint.Parse(invitation.Endpoint).Address);
        Assert.AreEqual(privateAddress.Address, IPEndPoint.Parse(invitation.SyncEndpoint).Address);
        Assert.AreNotEqual(0, IPEndPoint.Parse(invitation.Endpoint).Port);
        Assert.AreNotEqual(0, IPEndPoint.Parse(invitation.SyncEndpoint).Port);
        Assert.IsNotEmpty(invitation.Package);
        using var invitationDocument = JsonDocument.Parse(invitationJson);
        CollectionAssert.AreEquivalent(
            new[] { "circleId", "invitationId", "expiresAtUtc", "package", "provider", "endpoint", "syncEndpoint" },
            invitationDocument.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    public async Task Browser_invitation_can_project_a_private_nat_address_without_changing_listener_bindings()
    {
        using var directory = new TemporaryDirectory();
        var privateAddress = SelectBindablePrivateAddress();
        if (privateAddress?.Address is not { } bindAddress)
        {
            Assert.Inconclusive("This host does not have an operational private IPv4 address.");
            return;
        }
        var advertisedAddress = bindAddress.Equals(IPAddress.Parse("10.254.254.254"))
            ? IPAddress.Parse("10.254.254.253")
            : IPAddress.Parse("10.254.254.254");

        await using var daemon = await StartDaemonAsync(
            directory.Path,
            automaticPrivateListeners: true,
            privateAddressSelector: () => privateAddress,
            advertisedPrivateAddress: advertisedAddress.ToString());
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        using var createCircleResponse = await ipcClient.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-6000-7000-8000-000000000011",
                "NAT Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircleResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);
        using var invitationRequest = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleInvitations(circle.Circle.Id),
            new CreateBrowserCircleInvitationRequest(60),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var invitationResponse = await browserClient.SendAsync(invitationRequest);
        var invitation = await invitationResponse.Content.ReadFromJsonAsync<BrowserCircleInvitationResponse>(
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.Created, invitationResponse.StatusCode);
        Assert.IsNotNull(invitation);
        Assert.AreEqual(privateAddress.Address, IPEndPoint.Parse(daemon.AdmissionAddress!.Value).Address);
        Assert.AreEqual(privateAddress.Address, IPEndPoint.Parse(daemon.MessageAddress!.Value).Address);
        Assert.AreEqual(advertisedAddress, IPEndPoint.Parse(invitation.Endpoint).Address);
        Assert.AreEqual(advertisedAddress, IPEndPoint.Parse(invitation.SyncEndpoint).Address);
        Assert.AreEqual(
            IPEndPoint.Parse(daemon.AdmissionAddress.Value).Port,
            IPEndPoint.Parse(invitation.Endpoint).Port);
        Assert.AreEqual(
            IPEndPoint.Parse(daemon.MessageAddress.Value).Port,
            IPEndPoint.Parse(invitation.SyncEndpoint).Port);
    }

    [TestMethod]
    public async Task Automatic_private_listener_ports_survive_daemon_relaunch()
    {
        using var directory = new TemporaryDirectory();
        var privateAddress = SelectBindablePrivateAddress();
        if (privateAddress is null)
        {
            Assert.Inconclusive("This host does not have an operational private IPv4 address.");
            return;
        }

        string admissionAddress;
        string messageAddress;
        await using (var first = await StartDaemonAsync(
                         directory.Path,
                         automaticPrivateListeners: true,
                         privateAddressSelector: () => privateAddress))
        {
            admissionAddress = first.AdmissionAddress!.Value;
            messageAddress = first.MessageAddress!.Value;
        }

        await using var relaunched = await StartDaemonAsync(
            directory.Path,
            automaticPrivateListeners: true,
            privateAddressSelector: () => privateAddress);

        Assert.AreEqual(admissionAddress, relaunched.AdmissionAddress!.Value);
        Assert.AreEqual(messageAddress, relaunched.MessageAddress!.Value);
    }

    [TestMethod]
    public async Task Invalid_automatic_private_listener_port_record_fails_closed()
    {
        using var directory = new TemporaryDirectory();
        var privateAddress = SelectBindablePrivateAddress();
        if (privateAddress is null)
        {
            Assert.Inconclusive("This host does not have an operational private IPv4 address.");
            return;
        }

        await using (var daemon = await StartDaemonAsync(
                         directory.Path,
                         automaticPrivateListeners: true,
                         privateAddressSelector: () => privateAddress))
        {
        }

        await File.WriteAllTextAsync(
            Path.Combine(
                directory.Path,
                "state",
                AutomaticPrivateListenerPortStore.FileName),
            "{\"schemaVersion\":1,\"admissionPort\":40321,\"messagePort\":40321}");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => StartDaemonAsync(
                directory.Path,
                automaticPrivateListeners: true,
                privateAddressSelector: () => privateAddress));
    }

    [TestMethod]
    public async Task Ambiguous_automatic_private_network_keeps_browser_available_and_refuses_invitation()
    {
        using var directory = new TemporaryDirectory();
        await using var daemon = await StartDaemonAsync(
            directory.Path,
            automaticPrivateListeners: true,
            privateAddressSelector: () => new PrivateIPv4AddressSelection(
                null,
                "private_network_ambiguous",
                "Balls found more than one private network connection and cannot safely choose one for invitations."));
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        using var createCircleResponse = await ipcClient.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-6000-7000-8000-000000000002",
                "Ambiguous Network Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircleResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.CircleInvitations(circle.Circle.Id),
            new CreateBrowserCircleInvitationRequest(60),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var response = await browserClient.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ErrorResponse>(responseJson, ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.IsNotNull(error);
        Assert.AreEqual("private_network_ambiguous", error.Code);
        Assert.IsTrue(error.Message.Length <= 120);
        Assert.IsNull(daemon.AdmissionAddress);
        Assert.IsNull(daemon.MessageAddress);
        Assert.IsFalse(responseJson.Contains("package", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(responseJson.Contains("endpoint", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Browser_join_accepts_the_existing_signed_invitation_over_authenticated_transport()
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive(
                ".NET 10 supports TLS 1.3 on macOS clients, but not macOS SslStream servers.");
            return;
        }

        using var ownerDirectory = new TemporaryDirectory();
        using var memberDirectory = new TemporaryDirectory();
        var privateAddress = FindOperationalPrivateAddress();
        if (privateAddress is null)
        {
            Assert.Inconclusive("Browser admission requires an operational private IPv4 interface.");
            return;
        }

        var admissionEndpoint = AllocatePrivateEndpoint(privateAddress);
        var messageEndpoint = AllocatePrivateEndpoint(privateAddress);
        await using var owner = await StartDaemonAsync(
            ownerDirectory.Path,
            admissionEndpoint,
            messageEndpoint);
        await using var member = await StartDaemonAsync(memberDirectory.Path);
        using var ownerClient = CreateIpcClient(GetEndpoint(ownerDirectory.Path));
        using var memberClient = CreateIpcClient(GetEndpoint(memberDirectory.Path));
        using var createCircleResponse = await ownerClient.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(
                "0198d000-6000-7000-8000-000000000002",
                "Joined Circle",
                "Alice"),
            ControlJson.Options);
        var circle = await createCircleResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
            ControlJson.Options);
        Assert.IsNotNull(circle);
        using var issueResponse = await ownerClient.PostAsJsonAsync(
            ControlRoutes.CircleInvitations(circle.Circle.Id),
            new CreateInvitationRequest(60),
            ControlJson.Options);
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
            new JoinBrowserCircleRequest(
                invitation.Package,
                LanTcpEndpoint.ProviderName,
                admissionEndpoint,
                messageEndpoint,
                "Bob"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);
        using var response = await browserClient.SendAsync(join);
        var joined = await response.Content.ReadFromJsonAsync<CircleDetailsResponse>(ControlJson.Options);

        using var viewerRequest = new HttpRequestMessage(
            HttpMethod.Get,
            BrowserRoutes.CircleViewer(circle.Circle.Id));
        viewerRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var viewerResponse = await browserClient.SendAsync(viewerRequest);
        var viewer = await viewerResponse.Content.ReadFromJsonAsync<BrowserCircleViewerResponse>(
            ControlJson.Options);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(joined);
        Assert.AreEqual(circle.Circle.Id, joined.Circle.Id);
        Assert.HasCount(2, joined.Members);
        Assert.IsTrue(joined.Members.Any(person => person.DisplayName == "Bob"));
        Assert.AreEqual(HttpStatusCode.OK, viewerResponse.StatusCode);
        Assert.IsNotNull(viewer);
        Assert.AreEqual("member", viewer.Role);
        Assert.AreEqual(
            joined.Members.Single(person => person.DisplayName == "Bob").Id,
            viewer.MemberId);
    }

    [TestMethod]
    public async Task Browser_request_body_is_bounded_before_capability_processing()
    {
        using var directory = new TemporaryDirectory();
        await using var daemon = await StartDaemonAsync(directory.Path);
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        using var request = new HttpRequestMessage(HttpMethod.Post, BrowserRoutes.Session)
        {
            Content = new StringContent(
                $"{{\"capability\":\"{new string('a', 33 * 1024)}\"}}",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Origin", GetOrigin(browserBaseUri));

        using var response = await browserClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [TestMethod]
    public async Task Browser_circle_journey_preserves_identifiers_after_daemon_restart()
    {
        using var directory = new TemporaryDirectory();
        var stateDirectory = Path.Combine(directory.Path, "state");
        var endpoint = GetEndpoint(directory.Path);
        string circleId;
        string nodeId;

        await using (var daemon = await DaemonHost.StartAsync(
                         new DaemonOptions(stateDirectory, endpoint, "Browser-PC")))
        using (var ipcClient = CreateIpcClient(endpoint))
        {
            var launch = await IssueLaunchAsync(ipcClient);
            var browserBaseUri = GetBrowserBaseUri(launch);
            using var browserClient = CreateBrowserClient(browserBaseUri);
            var authenticated = await ExchangeAsync(browserClient, launch);
            using var create = CreateJsonRequest(
                HttpMethod.Post,
                BrowserRoutes.Circles,
                new CreateCircleRequest(
                    "0198c2d8-b000-7000-8000-000000000502",
                    "Restart Circle",
                    "Alice"),
                GetOrigin(browserBaseUri),
                authenticated.Cookie,
                authenticated.Session.AntiforgeryToken);
            using var createResponse = await browserClient.SendAsync(create);
            var created = await createResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
                ControlJson.Options);
            Assert.IsNotNull(created);
            circleId = created.Circle.Id;
            nodeId = created.Nodes.Single().Id;
        }

        await using (var daemon = await DaemonHost.StartAsync(
                         new DaemonOptions(stateDirectory, endpoint, "Renamed-PC")))
        using (var ipcClient = CreateIpcClient(endpoint))
        {
            var launch = await IssueLaunchAsync(ipcClient);
            var browserBaseUri = GetBrowserBaseUri(launch);
            using var browserClient = CreateBrowserClient(browserBaseUri);
            var authenticated = await ExchangeAsync(browserClient, launch);
            using var detailsRequest = new HttpRequestMessage(
                HttpMethod.Get,
                BrowserRoutes.Circle(circleId));
            detailsRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
            using var detailsResponse = await browserClient.SendAsync(detailsRequest);
            var details = await detailsResponse.Content.ReadFromJsonAsync<CircleDetailsResponse>(
                ControlJson.Options);

            Assert.AreEqual(HttpStatusCode.OK, detailsResponse.StatusCode);
            Assert.IsNotNull(details);
            Assert.AreEqual(circleId, details.Circle.Id);
            Assert.AreEqual(nodeId, details.Nodes.Single().Id);
            Assert.AreEqual("Browser-PC", details.Nodes.Single().DisplayName);
        }
    }

    private static async Task<DaemonInstance> StartDaemonAsync(
        string root,
        string? admissionListenEndpoint = null,
        string? messageListenEndpoint = null,
        bool automaticPrivateListeners = false,
        Func<PrivateIPv4AddressSelection>? privateAddressSelector = null,
        string? advertisedPrivateAddress = null)
    {
        var options = new DaemonOptions(
            Path.Combine(root, "state"),
            GetEndpoint(root),
            "Browser-PC",
            admissionListenEndpoint,
            messageListenEndpoint,
            automaticPrivateListeners,
            advertisedPrivateAddress);
        if (privateAddressSelector is null)
        {
            return await DaemonHost.StartAsync(options);
        }

        var host = (SupportedHostPlatform)HostPlatformSelector.SelectCurrent();
        return await DaemonHost.StartAsync(
            options,
            host.Platform,
            host.PrivateMaterialProtector,
            privateAddressSelector: privateAddressSelector);
    }

    private static HttpClient CreateIpcClient(string endpoint)
    {
        var selection = HostPlatformSelector.SelectCurrent();
        var host = (SupportedHostPlatform)selection;
        return host.Platform.LocalControlClient.CreateClient(
            endpoint);
    }

    private static async Task<LaunchBrowserResponse> IssueLaunchAsync(HttpClient ipcClient)
    {
        using var response = await ipcClient.PostAsync(ControlRoutes.BrowserLaunch, null);
        var launch = await response.Content.ReadFromJsonAsync<LaunchBrowserResponse>(
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return launch ?? throw new AssertFailedException("Browser launch response was empty.");
    }

    private static HttpClient CreateBrowserClient(Uri browserBaseUri)
    {
        return new HttpClient(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
            })
        {
            BaseAddress = browserBaseUri,
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    private static async Task<AuthenticatedBrowser> ExchangeAsync(
        HttpClient browserClient,
        LaunchBrowserResponse launch)
    {
        var baseUri = GetBrowserBaseUri(launch);
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            BrowserRoutes.Session,
            new ExchangeBrowserSessionRequest(ReadCapability(launch.Url)),
            GetOrigin(baseUri));
        using var response = await browserClient.SendAsync(request);
        var session = await response.Content.ReadFromJsonAsync<BrowserSessionResponse>(
            ControlJson.Options);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(session);
        var cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        return new AuthenticatedBrowser(cookie, session);
    }

    private static HttpRequestMessage CreateJsonRequest<T>(
        HttpMethod method,
        string path,
        T body,
        string origin,
        string? cookie = null,
        string? antiforgeryToken = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: ControlJson.Options),
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
        if (antiforgeryToken is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Balls-Antiforgery", antiforgeryToken);
        }

        return request;
    }

    private static Uri GetBrowserBaseUri(LaunchBrowserResponse launch)
    {
        var launchUri = new Uri(launch.Url, UriKind.Absolute);
        return new Uri(launchUri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
    }

    private static PrivateIPv4AddressSelection? SelectBindablePrivateAddress()
    {
        var bindAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(LanTcpEndpoint.IsPrivateIPv4)
            .OrderBy(address => address.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();
        return bindAddress is null
            ? null
            : new PrivateIPv4AddressSelection(
                bindAddress,
                null,
                "One private network connection is ready.");
    }

    private static string ReadCapability(string launchUrl)
    {
        var fragment = new Uri(launchUrl, UriKind.Absolute).Fragment;
        const string prefix = "#launch=";
        Assert.IsTrue(fragment.StartsWith(prefix, StringComparison.Ordinal));
        return Uri.UnescapeDataString(fragment[prefix.Length..]);
    }

    private static string GetOrigin(Uri browserBaseUri)
    {
        return browserBaseUri.GetLeftPart(UriPartial.Authority);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        var contentSecurityPolicy = response.Headers
            .GetValues("Content-Security-Policy")
            .Single();
        StringAssert.Contains(contentSecurityPolicy, "default-src 'self'");
        StringAssert.Contains(contentSecurityPolicy, "script-src 'self'");
        StringAssert.Contains(contentSecurityPolicy, "object-src 'none'");
        StringAssert.Contains(contentSecurityPolicy, "base-uri 'none'");
        StringAssert.Contains(contentSecurityPolicy, "frame-ancestors 'none'");
        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    private static string GetEndpoint(string root)
    {
        return OperatingSystem.IsWindows()
            ? $"balls-browser-{Path.GetFileName(root)}"
            : Path.Combine(root, "runtime", "control.sock");
    }

    [GeneratedRegex("(?:src|href)=\"([^\"]*/assets/[^\"]+)\"")]
    private static partial Regex AssetPath();

    private sealed record AuthenticatedBrowser(string Cookie, BrowserSessionResponse Session);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset value = utcNow;

        public override DateTimeOffset GetUtcNow() => value;

        internal void Advance(TimeSpan duration) => value += duration;
    }

    private sealed class StubFolderPicker(string folderPath, string displayName)
        : ICircleFilesFolderPicker
    {
        public ValueTask<CircleFilesFolderSelection?> SelectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<CircleFilesFolderSelection?>(
                new CircleFilesFolderSelection(folderPath, displayName));
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
            return ValueTask.FromResult(new CircleFilesHostApplyResult(
                Requests.Count == 2
                    ? CircleFilesHostApplyStatus.Applied
                    : CircleFilesHostApplyStatus.AlreadyApplied,
                plan));
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
                true,
                ["Preserve existing files and create exact owned resources."]);
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
                    OperatingSystem.IsLinux()
                        ? System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ".local",
                            "state")
                        : System.IO.Path.GetTempPath(),
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
