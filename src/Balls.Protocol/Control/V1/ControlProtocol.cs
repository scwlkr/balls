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
    public const string RevitServerSetupInspection = BasePath + "/revit-server/setup/inspection";
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

    public static string CircleFilesGrantCredential(
        string circleId,
        string contributionId,
        string grantId)
    {
        return $"{CircleFilesAccessGrants(circleId, contributionId)}/{Uri.EscapeDataString(grantId)}/credential";
    }

    public static string CircleFilesGrantCredentialPreview(
        string circleId,
        string contributionId,
        string grantId) =>
        CircleFilesGrantCredential(circleId, contributionId, grantId) + "/preview";

    public static string CircleFilesGrantCredentialApply(
        string circleId,
        string contributionId,
        string grantId) =>
        CircleFilesGrantCredential(circleId, contributionId, grantId) + "/apply";

    public static string CircleFilesGrantRevoke(
        string circleId,
        string contributionId,
        string grantId) =>
        $"{CircleFilesAccessGrants(circleId, contributionId)}/{Uri.EscapeDataString(grantId)}/revoke";

    public static string CircleFilesGrantCleanup(
        string circleId,
        string contributionId,
        string grantId) =>
        $"{CircleFilesAccessGrants(circleId, contributionId)}/{Uri.EscapeDataString(grantId)}/cleanup";

    public static string CircleFilesGrantCleanupPreview(
        string circleId,
        string contributionId,
        string grantId) =>
        CircleFilesGrantCleanup(circleId, contributionId, grantId) + "/preview";

    public static string CircleFilesGrantCleanupApply(
        string circleId,
        string contributionId,
        string grantId) =>
        CircleFilesGrantCleanup(circleId, contributionId, grantId) + "/apply";

    public static string CircleFilesHostRemovalPreview(string circleId, string contributionId) =>
        CircleFilesHost(circleId, contributionId) + "/remove/preview";

    public static string CircleFilesHostRemovalApply(string circleId, string contributionId) =>
        CircleFilesHost(circleId, contributionId) + "/remove/apply";

    public static string CircleFilesMemberMapping(
        string circleId,
        string contributionId,
        string grantId) =>
        $"{CircleFilesAccessGrants(circleId, contributionId)}/{Uri.EscapeDataString(grantId)}/mapping";

    public static string CircleFilesMemberMappingPreview(
        string circleId, string contributionId, string grantId) =>
        CircleFilesMemberMapping(circleId, contributionId, grantId) + "/preview";

    public static string CircleFilesMemberMappingMap(
        string circleId, string contributionId, string grantId) =>
        CircleFilesMemberMapping(circleId, contributionId, grantId) + "/map";

    public static string CircleFilesMemberMappingInspect(
        string circleId, string contributionId, string grantId) =>
        CircleFilesMemberMapping(circleId, contributionId, grantId) + "/inspect";

    public static string CircleFilesMemberMappingUnmap(
        string circleId, string contributionId, string grantId) =>
        CircleFilesMemberMapping(circleId, contributionId, grantId) + "/unmap";
}
