using System.Security.Cryptography;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleMessageSecurityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 22, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Dual_signed_message_and_anchor_receipt_validate()
    {
        using var member = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var node = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var anchor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = SignRequest(member, node);
        var result = CircleMessageSecurity.ValidateRequest(
            signed,
            Context(signed));

        Assert.IsTrue(result.IsAccepted);
        var receipt = new CircleMessageReceipt(
            1,
            signed.Request.CircleId,
            signed.Request.MessageId,
            1,
            signed.Request.AuthorMemberId,
            signed.Request.AuthorNodeId,
            signed.Request.Text,
            signed.Request.AuthoredAtUtc,
            Now.AddSeconds(1),
            CircleMessageSecurity.DigestRequest(signed));
        var signedReceipt = new SignedCircleMessageReceipt(
            receipt,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteIdentity.Sign(CircleMessageSecurity.EncodeReceipt(receipt), anchor));
        var receiptResult = CircleMessageSecurity.ValidateReceipt(
            signedReceipt,
            new CircleMessageReceiptVerificationContext(
                signed,
                RemoteIdentity.CreateCredential(KeyRole.Anchor, anchor),
                signed.Request.CircleId,
                Now.AddSeconds(1),
                CircleMessageSecurity.DefaultMaximumClockSkew));

        Assert.IsTrue(receiptResult.IsAccepted);
        CollectionAssert.AreEqual(
            CircleMessageWireCodec.EncodeRequest(signed),
            CircleMessageWireCodec.EncodeRequest(
                CircleMessageWireCodec.DecodeRequest(
                    CircleMessageWireCodec.EncodeRequest(signed))));
    }

    [TestMethod]
    public void Tampered_member_or_node_signature_rejects_as_forged()
    {
        using var member = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var node = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = SignRequest(member, node);
        var tamperedMember = signed with { MemberSignature = Corrupt(signed.MemberSignature) };
        var tamperedNode = signed with { NodeSignature = Corrupt(signed.NodeSignature) };

        Assert.AreEqual(
            CircleMessageRejectionCode.Forged,
            CircleMessageSecurity.ValidateRequest(tamperedMember, Context(signed)).RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.Forged,
            CircleMessageSecurity.ValidateRequest(tamperedNode, Context(signed)).RejectionCode);
    }

    [TestMethod]
    public void Wrong_context_revocation_and_stale_time_are_deterministic()
    {
        using var member = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var node = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = SignRequest(member, node);
        var context = Context(signed);

        Assert.AreEqual(
            CircleMessageRejectionCode.WrongCircle,
            CircleMessageSecurity.ValidateRequest(
                signed,
                context with { ExpectedCircleId = Guid.NewGuid().ToString("D") }).RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.WrongNode,
            CircleMessageSecurity.ValidateRequest(
                signed,
                context with { ExpectedPeerNodeId = Guid.NewGuid().ToString("D") }).RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.Revoked,
            CircleMessageSecurity.ValidateRequest(
                signed,
                context with
                {
                    RevokedKeyIds = new HashSet<string>
                    {
                        signed.Request.MemberCredential.KeyId,
                    },
                }).RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.Stale,
            CircleMessageSecurity.ValidateRequest(
                signed,
                context with { NowUtc = Now.AddMinutes(6) }).RejectionCode);
    }

    [TestMethod]
    public void Unauthorized_credentials_and_oversized_text_reject_before_verification()
    {
        using var member = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherMember = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var node = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = SignRequest(member, node);
        var unauthorized = Context(signed) with
        {
            TrustedMemberCredential = RemoteIdentity.CreateCredential(KeyRole.Member, otherMember),
        };

        Assert.AreEqual(
            CircleMessageRejectionCode.Unauthorized,
            CircleMessageSecurity.ValidateRequest(signed, unauthorized).RejectionCode);
        var oversized = signed with
        {
            Request = signed.Request with
            {
                Text = new string('x', CircleMessageSecurity.MaximumTextUtf8Bytes + 1),
            },
        };
        Assert.AreEqual(
            CircleMessageRejectionCode.Malformed,
            CircleMessageSecurity.ValidateRequest(oversized, Context(signed)).RejectionCode);
    }

    private static SignedCircleMessageRequest SignRequest(ECDsa member, ECDsa node)
    {
        var request = new CircleMessageRequest(
            1,
            "019c0000-0000-7000-8000-000000000001",
            "019c0000-0000-7000-8000-000000000002",
            "019c0000-0000-7000-8000-000000000003",
            "019c0000-0000-7000-8000-000000000004",
            RemoteIdentity.CreateCredential(KeyRole.Member, member),
            RemoteIdentity.CreateCredential(KeyRole.Node, node),
            "Hello from the other Node.",
            Now,
            RemoteSecurityProtocol.SignatureSuite);
        var transcript = CircleMessageSecurity.EncodeRequest(request);
        return new SignedCircleMessageRequest(
            request,
            RemoteIdentity.Sign(transcript, member),
            RemoteIdentity.Sign(transcript, node));
    }

    private static CircleMessageVerificationContext Context(SignedCircleMessageRequest signed) =>
        new(
            signed.Request.CircleId,
            signed.Request.AuthorNodeId,
            signed.Request.MemberCredential,
            signed.Request.NodeCredential,
            Now,
            CircleMessageSecurity.DefaultMaximumClockSkew,
            new HashSet<string>(StringComparer.Ordinal));

    private static byte[] Corrupt(byte[] value)
    {
        var copy = value.ToArray();
        copy[^1] ^= 0xff;
        return copy;
    }
}
