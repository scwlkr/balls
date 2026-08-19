using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Balls.Daemon;

internal sealed class BrowserAccessBroker
{
    private const int MaximumOutstandingLaunches = 32;
    private const int MaximumSessions = 64;
    private readonly ConcurrentDictionary<string, DateTimeOffset> launches = new();
    private readonly ConcurrentDictionary<string, SessionRecord> sessions = new();
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan launchLifetime;
    private readonly TimeSpan sessionLifetime;

    public BrowserAccessBroker(
        TimeProvider timeProvider,
        TimeSpan launchLifetime,
        TimeSpan sessionLifetime)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (launchLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(launchLifetime));
        }
        if (sessionLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionLifetime));
        }

        this.timeProvider = timeProvider;
        this.launchLifetime = launchLifetime;
        this.sessionLifetime = sessionLifetime;
    }

    public BrowserLaunch IssueLaunch(Uri browserBaseUri)
    {
        ArgumentNullException.ThrowIfNull(browserBaseUri);
        var now = timeProvider.GetUtcNow();
        Prune(now);
        TrimOldest(launches, MaximumOutstandingLaunches - 1, pair => pair.Value);

        var capability = CreateToken();
        var expiresAtUtc = now.Add(launchLifetime);
        launches[capability] = expiresAtUtc;
        var builder = new UriBuilder(browserBaseUri)
        {
            Fragment = $"launch={Uri.EscapeDataString(capability)}",
            Query = string.Empty,
        };
        return new BrowserLaunch(builder.Uri, capability, expiresAtUtc);
    }

    public BrowserSession? ExchangeLaunchCapability(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability)
            || !launches.TryRemove(capability, out var expiresAtUtc))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (expiresAtUtc <= now)
        {
            return null;
        }

        Prune(now);
        TrimOldest(sessions, MaximumSessions - 1, pair => pair.Value.ExpiresAtUtc);
        var sessionToken = CreateToken();
        var antiforgeryToken = CreateToken();
        var sessionExpiresAtUtc = now.Add(sessionLifetime);
        sessions[sessionToken] = new SessionRecord(antiforgeryToken, sessionExpiresAtUtc);
        return new BrowserSession(sessionToken, antiforgeryToken, sessionExpiresAtUtc);
    }

    public bool IsSessionAuthorized(string? sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)
            || !sessions.TryGetValue(sessionToken, out var session))
        {
            return false;
        }

        if (session.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            sessions.TryRemove(sessionToken, out _);
            return false;
        }

        return true;
    }

    public bool IsStateChangeAuthorized(string? sessionToken, string? antiforgeryToken)
    {
        if (!IsSessionAuthorized(sessionToken)
            || antiforgeryToken is null
            || !sessions.TryGetValue(sessionToken!, out var session))
        {
            return false;
        }

        return FixedTimeEquals(session.AntiforgeryToken, antiforgeryToken);
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var launch in launches.Where(pair => pair.Value <= now))
        {
            launches.TryRemove(launch.Key, out _);
        }

        foreach (var session in sessions.Where(pair => pair.Value.ExpiresAtUtc <= now))
        {
            sessions.TryRemove(session.Key, out _);
        }
    }

    private static void TrimOldest<TValue>(
        ConcurrentDictionary<string, TValue> values,
        int maximumCount,
        Func<KeyValuePair<string, TValue>, DateTimeOffset> getExpiry)
    {
        foreach (var value in values
                     .OrderBy(getExpiry)
                     .Take(Math.Max(0, values.Count - maximumCount)))
        {
            values.TryRemove(value.Key, out _);
        }
    }

    private sealed record SessionRecord(string AntiforgeryToken, DateTimeOffset ExpiresAtUtc);
}

internal sealed record BrowserLaunch(Uri Url, string Capability, DateTimeOffset ExpiresAtUtc);

internal sealed record BrowserSession(
    string SessionToken,
    string AntiforgeryToken,
    DateTimeOffset ExpiresAtUtc);
