using System.Security.Cryptography;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    private const string CircleMessageSchemaSql =
        """
        CREATE TABLE local_circle_members (
            circle_id TEXT NOT NULL PRIMARY KEY,
            member_id TEXT NOT NULL UNIQUE,
            key_algorithm TEXT NOT NULL,
            key_id TEXT NOT NULL,
            public_key_spki BLOB NOT NULL,
            private_key_scheme TEXT NOT NULL,
            protected_private_key BLOB NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(member_id) ON DELETE CASCADE
        );

        CREATE TABLE circle_member_nodes (
            circle_id TEXT NOT NULL,
            member_id TEXT NOT NULL,
            node_id TEXT NOT NULL,
            PRIMARY KEY (circle_id, member_id, node_id),
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(member_id) ON DELETE CASCADE,
            FOREIGN KEY (node_id) REFERENCES nodes(node_id)
        );

        CREATE TABLE outgoing_circle_messages (
            message_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            author_member_id TEXT NOT NULL,
            author_node_id TEXT NOT NULL,
            text TEXT NOT NULL,
            authored_at_utc TEXT NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (author_member_id) REFERENCES members(member_id),
            FOREIGN KEY (author_node_id) REFERENCES nodes(node_id)
        );

        CREATE TABLE circle_messages (
            message_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            author_member_id TEXT NOT NULL,
            author_node_id TEXT NOT NULL,
            text TEXT NOT NULL,
            authored_at_utc TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            accepted_at_utc TEXT NOT NULL,
            request_sha256 BLOB NOT NULL,
            encoded_signed_message BLOB NOT NULL,
            encoded_receipt BLOB NOT NULL,
            UNIQUE (circle_id, sequence),
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (author_member_id) REFERENCES members(member_id),
            FOREIGN KEY (author_node_id) REFERENCES nodes(node_id)
        );
        """;

    public Task<PreparedOutgoingCircleMessage> PrepareOutgoingCircleMessageAsync(
        CircleMessageId messageId,
        CircleId circleId,
        string text,
        DateTimeOffset authoredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidatePreparedMessage(text, authoredAtUtc);
        return ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadPreparedMessageAsync(messageId, transaction, token)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    if (existing.CircleId != circleId
                        || !string.Equals(existing.Text, text, StringComparison.Ordinal))
                    {
                        throw new LocalStateConflictException(
                            "message_request_conflict",
                            "The message request identity was already used for different content.");
                    }

                    return existing;
                }

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO outgoing_circle_messages (
                        message_id, circle_id, author_member_id, author_node_id, text,
                        authored_at_utc)
                    SELECT $message_id, l.circle_id, l.member_id, n.node_id, $text,
                           $authored_at
                    FROM local_circle_members l CROSS JOIN local_node n
                    WHERE l.circle_id = $circle_id;
                    """;
                command.Parameters.AddWithValue("$message_id", messageId.ToString());
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                command.Parameters.AddWithValue("$text", text);
                command.Parameters.AddWithValue("$authored_at", Format(authoredAtUtc));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                {
                    throw new LocalStateException(
                        "local_circle_member_not_found",
                        "This Node has no local Member identity for the Circle.");
                }

                var prepared = await ReadPreparedMessageAsync(messageId, transaction, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "message_prepare_failed",
                        "The outgoing Circle message could not be prepared.");
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return prepared;
            },
            cancellationToken);
    }

    public Task<LocalCircleMessageAuthor?> GetLocalCircleMessageAuthorAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<LocalCircleMessageAuthor?>(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT l.member_id, l.key_algorithm, l.key_id, l.public_key_spki,
                           n.node_id
                    FROM local_circle_members l
                    CROSS JOIN local_node n
                    WHERE l.circle_id = $circle_id;
                    """;
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false)
                    ? new LocalCircleMessageAuthor(
                        circleId,
                        new MemberId(Guid.Parse(reader.GetString(0))),
                        ReadCredential(
                            IdentityKeyRole.Member,
                            reader.GetString(1),
                            reader.GetString(2),
                            (byte[])reader.GetValue(3)),
                        new NodeId(Guid.Parse(reader.GetString(4))))
                    : null;
            },
            cancellationToken);

    public Task<CircleMessageAuthorState?> GetCircleMessageAuthorAsync(
        CircleId circleId,
        MemberId memberId,
        NodeId nodeId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<CircleMessageAuthorState?>(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT m.key_algorithm, m.key_id, m.public_key_spki,
                           n.node_key_algorithm, n.node_key_id, n.node_public_key_spki,
                           CASE WHEN a.node_id IS NULL THEN 0 ELSE 1 END
                    FROM circle_member_credentials m
                    INNER JOIN circle_node_credentials n
                        ON n.circle_id = m.circle_id AND n.node_id = $node_id
                    LEFT JOIN circle_member_nodes a
                        ON a.circle_id = m.circle_id
                       AND a.member_id = m.member_id
                       AND a.node_id = n.node_id
                    WHERE m.circle_id = $circle_id AND m.member_id = $member_id;
                    """;
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                command.Parameters.AddWithValue("$member_id", memberId.ToString());
                command.Parameters.AddWithValue("$node_id", nodeId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false)
                    ? new CircleMessageAuthorState(
                        circleId,
                        memberId,
                        ReadCredential(
                            IdentityKeyRole.Member,
                            reader.GetString(0),
                            reader.GetString(1),
                            (byte[])reader.GetValue(2)),
                        nodeId,
                        ReadCredential(
                            IdentityKeyRole.Node,
                            reader.GetString(3),
                            reader.GetString(4),
                            (byte[])reader.GetValue(5)),
                        reader.GetInt32(6) == 1)
                    : null;
            },
            cancellationToken);

    public Task<byte[]> SignWithLocalCircleMemberAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadLocalCircleMemberPrivateIdentityAsync(circleId, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "local_circle_member_not_found",
                        "This Node has no local Member identity for the Circle.");
                using var key = OpenPrivateKey(stored);
                return IdentityCryptography.Sign(data.Span, key);
            },
            cancellationToken);

    public Task<long> GetNextCircleMessageSequenceAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT CASE
                        WHEN EXISTS (SELECT 1 FROM circles WHERE circle_id = $circle_id)
                        THEN COALESCE(MAX(sequence), 0) + 1
                        ELSE NULL
                    END
                    FROM circle_messages WHERE circle_id = $circle_id;
                    """;
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                return value is long sequence
                    ? sequence
                    : throw new LocalStateException(
                        "circle_not_found",
                        "The requested Circle is not known to this Node.");
            },
            cancellationToken);

    public Task<CircleMessageCommitResult> CommitCircleMessageAsync(
        CircleMessageCommit commit,
        CancellationToken cancellationToken = default)
    {
        ValidateMessageCommit(commit);
        return ExecuteLockedAsync<CircleMessageCommitResult>(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadMessageCommitAsync(
                    commit.Message.Id,
                    transaction,
                    token).ConfigureAwait(false);
                if (existing is not null)
                {
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return CryptographicOperations.FixedTimeEquals(
                            existing.Value.Digest,
                            commit.RequestSha256)
                        ? new(
                            CircleMessageCommitStatus.IdempotentRetry,
                            existing.Value.Message,
                            existing.Value.Receipt)
                        : new(CircleMessageCommitStatus.Conflict, null, null);
                }

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO circle_messages (
                        message_id, circle_id, author_member_id, author_node_id, text,
                        authored_at_utc, sequence, accepted_at_utc, request_sha256,
                        encoded_signed_message, encoded_receipt)
                    SELECT $message_id, $circle_id, $member_id, $node_id, $text,
                           $authored_at, $sequence, $accepted_at, $digest, $message, $receipt
                    WHERE EXISTS (
                        SELECT 1 FROM circle_member_nodes
                        WHERE circle_id = $circle_id
                          AND member_id = $member_id
                          AND node_id = $node_id);
                    """;
                AddMessageParameters(command, commit);
                try
                {
                    if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    {
                        throw new LocalStateException(
                            "unauthorized_message_author",
                            "The message author is not authorized for this Circle Node.");
                    }

                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return new(
                        CircleMessageCommitStatus.Accepted,
                        commit.Message,
                        commit.EncodedReceipt.ToArray());
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return new(CircleMessageCommitStatus.Conflict, null, null);
                }
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<PersistedCircleMessage>> ListCircleMessagesAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<IReadOnlyList<PersistedCircleMessage>>(
            async token =>
            {
                var messages = new List<PersistedCircleMessage>();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT message_id, author_member_id, author_node_id, text,
                           authored_at_utc, sequence, accepted_at_utc
                    FROM circle_messages
                    WHERE circle_id = $circle_id
                    ORDER BY sequence;
                    """;
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    messages.Add(new PersistedCircleMessage(
                        new CircleMessageId(Guid.Parse(reader.GetString(0))),
                        circleId,
                        new MemberId(Guid.Parse(reader.GetString(1))),
                        new NodeId(Guid.Parse(reader.GetString(2))),
                        reader.GetString(3),
                        ParseTimestamp(reader.GetString(4)),
                        reader.GetInt64(5),
                        ParseTimestamp(reader.GetString(6))));
                }

                return messages;
            },
            cancellationToken);

    private async Task InsertCreatedLocalMessageAuthorAsync(
        CircleId circleId,
        MemberId memberId,
        NodeId nodeId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var material = GeneratePrivateIdentity(IdentityKeyRole.Member, privateMaterialProtector);
        try
        {
            await InsertLocalCircleMemberAsync(
                circleId,
                memberId,
                material,
                transaction,
                cancellationToken).ConfigureAwait(false);
            await InsertMemberCredentialAsync(
                circleId,
                memberId,
                material.Credential,
                transaction,
                cancellationToken).ConfigureAwait(false);
            await InsertMemberNodeAuthorizationAsync(
                circleId,
                memberId,
                nodeId,
                transaction,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.ProtectedPrivateKey);
        }
    }

    private async Task InsertLocalCircleMemberFromAdmissionAsync(
        CircleId circleId,
        MemberId memberId,
        InvitationId invitationId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO local_circle_members (
                circle_id, member_id, key_algorithm, key_id, public_key_spki,
                private_key_scheme, protected_private_key)
            SELECT $circle_id, $member_id, member_key_algorithm, member_key_id,
                   member_public_key_spki, member_private_key_scheme,
                   member_protected_private_key
            FROM admission_attempts WHERE invitation_id = $invitation_id;
            """;
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        command.Parameters.AddWithValue("$member_id", memberId.ToString());
        command.Parameters.AddWithValue("$invitation_id", invitationId.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new LocalStateException(
                "admission_attempt_not_found",
                "The local admission Member identity is missing.");
        }
    }

    private async Task InsertLocalCircleMemberAsync(
        CircleId circleId,
        MemberId memberId,
        GeneratedPrivateIdentity material,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO local_circle_members (
                circle_id, member_id, key_algorithm, key_id, public_key_spki,
                private_key_scheme, protected_private_key)
            VALUES ($circle_id, $member_id, $algorithm, $key_id, $spki, $scheme, $private_key);
            """;
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        command.Parameters.AddWithValue("$member_id", memberId.ToString());
        command.Parameters.AddWithValue("$algorithm", material.Credential.Algorithm);
        command.Parameters.AddWithValue("$key_id", material.Credential.KeyId);
        command.Parameters.AddWithValue("$spki", material.Credential.SubjectPublicKeyInfo);
        command.Parameters.AddWithValue("$scheme", material.ProtectionScheme);
        command.Parameters.AddWithValue("$private_key", material.ProtectedPrivateKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertMemberNodeAuthorizationAsync(
        CircleId circleId,
        MemberId memberId,
        NodeId nodeId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO circle_member_nodes (circle_id, member_id, node_id)
            VALUES ($circle_id, $member_id, $node_id)
            ON CONFLICT(circle_id, member_id, node_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        command.Parameters.AddWithValue("$member_id", memberId.ToString());
        command.Parameters.AddWithValue("$node_id", nodeId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoredPrivateIdentity?> ReadLocalCircleMemberPrivateIdentityAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT key_algorithm, key_id, public_key_spki,
                   private_key_scheme, protected_private_key
            FROM local_circle_members WHERE circle_id = $circle_id;
            """;
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredPrivateIdentity(
                ReadCredential(
                    IdentityKeyRole.Member,
                    reader.GetString(0),
                    reader.GetString(1),
                    (byte[])reader.GetValue(2)),
                reader.GetString(3),
                (byte[])reader.GetValue(4))
            : null;
    }

    private async Task<(PersistedCircleMessage Message, byte[] Digest, byte[] Receipt)?>
        ReadMessageCommitAsync(
            CircleMessageId messageId,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT circle_id, author_member_id, author_node_id, text, authored_at_utc,
                   sequence, accepted_at_utc, request_sha256, encoded_receipt
            FROM circle_messages WHERE message_id = $message_id;
            """;
        command.Parameters.AddWithValue("$message_id", messageId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (
            new PersistedCircleMessage(
                messageId,
                new CircleId(Guid.Parse(reader.GetString(0))),
                new MemberId(Guid.Parse(reader.GetString(1))),
                new NodeId(Guid.Parse(reader.GetString(2))),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)),
                reader.GetInt64(5),
                ParseTimestamp(reader.GetString(6))),
            (byte[])reader.GetValue(7),
            (byte[])reader.GetValue(8));
    }

    private async Task<PreparedOutgoingCircleMessage?> ReadPreparedMessageAsync(
        CircleMessageId messageId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT circle_id, author_member_id, author_node_id, text, authored_at_utc
            FROM outgoing_circle_messages WHERE message_id = $message_id;
            """;
        command.Parameters.AddWithValue("$message_id", messageId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new PreparedOutgoingCircleMessage(
                messageId,
                new CircleId(Guid.Parse(reader.GetString(0))),
                new MemberId(Guid.Parse(reader.GetString(1))),
                new NodeId(Guid.Parse(reader.GetString(2))),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)))
            : null;
    }

    private static void AddMessageParameters(SqliteCommand command, CircleMessageCommit commit)
    {
        command.Parameters.AddWithValue("$message_id", commit.Message.Id.ToString());
        command.Parameters.AddWithValue("$circle_id", commit.Message.CircleId.ToString());
        command.Parameters.AddWithValue("$member_id", commit.Message.AuthorMemberId.ToString());
        command.Parameters.AddWithValue("$node_id", commit.Message.AuthorNodeId.ToString());
        command.Parameters.AddWithValue("$text", commit.Message.Text);
        command.Parameters.AddWithValue("$authored_at", Format(commit.Message.AuthoredAtUtc));
        command.Parameters.AddWithValue("$sequence", commit.Message.Sequence);
        command.Parameters.AddWithValue("$accepted_at", Format(commit.Message.AcceptedAtUtc));
        command.Parameters.AddWithValue("$digest", commit.RequestSha256);
        command.Parameters.AddWithValue("$message", commit.EncodedSignedMessage);
        command.Parameters.AddWithValue("$receipt", commit.EncodedReceipt);
    }

    private static void ValidateMessageCommit(CircleMessageCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.Message is null
            || string.IsNullOrWhiteSpace(commit.Message.Text)
            || System.Text.Encoding.UTF8.GetByteCount(commit.Message.Text) > 4096
            || commit.Message.Sequence <= 0
            || commit.Message.AuthoredAtUtc.Offset != TimeSpan.Zero
            || commit.Message.AcceptedAtUtc.Offset != TimeSpan.Zero
            || commit.RequestSha256 is not { Length: SHA256.HashSizeInBytes }
            || commit.EncodedSignedMessage is not { Length: > 0 and <= 64 * 1024 }
            || commit.EncodedReceipt is not { Length: > 0 and <= 64 * 1024 })
        {
            throw new ArgumentException("The Circle message commit is invalid.", nameof(commit));
        }
    }

    private static void ValidatePreparedMessage(string text, DateTimeOffset authoredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(text)
            || System.Text.Encoding.UTF8.GetByteCount(text) > 4096
            || authoredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The outgoing Circle message is invalid.", nameof(text));
        }
    }

    private static async Task MigrateV4ToV5Async(
        SqliteConnection connection,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = CircleMessageSchemaSql;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var creators = new List<(CircleId CircleId, MemberId MemberId)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT c.circle_id, m.member_id
                FROM circle_creations c
                INNER JOIN members m ON m.circle_id = c.circle_id AND m.role = 1;
                """;
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                creators.Add((
                    new CircleId(Guid.Parse(reader.GetString(0))),
                    new MemberId(Guid.Parse(reader.GetString(1)))));
            }
        }

        foreach (var creator in creators)
        {
            var material = GeneratePrivateIdentity(IdentityKeyRole.Member, protector);
            try
            {
                using var local = connection.CreateCommand();
                local.Transaction = transaction;
                local.CommandText =
                    """
                    INSERT INTO local_circle_members (
                        circle_id, member_id, key_algorithm, key_id, public_key_spki,
                        private_key_scheme, protected_private_key)
                    VALUES ($circle_id, $member_id, $algorithm, $key_id, $spki, $scheme, $private);
                    INSERT INTO circle_member_credentials (
                        circle_id, member_id, key_algorithm, key_id, public_key_spki)
                    VALUES ($circle_id, $member_id, $algorithm, $key_id, $spki);
                    """;
                local.Parameters.AddWithValue("$circle_id", creator.CircleId.ToString());
                local.Parameters.AddWithValue("$member_id", creator.MemberId.ToString());
                local.Parameters.AddWithValue("$algorithm", material.Credential.Algorithm);
                local.Parameters.AddWithValue("$key_id", material.Credential.KeyId);
                local.Parameters.AddWithValue("$spki", material.Credential.SubjectPublicKeyInfo);
                local.Parameters.AddWithValue("$scheme", material.ProtectionScheme);
                local.Parameters.AddWithValue("$private", material.ProtectedPrivateKey);
                await local.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(material.ProtectedPrivateKey);
            }
        }

        using (var joined = connection.CreateCommand())
        {
            joined.Transaction = transaction;
            joined.CommandText =
                """
                INSERT OR IGNORE INTO local_circle_members (
                    circle_id, member_id, key_algorithm, key_id, public_key_spki,
                    private_key_scheme, protected_private_key)
                SELECT a.circle_id, a.member_id, a.member_key_algorithm, a.member_key_id,
                       a.member_public_key_spki, a.member_private_key_scheme,
                       a.member_protected_private_key
                FROM admission_attempts a
                WHERE a.status = 1;

                INSERT INTO circle_member_nodes (circle_id, member_id, node_id)
                SELECT c.circle_id, m.member_id, c.node_id
                FROM circle_creations c
                INNER JOIN members m ON m.circle_id = c.circle_id AND m.role = 1;

                INSERT OR IGNORE INTO circle_member_nodes (circle_id, member_id, node_id)
                SELECT circle_id, member_id, node_id FROM circle_admissions
                ;

                INSERT OR IGNORE INTO circle_member_nodes (circle_id, member_id, node_id)
                SELECT a.circle_id, a.member_id, n.node_id
                FROM admission_attempts a CROSS JOIN local_node n
                WHERE a.status = 1;
                """;
            await joined.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using var version = connection.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = "PRAGMA user_version = 5;";
        await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCircleMessageExpectedTables(IDictionary<string, TableSchema> tables)
    {
        tables["local_circle_members"] = new(
            [
                new("circle_id", "TEXT", 1), new("member_id", "TEXT", 0),
                new("key_algorithm", "TEXT", 0), new("key_id", "TEXT", 0),
                new("public_key_spki", "BLOB", 0), new("private_key_scheme", "TEXT", 0),
                new("protected_private_key", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "member_id", "member_id", "CASCADE"),
            ]);
        tables["circle_member_nodes"] = new(
            [
                new("circle_id", "TEXT", 1), new("member_id", "TEXT", 2),
                new("node_id", "TEXT", 3),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "member_id", "member_id", "CASCADE"),
                new("nodes", "node_id", "node_id", "NO ACTION"),
            ]);
        tables["outgoing_circle_messages"] = new(
            [
                new("message_id", "TEXT", 1), new("circle_id", "TEXT", 0),
                new("author_member_id", "TEXT", 0), new("author_node_id", "TEXT", 0),
                new("text", "TEXT", 0), new("authored_at_utc", "TEXT", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "author_member_id", "member_id", "NO ACTION"),
                new("nodes", "author_node_id", "node_id", "NO ACTION"),
            ]);
        tables["circle_messages"] = new(
            [
                new("message_id", "TEXT", 1), new("circle_id", "TEXT", 0),
                new("author_member_id", "TEXT", 0), new("author_node_id", "TEXT", 0),
                new("text", "TEXT", 0), new("authored_at_utc", "TEXT", 0),
                new("sequence", "INTEGER", 0), new("accepted_at_utc", "TEXT", 0),
                new("request_sha256", "BLOB", 0),
                new("encoded_signed_message", "BLOB", 0), new("encoded_receipt", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "author_member_id", "member_id", "NO ACTION"),
                new("nodes", "author_node_id", "node_id", "NO ACTION"),
            ]);
    }
}
