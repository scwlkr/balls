using Balls.Daemon;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class BrowserAccessBrokerTests
{
    private static readonly Uri BrowserBaseUri = new("http://127.0.0.1:43123/");

    [TestMethod]
    public void Launch_capability_is_short_lived_single_use_and_stays_out_of_the_query()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var broker = new BrowserAccessBroker(
            time,
            launchLifetime: TimeSpan.FromSeconds(30),
            sessionLifetime: TimeSpan.FromMinutes(15));

        var launch = broker.IssueLaunch(BrowserBaseUri);

        Assert.AreEqual(string.Empty, launch.Url.Query);
        StringAssert.StartsWith(launch.Url.Fragment, "#launch=");
        Assert.AreEqual(time.GetUtcNow().AddSeconds(30), launch.ExpiresAtUtc);
        Assert.IsNotNull(broker.ExchangeLaunchCapability(launch.Capability));
        Assert.IsNull(broker.ExchangeLaunchCapability(launch.Capability));
    }

    [TestMethod]
    public void Expired_launch_capability_cannot_create_a_session()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var broker = new BrowserAccessBroker(
            time,
            launchLifetime: TimeSpan.FromSeconds(30),
            sessionLifetime: TimeSpan.FromMinutes(15));
        var launch = broker.IssueLaunch(BrowserBaseUri);

        time.Advance(TimeSpan.FromSeconds(31));

        Assert.IsNull(broker.ExchangeLaunchCapability(launch.Capability));
    }

    [TestMethod]
    public void State_change_requires_the_session_and_its_matching_antiforgery_token()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var broker = new BrowserAccessBroker(
            time,
            launchLifetime: TimeSpan.FromSeconds(30),
            sessionLifetime: TimeSpan.FromMinutes(15));
        var launch = broker.IssueLaunch(BrowserBaseUri);
        var session = broker.ExchangeLaunchCapability(launch.Capability);

        Assert.IsNotNull(session);
        Assert.IsTrue(broker.IsSessionAuthorized(session.SessionToken));
        Assert.IsTrue(
            broker.IsStateChangeAuthorized(session.SessionToken, session.AntiforgeryToken));
        Assert.IsFalse(broker.IsStateChangeAuthorized(session.SessionToken, "wrong-token"));
        Assert.IsFalse(broker.IsStateChangeAuthorized("wrong-session", session.AntiforgeryToken));
    }

    [TestMethod]
    public void Expired_session_loses_read_and_state_change_authority()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var broker = new BrowserAccessBroker(
            time,
            launchLifetime: TimeSpan.FromSeconds(30),
            sessionLifetime: TimeSpan.FromMinutes(15));
        var launch = broker.IssueLaunch(BrowserBaseUri);
        var session = broker.ExchangeLaunchCapability(launch.Capability);
        Assert.IsNotNull(session);

        time.Advance(TimeSpan.FromMinutes(16));

        Assert.IsFalse(broker.IsSessionAuthorized(session.SessionToken));
        Assert.IsFalse(
            broker.IsStateChangeAuthorized(session.SessionToken, session.AntiforgeryToken));
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
        }
    }
}
