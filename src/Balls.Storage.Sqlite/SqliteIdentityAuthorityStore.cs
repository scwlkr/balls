using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    private const int BackupPbkdf2Iterations = 600_000;
    private const string IdentitySchemaSql =
        """
        CREATE TABLE local_node_credentials (
            singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
            key_algorithm TEXT NOT NULL,
            key_id TEXT NOT NULL UNIQUE,
            public_key_spki BLOB NOT NULL,
            private_key_scheme TEXT NOT NULL,
            protected_private_key BLOB NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (singleton_id) REFERENCES local_node(singleton_id) ON DELETE CASCADE
        );

        CREATE TABLE circle_authorities (
            circle_id TEXT NOT NULL PRIMARY KEY,
            authority_generation INTEGER NOT NULL,
            root_key_id TEXT NOT NULL UNIQUE,
            root_public_key_spki BLOB NOT NULL,
            root_protected_private_key BLOB NOT NULL,
            anchor_key_id TEXT NOT NULL UNIQUE,
            anchor_public_key_spki BLOB NOT NULL,
            anchor_protected_private_key BLOB NOT NULL,
            private_key_scheme TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE
        );
        """;

    public Task<NodeCryptographicIdentity?> GetNodeCryptographicIdentityAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            token => ReadNodeCryptographicIdentityAsync(transaction: null, token),
            cancellationToken);

    public Task<CircleAuthorityIdentity?> GetCircleAuthorityAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            token => ReadCircleAuthorityAsync(circleId, transaction: null, token),
            cancellationToken);

    public Task<LocalTransportIdentity?> GetLocalTransportIdentityAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            token => ReadLocalTransportIdentityAsync(transaction: null, token),
            cancellationToken);

    public Task<X509Certificate2> CreateTransportCertificateAsync(
        string dnsName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dnsName);
        if (dnsName.Length > 253
            || nowUtc.Offset != TimeSpan.Zero
            || dnsName.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '.')))
        {
            throw new ArgumentException("The transport certificate request is invalid.");
        }

        return ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadTransportPrivateIdentityAsync(transaction: null, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "transport_identity_missing",
                        "The local transport cryptographic identity is missing.");
                using var key = OpenPrivateKey(stored);
                var request = new CertificateRequest(
                    $"CN={dnsName}",
                    key,
                    HashAlgorithmName.SHA256);
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName(dnsName);
                request.CertificateExtensions.Add(names.Build());
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, critical: true));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature,
                        critical: true));
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection
                        {
                            new("1.3.6.1.5.5.7.3.1"),
                            new("1.3.6.1.5.5.7.3.2"),
                        },
                        critical: true));
                using var generated = request.CreateSelfSigned(
                    nowUtc.AddMinutes(-5),
                    nowUtc.AddHours(24));
                return X509CertificateLoader.LoadPkcs12(
                    generated.Export(X509ContentType.Pkcs12),
                    password: null,
                    OperatingSystem.IsWindows()
                        ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable
                        : OperatingSystem.IsMacOS()
                            ? X509KeyStorageFlags.Exportable
                            : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
            },
            cancellationToken);
    }

    public Task<byte[]> SignWithNodeAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadNodePrivateIdentityAsync(transaction: null, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "node_identity_missing",
                        "The local Node cryptographic identity is missing.");
                using var key = OpenPrivateKey(stored);
                return IdentityCryptography.Sign(data.Span, key);
            },
            cancellationToken);

    public Task<byte[]> SignWithTransportAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadTransportPrivateIdentityAsync(transaction: null, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "transport_identity_missing",
                        "The local transport cryptographic identity is missing.");
                using var key = OpenPrivateKey(stored);
                return IdentityCryptography.Sign(data.Span, key);
            },
            cancellationToken);

    public Task<byte[]> SignWithCircleAuthorityAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        SignWithCircleKeyAsync(circleId, data, useAnchor: false, cancellationToken);

    public Task<byte[]> SignWithCircleAnchorAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        SignWithCircleKeyAsync(circleId, data, useAnchor: true, cancellationToken);

    public Task<AuthorityBackupEnvelope> ExportCircleAuthorityAsync(
        CircleId circleId,
        ReadOnlyMemory<char> passphrase,
        CancellationToken cancellationToken = default)
    {
        if (passphrase.Length is < 12 or > 1024)
        {
            throw new ArgumentException(
                "Authority backup passphrases must contain between 12 and 1024 characters.",
                nameof(passphrase));
        }

        return ExecuteLockedAsync(
            async token =>
            {
                var authority = await ReadCirclePrivateAuthorityAsync(
                    circleId,
                    transaction: null,
                    token).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_authority_not_found",
                        "The requested Circle authority is not known to this Node.");
                using var rootKey = OpenPrivateKey(authority.Root);
                using var anchorKey = OpenPrivateKey(authority.Anchor);
                return CreateBackupEnvelope(
                    authority.Identity,
                    authority.CreatedAtUtc,
                    rootKey,
                    anchorKey,
                    passphrase.Span);
            },
            cancellationToken);
    }

    private Task<byte[]> SignWithCircleKeyAsync(
        CircleId circleId,
        ReadOnlyMemory<byte> data,
        bool useAnchor,
        CancellationToken cancellationToken) =>
        ExecuteLockedAsync(
            async token =>
            {
                var authority = await ReadCirclePrivateAuthorityAsync(
                    circleId,
                    transaction: null,
                    token).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_authority_not_found",
                        "The requested Circle authority is not known to this Node.");
                using var key = OpenPrivateKey(useAnchor ? authority.Anchor : authority.Root);
                return IdentityCryptography.Sign(data.Span, key);
            },
            cancellationToken);

    private async Task<NodeCryptographicIdentity?> ReadNodeCryptographicIdentityAsync(
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT local_node.node_id,
                   local_node_credentials.key_algorithm,
                   local_node_credentials.key_id,
                   local_node_credentials.public_key_spki
            FROM local_node_credentials
            INNER JOIN local_node
              ON local_node.singleton_id = local_node_credentials.singleton_id
            WHERE local_node_credentials.singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var credential = ReadCredential(
            IdentityKeyRole.Node,
            reader.GetString(1),
            reader.GetString(2),
            (byte[])reader.GetValue(3));
        return new NodeCryptographicIdentity(
            new NodeId(Guid.Parse(reader.GetString(0))),
            credential);
    }

    private async Task<LocalTransportIdentity?> ReadLocalTransportIdentityAsync(
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT local_node.node_id,
                   local_transport_credentials.key_algorithm,
                   local_transport_credentials.key_id,
                   local_transport_credentials.public_key_spki
            FROM local_transport_credentials
            INNER JOIN local_node
              ON local_node.singleton_id = local_transport_credentials.singleton_id
            WHERE local_transport_credentials.singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new LocalTransportIdentity(
            new NodeId(Guid.Parse(reader.GetString(0))),
            ReadCredential(
                IdentityKeyRole.Transport,
                reader.GetString(1),
                reader.GetString(2),
                (byte[])reader.GetValue(3)));
    }

    private async Task<CircleAuthorityIdentity?> ReadCircleAuthorityAsync(
        CircleId circleId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var authority = await ReadCirclePrivateAuthorityAsync(
            circleId,
            transaction,
            cancellationToken).ConfigureAwait(false);
        return authority?.Identity;
    }

    private async Task<StoredPrivateIdentity?> ReadNodePrivateIdentityAsync(
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT key_algorithm,
                   key_id,
                   public_key_spki,
                   private_key_scheme,
                   protected_private_key
            FROM local_node_credentials
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredPrivateIdentity(
            ReadCredential(
                IdentityKeyRole.Node,
                reader.GetString(0),
                reader.GetString(1),
                (byte[])reader.GetValue(2)),
            reader.GetString(3),
            (byte[])reader.GetValue(4));
    }

    private async Task<StoredPrivateIdentity?> ReadTransportPrivateIdentityAsync(
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT key_algorithm, key_id, public_key_spki,
                   private_key_scheme, protected_private_key
            FROM local_transport_credentials
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredPrivateIdentity(
            ReadCredential(
                IdentityKeyRole.Transport,
                reader.GetString(0),
                reader.GetString(1),
                (byte[])reader.GetValue(2)),
            reader.GetString(3),
            (byte[])reader.GetValue(4));
    }

    private async Task<StoredCircleAuthority?> ReadCirclePrivateAuthorityAsync(
        CircleId circleId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT authority_generation,
                   root_key_id,
                   root_public_key_spki,
                   root_protected_private_key,
                   anchor_key_id,
                   anchor_public_key_spki,
                   anchor_protected_private_key,
                   private_key_scheme,
                   created_at_utc
            FROM circle_authorities
            WHERE circle_id = $circle_id;
            """;
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var generation = reader.GetInt64(0);
        var root = new StoredPrivateIdentity(
            ReadCredential(
                IdentityKeyRole.CircleAuthority,
                IdentityCryptography.Algorithm,
                reader.GetString(1),
                (byte[])reader.GetValue(2)),
            reader.GetString(7),
            (byte[])reader.GetValue(3));
        var anchor = new StoredPrivateIdentity(
            ReadCredential(
                IdentityKeyRole.Anchor,
                IdentityCryptography.Algorithm,
                reader.GetString(4),
                (byte[])reader.GetValue(5)),
            reader.GetString(7),
            (byte[])reader.GetValue(6));
        return new StoredCircleAuthority(
            new CircleAuthorityIdentity(circleId, generation, root.Credential, anchor.Credential),
            root,
            anchor,
            ParseTimestamp(reader.GetString(8)));
    }

    private static PublicIdentityCredential ReadCredential(
        IdentityKeyRole role,
        string algorithm,
        string keyId,
        byte[] subjectPublicKeyInfo)
    {
        var credential = new PublicIdentityCredential(role, algorithm, keyId, subjectPublicKeyInfo);
        if (!IdentityCryptography.IsValidCredential(credential))
        {
            throw InvalidPrivateMaterial();
        }

        return credential;
    }

    private ECDsa OpenPrivateKey(StoredPrivateIdentity stored)
    {
        if (!string.Equals(
                stored.ProtectionScheme,
                privateMaterialProtector.Scheme,
                StringComparison.Ordinal))
        {
            throw InvalidPrivateMaterial();
        }

        byte[]? privateKey = null;
        try
        {
            privateKey = privateMaterialProtector.Unprotect(stored.ProtectedPrivateKey);
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
            var actual = IdentityCryptography.CreateCredential(stored.Credential.Role, key);
            if (bytesRead != privateKey.Length
                || !string.Equals(
                    actual.KeyId,
                    stored.Credential.KeyId,
                    StringComparison.Ordinal)
                || !CryptographicOperations.FixedTimeEquals(
                    actual.SubjectPublicKeyInfo,
                    stored.Credential.SubjectPublicKeyInfo))
            {
                key.Dispose();
                throw InvalidPrivateMaterial();
            }

            return key;
        }
        catch (LocalStateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw InvalidPrivateMaterial();
        }
        finally
        {
            if (privateKey is not null)
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }

            CryptographicOperations.ZeroMemory(stored.ProtectedPrivateKey);
        }
    }

    private async Task InsertNodeCryptographicIdentityAsync(
        NodeIdentity node,
        IPrivateMaterialProtector protector,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var material = GeneratePrivateIdentity(IdentityKeyRole.Node, protector);
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO local_node_credentials (
                    singleton_id,
                    key_algorithm,
                    key_id,
                    public_key_spki,
                    private_key_scheme,
                    protected_private_key,
                    created_at_utc)
                VALUES (1, $algorithm, $key_id, $spki, $scheme, $private_key, $created_at_utc);
                """;
            AddIdentityParameters(command, material, node.CreatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.ProtectedPrivateKey);
        }
    }

    private async Task InsertTransportIdentityAsync(
        NodeIdentity node,
        IPrivateMaterialProtector protector,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var material = GeneratePrivateIdentity(IdentityKeyRole.Transport, protector);
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO local_transport_credentials (
                    singleton_id, key_algorithm, key_id, public_key_spki,
                    private_key_scheme, protected_private_key, created_at_utc)
                VALUES (1, $algorithm, $key_id, $spki, $scheme, $private_key, $created_at_utc);
                """;
            AddIdentityParameters(command, material, node.CreatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.ProtectedPrivateKey);
        }
    }

    private async Task InsertCircleAuthorityAsync(
        Circle circle,
        IPrivateMaterialProtector protector,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var root = GeneratePrivateIdentity(IdentityKeyRole.CircleAuthority, protector);
        try
        {
            var anchor = GeneratePrivateIdentity(IdentityKeyRole.Anchor, protector);
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO circle_authorities (
                        circle_id,
                        authority_generation,
                        root_key_id,
                        root_public_key_spki,
                        root_protected_private_key,
                        anchor_key_id,
                        anchor_public_key_spki,
                        anchor_protected_private_key,
                        private_key_scheme,
                        created_at_utc)
                    VALUES (
                        $circle_id, 1,
                        $root_key_id, $root_spki, $root_private_key,
                        $anchor_key_id, $anchor_spki, $anchor_private_key,
                        $scheme, $created_at_utc);
                    """;
                command.Parameters.AddWithValue("$circle_id", circle.Id.ToString());
                command.Parameters.AddWithValue("$root_key_id", root.Credential.KeyId);
                command.Parameters.AddWithValue("$root_spki", root.Credential.SubjectPublicKeyInfo);
                command.Parameters.AddWithValue("$root_private_key", root.ProtectedPrivateKey);
                command.Parameters.AddWithValue("$anchor_key_id", anchor.Credential.KeyId);
                command.Parameters.AddWithValue("$anchor_spki", anchor.Credential.SubjectPublicKeyInfo);
                command.Parameters.AddWithValue("$anchor_private_key", anchor.ProtectedPrivateKey);
                command.Parameters.AddWithValue("$scheme", protector.Scheme);
                command.Parameters.AddWithValue("$created_at_utc", Format(circle.CreatedAtUtc));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(anchor.ProtectedPrivateKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root.ProtectedPrivateKey);
        }
    }

    private static GeneratedPrivateIdentity GeneratePrivateIdentity(
        IdentityKeyRole role,
        IPrivateMaterialProtector protector)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = IdentityCryptography.CreateCredential(role, key);
        var privateKey = key.ExportPkcs8PrivateKey();
        try
        {
            return new GeneratedPrivateIdentity(
                credential,
                protector.Scheme,
                protector.Protect(privateKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static void AddIdentityParameters(
        SqliteCommand command,
        GeneratedPrivateIdentity material,
        DateTimeOffset createdAtUtc)
    {
        command.Parameters.AddWithValue("$algorithm", material.Credential.Algorithm);
        command.Parameters.AddWithValue("$key_id", material.Credential.KeyId);
        command.Parameters.AddWithValue("$spki", material.Credential.SubjectPublicKeyInfo);
        command.Parameters.AddWithValue("$scheme", material.ProtectionScheme);
        command.Parameters.AddWithValue("$private_key", material.ProtectedPrivateKey);
        command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
    }

    private static async Task MigrateV1ToV2Async(
        SqliteConnection connection,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = IdentitySchemaSql;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        NodeIdentity? node = null;
        using (var nodeCommand = connection.CreateCommand())
        {
            nodeCommand.Transaction = transaction;
            nodeCommand.CommandText =
                "SELECT node_id, display_name, created_at_utc FROM local_node WHERE singleton_id = 1;";
            await using var reader = await nodeCommand.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                node = new NodeIdentity(
                    new NodeId(Guid.Parse(reader.GetString(0))),
                    reader.GetString(1),
                    ParseTimestamp(reader.GetString(2)));
            }
        }

        if (node is not null)
        {
            await InsertMigratedNodeAsync(connection, transaction, node, protector, cancellationToken)
                .ConfigureAwait(false);
        }

        var circles = new List<Circle>();
        using (var circleCommand = connection.CreateCommand())
        {
            circleCommand.Transaction = transaction;
            circleCommand.CommandText = "SELECT circle_id, name, created_at_utc FROM circles;";
            await using var reader = await circleCommand.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                circles.Add(new Circle(
                    new CircleId(Guid.Parse(reader.GetString(0))),
                    reader.GetString(1),
                    ParseTimestamp(reader.GetString(2))));
            }
        }

        foreach (var circle in circles)
        {
            await InsertMigratedAuthorityAsync(
                connection,
                transaction,
                circle,
                protector,
                cancellationToken).ConfigureAwait(false);
        }

        using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version = 2;";
            await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertMigratedNodeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NodeIdentity node,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        var material = GeneratePrivateIdentity(IdentityKeyRole.Node, protector);
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO local_node_credentials (
                    singleton_id, key_algorithm, key_id, public_key_spki,
                    private_key_scheme, protected_private_key, created_at_utc)
                VALUES (1, $algorithm, $key_id, $spki, $scheme, $private_key, $created_at_utc);
                """;
            AddIdentityParameters(command, material, node.CreatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.ProtectedPrivateKey);
        }
    }

    private static async Task InsertMigratedAuthorityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Circle circle,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        var root = GeneratePrivateIdentity(IdentityKeyRole.CircleAuthority, protector);
        try
        {
            var anchor = GeneratePrivateIdentity(IdentityKeyRole.Anchor, protector);
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO circle_authorities (
                        circle_id, authority_generation,
                        root_key_id, root_public_key_spki, root_protected_private_key,
                        anchor_key_id, anchor_public_key_spki, anchor_protected_private_key,
                        private_key_scheme, created_at_utc)
                    VALUES (
                        $circle_id, 1,
                        $root_key_id, $root_spki, $root_private_key,
                        $anchor_key_id, $anchor_spki, $anchor_private_key,
                        $scheme, $created_at_utc);
                    """;
                command.Parameters.AddWithValue("$circle_id", circle.Id.ToString());
                command.Parameters.AddWithValue("$root_key_id", root.Credential.KeyId);
                command.Parameters.AddWithValue("$root_spki", root.Credential.SubjectPublicKeyInfo);
                command.Parameters.AddWithValue("$root_private_key", root.ProtectedPrivateKey);
                command.Parameters.AddWithValue("$anchor_key_id", anchor.Credential.KeyId);
                command.Parameters.AddWithValue("$anchor_spki", anchor.Credential.SubjectPublicKeyInfo);
                command.Parameters.AddWithValue("$anchor_private_key", anchor.ProtectedPrivateKey);
                command.Parameters.AddWithValue("$scheme", protector.Scheme);
                command.Parameters.AddWithValue("$created_at_utc", Format(circle.CreatedAtUtc));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(anchor.ProtectedPrivateKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root.ProtectedPrivateKey);
        }
    }

    private static AuthorityBackupEnvelope CreateBackupEnvelope(
        CircleAuthorityIdentity authority,
        DateTimeOffset createdAtUtc,
        ECDsa rootKey,
        ECDsa anchorKey,
        ReadOnlySpan<char> passphrase)
    {
        var pbe = new PbeParameters(
            PbeEncryptionAlgorithm.Aes256Cbc,
            HashAlgorithmName.SHA256,
            BackupPbkdf2Iterations);
        var encryptedRoot = rootKey.ExportEncryptedPkcs8PrivateKey(passphrase, pbe);
        var encryptedAnchor = anchorKey.ExportEncryptedPkcs8PrivateKey(passphrase, pbe);
        try
        {
            var manifest = WriteBackupManifest(
                authority,
                createdAtUtc,
                SHA256.HashData(encryptedRoot),
                SHA256.HashData(encryptedAnchor));
            var signature = IdentityCryptography.Sign(manifest, rootKey);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteString("format", "balls-circle-authority-backup");
                writer.WriteNumber("version", 1);
                writer.WriteBase64String("manifest", manifest);
                writer.WriteBase64String("manifestSignature", signature);
                writer.WriteBase64String("encryptedRootPrivateKeyPkcs8", encryptedRoot);
                writer.WriteBase64String("encryptedAnchorPrivateKeyPkcs8", encryptedAnchor);
                writer.WriteEndObject();
            }

            return new AuthorityBackupEnvelope(output.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedRoot);
            CryptographicOperations.ZeroMemory(encryptedAnchor);
        }
    }

    private static byte[] WriteBackupManifest(
        CircleAuthorityIdentity authority,
        DateTimeOffset createdAtUtc,
        byte[] encryptedRootDigest,
        byte[] encryptedAnchorDigest)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "balls-circle-authority-backup-manifest");
            writer.WriteNumber("version", 1);
            writer.WriteString("circleId", authority.CircleId.ToString());
            writer.WriteNumber("authorityGeneration", authority.AuthorityGeneration);
            writer.WriteString("createdAtUtc", Format(createdAtUtc));
            WriteBackupCredential(writer, "rootCredential", authority.RootCredential);
            WriteBackupCredential(writer, "anchorCredential", authority.AnchorCredential);
            writer.WriteString("privateKeyEncoding", "encrypted-pkcs8");
            writer.WriteString("pbeEncryption", "aes-256-cbc");
            writer.WriteString("pbeKdf", "pbkdf2-hmac-sha256");
            writer.WriteNumber("pbeIterations", BackupPbkdf2Iterations);
            writer.WriteBase64String("encryptedRootSha256", encryptedRootDigest);
            writer.WriteBase64String("encryptedAnchorSha256", encryptedAnchorDigest);
            writer.WriteEndObject();
        }

        return output.ToArray();
    }

    private static void WriteBackupCredential(
        Utf8JsonWriter writer,
        string propertyName,
        PublicIdentityCredential credential)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("role", credential.Role switch
        {
            IdentityKeyRole.CircleAuthority => "circle-authority",
            IdentityKeyRole.Anchor => "anchor",
            _ => throw new InvalidOperationException("An authority backup contains an invalid role."),
        });
        writer.WriteString("algorithm", credential.Algorithm);
        writer.WriteString("keyId", credential.KeyId);
        writer.WriteBase64String("subjectPublicKeyInfo", credential.SubjectPublicKeyInfo);
        writer.WriteEndObject();
    }

    private static async Task ValidatePrivateMaterialsAsync(
        SqliteConnection connection,
        IPrivateMaterialProtector protector,
        CancellationToken cancellationToken)
    {
        using (var completeness = connection.CreateCommand())
        {
            completeness.CommandText =
                """
                SELECT
                    (SELECT COUNT(*) FROM local_node),
                    (SELECT COUNT(*) FROM local_node_credentials),
                    (SELECT COUNT(*) FROM local_transport_credentials),
                    (SELECT COUNT(*) FROM circles),
                    (SELECT COUNT(*) FROM circle_authorities),
                    (SELECT COUNT(*) FROM circle_authorities WHERE authority_generation < 1),
                    (SELECT COUNT(*) FROM circle_trust),
                    (SELECT COUNT(*) FROM local_circle_members);
                """;
            await using var reader = await completeness.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || reader.GetInt64(0) != reader.GetInt64(1)
                || reader.GetInt64(0) != reader.GetInt64(2)
                || reader.GetInt64(4) > reader.GetInt64(3)
                || reader.GetInt64(5) != 0
                || reader.GetInt64(6) != reader.GetInt64(3)
                || reader.GetInt64(7) != reader.GetInt64(3))
            {
                throw InvalidPrivateMaterial();
            }
        }

        using (var trust = connection.CreateCommand())
        {
            trust.CommandText =
                """
                SELECT t.authority_generation, t.authority_sequence,
                       t.root_key_algorithm, t.root_key_id, t.root_public_key_spki,
                       t.anchor_key_algorithm, t.anchor_key_id, t.anchor_public_key_spki,
                       a.authority_generation, a.root_key_id, a.root_public_key_spki,
                       a.anchor_key_id, a.anchor_public_key_spki
                FROM circle_trust t
                LEFT JOIN circle_authorities a ON a.circle_id = t.circle_id;
                """;
            await using var reader = await trust.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var root = ReadCredential(
                        IdentityKeyRole.CircleAuthority,
                        reader.GetString(2),
                        reader.GetString(3),
                        (byte[])reader.GetValue(4));
                    var anchor = ReadCredential(
                        IdentityKeyRole.Anchor,
                        reader.GetString(5),
                        reader.GetString(6),
                        (byte[])reader.GetValue(7));
                    if (reader.GetInt64(0) < 1 || reader.GetInt64(1) < 0)
                    {
                        throw InvalidPrivateMaterial();
                    }

                    if (!reader.IsDBNull(8)
                        && (reader.GetInt64(0) != reader.GetInt64(8)
                            || root.KeyId != reader.GetString(9)
                            || !CryptographicOperations.FixedTimeEquals(
                                root.SubjectPublicKeyInfo,
                                (byte[])reader.GetValue(10))
                            || anchor.KeyId != reader.GetString(11)
                            || !CryptographicOperations.FixedTimeEquals(
                                anchor.SubjectPublicKeyInfo,
                                (byte[])reader.GetValue(12))))
                    {
                        throw InvalidPrivateMaterial();
                    }
                }
                catch (LocalStateException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is
                    CryptographicException or ArgumentException)
                {
                    throw InvalidPrivateMaterial();
                }
            }
        }

        var credentials = new List<(IdentityKeyRole Role, string Algorithm, string KeyId, byte[] Spki,
            string Scheme, byte[] Protected)>();
        using (var node = connection.CreateCommand())
        {
            node.CommandText =
                """
                SELECT key_algorithm, key_id, public_key_spki,
                       private_key_scheme, protected_private_key
                FROM local_node_credentials;
                """;
            await using var reader = await node.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                credentials.Add((
                    IdentityKeyRole.Node,
                    reader.GetString(0),
                    reader.GetString(1),
                    (byte[])reader.GetValue(2),
                    reader.GetString(3),
                    (byte[])reader.GetValue(4)));
            }
        }

        using (var circles = connection.CreateCommand())
        {
            circles.CommandText =
                """
                SELECT root_key_id, root_public_key_spki, root_protected_private_key,
                       anchor_key_id, anchor_public_key_spki, anchor_protected_private_key,
                       private_key_scheme
                FROM circle_authorities;
                """;
            await using var reader = await circles.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                credentials.Add((
                    IdentityKeyRole.CircleAuthority,
                    IdentityCryptography.Algorithm,
                    reader.GetString(0),
                    (byte[])reader.GetValue(1),
                    reader.GetString(6),
                    (byte[])reader.GetValue(2)));
                credentials.Add((
                    IdentityKeyRole.Anchor,
                    IdentityCryptography.Algorithm,
                    reader.GetString(3),
                    (byte[])reader.GetValue(4),
                    reader.GetString(6),
                    (byte[])reader.GetValue(5)));
            }
        }


        using (var transport = connection.CreateCommand())
        {
            transport.CommandText =
                """
                SELECT key_algorithm, key_id, public_key_spki,
                       private_key_scheme, protected_private_key
                FROM local_transport_credentials;
                """;
            await using var reader = await transport.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                credentials.Add((
                    IdentityKeyRole.Transport,
                    reader.GetString(0),
                    reader.GetString(1),
                    (byte[])reader.GetValue(2),
                    reader.GetString(3),
                    (byte[])reader.GetValue(4)));
            }
        }

        using (var admissionMembers = connection.CreateCommand())
        {
            admissionMembers.CommandText =
                """
                SELECT member_key_algorithm, member_key_id, member_public_key_spki,
                       member_private_key_scheme, member_protected_private_key
                FROM admission_attempts;
                """;
            await using var reader = await admissionMembers.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                credentials.Add((
                    IdentityKeyRole.Member,
                    reader.GetString(0),
                    reader.GetString(1),
                    (byte[])reader.GetValue(2),
                    reader.GetString(3),
                    (byte[])reader.GetValue(4)));
            }
        }

        using (var localCircleMembers = connection.CreateCommand())
        {
            localCircleMembers.CommandText =
                """
                SELECT key_algorithm, key_id, public_key_spki,
                       private_key_scheme, protected_private_key
                FROM local_circle_members;
                """;
            await using var reader = await localCircleMembers.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                credentials.Add((
                    IdentityKeyRole.Member,
                    reader.GetString(0),
                    reader.GetString(1),
                    (byte[])reader.GetValue(2),
                    reader.GetString(3),
                    (byte[])reader.GetValue(4)));
            }
        }

        try
        {
            foreach (var item in credentials)
            {
                byte[]? privateKey = null;
                try
                {
                    if (!string.Equals(item.Scheme, protector.Scheme, StringComparison.Ordinal))
                    {
                        throw InvalidPrivateMaterial();
                    }

                    var credential = ReadCredential(item.Role, item.Algorithm, item.KeyId, item.Spki);
                    privateKey = protector.Unprotect(item.Protected);
                    using var key = ECDsa.Create();
                    key.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
                    var actual = IdentityCryptography.CreateCredential(item.Role, key);
                    if (bytesRead != privateKey.Length
                        || actual.KeyId != credential.KeyId
                        || !CryptographicOperations.FixedTimeEquals(
                            actual.SubjectPublicKeyInfo,
                            credential.SubjectPublicKeyInfo))
                    {
                        throw InvalidPrivateMaterial();
                    }
                }
                catch (LocalStateException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is
                    CryptographicException or ArgumentException)
                {
                    throw InvalidPrivateMaterial();
                }
                finally
                {
                    if (privateKey is not null)
                    {
                        CryptographicOperations.ZeroMemory(privateKey);
                    }
                }
            }
        }
        finally
        {
            foreach (var item in credentials)
            {
                CryptographicOperations.ZeroMemory(item.Protected);
            }
        }
    }

    private static LocalStateException InvalidPrivateMaterial() => new(
        "invalid_private_material",
        "Protected cryptographic identity is unreadable or invalid; state was left unchanged.");

    private sealed record GeneratedPrivateIdentity(
        PublicIdentityCredential Credential,
        string ProtectionScheme,
        byte[] ProtectedPrivateKey);

    private sealed record StoredPrivateIdentity(
        PublicIdentityCredential Credential,
        string ProtectionScheme,
        byte[] ProtectedPrivateKey);

    private sealed record StoredCircleAuthority(
        CircleAuthorityIdentity Identity,
        StoredPrivateIdentity Root,
        StoredPrivateIdentity Anchor,
        DateTimeOffset CreatedAtUtc);

}
