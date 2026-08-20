using System.Security.Cryptography;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    private const string MessageSchemaSql =
        """
        CREATE TABLE message_drafts (
            message_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            author_member_id TEXT NOT NULL,
            author_node_id TEXT NOT NULL,
            text TEXT NOT NULL,
            authored_at_utc TEXT NOT NULL,
            status INTEGER NOT NULL,
            encoded_response BLOB NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (author_member_id) REFERENCES members(member_id),
            FOREIGN KEY (author_node_id) REFERENCES nodes(node_id)
        );

        CREATE TABLE circle_messages (
            message_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            author_member_id TEXT NOT NULL,
            author_node_id TEXT NOT NULL,
            text TEXT NOT NULL,
            authored_at_utc TEXT NOT NULL,
            accepted_at_utc TEXT NOT NULL,
            request_sha256 BLOB NOT NULL,
            encoded_response BLOB NOT NULL,
            UNIQUE (circle_id, sequence),
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (author_member_id) REFERENCES members(member_id),
            FOREIGN KEY (author_node_id) REFERENCES nodes(node_id)
        );
        """;

    public Task<MessageDraft> PrepareMessageDraftAsync(
        CircleId circleId,
        MessageId messageId,
        string text,
        DateTimeOffset authoredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateText(text);
        if (authoredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Message authored time must be UTC.", nameof(authoredAtUtc));
        }

        return ExecuteLockedAsync(
            async token =>
            {
                var author = await ReadLocalMessageAuthorAsync(circleId, token)
                    .ConfigureAwait(false);
                var existing = await ReadMessageDraftAsync(messageId, author, token)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.CircleId != circleId || existing.Text != text)
                    {
                        throw new LocalStateConflictException(
                            "message_id_conflict",
                            "This message ID already belongs to different content.");
                    }

                    return existing;
                }

                var draft = new MessageDraft(
                    messageId,
                    circleId,
                    author.MemberId,
                    author.NodeId,
                    author.MemberCredential,
                    author.NodeCredential,
                    text,
                    authoredAtUtc);
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO message_drafts (
                        message_id, circle_id, author_member_id, author_node_id,
                        text, authored_at_utc, status, encoded_response)
                    VALUES ($message, $circle, $member, $node, $text, $authored, 0, X'');
                    """;
                command.Parameters.AddWithValue("$message", messageId.ToString());
                command.Parameters.AddWithValue("$circle", circleId.ToString());
                command.Parameters.AddWithValue("$member", author.MemberId.ToString());
                command.Parameters.AddWithValue("$node", author.NodeId.ToString());
                command.Parameters.AddWithValue("$text", text);
                command.Parameters.AddWithValue("$authored", Format(authoredAtUtc));
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return draft;
            },
            cancellationToken);
    }

    public Task<byte[]> SignMessageDraftWithMemberAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadLocalMemberPrivateIdentityAsync(circleId, token)
                    .ConfigureAwait(false);
                using var key = OpenPrivateKey(stored);
                return IdentityCryptography.Sign(data.Span, key);
            },
            cancellationToken);

    public Task<PublicIdentityCredential?> GetCircleMemberCredentialAsync(
        CircleId circleId,
        MemberId memberId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT key_algorithm, key_id, public_key_spki
                    FROM circle_member_credentials
                    WHERE circle_id = $circle AND member_id = $member;
                    """;
                command.Parameters.AddWithValue("$circle", circleId.ToString());
                command.Parameters.AddWithValue("$member", memberId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false)
                    ? ReadCredential(
                        IdentityKeyRole.Member,
                        reader.GetString(0),
                        reader.GetString(1),
                        (byte[])reader.GetValue(2))
                    : null;
            },
            cancellationToken);

    public Task<MessageCommitResult?> GetAuthoritativeMessageResultAsync(
        CircleId circleId,
        MessageId messageId,
        ReadOnlyMemory<byte> requestSha256,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadStoredMessageAsync(messageId, transaction: null, token)
                    .ConfigureAwait(false);
                if (stored is null)
                {
                    return null;
                }

                return stored.Message.CircleId == circleId
                    && CryptographicOperations.FixedTimeEquals(
                        stored.RequestSha256,
                        requestSha256.Span)
                    ? new MessageCommitResult(
                        MessageCommitStatus.IdempotentRetry,
                        stored.Message,
                        stored.EncodedResponse)
                    : new MessageCommitResult(MessageCommitStatus.Conflict, null, null);
            },
            cancellationToken);

    public Task<MessageCommitResult> CommitAuthoritativeMessageAsync(
        AuthoritativeMessageCommit commit,
        CancellationToken cancellationToken = default)
    {
        ValidateCommit(commit);
        return ExecuteLockedAsync(
            token => CommitMessageAsync(commit, authoritative: true, token),
            cancellationToken);
    }

    public Task<MessageCommitResult> CommitReplicatedMessageAsync(
        AuthoritativeMessageCommit commit,
        CancellationToken cancellationToken = default)
    {
        ValidateCommit(commit);
        return ExecuteLockedAsync(
            token => CommitMessageAsync(commit, authoritative: false, token),
            cancellationToken);
    }

    public Task<CircleMessage?> GetMessageAsync(
        CircleId circleId,
        MessageId messageId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadStoredMessageAsync(messageId, transaction: null, token)
                    .ConfigureAwait(false);
                return stored?.Message.CircleId == circleId ? stored.Message : null;
            },
            cancellationToken);

    public Task<IReadOnlyList<CircleMessage>> ListMessagesAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<IReadOnlyList<CircleMessage>>(
            async token =>
            {
                var values = new List<CircleMessage>();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT message_id, sequence, author_member_id, author_node_id,
                           text, authored_at_utc, accepted_at_utc
                    FROM circle_messages
                    WHERE circle_id = $circle
                    ORDER BY sequence, message_id;
                    """;
                command.Parameters.AddWithValue("$circle", circleId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    values.Add(ReadMessage(circleId, reader));
                }

                return values;
            },
            cancellationToken);

    private async Task<MessageCommitResult> CommitMessageAsync(
        AuthoritativeMessageCommit commit,
        bool authoritative,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        var existing = await ReadStoredMessageAsync(commit.Message.Id, transaction, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return MessagesEqual(existing.Message, commit.Message)
                && CryptographicOperations.FixedTimeEquals(
                    existing.RequestSha256,
                    commit.RequestSha256)
                && CryptographicOperations.FixedTimeEquals(
                    existing.EncodedResponse,
                    commit.EncodedResponse)
                ? new MessageCommitResult(
                    MessageCommitStatus.IdempotentRetry,
                    existing.Message,
                    existing.EncodedResponse)
                : new MessageCommitResult(MessageCommitStatus.Conflict, null, null);
        }

        if (authoritative)
        {
            using var sequence = connection.CreateCommand();
            sequence.Transaction = transaction;
            sequence.CommandText =
                "SELECT COALESCE(MAX(sequence), 0) + 1 FROM circle_messages WHERE circle_id = $circle;";
            sequence.Parameters.AddWithValue("$circle", commit.Message.CircleId.ToString());
            var expected = Convert.ToInt64(
                await sequence.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (expected != commit.Message.Sequence)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new MessageCommitResult(MessageCommitStatus.Conflict, null, null);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO circle_messages (
                    message_id, circle_id, sequence, author_member_id, author_node_id,
                    text, authored_at_utc, accepted_at_utc, request_sha256, encoded_response)
                VALUES ($message, $circle, $sequence, $member, $node,
                        $text, $authored, $accepted, $digest, $response);
                """;
            AddMessageParameters(command, commit);
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new MessageCommitResult(MessageCommitStatus.Conflict, null, null);
            }
        }

        if (!authoritative)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE message_drafts
                SET status = 1, encoded_response = $response
                WHERE message_id = $message AND circle_id = $circle
                  AND text = $text AND authored_at_utc = $authored;
                """;
            update.Parameters.AddWithValue("$response", commit.EncodedResponse);
            update.Parameters.AddWithValue("$message", commit.Message.Id.ToString());
            update.Parameters.AddWithValue("$circle", commit.Message.CircleId.ToString());
            update.Parameters.AddWithValue("$text", commit.Message.Text);
            update.Parameters.AddWithValue("$authored", Format(commit.Message.AuthoredAtUtc));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new MessageCommitResult(MessageCommitStatus.Conflict, null, null);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MessageCommitResult(
            MessageCommitStatus.Accepted,
            commit.Message,
            commit.EncodedResponse);
    }

    private async Task<LocalMessageAuthor> ReadLocalMessageAuthorAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.member_id, a.member_key_algorithm, a.member_key_id, a.member_public_key_spki,
                   l.node_id, n.key_algorithm, n.key_id, n.public_key_spki
            FROM admission_attempts a
            INNER JOIN members m ON m.member_id = a.member_id AND m.circle_id = a.circle_id
            CROSS JOIN local_node l
            CROSS JOIN local_node_credentials n
            WHERE a.circle_id = $circle AND a.status = 1
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("$circle", circleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LocalStateException(
                "local_message_author_missing",
                "This Node has no admitted Member identity for the Circle.");
        }

        var author = new LocalMessageAuthor(
            new MemberId(Guid.Parse(reader.GetString(0))),
            new NodeId(Guid.Parse(reader.GetString(4))),
            ReadCredential(
                IdentityKeyRole.Member,
                reader.GetString(1),
                reader.GetString(2),
                (byte[])reader.GetValue(3)),
            ReadCredential(
                IdentityKeyRole.Node,
                reader.GetString(5),
                reader.GetString(6),
                (byte[])reader.GetValue(7)));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LocalStateException(
                "local_message_author_ambiguous",
                "This Node has more than one admitted Member identity for the Circle.");
        }

        return author;
    }

    private async Task<StoredPrivateIdentity> ReadLocalMemberPrivateIdentityAsync(
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT member_key_algorithm, member_key_id, member_public_key_spki,
                   member_private_key_scheme, member_protected_private_key
            FROM admission_attempts
            WHERE circle_id = $circle AND status = 1
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("$circle", circleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LocalStateException(
                "local_message_author_missing",
                "This Node has no admitted Member signing identity for the Circle.");
        }

        var stored = new StoredPrivateIdentity(
            ReadCredential(
                IdentityKeyRole.Member,
                reader.GetString(0),
                reader.GetString(1),
                (byte[])reader.GetValue(2)),
            reader.GetString(3),
            (byte[])reader.GetValue(4));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new LocalStateException(
                "local_message_author_ambiguous",
                "This Node has more than one admitted Member signing identity for the Circle.");
        }

        return stored;
    }

    private async Task<MessageDraft?> ReadMessageDraftAsync(
        MessageId messageId,
        LocalMessageAuthor author,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT circle_id, author_member_id, author_node_id, text, authored_at_utc
            FROM message_drafts WHERE message_id = $message;
            """;
        command.Parameters.AddWithValue("$message", messageId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new MessageDraft(
            messageId,
            new CircleId(Guid.Parse(reader.GetString(0))),
            new MemberId(Guid.Parse(reader.GetString(1))),
            new NodeId(Guid.Parse(reader.GetString(2))),
            author.MemberCredential,
            author.NodeCredential,
            reader.GetString(3),
            ParseTimestamp(reader.GetString(4)));
    }

    private async Task<StoredMessage?> ReadStoredMessageAsync(
        MessageId messageId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT circle_id, sequence, author_member_id, author_node_id, text,
                   authored_at_utc, accepted_at_utc, request_sha256, encoded_response
            FROM circle_messages WHERE message_id = $message;
            """;
        command.Parameters.AddWithValue("$message", messageId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var circleId = new CircleId(Guid.Parse(reader.GetString(0)));
        return new StoredMessage(
            new CircleMessage(
                messageId,
                circleId,
                reader.GetInt64(1),
                new MemberId(Guid.Parse(reader.GetString(2))),
                new NodeId(Guid.Parse(reader.GetString(3))),
                reader.GetString(4),
                ParseTimestamp(reader.GetString(5)),
                ParseTimestamp(reader.GetString(6))),
            (byte[])reader.GetValue(7),
            (byte[])reader.GetValue(8));
    }

    private static CircleMessage ReadMessage(CircleId circleId, SqliteDataReader reader) =>
        new(
            new MessageId(Guid.Parse(reader.GetString(0))),
            circleId,
            reader.GetInt64(1),
            new MemberId(Guid.Parse(reader.GetString(2))),
            new NodeId(Guid.Parse(reader.GetString(3))),
            reader.GetString(4),
            ParseTimestamp(reader.GetString(5)),
            ParseTimestamp(reader.GetString(6)));

    private static void AddMessageParameters(
        SqliteCommand command,
        AuthoritativeMessageCommit commit)
    {
        command.Parameters.AddWithValue("$message", commit.Message.Id.ToString());
        command.Parameters.AddWithValue("$circle", commit.Message.CircleId.ToString());
        command.Parameters.AddWithValue("$sequence", commit.Message.Sequence);
        command.Parameters.AddWithValue("$member", commit.Message.AuthorMemberId.ToString());
        command.Parameters.AddWithValue("$node", commit.Message.AuthorNodeId.ToString());
        command.Parameters.AddWithValue("$text", commit.Message.Text);
        command.Parameters.AddWithValue("$authored", Format(commit.Message.AuthoredAtUtc));
        command.Parameters.AddWithValue("$accepted", Format(commit.Message.AcceptedAtUtc));
        command.Parameters.AddWithValue("$digest", commit.RequestSha256);
        command.Parameters.AddWithValue("$response", commit.EncodedResponse);
    }

    private static void ValidateCommit(AuthoritativeMessageCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ValidateText(commit.Message.Text);
        if (commit.Message.Sequence <= 0
            || commit.Message.AuthoredAtUtc.Offset != TimeSpan.Zero
            || commit.Message.AcceptedAtUtc.Offset != TimeSpan.Zero
            || commit.RequestSha256.Length != SHA256.HashSizeInBytes
            || commit.EncodedResponse is not { Length: > 0 and <= 16 * 1024 })
        {
            throw new ArgumentException("The Circle message commit is invalid.", nameof(commit));
        }
    }

    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || System.Text.Encoding.UTF8.GetByteCount(text) > 4 * 1024
            || text.Any(character => char.IsControl(character)
                && character is not ('\r' or '\n' or '\t')))
        {
            throw new InputValidationException(
                "message_text_invalid",
                "Message text must be non-blank and no larger than 4 KiB UTF-8.");
        }
    }

    private static bool MessagesEqual(CircleMessage left, CircleMessage right) =>
        left == right;

    private static async Task MigrateV4ToV5Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"{MessageSchemaSql}\nPRAGMA user_version = 5;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddMessageExpectedTables(Dictionary<string, TableSchema> tables)
    {
        tables["message_drafts"] = new(
            [
                new("message_id", "TEXT", 1),
                new("circle_id", "TEXT", 0),
                new("author_member_id", "TEXT", 0),
                new("author_node_id", "TEXT", 0),
                new("text", "TEXT", 0),
                new("authored_at_utc", "TEXT", 0),
                new("status", "INTEGER", 0),
                new("encoded_response", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "author_member_id", "member_id", "NO ACTION"),
                new("nodes", "author_node_id", "node_id", "NO ACTION"),
            ]);
        tables["circle_messages"] = new(
            [
                new("message_id", "TEXT", 1),
                new("circle_id", "TEXT", 0),
                new("sequence", "INTEGER", 0),
                new("author_member_id", "TEXT", 0),
                new("author_node_id", "TEXT", 0),
                new("text", "TEXT", 0),
                new("authored_at_utc", "TEXT", 0),
                new("accepted_at_utc", "TEXT", 0),
                new("request_sha256", "BLOB", 0),
                new("encoded_response", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "author_member_id", "member_id", "NO ACTION"),
                new("nodes", "author_node_id", "node_id", "NO ACTION"),
            ]);
    }

    private sealed record LocalMessageAuthor(
        MemberId MemberId,
        NodeId NodeId,
        PublicIdentityCredential MemberCredential,
        PublicIdentityCredential NodeCredential);

    private sealed record StoredMessage(
        CircleMessage Message,
        byte[] RequestSha256,
        byte[] EncodedResponse);
}
