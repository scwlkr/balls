using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using Balls.Daemon;
using Balls.Host;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

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

        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, BrowserRoutes.Status);
        statusRequest.Headers.TryAddWithoutValidation("Cookie", authenticated.Cookie);
        using var statusResponse = await browserClient.SendAsync(statusRequest);

        Assert.AreEqual(HttpStatusCode.BadRequest, hostileHostResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, ambiguousHostResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, missingAntiforgeryResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.IsNotNull(created);
        Assert.AreEqual("Secure Circle", created.Circle.Name);
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);
    }

    [TestMethod]
    public async Task Browser_projection_exposes_no_Circle_Files_mutation_route()
    {
        using var directory = new TemporaryDirectory();
        await using var daemon = await StartDaemonAsync(directory.Path);
        using var ipcClient = CreateIpcClient(GetEndpoint(directory.Path));
        var launch = await IssueLaunchAsync(ipcClient);
        var browserBaseUri = GetBrowserBaseUri(launch);
        using var browserClient = CreateBrowserClient(browserBaseUri);
        var authenticated = await ExchangeAsync(browserClient, launch);
        var path = BrowserRoutes.Circles
            + "/0198d000-5000-7000-8000-000000000001/files/contributions";
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            path,
            new CreateCircleFilesContributionRequest(
                "0198d000-5000-7000-8000-000000000002",
                "Must Not Be Created"),
            GetOrigin(browserBaseUri),
            authenticated.Cookie,
            authenticated.Session.AntiforgeryToken);

        using var response = await browserClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
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

    private static async Task<DaemonInstance> StartDaemonAsync(string root)
    {
        return await DaemonHost.StartAsync(
            new DaemonOptions(
                Path.Combine(root, "state"),
                GetEndpoint(root),
                "Browser-PC"));
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
