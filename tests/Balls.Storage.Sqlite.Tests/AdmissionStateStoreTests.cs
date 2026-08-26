using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Balls.Core;
using Balls.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class AdmissionStateStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Applicant_identity_is_retry_stable_protected_and_conflict_checked()
    {
        using var directory = new TemporaryDirectory();
        var invitationId = InvitationId.New();
        var circleId = CircleId.New();
        var digest = RandomNumberGenerator.GetBytes(32);
        AdmissionApplicantState first;
        await using (var store = await OpenAsync(directory.Path))
        {
            await new CircleApplication(store, TimeProvider.System, "Joiner-PC").GetLocalNodeAsync();
            first = await store.PrepareAdmissionApplicantAsync(
                invitationId,
                circleId,
                digest,
                "Bob",
                Now);
            var second = await store.PrepareAdmissionApplicantAsync(
                invitationId,
                circleId,
                digest,
                "Bob",
                Now);
            Assert.AreEqual(first.InvitationId, second.InvitationId);
            Assert.AreEqual(first.MemberId, second.MemberId);
            Assert.AreEqual(first.MemberCredential.KeyId, second.MemberCredential.KeyId);
            CollectionAssert.AreEqual(first.ApplicantChallenge, second.ApplicantChallenge);
            CollectionAssert.AreEqual(first.PackageSha256, second.PackageSha256);
            await Assert.ThrowsExactlyAsync<LocalStateConflictException>(
                () => store.PrepareAdmissionApplicantAsync(
                    invitationId,
                    circleId,
                    digest,
                    "Mallory",
                    Now));
        }

        await using var reopened = await OpenAsync(directory.Path);
        var signature = await reopened.SignWithAdmissionMemberAsync(
            invitationId,
            "retry-proof"u8.ToArray());
        Assert.IsTrue(IdentityCryptography.Verify(
            "retry-proof"u8,
            signature,
            first.MemberCredential));
    }

    [TestMethod]
    public async Task Transport_certificate_uses_the_protected_local_transport_identity()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await OpenAsync(directory.Path);
        await new CircleApplication(store, TimeProvider.System, "Joiner-PC").GetLocalNodeAsync();
        var expected = (await store.GetLocalTransportIdentityAsync())!.Credential;
        using var certificate = await store.CreateTransportCertificateAsync("node.balls", Now);
        using var publicKey = certificate.GetECDsaPublicKey();
        var actual = IdentityCryptography.CreateCredential(
            IdentityKeyRole.Transport,
            publicKey!);

        Assert.IsTrue(certificate.HasPrivateKey);
        Assert.AreEqual(expected.KeyId, actual.KeyId);
        CollectionAssert.AreEqual(expected.SubjectPublicKeyInfo, actual.SubjectPublicKeyInfo);
    }

    [TestMethod]
    public async Task Anchor_commit_consumes_invitation_with_membership_and_replays_exact_response()
    {
        using var directory = new TemporaryDirectory();
        await using var store = await OpenAsync(directory.Path);
        var application = new CircleApplication(store, TimeProvider.System, "Anchor-PC");
        var created = await application.CreateCircleAsync(
            new CreateCircleCommand(
                new CreationRequestId(Guid.CreateVersion7()),
                "Example Circle",
                "Alice"));
        var invitationId = InvitationId.New();
        var package = "package"u8.ToArray();
        var packageDigest = SHA256.HashData(package);
        await store.StoreCircleInvitationAsync(
            new PersistedCircleInvitation(
                invitationId,
                created.Circle.Id,
                packageDigest,
                package,
                Now.AddHours(1),
                Now));
        var sequence = await store.ReserveAuthoritySequenceAsync(created.Circle.Id);
        var memberId = MemberId.New();
        var nodeId = NodeId.New();
        var memberCredential = Credential(IdentityKeyRole.Member);
        var nodeCredential = Credential(IdentityKeyRole.Node);
        var transportCredential = Credential(IdentityKeyRole.Transport);
        var commit = new AnchorAdmissionCommit(
            invitationId,
            created.Circle.Id,
            packageDigest,
            RandomNumberGenerator.GetBytes(32),
            "signed-response"u8.ToArray(),
            new Member(memberId, created.Circle.Id, "Bob", MemberRole.Member, Now),
            new CircleNode(created.Circle.Id, nodeId, "Bob-PC", Now),
            memberCredential,
            nodeCredential,
            transportCredential,
            "signed-binding"u8.ToArray(),
            sequence,
            Now);

        var accepted = await store.CommitAnchorAdmissionAsync(commit);
        var queried = await store.GetAnchorAdmissionResultAsync(
            invitationId,
            commit.RequestSha256);
        var retried = await store.CommitAnchorAdmissionAsync(commit);
        var conflict = await store.CommitAnchorAdmissionAsync(
            commit with { RequestSha256 = RandomNumberGenerator.GetBytes(32) });
        var details = await store.GetCircleAsync(created.Circle.Id);

        Assert.AreEqual(AnchorAdmissionCommitStatus.Accepted, accepted.Status);
        Assert.AreEqual(AnchorAdmissionCommitStatus.IdempotentRetry, queried!.Status);
        CollectionAssert.AreEqual(commit.EncodedResponse, queried.EncodedResponse);
        Assert.AreEqual(AnchorAdmissionCommitStatus.IdempotentRetry, retried.Status);
        CollectionAssert.AreEqual(commit.EncodedResponse, retried.EncodedResponse);
        Assert.AreEqual(AnchorAdmissionCommitStatus.Replayed, conflict.Status);
        Assert.AreEqual(2, details!.Members.Count);
        Assert.AreEqual(2, details.Nodes.Count);
        Assert.AreEqual(
            InvitationRedemptionStatus.Replayed,
            (await store.RedeemCircleInvitationAsync(
                invitationId,
                packageDigest,
                RedemptionId.New(),
                Now)).Status);

        var revokedInvitationId = InvitationId.New();
        var revokedPackage = "revoked-package"u8.ToArray();
        var revokedDigest = SHA256.HashData(revokedPackage);
        await store.StoreCircleInvitationAsync(
            new PersistedCircleInvitation(
                revokedInvitationId,
                created.Circle.Id,
                revokedDigest,
                revokedPackage,
                Now.AddHours(1),
                Now));
        await store.RevokeCircleInvitationAsync(revokedInvitationId, Now);
        var revoked = await store.CommitAnchorAdmissionAsync(
            commit with
            {
                InvitationId = revokedInvitationId,
                PackageSha256 = revokedDigest,
                RequestSha256 = RandomNumberGenerator.GetBytes(32),
                Member = commit.Member with { Id = MemberId.New() },
                Node = commit.Node with { NodeId = NodeId.New() },
            });
        Assert.AreEqual(AnchorAdmissionCommitStatus.Revoked, revoked.Status);

        var expiredInvitationId = InvitationId.New();
        var expiredPackage = "expired-package"u8.ToArray();
        var expiredDigest = SHA256.HashData(expiredPackage);
        await store.StoreCircleInvitationAsync(
            new PersistedCircleInvitation(
                expiredInvitationId,
                created.Circle.Id,
                expiredDigest,
                expiredPackage,
                Now,
                Now.AddMinutes(-1)));
        var expired = await store.CommitAnchorAdmissionAsync(
            commit with
            {
                InvitationId = expiredInvitationId,
                PackageSha256 = expiredDigest,
                RequestSha256 = RandomNumberGenerator.GetBytes(32),
                Member = commit.Member with { Id = MemberId.New() },
                Node = commit.Node with { NodeId = NodeId.New() },
            });
        Assert.AreEqual(AnchorAdmissionCommitStatus.Expired, expired.Status);

        for (var index = 0; index < 520; index++)
        {
            await store.RecordAdmissionAuditAsync(
                created.Circle.Id,
                "forged",
                Now.AddSeconds(index));
        }

        await using var audit = new SqliteConnection(
            $"Data Source={Path.Combine(directory.Path, "balls.db")};Pooling=False");
        await audit.OpenAsync();
        using var count = audit.CreateCommand();
        count.CommandText =
            "SELECT COUNT(*) FROM security_audit_events WHERE circle_id = $circle_id;";
        count.Parameters.AddWithValue("$circle_id", created.Circle.Id.ToString());
        Assert.AreEqual(512L, (long)(await count.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task Joined_Node_persists_signed_membership_without_private_Circle_authority()
    {
        using var directory = new TemporaryDirectory();
        var circleId = CircleId.New();
        var invitationId = InvitationId.New();
        var digest = RandomNumberGenerator.GetBytes(32);
        NodeIdentity localNode;
        PublicIdentityCredential nodeCredential;
        PublicIdentityCredential transportCredential;
        AdmissionApplicantState applicant;
        var connectionState = new CircleConnectionState(
            circleId,
            1,
            "lan-tcp-v1",
            "192.168.50.10:43120",
            "192.168.50.10:43155",
            Now);
        await using (var store = await OpenAsync(directory.Path))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Bob-PC");
            localNode = await application.GetLocalNodeAsync();
            nodeCredential = (await store.GetNodeCryptographicIdentityAsync())!.Credential;
            transportCredential = (await store.GetLocalTransportIdentityAsync())!.Credential;
            applicant = await store.PrepareAdmissionApplicantAsync(
                invitationId,
                circleId,
                digest,
                "Bob",
                Now);
            var ownerId = MemberId.New();
            var anchorNodeId = NodeId.New();
            var root = Credential(IdentityKeyRole.CircleAuthority);
            var anchor = Credential(IdentityKeyRole.Anchor);
            var circle = new CircleDetails(
                new Circle(circleId, "Example Circle", Now.AddDays(-1)),
                [
                    new(ownerId, circleId, "Alice", MemberRole.Owner, Now.AddDays(-1)),
                    new(applicant.MemberId, circleId, "Bob", MemberRole.Member, Now),
                ],
                [
                    new(circleId, anchorNodeId, "Anchor-PC", Now.AddDays(-1)),
                    new(circleId, localNode.Id, localNode.DisplayName, Now),
                ]);
            var nodeSecurity = new[]
            {
                new CircleNodeSecurityState(
                    circleId,
                    anchorNodeId,
                    Credential(IdentityKeyRole.Node),
                    Credential(IdentityKeyRole.Transport),
                    "anchor-binding"u8.ToArray()),
                new CircleNodeSecurityState(
                    circleId,
                    localNode.Id,
                    nodeCredential,
                    transportCredential,
                    "local-binding"u8.ToArray()),
            };
            var joined = new JoinedCircleCommit(
                    invitationId,
                    digest,
                    circle,
                    new CircleTrustState(
                        circleId,
                        1,
                        1,
                        anchorNodeId,
                        root,
                        anchor,
                        "signed-membership"u8.ToArray()),
                    applicant.MemberCredential,
                    nodeSecurity,
                    connectionState,
                    Now);
            await store.CommitJoinedCircleAsync(joined);
            await store.CommitJoinedCircleAsync(joined);
            Assert.AreEqual(connectionState, await store.GetCircleConnectionAsync(circleId));
            var connectionConflict = await Assert.ThrowsExactlyAsync<LocalStateConflictException>(
                () => store.StoreCircleConnectionAsync(
                    connectionState with { SyncEndpoint = "192.168.50.11:43155" }));
            Assert.AreEqual("circle_connection_conflict", connectionConflict.Code);
            var conflict = joined with
            {
                Trust = joined.Trust with
                {
                    SignedAdmissionReceipt = "different-membership"u8.ToArray(),
                },
            };
            var conflictError = await Assert.ThrowsExactlyAsync<LocalStateConflictException>(
                () => store.CommitJoinedCircleAsync(conflict));
            Assert.AreEqual("admission_attempt_conflict", conflictError.Code);
        }

        await using (var connection = new SqliteConnection(
                         $"Data Source={Path.Combine(directory.Path, "balls.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT protection_scheme, protected_connection FROM circle_connections;";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(TestPrivateMaterialProtector.Instance.Scheme, reader.GetString(0));
            var protectedValue = (byte[])reader.GetValue(1);
            var rendered = Encoding.UTF8.GetString(protectedValue);
            Assert.IsFalse(rendered.Contains(connectionState.Provider, StringComparison.Ordinal));
            Assert.IsFalse(
                rendered.Contains(connectionState.AdmissionEndpoint, StringComparison.Ordinal));
            Assert.IsFalse(
                rendered.Contains(connectionState.SyncEndpoint, StringComparison.Ordinal));
        }

        await using var reopened = await OpenAsync(directory.Path);
        var persisted = await reopened.GetCircleAsync(circleId);
        Assert.AreEqual(2, persisted!.Members.Count);
        Assert.AreEqual(2, persisted.Nodes.Count);
        Assert.IsNull(await reopened.GetCircleAuthorityAsync(circleId));
        Assert.AreEqual(connectionState, await reopened.GetCircleConnectionAsync(circleId));
        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => reopened.ExportCircleAuthorityAsync(
                circleId,
                "correct horse battery staple".AsMemory()));
        Assert.AreEqual("circle_authority_not_found", error.Code);
    }

    [TestMethod]
    public async Task Corrupt_circle_connection_fails_closed_without_leaking_invitation_details()
    {
        using var directory = new TemporaryDirectory();
        CircleId circleId;
        await using (var store = await OpenAsync(directory.Path))
        {
            var created = await new CircleApplication(
                    store,
                    TimeProvider.System,
                    "Bob-PC")
                .CreateCircleAsync(
                    new CreateCircleCommand(
                        new CreationRequestId(Guid.CreateVersion7()),
                        "Example Circle",
                        "Bob"));
            circleId = created.Circle.Id;
            await store.StoreCircleConnectionAsync(
                new CircleConnectionState(
                    circleId,
                    1,
                    "lan-tcp-v1",
                    "192.168.50.10:43120",
                    "192.168.50.10:43155",
                    Now));
        }

        var databasePath = Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection(
                         $"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var corrupt = connection.CreateCommand();
            corrupt.CommandText =
                "UPDATE circle_connections SET protected_connection = X'00' WHERE circle_id = $circle_id;";
            corrupt.Parameters.AddWithValue("$circle_id", circleId.ToString());
            await corrupt.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => OpenAsync(directory.Path));
        Assert.AreEqual("invalid_circle_connection", error.Code);
        Assert.IsFalse(error.Message.Contains("192.168.50.10", StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains("lan-tcp-v1", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Failed_first_connection_protection_rolls_back_join_and_retry_succeeds()
    {
        using var directory = new TemporaryDirectory();
        JoinedCircleCommit commit;
        var circleId = CircleId.New();
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         RejectCircleConnectionProtector.Instance))
        {
            var application = new CircleApplication(
                store,
                TimeProvider.System,
                "Bob-PC");
            var localNode = await application.GetLocalNodeAsync();
            var nodeCredential = (await store.GetNodeCryptographicIdentityAsync())!.Credential;
            var transportCredential = (await store.GetLocalTransportIdentityAsync())!.Credential;
            var invitationId = InvitationId.New();
            var digest = RandomNumberGenerator.GetBytes(32);
            var applicant = await store.PrepareAdmissionApplicantAsync(
                invitationId,
                circleId,
                digest,
                "Bob",
                Now);
            var anchorNodeId = NodeId.New();
            var circle = new CircleDetails(
                new Circle(circleId, "Example Circle", Now),
                [
                    new(MemberId.New(), circleId, "Alice", MemberRole.Owner, Now),
                    new(applicant.MemberId, circleId, "Bob", MemberRole.Member, Now),
                ],
                [
                    new(circleId, anchorNodeId, "Alice-PC", Now),
                    new(circleId, localNode.Id, localNode.DisplayName, Now),
                ]);
            commit = new JoinedCircleCommit(
                invitationId,
                digest,
                circle,
                new CircleTrustState(
                    circleId,
                    1,
                    1,
                    anchorNodeId,
                    Credential(IdentityKeyRole.CircleAuthority),
                    Credential(IdentityKeyRole.Anchor),
                    "signed-membership"u8.ToArray()),
                applicant.MemberCredential,
                [
                    new CircleNodeSecurityState(
                        circleId,
                        anchorNodeId,
                        Credential(IdentityKeyRole.Node),
                        Credential(IdentityKeyRole.Transport),
                        "anchor-binding"u8.ToArray()),
                    new CircleNodeSecurityState(
                        circleId,
                        localNode.Id,
                        nodeCredential,
                        transportCredential,
                        "local-binding"u8.ToArray()),
                ],
                new CircleConnectionState(
                    circleId,
                    1,
                    "lan-tcp-v1",
                    "192.168.50.10:43120",
                    "192.168.50.10:43155",
                    Now),
                Now);

            await Assert.ThrowsExactlyAsync<CryptographicException>(
                () => store.CommitJoinedCircleAsync(commit));
            Assert.IsNull(await store.GetCircleAsync(circleId));
            Assert.IsNull(await store.GetCircleTrustAsync(circleId));
            Assert.IsNull(await store.GetCircleConnectionAsync(circleId));
        }

        await using var retry = await OpenAsync(directory.Path);
        await retry.CommitJoinedCircleAsync(commit);
        Assert.IsNotNull(await retry.GetCircleAsync(circleId));
        Assert.AreEqual(commit.Connection, await retry.GetCircleConnectionAsync(circleId));
    }

    [TestMethod]
    public async Task Version_eight_circle_connection_migration_is_atomic_and_restartable()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await OpenAsync(directory.Path))
        {
        }

        var databasePath = Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection(
                         $"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using (var downgrade = connection.CreateCommand())
            {
                downgrade.CommandText =
                    """
                    DROP TABLE circle_files_hosted_folders;
                    DROP TABLE circle_connections;
                    PRAGMA user_version = 8;
                    """;
                await downgrade.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => SqliteLocalStateStore.MigrateV8ToV9Async(
                    connection,
                    CancellationToken.None,
                    _ => throw new InvalidOperationException("injected")));
            using var inspect = connection.CreateCommand();
            inspect.CommandText =
                """
                SELECT (SELECT user_version FROM pragma_user_version),
                       (SELECT COUNT(*) FROM sqlite_master
                        WHERE type = 'table' AND name = 'circle_connections');
                """;
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(8L, reader.GetInt64(0));
            Assert.AreEqual(0L, reader.GetInt64(1));
        }

        await using var reopened = await OpenAsync(directory.Path);
        await using var migrated = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await migrated.OpenAsync();
        using var version = migrated.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.AreEqual(
            (long)SqliteLocalStateStore.CurrentSchemaVersion,
            (long)(await version.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task Version_nine_hosted_folder_migration_is_atomic_and_restartable()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await OpenAsync(directory.Path))
        {
        }

        var databasePath = Path.Combine(directory.Path, "balls.db");
        await using (var connection = new SqliteConnection(
                         $"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using (var downgrade = connection.CreateCommand())
            {
                downgrade.CommandText =
                    """
                    DROP TABLE circle_files_hosted_folders;
                    PRAGMA user_version = 9;
                    """;
                await downgrade.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => SqliteLocalStateStore.MigrateV9ToV10Async(
                    connection,
                    CancellationToken.None,
                    _ => throw new InvalidOperationException("injected")));
            using var inspect = connection.CreateCommand();
            inspect.CommandText =
                """
                SELECT (SELECT user_version FROM pragma_user_version),
                       (SELECT COUNT(*) FROM sqlite_master
                        WHERE type = 'table' AND name = 'circle_files_hosted_folders');
                """;
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(9L, reader.GetInt64(0));
            Assert.AreEqual(0L, reader.GetInt64(1));
        }

        await using var reopened = await OpenAsync(directory.Path);
        await using var migrated = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await migrated.OpenAsync();
        using var version = migrated.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.AreEqual(
            (long)SqliteLocalStateStore.CurrentSchemaVersion,
            (long)(await version.ExecuteScalarAsync())!);
    }

    private static Task<SqliteLocalStateStore> OpenAsync(string path) =>
        SqliteLocalStateStore.OpenAsync(path, TestPrivateMaterialProtector.Instance);

    private static PublicIdentityCredential Credential(IdentityKeyRole role)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return IdentityCryptography.CreateCredential(role, key);
    }

    private sealed class RejectCircleConnectionProtector : IPrivateMaterialProtector
    {
        internal static RejectCircleConnectionProtector Instance { get; } = new();

        public string Scheme => TestPrivateMaterialProtector.Instance.Scheme;

        public byte[] Protect(ReadOnlySpan<byte> privateMaterial)
        {
            if (privateMaterial.IndexOf("\"AdmissionEndpoint\""u8) >= 0)
            {
                throw new CryptographicException("injected connection protection failure");
            }

            return TestPrivateMaterialProtector.Instance.Protect(privateMaterial);
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial) =>
            TestPrivateMaterialProtector.Instance.Unprotect(protectedMaterial);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"balls-admission-{Guid.CreateVersion7():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
