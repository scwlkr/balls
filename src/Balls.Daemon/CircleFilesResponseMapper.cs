using Balls.Core;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal static class CircleFilesResponseMapper
{
    public static CircleFilesContributionResponse ToResponse(
        CircleFilesContribution contribution) =>
        new(
            contribution.Id.ToString(),
            contribution.CircleId.ToString(),
            new CircleFilesProviderResponse(
                contribution.Provider.Id.ToString(),
                contribution.Provider.NodeId.ToString()),
            contribution.DisplayName,
            contribution.Lifecycle switch
            {
                CircleFilesContributionLifecycle.Defined => "defined",
                CircleFilesContributionLifecycle.Active => "active",
                CircleFilesContributionLifecycle.Retired => "retired",
                _ => throw new InvalidOperationException("Unknown contribution lifecycle."),
            },
            contribution.Generation,
            contribution.CreatedAtUtc,
            contribution.Authorization.OwnerMemberId.ToString(),
            contribution.Authorization.AuthorityGeneration,
            contribution.Authorization.AuthorizedAtUtc);

    public static MemberAccessGrantResponse ToResponse(MemberAccessGrant grant) =>
        new(
            grant.Id.ToString(),
            grant.CircleId.ToString(),
            grant.ContributionId.ToString(),
            grant.MemberId.ToString(),
            grant.Access switch
            {
                MemberAccessMode.ReadOnly => "read-only",
                MemberAccessMode.ReadWrite => "read-write",
                _ => throw new InvalidOperationException("Unknown Member access mode."),
            },
            grant.Lifecycle switch
            {
                MemberAccessGrantLifecycle.Defined => "defined",
                MemberAccessGrantLifecycle.Active => "active",
                MemberAccessGrantLifecycle.Revoked => "revoked",
                _ => throw new InvalidOperationException("Unknown Access Grant lifecycle."),
            },
            grant.Generation,
            grant.CreatedAtUtc,
            grant.Authorization.OwnerMemberId.ToString(),
            grant.Authorization.AuthorityGeneration,
            grant.Authorization.AuthorizedAtUtc);
}
