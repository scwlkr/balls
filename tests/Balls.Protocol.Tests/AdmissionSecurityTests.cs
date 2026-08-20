using System.Security.Cryptography;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class AdmissionSecurityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void P256_credentials_and_signatures_have_stable_wire_forms()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = RemoteIdentity.CreateCredential(KeyRole.Member, key);
        var message = "balls-security-spike"u8.ToArray();

        var signature = RemoteIdentity.Sign(message, key);

        Assert.IsTrue(credential.KeyId.StartsWith("member:p256-sha256:", StringComparison.Ordinal));
        Assert.AreEqual(91, credential.SubjectPublicKeyInfo.Length);
        Assert.AreEqual(64, signature.Length);
        Assert.IsTrue(RemoteIdentity.Verify(message, signature, credential));
    }

    [TestMethod]
    public void Canonical_invitation_bytes_are_stable()
    {
        using var issuerKey = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Convert.FromHexString(
                    "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"),
                Y = Convert.FromHexString(
                    "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"),
            },
        });
        var issuer = RemoteIdentity.CreateCredential(KeyRole.Anchor, issuerKey);
        var invitation = CreateInvitation(issuer);

        var encoded = AdmissionSecurity.EncodeInvitation(invitation);

        Assert.AreEqual(
            "IvpdShOQvFx3KjkLyFMTvomHVwsU895bcrJKQz8oC_Y",
            RemoteIdentity.Base64Url(SHA256.HashData(encoded)));
    }

    [TestMethod]
    public void Valid_admission_is_accepted_at_the_highest_common_version()
    {
        using var fixture = new AdmissionFixture();

        var result = AdmissionSecurity.Validate(fixture.SignedRequest, fixture.Context);

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(1, result.NegotiatedProtocolVersion);
        Assert.AreEqual(AdmissionRejectionCode.None, result.RejectionCode);
    }

    [TestMethod]
    [DataRow(AdmissionMutation.Forged, AdmissionRejectionCode.Forged)]
    [DataRow(AdmissionMutation.Expired, AdmissionRejectionCode.Expired)]
    [DataRow(AdmissionMutation.Replayed, AdmissionRejectionCode.Replayed)]
    [DataRow(AdmissionMutation.Downgraded, AdmissionRejectionCode.Downgraded)]
    [DataRow(AdmissionMutation.WrongCircle, AdmissionRejectionCode.WrongCircle)]
    [DataRow(AdmissionMutation.WrongNode, AdmissionRejectionCode.WrongNode)]
    [DataRow(AdmissionMutation.UnauthorizedIssuer, AdmissionRejectionCode.UnauthorizedIssuer)]
    [DataRow(AdmissionMutation.Revoked, AdmissionRejectionCode.Revoked)]
    [DataRow(AdmissionMutation.StaleAuthority, AdmissionRejectionCode.StaleAuthorityState)]
    public void Invalid_admission_has_a_deterministic_rejection(
        AdmissionMutation mutation,
        AdmissionRejectionCode expected)
    {
        using var fixture = new AdmissionFixture();
        var (request, context) = fixture.Mutate(mutation);

        var first = AdmissionSecurity.Validate(request, context);
        var second = AdmissionSecurity.Validate(request, context);

        Assert.IsFalse(first.IsAccepted);
        Assert.AreEqual(expected, first.RejectionCode);
        Assert.AreEqual(first, second);
    }

    private static CircleInvitation CreateInvitation(PublicKeyCredential issuer) => new(
        CircleId: "0198c837-3000-7000-8000-000000000001",
        InvitationId: "0198c837-3000-7000-8000-000000000002",
        IssuerId: "0198c837-3000-7000-8000-000000000003",
        IssuerKeyId: issuer.KeyId,
        AnchorTransportKeyId:
            "transport:p256-sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        AuthorityGeneration: 7,
        NotBeforeUtc: Now.AddMinutes(-1),
        ExpiresAtUtc: Now.AddHours(1),
        MaximumRedemptions: 1,
        MinimumProtocolVersion: 1,
        MaximumProtocolVersion: 1,
        InvitationNonce: Convert.FromHexString(
            "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF"));

    public enum AdmissionMutation
    {
        Forged,
        Expired,
        Replayed,
        Downgraded,
        WrongCircle,
        WrongNode,
        UnauthorizedIssuer,
        Revoked,
        StaleAuthority,
    }

    private sealed class AdmissionFixture : IDisposable
    {
        private readonly ECDsa issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa nodeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa transportKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly PublicKeyCredential issuer;
        private readonly PublicKeyCredential transport;

        public AdmissionFixture()
        {
            issuer = RemoteIdentity.CreateCredential(KeyRole.Anchor, issuerKey);
            var invitation = AdmissionSecurity.SignInvitation(
                CreateInvitation(issuer),
                issuerKey);
            var request = new AdmissionRequest(
                CircleId: invitation.Invitation.CircleId,
                InvitationId: invitation.Invitation.InvitationId,
                MemberId: "0198c837-3000-7000-8000-000000000004",
                MemberCredential: RemoteIdentity.CreateCredential(KeyRole.Member, memberKey),
                NodeId: "0198c837-3000-7000-8000-000000000005",
                NodeCredential: RemoteIdentity.CreateCredential(KeyRole.Node, nodeKey),
                TransportCredential: RemoteIdentity.CreateCredential(KeyRole.Transport, transportKey),
                MinimumProtocolVersion: 1,
                MaximumProtocolVersion: 1,
                SelectedProtocolVersion: 1,
                SignatureSuite: RemoteSecurityProtocol.SignatureSuite,
                Alpn: RemoteSecurityProtocol.Alpn,
                InvitationDigest: AdmissionSecurity.DigestInvitation(invitation),
                AnchorChallenge: Convert.FromHexString(
                    "102132435465768798A9BACBDCEDFE0F102132435465768798A9BACBDCEDFE0F"),
                ApplicantChallenge: Convert.FromHexString(
                    "FFEEDDCCBBAA99887766554433221100FFEEDDCCBBAA99887766554433221100"));
            SignedRequest = AdmissionSecurity.SignAdmission(
                invitation,
                request,
                memberKey,
                nodeKey);
            transport = request.TransportCredential;
            Context = CreateContext();
        }

        public SignedAdmissionRequest SignedRequest { get; }

        public AdmissionVerificationContext Context { get; }

        public (SignedAdmissionRequest Request, AdmissionVerificationContext Context) Mutate(
            AdmissionMutation mutation)
        {
            switch (mutation)
            {
                case AdmissionMutation.Forged:
                    var signature = SignedRequest.NodeSignature.ToArray();
                    signature[0] ^= 0x01;
                    return (SignedRequest with { NodeSignature = signature }, Context);
                case AdmissionMutation.Expired:
                    return (SignedRequest, CreateContext(nowUtc: Now.AddHours(2)));
                case AdmissionMutation.Replayed:
                    return (SignedRequest, CreateContext(invitationState: InvitationUseState.Consumed));
                case AdmissionMutation.Downgraded:
                    var invitation = AdmissionSecurity.SignInvitation(
                        SignedRequest.Invitation.Invitation with
                        {
                            MaximumProtocolVersion = 2,
                        },
                        issuerKey);
                    var downgraded = SignedRequest.Request with
                    {
                        MaximumProtocolVersion = 2,
                        SelectedProtocolVersion = 1,
                        InvitationDigest = AdmissionSecurity.DigestInvitation(invitation),
                    };
                    return (
                        AdmissionSecurity.SignAdmission(
                            invitation,
                            downgraded,
                            memberKey,
                            nodeKey),
                        CreateContext(supportedMaximumProtocolVersion: 2));
                case AdmissionMutation.WrongCircle:
                    return (
                        SignedRequest,
                        CreateContext(
                            expectedCircleId: "0198c837-3000-7000-8000-000000000099"));
                case AdmissionMutation.WrongNode:
                    using (var otherTransportKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                    {
                        return (
                            SignedRequest,
                            CreateContext(
                                peerTransportCredential: RemoteIdentity.CreateCredential(
                                    KeyRole.Transport,
                                    otherTransportKey)));
                    }
                case AdmissionMutation.UnauthorizedIssuer:
                    return (
                        SignedRequest,
                        CreateContext(
                            trustedIssuerId: "0198c837-3000-7000-8000-000000000099"));
                case AdmissionMutation.Revoked:
                    return (
                        SignedRequest,
                        CreateContext(
                            revokedKeyIds: new HashSet<string>(StringComparer.Ordinal)
                            {
                                SignedRequest.Request.NodeCredential.KeyId,
                            }));
                case AdmissionMutation.StaleAuthority:
                    return (SignedRequest, CreateContext(minimumAuthorityGeneration: 8));
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        }

        public void Dispose()
        {
            issuerKey.Dispose();
            memberKey.Dispose();
            nodeKey.Dispose();
            transportKey.Dispose();
        }

        private AdmissionVerificationContext CreateContext(
            string? expectedCircleId = null,
            DateTimeOffset? nowUtc = null,
            InvitationUseState invitationState = InvitationUseState.Available,
            int supportedMaximumProtocolVersion = 1,
            PublicKeyCredential? peerTransportCredential = null,
            string? trustedIssuerId = null,
            long minimumAuthorityGeneration = 7,
            IReadOnlySet<string>? revokedKeyIds = null) => new(
                ExpectedCircleId: expectedCircleId ?? SignedRequest.Request.CircleId,
                TrustedIssuerId: trustedIssuerId ?? SignedRequest.Invitation.Invitation.IssuerId,
                TrustedIssuerCredential: issuer,
                PeerTransportCredential: peerTransportCredential ?? transport,
                ExpectedAnchorChallenge: SignedRequest.Request.AnchorChallenge,
                NowUtc: nowUtc ?? Now,
                InvitationState: invitationState,
                SupportedMinimumProtocolVersion: 1,
                SupportedMaximumProtocolVersion: supportedMaximumProtocolVersion,
                MinimumAuthorityGeneration: minimumAuthorityGeneration,
                RevokedKeyIds: revokedKeyIds ?? new HashSet<string>(StringComparer.Ordinal));

    }
}
