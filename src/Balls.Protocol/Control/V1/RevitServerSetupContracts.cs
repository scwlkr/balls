namespace Balls.Protocol.Control.V1;

public sealed record InspectRevitServerSetupRequest(string MediaPath);

public sealed record RevitServerReadinessCheckResponse(
    string Id,
    string Status,
    string Code,
    string Summary);

public sealed record RevitServerSetupPlanResponse(
    string PlanDigest,
    string Machine,
    string Windows,
    string Media,
    string MediaSha256,
    IReadOnlyList<string> EnabledRoles,
    IReadOnlyList<string> ForbiddenRoles,
    IReadOnlyList<string> DataPaths,
    IReadOnlyList<string> WindowsPrerequisites,
    IReadOnlyList<string> AclIntent,
    IReadOnlyList<string> DefaultWebSiteEffects,
    IReadOnlyList<string> RsnIni,
    IReadOnlyList<string> FirewallEffects,
    IReadOnlyList<string> VerificationActions,
    IReadOnlyList<string> BallsOwnedState,
    IReadOnlyList<string> AutodeskOwnedState);

public sealed record RevitServerSetupInspectionResponse(
    string Status,
    string Summary,
    IReadOnlyList<RevitServerReadinessCheckResponse> Checks,
    RevitServerSetupPlanResponse? Plan);

public sealed record BeginRevitServerSetupRequest(
    string SelectionId,
    string PlanDigest,
    bool Consent);

public sealed record RevitServerSetupStatusResponse(
    string Stage,
    string Summary,
    string? AttemptId,
    RevitServerSetupPlanResponse? Plan,
    IReadOnlyList<RevitServerReadinessCheckResponse> Checks);
