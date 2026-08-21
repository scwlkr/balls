using System.Security.Cryptography;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    private const string CircleFilesSchemaSql =
        """
        CREATE TABLE circle_files_contributions (
            contribution_id TEXT NOT NULL PRIMARY KEY,
            request_id TEXT NOT NULL UNIQUE,
            circle_id TEXT NOT NULL,
            provider_id TEXT NOT NULL UNIQUE,
            provider_node_id TEXT NOT NULL,
            display_name TEXT NOT NULL,
            lifecycle INTEGER NOT NULL,
            generation INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            owner_member_id TEXT NOT NULL,
            authority_generation INTEGER NOT NULL,
            authorized_at_utc TEXT NOT NULL,
            authorization_transcript BLOB NOT NULL,
            member_signature BLOB NOT NULL,
            circle_authority_signature BLOB NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (provider_node_id) REFERENCES nodes(node_id),
            FOREIGN KEY (owner_member_id) REFERENCES members(member_id)
        );

        CREATE TABLE circle_files_access_grants (
            grant_id TEXT NOT NULL PRIMARY KEY,
            request_id TEXT NOT NULL UNIQUE,
            circle_id TEXT NOT NULL,
            contribution_id TEXT NOT NULL,
            member_id TEXT NOT NULL,
            access INTEGER NOT NULL,
            lifecycle INTEGER NOT NULL,
            generation INTEGER NOT NULL,
            created_at_utc TEXT NOT NULL,
            owner_member_id TEXT NOT NULL,
            authority_generation INTEGER NOT NULL,
            authorized_at_utc TEXT NOT NULL,
            authorization_transcript BLOB NOT NULL,
            member_signature BLOB NOT NULL,
            circle_authority_signature BLOB NOT NULL,
            UNIQUE (contribution_id, member_id),
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (contribution_id) REFERENCES circle_files_contributions(contribution_id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(member_id),
            FOREIGN KEY (owner_member_id) REFERENCES members(member_id)
        );
        """;

    public Task<CircleFilesAuthorizationContext?> GetAuthorizationContextAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<CircleFilesAuthorizationContext?>(
            token => ReadCircleFilesAuthorizationContextAsync(circleId, transaction: null, token),
            cancellationToken);

    public Task<byte[]> SignWithLocalMemberAsync(
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

    public Task<CircleFilesContribution> CreateContributionAsync(
        CircleFilesContributionRequestId requestId,
        CircleFilesContribution contribution,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadContributionByRequestAsync(requestId, transaction, token)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    EnsureEquivalentContribution(existing, contribution);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return existing;
                }

                var context = await ReadCircleFilesAuthorizationContextAsync(
                    contribution.CircleId,
                    transaction,
                    token).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_not_found",
                        "The requested Circle is not known to this Node.");
                ValidateContributionAuthorization(requestId, contribution, context);

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO circle_files_contributions (
                        contribution_id, request_id, circle_id, provider_id, provider_node_id,
                        display_name, lifecycle, generation, created_at_utc, owner_member_id,
                        authority_generation, authorized_at_utc, authorization_transcript,
                        member_signature, circle_authority_signature)
                    VALUES (
                        $contribution_id, $request_id, $circle_id, $provider_id, $provider_node_id,
                        $display_name, $lifecycle, $generation, $created_at_utc, $owner_member_id,
                        $authority_generation, $authorized_at_utc, $transcript,
                        $member_signature, $authority_signature);
                    """;
                AddContributionParameters(command, requestId, contribution);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return contribution;
            },
            cancellationToken);

    public Task<IReadOnlyList<CircleFilesContribution>> ListContributionsAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<IReadOnlyList<CircleFilesContribution>>(
            async token =>
            {
                var values = new List<CircleFilesContribution>();
                using var command = connection.CreateCommand();
                command.CommandText = ContributionSelect +
                    " WHERE circle_id = $circle_id ORDER BY created_at_utc, contribution_id;";
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    values.Add(ReadContribution(reader));
                }

                return values;
            },
            cancellationToken);

    public Task<MemberAccessGrant> CreateAccessGrantAsync(
        MemberAccessGrantRequestId requestId,
        MemberAccessGrant grant,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadGrantByRequestAsync(requestId, transaction, token)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    EnsureEquivalentGrant(existing, grant);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return existing;
                }

                var context = await ReadCircleFilesAuthorizationContextAsync(
                    grant.CircleId,
                    transaction,
                    token).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_not_found",
                        "The requested Circle is not known to this Node.");
                ValidateGrantAuthorization(requestId, grant, context);
                await ValidateGrantReferencesAsync(grant, transaction, token).ConfigureAwait(false);

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO circle_files_access_grants (
                        grant_id, request_id, circle_id, contribution_id, member_id, access,
                        lifecycle, generation, created_at_utc, owner_member_id,
                        authority_generation, authorized_at_utc, authorization_transcript,
                        member_signature, circle_authority_signature)
                    VALUES (
                        $grant_id, $request_id, $circle_id, $contribution_id, $member_id, $access,
                        $lifecycle, $generation, $created_at_utc, $owner_member_id,
                        $authority_generation, $authorized_at_utc, $transcript,
                        $member_signature, $authority_signature);
                    """;
                AddGrantParameters(command, requestId, grant);
                try
                {
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new LocalStateConflictException(
                        "circle_files_grant_exists",
                        "This Member already has an Access Grant for the contribution.");
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
                return grant;
            },
            cancellationToken);

    public Task<IReadOnlyList<MemberAccessGrant>> ListAccessGrantsAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<IReadOnlyList<MemberAccessGrant>>(
            async token =>
            {
                var values = new List<MemberAccessGrant>();
                using var command = connection.CreateCommand();
                command.CommandText = GrantSelect +
                    " WHERE circle_id = $circle_id AND contribution_id = $contribution_id" +
                    " ORDER BY created_at_utc, grant_id;";
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                command.Parameters.AddWithValue("$contribution_id", contributionId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    values.Add(ReadGrant(reader));
                }

                return values;
            },
            cancellationToken);

    private const string ContributionSelect =
        """
        SELECT contribution_id, circle_id, provider_id, provider_node_id, display_name,
               lifecycle, generation, created_at_utc, owner_member_id, authority_generation,
               authorized_at_utc, authorization_transcript, member_signature,
               circle_authority_signature
        FROM circle_files_contributions
        """;

    private const string GrantSelect =
        """
        SELECT grant_id, circle_id, contribution_id, member_id, access, lifecycle, generation,
               created_at_utc, owner_member_id, authority_generation, authorized_at_utc,
               authorization_transcript, member_signature, circle_authority_signature
        FROM circle_files_access_grants
        """;

    private async Task<CircleFilesAuthorizationContext?> ReadCircleFilesAuthorizationContextAsync(
        CircleId circleId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT l.member_id, m.role, l.key_algorithm, l.key_id, l.public_key_spki,
                   n.node_id, t.authority_generation, t.root_key_algorithm,
                   t.root_key_id, t.root_public_key_spki
            FROM local_circle_members l
            INNER JOIN members m ON m.member_id = l.member_id AND m.circle_id = l.circle_id
            CROSS JOIN local_node n
            INNER JOIN circle_trust t ON t.circle_id = l.circle_id
            WHERE l.circle_id = $circle_id;
            """;
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new CircleFilesAuthorizationContext(
                circleId,
                new MemberId(Guid.Parse(reader.GetString(0))),
                (MemberRole)reader.GetInt32(1),
                ReadCredential(
                    IdentityKeyRole.Member,
                    reader.GetString(2),
                    reader.GetString(3),
                    (byte[])reader.GetValue(4)),
                new NodeId(Guid.Parse(reader.GetString(5))),
                reader.GetInt64(6),
                ReadCredential(
                    IdentityKeyRole.CircleAuthority,
                    reader.GetString(7),
                    reader.GetString(8),
                    (byte[])reader.GetValue(9)))
            : null;
    }

    private async Task<CircleFilesContribution?> ReadContributionByRequestAsync(
        CircleFilesContributionRequestId requestId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ContributionSelect + " WHERE request_id = $request_id;";
        command.Parameters.AddWithValue("$request_id", requestId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadContribution(reader)
            : null;
    }

    private async Task<MemberAccessGrant?> ReadGrantByRequestAsync(
        MemberAccessGrantRequestId requestId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = GrantSelect + " WHERE request_id = $request_id;";
        command.Parameters.AddWithValue("$request_id", requestId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadGrant(reader)
            : null;
    }

    private async Task ValidateGrantReferencesAsync(
        MemberAccessGrant grant,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                EXISTS(SELECT 1 FROM circle_files_contributions
                       WHERE contribution_id = $contribution_id AND circle_id = $circle_id),
                EXISTS(SELECT 1 FROM members
                       WHERE member_id = $member_id AND circle_id = $circle_id);
            """;
        command.Parameters.AddWithValue("$contribution_id", grant.ContributionId.ToString());
        command.Parameters.AddWithValue("$member_id", grant.MemberId.ToString());
        command.Parameters.AddWithValue("$circle_id", grant.CircleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reader.GetInt32(0) != 1)
        {
            throw new LocalStateException(
                "circle_files_contribution_not_found",
                "The requested Circle Files contribution is not known.");
        }

        if (reader.GetInt32(1) != 1)
        {
            throw new LocalStateException(
                "member_not_found",
                "The requested Member is not known in this Circle.");
        }
    }

    private static void ValidateContributionAuthorization(
        CircleFilesContributionRequestId requestId,
        CircleFilesContribution contribution,
        CircleFilesAuthorizationContext context)
    {
        var expected = CircleFilesAuthorizationTranscript.EncodeContribution(requestId, contribution);
        ValidateAuthorization(contribution.Authorization, expected, context);
        if (contribution.CircleId != context.CircleId
            || contribution.Provider.NodeId != context.NodeId
            || contribution.Lifecycle != CircleFilesContributionLifecycle.Defined
            || contribution.Generation != 1
            || contribution.CreatedAtUtc != contribution.Authorization.AuthorizedAtUtc
            || string.IsNullOrWhiteSpace(contribution.DisplayName)
            || contribution.DisplayName.Length > 100)
        {
            throw new ArgumentException("The Circle Files contribution is invalid.");
        }
    }

    private static void ValidateGrantAuthorization(
        MemberAccessGrantRequestId requestId,
        MemberAccessGrant grant,
        CircleFilesAuthorizationContext context)
    {
        var expected = CircleFilesAuthorizationTranscript.EncodeGrant(requestId, grant);
        ValidateAuthorization(grant.Authorization, expected, context);
        if (grant.CircleId != context.CircleId
            || grant.Lifecycle != MemberAccessGrantLifecycle.Defined
            || grant.Generation != 1
            || grant.CreatedAtUtc != grant.Authorization.AuthorizedAtUtc
            || !Enum.IsDefined(grant.Access))
        {
            throw new ArgumentException("The Circle Files Access Grant is invalid.");
        }
    }

    private static void ValidateAuthorization(
        CircleFilesOwnerAuthorization authorization,
        byte[] expectedTranscript,
        CircleFilesAuthorizationContext context)
    {
        if (context.MemberRole != MemberRole.Owner
            || authorization.OwnerMemberId != context.MemberId
            || authorization.AuthorityGeneration != context.AuthorityGeneration
            || !CryptographicOperations.FixedTimeEquals(
                authorization.Transcript,
                expectedTranscript)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.MemberSignature,
                context.MemberCredential)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.CircleAuthoritySignature,
                context.RootCredential))
        {
            throw new LocalStateException(
                "circle_files_authorization_failed",
                "The Circle Files mutation authorization is invalid.");
        }
    }

    private static void EnsureEquivalentContribution(
        CircleFilesContribution existing,
        CircleFilesContribution requested)
    {
        if (existing.CircleId != requested.CircleId
            || existing.Provider.NodeId != requested.Provider.NodeId
            || !string.Equals(existing.DisplayName, requested.DisplayName, StringComparison.Ordinal))
        {
            throw new LocalStateConflictException(
                "circle_files_contribution_request_conflict",
                "The contribution request identifier was already used for different input.");
        }
    }

    private static void EnsureEquivalentGrant(MemberAccessGrant existing, MemberAccessGrant requested)
    {
        if (existing.CircleId != requested.CircleId
            || existing.ContributionId != requested.ContributionId
            || existing.MemberId != requested.MemberId
            || existing.Access != requested.Access)
        {
            throw new LocalStateConflictException(
                "circle_files_grant_request_conflict",
                "The Access Grant request identifier was already used for different input.");
        }
    }

    private static void AddContributionParameters(
        SqliteCommand command,
        CircleFilesContributionRequestId requestId,
        CircleFilesContribution value)
    {
        command.Parameters.AddWithValue("$contribution_id", value.Id.ToString());
        command.Parameters.AddWithValue("$request_id", requestId.ToString());
        command.Parameters.AddWithValue("$circle_id", value.CircleId.ToString());
        command.Parameters.AddWithValue("$provider_id", value.Provider.Id.ToString());
        command.Parameters.AddWithValue("$provider_node_id", value.Provider.NodeId.ToString());
        command.Parameters.AddWithValue("$display_name", value.DisplayName);
        command.Parameters.AddWithValue("$lifecycle", (int)value.Lifecycle);
        command.Parameters.AddWithValue("$generation", value.Generation);
        command.Parameters.AddWithValue("$created_at_utc", Format(value.CreatedAtUtc));
        AddAuthorizationParameters(command, value.Authorization);
    }

    private static void AddGrantParameters(
        SqliteCommand command,
        MemberAccessGrantRequestId requestId,
        MemberAccessGrant value)
    {
        command.Parameters.AddWithValue("$grant_id", value.Id.ToString());
        command.Parameters.AddWithValue("$request_id", requestId.ToString());
        command.Parameters.AddWithValue("$circle_id", value.CircleId.ToString());
        command.Parameters.AddWithValue("$contribution_id", value.ContributionId.ToString());
        command.Parameters.AddWithValue("$member_id", value.MemberId.ToString());
        command.Parameters.AddWithValue("$access", (int)value.Access);
        command.Parameters.AddWithValue("$lifecycle", (int)value.Lifecycle);
        command.Parameters.AddWithValue("$generation", value.Generation);
        command.Parameters.AddWithValue("$created_at_utc", Format(value.CreatedAtUtc));
        AddAuthorizationParameters(command, value.Authorization);
    }

    private static void AddAuthorizationParameters(
        SqliteCommand command,
        CircleFilesOwnerAuthorization value)
    {
        command.Parameters.AddWithValue("$owner_member_id", value.OwnerMemberId.ToString());
        command.Parameters.AddWithValue("$authority_generation", value.AuthorityGeneration);
        command.Parameters.AddWithValue("$authorized_at_utc", Format(value.AuthorizedAtUtc));
        command.Parameters.AddWithValue("$transcript", value.Transcript);
        command.Parameters.AddWithValue("$member_signature", value.MemberSignature);
        command.Parameters.AddWithValue("$authority_signature", value.CircleAuthoritySignature);
    }

    private static CircleFilesContribution ReadContribution(SqliteDataReader reader) =>
        new(
            new CircleFilesContributionId(Guid.Parse(reader.GetString(0))),
            new CircleId(Guid.Parse(reader.GetString(1))),
            new CircleFilesProviderIdentity(
                new CircleFilesProviderId(Guid.Parse(reader.GetString(2))),
                new NodeId(Guid.Parse(reader.GetString(3)))),
            reader.GetString(4),
            (CircleFilesContributionLifecycle)reader.GetInt32(5),
            reader.GetInt64(6),
            ParseTimestamp(reader.GetString(7)),
            ReadAuthorization(reader, 8));

    private static MemberAccessGrant ReadGrant(SqliteDataReader reader) =>
        new(
            new MemberAccessGrantId(Guid.Parse(reader.GetString(0))),
            new CircleId(Guid.Parse(reader.GetString(1))),
            new CircleFilesContributionId(Guid.Parse(reader.GetString(2))),
            new MemberId(Guid.Parse(reader.GetString(3))),
            (MemberAccessMode)reader.GetInt32(4),
            (MemberAccessGrantLifecycle)reader.GetInt32(5),
            reader.GetInt64(6),
            ParseTimestamp(reader.GetString(7)),
            ReadAuthorization(reader, 8));

    private static CircleFilesOwnerAuthorization ReadAuthorization(
        SqliteDataReader reader,
        int offset) =>
        new(
            new MemberId(Guid.Parse(reader.GetString(offset))),
            reader.GetInt64(offset + 1),
            ParseTimestamp(reader.GetString(offset + 2)),
            (byte[])reader.GetValue(offset + 3),
            (byte[])reader.GetValue(offset + 4),
            (byte[])reader.GetValue(offset + 5));

    private static async Task MigrateV5ToV6Async(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        await ExecuteV5ToV6MigrationAsync(
            connection,
            beforeCommit: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task ExecuteV5ToV6MigrationAsync(
        SqliteConnection connection,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CircleFilesSchemaSql + " PRAGMA user_version = 6;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (beforeCommit is not null)
        {
            await beforeCommit(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCircleFilesExpectedTables(IDictionary<string, TableSchema> tables)
    {
        tables["circle_files_contributions"] = new(
            [
                new("contribution_id", "TEXT", 1), new("request_id", "TEXT", 0),
                new("circle_id", "TEXT", 0), new("provider_id", "TEXT", 0),
                new("provider_node_id", "TEXT", 0), new("display_name", "TEXT", 0),
                new("lifecycle", "INTEGER", 0), new("generation", "INTEGER", 0),
                new("created_at_utc", "TEXT", 0), new("owner_member_id", "TEXT", 0),
                new("authority_generation", "INTEGER", 0), new("authorized_at_utc", "TEXT", 0),
                new("authorization_transcript", "BLOB", 0), new("member_signature", "BLOB", 0),
                new("circle_authority_signature", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("nodes", "provider_node_id", "node_id", "NO ACTION"),
                new("members", "owner_member_id", "member_id", "NO ACTION"),
            ]);
        tables["circle_files_access_grants"] = new(
            [
                new("grant_id", "TEXT", 1), new("request_id", "TEXT", 0),
                new("circle_id", "TEXT", 0), new("contribution_id", "TEXT", 0),
                new("member_id", "TEXT", 0), new("access", "INTEGER", 0),
                new("lifecycle", "INTEGER", 0), new("generation", "INTEGER", 0),
                new("created_at_utc", "TEXT", 0), new("owner_member_id", "TEXT", 0),
                new("authority_generation", "INTEGER", 0), new("authorized_at_utc", "TEXT", 0),
                new("authorization_transcript", "BLOB", 0), new("member_signature", "BLOB", 0),
                new("circle_authority_signature", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("circle_files_contributions", "contribution_id", "contribution_id", "CASCADE"),
                new("members", "member_id", "member_id", "NO ACTION"),
                new("members", "owner_member_id", "member_id", "NO ACTION"),
            ]);
    }
}
