using System.Text.Json;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class BrowserContractTests
{
    [TestMethod]
    public void Ipc_launch_response_exposes_only_the_fragment_url_and_expiry()
    {
        var response = new LaunchBrowserResponse(
            "http://127.0.0.1:43123/#launch=one-time",
            new DateTimeOffset(2026, 8, 19, 12, 0, 30, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(response, ControlJson.Options);

        Assert.AreEqual(
            "{\"url\":\"http://127.0.0.1:43123/#launch=one-time\",\"expiresAtUtc\":\"2026-08-19T12:00:30+00:00\"}",
            json);
        Assert.IsFalse(json.Contains("capability", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Browser_session_response_returns_antiforgery_but_not_the_http_only_session()
    {
        var response = new BrowserSessionResponse(
            "anti-forgery",
            new DateTimeOffset(2026, 8, 19, 12, 15, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(response, ControlJson.Options);

        Assert.AreEqual(
            "{\"antiforgeryToken\":\"anti-forgery\",\"expiresAtUtc\":\"2026-08-19T12:15:00+00:00\"}",
            json);
        Assert.IsFalse(json.Contains("session", StringComparison.OrdinalIgnoreCase));
    }
}
