namespace Balls.Protocol.Browser.V1;

public static class BrowserRoutes
{
    public const string BasePath = "/browser/v1";
    public const string Session = BasePath + "/session";
    public const string Status = BasePath + "/status";
    public const string Circles = BasePath + "/circles";

    public static string Circle(string circleId)
    {
        return $"{Circles}/{Uri.EscapeDataString(circleId)}";
    }

    public static string CircleMessages(string circleId)
    {
        return $"{Circle(circleId)}/messages";
    }

    public static string CircleFilesContributions(string circleId)
    {
        return $"{Circle(circleId)}/files/contributions";
    }

    public static string CircleFilesAccessGrants(string circleId, string contributionId)
    {
        return $"{CircleFilesContributions(circleId)}/{Uri.EscapeDataString(contributionId)}/grants";
    }
}

public sealed record LaunchBrowserResponse(string Url, DateTimeOffset ExpiresAtUtc);

public sealed record ExchangeBrowserSessionRequest(string Capability);

public sealed record BrowserSessionResponse(
    string AntiforgeryToken,
    DateTimeOffset ExpiresAtUtc);
