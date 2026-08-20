using System.Security.Cryptography;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    private const string InvitationSchemaSql =
        """
        CREATE TABLE local_transport_credentials (
            singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
            key_algorithm TEXT NOT NULL,
            key_id TEXT NOT NULL UNIQUE,
            public_key_spki BLOB NOT NULL,
            private_key_scheme TEXT NOT NULL,
            protected_private_key BLOB NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (singleton_id) REFERENCES local_node(singleton_id) ON DELETE CASCADE
        );

        CREATE TABLE circle_invitations (
            invitation_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            package_sha256 BLOB NOT NULL,
            encoded_package BLOB NOT NULL,
            expires_at_utc TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE
        );

        CREATE TABLE invitation_redemptions (
            invitation_id TEXT NOT NULL PRIMARY KEY,
            redemption_id TEXT NOT NULL UNIQUE,
            redeemed_at_utc TEXT NOT NULL,
            FOREIGN KEY (invitation_id) REFERENCES circle_invitations(invitation_id) ON DELETE CASCADE
        );

        CREATE TABLE revoked_invitations (
            invitation_id TEXT NOT NULL PRIMARY KEY,
            revoked_at_utc TEXT NOT NULL,
            FOREIGN KEY (invitation_id) REFERENCES circle_invitations(invitation_id) ON DELETE CASCADE
        );
        """;

    public Task StoreCircleInvitationAsync(
        PersistedCircleInvitation invitation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ValidateInvitationMaterial(invitation);
        return ExecuteLockedAsync(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO circle_invitations (
                        invitation_id, circle_id, package_sha256, encoded_package,
                        expires_at_utc, created_at_utc)
                    VALUES (
                        $invitation_id, $circle_id, $digest, $package,
                        $expires_at_utc, $created_at_utc);
                    """;
                AddInvitationParameters(command, invitation);
                try
                {
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new LocalStateConflictException(
                        "invitation_conflict",
                        "The invitation identifier is already in use.");
                }
            },
            cancellationToken);
    }

    public Task<PersistedCircleInvitation?> GetCircleInvitationAsync(
        InvitationId invitationId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            token => ReadCircleInvitationAsync(invitationId, token),
            cancellationToken);

    public Task<InvitationRedemptionResult> RedeemCircleInvitationAsync(
        InvitationId invitationId,
        ReadOnlyMemory<byte> packageSha256,
        RedemptionId redemptionId,
        DateTimeOffset redeemedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (packageSha256.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("Invitation digests must be SHA-256 values.", nameof(packageSha256));
        }

        if (redeemedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Redemption timestamps must be UTC.", nameof(redeemedAtUtc));
        }

        return ExecuteLockedAsync<InvitationRedemptionResult>(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var stored = await ReadCircleInvitationAsync(invitationId, token, transaction)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "invitation_not_found",
                        "The requested Circle invitation is not known to this Node.");
                if (!CryptographicOperations.FixedTimeEquals(
                        stored.PackageSha256,
                        packageSha256.Span))
                {
                    throw new LocalStateException(
                        "invitation_mismatch",
                        "The Circle invitation does not match the stored issued package.");
                }

                if (redeemedAtUtc >= stored.ExpiresAtUtc)
                {
                    return new(InvitationRedemptionStatus.Expired, null);
                }

                if (await HasInvitationRowAsync(
                        "revoked_invitations",
                        invitationId,
                        transaction,
                        token).ConfigureAwait(false))
                {
                    return new(InvitationRedemptionStatus.Revoked, null);
                }

                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO invitation_redemptions (
                        invitation_id, redemption_id, redeemed_at_utc)
                    VALUES ($invitation_id, $redemption_id, $redeemed_at_utc)
                    ON CONFLICT(invitation_id) DO NOTHING;
                    """;
                insert.Parameters.AddWithValue("$invitation_id", invitationId.ToString());
                insert.Parameters.AddWithValue("$redemption_id", redemptionId.ToString());
                insert.Parameters.AddWithValue("$redeemed_at_utc", Format(redeemedAtUtc));
                var inserted = await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return inserted == 1
                    ? new(InvitationRedemptionStatus.Accepted, redemptionId)
                    : new(InvitationRedemptionStatus.Replayed, null);
            },
            cancellationToken);
    }

    public Task RevokeCircleInvitationAsync(
        InvitationId invitationId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (revokedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Revocation timestamps must be UTC.", nameof(revokedAtUtc));
        }

        return ExecuteLockedAsync(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO revoked_invitations (invitation_id, revoked_at_utc)
                    VALUES ($invitation_id, $revoked_at_utc)
                    ON CONFLICT(invitation_id) DO NOTHING;
                    """;
                command.Parameters.AddWithValue("$invitation_id", invitationId.ToString());
                command.Parameters.AddWithValue("$revoked_at_utc", Format(revokedAtUtc));
                try
                {
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new LocalStateException(
                        "invitation_not_found",
                        "The requested Circle invitation is not known to this Node.");
                }
            },
            cancellationToken);
    }

    private static void ValidateInvitationMaterial(PersistedCircleInvitation invitation)
    {
        if (invitation.PackageSha256 is not { Length: SHA256.HashSizeInBytes }
            || invitation.EncodedPackage is not { Length: > 0 and <= 16 * 1024 }
            || !CryptographicOperations.FixedTimeEquals(
                invitation.PackageSha256,
                SHA256.HashData(invitation.EncodedPackage))
            || invitation.ExpiresAtUtc.Offset != TimeSpan.Zero
            || invitation.CreatedAtUtc.Offset != TimeSpan.Zero
            || invitation.ExpiresAtUtc <= invitation.CreatedAtUtc)
        {
            throw new ArgumentException("The persisted Circle invitation is invalid.", nameof(invitation));
        }
    }

    private static void AddInvitationParameters(
        SqliteCommand command,
        PersistedCircleInvitation invitation)
    {
        command.Parameters.AddWithValue("$invitation_id", invitation.InvitationId.ToString());
        command.Parameters.AddWithValue("$circle_id", invitation.CircleId.ToString());
        command.Parameters.AddWithValue("$digest", invitation.PackageSha256);
        command.Parameters.AddWithValue("$package", invitation.EncodedPackage);
        command.Parameters.AddWithValue("$expires_at_utc", Format(invitation.ExpiresAtUtc));
        command.Parameters.AddWithValue("$created_at_utc", Format(invitation.CreatedAtUtc));
    }

    private async Task<PersistedCircleInvitation?> ReadCircleInvitationAsync(
        InvitationId invitationId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT circle_id, package_sha256, encoded_package, expires_at_utc, created_at_utc
            FROM circle_invitations
            WHERE invitation_id = $invitation_id;
            """;
        command.Parameters.AddWithValue("$invitation_id", invitationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new PersistedCircleInvitation(
            invitationId,
            new CircleId(Guid.Parse(reader.GetString(0))),
            (byte[])reader.GetValue(1),
            (byte[])reader.GetValue(2),
            ParseTimestamp(reader.GetString(3)),
            ParseTimestamp(reader.GetString(4)));
    }

    private async Task<bool> HasInvitationRowAsync(
        string table,
        InvitationId invitationId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT 1 FROM {table} WHERE invitation_id = $invitation_id;";
        command.Parameters.AddWithValue("$invitation_id", invitationId.ToString());
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task MigrateV2ToV3Async(
        SqliteConnection connection,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = InvitationSchemaSql;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        NodeIdentity? node = null;
        using (var nodeCommand = connection.CreateCommand())
        {
            nodeCommand.Transaction = transaction;
            nodeCommand.CommandText =
                "SELECT node_id, display_name, created_at_utc FROM local_node WHERE singleton_id = 1;";
            await using var reader = await nodeCommand.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                node = new NodeIdentity(
                    new NodeId(Guid.Parse(reader.GetString(0))),
                    reader.GetString(1),
                    ParseTimestamp(reader.GetString(2)));
            }
        }

        if (node is not null)
        {
            await InsertMigratedTransportAsync(
                connection,
                transaction,
                node,
                protector,
                cancellationToken).ConfigureAwait(false);
        }

        using var version = connection.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
        await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertMigratedTransportAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NodeIdentity node,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        var material = GeneratePrivateIdentity(IdentityKeyRole.Transport, protector);
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO local_transport_credentials (
                    singleton_id, key_algorithm, key_id, public_key_spki,
                    private_key_scheme, protected_private_key, created_at_utc)
                VALUES (1, $algorithm, $key_id, $spki, $scheme, $private_key, $created_at_utc);
                """;
            AddIdentityParameters(command, material, node.CreatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.ProtectedPrivateKey);
        }
    }
}
