using System.Net;
using System.Net.Sockets;
using Balls.Core;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;
using Balls.Protocol.Remote.V1;
using Balls.Transport.Lan;
using Microsoft.AspNetCore.Http;

namespace Balls.Daemon;

internal static class BrowserCircleFilesSyncEndpoints
{
    internal static async Task<IResult> SynchronizeAsync(
        TrustedCircleFilesSyncApplication application,
        string circleId,
        SyncBrowserCircleFilesRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(circleId, "D", out var parsedCircleId)
            || parsedCircleId == Guid.Empty
            || !string.Equals(circleId, parsedCircleId.ToString("D"), StringComparison.Ordinal))
        {
            return Results.BadRequest(
                new ErrorResponse("invalid_circle_id", "Circle ID must be a canonical UUID."));
        }

        try
        {
            var endpoint = LanTcpEndpoint.Parse(
                new RemoteTransportAddress(LanTcpEndpoint.ProviderName, request.Endpoint));
            if (endpoint.Address.AddressFamily != AddressFamily.InterNetwork
                || IPAddress.IsLoopback(endpoint.Address))
            {
                return InvalidEndpoint();
            }

            var synced = await application.SynchronizeAsync(
                new CircleId(parsedCircleId),
                endpoint.ToString(),
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(
                new BrowserCircleFilesSyncResponse(synced.CircleId, synced.ImportedGrantCount));
        }
        catch (InputValidationException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (ArgumentException)
        {
            return InvalidEndpoint();
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

    private static IResult InvalidEndpoint() =>
        Results.BadRequest(
            new ErrorResponse(
                "invalid_files_sync_endpoint",
                "Circle Files synchronization requires a private IPv4 address and port."));
}
