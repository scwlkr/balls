using System.Security.Cryptography;
using Balls.Core;
using Balls.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite.Tests;

[TestClass]
public sealed class SqliteLocalStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Separate_data_directories_create_distinct_local_node_identities()
    {
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        await using var firstStore = await SqliteLocalStateStore.OpenAsync(firstDirectory.Path);
        await using var secondStore = await SqliteLocalStateStore.OpenAsync(secondDirectory.Path);
        var firstApplication = new CircleApplication(firstStore, TimeProvider.System, "Alice-PC");
        var secondApplication = new CircleApplication(secondStore, TimeProvider.System, "Alice-PC");

        var firstNode = await firstApplication.GetLocalNodeAsync();
        var secondNode = await secondApplication.GetLocalNodeAsync();

        Assert.AreNotEqual(firstNode.Id, secondNode.Id);
    }

    [TestMethod]
    public async Task Multiple_circles_keep_their_members_and_nodes_scoped()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await SqliteLocalStateStore.OpenAsync(directory.Path);
        var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");

        var first = await application.CreateCircleAsync(
            new CreateCircleCommand(new CreationRequestId(Guid.CreateVersion7()), "Family", "Alice"));
        var second = await application.CreateCircleAsync(
            new CreateCircleCommand(new CreationRequestId(Guid.CreateVersion7()), "Workshop", "Morgan"));
        var circles = await application.ListCirclesAsync();

        Assert.AreEqual(2, circles.Count);
        Assert.AreNotEqual(first.Circle.Id, second.Circle.Id);
        Assert.IsTrue(first.Members.All(member => member.CircleId == first.Circle.Id));
        Assert.IsTrue(first.Nodes.All(node => node.CircleId == first.Circle.Id));
        Assert.IsTrue(second.Members.All(member => member.CircleId == second.Circle.Id));
        Assert.IsTrue(second.Nodes.All(node => node.CircleId == second.Circle.Id));
        Assert.AreEqual("Alice", first.Members.Single().DisplayName);
        Assert.AreEqual("Morgan", second.Members.Single().DisplayName);
    }

    [TestMethod]
    public async Task Concurrent_unique_circle_creations_are_serialized_without_data_loss()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await SqliteLocalStateStore.OpenAsync(directory.Path);
        var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");

        var creations = Enumerable.Range(1, 16).Select(
            index => application.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    $"Circle {index}",
                    "Alice")));
        var created = await Task.WhenAll(creations);
        var persisted = await application.ListCirclesAsync();

        Assert.AreEqual(16, created.Select(circle => circle.Circle.Id).Distinct().Count());
        Assert.AreEqual(16, persisted.Count);
    }

    [TestMethod]
    public async Task Reopening_the_store_preserves_node_circle_member_and_enrollment_identity()
    {
        using var directory = new TemporaryDirectory();
        var node = new NodeIdentity(
            new NodeId(Guid.Parse("0198c2d8-b000-7000-8000-000000000010")),
            "Alice-PC",
            Now);
        var expected = CreateCircle(
            node,
            "0198c2d8-b000-7000-8000-000000000012",
            "0198c2d8-b000-7000-8000-000000000013");

        await using (var firstStore = await SqliteLocalStateStore.OpenAsync(directory.Path))
        {
            await firstStore.SaveNodeAsync(node);
            var created = await firstStore.CreateCircleAsync(
                new CreationRequestId(Guid.Parse("0198c2d8-b000-7000-8000-000000000011")),
                expected);

            AssertCircle(expected, created);
        }

        await using var reopenedStore = await SqliteLocalStateStore.OpenAsync(directory.Path);
        var reloadedNode = await reopenedStore.GetNodeAsync();
        var reloadedCircle = await reopenedStore.GetCircleAsync(expected.Circle.Id);
        var circles = await reopenedStore.ListCirclesAsync();

        Assert.AreEqual(node, reloadedNode);
        Assert.IsNotNull(reloadedCircle);
        AssertCircle(expected, reloadedCircle);
        Assert.AreEqual(1, circles.Count);
        AssertCircle(expected, circles[0]);
    }

    [TestMethod]
    public async Task Retrying_the_same_creation_request_returns_the_original_circle_without_a_duplicate()
    {
        using var directory = new TemporaryDirectory();
        var node = new NodeIdentity(
            new NodeId(Guid.Parse("0198c2d8-b000-7000-8000-000000000020")),
            "Alice-PC",
            Now);
        var requestId = new CreationRequestId(
            Guid.Parse("0198c2d8-b000-7000-8000-000000000021"));
        var original = CreateCircle(
            node,
            "0198c2d8-b000-7000-8000-000000000022",
            "0198c2d8-b000-7000-8000-000000000023");
        var retryPayload = CreateCircle(
            node,
            "0198c2d8-b000-7000-8000-000000000024",
            "0198c2d8-b000-7000-8000-000000000025");

        await using var store = await SqliteLocalStateStore.OpenAsync(directory.Path);
        await store.SaveNodeAsync(node);
        var firstResult = await store.CreateCircleAsync(requestId, original);
        var retryResult = await store.CreateCircleAsync(requestId, retryPayload);
        var circles = await store.ListCirclesAsync();

        AssertCircle(original, firstResult);
        AssertCircle(original, retryResult);
        Assert.AreEqual(1, circles.Count);
        AssertCircle(original, circles[0]);
    }

    [TestMethod]
    public async Task Reusing_a_creation_request_for_different_input_is_rejected_without_changing_state()
    {
        using var directory = new TemporaryDirectory();
        var node = new NodeIdentity(
            new NodeId(Guid.Parse("0198c2d8-b000-7000-8000-000000000030")),
            "Alice-PC",
            Now);
        var requestId = new CreationRequestId(
            Guid.Parse("0198c2d8-b000-7000-8000-000000000031"));
        var original = CreateCircle(
            node,
            "0198c2d8-b000-7000-8000-000000000032",
            "0198c2d8-b000-7000-8000-000000000033");
        var conflictingPayload = CreateCircle(
            node,
            "0198c2d8-b000-7000-8000-000000000034",
            "0198c2d8-b000-7000-8000-000000000035",
            "Example Lab");

        await using var store = await SqliteLocalStateStore.OpenAsync(directory.Path);
        await store.SaveNodeAsync(node);
        await store.CreateCircleAsync(requestId, original);

        var error = await Assert.ThrowsExactlyAsync<LocalStateConflictException>(
            () => store.CreateCircleAsync(requestId, conflictingPayload));
        var circles = await store.ListCirclesAsync();

        Assert.AreEqual("creation_request_conflict", error.Code);
        Assert.AreEqual(1, circles.Count);
        AssertCircle(original, circles[0]);
    }

    [TestMethod]
    public async Task Circle_enrollment_can_reference_a_node_other_than_the_local_node()
    {
        using var directory = new TemporaryDirectory();
        var localNode = new NodeIdentity(
            new NodeId(Guid.Parse("0198c2d8-b000-7000-8000-000000000040")),
            "Alice-PC",
            Now);
        var remoteNode = new NodeIdentity(
            new NodeId(Guid.Parse("0198c2d8-b000-7000-8000-000000000041")),
            "Workshop-PC",
            Now);
        var circle = CreateCircle(
            remoteNode,
            "0198c2d8-b000-7000-8000-000000000042",
            "0198c2d8-b000-7000-8000-000000000043");

        await using var store = await SqliteLocalStateStore.OpenAsync(directory.Path);
        await store.SaveNodeAsync(localNode);

        var created = await store.CreateCircleAsync(
            new CreationRequestId(Guid.Parse("0198c2d8-b000-7000-8000-000000000044")),
            circle);

        AssertCircle(circle, created);
        Assert.AreEqual(localNode, await store.GetNodeAsync());
    }

    [TestMethod]
    public async Task Failed_circle_creation_rolls_back_every_row()
    {
        using var directory = new TemporaryDirectory();
        var node = new NodeIdentity(
            new NodeId(Guid.Parse("0198c2d8-b000-7000-8000-000000000050")),
            "Alice-PC",
            Now);
        var circleId = new CircleId(Guid.Parse("0198c2d8-b000-7000-8000-000000000051"));
        var invalidMemberCircleId = new CircleId(
            Guid.Parse("0198c2d8-b000-7000-8000-000000000052"));
        var invalid = new CircleDetails(
            new Circle(circleId, "Example Studio", Now),
            [
                new Member(
                    new MemberId(Guid.Parse("0198c2d8-b000-7000-8000-000000000053")),
                    invalidMemberCircleId,
                    "Alice",
                    MemberRole.Owner,
                    Now),
            ],
            [new CircleNode(circleId, node.Id, node.DisplayName, Now)]);

        await using var store = await SqliteLocalStateStore.OpenAsync(directory.Path);
        await store.SaveNodeAsync(node);

        await Assert.ThrowsExactlyAsync<SqliteException>(
            () => store.CreateCircleAsync(
                new CreationRequestId(Guid.Parse("0198c2d8-b000-7000-8000-000000000054")),
                invalid));

        Assert.AreEqual(0, (await store.ListCirclesAsync()).Count);
    }

    [TestMethod]
    public async Task A_newer_schema_version_fails_closed_without_replacing_or_downgrading_state()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                PRAGMA journal_mode = DELETE;
                PRAGMA application_id = {SqliteLocalStateStore.ApplicationId};
                PRAGMA user_version = 2;
                CREATE TABLE future_state (value TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(databasePath));

        UnsupportedLocalStateSchemaException? error = null;
        try
        {
            await using var unexpectedStore = await SqliteLocalStateStore.OpenAsync(directory.Path);
            Assert.Fail("A newer state schema must not be opened or migrated.");
        }
        catch (UnsupportedLocalStateSchemaException exception)
        {
            error = exception;
        }

        Assert.IsNotNull(error);
        Assert.AreEqual(2, error.FoundVersion);
        Assert.AreEqual(SqliteLocalStateStore.CurrentSchemaVersion, error.SupportedVersion);

        await using var verificationConnection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await verificationConnection.OpenAsync();
        using var versionCommand = verificationConnection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync());
        Assert.AreEqual(2, version);
        using var journalModeCommand = verificationConnection.CreateCommand();
        journalModeCommand.CommandText = "PRAGMA journal_mode;";
        Assert.AreEqual("delete", (string)(await journalModeCommand.ExecuteScalarAsync())!);
        await verificationConnection.CloseAsync();
        CollectionAssert.AreEqual(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(databasePath)));
        Assert.IsFalse(File.Exists(databasePath + "-wal"));
        Assert.IsFalse(File.Exists(databasePath + "-shm"));
    }

    [TestMethod]
    public async Task Corrupt_state_fails_with_a_safe_error_without_replacing_the_database()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        const string corruptContent = "this is not a SQLite database";
        await File.WriteAllTextAsync(databasePath, corruptContent);

        var error = await Assert.ThrowsExactlyAsync<LocalStateOpenException>(
            () => SqliteLocalStateStore.OpenAsync(directory.Path));

        Assert.AreEqual("invalid_local_state", error.Code);
        Assert.AreEqual(corruptContent, await File.ReadAllTextAsync(databasePath));
    }

    [TestMethod]
    public async Task A_schema_version_without_the_required_tables_fails_closed()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                PRAGMA application_id = {SqliteLocalStateStore.ApplicationId};
                PRAGMA user_version = {SqliteLocalStateStore.CurrentSchemaVersion};
                """;
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => SqliteLocalStateStore.OpenAsync(directory.Path));

        Assert.AreEqual("invalid_state_schema", error.Code);
        await using var verification = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await verification.OpenAsync();
        using var tableCountCommand = verification.CreateCommand();
        tableCountCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        Assert.AreEqual(0L, (long)(await tableCountCommand.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task An_unrelated_sqlite_database_is_rejected_without_modification()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE unrelated_app_data (important_value TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        var original = await File.ReadAllBytesAsync(databasePath);

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => SqliteLocalStateStore.OpenAsync(directory.Path));

        Assert.AreEqual("foreign_local_state", error.Code);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(databasePath));
        Assert.IsFalse(File.Exists(databasePath + "-wal"));
        Assert.IsFalse(File.Exists(databasePath + "-shm"));
    }

    [TestMethod]
    public async Task A_foreign_database_with_only_a_view_is_not_mistaken_for_an_empty_database()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE VIEW unrelated_view AS SELECT 1 AS important_value;";
            await command.ExecuteNonQueryAsync();
        }

        var original = await File.ReadAllBytesAsync(databasePath);
        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => SqliteLocalStateStore.OpenAsync(directory.Path));

        Assert.AreEqual("foreign_local_state", error.Code);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(databasePath));
    }

    [TestMethod]
    public async Task A_schema_with_expected_column_names_but_missing_constraints_is_rejected()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE nodes (node_id TEXT);
                CREATE TABLE local_node (
                    singleton_id INTEGER, node_id TEXT, display_name TEXT, created_at_utc TEXT);
                CREATE TABLE circles (circle_id TEXT, name TEXT, created_at_utc TEXT);
                CREATE TABLE members (
                    member_id TEXT, circle_id TEXT, display_name TEXT, role INTEGER, joined_at_utc TEXT);
                CREATE TABLE circle_nodes (
                    circle_id TEXT, node_id TEXT, display_name TEXT, joined_at_utc TEXT);
                CREATE TABLE circle_creations (
                    request_id TEXT, circle_id TEXT, circle_name TEXT,
                    owner_display_name TEXT, node_id TEXT);
                PRAGMA application_id = {SqliteLocalStateStore.ApplicationId};
                PRAGMA user_version = {SqliteLocalStateStore.CurrentSchemaVersion};
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteLocalStateStore? unexpectedStore = null;
        LocalStateException? error = null;
        try
        {
            unexpectedStore = await SqliteLocalStateStore.OpenAsync(directory.Path);
            Assert.Fail("A schema without its required constraints must not open.");
        }
        catch (LocalStateException exception)
        {
            error = exception;
        }
        finally
        {
            if (unexpectedStore is not null)
            {
                await unexpectedStore.DisposeAsync();
            }
        }

        Assert.IsNotNull(error);
        Assert.AreEqual("invalid_state_schema", error.Code);
    }

    [TestMethod]
    public async Task Disposal_waits_for_an_in_flight_write_to_finish()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(directory.Path, "balls.db");
        var store = await SqliteLocalStateStore.OpenAsync(directory.Path);
        await using var blocker = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await blocker.OpenAsync();
        using var begin = blocker.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE;";
        await begin.ExecuteNonQueryAsync();

        var saveTask = Task.Run(
            () => store.SaveNodeAsync(
                new NodeIdentity(
                    new NodeId(Guid.Parse("0198c2d8-b000-7000-8000-000000000060")),
                    "Alice-PC",
                    Now)));
        await Task.Delay(200);

        var disposeTask = store.DisposeAsync().AsTask();
        await Task.Delay(50);

        var disposedBeforeWriteFinished = disposeTask.IsCompleted;
        using var commit = blocker.CreateCommand();
        commit.CommandText = "COMMIT;";
        await commit.ExecuteNonQueryAsync();
        await saveTask;
        await disposeTask;

        Assert.IsFalse(
            disposedBeforeWriteFinished,
            "Disposal must drain the active write first.");
    }

    private static CircleDetails CreateCircle(
        NodeIdentity node,
        string circleIdValue,
        string memberIdValue,
        string name = "Example Studio")
    {
        var circleId = new CircleId(Guid.Parse(circleIdValue));
        return new CircleDetails(
            new Circle(circleId, name, Now),
            [
                new Member(
                    new MemberId(Guid.Parse(memberIdValue)),
                    circleId,
                    "Alice",
                    MemberRole.Owner,
                    Now),
            ],
            [new CircleNode(circleId, node.Id, node.DisplayName, Now)]);
    }

    private static void AssertCircle(CircleDetails expected, CircleDetails actual)
    {
        Assert.AreEqual(expected.Circle, actual.Circle);
        CollectionAssert.AreEqual(expected.Members.ToArray(), actual.Members.ToArray());
        CollectionAssert.AreEqual(expected.Nodes.ToArray(), actual.Nodes.ToArray());
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
