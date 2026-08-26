using Balls.Core;

namespace Balls.Storage.Sqlite;

public sealed record CircleFilesHostedFolderBinding(
    CircleId CircleId,
    CircleFilesContributionId ContributionId,
    CircleFilesProviderId ProviderId,
    NodeId NodeId,
    string FolderPath);

public interface ICircleFilesHostedFolderStore
{
    Task<CircleFilesHostedFolderBinding?> GetCircleFilesHostedFolderAsync(
        CircleId circleId,
        CircleFilesContributionId contributionId,
        CancellationToken cancellationToken = default);

    Task SaveCircleFilesHostedFolderAsync(
        CircleFilesHostedFolderBinding binding,
        CancellationToken cancellationToken = default);
}
