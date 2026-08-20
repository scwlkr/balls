namespace Balls.Protocol.Remote.V1;

public static class RemoteSecurityProtocol
{
    public const int Version = 1;
    public const string SignatureSuite = "ecdsa-p256-sha256-p1363";
    public const string Alpn = "balls-circle/1";
}

public enum KeyRole
{
    CircleAuthority,
    Anchor,
    Member,
    Node,
    Transport,
}

public sealed record PublicKeyCredential(
    KeyRole Role,
    string Algorithm,
    string KeyId,
    byte[] SubjectPublicKeyInfo);

public sealed record CircleInvitation(
    string CircleId,
    string InvitationId,
    string IssuerId,
    string IssuerKeyId,
    string AnchorTransportKeyId,
    long AuthorityGeneration,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaximumRedemptions,
    int MinimumProtocolVersion,
    int MaximumProtocolVersion,
    byte[] InvitationNonce);

public sealed record SignedCircleInvitation(
    CircleInvitation Invitation,
    string SignatureSuite,
    byte[] IssuerSignature);

public sealed record InvitationIssuerDelegation(
    string CircleId,
    long AuthorityGeneration,
    string RootKeyId,
    string IssuerId,
    PublicKeyCredential IssuerCredential,
    string Authorization,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record SignedInvitationIssuerDelegation(
    InvitationIssuerDelegation Delegation,
    string SignatureSuite,
    byte[] RootSignature);

public sealed record CircleInvitationPackage(
    int Version,
    PublicKeyCredential RootCredential,
    SignedInvitationIssuerDelegation IssuerDelegation,
    SignedCircleInvitation Invitation);

public sealed record InvitationVerificationContext(
    string ExpectedCircleId,
    PublicKeyCredential TrustedRootCredential,
    DateTimeOffset NowUtc,
    long MinimumAuthorityGeneration,
    InvitationUseState InvitationState,
    IReadOnlySet<string> RevokedKeyIds);

public enum InvitationRejectionCode
{
    None,
    Malformed,
    UnsupportedVersion,
    UnsupportedSuite,
    UnauthorizedIssuer,
    Forged,
    Revoked,
    StaleAuthorityState,
    WrongCircle,
    NotYetValid,
    Expired,
    Replayed,
}

public sealed record InvitationValidationResult(
    bool IsValid,
    InvitationRejectionCode RejectionCode)
{
    public static InvitationValidationResult Valid() =>
        new(true, InvitationRejectionCode.None);

    public static InvitationValidationResult Rejected(InvitationRejectionCode rejectionCode) =>
        new(false, rejectionCode);
}

public sealed record AdmissionRequest(
    string CircleId,
    string InvitationId,
    string MemberId,
    PublicKeyCredential MemberCredential,
    string NodeId,
    PublicKeyCredential NodeCredential,
    PublicKeyCredential TransportCredential,
    int MinimumProtocolVersion,
    int MaximumProtocolVersion,
    int SelectedProtocolVersion,
    string SignatureSuite,
    string Alpn,
    byte[] InvitationDigest,
    byte[] AnchorChallenge,
    byte[] ApplicantChallenge);

public sealed record SignedAdmissionRequest(
    SignedCircleInvitation Invitation,
    AdmissionRequest Request,
    byte[] MemberSignature,
    byte[] NodeSignature);

public enum InvitationUseState
{
    Available,
    Consumed,
    Revoked,
}

public sealed record AdmissionVerificationContext(
    string ExpectedCircleId,
    string TrustedIssuerId,
    PublicKeyCredential TrustedIssuerCredential,
    PublicKeyCredential PeerTransportCredential,
    byte[] ExpectedAnchorChallenge,
    DateTimeOffset NowUtc,
    InvitationUseState InvitationState,
    int SupportedMinimumProtocolVersion,
    int SupportedMaximumProtocolVersion,
    long MinimumAuthorityGeneration,
    IReadOnlySet<string> RevokedKeyIds);

public enum AdmissionRejectionCode
{
    None,
    Malformed,
    UnsupportedSuite,
    UnauthorizedIssuer,
    Forged,
    Revoked,
    StaleAuthorityState,
    WrongCircle,
    WrongNode,
    NotYetValid,
    Expired,
    Replayed,
    Downgraded,
}

public sealed record AdmissionValidationResult(
    bool IsAccepted,
    AdmissionRejectionCode RejectionCode,
    int? NegotiatedProtocolVersion)
{
    public static AdmissionValidationResult Accepted(int negotiatedProtocolVersion) =>
        new(true, AdmissionRejectionCode.None, negotiatedProtocolVersion);

    public static AdmissionValidationResult Rejected(AdmissionRejectionCode rejectionCode) =>
        new(false, rejectionCode, null);
}

public sealed record RemoteTransportAddress(string Provider, string Value);

public sealed record UntrustedRemoteConnection(
    Stream Stream,
    string Provider,
    string PeerAddress) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

public interface IRemoteTransportConnector
{
    ValueTask<UntrustedRemoteConnection> ConnectAsync(
        RemoteTransportAddress address,
        CancellationToken cancellationToken = default);
}

public interface IRemoteTransportListener : IAsyncDisposable
{
    IAsyncEnumerable<UntrustedRemoteConnection> AcceptAsync(
        CancellationToken cancellationToken = default);
}
