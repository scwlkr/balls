using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore : ICircleFilesHostedFolderStore
{
    private const string CircleFilesHostedFolderSchemaSql =
        """
        CREATE TABLE circle_files_hosted_folders (
            contribution_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            provider_id TEXT NOT NULL UNIQUE,
            node_id TEXT NOT NULL,
            folder_path TEXT NOT NULL,
            FOREIGN KEY (contribution_id) REFERENCES circle_files_contributions(contribution_id) ON DELETE CASCADE,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (node_id) REFERENCES nodes(node_id)
        );
        """;

    public Task<CircleFilesHostedFolderBinding?> GetCircleFilesHostedFolderAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT circle_id, contribution_id, provider_id, node_id, folder_path
                    FROM circle_files_hosted_folders
                    WHERE circle_id = $circle_id AND contribution_id = $contribution_id;
                    """;
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                command.Parameters.AddWithValue("$contribution_id", contributionId.ToString());
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    return null;
                }

                return new CircleFilesHostedFolderBinding(
                    new CircleId(Guid.ParseExact(reader.GetString(0), "D")),
                    new CircleFilesContributionId(Guid.ParseExact(reader.GetString(1), "D")),
                    new CircleFilesProviderId(Guid.ParseExact(reader.GetString(2), "D")),
                    new NodeId(Guid.ParseExact(reader.GetString(3), "D")),
                    reader.GetString(4));
            },
            cancellationToken);

    public Task SaveCircleFilesHostedFolderAsync(
        CircleFilesHostedFolderBinding binding,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                ValidateHostedFolderBinding(binding);
                using var transaction = connection.BeginTransaction();
                var existing = await ReadHostedFolderAsync(
                    binding.ContributionId,
                    transaction,
                    token).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing != binding)
                    {
                        throw new LocalStateConflictException(
                            "circle_files_hosted_folder_conflict",
                            "The contribution is already bound to a different hosted folder.");
                    }

                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return;
                }

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO circle_files_hosted_folders (
                        contribution_id, circle_id, provider_id, node_id, folder_path)
                    VALUES (
                        $contribution_id, $circle_id, $provider_id, $node_id, $folder_path);
                    """;
                command.Parameters.AddWithValue(
                    "$contribution_id",
                    binding.ContributionId.ToString());
                command.Parameters.AddWithValue("$circle_id", binding.CircleId.ToString());
                command.Parameters.AddWithValue("$provider_id", binding.ProviderId.ToString());
                command.Parameters.AddWithValue("$node_id", binding.NodeId.ToString());
                command.Parameters.AddWithValue("$folder_path", binding.FolderPath);
                try
                {
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new LocalStateConflictException(
                        "circle_files_hosted_folder_conflict",
                        "The hosted folder binding conflicts with existing Circle Files state.");
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    internal static async Task MigrateV9ToV10Async(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeCommit = null)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CircleFilesHostedFolderSchemaSql + " PRAGMA user_version = 10;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (beforeCommit is not null)
        {
            await beforeCommit(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<CircleFilesHostedFolderBinding?> ReadHostedFolderAsync(
        CircleFilesContributionId contributionId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT circle_id, contribution_id, provider_id, node_id, folder_path
            FROM circle_files_hosted_folders
            WHERE contribution_id = $contribution_id;
            """;
        command.Parameters.AddWithValue("$contribution_id", contributionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new CircleFilesHostedFolderBinding(
            new CircleId(Guid.ParseExact(reader.GetString(0), "D")),
            new CircleFilesContributionId(Guid.ParseExact(reader.GetString(1), "D")),
            new CircleFilesProviderId(Guid.ParseExact(reader.GetString(2), "D")),
            new NodeId(Guid.ParseExact(reader.GetString(3), "D")),
            reader.GetString(4));
    }

    private static void ValidateHostedFolderBinding(CircleFilesHostedFolderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.CircleId.Value == Guid.Empty
            || binding.ContributionId.Value == Guid.Empty
            || binding.ProviderId.Value == Guid.Empty
            || binding.NodeId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(binding.FolderPath)
            || binding.FolderPath.Length > 1024
            || binding.FolderPath.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("The hosted folder binding is invalid.", nameof(binding));
        }
    }

    private static void AddCircleFilesHostedFolderExpectedTable(
        IDictionary<string, TableSchema> tables)
    {
        tables["circle_files_hosted_folders"] = new(
            [
                new("contribution_id", "TEXT", 1),
                new("circle_id", "TEXT", 0),
                new("provider_id", "TEXT", 0),
                new("node_id", "TEXT", 0),
                new("folder_path", "TEXT", 0),
            ],
            [
                new("circle_files_contributions", "contribution_id", "contribution_id", "CASCADE"),
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("nodes", "node_id", "node_id", "NO ACTION"),
            ]);
    }
}
