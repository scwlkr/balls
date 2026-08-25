using System.Net;
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
        string? admissionListenEndpoint,
        string circleId,
        CreateBrowserCircleInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(circleId, "D", out var parsedCircleId))
        {
            return Results.BadRequest(
                new ErrorResponse("invalid_circle_id", "Circle ID must be a canonical UUID."));
        }

        if (admissionListenEndpoint is null)
        {
            return Results.Conflict(
                new ErrorResponse(
                    "admission_listener_unavailable",
                    "This device must be configured to accept connections on your local network before inviting someone."));
        }

        if (!TryResolveShareableEndpoint(admissionListenEndpoint, request.HostAddress, out var endpoint))
        {
            return Results.BadRequest(
                new ErrorResponse(
                    "invalid_shareable_host_address",
                    "Enter the private IPv4 address that invited people can reach on your local network."));
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
                    endpoint));
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
        JoinCircleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var circle = await admissions.JoinAsync(
                request.Package,
                new RemoteTransportAddress(LanTcpEndpoint.ProviderName, request.Endpoint),
                request.MemberDisplayName,
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToResponse(circle));
        }
        catch (InputValidationException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(
                new ErrorResponse(
                    "invalid_admission_endpoint",
                    "The invitation does not contain a valid private network address."));
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

    private static bool TryResolveShareableEndpoint(
        string admissionListenEndpoint,
        string? hostAddress,
        out string endpoint)
    {
        endpoint = string.Empty;
        if (!IPEndPoint.TryParse(admissionListenEndpoint, out var listenEndpoint))
        {
            return false;
        }

        IPAddress address;
        if (string.IsNullOrWhiteSpace(hostAddress))
        {
            address = listenEndpoint.Address;
        }
        else if (!IPAddress.TryParse(hostAddress.Trim(), out address!))
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var candidate = $"{address}:{listenEndpoint.Port}";
        try
        {
            LanTcpEndpoint.Parse(new RemoteTransportAddress(LanTcpEndpoint.ProviderName, candidate));
            endpoint = candidate;
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
