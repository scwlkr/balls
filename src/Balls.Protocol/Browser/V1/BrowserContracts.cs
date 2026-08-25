namespace Balls.Protocol.Browser.V1;

public static class BrowserRoutes
{
    public const string BasePath = "/browser/v1";
    public const string Session = BasePath + "/session";
    public const string Status = BasePath + "/status";
    public const string Circles = BasePath + "/circles";
    public const string CircleJoin = Circles + "/join";

    public static string Circle(string circleId)
    {
        return $"{Circles}/{Uri.EscapeDataString(circleId)}";
    }

    public static string CircleMessages(string circleId)
    {
        return $"{Circle(circleId)}/messages";
    }

    public static string CircleViewer(string circleId)
    {
        return $"{Circle(circleId)}/viewer";
    }

    public static string CircleInvitations(string circleId)
    {
        return $"{Circle(circleId)}/invitations";
    }

    public static string CircleFilesContributions(string circleId)
    {
        return $"{Circle(circleId)}/files/contributions";
    }

    public static string CircleFilesSync(string circleId)
    {
        return $"{Circle(circleId)}/files/sync";
    }

    public static string CircleFilesAccessGrants(string circleId, string contributionId)
    {
        return $"{CircleFilesContributions(circleId)}/{Uri.EscapeDataString(contributionId)}/grants";
    }

    public static string CircleFilesMemberMapping(
        string circleId,
        string contributionId,
        string grantId)
    {
        return $"{CircleFilesAccessGrants(circleId, contributionId)}/{Uri.EscapeDataString(grantId)}/mapping";
    }
}

public sealed record LaunchBrowserResponse(string Url, DateTimeOffset ExpiresAtUtc);

public sealed record ExchangeBrowserSessionRequest(string Capability);

public sealed record BrowserSessionResponse(
    string AntiforgeryToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record CreateBrowserCircleInvitationRequest(
    int ValidForMinutes,
    string? HostAddress);

public sealed record BrowserCircleInvitationResponse(
    string CircleId,
    string InvitationId,
    DateTimeOffset ExpiresAtUtc,
    string Package,
    string Endpoint,
    string SyncEndpoint);

public sealed record BrowserCircleViewerResponse(string MemberId, string Role);

public sealed record SyncBrowserCircleFilesRequest(string Endpoint);

public sealed record BrowserCircleFilesSyncResponse(string CircleId, int ImportedGrantCount);
