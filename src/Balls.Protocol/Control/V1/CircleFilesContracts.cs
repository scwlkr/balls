namespace Balls.Protocol.Control.V1;

public sealed record CircleFilesReadinessCheckResponse(
    string Id,
    string Status,
    string Code,
    string Summary);

public sealed record CircleFilesReadinessResponse(
    string Provider,
    string Status,
    IReadOnlyList<CircleFilesReadinessCheckResponse> Checks);

public sealed record CreateCircleFilesContributionRequest(
    string RequestId,
    string DisplayName);

public sealed record CircleFilesProviderResponse(
    string Id,
    string NodeId);

public sealed record CircleFilesContributionResponse(
    string Id,
    string CircleId,
    CircleFilesProviderResponse Provider,
    string DisplayName,
    string Lifecycle,
    long Generation,
    DateTimeOffset CreatedAtUtc,
    string AuthorizedByMemberId,
    long AuthorityGeneration,
    DateTimeOffset AuthorizedAtUtc);

public sealed record CircleFilesContributionListResponse(
    string CircleId,
    IReadOnlyList<CircleFilesContributionResponse> Contributions);

public sealed record PreviewCircleFilesHostRequest(string FolderPath);

public sealed record ApplyCircleFilesHostRequest(string FolderPath, string PlanId);

public sealed record CircleFilesHostPlanResponse(
    int ContractVersion,
    string PlanId,
    string Provider,
    string FolderPath,
    string ShareName,
    string FirewallRuleName,
    string OwnershipId,
    bool TargetExists,
    IReadOnlyList<string> Actions);

public sealed record CircleFilesHostApplyResponse(
    string Status,
    CircleFilesHostPlanResponse Plan);

public sealed record PreviewCircleFilesGrantCredentialRequest(string FolderPath);

public sealed record ApplyCircleFilesGrantCredentialRequest(string FolderPath, string PlanId);

public sealed record CircleFilesGrantCredentialPlanResponse(
    int ContractVersion,
    string PlanId,
    string Provider,
    string FolderPath,
    string ShareName,
    string AccountName,
    string OwnershipId,
    string Access,
    long Generation,
    IReadOnlyList<string> Actions);

public sealed record CircleFilesGrantCredentialApplyResponse(
    string Status,
    CircleFilesGrantCredentialPlanResponse Plan);

public sealed record PreviewCircleFilesMemberMappingRequest(string Endpoint, string DriveLetter);

public sealed record ApplyCircleFilesMemberMappingRequest(
    string Endpoint,
    string DriveLetter,
    string PlanId);

public sealed record InspectCircleFilesMemberMappingRequest(string Endpoint, string DriveLetter);

public sealed record UnmapCircleFilesMemberMappingRequest(string Endpoint, string DriveLetter);

public sealed record CircleFilesMemberMappingPlanResponse(
    int ContractVersion,
    string PlanId,
    string Endpoint,
    string UncPath,
    string CredentialTarget,
    string DriveLetter,
    string FriendlyName,
    string OwnershipId,
    IReadOnlyList<string> AvailableDriveLetters,
    IReadOnlyList<string> Actions);

public sealed record CircleFilesMemberMappingInspectionResponse(
    string Status,
    CircleFilesMemberMappingPlanResponse Plan);

public sealed record CircleFilesMemberMappingResultResponse(
    string Status,
    CircleFilesMemberMappingPlanResponse Plan);

public sealed record CreateMemberAccessGrantRequest(
    string RequestId,
    string MemberId,
    string Access);

public sealed record MemberAccessGrantResponse(
    string Id,
    string CircleId,
    string ContributionId,
    string MemberId,
    string Access,
    string Lifecycle,
    long Generation,
    DateTimeOffset CreatedAtUtc,
    string AuthorizedByMemberId,
    long AuthorityGeneration,
    DateTimeOffset AuthorizedAtUtc);

public sealed record MemberAccessGrantListResponse(
    string CircleId,
    string ContributionId,
    IReadOnlyList<MemberAccessGrantResponse> Grants);
