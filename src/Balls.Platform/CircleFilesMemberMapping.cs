namespace Balls.Platform;

public static class CircleFilesMemberMappingContract
{
    public const int Version = 1;
}

public sealed record CircleFilesMemberMappingRequest(
    string CircleId,
    string ContributionId,
    string ProviderId,
    string GrantId,
    string MemberId,
    string AccountName,
    string GrantOwnershipId,
    string Access,
    long Generation,
    string CircleName,
    string Endpoint,
    string DriveLetter);

public sealed record CircleFilesMemberMappingPlan(
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

public sealed record CircleFilesMemberMappingInspection(
    string Status,
    CircleFilesMemberMappingPlan Plan);

public sealed record CircleFilesMemberMappingResult(
    string Status,
    CircleFilesMemberMappingPlan Plan);

public interface ICircleFilesMemberMapper
{
    ValueTask<CircleFilesMemberMappingPlan> PreviewAsync(
        CircleFilesMemberMappingRequest request,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesMemberMappingInspection> InspectAsync(
        CircleFilesMemberMappingRequest request,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesMemberMappingResult> MapAsync(
        CircleFilesMemberMappingRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesMemberMappingResult> UnmapAsync(
        CircleFilesMemberMappingRequest request,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken);
}

public sealed class UnsupportedCircleFilesMemberMapper : ICircleFilesMemberMapper
{
    public ValueTask<CircleFilesMemberMappingPlan> PreviewAsync(
        CircleFilesMemberMappingRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesMemberMappingPlan>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesMemberMappingInspection> InspectAsync(
        CircleFilesMemberMappingRequest request,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesMemberMappingInspection>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesMemberMappingResult> MapAsync(
        CircleFilesMemberMappingRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesMemberMappingResult>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesMemberMappingResult> UnmapAsync(
        CircleFilesMemberMappingRequest request,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesMemberMappingResult>(Unsupported(cancellationToken));

    private static Exception Unsupported(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new CircleFilesHostingException(
            "windows_required",
            "Circle Files Explorer mapping is available only on Windows.");
    }
}
