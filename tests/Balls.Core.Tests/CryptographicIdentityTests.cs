using System.Security.Cryptography;
using System.Text.Json;
using Balls.Core;

namespace Balls.Core.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class CryptographicIdentityTests
{
    [TestMethod]
    public void P256_key_identifiers_are_role_scoped_and_stable()
    {
        using var key = ECDsa.Create(new ECParameters
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

        var node = IdentityCryptography.CreateCredential(IdentityKeyRole.Node, key);
        var authority = IdentityCryptography.CreateCredential(
            IdentityKeyRole.CircleAuthority,
            key);

        Assert.AreEqual(
            "node:p256-sha256:XNJS-wzokyQ2-vjM0QQJgbie5K1rn-niorfnGqyyfNM",
            node.KeyId);
        Assert.AreEqual(
            "circle-authority:p256-sha256:XNJS-wzokyQ2-vjM0QQJgbie5K1rn-niorfnGqyyfNM",
            authority.KeyId);
        CollectionAssert.AreEqual(node.SubjectPublicKeyInfo, authority.SubjectPublicKeyInfo);
    }

    [TestMethod]
    public void Backup_envelope_has_no_serializable_or_printable_secret_payload()
    {
        var envelope = new AuthorityBackupEnvelope("{}"u8.ToArray());

        var json = JsonSerializer.Serialize(envelope);

        Assert.AreEqual("Balls Circle authority backup v1 (sensitive)", envelope.ToString());
        Assert.AreEqual("{\"format\":\"balls-circle-authority-backup\",\"version\":1}", json);
    }

    [TestMethod]
    public void Missing_backup_fields_return_a_typed_malformed_result()
    {
        var validation = AuthorityBackupValidator.Validate(
            "{}"u8,
            new CircleId(Guid.Parse("0198c837-3000-7000-8000-000000000001")));

        Assert.IsFalse(validation.IsValid);
        Assert.AreEqual(AuthorityBackupRejectionCode.Malformed, validation.RejectionCode);
    }
}
