using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class CircleFilesLifecycleEndpoints
{
    internal static async Task<IResult> RevokeGrantAsync(
        CircleFilesLifecycleApplication application,
        string circleId,
        string contributionId,
        string grantId,
        RevokeMemberAccessGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParse(circleId, out var circle)
            || !TryParse(contributionId, out var contribution)
            || !TryParse(grantId, out var grant)
            || !TryParse(request.RequestId, out var requestId))
        {
            return InvalidIds();
        }

        return await InvokeAsync(
            () => application.RevokeGrantAsync(
                new CircleId(circle),
                new CircleFilesContributionId(contribution),
                new MemberAccessGrantId(grant),
                new MemberAccessGrantRevocationRequestId(requestId),
                request.ExpectedGeneration,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<IResult> PreviewGrantCleanupAsync(
        CircleFilesLifecycleApplication application,
        string circleId,
        string contributionId,
        string grantId,
        PreviewCircleFilesGrantCleanupRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParse(circleId, out var circle)
            || !TryParse(contributionId, out var contribution)
            || !TryParse(grantId, out var grant))
        {
            return InvalidIds();
        }

        return await InvokeAsync(
            () => application.PreviewGrantCleanupAsync(
                new CircleId(circle),
                new CircleFilesContributionId(contribution),
                new MemberAccessGrantId(grant),
                request.FolderPath,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<IResult> ApplyGrantCleanupAsync(
        CircleFilesLifecycleApplication application,
        string circleId,
        string contributionId,
        string grantId,
        ApplyCircleFilesGrantCleanupRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParse(circleId, out var circle)
            || !TryParse(contributionId, out var contribution)
            || !TryParse(grantId, out var grant))
        {
            return InvalidIds();
        }

        return await InvokeAsync(
            () => application.RemoveGrantAsync(
                new CircleId(circle),
                new CircleFilesContributionId(contribution),
                new MemberAccessGrantId(grant),
                request.FolderPath,
                request.PlanId,
                request.TerminateOpenSessions,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<IResult> PreviewHostRemovalAsync(
        CircleFilesLifecycleApplication application,
        string circleId,
        string contributionId,
        PreviewCircleFilesHostRemovalRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParse(circleId, out var circle)
            || !TryParse(contributionId, out var contribution))
        {
            return InvalidIds();
        }

        return await InvokeAsync(
            () => application.PreviewHostRemovalAsync(
                new CircleId(circle),
                new CircleFilesContributionId(contribution),
                request.FolderPath,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<IResult> ApplyHostRemovalAsync(
        CircleFilesLifecycleApplication application,
        string circleId,
        string contributionId,
        ApplyCircleFilesHostRemovalRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParse(circleId, out var circle)
            || !TryParse(contributionId, out var contribution))
        {
            return InvalidIds();
        }

        return await InvokeAsync(
            () => application.RemoveHostAsync(
                new CircleId(circle),
                new CircleFilesContributionId(contribution),
                request.FolderPath,
                request.PlanId,
                request.TerminateOpenSessions,
                cancellationToken)).ConfigureAwait(false);
    }

    private static async Task<IResult> InvokeAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation().ConfigureAwait(false));
        }
        catch (InputValidationException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (LocalStateConflictException exception)
        {
            return Results.Conflict(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (LocalStateException exception)
        {
            var error = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code is "circle_not_found"
                or "circle_files_contribution_not_found"
                or "circle_files_grant_not_found"
                ? Results.NotFound(error)
                : Results.BadRequest(error);
        }
        catch (CircleFilesHostingException exception)
        {
            var error = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code is "hosting_path_invalid"
                or "hosting_authorization_invalid"
                or "grant_authorization_invalid"
                or "grant_secret_invalid"
                or "windows_required"
                ? Results.BadRequest(error)
                : Results.Conflict(error);
        }
    }

    private static IResult InvalidIds() => Results.BadRequest(new ErrorResponse(
        "invalid_request_id",
        "Circle Files lifecycle IDs must be canonical non-empty UUIDs."));

    private static bool TryParse(string? value, out Guid parsed) =>
        Guid.TryParseExact(value, "D", out parsed)
        && parsed != Guid.Empty
        && string.Equals(value, parsed.ToString("D"), StringComparison.Ordinal);
}
