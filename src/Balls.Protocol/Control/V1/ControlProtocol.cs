namespace Balls.Protocol.Control.V1;

public static class ControlProtocol
{
    public const int Version = 1;
}

public static class ControlRoutes
{
    public const string BasePath = "/control/v1";
    public const string Status = BasePath + "/status";
    public const string Circles = BasePath + "/circles";
    public const string CircleJoin = Circles + "/join";
    public const string BrowserLaunch = BasePath + "/ui/launch";
    public const string Invitations = BasePath + "/invitations";
    public const string CircleFilesReadiness = BasePath + "/files/readiness";
    public const string OpenApi = BasePath + "/openapi.json";

    public static string Circle(string circleId)
    {
        return $"{Circles}/{Uri.EscapeDataString(circleId)}";
    }

    public static string CircleMembers(string circleId)
    {
        return $"{Circle(circleId)}/members";
    }

    public static string CircleNodes(string circleId)
    {
        return $"{Circle(circleId)}/nodes";
    }

    public static string CircleInvitations(string circleId)
    {
        return $"{Circle(circleId)}/invitations";
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

    public static string CircleFilesHost(string circleId, string contributionId)
    {
        return $"{CircleFilesContributions(circleId)}/{Uri.EscapeDataString(contributionId)}/host";
    }

    public static string CircleFilesHostPreview(string circleId, string contributionId)
    {
        return CircleFilesHost(circleId, contributionId) + "/preview";
    }

    public static string CircleFilesHostApply(string circleId, string contributionId)
    {
        return CircleFilesHost(circleId, contributionId) + "/apply";
    }
}
