using System.Security.Cryptography;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class NodeTransportSecurityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 20, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Circle_signed_transport_binding_authenticates_node_and_transport_independently()
    {
        using var fixture = new BindingFixture();

        var result = NodeTransportSecurity.Validate(fixture.SignedBinding, fixture.Context);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(NodeTransportRejectionCode.None, result.RejectionCode);
        CollectionAssert.AreEqual(
            NodeTransportSecurity.Encode(fixture.Binding),
            NodeTransportSecurity.Encode(fixture.Binding));
    }

    [TestMethod]
    [DataRow(BindingMutation.Tampered, NodeTransportRejectionCode.Forged)]
    [DataRow(BindingMutation.Revoked, NodeTransportRejectionCode.Revoked)]
    [DataRow(BindingMutation.Stale, NodeTransportRejectionCode.StaleAuthorityState)]
    [DataRow(BindingMutation.WrongCircle, NodeTransportRejectionCode.WrongCircle)]
    [DataRow(BindingMutation.WrongNode, NodeTransportRejectionCode.WrongNode)]
    [DataRow(BindingMutation.Future, NodeTransportRejectionCode.NotYetValid)]
    [DataRow(BindingMutation.Expired, NodeTransportRejectionCode.Expired)]
    [DataRow(BindingMutation.UnsupportedVersion, NodeTransportRejectionCode.UnsupportedVersion)]
    [DataRow(BindingMutation.Downgraded, NodeTransportRejectionCode.Downgraded)]
    public void Invalid_binding_has_one_deterministic_fail_closed_result(
        BindingMutation mutation,
        NodeTransportRejectionCode expected)
    {
        using var fixture = new BindingFixture();
        var (binding, context) = fixture.Mutate(mutation);

        var first = NodeTransportSecurity.Validate(binding, context);
        var second = NodeTransportSecurity.Validate(binding, context);

        Assert.IsFalse(first.IsValid);
        Assert.AreEqual(expected, first.RejectionCode);
        Assert.AreEqual(first, second);
    }

    public enum BindingMutation
    {
        Tampered,
        Revoked,
        Stale,
        WrongCircle,
        WrongNode,
        Future,
        Expired,
        UnsupportedVersion,
        Downgraded,
    }

    private sealed class BindingFixture : IDisposable
    {
        private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa transportKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        internal BindingFixture()
        {
            RootCredential = RemoteIdentity.CreateCredential(KeyRole.CircleAuthority, rootKey);
            Binding = new NodeTransportBinding(
                Version: RemoteSecurityProtocol.Version,
                CircleId: CircleId,
                NodeId: NodeId,
                AuthorityGeneration: 4,
                TransportCredential: RemoteIdentity.CreateCredential(
                    KeyRole.Transport,
                    transportKey),
                NotBeforeUtc: Now.AddMinutes(-5),
                ExpiresAtUtc: Now.AddDays(1),
                MinimumProtocolVersion: RemoteSecurityProtocol.Version,
                MaximumProtocolVersion: RemoteSecurityProtocol.Version);
            SignedBinding = NodeTransportSecurity.Sign(Binding, RootCredential, rootKey);
            Context = new NodeTransportVerificationContext(
                ExpectedCircleId: CircleId,
                ExpectedNodeId: NodeId,
                TrustedRootCredential: RootCredential,
                NowUtc: Now,
                MinimumAuthorityGeneration: 4,
                SupportedMinimumProtocolVersion: RemoteSecurityProtocol.Version,
                SupportedMaximumProtocolVersion: RemoteSecurityProtocol.Version,
                RevokedKeyIds: new HashSet<string>(StringComparer.Ordinal));
        }

        internal const string CircleId = "0198c837-5000-7000-8000-000000000001";
        internal const string NodeId = "0198c837-5000-7000-8000-000000000002";

        internal PublicKeyCredential RootCredential { get; }

        internal NodeTransportBinding Binding { get; }

        internal SignedNodeTransportBinding SignedBinding { get; }

        internal NodeTransportVerificationContext Context { get; }

        internal (SignedNodeTransportBinding Binding, NodeTransportVerificationContext Context) Mutate(
            BindingMutation mutation)
        {
            switch (mutation)
            {
                case BindingMutation.Tampered:
                    var signature = SignedBinding.AuthoritySignature.ToArray();
                    signature[0] ^= 0x01;
                    return (SignedBinding with { AuthoritySignature = signature }, Context);
                case BindingMutation.Revoked:
                    return (SignedBinding, Context with
                    {
                        RevokedKeyIds = new HashSet<string>(StringComparer.Ordinal)
                        {
                            Binding.TransportCredential.KeyId,
                        },
                    });
                case BindingMutation.Stale:
                    return (SignedBinding, Context with { MinimumAuthorityGeneration = 5 });
                case BindingMutation.WrongCircle:
                    return (SignedBinding, Context with
                    {
                        ExpectedCircleId = "0198c837-5000-7000-8000-000000000099",
                    });
                case BindingMutation.WrongNode:
                    return (SignedBinding, Context with
                    {
                        ExpectedNodeId = "0198c837-5000-7000-8000-000000000099",
                    });
                case BindingMutation.Future:
                    return (SignedBinding, Context with { NowUtc = Now.AddMinutes(-10) });
                case BindingMutation.Expired:
                    return (SignedBinding, Context with { NowUtc = Now.AddDays(2) });
                case BindingMutation.UnsupportedVersion:
                    return (SignedBinding with
                    {
                        Binding = Binding with { Version = 2 },
                    }, Context);
                case BindingMutation.Downgraded:
                    var downgraded = Binding with
                    {
                        MinimumProtocolVersion = 2,
                        MaximumProtocolVersion = 2,
                    };
                    return (
                        NodeTransportSecurity.Sign(downgraded, RootCredential, rootKey),
                        Context);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        }

        public void Dispose()
        {
            rootKey.Dispose();
            transportKey.Dispose();
        }
    }
}
