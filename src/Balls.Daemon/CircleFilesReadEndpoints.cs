using Balls.Core;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class CircleFilesReadEndpoints
{
    public static async Task<IResult> GetLocalViewerAsync(
        CircleApplication circles,
        CircleFilesApplication files,
        string circleId,
        CancellationToken cancellationToken)
    {
        var circle = await FindCircleAsync(circles, circleId, cancellationToken)
            .ConfigureAwait(false);
        if (circle.Error is not null)
        {
            return circle.Error;
        }

        var viewer = await files.GetLocalAuthorizationContextAsync(
            circle.Details!.Circle.Id,
            cancellationToken).ConfigureAwait(false);
        return viewer is null
            ? Results.NotFound(
                new ErrorResponse(
                    "local_circle_member_not_found",
                    "This device does not have an authorized Circle member."))
            : Results.Ok(new BrowserCircleViewerResponse(
                viewer.MemberId.ToString(),
                viewer.MemberRole switch
                {
                    MemberRole.Owner => "owner",
                    MemberRole.Member => "member",
                    _ => throw new InvalidOperationException("Unknown Member role."),
                }));
    }

    public static async Task<IResult> ListContributionsAsync(
        CircleApplication circles,
        CircleFilesApplication files,
        string circleId,
        CancellationToken cancellationToken)
    {
        var circle = await FindCircleAsync(circles, circleId, cancellationToken)
            .ConfigureAwait(false);
        if (circle.Error is not null)
        {
            return circle.Error;
        }

        var contributions = await files.ListContributionsAsync(
            circle.Details!.Circle.Id,
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(
            new CircleFilesContributionListResponse(
                circle.Details.Circle.Id.ToString(),
                contributions.Select(CircleFilesResponseMapper.ToResponse).ToArray()));
    }

    public static async Task<IResult> ListAccessGrantsAsync(
        CircleApplication circles,
        CircleFilesApplication files,
        string circleId,
        string contributionId,
        CancellationToken cancellationToken) =>
        await ListAccessGrantsAsync(
            circles,
            files,
            circleId,
            contributionId,
            restrictToLocalMember: false,
            cancellationToken).ConfigureAwait(false);

    public static async Task<IResult> ListAccessGrantsForViewerAsync(
        CircleApplication circles,
        CircleFilesApplication files,
        string circleId,
        string contributionId,
        CancellationToken cancellationToken) =>
        await ListAccessGrantsAsync(
            circles,
            files,
            circleId,
            contributionId,
            restrictToLocalMember: true,
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> ListAccessGrantsAsync(
        CircleApplication circles,
        CircleFilesApplication files,
        string circleId,
        string contributionId,
        bool restrictToLocalMember,
        CancellationToken cancellationToken)
    {
        var circle = await FindCircleAsync(circles, circleId, cancellationToken)
            .ConfigureAwait(false);
        if (circle.Error is not null)
        {
            return circle.Error;
        }

        if (!TryParseCanonicalId(contributionId, out var parsedContributionId))
        {
            return Results.BadRequest(
                new ErrorResponse(
                    "invalid_contribution_id",
                    "Contribution ID must be a canonical UUID."));
        }

        var contributions = await files.ListContributionsAsync(
            circle.Details!.Circle.Id,
            cancellationToken).ConfigureAwait(false);
        if (contributions.All(value => value.Id.Value != parsedContributionId))
        {
            return Results.NotFound(
                new ErrorResponse(
                    "circle_files_contribution_not_found",
                    "The requested Circle Files contribution is not known."));
        }

        var grants = await files.ListAccessGrantsAsync(
            circle.Details.Circle.Id,
            new CircleFilesContributionId(parsedContributionId),
            cancellationToken).ConfigureAwait(false);

        if (restrictToLocalMember)
        {
            var viewer = await files.GetLocalAuthorizationContextAsync(
                circle.Details.Circle.Id,
                cancellationToken).ConfigureAwait(false);
            if (viewer is null)
            {
                return Results.NotFound(
                    new ErrorResponse(
                        "local_circle_member_not_found",
                        "This device does not have an authorized Circle member."));
            }

            if (viewer.MemberRole != MemberRole.Owner)
            {
                grants = grants.Where(grant => grant.MemberId == viewer.MemberId).ToArray();
            }
        }

        return Results.Ok(
            new MemberAccessGrantListResponse(
                circle.Details.Circle.Id.ToString(),
                parsedContributionId.ToString("D"),
                grants.Select(CircleFilesResponseMapper.ToResponse).ToArray()));
    }

    private static async Task<(CircleDetails? Details, IResult? Error)> FindCircleAsync(
        CircleApplication application,
        string value,
        CancellationToken cancellationToken)
    {
        if (!TryParseCanonicalId(value, out var circleId))
        {
            return (
                null,
                Results.BadRequest(
                    new ErrorResponse(
                        "invalid_circle_id",
                        "Circle ID must be a canonical UUID.")));
        }

        var circle = await application.GetCircleAsync(new CircleId(circleId), cancellationToken)
            .ConfigureAwait(false);
        return circle is null
            ? (
                null,
                Results.NotFound(
                    new ErrorResponse(
                        "circle_not_found",
                        "The requested Circle is not known to this Node.")))
            : (circle, null);
    }

    private static bool TryParseCanonicalId(string value, out Guid id) =>
        Guid.TryParseExact(value, "D", out id)
        && id != Guid.Empty
        && string.Equals(value, id.ToString("D"), StringComparison.Ordinal);
}
