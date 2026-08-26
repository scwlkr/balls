using System.Net.Sockets;
using Balls.Core;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;
using Balls.Protocol.Remote.V1;
using Balls.Transport.Lan;
using Microsoft.AspNetCore.Http;

namespace Balls.Daemon;

internal static class BrowserInvitationEndpoints
{
    internal static async Task<IResult> CreateAsync(
        InvitationApplication invitations,
        BrowserInvitationListenerState listeners,
        string circleId,
        CreateBrowserCircleInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!BrowserUuid.TryParse(circleId, out var parsedCircleId))
        {
            return Results.BadRequest(
                new ErrorResponse("invalid_circle_id", "Circle ID must be a canonical UUID."));
        }

        if (!listeners.IsAvailable)
        {
            return Results.Conflict(
                new ErrorResponse(
                    listeners.ErrorCode,
                    listeners.ErrorMessage));
        }

        if (!TryGetShareableEndpoint(listeners.AdmissionAddress!, out var endpoint)
            || !TryGetShareableEndpoint(listeners.SyncAddress!, out var syncEndpoint))
        {
            return Results.Conflict(
                new ErrorResponse(
                    "private_listeners_unavailable",
                    "Balls is not ready to accept invitations on a reachable private network connection."));
        }

        try
        {
            var issued = await invitations.CreateAsync(
                new CircleId(parsedCircleId),
                request.ValidForMinutes,
                cancellationToken).ConfigureAwait(false);
            return Results.Created(
                BrowserRoutes.CircleInvitations(circleId) + "/" + issued.InvitationId,
                new BrowserCircleInvitationResponse(
                    issued.CircleId.ToString(),
                    issued.InvitationId.ToString(),
                    issued.ExpiresAtUtc,
                    issued.Package,
                    LanTcpEndpoint.ProviderName,
                    endpoint,
                    syncEndpoint));
        }
        catch (InputValidationException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (LocalStateException exception) when (exception.Code == "circle_not_found")
        {
            return Results.NotFound(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (LocalStateException exception) when (exception.Code == "circle_authority_not_found")
        {
            return Results.Json(
                new ErrorResponse(
                    "invitation_not_authorized",
                    "Only the Circle owner can invite someone from this device."),
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    internal static async Task<IResult> JoinAsync(
        TrustedCircleAdmissionApplication admissions,
        JoinBrowserCircleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = BrowserCircleConnections.ParseInvitation(
                request.Provider,
                request.AdmissionEndpoint,
                request.SyncEndpoint);
            var circle = await admissions.JoinWithConnectionAsync(
                request.Package,
                connection.AdmissionAddress,
                connection.SyncAddress,
                request.MemberDisplayName,
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToResponse(circle));
        }
        catch (InputValidationException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (AdmissionRejectedException exception)
        {
            var error = new ErrorResponse(exception.Code, "The Circle invitation was rejected.");
            return exception.Code == "replayed"
                ? Results.Conflict(error)
                : Results.BadRequest(error);
        }
        catch (LocalStateConflictException exception)
        {
            return Results.Conflict(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (RemoteChannelException exception)
        {
            return Results.Json(
                new ErrorResponse(exception.Code, "The Circle owner could not complete your invitation."),
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

    private static bool TryGetShareableEndpoint(
        RemoteTransportAddress address,
        out string endpoint)
    {
        endpoint = string.Empty;
        try
        {
            var parsed = LanTcpEndpoint.Parse(address);
            if (!LanTcpEndpoint.IsPrivateIPv4(parsed.Address))
            {
                return false;
            }

            endpoint = parsed.ToString();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static CircleDetailsResponse ToResponse(CircleDetails details)
    {
        return new CircleDetailsResponse(
            new CircleResponse(
                details.Circle.Id.ToString(),
                details.Circle.Name,
                details.Circle.CreatedAtUtc,
                details.Members.Count,
                details.Nodes.Count),
            details.Members.Select(member => new MemberResponse(
                member.Id.ToString(),
                member.DisplayName,
                member.Role == MemberRole.Owner ? "owner" : "member",
                member.JoinedAtUtc)).ToArray(),
            details.Nodes.Select(node => new CircleNodeResponse(
                node.NodeId.ToString(),
                node.DisplayName,
                node.JoinedAtUtc)).ToArray());
    }
}

internal sealed record BrowserInvitationListenerState(
    RemoteTransportAddress? AdmissionAddress,
    RemoteTransportAddress? SyncAddress,
    string ErrorCode,
    string ErrorMessage)
{
    internal bool IsAvailable => AdmissionAddress is not null && SyncAddress is not null;

    internal static BrowserInvitationListenerState Available(
        RemoteTransportAddress admissionAddress,
        RemoteTransportAddress syncAddress) =>
        new(admissionAddress, syncAddress, string.Empty, string.Empty);

    internal static BrowserInvitationListenerState Unavailable(
        string errorCode,
        string errorMessage) =>
        new(null, null, errorCode, errorMessage);
}
