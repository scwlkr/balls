namespace Balls.Core;

public interface ILocalStateStore
{
    Task<NodeIdentity?> GetNodeAsync(CancellationToken cancellationToken = default);

    Task SaveNodeAsync(NodeIdentity node, CancellationToken cancellationToken = default);

    Task<CircleDetails> CreateCircleAsync(
        CreationRequestId requestId,
        CircleDetails circle,
        CancellationToken cancellationToken = default);

    Task<CircleDetails?> GetCircleAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CircleDetails>> ListCirclesAsync(
        CancellationToken cancellationToken = default);
}
