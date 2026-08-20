using System.Security.Cryptography;
using System.Text;
using Balls.Protocol.Remote.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class InvitationPackageTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Package_is_canonical_inspectable_bounded_and_valid()
    {
        using var fixture = new InvitationFixture();

        var encoded = InvitationPackageCodec.Encode(fixture.Package);
        var decoded = InvitationPackageCodec.Decode(encoded);
        var validation = InvitationSecurity.Validate(decoded, fixture.Context);

        Assert.IsTrue(validation.IsValid);
        Assert.AreEqual(InvitationRejectionCode.None, validation.RejectionCode);
        Assert.IsTrue(encoded.Length < InvitationPackageCodec.MaximumEncodedLength);
        StringAssert.Contains(Encoding.UTF8.GetString(encoded), "balls-circle-invitation");
        StringAssert.Contains(Encoding.UTF8.GetString(encoded), fixture.CircleId);
        Assert.IsFalse(
            Encoding.UTF8.GetString(encoded).Contains("PRIVATE", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEqual(encoded, InvitationPackageCodec.Encode(decoded));
    }

    [TestMethod]
    [DataRow(InvitationMutation.Forged, InvitationRejectionCode.Forged)]
    [DataRow(InvitationMutation.Altered, InvitationRejectionCode.Forged)]
    [DataRow(InvitationMutation.Expired, InvitationRejectionCode.Expired)]
    [DataRow(InvitationMutation.Future, InvitationRejectionCode.NotYetValid)]
    [DataRow(InvitationMutation.RevokedIssuer, InvitationRejectionCode.Revoked)]
    [DataRow(InvitationMutation.WrongCircle, InvitationRejectionCode.WrongCircle)]
    [DataRow(InvitationMutation.UnsupportedVersion, InvitationRejectionCode.UnsupportedVersion)]
    public void Invalid_package_has_one_deterministic_rejection(
        InvitationMutation mutation,
        InvitationRejectionCode expected)
    {
        using var fixture = new InvitationFixture();
        var (package, context) = fixture.Mutate(mutation);

        var first = InvitationSecurity.Validate(package, context);
        var second = InvitationSecurity.Validate(package, context);

        Assert.IsFalse(first.IsValid);
        Assert.AreEqual(expected, first.RejectionCode);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Malformed_noncanonical_and_oversized_envelopes_are_rejected_before_use()
    {
        using var fixture = new InvitationFixture();
        var canonical = InvitationPackageCodec.Encode(fixture.Package);
        var nonCanonical = Encoding.UTF8.GetBytes(
            " \n" + Encoding.UTF8.GetString(canonical));

        Assert.ThrowsExactly<InvitationPackageException>(
            () => InvitationPackageCodec.Decode("{}"u8));
        Assert.ThrowsExactly<InvitationPackageException>(
            () => InvitationPackageCodec.Decode(nonCanonical));
        Assert.ThrowsExactly<InvitationPackageException>(
            () => InvitationPackageCodec.Decode(
                new byte[InvitationPackageCodec.MaximumEncodedLength + 1]));
    }

    public enum InvitationMutation
    {
        Forged,
        Altered,
        Expired,
        Future,
        RevokedIssuer,
        WrongCircle,
        UnsupportedVersion,
    }

    private sealed class InvitationFixture : IDisposable
    {
        private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public InvitationFixture()
        {
            var root = RemoteIdentity.CreateCredential(KeyRole.CircleAuthority, rootKey);
            var issuer = RemoteIdentity.CreateCredential(KeyRole.Anchor, issuerKey);
            var delegation = new InvitationIssuerDelegation(
                CircleId,
                AuthorityGeneration: 7,
                RootKeyId: root.KeyId,
                IssuerId: "0198c837-3000-7000-8000-000000000003",
                IssuerCredential: issuer,
                Authorization: InvitationSecurity.SingleUseInvitationAuthorization,
                NotBeforeUtc: Now.AddMinutes(-5),
                ExpiresAtUtc: Now.AddDays(1));
            var signedDelegation = InvitationSecurity.SignDelegation(
                delegation,
                root,
                rootKey);
            var invitation = new CircleInvitation(
                CircleId,
                InvitationId: "0198c837-3000-7000-8000-000000000002",
                IssuerId: delegation.IssuerId,
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
            Package = new CircleInvitationPackage(
                InvitationPackageCodec.Version,
                root,
                signedDelegation,
                AdmissionSecurity.SignInvitation(invitation, issuerKey));
            Context = new InvitationVerificationContext(
                ExpectedCircleId: CircleId,
                TrustedRootCredential: root,
                NowUtc: Now,
                MinimumAuthorityGeneration: 7,
                InvitationState: InvitationUseState.Available,
                RevokedKeyIds: new HashSet<string>(StringComparer.Ordinal));
        }

        public string CircleId { get; } = "0198c837-3000-7000-8000-000000000001";

        public CircleInvitationPackage Package { get; }

        public InvitationVerificationContext Context { get; }

        public (CircleInvitationPackage Package, InvitationVerificationContext Context) Mutate(
            InvitationMutation mutation)
        {
            switch (mutation)
            {
                case InvitationMutation.Forged:
                    var signature = Package.Invitation.IssuerSignature.ToArray();
                    signature[0] ^= 0x01;
                    return (Package with
                    {
                        Invitation = Package.Invitation with { IssuerSignature = signature },
                    }, Context);
                case InvitationMutation.Altered:
                    return (Package with
                    {
                        Invitation = Package.Invitation with
                        {
                            Invitation = Package.Invitation.Invitation with
                            {
                                ExpiresAtUtc = Package.Invitation.Invitation.ExpiresAtUtc.AddHours(1),
                            },
                        },
                    }, Context);
                case InvitationMutation.Expired:
                    return (Package, Context with { NowUtc = Now.AddHours(2) });
                case InvitationMutation.Future:
                    return (Package, Context with { NowUtc = Now.AddMinutes(-10) });
                case InvitationMutation.RevokedIssuer:
                    return (Package, Context with
                    {
                        RevokedKeyIds = new HashSet<string>(StringComparer.Ordinal)
                        {
                            Package.Invitation.Invitation.IssuerKeyId,
                        },
                    });
                case InvitationMutation.WrongCircle:
                    return (Package, Context with
                    {
                        ExpectedCircleId = "0198c837-3000-7000-8000-000000000099",
                    });
                case InvitationMutation.UnsupportedVersion:
                    return (Package with { Version = 2 }, Context);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        }

        public void Dispose()
        {
            rootKey.Dispose();
            issuerKey.Dispose();
        }
    }
}
