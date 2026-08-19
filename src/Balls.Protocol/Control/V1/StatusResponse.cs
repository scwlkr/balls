namespace Balls.Protocol.Control.V1;

public sealed record StatusResponse(
    string ProductVersion,
    int ProtocolVersion,
    NodeResponse Node);

public sealed record NodeResponse(
    string Id,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);
