using System.Security.Cryptography;
using Balls.Core;
using Balls.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class InvitationStateStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Transport_identity_and_issued_invitation_are_protected_and_restart_stable()
    {
        using var directory = new TemporaryDirectory();
        var protector = TestPrivateMaterialProtector.Instance;
        CircleId circleId;
        string transportKeyId;
        var invitation = CreateInvitation();

        await using (var store = await SqliteLocalStateStore.OpenAsync(directory.Path, protector))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
            var circle = await application.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Invitation Circle",
                    "Alice"));
            circleId = circle.Circle.Id;
            var node = (await store.GetNodeCryptographicIdentityAsync())!;
            var transport = (await store.GetLocalTransportIdentityAsync())!;
            transportKeyId = transport.Credential.KeyId;

            Assert.AreEqual(node.NodeId, transport.NodeId);
            Assert.AreNotEqual(node.Credential.KeyId, transport.Credential.KeyId);
            Assert.AreEqual(IdentityKeyRole.Transport, transport.Credential.Role);
            var message = "transport-proof"u8.ToArray();
            var signature = await store.SignWithTransportAsync(message);
            Assert.IsTrue(IdentityCryptography.Verify(message, signature, transport.Credential));

            await store.StoreCircleInvitationAsync(invitation with { CircleId = circleId });
        }

        await AssertTransportRowProtectedAsync(directory.Path, protector);

        await using var reopened = await SqliteLocalStateStore.OpenAsync(directory.Path, protector);
        Assert.AreEqual(
            transportKeyId,
            (await reopened.GetLocalTransportIdentityAsync())!.Credential.KeyId);
        var stored = await reopened.GetCircleInvitationAsync(invitation.InvitationId);
        Assert.IsNotNull(stored);
        Assert.AreEqual(circleId, stored.CircleId);
        CollectionAssert.AreEqual(invitation.EncodedPackage, stored.EncodedPackage);
    }

    [TestMethod]
    public async Task Concurrent_redemption_commits_one_result_and_rejects_every_replay()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
        var circle = await application.CreateCircleAsync(
            new CreateCircleCommand(
                new CreationRequestId(Guid.CreateVersion7()),
                "Concurrent Circle",
                "Alice"));
        var invitation = CreateInvitation() with { CircleId = circle.Circle.Id };
        await store.StoreCircleInvitationAsync(invitation);

        var attempts = Enumerable.Range(0, 16)
            .Select(_ => store.RedeemCircleInvitationAsync(
                invitation.InvitationId,
                invitation.PackageSha256,
                RedemptionId.New(),
                Now))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.AreEqual(1, results.Count(result => result.Status == InvitationRedemptionStatus.Accepted));
        Assert.AreEqual(15, results.Count(result => result.Status == InvitationRedemptionStatus.Replayed));
        Assert.IsNotNull(results.Single(result =>
            result.Status == InvitationRedemptionStatus.Accepted).RedemptionId);

        var replay = await store.RedeemCircleInvitationAsync(
            invitation.InvitationId,
            invitation.PackageSha256,
            RedemptionId.New(),
            Now);
        Assert.AreEqual(InvitationRedemptionStatus.Replayed, replay.Status);
        Assert.IsNull(replay.RedemptionId);
    }

    [TestMethod]
    public async Task Mismatched_expired_revoked_and_unknown_redemptions_fail_closed()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
        var circle = await application.CreateCircleAsync(
            new CreateCircleCommand(
                new CreationRequestId(Guid.CreateVersion7()),
                "Rejection Circle",
                "Alice"));
        var invitation = CreateInvitation() with { CircleId = circle.Circle.Id };
        await store.StoreCircleInvitationAsync(invitation);

        var mismatch = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => store.RedeemCircleInvitationAsync(
                invitation.InvitationId,
                SHA256.HashData("other-package"u8),
                RedemptionId.New(),
                Now));
        Assert.AreEqual("invitation_mismatch", mismatch.Code);

        var expired = await store.RedeemCircleInvitationAsync(
            invitation.InvitationId,
            invitation.PackageSha256,
            RedemptionId.New(),
            invitation.ExpiresAtUtc);
        Assert.AreEqual(InvitationRedemptionStatus.Expired, expired.Status);

        await store.RevokeCircleInvitationAsync(invitation.InvitationId, Now);
        var revoked = await store.RedeemCircleInvitationAsync(
            invitation.InvitationId,
            invitation.PackageSha256,
            RedemptionId.New(),
            Now);
        Assert.AreEqual(InvitationRedemptionStatus.Revoked, revoked.Status);

        var missing = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => store.RedeemCircleInvitationAsync(
                InvitationId.New(),
                invitation.PackageSha256,
                RedemptionId.New(),
                Now));
        Assert.AreEqual("invitation_not_found", missing.Code);
    }

    private static PersistedCircleInvitation CreateInvitation()
    {
        var encoded = "{\"format\":\"balls-circle-invitation\",\"version\":1}"u8.ToArray();
        return new PersistedCircleInvitation(
            InvitationId.New(),
            new CircleId(Guid.Parse("0198c837-3000-7000-8000-000000000001")),
            SHA256.HashData(encoded),
            encoded,
            Now.AddHours(1),
            Now);
    }

    private static async Task AssertTransportRowProtectedAsync(
        string directory,
        IPrivateMaterialProtector protector)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(directory, "balls.db")};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT private_key_scheme, protected_private_key FROM local_transport_credentials;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(protector.Scheme, reader.GetString(0));
        var protectedPrivateKey = (byte[])reader.GetValue(1);
        CollectionAssert.AreNotEqual(
            protectedPrivateKey,
            protector.Unprotect(protectedPrivateKey));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
