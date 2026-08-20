namespace Balls.Protocol.Control.V1;

public sealed record CreateCircleRequest(
    string RequestId,
    string Name,
    string OwnerDisplayName);

public sealed record JoinCircleRequest(
    string Package,
    string Endpoint,
    string MemberDisplayName);

public sealed record CircleResponse(
    string Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    int MemberCount,
    int NodeCount);

public sealed record MemberResponse(
    string Id,
    string DisplayName,
    string Role,
    DateTimeOffset JoinedAtUtc);

public sealed record CircleNodeResponse(
    string Id,
    string DisplayName,
    DateTimeOffset JoinedAtUtc);

public sealed record CircleDetailsResponse(
    CircleResponse Circle,
    IReadOnlyList<MemberResponse> Members,
    IReadOnlyList<CircleNodeResponse> Nodes);

public sealed record CircleListResponse(IReadOnlyList<CircleResponse> Circles);

public sealed record MemberListResponse(
    string CircleId,
    IReadOnlyList<MemberResponse> Members);

public sealed record NodeListResponse(
    string CircleId,
    IReadOnlyList<CircleNodeResponse> Nodes);

public sealed record SendCircleMessageRequest(
    string MessageId,
    string Endpoint,
    string Text);

public sealed record CircleMessageResponse(
    string Id,
    string CircleId,
    long Sequence,
    string AuthorMemberId,
    string AuthorNodeId,
    string Text,
    DateTimeOffset AuthoredAtUtc,
    DateTimeOffset AcceptedAtUtc);

public sealed record CircleMessageListResponse(
    string CircleId,
    IReadOnlyList<CircleMessageResponse> Messages);

public sealed record ErrorResponse(string Code, string Message);
