using System.Security.Cryptography;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class CircleMessageSecurityTests
{
    private static readonly DateTimeOffset AuthoredAt =
        new(2026, 8, 21, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Dual_signed_message_and_anchor_receipt_round_trip_with_stable_identity_and_order()
    {
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var nodeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var anchorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var message = CreateMessage();
        var transcript = CircleMessageSecurity.EncodeMessage(message);
        var signed = new SignedCircleMessage(
            message,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteIdentity.Sign(transcript, memberKey),
            RemoteIdentity.Sign(transcript, nodeKey));

        var validation = CircleMessageSecurity.Validate(
            signed,
            new CircleMessageVerificationContext(
                message.CircleId,
                message.AuthorMemberId,
                message.AuthorNodeId,
                RemoteIdentity.CreateCredential(KeyRole.Member, memberKey),
                RemoteIdentity.CreateCredential(KeyRole.Node, nodeKey),
                IsAuthorizedAuthor: true,
                AuthoredAt.AddMinutes(1)));

        Assert.IsTrue(validation.IsAccepted);
        var receipt = CircleMessageSecurity.SignReceipt(
            signed,
            sequence: 7,
            acceptedAtUtc: AuthoredAt.AddSeconds(3),
            anchorKey);
        var encoded = CircleMessageWireCodec.EncodeReceipt(receipt);
        var decoded = CircleMessageWireCodec.DecodeReceipt(encoded);
        var receiptValidation = CircleMessageSecurity.ValidateReceipt(
            decoded,
            signed,
            RemoteIdentity.CreateCredential(KeyRole.Anchor, anchorKey));

        Assert.IsTrue(receiptValidation.IsAccepted);
        Assert.AreEqual(message.MessageId, decoded.Message.MessageId);
        Assert.AreEqual(message.Text, decoded.Message.Text);
        Assert.AreEqual(7, decoded.Sequence);
        Assert.AreEqual(AuthoredAt.AddSeconds(3), decoded.AcceptedAtUtc);
        CollectionAssert.AreEqual(encoded, CircleMessageWireCodec.EncodeReceipt(decoded));
    }

    [TestMethod]
    public void Validation_rejects_tampering_wrong_context_unauthorized_authorship_and_oversized_text()
    {
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var nodeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var message = CreateMessage();
        var transcript = CircleMessageSecurity.EncodeMessage(message);
        var signed = new SignedCircleMessage(
            message,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteIdentity.Sign(transcript, memberKey),
            RemoteIdentity.Sign(transcript, nodeKey));
        var context = new CircleMessageVerificationContext(
            message.CircleId,
            message.AuthorMemberId,
            message.AuthorNodeId,
            RemoteIdentity.CreateCredential(KeyRole.Member, memberKey),
            RemoteIdentity.CreateCredential(KeyRole.Node, nodeKey),
            IsAuthorizedAuthor: true,
            AuthoredAt.AddMinutes(1));

        var tampered = signed with { Message = message with { Text = "tampered" } };
        Assert.AreEqual(
            CircleMessageRejectionCode.Forged,
            CircleMessageSecurity.Validate(tampered, context).RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.WrongCircle,
            CircleMessageSecurity.Validate(
                signed,
                context with { ExpectedCircleId = "0198c2d8-b000-7000-8000-000000000099" })
                .RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.WrongNode,
            CircleMessageSecurity.Validate(
                signed,
                context with { ExpectedNodeId = "0198c2d8-b000-7000-8000-000000000099" })
                .RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.Unauthorized,
            CircleMessageSecurity.Validate(signed, context with { IsAuthorizedAuthor = false })
                .RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.Malformed,
            CircleMessageSecurity.Validate(
                signed with { Message = message with { Text = new string('x', 4097) } },
                context).RejectionCode);
        Assert.AreEqual(
            CircleMessageRejectionCode.UnsupportedSuite,
            CircleMessageSecurity.Validate(signed with { SignatureSuite = "future-suite" }, context)
                .RejectionCode);
        Assert.IsFalse(CircleMessageSecurity.IsValidText("\ud800"));
    }

    [TestMethod]
    public void Wire_codec_rejects_malformed_and_oversized_payloads_before_application_use()
    {
        Assert.ThrowsExactly<CircleMessageProtocolException>(
            () => CircleMessageWireCodec.DecodeRequest("not-a-message"u8));
        Assert.ThrowsExactly<CircleMessageProtocolException>(
            () => CircleMessageWireCodec.DecodeRequest(new byte[65 * 1024]));
    }

    private static CircleMessage CreateMessage() => new(
        RemoteSecurityProtocol.Version,
        "0198c2d8-b000-7000-8000-000000000010",
        "0198c2d8-b000-7000-8000-000000000020",
        "0198c2d8-b000-7000-8000-000000000030",
        "0198c2d8-b000-7000-8000-000000000040",
        AuthoredAt,
        "Hello from Bob's Node.");
}
