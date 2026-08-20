namespace Balls.Protocol.Control.V1;

public sealed record CreateInvitationRequest(int ValidForMinutes);

public sealed record CreateInvitationResponse(
    string CircleId,
    string InvitationId,
    DateTimeOffset ExpiresAtUtc,
    string Package);

public sealed record RedeemInvitationRequest(string Package);

public sealed record RedeemInvitationResponse(
    string CircleId,
    string InvitationId,
    string RedemptionId,
    string Status);
