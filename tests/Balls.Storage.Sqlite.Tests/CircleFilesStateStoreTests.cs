using System.Security.Cryptography;
using System.Text;
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
    public async Task Grant_revocation_is_atomic_idempotent_and_restart_stable()
    {
        using var directory = new TemporaryDirectory();
        CircleId circleId;
        CircleFilesContributionId contributionId;
        MemberAccessGrantId grantId;
        var requestId = new MemberAccessGrantRevocationRequestId(
            Guid.Parse("0198d000-2000-7000-8000-000000000013"));

        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var circles = new CircleApplication(store, new FixedTimeProvider(Now), "Alice-PC");
            var circle = await circles.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000010")),
                    "Revocation Circle",
                    "Alice"));
            circleId = circle.Circle.Id;
            var files = new CircleFilesApplication(store, store, new FixedTimeProvider(Now));
            var contribution = await files.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    new CircleFilesContributionRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000011")),
                    circleId,
                    "Revocation Files"));
            contributionId = contribution.Id;
            var grant = await files.CreateAccessGrantAsync(
                new CreateMemberAccessGrantCommand(
                    new MemberAccessGrantRequestId(
                        Guid.Parse("0198d000-2000-7000-8000-000000000012")),
                    circleId,
                    contributionId,
                    circle.Members.Single().Id,
                    MemberAccessMode.ReadWrite));
            grantId = grant.Id;

            var revoked = await files.RevokeAccessGrantAsync(
                new RevokeMemberAccessGrantCommand(
                    requestId,
                    circleId,
                    contributionId,
                    grantId,
                    grant.Generation));
            Assert.AreEqual(MemberAccessGrantLifecycle.Revoked, revoked.Grant.Lifecycle);
            Assert.AreEqual(requestId, revoked.Revocation.RequestId);
        }

        await using var reopened = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        var filesAfterRestart = new CircleFilesApplication(
            reopened,
            reopened,
            new FixedTimeProvider(Now.AddMinutes(5)));
        var retry = await filesAfterRestart.RevokeAccessGrantAsync(
            new RevokeMemberAccessGrantCommand(
                requestId,
                circleId,
                contributionId,
                grantId,
                ExpectedGeneration: 1));

        Assert.AreEqual(MemberAccessGrantLifecycle.Revoked, retry.Grant.Lifecycle);
        Assert.AreEqual(Now, retry.Revocation.RevokedAtUtc);
        var persisted = await reopened.GetAccessGrantRevocationAsync(
            circleId,
            contributionId,
            grantId);
        Assert.IsNotNull(persisted);
        AssertGrant(retry.Grant, persisted.Grant);
        Assert.AreEqual(retry.Revocation.RequestId, persisted.Revocation.RequestId);
        Assert.AreEqual(retry.Revocation.CircleId, persisted.Revocation.CircleId);
        Assert.AreEqual(retry.Revocation.ContributionId, persisted.Revocation.ContributionId);
        Assert.AreEqual(retry.Revocation.GrantId, persisted.Revocation.GrantId);
        Assert.AreEqual(
            retry.Revocation.RevokedGeneration,
            persisted.Revocation.RevokedGeneration);
        Assert.AreEqual(retry.Revocation.RevokedAtUtc, persisted.Revocation.RevokedAtUtc);
        AssertAuthorization(
            retry.Revocation.Authorization,
            persisted.Revocation.Authorization);
        Assert.AreEqual(
            MemberAccessGrantLifecycle.Revoked,
            (await reopened.ListAccessGrantsAsync(circleId, contributionId)).Single().Lifecycle);
    }

    [TestMethod]
    public async Task Provider_credential_is_protected_restart_stable_and_conflicting_reuse_fails()
    {
        using var directory = new TemporaryDirectory();
        CircleFilesProviderCredentialBinding binding;
        var secret = Encoding.UTF8.GetBytes("Aa2!provider-secret-that-never-leaks");
        try
        {
            await using (var store = await SqliteLocalStateStore.OpenAsync(
                             directory.Path,
                             TestPrivateMaterialProtector.Instance))
            {
                var circles = new CircleApplication(store, new FixedTimeProvider(Now), "Owner-PC");
                var circle = await circles.CreateCircleAsync(
                    new CreateCircleCommand(
                        new CreationRequestId(Guid.Parse("0198d000-2000-7000-8000-000000000081")),
                        "Credential Circle",
                        "Owner"));
                var files = new CircleFilesApplication(store, store, new FixedTimeProvider(Now));
                var contribution = await files.CreateContributionAsync(
                    new CreateCircleFilesContributionCommand(
                        new CircleFilesContributionRequestId(
                            Guid.Parse("0198d000-2000-7000-8000-000000000082")),
                        circle.Circle.Id,
                        "Credential Files"));
                var grant = await files.CreateAccessGrantAsync(
                    new CreateMemberAccessGrantCommand(
                        new MemberAccessGrantRequestId(
                            Guid.Parse("0198d000-2000-7000-8000-000000000083")),
                        circle.Circle.Id,
                        contribution.Id,
                        circle.Members.Single().Id,
                        MemberAccessMode.ReadWrite));
                binding = new CircleFilesProviderCredentialBinding(
                    grant.Id.ToString(), circle.Circle.Id.ToString(), contribution.Id.ToString(),
                    grant.MemberId.ToString(), "windows-smb-3.1.1-v1", "BallsG-abcdef0123456",
                    new string('a', 64), "read-write", grant.Generation);

                using var prepared = await store.PrepareCircleFilesProviderCredentialAsync(binding, secret);
                Assert.IsTrue(prepared.IsNew);
                CollectionAssert.AreEqual(secret, prepared.Secret.ToArray());
                await store.CompleteCircleFilesProviderCredentialAsync(binding);
                Assert.AreEqual(
                    binding,
                    await store.GetActiveCircleFilesProviderCredentialBindingAsync(binding.GrantId));
                using var active = await store.GetActiveCircleFilesProviderCredentialAsync(binding.GrantId);
                Assert.IsNotNull(active);
                Assert.AreEqual(binding, active.Binding);
                Assert.IsTrue(active.IsActive);
                CollectionAssert.AreEqual(secret, active.Secret.ToArray());
            }

            var databasePath = Path.Combine(directory.Path, "balls.db");
            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT protected_secret FROM circle_files_provider_credentials;";
                var protectedValue = (byte[])(await command.ExecuteScalarAsync())!;
                Assert.IsFalse(secret.SequenceEqual(protectedValue));
            }

            await using var reopened = await SqliteLocalStateStore.OpenAsync(
                directory.Path,
                TestPrivateMaterialProtector.Instance);
            using (var retry = await reopened.PrepareCircleFilesProviderCredentialAsync(
                       binding,
                       Encoding.UTF8.GetBytes("Different-candidate-secret-Aa2!")))
            {
                Assert.IsFalse(retry.IsNew);
                Assert.IsTrue(retry.IsActive);
                CollectionAssert.AreEqual(secret, retry.Secret.ToArray());
                Assert.AreEqual("Circle Files provider credential (redacted)", retry.ToString());
            }

            var conflict = binding with { MemberId = Guid.NewGuid().ToString("D") };
            var error = await Assert.ThrowsExactlyAsync<LocalStateConflictException>(
                () => reopened.PrepareCircleFilesProviderCredentialAsync(conflict, secret));
            Assert.AreEqual("circle_files_provider_credential_conflict", error.Code);
            await reopened.DisposeAsync();

            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                using var corrupt = connection.CreateCommand();
                corrupt.CommandText = "UPDATE circle_files_provider_credentials SET protected_secret = X'00';";
                await corrupt.ExecuteNonQueryAsync();
            }
            var invalid = await Assert.ThrowsExactlyAsync<LocalStateException>(
                () => SqliteLocalStateStore.OpenAsync(
                    directory.Path,
                    TestPrivateMaterialProtector.Instance));
            Assert.AreEqual("invalid_private_material", invalid.Code);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [TestMethod]
    public async Task Removed_provider_credential_and_redacted_lifecycle_audit_survive_restart()
    {
        using var directory = new TemporaryDirectory();
        CircleId circleId;
        CircleFilesContributionId contributionId;
        MemberAccessGrantId grantId;
        CircleFilesProviderCredentialBinding binding;
        var secret = Encoding.UTF8.GetBytes("Aa2!cleanup-secret-that-remains-protected");
        try
        {
            await using (var store = await SqliteLocalStateStore.OpenAsync(
                             directory.Path,
                             TestPrivateMaterialProtector.Instance))
            {
                var circle = await new CircleApplication(
                        store,
                        new FixedTimeProvider(Now),
                        "Owner-PC")
                    .CreateCircleAsync(new CreateCircleCommand(
                        new CreationRequestId(
                            Guid.Parse("0198d000-2000-7000-8000-0000000000a1")),
                        "Cleanup Circle",
                        "Owner"));
                circleId = circle.Circle.Id;
                var files = new CircleFilesApplication(store, store, new FixedTimeProvider(Now));
                var contribution = await files.CreateContributionAsync(
                    new CreateCircleFilesContributionCommand(
                        new CircleFilesContributionRequestId(
                            Guid.Parse("0198d000-2000-7000-8000-0000000000a2")),
                        circleId,
                        "Cleanup Files"));
                contributionId = contribution.Id;
                var grant = await files.CreateAccessGrantAsync(
                    new CreateMemberAccessGrantCommand(
                        new MemberAccessGrantRequestId(
                            Guid.Parse("0198d000-2000-7000-8000-0000000000a3")),
                        circleId,
                        contributionId,
                        circle.Members.Single().Id,
                        MemberAccessMode.ReadOnly));
                grantId = grant.Id;
                binding = new CircleFilesProviderCredentialBinding(
                    grantId.ToString(),
                    circleId.ToString(),
                    contributionId.ToString(),
                    grant.MemberId.ToString(),
                    "windows-smb-3.1.1-v1",
                    "BallsG-abcdef0123456",
                    new string('a', 64),
                    "read-only",
                    grant.Generation);
                using var prepared = await store.PrepareCircleFilesProviderCredentialAsync(
                    binding,
                    secret);
                await store.CompleteCircleFilesProviderCredentialAsync(binding);
                await store.CompleteCircleFilesProviderCredentialRemovalAsync(binding);
                await store.RecordCircleFilesLifecycleAuditEventAsync(
                    new CircleFilesLifecycleAuditEvent(
                        Guid.Parse("0198d000-2000-7000-8000-0000000000a4"),
                        circleId,
                        contributionId,
                        grantId,
                        "grant-cleanup",
                        "removed",
                        0,
                        Now));
                await store.RecordCircleFilesLifecycleAuditEventAsync(
                    new CircleFilesLifecycleAuditEvent(
                        Guid.Parse("0198d000-2000-7000-8000-0000000000a0"),
                        circleId,
                        contributionId,
                        grantId,
                        "grant-cleanup",
                        "already-removed",
                        0,
                        Now));
            }

            await using (var reopened = await SqliteLocalStateStore.OpenAsync(
                             directory.Path,
                             TestPrivateMaterialProtector.Instance))
            {
                Assert.IsNull(await reopened.GetActiveCircleFilesProviderCredentialAsync(
                    grantId.ToString()));
                var state = await reopened.GetCircleFilesProviderCredentialStateAsync(
                    grantId.ToString());
                Assert.IsNotNull(state);
                Assert.IsTrue(state.IsRemoved);
                Assert.IsFalse(state.IsActive);
                using var cleanup = await reopened.GetCircleFilesProviderCredentialForCleanupAsync(
                    grantId.ToString());
                Assert.IsNotNull(cleanup);
                CollectionAssert.AreEqual(secret, cleanup.Secret.ToArray());
                var events = await reopened.ListCircleFilesLifecycleAuditEventsAsync(circleId);
                Assert.AreEqual(2, events.Count);
                Assert.IsTrue(events.All(value => value.Operation == "grant-cleanup"));
                CollectionAssert.AreEqual(
                    new[] { "removed", "already-removed" },
                    events.Select(value => value.Outcome).ToArray());
            }

            var databasePath = Path.Combine(directory.Path, "balls.db");
            var databaseBytes = await File.ReadAllBytesAsync(databasePath);
            Assert.IsFalse(Encoding.UTF8.GetString(databaseBytes).Contains(
                Encoding.UTF8.GetString(secret),
                StringComparison.Ordinal));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [TestMethod]
    public async Task Version_seven_lifecycle_migration_is_atomic_and_restartable()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
        }

        var databasePath = Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using (var downgrade = connection.CreateCommand())
            {
                downgrade.CommandText =
                    """
                    PRAGMA foreign_keys = OFF;
                    DROP TABLE circle_files_lifecycle_audit_events;
                    DROP TABLE circle_files_access_grant_revocations;
                    PRAGMA user_version = 7;
                    PRAGMA foreign_keys = ON;
                    """;
                await downgrade.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => SqliteLocalStateStore.ExecuteV7ToV8MigrationAsync(
                    connection,
                    _ => throw new InvalidOperationException("injected"),
                    CancellationToken.None));
            using var inspect = connection.CreateCommand();
            inspect.CommandText =
                """
                SELECT (SELECT user_version FROM pragma_user_version),
                       (SELECT COUNT(*) FROM sqlite_master
                        WHERE type='table' AND name IN (
                            'circle_files_access_grant_revocations',
                            'circle_files_lifecycle_audit_events'));
                """;
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(7L, reader.GetInt64(0));
            Assert.AreEqual(0L, reader.GetInt64(1));
        }

        await using var reopened = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        Assert.AreEqual(SqliteLocalStateStore.CurrentSchemaVersion, await ReadVersionAsync(databasePath));
    }

    [TestMethod]
    public async Task Version_six_provider_credential_migration_is_atomic_and_restartable()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
        }
        var databasePath = Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using (var downgrade = connection.CreateCommand())
            {
                downgrade.CommandText =
                    """
                    PRAGMA foreign_keys = OFF;
                    DROP TABLE circle_files_lifecycle_audit_events;
                    DROP TABLE circle_files_access_grant_revocations;
                    DROP TABLE circle_files_provider_credentials;
                    PRAGMA user_version = 6;
                    PRAGMA foreign_keys = ON;
                    """;
                await downgrade.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => SqliteLocalStateStore.ExecuteV6ToV7MigrationAsync(
                    connection,
                    _ => throw new InvalidOperationException("injected"),
                    CancellationToken.None));
            using var inspect = connection.CreateCommand();
            inspect.CommandText =
                """
                SELECT (SELECT user_version FROM pragma_user_version),
                       (SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='circle_files_provider_credentials');
                """;
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(6L, reader.GetInt64(0));
            Assert.AreEqual(0L, reader.GetInt64(1));
        }

        await using var reopened = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        Assert.AreEqual(SqliteLocalStateStore.CurrentSchemaVersion, await ReadVersionAsync(databasePath));
    }

    [TestMethod]
    public async Task Version_three_step_records_four_so_an_interrupted_upgrade_can_resume()
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
                        Guid.Parse("0198d000-2000-7000-8000-000000000010")),
                    "Interrupted Migration Circle",
                    "Alice"))).Circle.Id;
        }

        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using (var downgrade = connection.CreateCommand())
            {
                downgrade.CommandText =
                    """
                    PRAGMA foreign_keys = OFF;
                    DROP TABLE circle_files_lifecycle_audit_events;
                    DROP TABLE circle_files_access_grant_revocations;
                    DROP TABLE circle_files_provider_credentials;
                    DROP TABLE circle_files_access_grants;
                    DROP TABLE circle_files_contributions;
                    DROP TABLE circle_messages;
                    DROP TABLE outgoing_circle_messages;
                    DROP TABLE circle_member_nodes;
                    DROP TABLE local_circle_members;
                    DROP TABLE security_audit_events;
                    DROP TABLE circle_admissions;
                    DROP TABLE admission_challenges;
                    DROP TABLE admission_attempts;
                    DROP TABLE circle_node_credentials;
                    DROP TABLE circle_member_credentials;
                    DROP TABLE circle_trust;
                    PRAGMA user_version = 3;
                    """;
                await downgrade.ExecuteNonQueryAsync();
            }

            await SqliteLocalStateStore.MigrateV3ToV4Async(connection, CancellationToken.None);
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.AreEqual(4, Convert.ToInt32(await version.ExecuteScalarAsync()));
        }

        await using var resumed = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        Assert.IsNotNull(await resumed.GetCircleAsync(circleId));
        Assert.AreEqual(0, (await resumed.ListContributionsAsync(circleId)).Count);
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
                DROP TABLE circle_files_lifecycle_audit_events;
                DROP TABLE circle_files_access_grant_revocations;
                DROP TABLE circle_files_provider_credentials;
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
    public async Task Version_five_migration_failure_rolls_back_and_restart_can_retry()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
        }

        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using (var downgrade = connection.CreateCommand())
            {
                downgrade.CommandText =
                    """
                    DROP TABLE circle_files_lifecycle_audit_events;
                    DROP TABLE circle_files_access_grant_revocations;
                    DROP TABLE circle_files_provider_credentials;
                    DROP TABLE circle_files_access_grants;
                    DROP TABLE circle_files_contributions;
                    PRAGMA user_version = 5;
                    """;
                await downgrade.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => SqliteLocalStateStore.ExecuteV5ToV6MigrationAsync(
                    connection,
                    _ => Task.FromException(
                        new InvalidOperationException("Injected before-commit failure.")),
                    CancellationToken.None));

            using var state = connection.CreateCommand();
            state.CommandText =
                """
                SELECT
                    (SELECT user_version FROM pragma_user_version),
                    (SELECT COUNT(*) FROM sqlite_master
                     WHERE type = 'table' AND name LIKE 'circle_files_%');
                """;
            await using var reader = await state.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(5, reader.GetInt32(0));
            Assert.AreEqual(0, reader.GetInt32(1));
        }

        await using var retried = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        Assert.AreEqual(
            0,
            (await retried.ListContributionsAsync(new CircleId(Guid.NewGuid()))).Count);
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

    private static async Task<int> ReadVersionAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
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
