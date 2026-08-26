using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class CircleFilesMemberMappingEndpoints
{
    internal static Task<IResult> PreviewAsync(
        CircleFilesMemberMappingApplication application,
        string circleId,
        string contributionId,
        string grantId,
        PreviewCircleFilesMemberMappingRequest request,
        CancellationToken token) =>
        InvokeAsync(
            circleId, contributionId, grantId,
            (circle, contribution, grant) => application.PreviewAsync(
                circle, contribution, grant, request.Endpoint, request.DriveLetter, token),
            token);

    internal static Task<IResult> InspectAsync(
        CircleFilesMemberMappingApplication application,
        string circleId,
        string contributionId,
        string grantId,
        InspectCircleFilesMemberMappingRequest request,
        CancellationToken token) =>
        InvokeAsync(
            circleId, contributionId, grantId,
            (circle, contribution, grant) => application.InspectAsync(
                circle, contribution, grant, request.Endpoint, request.DriveLetter, token),
            token);

    internal static Task<IResult> MapAsync(
        CircleFilesMemberMappingApplication application,
        string circleId,
        string contributionId,
        string grantId,
        ApplyCircleFilesMemberMappingRequest request,
        CancellationToken token) =>
        InvokeAsync(
            circleId, contributionId, grantId,
            (circle, contribution, grant) => application.MapAsync(
                circle, contribution, grant, request.Endpoint, request.DriveLetter,
                request.PlanId, token),
            token);

    internal static Task<IResult> UnmapAsync(
        CircleFilesMemberMappingApplication application,
        string circleId,
        string contributionId,
        string grantId,
        UnmapCircleFilesMemberMappingRequest request,
        CancellationToken token) =>
        InvokeAsync(
            circleId, contributionId, grantId,
            (circle, contribution, grant) => application.UnmapAsync(
                circle, contribution, grant, request.Endpoint, request.DriveLetter, token),
            token);

    private static async Task<IResult> InvokeAsync<T>(
        string circleId,
        string contributionId,
        string grantId,
        Func<CircleId, CircleFilesContributionId, MemberAccessGrantId, Task<T>> operation,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(circleId, "D", out var parsedCircle)
            || !Guid.TryParseExact(contributionId, "D", out var parsedContribution)
            || !Guid.TryParseExact(grantId, "D", out var parsedGrant))
        {
            return Results.BadRequest(new ErrorResponse(
                "invalid_circle_files_mapping_id",
                "Circle, contribution, and grant IDs must be canonical UUIDs."));
        }

        try
        {
            return Results.Ok(await operation(
                new CircleId(parsedCircle),
                new CircleFilesContributionId(parsedContribution),
                new MemberAccessGrantId(parsedGrant)).ConfigureAwait(false));
        }
        catch (CircleFilesHostingException exception)
        {
            var response = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code.Contains("collision", StringComparison.Ordinal)
                || exception.Code is "mapping_plan_changed" or "mapping_share_identity_mismatch"
                    or "mapping_recovery_incomplete"
                ? Results.Conflict(response)
                : Results.BadRequest(response);
        }
        catch (LocalStateConflictException exception)
        {
            return Results.Conflict(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (LocalStateException exception)
        {
            var response = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code is "circle_not_found" or "circle_files_contribution_not_found"
                or "circle_files_grant_not_found"
                ? Results.NotFound(response)
                : Results.BadRequest(response);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Json(
                new ErrorResponse(
                    "mapping_endpoint_unreachable",
                    "The exact private SMB endpoint could not be reached."),
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

}
