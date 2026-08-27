namespace Balls.Protocol.Browser.V1;

public static class BrowserRoutes
{
    public const string BasePath = "/browser/v1";
    public const string Session = BasePath + "/session";
    public const string Status = BasePath + "/status";
    public const string Circles = BasePath + "/circles";
    public const string CircleJoin = Circles + "/join";
    public const string RevitServerMediaSelection = BasePath + "/revit-server/setup/media-selection";
    public const string RevitServerSetupInspection = BasePath + "/revit-server/setup/inspection";
    public const string RevitServerSetupStatus = BasePath + "/revit-server/setup/status";
    public const string RevitServerSetupBegin = BasePath + "/revit-server/setup/begin";
    public const string RevitServerSetupVerify = BasePath + "/revit-server/setup/verify";
    public const string RevitServerSetupRetry = BasePath + "/revit-server/setup/retry";

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

    public static string CircleFilesFolderSelection(string circleId)
    {
        return $"{CircleFilesContributions(circleId)}/folder-selection";
    }

    public static string CircleFilesFolderApply(string circleId)
    {
        return $"{CircleFilesContributions(circleId)}/folder-apply";
    }

    public static string CircleFilesSync(string circleId)
    {
        return $"{Circle(circleId)}/files/sync";
    }

    public static string CircleFilesOpen(string circleId)
    {
        return $"{Circle(circleId)}/files/open";
    }

    public static string CircleFilesGrantPreview(string circleId)
    {
        return $"{Circle(circleId)}/files/grant/preview";
    }

    public static string CircleFilesGrantApply(string circleId)
    {
        return $"{Circle(circleId)}/files/grant/apply";
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

public sealed record CreateBrowserCircleInvitationRequest(int ValidForMinutes);

public sealed record BrowserCircleInvitationResponse(
    string CircleId,
    string InvitationId,
    DateTimeOffset ExpiresAtUtc,
    string Package,
    string Provider,
    string Endpoint,
    string SyncEndpoint);

public sealed record JoinBrowserCircleRequest(
    string Package,
    string Provider,
    string AdmissionEndpoint,
    string SyncEndpoint,
    string MemberDisplayName);

public sealed record BrowserCircleViewerResponse(string MemberId, string Role);

public sealed record BrowserCircleFilesSyncResponse(string CircleId, int ImportedGrantCount);

public sealed record BrowserCircleFilesOpenResponse(
    string Status,
    string FolderName,
    string Message);

public sealed record BrowserCircleFilesFolderSelectionResponse(
    string Status,
    string? SelectionId,
    string? FolderPath,
    string? DisplayName);

public sealed record ApplyBrowserCircleFilesFolderRequest(
    string RequestId,
    string SelectionId);

public sealed record BrowserCircleFilesContributionResponse(
    string Status,
    string ContributionId,
    string DisplayName,
    string FolderPath);

public sealed record PreviewBrowserCircleFilesGrantRequest(
    string FolderName,
    string MemberName,
    string Access);

public sealed record BrowserCircleFilesGrantPreviewResponse(
    string FolderName,
    string FolderPath,
    string MemberName,
    string Access,
    string Summary);

public sealed record BrowserCircleFilesGrantApplyResponse(
    string Status,
    string FolderName,
    string MemberName,
    string Access,
    string Message);

public sealed record BrowserRevitServerMediaSelectionResponse(
    string Status,
    string? SelectionId,
    string? FileName);

public sealed record InspectBrowserRevitServerSetupRequest(string SelectionId);
