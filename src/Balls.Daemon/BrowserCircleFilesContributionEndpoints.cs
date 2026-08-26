using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class BrowserCircleFilesContributionEndpoints
{
    internal static async Task<IResult> SelectFolderAsync(
        BrowserCircleFilesContributionApplication application,
        string circleId,
        CancellationToken cancellationToken)
    {
        if (!TryParse(circleId, out var circle))
        {
            return InvalidIds();
        }

        return await InvokeAsync(
            () => application.SelectFolderAsync(
                new CircleId(circle),
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<IResult> ApplyAsync(
        BrowserCircleFilesContributionApplication application,
        string circleId,
        ApplyBrowserCircleFilesFolderRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParse(circleId, out var circle)
            || !TryParse(request.RequestId, out var requestId))
        {
            return InvalidIds();
        }

        return await InvokeAsync(
            () => application.ApplyAsync(
                new CircleId(circle),
                new CircleFilesContributionRequestId(requestId),
                request.FolderPath,
                request.DisplayName,
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
            return Results.Conflict(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (LocalStateException exception)
        {
            var error = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code is "circle_not_found" or "local_circle_member_not_found"
                ? Results.NotFound(error)
                : Results.BadRequest(error);
        }
        catch (CircleFilesHostingException exception)
        {
            var error = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code is "hosting_path_invalid"
                or "hosting_authorization_invalid"
                or "windows_required"
                ? Results.BadRequest(error)
                : Results.Conflict(error);
        }
    }

    private static IResult InvalidIds() => Results.BadRequest(new ErrorResponse(
        "invalid_request_id",
        "Circle and contribution request IDs must be canonical non-empty UUIDs."));

    private static bool TryParse(string? value, out Guid parsed) =>
        Guid.TryParseExact(value, "D", out parsed)
        && parsed != Guid.Empty
        && string.Equals(value, parsed.ToString("D"), StringComparison.Ordinal);
}
