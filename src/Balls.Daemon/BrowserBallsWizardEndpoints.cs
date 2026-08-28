using Balls.Core;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class BrowserBallsWizardEndpoints
{
    public static async Task<IResult> GetStatusAsync(
        BallsWizardApplication application,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await application.GetStatusAsync(cancellationToken).ConfigureAwait(false));
    }

    public static async Task<IResult> StartInstallAsync(
        BallsWizardApplication application,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Accepted(
                BrowserRoutes.Wizard,
                await application.StartInstallAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException)
        {
            return Results.BadRequest(
                new ErrorResponse(
                    "wizard_install_unavailable",
                    "This Node cannot install Balls Wizard in its current state."));
        }
    }

    public static async Task<IResult> CancelInstallAsync(
        BallsWizardApplication application,
        CancellationToken cancellationToken)
    {
        return Results.Ok(
            await application.CancelInstallAsync(cancellationToken).ConfigureAwait(false));
    }

    public static async Task<IResult> RemoveAsync(
        BallsWizardApplication application,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await application.RemoveAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (IOException)
        {
            return Results.Conflict(
                new ErrorResponse(
                    "wizard_remove_failed",
                    "Wizard removal did not finish. Balls and Circle data were not changed."));
        }
    }

    public static async Task<IResult> ChatAsync(
        BallsWizardApplication application,
        CircleFilesApplication files,
        BrowserBallsWizardChatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var localRole = await GetLocalRoleAsync(files, request.CircleId, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(
                await application.ChatAsync(request, localRole, cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (BallsWizardApplicationException exception)
        {
            var error = new ErrorResponse(exception.Code, exception.Message);
            return exception.Code switch
            {
                "wizard_answer_failed" =>
                    Results.Json(error, statusCode: StatusCodes.Status502BadGateway),
                "wizard_integrity_failed" => Results.Conflict(error),
                _ => Results.BadRequest(error),
            };
        }
    }

    private static async Task<string> GetLocalRoleAsync(
        CircleFilesApplication files,
        string? circleId,
        CancellationToken cancellationToken)
    {
        if (circleId is null)
        {
            return "none";
        }
        if (!Guid.TryParse(circleId, out var parsedCircleId))
        {
            throw new BallsWizardApplicationException(
                "wizard_circle_invalid",
                "Wizard context requires a valid Circle identifier.");
        }

        var viewer = await files.GetLocalAuthorizationContextAsync(
            new CircleId(parsedCircleId),
            cancellationToken).ConfigureAwait(false);
        return viewer?.MemberRole switch
        {
            MemberRole.Owner => "owner",
            MemberRole.Member => "member",
            _ => "none",
        };
    }
}
