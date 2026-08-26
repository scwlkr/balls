using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class BrowserCircleFilesGrantEndpoints
{
    internal static async Task<IResult> PreviewAsync(
        BrowserCircleFilesGrantApplication application,
        string circleId,
        string sessionToken,
        PreviewBrowserCircleFilesGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (!BrowserUuid.TryParse(circleId, out var circle))
        {
            return InvalidCircle();
        }

        return await InvokeAsync(
            () => application.PreviewAsync(
                new CircleId(circle),
                sessionToken,
                request.FolderName,
                request.MemberName,
                request.Access,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<IResult> ApplyAsync(
        BrowserCircleFilesGrantApplication application,
        string circleId,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        if (!BrowserUuid.TryParse(circleId, out var circle))
        {
            return InvalidCircle();
        }

        return await InvokeAsync(
            () => application.ApplyAsync(
                new CircleId(circle),
                sessionToken,
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
            var error = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code == "circle_files_owner_required"
                ? Results.Json(error, ControlJson.Options, statusCode: StatusCodes.Status403Forbidden)
                : Results.BadRequest(error);
        }
        catch (LocalStateConflictException exception)
        {
            return SafeConflict(exception);
        }
        catch (LocalStateException exception)
        {
            var error = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code == "circle_not_found"
                ? Results.NotFound(error)
                : Results.BadRequest(error);
        }
        catch (CircleFilesHostingException)
        {
            return GrantMutationFailed();
        }
    }

    private static IResult SafeConflict(LocalStateConflictException exception) =>
        exception.Code is "circle_files_grant_preview_required"
            or "circle_files_grant_approval_changed"
            or "circle_files_grant_member_unavailable"
            or "circle_files_grant_folder_unavailable"
            or "circle_files_hosted_folder_missing"
            ? Results.Conflict(new ErrorResponse(exception.Code, exception.Message))
            : GrantMutationFailed();

    private static IResult GrantMutationFailed() => Results.Conflict(new ErrorResponse(
        "circle_files_grant_apply_failed",
        "Windows could not finish this access change. Approve the Windows prompt and try again."));

    private static IResult InvalidCircle() => Results.BadRequest(new ErrorResponse(
        "invalid_circle_id",
        "Circle ID must be a canonical non-empty UUID."));

}
