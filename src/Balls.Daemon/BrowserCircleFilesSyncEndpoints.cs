using System.Net.Sockets;
using Balls.Core;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;
using Balls.Protocol.Remote.V1;
using Microsoft.AspNetCore.Http;

namespace Balls.Daemon;

internal static class BrowserCircleFilesSyncEndpoints
{
    internal static async Task<IResult> SynchronizeAsync(
        TrustedCircleFilesSyncApplication application,
        IAdmissionStateStore connections,
        string circleId,
        CancellationToken cancellationToken)
    {
        if (!BrowserUuid.TryParse(circleId, out var parsedCircleId))
        {
            return Results.BadRequest(
                new ErrorResponse("invalid_circle_id", "Circle ID must be a canonical UUID."));
        }

        try
        {
            var connection = await BrowserCircleConnections.LoadAsync(
                connections,
                new CircleId(parsedCircleId),
                cancellationToken).ConfigureAwait(false);
            var synced = await application.SynchronizeAsync(
                new CircleId(parsedCircleId),
                connection.SyncAddress.Value,
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(
                new BrowserCircleFilesSyncResponse(synced.CircleId, synced.ImportedGrantCount));
        }
        catch (InputValidationException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (LocalStateException exception)
        {
            return Results.Conflict(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (RemoteChannelException exception)
        {
            return Results.Json(
                new ErrorResponse(
                    exception.Code,
                    "The Circle owner's device could not synchronize your shared files."),
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or SocketException)
        {
            return Results.Json(
                new ErrorResponse(
                    "connection_failed",
                    "The Circle owner's device could not be reached on your local network."),
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
