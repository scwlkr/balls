using Balls.Core;
using Balls.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleFilesStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 19, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Contribution_and_grant_are_idempotent_and_restart_stable()
    {
        using var directory = new TemporaryDirectory();
        CircleFilesContribution createdContribution;
        MemberAccessGrant createdGrant;
        CircleId circleId;

        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var circles = new CircleApplication(store, new FixedTimeProvider(Now), "Alice-PC");
            var circle = await circles.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000001")),
                    "Example Studio",
                    "Alice"));
            circleId = circle.Circle.Id;
            var ownerId = circle.Members.Single().Id;
            var files = new CircleFilesApplication(store, store, new FixedTimeProvider(Now));
            var contributionRequest = new CircleFilesContributionRequestId(
                Guid.Parse("0198d000-2000-7000-8000-000000000002"));

            createdContribution = await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    contributionRequest,
                    circleId,
                    "Project Files"));
            var retriedContribution = await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    contributionRequest,
                    circleId,
                    " Project Files "));
            AssertContribution(createdContribution, retriedContribution);

            var grantRequest = new MemberAccessGrantRequestId(
                Guid.Parse("0198d000-2000-7000-8000-000000000003"));
            createdGrant = await files.CreateAccessGrantAsync(
                new CreateMemberAccessGrantCommand(
                    grantRequest,
                    circleId,
                    createdContribution.Id,
                    ownerId,
                    MemberAccessMode.ReadOnly));
            var retriedGrant = await files.CreateAccessGrantAsync(
                new CreateMemberAccessGrantCommand(
                    grantRequest,
                    circleId,
                    createdContribution.Id,
                    ownerId,
                    MemberAccessMode.ReadOnly));
            AssertGrant(createdGrant, retriedGrant);
        }

        await using var reopened = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        var contributions = await reopened.ListContributionsAsync(circleId);
        var grants = await reopened.ListAccessGrantsAsync(circleId, createdContribution.Id);

        Assert.AreEqual(1, contributions.Count);
        AssertContribution(createdContribution, contributions.Single());
        Assert.AreEqual(1, grants.Count);
        AssertGrant(createdGrant, grants.Single());
    }

    [TestMethod]
    public async Task Version_five_migrates_forward_once_and_preserves_existing_Circle_state()
    {
        using var directory = new TemporaryDirectory();
        CircleId circleId;
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var circles = new CircleApplication(store, new FixedTimeProvider(Now), "Alice-PC");
            circleId = (await circles.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000004")),
                    "Migration Circle",
                    "Alice"))).Circle.Id;
        }

        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                DROP TABLE circle_files_access_grants;
                DROP TABLE circle_files_contributions;
                PRAGMA user_version = 5;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var migrated = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            Assert.IsNotNull(await migrated.GetCircleAsync(circleId));
            Assert.AreEqual(0, (await migrated.ListContributionsAsync(circleId)).Count);
            var files = new CircleFilesApplication(migrated, migrated, new FixedTimeProvider(Now));
            _ = await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    new CircleFilesContributionRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000005")),
                    circleId,
                    "Migrated Files"));
        }

        await using var versionConnection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await versionConnection.OpenAsync();
        using var versionCommand = versionConnection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert.AreEqual(
            SqliteLocalStateStore.CurrentSchemaVersion,
            Convert.ToInt32(await versionCommand.ExecuteScalarAsync()));
    }

    [TestMethod]
    public async Task Invalid_grant_rolls_back_without_partial_state_and_restart_keeps_it_absent()
    {
        using var directory = new TemporaryDirectory();
        CircleId circleId;
        CircleFilesContributionId contributionId;
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var circles = new CircleApplication(store, new FixedTimeProvider(Now), "Alice-PC");
            circleId = (await circles.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000006")),
                    "Rollback Circle",
                    "Alice"))).Circle.Id;
            var files = new CircleFilesApplication(store, store, new FixedTimeProvider(Now));
            contributionId = (await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    new CircleFilesContributionRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000007")),
                    circleId,
                    "Rollback Files"))).Id;

            var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
                () => files.CreateAccessGrantAsync(
                    new CreateMemberAccessGrantCommand(
                        new MemberAccessGrantRequestId(
                            Guid.Parse("0198d000-2000-7000-8000-000000000008")),
                        circleId,
                        contributionId,
                        new MemberId(
                            Guid.Parse("0198d000-2000-7000-8000-000000000009")),
                        MemberAccessMode.ReadWrite)));

            Assert.AreEqual("member_not_found", error.Code);
            Assert.AreEqual(0, (await files.ListAccessGrantsAsync(circleId, contributionId)).Count);
        }

        await using var reopened = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        Assert.AreEqual(
            0,
            (await reopened.ListAccessGrantsAsync(circleId, contributionId)).Count);
    }

    private static void AssertContribution(
        CircleFilesContribution expected,
        CircleFilesContribution actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.CircleId, actual.CircleId);
        Assert.AreEqual(expected.Provider, actual.Provider);
        Assert.AreEqual(expected.DisplayName, actual.DisplayName);
        Assert.AreEqual(expected.Lifecycle, actual.Lifecycle);
        Assert.AreEqual(expected.Generation, actual.Generation);
        Assert.AreEqual(expected.CreatedAtUtc, actual.CreatedAtUtc);
        AssertAuthorization(expected.Authorization, actual.Authorization);
    }

    private static void AssertGrant(MemberAccessGrant expected, MemberAccessGrant actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.CircleId, actual.CircleId);
        Assert.AreEqual(expected.ContributionId, actual.ContributionId);
        Assert.AreEqual(expected.MemberId, actual.MemberId);
        Assert.AreEqual(expected.Access, actual.Access);
        Assert.AreEqual(expected.Lifecycle, actual.Lifecycle);
        Assert.AreEqual(expected.Generation, actual.Generation);
        Assert.AreEqual(expected.CreatedAtUtc, actual.CreatedAtUtc);
        AssertAuthorization(expected.Authorization, actual.Authorization);
    }

    private static void AssertAuthorization(
        CircleFilesOwnerAuthorization expected,
        CircleFilesOwnerAuthorization actual)
    {
        Assert.AreEqual(expected.OwnerMemberId, actual.OwnerMemberId);
        Assert.AreEqual(expected.AuthorityGeneration, actual.AuthorityGeneration);
        Assert.AreEqual(expected.AuthorizedAtUtc, actual.AuthorizedAtUtc);
        CollectionAssert.AreEqual(expected.Transcript, actual.Transcript);
        CollectionAssert.AreEqual(expected.MemberSignature, actual.MemberSignature);
        CollectionAssert.AreEqual(
            expected.CircleAuthoritySignature,
            actual.CircleAuthoritySignature);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
