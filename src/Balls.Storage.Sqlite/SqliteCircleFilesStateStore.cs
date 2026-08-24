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

    private const string CircleFilesLifecycleSchemaSql =
        """
        CREATE TABLE circle_files_access_grant_revocations (
            request_id TEXT NOT NULL PRIMARY KEY,
            grant_id TEXT NOT NULL UNIQUE,
            circle_id TEXT NOT NULL,
            contribution_id TEXT NOT NULL,
            revoked_generation INTEGER NOT NULL,
            revoked_at_utc TEXT NOT NULL,
            owner_member_id TEXT NOT NULL,
            authority_generation INTEGER NOT NULL,
            authorized_at_utc TEXT NOT NULL,
            authorization_transcript BLOB NOT NULL,
            member_signature BLOB NOT NULL,
            circle_authority_signature BLOB NOT NULL,
            FOREIGN KEY (grant_id) REFERENCES circle_files_access_grants(grant_id) ON DELETE CASCADE,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (contribution_id) REFERENCES circle_files_contributions(contribution_id) ON DELETE CASCADE,
            FOREIGN KEY (owner_member_id) REFERENCES members(member_id)
        );

        CREATE TABLE circle_files_lifecycle_audit_events (
            event_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            contribution_id TEXT NOT NULL,
            subject_kind TEXT NOT NULL,
            subject_id TEXT NOT NULL,
            operation TEXT NOT NULL,
            outcome TEXT NOT NULL,
            open_session_count INTEGER NOT NULL,
            occurred_at_utc TEXT NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (contribution_id) REFERENCES circle_files_contributions(contribution_id) ON DELETE CASCADE
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

    public Task<RevokedMemberAccessGrant?> GetAccessGrantRevocationAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            token => ReadGrantRevocationAsync(
                circleId,
                contributionId,
                grantId,
                transaction: null,
                token),
            cancellationToken);

    public Task<RevokedMemberAccessGrant> RevokeAccessGrantAsync(
        MemberAccessGrantRevocationRequestId requestId,
        MemberAccessGrant revokedGrant,
        MemberAccessGrantRevocation revocation,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadGrantRevocationByRequestOrGrantAsync(
                    requestId,
                    revokedGrant.Id,
                    transaction,
                    token).ConfigureAwait(false);
                if (existing is not null)
                {
                    EnsureEquivalentRevocation(existing, revocation);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return existing;
                }

                var context = await ReadCircleFilesAuthorizationContextAsync(
                    revokedGrant.CircleId,
                    transaction,
                    token).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_not_found",
                        "The requested Circle is not known to this Node.");
                var current = await ReadGrantByIdAsync(revokedGrant.Id, transaction, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_files_grant_not_found",
                        "The requested Circle Files Access Grant was not found.");
                ValidateRevokedGrant(current, revokedGrant, revocation, requestId, context);

                using (var update = connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandText =
                        """
                        UPDATE circle_files_access_grants
                        SET lifecycle = $revoked
                        WHERE grant_id = $grant_id
                          AND generation = $generation
                          AND lifecycle IN ($defined, $active);
                        """;
                    update.Parameters.AddWithValue("$revoked", (int)MemberAccessGrantLifecycle.Revoked);
                    update.Parameters.AddWithValue("$grant_id", revokedGrant.Id.ToString());
                    update.Parameters.AddWithValue("$generation", revokedGrant.Generation);
                    update.Parameters.AddWithValue("$defined", (int)MemberAccessGrantLifecycle.Defined);
                    update.Parameters.AddWithValue("$active", (int)MemberAccessGrantLifecycle.Active);
                    if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    {
                        throw new LocalStateConflictException(
                            "circle_files_grant_generation_changed",
                            "The Access Grant changed before revocation could be committed.");
                    }
                }

                using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText =
                        """
                        INSERT INTO circle_files_access_grant_revocations (
                            request_id, grant_id, circle_id, contribution_id, revoked_generation,
                            revoked_at_utc, owner_member_id, authority_generation, authorized_at_utc,
                            authorization_transcript, member_signature, circle_authority_signature)
                        VALUES (
                            $request_id, $grant_id, $circle_id, $contribution_id, $generation,
                            $revoked_at_utc, $owner_member_id, $authority_generation,
                            $authorized_at_utc, $transcript, $member_signature, $authority_signature);
                        """;
                    insert.Parameters.AddWithValue("$request_id", requestId.ToString());
                    insert.Parameters.AddWithValue("$grant_id", revokedGrant.Id.ToString());
                    insert.Parameters.AddWithValue("$circle_id", revokedGrant.CircleId.ToString());
                    insert.Parameters.AddWithValue(
                        "$contribution_id",
                        revokedGrant.ContributionId.ToString());
                    insert.Parameters.AddWithValue("$generation", revocation.RevokedGeneration);
                    insert.Parameters.AddWithValue("$revoked_at_utc", Format(revocation.RevokedAtUtc));
                    AddAuthorizationParameters(insert, revocation.Authorization);
                    await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
                return new RevokedMemberAccessGrant(revokedGrant, revocation);
            },
            cancellationToken);

    public Task RecordCircleFilesLifecycleAuditEventAsync(
        CircleFilesLifecycleAuditEvent auditEvent,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                ValidateLifecycleAuditEvent(auditEvent);
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO circle_files_lifecycle_audit_events (
                        event_id, circle_id, contribution_id, subject_kind, subject_id, operation, outcome,
                        open_session_count, occurred_at_utc)
                    VALUES (
                        $event_id, $circle_id, $contribution_id, $subject_kind, $subject_id, $operation, $outcome,
                        $open_session_count, $occurred_at_utc);
                    """;
                command.Parameters.AddWithValue("$event_id", auditEvent.EventId.ToString("D"));
                command.Parameters.AddWithValue("$circle_id", auditEvent.CircleId.ToString());
                command.Parameters.AddWithValue(
                    "$contribution_id",
                    auditEvent.ContributionId.ToString());
                command.Parameters.AddWithValue(
                    "$subject_kind",
                    auditEvent.GrantId is null ? "contribution" : "grant");
                command.Parameters.AddWithValue(
                    "$subject_id",
                    auditEvent.GrantId is { } grantId
                        ? grantId.ToString()
                        : auditEvent.ContributionId.ToString());
                command.Parameters.AddWithValue("$operation", auditEvent.Operation);
                command.Parameters.AddWithValue("$outcome", auditEvent.Outcome);
                command.Parameters.AddWithValue(
                    "$open_session_count",
                    auditEvent.OpenSessionCount);
                command.Parameters.AddWithValue(
                    "$occurred_at_utc",
                    Format(auditEvent.OccurredAtUtc));
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<IReadOnlyList<CircleFilesLifecycleAuditEvent>>
        ListCircleFilesLifecycleAuditEventsAsync(
            CircleId circleId,
            CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<IReadOnlyList<CircleFilesLifecycleAuditEvent>>(
            async token =>
            {
                var events = new List<CircleFilesLifecycleAuditEvent>();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT event_id, circle_id, contribution_id, subject_kind, subject_id,
                           operation, outcome,
                           open_session_count, occurred_at_utc
                    FROM circle_files_lifecycle_audit_events
                    WHERE circle_id = $circle_id
                    ORDER BY occurred_at_utc, event_id;
                    """;
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    events.Add(new CircleFilesLifecycleAuditEvent(
                        Guid.Parse(reader.GetString(0)),
                        new CircleId(Guid.Parse(reader.GetString(1))),
                        new CircleFilesContributionId(Guid.Parse(reader.GetString(2))),
                        reader.GetString(3) == "grant"
                            ? new MemberAccessGrantId(Guid.Parse(reader.GetString(4)))
                            : null,
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.GetInt32(7),
                        ParseTimestamp(reader.GetString(8))));
                }

                return events;
            },
            cancellationToken);

    private static void ValidateLifecycleAuditEvent(CircleFilesLifecycleAuditEvent auditEvent)
    {
        if (auditEvent.EventId == Guid.Empty
            || auditEvent.CircleId.Value == Guid.Empty
            || auditEvent.ContributionId.Value == Guid.Empty
            || auditEvent.GrantId is { Value: var grantId } && grantId == Guid.Empty
            || auditEvent.OpenSessionCount is < 0 or > 1000
            || !IsAuditToken(auditEvent.Operation)
            || !IsAuditToken(auditEvent.Outcome))
        {
            throw new LocalStateException(
                "circle_files_audit_invalid",
                "The Circle Files lifecycle audit event is invalid.");
        }
    }

    private static bool IsAuditToken(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 50
        && value.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

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

    private async Task<MemberAccessGrant?> ReadGrantByIdAsync(
        MemberAccessGrantId grantId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = GrantSelect + " WHERE grant_id = $grant_id;";
        command.Parameters.AddWithValue("$grant_id", grantId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadGrant(reader)
            : null;
    }

    private async Task<RevokedMemberAccessGrant?> ReadGrantRevocationAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        MemberAccessGrantId grantId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = CreateGrantRevocationSelect(transaction);
        command.CommandText +=
            " WHERE r.circle_id = $circle_id AND r.contribution_id = $contribution_id" +
            " AND r.grant_id = $grant_id;";
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        command.Parameters.AddWithValue("$contribution_id", contributionId.ToString());
        command.Parameters.AddWithValue("$grant_id", grantId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadGrantRevocation(reader)
            : null;
    }

    private async Task<RevokedMemberAccessGrant?> ReadGrantRevocationByRequestOrGrantAsync(
        MemberAccessGrantRevocationRequestId requestId,
        MemberAccessGrantId grantId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = CreateGrantRevocationSelect(transaction);
        command.CommandText += " WHERE r.request_id = $request_id OR r.grant_id = $grant_id;";
        command.Parameters.AddWithValue("$request_id", requestId.ToString());
        command.Parameters.AddWithValue("$grant_id", grantId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadGrantRevocation(reader)
            : null;
    }

    private SqliteCommand CreateGrantRevocationSelect(SqliteTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT g.grant_id, g.circle_id, g.contribution_id, g.member_id, g.access,
                   g.lifecycle, g.generation, g.created_at_utc, g.owner_member_id,
                   g.authority_generation, g.authorized_at_utc, g.authorization_transcript,
                   g.member_signature, g.circle_authority_signature,
                   r.request_id, r.circle_id, r.contribution_id, r.revoked_generation,
                   r.revoked_at_utc, r.owner_member_id, r.authority_generation,
                   r.authorized_at_utc, r.authorization_transcript, r.member_signature,
                   r.circle_authority_signature
            FROM circle_files_access_grant_revocations r
            INNER JOIN circle_files_access_grants g ON g.grant_id = r.grant_id
            """;
        return command;
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

    private static void ValidateRevokedGrant(
        MemberAccessGrant current,
        MemberAccessGrant requested,
        MemberAccessGrantRevocation revocation,
        MemberAccessGrantRevocationRequestId requestId,
        CircleFilesAuthorizationContext context)
    {
        var expected = CircleFilesAuthorizationTranscript.EncodeGrantRevocation(revocation);
        ValidateAuthorization(revocation.Authorization, expected, context);
        if (current.Id != requested.Id
            || current.CircleId != requested.CircleId
            || current.ContributionId != requested.ContributionId
            || current.MemberId != requested.MemberId
            || current.Access != requested.Access
            || current.Generation != requested.Generation
            || current.Lifecycle is not (MemberAccessGrantLifecycle.Defined
                or MemberAccessGrantLifecycle.Active)
            || requested.Lifecycle != MemberAccessGrantLifecycle.Revoked
            || requestId != revocation.RequestId
            || revocation.CircleId != requested.CircleId
            || revocation.ContributionId != requested.ContributionId
            || revocation.GrantId != requested.Id
            || revocation.RevokedGeneration != requested.Generation
            || revocation.RevokedAtUtc != revocation.Authorization.AuthorizedAtUtc)
        {
            throw new ArgumentException("The Circle Files Access Grant revocation is invalid.");
        }
    }

    private static void EnsureEquivalentRevocation(
        RevokedMemberAccessGrant existing,
        MemberAccessGrantRevocation requested)
    {
        if (existing.Revocation.RequestId != requested.RequestId
            || existing.Revocation.CircleId != requested.CircleId
            || existing.Revocation.ContributionId != requested.ContributionId
            || existing.Revocation.GrantId != requested.GrantId
            || existing.Revocation.RevokedGeneration != requested.RevokedGeneration)
        {
            throw new LocalStateConflictException(
                "circle_files_grant_revocation_conflict",
                "The revocation request or Access Grant was already used for different input.");
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

    private static RevokedMemberAccessGrant ReadGrantRevocation(SqliteDataReader reader)
    {
        var grant = ReadGrant(reader);
        var revocation = new MemberAccessGrantRevocation(
            new MemberAccessGrantRevocationRequestId(Guid.Parse(reader.GetString(14))),
            new CircleId(Guid.Parse(reader.GetString(15))),
            new CircleFilesContributionId(Guid.Parse(reader.GetString(16))),
            grant.Id,
            reader.GetInt64(17),
            ParseTimestamp(reader.GetString(18)),
            ReadAuthorization(reader, 19));
        return new RevokedMemberAccessGrant(grant, revocation);
    }

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

    private static async Task MigrateV7ToV8Async(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        await ExecuteV7ToV8MigrationAsync(
            connection,
            beforeCommit: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task ExecuteV7ToV8MigrationAsync(
        SqliteConnection connection,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CircleFilesLifecycleSchemaSql + " PRAGMA user_version = 8;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (beforeCommit is not null)
        {
            await beforeCommit(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCircleFilesLifecycleExpectedTables(
        IDictionary<string, TableSchema> tables)
    {
        tables["circle_files_access_grant_revocations"] = new(
            [
                new("request_id", "TEXT", 1), new("grant_id", "TEXT", 0),
                new("circle_id", "TEXT", 0), new("contribution_id", "TEXT", 0),
                new("revoked_generation", "INTEGER", 0), new("revoked_at_utc", "TEXT", 0),
                new("owner_member_id", "TEXT", 0), new("authority_generation", "INTEGER", 0),
                new("authorized_at_utc", "TEXT", 0),
                new("authorization_transcript", "BLOB", 0),
                new("member_signature", "BLOB", 0),
                new("circle_authority_signature", "BLOB", 0),
            ],
            [
                new("circle_files_access_grants", "grant_id", "grant_id", "CASCADE"),
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("circle_files_contributions", "contribution_id", "contribution_id", "CASCADE"),
                new("members", "owner_member_id", "member_id", "NO ACTION"),
            ]);
        tables["circle_files_lifecycle_audit_events"] = new(
            [
                new("event_id", "TEXT", 1), new("circle_id", "TEXT", 0),
                new("contribution_id", "TEXT", 0), new("subject_kind", "TEXT", 0),
                new("subject_id", "TEXT", 0),
                new("operation", "TEXT", 0), new("outcome", "TEXT", 0),
                new("open_session_count", "INTEGER", 0), new("occurred_at_utc", "TEXT", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("circle_files_contributions", "contribution_id", "contribution_id", "CASCADE"),
            ]);
    }
}
