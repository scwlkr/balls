using System.Security.Cryptography;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore : ICircleFilesProviderCredentialStore
{
    private const string CircleFilesProviderCredentialSchemaSql =
        """
        CREATE TABLE circle_files_provider_credentials (
            grant_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            contribution_id TEXT NOT NULL,
            member_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            account_name TEXT NOT NULL UNIQUE,
            ownership_id TEXT NOT NULL UNIQUE,
            access TEXT NOT NULL,
            generation INTEGER NOT NULL,
            lifecycle INTEGER NOT NULL,
            secret_scheme TEXT NOT NULL,
            protected_secret BLOB NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (grant_id) REFERENCES circle_files_access_grants(grant_id) ON DELETE CASCADE,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (contribution_id) REFERENCES circle_files_contributions(contribution_id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(member_id)
        );
        """;

    public Task<CircleFilesProviderCredentialMaterial?> GetActiveCircleFilesProviderCredentialAsync(
        string grantId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                if (!Guid.TryParseExact(grantId, "D", out _))
                {
                    throw new ArgumentException("Grant ID must be a canonical UUID.", nameof(grantId));
                }
                var existing = await ReadProviderCredentialAsync(grantId, null, token)
                    .ConfigureAwait(false);
                if (existing is null || existing.Value.Lifecycle != 2) return null;
                return new CircleFilesProviderCredentialMaterial(
                    existing.Value.Binding,
                    UnprotectProviderSecret(existing.Value.Scheme, existing.Value.Protected),
                    isNew: false,
                    isActive: true);
            },
            cancellationToken);

    public Task<CircleFilesProviderCredentialMaterial> PrepareCircleFilesProviderCredentialAsync(
        CircleFilesProviderCredentialBinding binding,
        ReadOnlyMemory<byte> candidateSecret,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                ValidateProviderCredentialBinding(binding);
                if (candidateSecret.Length is < 24 or > 128)
                {
                    throw new ArgumentException("The provider credential secret is invalid.", nameof(candidateSecret));
                }

                using var transaction = connection.BeginTransaction();
                var existing = await ReadProviderCredentialAsync(binding.GrantId, transaction, token)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    EnsureEquivalentProviderCredential(binding, existing.Value.Binding);
                    var secret = UnprotectProviderSecret(existing.Value.Scheme, existing.Value.Protected);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return new CircleFilesProviderCredentialMaterial(
                        existing.Value.Binding,
                        secret,
                        isNew: false,
                        isActive: existing.Value.Lifecycle == 2);
                }

                var protectedSecret = privateMaterialProtector.Protect(candidateSecret.Span);
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT INTO circle_files_provider_credentials (
                            grant_id, circle_id, contribution_id, member_id, provider,
                            account_name, ownership_id, access, generation, lifecycle,
                            secret_scheme, protected_secret, created_at_utc)
                        VALUES (
                            $grant_id, $circle_id, $contribution_id, $member_id, $provider,
                            $account_name, $ownership_id, $access, $generation, 1,
                            $scheme, $protected, $created_at);
                        """;
                    AddProviderCredentialParameters(
                        command,
                        binding,
                        privateMaterialProtector.Scheme,
                        protectedSecret);
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return new CircleFilesProviderCredentialMaterial(
                        binding,
                        candidateSecret.ToArray(),
                        isNew: true,
                        isActive: false);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new LocalStateConflictException(
                        "circle_files_provider_credential_conflict",
                        "The Windows provider credential identity is already in use.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedSecret);
                }
            },
            cancellationToken);

    public Task CompleteCircleFilesProviderCredentialAsync(
        CircleFilesProviderCredentialBinding binding,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadProviderCredentialAsync(binding.GrantId, transaction, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_files_provider_credential_missing",
                        "The Windows provider credential state is missing.");
                EnsureEquivalentProviderCredential(binding, existing.Binding);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE circle_files_provider_credentials
                    SET lifecycle = 2
                    WHERE grant_id = $grant_id AND lifecycle IN (1, 2);
                    """;
                command.Parameters.AddWithValue("$grant_id", binding.GrantId);
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                {
                    throw new LocalStateException(
                        "circle_files_provider_credential_conflict",
                        "The Windows provider credential state changed unexpectedly.");
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    private byte[] UnprotectProviderSecret(string scheme, byte[] protectedSecret)
    {
        if (!string.Equals(scheme, privateMaterialProtector.Scheme, StringComparison.Ordinal))
        {
            throw InvalidProviderCredential();
        }

        try
        {
            var secret = privateMaterialProtector.Unprotect(protectedSecret);
            if (secret.Length is < 24 or > 128)
            {
                CryptographicOperations.ZeroMemory(secret);
                throw InvalidProviderCredential();
            }

            return secret;
        }
        catch (LocalStateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw InvalidProviderCredential();
        }
    }

    private async Task<(CircleFilesProviderCredentialBinding Binding, int Lifecycle, string Scheme,
        byte[] Protected)?> ReadProviderCredentialAsync(
            string grantId,
            SqliteTransaction? transaction,
            CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT grant_id, circle_id, contribution_id, member_id, provider,
                   account_name, ownership_id, access, generation, lifecycle,
                   secret_scheme, protected_secret
            FROM circle_files_provider_credentials
            WHERE grant_id = $grant_id;
            """;
        command.Parameters.AddWithValue("$grant_id", grantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (
            new CircleFilesProviderCredentialBinding(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetInt64(8)),
            reader.GetInt32(9),
            reader.GetString(10),
            (byte[])reader.GetValue(11));
    }

    private static void AddProviderCredentialParameters(
        SqliteCommand command,
        CircleFilesProviderCredentialBinding binding,
        string scheme,
        byte[] protectedSecret)
    {
        command.Parameters.AddWithValue("$grant_id", binding.GrantId);
        command.Parameters.AddWithValue("$circle_id", binding.CircleId);
        command.Parameters.AddWithValue("$contribution_id", binding.ContributionId);
        command.Parameters.AddWithValue("$member_id", binding.MemberId);
        command.Parameters.AddWithValue("$provider", binding.Provider);
        command.Parameters.AddWithValue("$account_name", binding.AccountName);
        command.Parameters.AddWithValue("$ownership_id", binding.OwnershipId);
        command.Parameters.AddWithValue("$access", binding.Access);
        command.Parameters.AddWithValue("$generation", binding.Generation);
        command.Parameters.AddWithValue("$scheme", scheme);
        command.Parameters.AddWithValue("$protected", protectedSecret);
        command.Parameters.AddWithValue("$created_at", Format(DateTimeOffset.UtcNow));
    }

    private static void EnsureEquivalentProviderCredential(
        CircleFilesProviderCredentialBinding expected,
        CircleFilesProviderCredentialBinding actual)
    {
        if (expected != actual)
        {
            throw new LocalStateConflictException(
                "circle_files_provider_credential_conflict",
                "The grant already has different Windows provider credential state.");
        }
    }

    private static void ValidateProviderCredentialBinding(CircleFilesProviderCredentialBinding value)
    {
        if (!Guid.TryParseExact(value.GrantId, "D", out _)
            || !Guid.TryParseExact(value.CircleId, "D", out _)
            || !Guid.TryParseExact(value.ContributionId, "D", out _)
            || !Guid.TryParseExact(value.MemberId, "D", out _)
            || value.Provider != "windows-smb-3.1.1-v1"
            || value.AccountName.Length is < 1 or > 20
            || value.OwnershipId.Length != 64
            || value.Access is not ("read-only" or "read-write")
            || value.Generation <= 0)
        {
            throw new ArgumentException("The provider credential binding is invalid.", nameof(value));
        }
    }

    private static LocalStateException InvalidProviderCredential() => new(
        "invalid_private_material",
        "Protected Circle Files provider material is unreadable or invalid; state was left unchanged.");

    private static async Task MigrateV6ToV7Async(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        await ExecuteV6ToV7MigrationAsync(
            connection,
            beforeCommit: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task ExecuteV6ToV7MigrationAsync(
        SqliteConnection connection,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CircleFilesProviderCredentialSchemaSql + " PRAGMA user_version = 7;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (beforeCommit is not null)
        {
            await beforeCommit(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCircleFilesProviderCredentialExpectedTable(
        IDictionary<string, TableSchema> tables) =>
        tables["circle_files_provider_credentials"] = new(
            [
                new("grant_id", "TEXT", 1), new("circle_id", "TEXT", 0),
                new("contribution_id", "TEXT", 0), new("member_id", "TEXT", 0),
                new("provider", "TEXT", 0), new("account_name", "TEXT", 0),
                new("ownership_id", "TEXT", 0), new("access", "TEXT", 0),
                new("generation", "INTEGER", 0), new("lifecycle", "INTEGER", 0),
                new("secret_scheme", "TEXT", 0), new("protected_secret", "BLOB", 0),
                new("created_at_utc", "TEXT", 0),
            ],
            [
                new("circle_files_access_grants", "grant_id", "grant_id", "CASCADE"),
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("circle_files_contributions", "contribution_id", "contribution_id", "CASCADE"),
                new("members", "member_id", "member_id", "NO ACTION"),
            ]);

    private static async Task ValidateCircleFilesProviderCredentialsAsync(
        SqliteConnection connection,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT secret_scheme, protected_secret FROM circle_files_provider_credentials;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var protectedSecret = (byte[])reader.GetValue(1);
            byte[]? secret = null;
            try
            {
                if (reader.GetString(0) != protector.Scheme)
                {
                    throw InvalidProviderCredential();
                }

                secret = protector.Unprotect(protectedSecret);
                if (secret.Length is < 24 or > 128)
                {
                    throw InvalidProviderCredential();
                }
            }
            catch (LocalStateException)
            {
                throw;
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                throw InvalidProviderCredential();
            }
            finally
            {
                if (secret is not null)
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
                CryptographicOperations.ZeroMemory(protectedSecret);
            }
        }
    }
}
