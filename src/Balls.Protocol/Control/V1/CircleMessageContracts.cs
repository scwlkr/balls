namespace Balls.Protocol.Control.V1;

public sealed record SendCircleMessageRequest(
    string RequestId,
    string Endpoint,
    string Text);

public sealed record CircleMessageResponse(
    string Id,
    string CircleId,
    string AuthorMemberId,
    string AuthorNodeId,
    string Text,
    DateTimeOffset AuthoredAtUtc,
    long Sequence,
    DateTimeOffset AcceptedAtUtc);

public sealed record CircleMessageListResponse(
    string CircleId,
    IReadOnlyList<CircleMessageResponse> Messages);
