namespace Balls.Core;

public sealed class CircleApplication(
    ILocalStateStore store,
    TimeProvider timeProvider,
    string localNodeDisplayName)
{
    private readonly SemaphoreSlim nodeInitializationLock = new(1, 1);

    public async Task<CircleDetails> CreateCircleAsync(
        CreateCircleCommand command,
        CancellationToken cancellationToken = default)
    {
        var circleName = NormalizeRequired(
            command.CircleName,
            "circle_name_required",
            "Circle name is required.");
        if (circleName.Length > 100)
        {
            throw new InputValidationException(
                "circle_name_too_long",
                "Circle name cannot exceed 100 characters.");
        }

        var ownerDisplayName = NormalizeRequired(
            command.OwnerDisplayName,
            "owner_display_name_required",
            "Owner display name is required.");
        if (ownerDisplayName.Length > 100)
        {
            throw new InputValidationException(
                "owner_display_name_too_long",
                "Owner display name cannot exceed 100 characters.");
        }

        var node = await GetLocalNodeAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var circleId = CircleId.New();
        var circle = new Circle(circleId, circleName, now);
        var founder = new Member(
            MemberId.New(),
            circleId,
            ownerDisplayName,
            MemberRole.Owner,
            now);
        var enrolledNode = new CircleNode(circleId, node.Id, node.DisplayName, now);
        var details = new CircleDetails(circle, [founder], [enrolledNode]);

        return await store
            .CreateCircleAsync(command.RequestId, details, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NodeIdentity> GetLocalNodeAsync(CancellationToken cancellationToken = default)
    {
        var existing = await store.GetNodeAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        await nodeInitializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = await store.GetNodeAsync(cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var nodeDisplayName = NormalizeRequired(
                localNodeDisplayName,
                "node_display_name_required",
                "Node display name is required.");
            if (nodeDisplayName.Length > 100)
            {
                throw new InputValidationException(
                    "node_display_name_too_long",
                    "Node display name cannot exceed 100 characters.");
            }

            var created = new NodeIdentity(
                NodeId.New(),
                nodeDisplayName,
                timeProvider.GetUtcNow());
            await store.SaveNodeAsync(created, cancellationToken).ConfigureAwait(false);
            return created;
        }
        finally
        {
            nodeInitializationLock.Release();
        }
    }

    public Task<IReadOnlyList<CircleDetails>> ListCirclesAsync(
        CancellationToken cancellationToken = default)
    {
        return store.ListCirclesAsync(cancellationToken);
    }

    public Task<CircleDetails?> GetCircleAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default)
    {
        return store.GetCircleAsync(circleId, cancellationToken);
    }

    private static string NormalizeRequired(string? value, string code, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new InputValidationException(code, message);
        }

        return normalized;
    }
}
