using System.Security.Cryptography;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class AdmissionResponseSecurityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Anchor_signed_response_binds_the_request_roster_and_root_signed_transports()
    {
        using var fixture = new ResponseFixture();

        var result = AdmissionSecurity.ValidateResponse(fixture.SignedResponse, fixture.Context);

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(RemoteSecurityProtocol.Version, result.NegotiatedProtocolVersion);
    }

    [TestMethod]
    [DataRow(ResponseMutation.Forged, AdmissionRejectionCode.Forged)]
    [DataRow(ResponseMutation.StaleAuthority, AdmissionRejectionCode.StaleAuthorityState)]
    [DataRow(ResponseMutation.WrongCircle, AdmissionRejectionCode.WrongCircle)]
    [DataRow(ResponseMutation.WrongNode, AdmissionRejectionCode.WrongNode)]
    [DataRow(ResponseMutation.Revoked, AdmissionRejectionCode.Revoked)]
    [DataRow(ResponseMutation.Downgraded, AdmissionRejectionCode.Downgraded)]
    public void Invalid_response_has_one_deterministic_rejection(
        ResponseMutation mutation,
        AdmissionRejectionCode expected)
    {
        using var fixture = new ResponseFixture();
        var (response, context) = fixture.Mutate(mutation);

        var first = AdmissionSecurity.ValidateResponse(response, context);
        var second = AdmissionSecurity.ValidateResponse(response, context);

        Assert.IsFalse(first.IsAccepted);
        Assert.AreEqual(expected, first.RejectionCode);
        Assert.AreEqual(first, second);
    }

    public enum ResponseMutation
    {
        Forged,
        StaleAuthority,
        WrongCircle,
        WrongNode,
        Revoked,
        Downgraded,
    }

    private sealed class ResponseFixture : IDisposable
    {
        private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa anchorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa nodeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa transportKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa anchorNodeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa anchorTransportKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly PublicKeyCredential root;
        private readonly PublicKeyCredential anchor;

        internal ResponseFixture()
        {
            root = RemoteIdentity.CreateCredential(KeyRole.CircleAuthority, rootKey);
            anchor = RemoteIdentity.CreateCredential(KeyRole.Anchor, anchorKey);
            var invitation = AdmissionSecurity.SignInvitation(
                new CircleInvitation(
                    CircleId,
                    InvitationId,
                    AnchorNodeId,
                    anchor.KeyId,
                    RemoteIdentity.CreateCredential(KeyRole.Transport, anchorTransportKey).KeyId,
                    7,
                    Now.AddMinutes(-1),
                    Now.AddHours(1),
                    1,
                    1,
                    1,
                    RandomNumberGenerator.GetBytes(32)),
                anchorKey);
            var request = new AdmissionRequest(
                CircleId,
                InvitationId,
                MemberId,
                RemoteIdentity.CreateCredential(KeyRole.Member, memberKey),
                NodeId,
                RemoteIdentity.CreateCredential(KeyRole.Node, nodeKey),
                RemoteIdentity.CreateCredential(KeyRole.Transport, transportKey),
                1,
                1,
                1,
                RemoteSecurityProtocol.SignatureSuite,
                RemoteSecurityProtocol.Alpn,
                AdmissionSecurity.DigestInvitation(invitation),
                RandomNumberGenerator.GetBytes(32),
                RandomNumberGenerator.GetBytes(32));
            var signedRequest = AdmissionSecurity.SignAdmission(
                invitation,
                request,
                memberKey,
                nodeKey);
            var admittedBinding = Binding(NodeId, request.TransportCredential);
            var anchorBinding = Binding(
                AnchorNodeId,
                RemoteIdentity.CreateCredential(KeyRole.Transport, anchorTransportKey));
            var response = new AdmissionResponse(
                1,
                CircleId,
                InvitationId,
                7,
                2,
                1,
                "Example Circle",
                Now.AddDays(-1),
                MemberId,
                request.MemberCredential,
                "member",
                ["circle.read"],
                NodeId,
                request.NodeCredential,
                admittedBinding,
                AdmissionSecurity.DigestAdmission(signedRequest),
                request.AnchorChallenge,
                request.ApplicantChallenge,
                [
                    new(OwnerId, "Alice", "owner", Now.AddDays(-1)),
                    new(MemberId, "Bob", "member", Now),
                ],
                [
                    new(
                        AnchorNodeId,
                        "Anchor",
                        Now.AddDays(-1),
                        RemoteIdentity.CreateCredential(KeyRole.Node, anchorNodeKey),
                        anchorBinding),
                    new(NodeId, "Bob-PC", Now, request.NodeCredential, admittedBinding),
                ]);
            SignedResponse = AdmissionSecurity.SignResponse(response, anchor, anchorKey);
            Context = new(
                signedRequest,
                root,
                anchor,
                Now,
                7,
                new HashSet<string>(StringComparer.Ordinal));
        }

        internal SignedAdmissionResponse SignedResponse { get; }

        internal AdmissionResponseVerificationContext Context { get; }

        internal (SignedAdmissionResponse, AdmissionResponseVerificationContext) Mutate(
            ResponseMutation mutation)
        {
            switch (mutation)
            {
                case ResponseMutation.Forged:
                    var signature = SignedResponse.AnchorSignature.ToArray();
                    signature[0] ^= 1;
                    return (SignedResponse with { AnchorSignature = signature }, Context);
                case ResponseMutation.StaleAuthority:
                    return (SignedResponse, Context with { MinimumAuthorityGeneration = 8 });
                case ResponseMutation.WrongCircle:
                    var wrongCircle = SignedResponse.Response with
                    {
                        CircleId = "0198c837-6000-7000-8000-000000000099",
                    };
                    return (
                        AdmissionSecurity.SignResponse(wrongCircle, anchor, anchorKey),
                        Context);
                case ResponseMutation.WrongNode:
                    {
                        using var otherNodeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                        var wrongNode = SignedResponse.Response with
                        {
                            AdmittedNodeCredential = RemoteIdentity.CreateCredential(
                                KeyRole.Node,
                                otherNodeKey),
                        };
                        wrongNode = wrongNode with
                        {
                            Nodes = wrongNode.Nodes.Select(node =>
                                node.NodeId == wrongNode.AdmittedNodeId
                                    ? node with
                                    {
                                        NodeCredential = wrongNode.AdmittedNodeCredential,
                                    }
                                    : node).ToArray(),
                        };
                        return (
                            AdmissionSecurity.SignResponse(wrongNode, anchor, anchorKey),
                            Context);
                    }
                case ResponseMutation.Revoked:
                    return (
                        SignedResponse,
                        Context with
                        {
                            RevokedKeyIds = new HashSet<string>(StringComparer.Ordinal)
                            {
                                SignedResponse.Response.AdmittedNodeCredential.KeyId,
                            },
                        });
                case ResponseMutation.Downgraded:
                    var downgraded = SignedResponse.Response with { SelectedProtocolVersion = 2 };
                    return (
                        AdmissionSecurity.SignResponse(downgraded, anchor, anchorKey),
                        Context);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        }

        public void Dispose()
        {
            rootKey.Dispose();
            anchorKey.Dispose();
            memberKey.Dispose();
            nodeKey.Dispose();
            transportKey.Dispose();
            anchorNodeKey.Dispose();
            anchorTransportKey.Dispose();
        }

        private SignedNodeTransportBinding Binding(
            string nodeId,
            PublicKeyCredential transport) =>
            NodeTransportSecurity.Sign(
                new NodeTransportBinding(
                    1,
                    CircleId,
                    nodeId,
                    7,
                    transport,
                    Now.AddMinutes(-1),
                    Now.AddDays(30),
                    1,
                    1),
                root,
                rootKey);

        private const string CircleId = "0198c837-6000-7000-8000-000000000001";
        private const string InvitationId = "0198c837-6000-7000-8000-000000000002";
        private const string OwnerId = "0198c837-6000-7000-8000-000000000003";
        private const string MemberId = "0198c837-6000-7000-8000-000000000004";
        private const string AnchorNodeId = "0198c837-6000-7000-8000-000000000005";
        private const string NodeId = "0198c837-6000-7000-8000-000000000006";
    }
}
