using System.Security.Cryptography;
using System.Text.Json;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    private const int CircleConnectionVersion = 1;
    private const int MaximumProtectedConnectionLength = 4 * 1024;
    private const string CircleConnectionSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS circle_connections (
            circle_id TEXT NOT NULL PRIMARY KEY,
            protection_scheme TEXT NOT NULL,
            protected_connection BLOB NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE
        );
        """;

    public Task<CircleConnectionState?> GetCircleConnectionAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            token => ReadCircleConnectionAsync(circleId, transaction: null, token),
            cancellationToken);

    public Task StoreCircleConnectionAsync(
        CircleConnectionState state,
        CancellationToken cancellationToken = default)
    {
        ValidateCircleConnection(state);
        return ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                await InsertOrValidateCircleConnectionAsync(state, transaction, token)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private async Task InsertOrValidateCircleConnectionAsync(
        CircleConnectionState state,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCircleConnectionAsync(
            state.CircleId,
            transaction,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Version != state.Version
                || !string.Equals(existing.Provider, state.Provider, StringComparison.Ordinal)
                || !string.Equals(
                    existing.AdmissionEndpoint,
                    state.AdmissionEndpoint,
                    StringComparison.Ordinal)
                || !string.Equals(existing.SyncEndpoint, state.SyncEndpoint, StringComparison.Ordinal))
            {
                throw new LocalStateConflictException(
                    "circle_connection_conflict",
                    "This Circle is already connected through different private invitation details.");
            }

            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new CircleConnectionPayload(
                state.Version,
                state.Provider,
                state.AdmissionEndpoint,
                state.SyncEndpoint,
                state.StoredAtUtc));
        byte[]? protectedPayload = null;
        try
        {
            protectedPayload = privateMaterialProtector.Protect(payload);
            if (protectedPayload is not { Length: > 0 and <= MaximumProtectedConnectionLength })
            {
                throw InvalidCircleConnection();
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO circle_connections (
                    circle_id, protection_scheme, protected_connection)
                VALUES ($circle_id, $scheme, $protected);
                """;
            command.Parameters.AddWithValue("$circle_id", state.CircleId.ToString());
            command.Parameters.AddWithValue("$scheme", privateMaterialProtector.Scheme);
            command.Parameters.AddWithValue("$protected", protectedPayload);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            if (protectedPayload is not null)
            {
                CryptographicOperations.ZeroMemory(protectedPayload);
            }
        }
    }

    private async Task<CircleConnectionState?> ReadCircleConnectionAsync(
        CircleId circleId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        string scheme;
        byte[] protectedPayload;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT protection_scheme, protected_connection
                FROM circle_connections
                WHERE circle_id = $circle_id;
                """;
            command.Parameters.AddWithValue("$circle_id", circleId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            scheme = reader.GetString(0);
            protectedPayload = (byte[])reader.GetValue(1);
        }

        byte[]? payload = null;
        try
        {
            if (!string.Equals(scheme, privateMaterialProtector.Scheme, StringComparison.Ordinal)
                || protectedPayload is not { Length: > 0 and <= MaximumProtectedConnectionLength })
            {
                throw InvalidCircleConnection();
            }

            payload = privateMaterialProtector.Unprotect(protectedPayload);
            var decoded = JsonSerializer.Deserialize<CircleConnectionPayload>(payload)
                ?? throw InvalidCircleConnection();
            var state = new CircleConnectionState(
                circleId,
                decoded.Version,
                decoded.Provider,
                decoded.AdmissionEndpoint,
                decoded.SyncEndpoint,
                decoded.StoredAtUtc);
            ValidateCircleConnection(state);
            return state;
        }
        catch (LocalStateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            CryptographicException or JsonException or ArgumentException or FormatException)
        {
            throw InvalidCircleConnection();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private static void ValidateCircleConnection(CircleConnectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != CircleConnectionVersion
            || !IsBoundedAscii(state.Provider, 64)
            || !IsBoundedAscii(state.AdmissionEndpoint, 128)
            || !IsBoundedAscii(state.SyncEndpoint, 128)
            || state.StoredAtUtc.Offset != TimeSpan.Zero)
        {
            throw InvalidCircleConnection();
        }
    }

    private static bool IsBoundedAscii(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(character => character is >= (char)0x21 and <= (char)0x7e);

    private static LocalStateException InvalidCircleConnection() =>
        new(
            "invalid_circle_connection",
            "The saved private Circle connection is invalid and was left unchanged.");

    private static async Task ValidateCircleConnectionsAsync(
        SqliteConnection connection,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT circle_id, protection_scheme, protected_connection FROM circle_connections;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParseExact(reader.GetString(0), "D", out var parsedCircleId)
                || parsedCircleId == Guid.Empty)
            {
                throw InvalidCircleConnection();
            }

            var scheme = reader.GetString(1);
            var protectedPayload = (byte[])reader.GetValue(2);
            byte[]? payload = null;
            try
            {
                if (!string.Equals(scheme, protector.Scheme, StringComparison.Ordinal)
                    || protectedPayload is not { Length: > 0 and <= MaximumProtectedConnectionLength })
                {
                    throw InvalidCircleConnection();
                }

                payload = protector.Unprotect(protectedPayload);
                var decoded = JsonSerializer.Deserialize<CircleConnectionPayload>(payload)
                    ?? throw InvalidCircleConnection();
                ValidateCircleConnection(
                    new CircleConnectionState(
                        new CircleId(parsedCircleId),
                        decoded.Version,
                        decoded.Provider,
                        decoded.AdmissionEndpoint,
                        decoded.SyncEndpoint,
                        decoded.StoredAtUtc));
            }
            catch (LocalStateException)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                CryptographicException or JsonException or ArgumentException or FormatException)
            {
                throw InvalidCircleConnection();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedPayload);
                if (payload is not null)
                {
                    CryptographicOperations.ZeroMemory(payload);
                }
            }
        }
    }

    internal static async Task MigrateV8ToV9Async(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeCommit = null)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CircleConnectionSchemaSql + " PRAGMA user_version = 9;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (beforeCommit is not null)
        {
            await beforeCommit(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCircleConnectionExpectedTable(
        IDictionary<string, TableSchema> tables)
    {
        tables["circle_connections"] = new(
            [
                new("circle_id", "TEXT", 1),
                new("protection_scheme", "TEXT", 0),
                new("protected_connection", "BLOB", 0),
            ],
            [new("circles", "circle_id", "circle_id", "CASCADE")]);
    }

    private sealed record CircleConnectionPayload(
        int Version,
        string Provider,
        string AdmissionEndpoint,
        string SyncEndpoint,
        DateTimeOffset StoredAtUtc);
}
