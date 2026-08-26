using Balls.Core;
using Balls.Daemon;
using Balls.Storage.Sqlite;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class BrowserCircleFilesGrantApprovalFingerprintTests
{
    [TestMethod]
    public void Fingerprint_is_stable_and_binds_the_exact_hosted_location()
    {
        var circleId = new CircleId(Guid.Parse("0198d000-5000-7000-8000-000000000001"));
        var ownerId = new MemberId(Guid.Parse("0198d000-5000-7000-8000-000000000002"));
        var member = new Member(
            new MemberId(Guid.Parse("0198d000-5000-7000-8000-000000000003")),
            circleId,
            "Bob",
            MemberRole.Member,
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        var contribution = new CircleFilesContribution(
            new CircleFilesContributionId(Guid.Parse("0198d000-5000-7000-8000-000000000004")),
            circleId,
            new CircleFilesProviderIdentity(
                new CircleFilesProviderId(Guid.Parse("0198d000-5000-7000-8000-000000000005")),
                new NodeId(Guid.Parse("0198d000-5000-7000-8000-000000000006"))),
            "Projects",
            CircleFilesContributionLifecycle.Defined,
            1,
            member.JoinedAtUtc,
            new CircleFilesOwnerAuthorization(
                ownerId,
                1,
                member.JoinedAtUtc,
                [1, 2, 3],
                [4, 5, 6],
                [7, 8, 9]));
        var hosted = new CircleFilesHostedFolderBinding(
            circleId,
            contribution.Id,
            contribution.Provider.Id,
            contribution.Provider.NodeId,
            @"C:\BallsDemo\Projects");

        var first = BrowserCircleFilesGrantApprovalFingerprint.Create(
            contribution,
            member,
            hosted);
        var same = BrowserCircleFilesGrantApprovalFingerprint.Create(
            contribution,
            member,
            hosted);
        var changed = BrowserCircleFilesGrantApprovalFingerprint.Create(
            contribution,
            member,
            hosted with { FolderPath = @"C:\BallsDemo\Substituted" });

        Assert.AreEqual(first, same);
        Assert.AreNotEqual(first, changed);
        Assert.AreEqual(64, first.Value.Length);
    }
}
