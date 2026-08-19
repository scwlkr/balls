using System.Globalization;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed class SqliteLocalStateStore : ILocalStateStore, IAsyncDisposable
{
    public const int ApplicationId = 0x42414C53;
    public const int CurrentSchemaVersion = 1;

    private readonly SqliteConnection connection;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private int disposed;

    private SqliteLocalStateStore(SqliteConnection connection)
    {
        this.connection = connection;
    }

    public static async Task<SqliteLocalStateStore> OpenAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var fullDataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(fullDataDirectory);
        var databasePath = Path.Combine(fullDataDirectory, "balls.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        var connection = new SqliteConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var applicationId = await ReadPragmaIntAsync(
                connection,
                "application_id",
                cancellationToken).ConfigureAwait(false);
            var version = await ReadPragmaIntAsync(
                connection,
                "user_version",
                cancellationToken).ConfigureAwait(false);
            var userObjectCount = await CountUserSchemaObjectsAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            var isFreshDatabase = applicationId == 0 && version == 0 && userObjectCount == 0;

            if (applicationId != 0 && applicationId != ApplicationId)
            {
                throw new LocalStateException(
                    "foreign_local_state",
                    "The local state file belongs to another application and was left unchanged.");
            }

            if (applicationId == 0 && !isFreshDatabase)
            {
                throw new LocalStateException(
                    "foreign_local_state",
                    "The local state file is not an initialized Balls database and was left unchanged.");
            }

            if (version > CurrentSchemaVersion)
            {
                throw new UnsupportedLocalStateSchemaException(version, CurrentSchemaVersion);
            }

            if (!isFreshDatabase)
            {
                if (version != CurrentSchemaVersion)
                {
                    throw new LocalStateException(
                        "invalid_state_schema",
                        "The Balls local state schema is incomplete and was left unchanged.");
                }

                await ValidateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (isFreshDatabase)
            {
                await MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
                await ValidateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            return new SqliteLocalStateStore(connection);
        }
        catch (SqliteException exception)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new LocalStateOpenException(exception);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<NodeIdentity?> GetNodeAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteLockedAsync(
            token => ReadNodeAsync(transaction: null, token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveNodeAsync(
        NodeIdentity node,
        CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT INTO nodes (node_id)
                        VALUES ($node_id)
                        ON CONFLICT(node_id) DO NOTHING;

                        INSERT INTO local_node (
                            singleton_id,
                            node_id,
                            display_name,
                            created_at_utc)
                        VALUES (1, $node_id, $display_name, $created_at_utc)
                        ON CONFLICT(singleton_id) DO NOTHING;
                        """;
                    command.Parameters.AddWithValue("$node_id", node.Id.ToString());
                    command.Parameters.AddWithValue("$display_name", node.DisplayName);
                    command.Parameters.AddWithValue("$created_at_utc", Format(node.CreatedAtUtc));
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                var persisted = await ReadNodeAsync(transaction, token).ConfigureAwait(false);
                if (persisted != node)
                {
                    throw new LocalStateConflictException(
                        "node_identity_conflict",
                        "This data directory already belongs to a different Node identity.");
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CircleDetails> CreateCircleAsync(
        CreationRequestId requestId,
        CircleDetails circle,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadCreationAsync(requestId, transaction, token).ConfigureAwait(false);
                if (existing is not null)
                {
                    EnsureEquivalentRequest(existing, circle);
                    var existingCircle = await ReadCircleAsync(existing.CircleId, transaction, token)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return existingCircle
                        ?? throw new LocalStateException(
                            "inconsistent_state",
                            "A Circle creation record references missing Circle state.");
                }

                await InsertCircleAsync(circle.Circle, transaction, token).ConfigureAwait(false);
                foreach (var member in circle.Members)
                {
                    await InsertMemberAsync(member, transaction, token).ConfigureAwait(false);
                }

                foreach (var node in circle.Nodes)
                {
                    await InsertCircleNodeAsync(node, transaction, token).ConfigureAwait(false);
                }

                var founder = circle.Members.Single(member => member.Role == MemberRole.Owner);
                var enrolledNode = circle.Nodes.Single();
                await InsertCreationAsync(
                    requestId,
                    circle.Circle,
                    founder,
                    enrolledNode,
                    transaction,
                    token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return circle;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CircleDetails?> GetCircleAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteLockedAsync(
            token => ReadCircleAsync(circleId, transaction: null, token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CircleDetails>> ListCirclesAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteLockedAsync(
            async token =>
            {
                var ids = new List<CircleId>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        """
                        SELECT circle_id
                        FROM circles
                        ORDER BY created_at_utc, circle_id;
                        """;
                    await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        ids.Add(ParseCircleId(reader.GetString(0)));
                    }
                }

                var circles = new List<CircleDetails>(ids.Count);
                foreach (var id in ids)
                {
                    var circle = await ReadCircleAsync(id, transaction: null, token).ConfigureAwait(false);
                    if (circle is null)
                    {
                        throw new LocalStateException(
                            "inconsistent_state",
                            "A listed Circle disappeared from local state.");
                    }

                    circles.Add(circle);
                }

                return circles;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
            operationLock.Dispose();
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA synchronous = FULL;
            PRAGMA busy_timeout = 5000;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var journalCommand = connection.CreateCommand();
        journalCommand.CommandText = "PRAGMA journal_mode = WAL;";
        var journalMode = Convert.ToString(
            await journalCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalStateException(
                "unsupported_state_filesystem",
                "The local state filesystem does not support the required SQLite WAL mode.");
        }
    }

    private static async Task<int> ReadPragmaIntAsync(
        SqliteConnection connection,
        string pragmaName,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName};";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountUserSchemaObjectsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type IN ('table', 'view', 'trigger')
              AND name NOT GLOB 'sqlite_*';
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task ValidateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var expectedTables = new Dictionary<string, TableSchema>(StringComparer.Ordinal)
        {
            ["nodes"] = new(
                [new("node_id", "TEXT", 1)],
                []),
            ["local_node"] = new(
                [
                    new("singleton_id", "INTEGER", 1),
                    new("node_id", "TEXT", 0),
                    new("display_name", "TEXT", 0),
                    new("created_at_utc", "TEXT", 0),
                ],
                [new("nodes", "node_id", "node_id", "NO ACTION")]),
            ["circles"] = new(
                [
                    new("circle_id", "TEXT", 1),
                    new("name", "TEXT", 0),
                    new("created_at_utc", "TEXT", 0),
                ],
                []),
            ["members"] = new(
                [
                    new("member_id", "TEXT", 1),
                    new("circle_id", "TEXT", 0),
                    new("display_name", "TEXT", 0),
                    new("role", "INTEGER", 0),
                    new("joined_at_utc", "TEXT", 0),
                ],
                [new("circles", "circle_id", "circle_id", "CASCADE")]),
            ["circle_nodes"] = new(
                [
                    new("circle_id", "TEXT", 1),
                    new("node_id", "TEXT", 2),
                    new("display_name", "TEXT", 0),
                    new("joined_at_utc", "TEXT", 0),
                ],
                [
                    new("circles", "circle_id", "circle_id", "CASCADE"),
                    new("nodes", "node_id", "node_id", "NO ACTION"),
                ]),
            ["circle_creations"] = new(
                [
                    new("request_id", "TEXT", 1),
                    new("circle_id", "TEXT", 0),
                    new("circle_name", "TEXT", 0),
                    new("owner_display_name", "TEXT", 0),
                    new("node_id", "TEXT", 0),
                ],
                [
                    new("circles", "circle_id", "circle_id", "CASCADE"),
                    new("nodes", "node_id", "node_id", "NO ACTION"),
                ]),
        };

        using (var unexpectedObjectCommand = connection.CreateCommand())
        {
            unexpectedObjectCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE (type IN ('view', 'trigger') AND name NOT GLOB 'sqlite_*')
                   OR (type = 'index' AND name NOT GLOB 'sqlite_autoindex_*');
                """;
            var unexpectedObjectCount = Convert.ToInt32(
                await unexpectedObjectCommand.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (unexpectedObjectCount != 0)
            {
                ThrowInvalidSchema();
            }
        }

        var actualTableNames = new List<string>();
        using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table' AND name NOT GLOB 'sqlite_*'
                ORDER BY name;
                """;
            await using var tableReader = await tableCommand.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await tableReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                actualTableNames.Add(tableReader.GetString(0));
            }
        }

        if (!actualTableNames.SequenceEqual(
                expectedTables.Keys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            ThrowInvalidSchema();
        }

        foreach (var (table, expected) in expectedTables)
        {
            var actualColumns = new List<ColumnSchema>();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetInt32(3) != 1)
                {
                    ThrowInvalidSchema();
                }

                actualColumns.Add(
                    new ColumnSchema(
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(5)));
            }

            if (!actualColumns.SequenceEqual(expected.Columns))
            {
                ThrowInvalidSchema();
            }

            var actualForeignKeys = await ReadForeignKeysAsync(
                connection,
                table,
                cancellationToken).ConfigureAwait(false);
            if (!actualForeignKeys
                    .OrderBy(key => key.SortKey, StringComparer.Ordinal)
                    .SequenceEqual(
                        expected.ForeignKeys.OrderBy(key => key.SortKey, StringComparer.Ordinal)))
            {
                ThrowInvalidSchema();
            }
        }

        if (!await HasUniqueIndexAsync(
                connection,
                "local_node",
                ["node_id"],
                cancellationToken).ConfigureAwait(false)
            || !await HasUniqueIndexAsync(
                connection,
                "circle_creations",
                ["circle_id"],
                cancellationToken).ConfigureAwait(false)
            || !await HasRequiredSingletonCheckAsync(connection, cancellationToken)
                .ConfigureAwait(false))
        {
            ThrowInvalidSchema();
        }

        using (var integrityCommand = connection.CreateCommand())
        {
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(
                await integrityCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.Ordinal))
            {
                throw new LocalStateException(
                    "invalid_local_state",
                    "The Balls local state database failed its integrity check and was left unchanged.");
            }
        }

        using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        await using var foreignKeyReader = await foreignKeyCommand
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await foreignKeyReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LocalStateException(
                "invalid_local_state",
                "The Balls local state database contains invalid relationships and was left unchanged.");
        }
    }

    private static async Task<IReadOnlyList<ForeignKeySchema>> ReadForeignKeysAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var foreignKeys = new List<ForeignKeySchema>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            foreignKeys.Add(
                new ForeignKeySchema(
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(6)));
        }

        return foreignKeys;
    }

    private static async Task<bool> HasUniqueIndexAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> expectedColumns,
        CancellationToken cancellationToken)
    {
        var uniqueIndexNames = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({table});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetInt32(2) == 1)
                {
                    uniqueIndexNames.Add(reader.GetString(1));
                }
            }
        }

        foreach (var indexName in uniqueIndexNames)
        {
            var actualColumns = new List<string>();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_info('{indexName.Replace("'", "''")}');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                actualColumns.Add(reader.GetString(2));
            }

            if (actualColumns.SequenceEqual(expectedColumns, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasRequiredSingletonCheckAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'local_node';";
        var sql = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        return sql?.Contains("CHECK (singleton_id = 1)", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void ThrowInvalidSchema()
    {
        throw new LocalStateException(
            "invalid_state_schema",
            "The Balls local state schema is missing required structure and was left unchanged.");
    }

    private static async Task MigrateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            CREATE TABLE nodes (
                node_id TEXT NOT NULL PRIMARY KEY
            );

            CREATE TABLE local_node (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                node_id TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (node_id) REFERENCES nodes(node_id)
            );

            CREATE TABLE circles (
                circle_id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE members (
                member_id TEXT NOT NULL PRIMARY KEY,
                circle_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                role INTEGER NOT NULL,
                joined_at_utc TEXT NOT NULL,
                FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE
            );

            CREATE TABLE circle_nodes (
                circle_id TEXT NOT NULL,
                node_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                joined_at_utc TEXT NOT NULL,
                PRIMARY KEY (circle_id, node_id),
                FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
                FOREIGN KEY (node_id) REFERENCES nodes(node_id)
            );

            CREATE TABLE circle_creations (
                request_id TEXT NOT NULL PRIMARY KEY,
                circle_id TEXT NOT NULL UNIQUE,
                circle_name TEXT NOT NULL,
                owner_display_name TEXT NOT NULL,
                node_id TEXT NOT NULL,
                FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
                FOREIGN KEY (node_id) REFERENCES nodes(node_id)
            );

            PRAGMA application_id = {ApplicationId};
            PRAGMA user_version = {CurrentSchemaVersion};
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeIdentity?> ReadNodeAsync(
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT local_node.node_id, local_node.display_name, local_node.created_at_utc
            FROM local_node
            INNER JOIN nodes ON nodes.node_id = local_node.node_id
            WHERE local_node.singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new NodeIdentity(
            new NodeId(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            ParseTimestamp(reader.GetString(2)));
    }

    private async Task<CreationRecord?> ReadCreationAsync(
        CreationRequestId requestId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT circle_id, circle_name, owner_display_name, node_id
            FROM circle_creations
            WHERE request_id = $request_id;
            """;
        command.Parameters.AddWithValue("$request_id", requestId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new CreationRecord(
            ParseCircleId(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            new NodeId(Guid.Parse(reader.GetString(3))));
    }

    private async Task<CircleDetails?> ReadCircleAsync(
        CircleId circleId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        Circle? circle;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT name, created_at_utc
                FROM circles
                WHERE circle_id = $circle_id;
                """;
            command.Parameters.AddWithValue("$circle_id", circleId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            circle = new Circle(circleId, reader.GetString(0), ParseTimestamp(reader.GetString(1)));
        }

        var members = new List<Member>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT member_id, display_name, role, joined_at_utc
                FROM members
                WHERE circle_id = $circle_id
                ORDER BY joined_at_utc, member_id;
                """;
            command.Parameters.AddWithValue("$circle_id", circleId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                members.Add(new Member(
                    new MemberId(Guid.Parse(reader.GetString(0))),
                    circleId,
                    reader.GetString(1),
                    (MemberRole)reader.GetInt32(2),
                    ParseTimestamp(reader.GetString(3))));
            }
        }

        var nodes = new List<CircleNode>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT node_id, display_name, joined_at_utc
                FROM circle_nodes
                WHERE circle_id = $circle_id
                ORDER BY joined_at_utc, node_id;
                """;
            command.Parameters.AddWithValue("$circle_id", circleId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                nodes.Add(new CircleNode(
                    circleId,
                    new NodeId(Guid.Parse(reader.GetString(0))),
                    reader.GetString(1),
                    ParseTimestamp(reader.GetString(2))));
            }
        }

        return new CircleDetails(circle, members, nodes);
    }

    private async Task InsertCircleAsync(
        Circle circle,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO circles (circle_id, name, created_at_utc)
            VALUES ($circle_id, $name, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$circle_id", circle.Id.ToString());
        command.Parameters.AddWithValue("$name", circle.Name);
        command.Parameters.AddWithValue("$created_at_utc", Format(circle.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertMemberAsync(
        Member member,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO members (
                member_id,
                circle_id,
                display_name,
                role,
                joined_at_utc)
            VALUES (
                $member_id,
                $circle_id,
                $display_name,
                $role,
                $joined_at_utc);
            """;
        command.Parameters.AddWithValue("$member_id", member.Id.ToString());
        command.Parameters.AddWithValue("$circle_id", member.CircleId.ToString());
        command.Parameters.AddWithValue("$display_name", member.DisplayName);
        command.Parameters.AddWithValue("$role", (int)member.Role);
        command.Parameters.AddWithValue("$joined_at_utc", Format(member.JoinedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertCircleNodeAsync(
        CircleNode node,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO nodes (node_id)
            VALUES ($node_id)
            ON CONFLICT(node_id) DO NOTHING;

            INSERT INTO circle_nodes (circle_id, node_id, display_name, joined_at_utc)
            VALUES ($circle_id, $node_id, $display_name, $joined_at_utc);
            """;
        command.Parameters.AddWithValue("$circle_id", node.CircleId.ToString());
        command.Parameters.AddWithValue("$node_id", node.NodeId.ToString());
        command.Parameters.AddWithValue("$display_name", node.DisplayName);
        command.Parameters.AddWithValue("$joined_at_utc", Format(node.JoinedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertCreationAsync(
        CreationRequestId requestId,
        Circle circle,
        Member founder,
        CircleNode node,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO circle_creations (
                request_id,
                circle_id,
                circle_name,
                owner_display_name,
                node_id)
            VALUES (
                $request_id,
                $circle_id,
                $circle_name,
                $owner_display_name,
                $node_id);
            """;
        command.Parameters.AddWithValue("$request_id", requestId.ToString());
        command.Parameters.AddWithValue("$circle_id", circle.Id.ToString());
        command.Parameters.AddWithValue("$circle_name", circle.Name);
        command.Parameters.AddWithValue("$owner_display_name", founder.DisplayName);
        command.Parameters.AddWithValue("$node_id", node.NodeId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureEquivalentRequest(CreationRecord existing, CircleDetails requested)
    {
        var founder = requested.Members.Single(member => member.Role == MemberRole.Owner);
        var node = requested.Nodes.Single();
        if (!string.Equals(existing.CircleName, requested.Circle.Name, StringComparison.Ordinal)
            || !string.Equals(existing.OwnerDisplayName, founder.DisplayName, StringComparison.Ordinal)
            || existing.NodeId != node.NodeId)
        {
            throw new LocalStateConflictException(
                "creation_request_conflict",
                "The Circle creation request identifier was already used for different input.");
        }
    }

    private async Task ExecuteLockedAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await ExecuteLockedAsync(
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> ExecuteLockedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private static CircleId ParseCircleId(string value)
    {
        return new CircleId(Guid.Parse(value));
    }

    private static string Format(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private sealed record CreationRecord(
        CircleId CircleId,
        string CircleName,
        string OwnerDisplayName,
        NodeId NodeId);

    private sealed record ColumnSchema(
        string Name,
        string Type,
        int PrimaryKeyPosition);

    private sealed record ForeignKeySchema(
        string ReferencedTable,
        string FromColumn,
        string ToColumn,
        string OnDelete)
    {
        public string SortKey =>
            $"{ReferencedTable}\0{FromColumn}\0{ToColumn}\0{OnDelete}";
    }

    private sealed record TableSchema(
        IReadOnlyList<ColumnSchema> Columns,
        IReadOnlyList<ForeignKeySchema> ForeignKeys);

}
