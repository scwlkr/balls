namespace Balls.Core;

public enum MemberRole
{
    Owner = 1,
    Member = 2,
}

public sealed record NodeIdentity(
    NodeId Id,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);

public sealed record Circle(
    CircleId Id,
    string Name,
    DateTimeOffset CreatedAtUtc);

public sealed record Member(
    MemberId Id,
    CircleId CircleId,
    string DisplayName,
    MemberRole Role,
    DateTimeOffset JoinedAtUtc);

public sealed record CircleNode(
    CircleId CircleId,
    NodeId NodeId,
    string DisplayName,
    DateTimeOffset JoinedAtUtc);

public sealed record CircleDetails(
    Circle Circle,
    IReadOnlyList<Member> Members,
    IReadOnlyList<CircleNode> Nodes);

public sealed record CreateCircleCommand(
    CreationRequestId RequestId,
    string? CircleName,
    string? OwnerDisplayName);
