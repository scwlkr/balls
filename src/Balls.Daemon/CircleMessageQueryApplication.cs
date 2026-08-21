using Balls.Core;
using Balls.Protocol.Control.V1;

namespace Balls.Daemon;

internal sealed class CircleMessageQueryApplication(ICircleMessageStateStore messages)
{
    internal async Task<CircleMessageListResponse> ListAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default)
    {
        var values = await messages.ListCircleMessagesAsync(circleId, cancellationToken)
            .ConfigureAwait(false);
        return new CircleMessageListResponse(
            circleId.ToString(),
            values.Select(ToResponse).ToArray());
    }

    internal static CircleMessageResponse ToResponse(PersistedCircleMessage message) =>
        new(
            message.Id.ToString(),
            message.CircleId.ToString(),
            message.AuthorMemberId.ToString(),
            message.AuthorNodeId.ToString(),
            message.Text,
            message.AuthoredAtUtc,
            message.Sequence,
            message.AcceptedAtUtc);
}
