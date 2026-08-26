using Balls.Core;
using Balls.Platform;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class BrowserCircleFilesOpenEndpoints
{
    internal static async Task<IResult> OpenAsync(
        CircleFilesMemberMappingApplication application,
        IAdmissionStateStore connections,
        string circleId,
        CancellationToken token)
    {
        if (!BrowserUuid.TryParse(circleId, out var parsedCircle))
        {
            return Results.BadRequest(
                new ErrorResponse("invalid_circle_id", "Circle ID must be a canonical UUID."));
        }

        try
        {
            var connection = await BrowserCircleConnections.LoadAsync(
                connections,
                new CircleId(parsedCircle),
                token).ConfigureAwait(false);
            return Results.Ok(await application.OpenAsync(
                new CircleId(parsedCircle),
                connection.FilesHost,
                token).ConfigureAwait(false));
        }
        catch (CircleFilesHostingException exception)
        {
            return ToBrowserFailure(exception.Code);
        }
        catch (LocalStateConflictException exception)
        {
            return ToBrowserFailure(exception.Code);
        }
        catch (LocalStateException exception)
        {
            return exception.Code == "circle_not_found"
                ? Results.NotFound(new ErrorResponse(
                    "shared_folder_unavailable",
                    "This Circle is no longer available on this computer."))
                : ToBrowserFailure(exception.Code);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Offline();
        }
    }

    private static IResult ToBrowserFailure(string code) => code switch
    {
        "mapping_endpoint_unreachable" => Offline(),
        "explorer_launch_failed" => Results.Json(
            new ErrorResponse(
                code,
                "The shared folder is connected, but File Explorer did not open. Try again."),
            statusCode: StatusCodes.Status502BadGateway),
        "windows_required" => Results.BadRequest(new ErrorResponse(
            code,
            "Opening shared folders in File Explorer is available only on Windows.")),
        "mapping_drive_unavailable" => Results.Conflict(new ErrorResponse(
            code,
            "No supported drive letter is free. Disconnect an unused drive and try again.")),
        "circle_files_capability_unavailable" => Results.Conflict(new ErrorResponse(
            code,
            "The shared folder is not ready yet. Ask the Circle owner to finish sharing it, then try again.")),
        "circle_files_capability_ambiguous" => Results.Conflict(new ErrorResponse(
            code,
            "More than one shared folder is ready. This version can open one at a time.")),
        "circle_files_member_required" => Results.Json(
            new ErrorResponse(code, "Only a joined Circle Member can open this shared folder."),
            statusCode: StatusCodes.Status403Forbidden),
        "circle_connection_missing" or "invalid_circle_connection" => Results.Conflict(
            new ErrorResponse(
                code,
                "Balls could not load this shared folder connection. Ask the Circle owner for a new invitation.")),
        _ when code.Contains("collision", StringComparison.Ordinal) => Results.Conflict(
            new ErrorResponse(
                "shared_folder_mapping_conflict",
                "A Windows drive or saved connection is already in use. Balls left it unchanged.")),
        _ when code is "mapping_recovery_incomplete" or "mapping_plan_changed"
            or "mapping_share_identity_mismatch" or "circle_files_provider_credential_conflict" =>
            Results.Conflict(new ErrorResponse(
                "shared_folder_open_failed",
                "Balls could not safely open this shared folder. Nothing unrelated was changed; try again.")),
        _ => Results.BadRequest(new ErrorResponse(
            "shared_folder_open_failed",
            "Balls could not open this shared folder. Try again.")),
    };

    private static IResult Offline() => Results.Json(
        new ErrorResponse(
            "shared_folder_offline",
            "The shared folder is offline. Check that the Circle owner's computer is on, then try again."),
        statusCode: StatusCodes.Status502BadGateway);
}
