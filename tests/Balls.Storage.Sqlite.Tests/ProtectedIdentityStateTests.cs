using System.Security.Cryptography;
using System.Text.Json;
using Balls.Core;
using Balls.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class ProtectedIdentityStateTests
{
    [TestMethod]
    public async Task Node_and_Circle_authority_are_distinct_protected_and_restart_stable()
    {
        using var directory = new TemporaryDirectory();
        var protector = new XorTestPrivateMaterialProtector();
        NodeCryptographicIdentity firstNode;
        CircleAuthorityIdentity firstAuthority;
        CircleId circleId;

        await using (var store = await SqliteLocalStateStore.OpenAsync(directory.Path, protector))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
            var node = await application.GetLocalNodeAsync();
            var circle = await application.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Example Studio",
                    "Alice"));
            circleId = circle.Circle.Id;
            firstNode = (await store.GetNodeCryptographicIdentityAsync())!;
            firstAuthority = (await store.GetCircleAuthorityAsync(circleId))!;

            Assert.AreEqual(node.Id, firstNode.NodeId);
            Assert.AreNotEqual(firstNode.Credential.KeyId, firstAuthority.RootCredential.KeyId);
            Assert.AreNotEqual(
                firstAuthority.RootCredential.KeyId,
                firstAuthority.AnchorCredential.KeyId);
            await AssertSignsAsync(
                firstNode.Credential,
                data => store.SignWithNodeAsync(data));
            await AssertSignsAsync(
                firstAuthority.RootCredential,
                data => store.SignWithCircleAuthorityAsync(circleId, data));
            await AssertSignsAsync(
                firstAuthority.AnchorCredential,
                data => store.SignWithCircleAnchorAsync(circleId, data));
        }

        await AssertProtectedRowsAsync(directory.Path, protector.Scheme);

        await using var reopened = await SqliteLocalStateStore.OpenAsync(directory.Path, protector);
        var secondNode = await reopened.GetNodeCryptographicIdentityAsync();
        var secondAuthority = await reopened.GetCircleAuthorityAsync(circleId);

        Assert.IsNotNull(secondNode);
        Assert.IsNotNull(secondAuthority);
        Assert.AreEqual(firstNode.Credential.KeyId, secondNode.Credential.KeyId);
        Assert.AreEqual(firstAuthority.RootCredential.KeyId, secondAuthority.RootCredential.KeyId);
        Assert.AreEqual(firstAuthority.AnchorCredential.KeyId, secondAuthority.AnchorCredential.KeyId);
    }

    [TestMethod]
    public async Task Malformed_private_material_fails_closed_without_regeneration()
    {
        using var directory = new TemporaryDirectory();
        var protector = new XorTestPrivateMaterialProtector();
        string keyId;
        await using (var store = await SqliteLocalStateStore.OpenAsync(directory.Path, protector))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
            await application.GetLocalNodeAsync();
            keyId = (await store.GetNodeCryptographicIdentityAsync())!.Credential.KeyId;
        }

        await using (var connection = OpenDatabase(directory.Path))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE local_node_credentials SET protected_private_key = X'00FF';";
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => SqliteLocalStateStore.OpenAsync(directory.Path, protector));

        await using var verificationConnection = OpenDatabase(directory.Path);
        await verificationConnection.OpenAsync();
        using var verificationCommand = verificationConnection.CreateCommand();
        verificationCommand.CommandText = "SELECT key_id FROM local_node_credentials;";
        var preservedKeyId = (string?)await verificationCommand.ExecuteScalarAsync();

        Assert.AreEqual("invalid_private_material", error.Code);
        Assert.AreEqual(keyId, preservedKeyId);
        Assert.IsFalse(error.Message.Contains(keyId, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Explicit_authority_backup_is_encrypted_signed_and_context_bound()
    {
        using var directory = new TemporaryDirectory();
        var protector = new XorTestPrivateMaterialProtector();
        await using var store = await SqliteLocalStateStore.OpenAsync(directory.Path, protector);
        var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
        var circle = await application.CreateCircleAsync(
            new CreateCircleCommand(
                new CreationRequestId(Guid.CreateVersion7()),
                "Example Studio",
                "Alice"));
        var authority = (await store.GetCircleAuthorityAsync(circle.Circle.Id))!;
        var passphrase = "correct horse battery staple".ToCharArray();

        var envelope = await store.ExportCircleAuthorityAsync(circle.Circle.Id, passphrase);
        using var output = new MemoryStream();
        envelope.WriteTo(output);
        var bytes = output.ToArray();
        var validation = AuthorityBackupValidator.Validate(bytes, circle.Circle.Id);

        Assert.IsTrue(validation.IsValid);
        Assert.AreEqual(authority.RootCredential.KeyId, validation.RootKeyId);
        using var document = JsonDocument.Parse(bytes);
        var encryptedRoot = Convert.FromBase64String(
            document.RootElement.GetProperty("encryptedRootPrivateKeyPkcs8").GetString()!);
        using var restoredRoot = ECDsa.Create();
        restoredRoot.ImportEncryptedPkcs8PrivateKey(passphrase, encryptedRoot, out var bytesRead);
        Assert.AreEqual(encryptedRoot.Length, bytesRead);
        CollectionAssert.AreEqual(
            authority.RootCredential.SubjectPublicKeyInfo,
            restoredRoot.ExportSubjectPublicKeyInfo());
        Assert.IsFalse(bytes.AsSpan().IndexOf(restoredRoot.ExportPkcs8PrivateKey()) >= 0);

        var wrongCircle = AuthorityBackupValidator.Validate(
            bytes,
            new CircleId(Guid.Parse("0198c837-3000-7000-8000-000000000099")));
        Assert.IsFalse(wrongCircle.IsValid);
        Assert.AreEqual(AuthorityBackupRejectionCode.WrongCircle, wrongCircle.RejectionCode);

        var forged = bytes.ToArray();
        forged[^8] ^= 0x01;
        var forgedResult = AuthorityBackupValidator.Validate(forged, circle.Circle.Id);
        Assert.IsFalse(forgedResult.IsValid);
        Assert.AreNotEqual(AuthorityBackupRejectionCode.None, forgedResult.RejectionCode);

        var unknownVersion = bytes.ToArray();
        var versionOffset = "\"version\":1"u8.ToArray();
        var versionIndex = unknownVersion.AsSpan().IndexOf(versionOffset);
        Assert.IsTrue(versionIndex >= 0);
        unknownVersion[versionIndex + versionOffset.Length - 1] = (byte)'2';
        var unknownVersionResult = AuthorityBackupValidator.Validate(
            unknownVersion,
            circle.Circle.Id);
        Assert.AreEqual(
            AuthorityBackupRejectionCode.Malformed,
            unknownVersionResult.RejectionCode);

        CryptographicOperations.ZeroMemory(passphrase.AsSpan().AsBytes());
    }

    [TestMethod]
    public async Task Version_one_migration_is_atomic_and_generates_missing_identity_once()
    {
        using var directory = new TemporaryDirectory();
        CircleId circleId;
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
            var circle = await application.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Migration Circle",
                    "Alice"));
            circleId = circle.Circle.Id;
        }

        await DowngradeToVersionOneAsync(directory.Path);

        await Assert.ThrowsExactlyAsync<CryptographicException>(
            () => SqliteLocalStateStore.OpenAsync(
                directory.Path,
                new ThrowingMigrationProtector(failOnProtectionNumber: 2)));
        await AssertVersionOneIdentityTablesAbsentAsync(directory.Path);

        string migratedNodeKeyId;
        string migratedRootKeyId;
        await using (var migrated = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            migratedNodeKeyId = (await migrated.GetNodeCryptographicIdentityAsync())!.Credential.KeyId;
            migratedRootKeyId = (await migrated.GetCircleAuthorityAsync(circleId))!.RootCredential.KeyId;
        }

        await using var restarted = await SqliteLocalStateStore.OpenAsync(
            directory.Path,
            TestPrivateMaterialProtector.Instance);
        Assert.AreEqual(
            migratedNodeKeyId,
            (await restarted.GetNodeCryptographicIdentityAsync())!.Credential.KeyId);
        Assert.AreEqual(
            migratedRootKeyId,
            (await restarted.GetCircleAuthorityAsync(circleId))!.RootCredential.KeyId);
    }

    [TestMethod]
    public async Task Protection_scheme_substitution_fails_closed()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
            await application.GetLocalNodeAsync();
        }

        await using (var connection = OpenDatabase(directory.Path))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE local_node_credentials SET private_key_scheme = 'other-scheme-v1';";
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => SqliteLocalStateStore.OpenAsync(
                directory.Path,
                TestPrivateMaterialProtector.Instance));
        Assert.AreEqual("invalid_private_material", error.Code);
    }

    [TestMethod]
    public async Task Missing_credential_rows_fail_closed_without_regeneration()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
            await application.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Missing Authority Circle",
                    "Alice"));
        }

        await using (var connection = OpenDatabase(directory.Path))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM local_node_credentials;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => SqliteLocalStateStore.OpenAsync(
                directory.Path,
                TestPrivateMaterialProtector.Instance));
        Assert.AreEqual("invalid_private_material", error.Code);

        await using var verification = OpenDatabase(directory.Path);
        await verification.OpenAsync();
        using var verificationCommand = verification.CreateCommand();
        verificationCommand.CommandText =
            "SELECT COUNT(*) FROM local_node_credentials;";
        Assert.AreEqual(0L, (long)(await verificationCommand.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task Invalid_authority_generation_fails_closed()
    {
        using var directory = new TemporaryDirectory();
        await using (var store = await SqliteLocalStateStore.OpenAsync(
                         directory.Path,
                         TestPrivateMaterialProtector.Instance))
        {
            var application = new CircleApplication(store, TimeProvider.System, "Alice-PC");
            await application.CreateCircleAsync(
                new CreateCircleCommand(
                    new CreationRequestId(Guid.CreateVersion7()),
                    "Invalid Authority Circle",
                    "Alice"));
        }

        await using (var connection = OpenDatabase(directory.Path))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE circle_authorities SET authority_generation = 0;";
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => SqliteLocalStateStore.OpenAsync(
                directory.Path,
                TestPrivateMaterialProtector.Instance));
        Assert.AreEqual("invalid_private_material", error.Code);
    }

    private static async Task DowngradeToVersionOneAsync(string directory)
    {
        await using var connection = OpenDatabase(directory);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = OFF;
            DROP TABLE revoked_invitations;
            DROP TABLE invitation_redemptions;
            DROP TABLE circle_invitations;
            DROP TABLE local_transport_credentials;
            DROP TABLE circle_authorities;
            DROP TABLE local_node_credentials;
            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertVersionOneIdentityTablesAbsentAsync(string directory)
    {
        await using var connection = OpenDatabase(directory);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT user_version FROM pragma_user_version),
                (SELECT COUNT(*) FROM sqlite_master
                 WHERE type = 'table'
                   AND name IN ('local_node_credentials', 'circle_authorities'));
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(1L, reader.GetInt64(0));
        Assert.AreEqual(0L, reader.GetInt64(1));
    }

    private static async Task AssertSignsAsync(
        PublicIdentityCredential credential,
        Func<ReadOnlyMemory<byte>, Task<byte[]>> signer)
    {
        var message = "identity-state-proof"u8.ToArray();
        var signature = await signer(message);
        Assert.IsTrue(IdentityCryptography.Verify(message, signature, credential));
    }

    private static async Task AssertProtectedRowsAsync(string directory, string scheme)
    {
        await using var connection = OpenDatabase(directory);
        await connection.OpenAsync();
        foreach (var query in new[]
                 {
                     "SELECT private_key_scheme, protected_private_key FROM local_node_credentials;",
                     "SELECT private_key_scheme, root_protected_private_key FROM circle_authorities;",
                     "SELECT private_key_scheme, anchor_protected_private_key FROM circle_authorities;",
                 })
        {
            using var command = connection.CreateCommand();
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(scheme, reader.GetString(0));
            var value = (byte[])reader.GetValue(1);
            Assert.AreEqual(XorTestPrivateMaterialProtector.Marker, value[0]);
        }
    }

    private static SqliteConnection OpenDatabase(string directory) =>
        new($"Data Source={Path.Combine(directory, "balls.db")};Pooling=False");

    private sealed class XorTestPrivateMaterialProtector : IPrivateMaterialProtector
    {
        public const byte Marker = 0xA5;

        public string Scheme => "test-xor-v1";

        public byte[] Protect(ReadOnlySpan<byte> privateMaterial)
        {
            var protectedMaterial = new byte[privateMaterial.Length + 1];
            protectedMaterial[0] = Marker;
            for (var index = 0; index < privateMaterial.Length; index++)
            {
                protectedMaterial[index + 1] = (byte)(privateMaterial[index] ^ 0x5a);
            }

            return protectedMaterial;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial)
        {
            if (protectedMaterial.Length < 2 || protectedMaterial[0] != Marker)
            {
                throw new CryptographicException("The test private material is malformed.");
            }

            var privateMaterial = new byte[protectedMaterial.Length - 1];
            for (var index = 0; index < privateMaterial.Length; index++)
            {
                privateMaterial[index] = (byte)(protectedMaterial[index + 1] ^ 0x5a);
            }

            return privateMaterial;
        }
    }

    private sealed class ThrowingMigrationProtector(int failOnProtectionNumber) :
        IPrivateMaterialProtector
    {
        private int protectionCount;

        public string Scheme => TestPrivateMaterialProtector.Instance.Scheme;

        public byte[] Protect(ReadOnlySpan<byte> privateMaterial)
        {
            if (Interlocked.Increment(ref protectionCount) == failOnProtectionNumber)
            {
                throw new CryptographicException("Injected migration failure.");
            }

            return TestPrivateMaterialProtector.Instance.Protect(privateMaterial);
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial) =>
            TestPrivateMaterialProtector.Instance.Unprotect(protectedMaterial);
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

internal static class CharSpanExtensions
{
    public static Span<byte> AsBytes(this Span<char> value) =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(value);
}
