using System.Security.Cryptography;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    private const int MaximumSecurityAuditEvents = 512;
    private const string AdmissionSchemaSql =
        """
        CREATE TABLE circle_trust (
            circle_id TEXT NOT NULL PRIMARY KEY,
            authority_generation INTEGER NOT NULL,
            authority_sequence INTEGER NOT NULL,
            issuer_node_id TEXT NOT NULL,
            root_key_algorithm TEXT NOT NULL,
            root_key_id TEXT NOT NULL,
            root_public_key_spki BLOB NOT NULL,
            anchor_key_algorithm TEXT NOT NULL,
            anchor_key_id TEXT NOT NULL,
            anchor_public_key_spki BLOB NOT NULL,
            signed_admission_receipt BLOB NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (issuer_node_id) REFERENCES nodes(node_id)
        );

        CREATE TABLE circle_member_credentials (
            circle_id TEXT NOT NULL,
            member_id TEXT NOT NULL,
            key_algorithm TEXT NOT NULL,
            key_id TEXT NOT NULL,
            public_key_spki BLOB NOT NULL,
            PRIMARY KEY (circle_id, member_id),
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(member_id) ON DELETE CASCADE
        );

        CREATE TABLE circle_node_credentials (
            circle_id TEXT NOT NULL,
            node_id TEXT NOT NULL,
            node_key_algorithm TEXT NOT NULL,
            node_key_id TEXT NOT NULL,
            node_public_key_spki BLOB NOT NULL,
            transport_key_algorithm TEXT NOT NULL,
            transport_key_id TEXT NOT NULL,
            transport_public_key_spki BLOB NOT NULL,
            signed_transport_binding BLOB NOT NULL,
            PRIMARY KEY (circle_id, node_id),
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (node_id) REFERENCES nodes(node_id)
        );

        CREATE TABLE admission_attempts (
            invitation_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            package_sha256 BLOB NOT NULL,
            member_id TEXT NOT NULL,
            member_display_name TEXT NOT NULL,
            member_key_algorithm TEXT NOT NULL,
            member_key_id TEXT NOT NULL,
            member_public_key_spki BLOB NOT NULL,
            member_private_key_scheme TEXT NOT NULL,
            member_protected_private_key BLOB NOT NULL,
            applicant_challenge BLOB NOT NULL,
            status INTEGER NOT NULL,
            encoded_response BLOB NOT NULL,
            created_at_utc TEXT NOT NULL
        );

        CREATE TABLE admission_challenges (
            invitation_id TEXT NOT NULL PRIMARY KEY,
            anchor_challenge BLOB NOT NULL,
            FOREIGN KEY (invitation_id) REFERENCES circle_invitations(invitation_id) ON DELETE CASCADE
        );

        CREATE TABLE circle_admissions (
            invitation_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            request_sha256 BLOB NOT NULL,
            encoded_response BLOB NOT NULL,
            member_id TEXT NOT NULL,
            node_id TEXT NOT NULL,
            authority_sequence INTEGER NOT NULL,
            admitted_at_utc TEXT NOT NULL,
            FOREIGN KEY (invitation_id) REFERENCES circle_invitations(invitation_id) ON DELETE CASCADE,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE,
            FOREIGN KEY (member_id) REFERENCES members(member_id),
            FOREIGN KEY (node_id) REFERENCES nodes(node_id)
        );

        CREATE TABLE security_audit_events (
            event_id TEXT NOT NULL PRIMARY KEY,
            circle_id TEXT NOT NULL,
            code TEXT NOT NULL,
            outcome TEXT NOT NULL,
            occurred_at_utc TEXT NOT NULL,
            FOREIGN KEY (circle_id) REFERENCES circles(circle_id) ON DELETE CASCADE
        );
        """;

    public Task<AdmissionApplicantState> PrepareAdmissionApplicantAsync(
        InvitationId invitationId,
        CircleId circleId,
        ReadOnlyMemory<byte> packageSha256,
        string memberDisplayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (packageSha256.Length != SHA256.HashSizeInBytes
            || string.IsNullOrWhiteSpace(memberDisplayName)
            || memberDisplayName.Length > 100
            || createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The admission applicant request is invalid.");
        }

        return ExecuteLockedAsync<AdmissionApplicantState>(
            async token =>
            {
                var existing = await ReadAdmissionApplicantAsync(invitationId, token)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.CircleId != circleId
                        || existing.MemberDisplayName != memberDisplayName
                        || !CryptographicOperations.FixedTimeEquals(
                            existing.PackageSha256,
                            packageSha256.Span))
                    {
                        throw new LocalStateConflictException(
                            "admission_attempt_conflict",
                            "This invitation already has a different local admission attempt.");
                    }

                    return existing;
                }

                var material = GeneratePrivateIdentity(
                    IdentityKeyRole.Member,
                    privateMaterialProtector);
                try
                {
                    var state = new AdmissionApplicantState(
                        invitationId,
                        circleId,
                        MemberId.New(),
                        memberDisplayName,
                        material.Credential,
                        RandomNumberGenerator.GetBytes(32),
                        packageSha256.ToArray(),
                        false,
                        []);
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        INSERT INTO admission_attempts (
                            invitation_id, circle_id, package_sha256, member_id,
                            member_display_name, member_key_algorithm, member_key_id,
                            member_public_key_spki, member_private_key_scheme,
                            member_protected_private_key, applicant_challenge,
                            status, encoded_response, created_at_utc)
                        VALUES (
                            $invitation_id, $circle_id, $digest, $member_id,
                            $display_name, $algorithm, $key_id,
                            $spki, $scheme, $private_key, $challenge,
                            0, X'', $created_at_utc);
                        """;
                    command.Parameters.AddWithValue("$invitation_id", invitationId.ToString());
                    command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                    command.Parameters.AddWithValue("$digest", state.PackageSha256);
                    command.Parameters.AddWithValue("$member_id", state.MemberId.ToString());
                    command.Parameters.AddWithValue("$display_name", memberDisplayName);
                    command.Parameters.AddWithValue("$algorithm", material.Credential.Algorithm);
                    command.Parameters.AddWithValue("$key_id", material.Credential.KeyId);
                    command.Parameters.AddWithValue("$spki", material.Credential.SubjectPublicKeyInfo);
                    command.Parameters.AddWithValue("$scheme", material.ProtectionScheme);
                    command.Parameters.AddWithValue("$private_key", material.ProtectedPrivateKey);
                    command.Parameters.AddWithValue("$challenge", state.ApplicantChallenge);
                    command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return state;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(material.ProtectedPrivateKey);
                }
            },
            cancellationToken);
    }

    public Task<byte[]> SignWithAdmissionMemberAsync(
        InvitationId invitationId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                var stored = await ReadAdmissionMemberPrivateIdentityAsync(invitationId, token)
                    .ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "admission_attempt_not_found",
                        "The local admission attempt is not known.");
                using var key = OpenPrivateKey(stored);
                return IdentityCryptography.Sign(data.Span, key);
            },
            cancellationToken);

    public Task<byte[]> GetOrCreateAdmissionChallengeAsync(
        InvitationId invitationId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using (var read = connection.CreateCommand())
                {
                    read.CommandText =
                        "SELECT anchor_challenge FROM admission_challenges WHERE invitation_id = $id;";
                    read.Parameters.AddWithValue("$id", invitationId.ToString());
                    var value = await read.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (value is byte[] existing)
                    {
                        return existing;
                    }
                }

                var challenge = RandomNumberGenerator.GetBytes(32);
                using var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO admission_challenges (invitation_id, anchor_challenge) VALUES ($id, $challenge);";
                insert.Parameters.AddWithValue("$id", invitationId.ToString());
                insert.Parameters.AddWithValue("$challenge", challenge);
                try
                {
                    await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return challenge;
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new LocalStateException(
                        "invitation_not_found",
                        "The requested Circle invitation is not known to this Node.");
                }
            },
            cancellationToken);

    public Task<CircleTrustState?> GetCircleTrustAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(token => ReadCircleTrustAsync(circleId, transaction: null, token), cancellationToken);

    public Task StoreCircleNodeSecurityAsync(
        CircleNodeSecurityState state,
        CancellationToken cancellationToken = default)
    {
        ValidateNodeSecurity(state);
        return ExecuteLockedAsync(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO circle_node_credentials (
                        circle_id, node_id, node_key_algorithm, node_key_id, node_public_key_spki,
                        transport_key_algorithm, transport_key_id, transport_public_key_spki,
                        signed_transport_binding)
                    VALUES ($circle_id, $node_id, $node_algorithm, $node_key_id, $node_spki,
                            $transport_algorithm, $transport_key_id, $transport_spki, $binding)
                    ON CONFLICT(circle_id, node_id) DO NOTHING;
                    """;
                AddNodeSecurityParameters(command, state);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                var persisted = await ReadCircleNodeSecurityAsync(
                    state.CircleId,
                    state.NodeId,
                    transaction: null,
                    token).ConfigureAwait(false);
                if (!NodeSecurityEquals(state, persisted))
                {
                    throw new LocalStateConflictException(
                        "node_credential_conflict",
                        "This Circle already records different credentials for the Node.");
                }
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<CircleNodeSecurityState>> ListCircleNodeSecurityAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<IReadOnlyList<CircleNodeSecurityState>>(
            token => ReadCircleNodeSecurityListAsync(circleId, transaction: null, token),
            cancellationToken);

    public Task<long> ReserveAuthoritySequenceAsync(
        CircleId circleId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    UPDATE circle_trust
                    SET authority_sequence = authority_sequence + 1
                    WHERE circle_id = $circle_id
                    RETURNING authority_sequence;
                    """;
                command.Parameters.AddWithValue("$circle_id", circleId.ToString());
                var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                return value is long sequence
                    ? sequence
                    : throw new LocalStateException(
                        "circle_trust_not_found",
                        "The requested Circle trust state is not known to this Node.");
            },
            cancellationToken);

    public Task<AnchorAdmissionCommitResult?> GetAnchorAdmissionResultAsync(
        InvitationId invitationId,
        ReadOnlyMemory<byte> requestSha256,
        CancellationToken cancellationToken = default)
    {
        if (requestSha256.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("The admission request digest is invalid.");
        }

        return ExecuteLockedAsync<AnchorAdmissionCommitResult?>(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadAnchorAdmissionAsync(
                    invitationId,
                    transaction,
                    token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                if (existing is null)
                {
                    return null;
                }

                return CryptographicOperations.FixedTimeEquals(
                        existing.Value.RequestSha256,
                        requestSha256.Span)
                    ? new AnchorAdmissionCommitResult(
                        AnchorAdmissionCommitStatus.IdempotentRetry,
                        existing.Value.EncodedResponse)
                    : new AnchorAdmissionCommitResult(AnchorAdmissionCommitStatus.Replayed, null);
            },
            cancellationToken);
    }

    public Task<AnchorAdmissionCommitResult> CommitAnchorAdmissionAsync(
        AnchorAdmissionCommit commit,
        CancellationToken cancellationToken = default)
    {
        ValidateAnchorCommit(commit);
        return ExecuteLockedAsync<AnchorAdmissionCommitResult>(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var existing = await ReadAnchorAdmissionAsync(
                    commit.InvitationId,
                    transaction,
                    token).ConfigureAwait(false);
                if (existing is not null)
                {
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return CryptographicOperations.FixedTimeEquals(
                            existing.Value.RequestSha256,
                            commit.RequestSha256)
                        ? new(
                            AnchorAdmissionCommitStatus.IdempotentRetry,
                            existing.Value.EncodedResponse)
                        : new(AnchorAdmissionCommitStatus.Replayed, null);
                }

                var invitation = await ReadCircleInvitationAsync(
                    commit.InvitationId,
                    token,
                    transaction).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "invitation_not_found",
                        "The requested Circle invitation is not known to this Node.");
                if (invitation.CircleId != commit.CircleId
                    || !CryptographicOperations.FixedTimeEquals(
                        invitation.PackageSha256,
                        commit.PackageSha256))
                {
                    throw new LocalStateConflictException(
                        "invitation_mismatch",
                        "The admission does not match the issued invitation.");
                }

                if (commit.AdmittedAtUtc >= invitation.ExpiresAtUtc)
                {
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return new(AnchorAdmissionCommitStatus.Expired, null);
                }

                if (await HasInvitationRowAsync(
                        "revoked_invitations",
                        commit.InvitationId,
                        transaction,
                        token).ConfigureAwait(false))
                {
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return new(AnchorAdmissionCommitStatus.Revoked, null);
                }

                if (await HasInvitationRowAsync(
                        "invitation_redemptions",
                        commit.InvitationId,
                        transaction,
                        token).ConfigureAwait(false))
                {
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return new(AnchorAdmissionCommitStatus.Replayed, null);
                }

                try
                {
                    await InsertMemberAsync(commit.Member, transaction, token).ConfigureAwait(false);
                    using (var node = connection.CreateCommand())
                    {
                        node.Transaction = transaction;
                        node.CommandText = "INSERT INTO nodes (node_id) VALUES ($node_id);";
                        node.Parameters.AddWithValue("$node_id", commit.Node.NodeId.ToString());
                        await node.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await InsertCircleNodeAsync(commit.Node, transaction, token).ConfigureAwait(false);
                    await InsertMemberCredentialAsync(
                        commit.CircleId,
                        commit.Member.Id,
                        commit.MemberCredential,
                        transaction,
                        token).ConfigureAwait(false);
                    await InsertNodeSecurityAsync(
                        new CircleNodeSecurityState(
                            commit.CircleId,
                            commit.Node.NodeId,
                            commit.NodeCredential,
                            commit.TransportCredential,
                            commit.SignedTransportBinding),
                        transaction,
                        token).ConfigureAwait(false);
                    await InsertMemberNodeAuthorizationAsync(
                        commit.CircleId,
                        commit.Member.Id,
                        commit.Node.NodeId,
                        transaction,
                        token).ConfigureAwait(false);
                    using (var redemption = connection.CreateCommand())
                    {
                        redemption.Transaction = transaction;
                        redemption.CommandText =
                            """
                            INSERT INTO invitation_redemptions (
                                invitation_id, redemption_id, redeemed_at_utc)
                            VALUES ($invitation_id, $redemption_id, $at);
                            """;
                        redemption.Parameters.AddWithValue(
                            "$invitation_id",
                            commit.InvitationId.ToString());
                        redemption.Parameters.AddWithValue("$redemption_id", RedemptionId.New().ToString());
                        redemption.Parameters.AddWithValue("$at", Format(commit.AdmittedAtUtc));
                        await redemption.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    using (var admission = connection.CreateCommand())
                    {
                        admission.Transaction = transaction;
                        admission.CommandText =
                            """
                            INSERT INTO circle_admissions (
                                invitation_id, circle_id, request_sha256, encoded_response,
                                member_id, node_id, authority_sequence, admitted_at_utc)
                            VALUES ($invitation_id, $circle_id, $request, $response,
                                    $member_id, $node_id, $sequence, $at);
                            """;
                        admission.Parameters.AddWithValue("$invitation_id", commit.InvitationId.ToString());
                        admission.Parameters.AddWithValue("$circle_id", commit.CircleId.ToString());
                        admission.Parameters.AddWithValue("$request", commit.RequestSha256);
                        admission.Parameters.AddWithValue("$response", commit.EncodedResponse);
                        admission.Parameters.AddWithValue("$member_id", commit.Member.Id.ToString());
                        admission.Parameters.AddWithValue("$node_id", commit.Node.NodeId.ToString());
                        admission.Parameters.AddWithValue("$sequence", commit.AuthoritySequence);
                        admission.Parameters.AddWithValue("$at", Format(commit.AdmittedAtUtc));
                        await admission.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await InsertAuditEventAsync(
                        commit.CircleId,
                        "admission",
                        "accepted",
                        commit.AdmittedAtUtc,
                        transaction,
                        token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return new(AnchorAdmissionCommitStatus.Accepted, commit.EncodedResponse);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return new(AnchorAdmissionCommitStatus.Replayed, null);
                }
            },
            cancellationToken);
    }

    public Task CommitJoinedCircleAsync(
        JoinedCircleCommit commit,
        CancellationToken cancellationToken = default)
    {
        ValidateJoinedCommit(commit);
        return ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var applicant = await ReadAdmissionApplicantAsync(
                    commit.InvitationId,
                    token,
                    transaction).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "admission_attempt_not_found",
                        "The local admission attempt is not known.");
                if (applicant.CircleId != commit.Circle.Circle.Id
                    || !CryptographicOperations.FixedTimeEquals(
                        applicant.PackageSha256,
                        commit.PackageSha256)
                    || applicant.MemberCredential.KeyId != commit.LocalMemberCredential.KeyId)
                {
                    throw new LocalStateConflictException(
                        "admission_attempt_conflict",
                        "The accepted admission differs from the prepared local attempt.");
                }

                if (applicant.IsCompleted)
                {
                    if (!CryptographicOperations.FixedTimeEquals(
                        applicant.EncodedResponse,
                        commit.Trust.SignedAdmissionReceipt))
                    {
                        throw new LocalStateConflictException(
                            "admission_attempt_conflict",
                            "The completed local admission has a different signed response.");
                    }

                    await transaction.CommitAsync(token).ConfigureAwait(false);
                    return;
                }

                await InsertCircleAsync(commit.Circle.Circle, transaction, token).ConfigureAwait(false);
                foreach (var member in commit.Circle.Members)
                {
                    await InsertMemberAsync(member, transaction, token).ConfigureAwait(false);
                }

                foreach (var node in commit.Circle.Nodes)
                {
                    using (var catalog = connection.CreateCommand())
                    {
                        catalog.Transaction = transaction;
                        catalog.CommandText =
                            "INSERT INTO nodes (node_id) VALUES ($id) ON CONFLICT(node_id) DO NOTHING;";
                        catalog.Parameters.AddWithValue("$id", node.NodeId.ToString());
                        await catalog.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await InsertCircleNodeAsync(node, transaction, token).ConfigureAwait(false);
                }

                await InsertCircleTrustAsync(commit.Trust, transaction, token).ConfigureAwait(false);
                var localMember = commit.Circle.Members.Single(member =>
                    member.Id == applicant.MemberId);
                await InsertMemberCredentialAsync(
                    commit.Circle.Circle.Id,
                    localMember.Id,
                    commit.LocalMemberCredential,
                    transaction,
                    token).ConfigureAwait(false);
                var localIdentity = await ReadNodeAsync(transaction, token).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "node_identity_missing",
                        "Local Node identity is missing.");
                var localNode = commit.Circle.Nodes.Single(node => node.NodeId == localIdentity.Id);
                await InsertLocalCircleMemberFromAdmissionAsync(
                    commit.Circle.Circle.Id,
                    localMember.Id,
                    commit.InvitationId,
                    transaction,
                    token).ConfigureAwait(false);
                await InsertMemberNodeAuthorizationAsync(
                    commit.Circle.Circle.Id,
                    localMember.Id,
                    localNode.NodeId,
                    transaction,
                    token).ConfigureAwait(false);
                foreach (var nodeSecurity in commit.NodeSecurity)
                {
                    await InsertNodeSecurityAsync(nodeSecurity, transaction, token).ConfigureAwait(false);
                }

                using (var complete = connection.CreateCommand())
                {
                    complete.Transaction = transaction;
                    complete.CommandText =
                        """
                        UPDATE admission_attempts
                        SET status = 1, encoded_response = $response
                        WHERE invitation_id = $invitation_id AND status = 0;
                        """;
                    complete.Parameters.AddWithValue("$response", commit.Trust.SignedAdmissionReceipt);
                    complete.Parameters.AddWithValue("$invitation_id", commit.InvitationId.ToString());
                    if (await complete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    {
                        throw new LocalStateConflictException(
                            "admission_attempt_conflict",
                            "The local admission attempt changed before commit.");
                    }
                }

                await InsertAuditEventAsync(
                    commit.Circle.Circle.Id,
                    "admission",
                    "joined",
                    commit.JoinedAtUtc,
                    transaction,
                    token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task RecordAdmissionAuditAsync(
        CircleId circleId,
        string outcome,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outcome)
            || outcome.Length > 64
            || outcome.Any(character => character > 0x7f)
            || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The admission audit event is invalid.");
        }

        return ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                await InsertAuditEventAsync(
                    circleId,
                    "admission",
                    outcome,
                    occurredAtUtc,
                    transaction,
                    token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private async Task InsertCreatorCircleTrustAsync(
        CircleId circleId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var authority = await ReadCirclePrivateAuthorityAsync(circleId, transaction, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException("circle_authority_not_found", "Circle authority is missing.");
        var node = await ReadNodeAsync(transaction, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalStateException("node_identity_missing", "Local Node identity is missing.");
        await InsertCircleTrustAsync(
            new CircleTrustState(
                circleId,
                authority.Identity.AuthorityGeneration,
                0,
                node.Id,
                authority.Identity.RootCredential,
                authority.Identity.AnchorCredential,
                []),
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task MigrateV3ToV4Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = AdmissionSchemaSql;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using (var trust = connection.CreateCommand())
        {
            trust.Transaction = transaction;
            trust.CommandText =
                """
                INSERT INTO circle_trust (
                    circle_id, authority_generation, authority_sequence, issuer_node_id,
                    root_key_algorithm, root_key_id, root_public_key_spki,
                    anchor_key_algorithm, anchor_key_id, anchor_public_key_spki,
                    signed_admission_receipt)
                SELECT a.circle_id, a.authority_generation, 0, n.node_id,
                       'p256-sha256', a.root_key_id, a.root_public_key_spki,
                       'p256-sha256', a.anchor_key_id, a.anchor_public_key_spki, X''
                FROM circle_authorities a
                CROSS JOIN local_node n;
                """;
            await trust.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using var version = connection.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = "PRAGMA user_version = 4;";
        await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddAdmissionExpectedTables(
        IDictionary<string, TableSchema> tables)
    {
        tables["circle_trust"] = new(
            [
                new("circle_id", "TEXT", 1), new("authority_generation", "INTEGER", 0),
                new("authority_sequence", "INTEGER", 0), new("issuer_node_id", "TEXT", 0),
                new("root_key_algorithm", "TEXT", 0), new("root_key_id", "TEXT", 0),
                new("root_public_key_spki", "BLOB", 0),
                new("anchor_key_algorithm", "TEXT", 0), new("anchor_key_id", "TEXT", 0),
                new("anchor_public_key_spki", "BLOB", 0),
                new("signed_admission_receipt", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("nodes", "issuer_node_id", "node_id", "NO ACTION"),
            ]);
        tables["circle_member_credentials"] = new(
            [
                new("circle_id", "TEXT", 1), new("member_id", "TEXT", 2),
                new("key_algorithm", "TEXT", 0), new("key_id", "TEXT", 0),
                new("public_key_spki", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "member_id", "member_id", "CASCADE"),
            ]);
        tables["circle_node_credentials"] = new(
            [
                new("circle_id", "TEXT", 1), new("node_id", "TEXT", 2),
                new("node_key_algorithm", "TEXT", 0), new("node_key_id", "TEXT", 0),
                new("node_public_key_spki", "BLOB", 0),
                new("transport_key_algorithm", "TEXT", 0), new("transport_key_id", "TEXT", 0),
                new("transport_public_key_spki", "BLOB", 0),
                new("signed_transport_binding", "BLOB", 0),
            ],
            [
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("nodes", "node_id", "node_id", "NO ACTION"),
            ]);
        tables["admission_attempts"] = new(
            [
                new("invitation_id", "TEXT", 1), new("circle_id", "TEXT", 0),
                new("package_sha256", "BLOB", 0), new("member_id", "TEXT", 0),
                new("member_display_name", "TEXT", 0),
                new("member_key_algorithm", "TEXT", 0), new("member_key_id", "TEXT", 0),
                new("member_public_key_spki", "BLOB", 0),
                new("member_private_key_scheme", "TEXT", 0),
                new("member_protected_private_key", "BLOB", 0),
                new("applicant_challenge", "BLOB", 0), new("status", "INTEGER", 0),
                new("encoded_response", "BLOB", 0), new("created_at_utc", "TEXT", 0),
            ], []);
        tables["admission_challenges"] = new(
            [new("invitation_id", "TEXT", 1), new("anchor_challenge", "BLOB", 0)],
            [new("circle_invitations", "invitation_id", "invitation_id", "CASCADE")]);
        tables["circle_admissions"] = new(
            [
                new("invitation_id", "TEXT", 1), new("circle_id", "TEXT", 0),
                new("request_sha256", "BLOB", 0), new("encoded_response", "BLOB", 0),
                new("member_id", "TEXT", 0), new("node_id", "TEXT", 0),
                new("authority_sequence", "INTEGER", 0), new("admitted_at_utc", "TEXT", 0),
            ],
            [
                new("circle_invitations", "invitation_id", "invitation_id", "CASCADE"),
                new("circles", "circle_id", "circle_id", "CASCADE"),
                new("members", "member_id", "member_id", "NO ACTION"),
                new("nodes", "node_id", "node_id", "NO ACTION"),
            ]);
        tables["security_audit_events"] = new(
            [
                new("event_id", "TEXT", 1), new("circle_id", "TEXT", 0),
                new("code", "TEXT", 0), new("outcome", "TEXT", 0),
                new("occurred_at_utc", "TEXT", 0),
            ],
            [new("circles", "circle_id", "circle_id", "CASCADE")]);
    }

    private async Task<AdmissionApplicantState?> ReadAdmissionApplicantAsync(
        InvitationId invitationId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT circle_id, package_sha256, member_id, member_display_name,
                   member_key_algorithm, member_key_id, member_public_key_spki,
                   applicant_challenge, status, encoded_response
            FROM admission_attempts WHERE invitation_id = $id;
            """;
        command.Parameters.AddWithValue("$id", invitationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new AdmissionApplicantState(
            invitationId,
            new CircleId(Guid.Parse(reader.GetString(0))),
            new MemberId(Guid.Parse(reader.GetString(2))),
            reader.GetString(3),
            ReadCredential(
                IdentityKeyRole.Member,
                reader.GetString(4),
                reader.GetString(5),
                (byte[])reader.GetValue(6)),
            (byte[])reader.GetValue(7),
            (byte[])reader.GetValue(1),
            reader.GetInt32(8) == 1,
            (byte[])reader.GetValue(9));
    }

    private async Task<StoredPrivateIdentity?> ReadAdmissionMemberPrivateIdentityAsync(
        InvitationId invitationId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT member_key_algorithm, member_key_id, member_public_key_spki,
                   member_private_key_scheme, member_protected_private_key
            FROM admission_attempts WHERE invitation_id = $id;
            """;
        command.Parameters.AddWithValue("$id", invitationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredPrivateIdentity(
            ReadCredential(
                IdentityKeyRole.Member,
                reader.GetString(0),
                reader.GetString(1),
                (byte[])reader.GetValue(2)),
            reader.GetString(3),
            (byte[])reader.GetValue(4));
    }

    private async Task<CircleTrustState?> ReadCircleTrustAsync(
        CircleId circleId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT authority_generation, authority_sequence, issuer_node_id,
                   root_key_algorithm, root_key_id, root_public_key_spki,
                   anchor_key_algorithm, anchor_key_id, anchor_public_key_spki,
                   signed_admission_receipt
            FROM circle_trust WHERE circle_id = $id;
            """;
        command.Parameters.AddWithValue("$id", circleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new CircleTrustState(
            circleId,
            reader.GetInt64(0),
            reader.GetInt64(1),
            new NodeId(Guid.Parse(reader.GetString(2))),
            ReadCredential(
                IdentityKeyRole.CircleAuthority,
                reader.GetString(3),
                reader.GetString(4),
                (byte[])reader.GetValue(5)),
            ReadCredential(
                IdentityKeyRole.Anchor,
                reader.GetString(6),
                reader.GetString(7),
                (byte[])reader.GetValue(8)),
            (byte[])reader.GetValue(9));
    }

    private async Task InsertCircleTrustAsync(
        CircleTrustState trust,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO circle_trust (
                circle_id, authority_generation, authority_sequence, issuer_node_id,
                root_key_algorithm, root_key_id, root_public_key_spki,
                anchor_key_algorithm, anchor_key_id, anchor_public_key_spki,
                signed_admission_receipt)
            VALUES ($circle_id, $generation, $sequence, $issuer,
                    $root_algorithm, $root_id, $root_spki,
                    $anchor_algorithm, $anchor_id, $anchor_spki, $receipt);
            """;
        command.Parameters.AddWithValue("$circle_id", trust.CircleId.ToString());
        command.Parameters.AddWithValue("$generation", trust.AuthorityGeneration);
        command.Parameters.AddWithValue("$sequence", trust.AuthoritySequence);
        command.Parameters.AddWithValue("$issuer", trust.IssuerNodeId.ToString());
        command.Parameters.AddWithValue("$root_algorithm", trust.RootCredential.Algorithm);
        command.Parameters.AddWithValue("$root_id", trust.RootCredential.KeyId);
        command.Parameters.AddWithValue("$root_spki", trust.RootCredential.SubjectPublicKeyInfo);
        command.Parameters.AddWithValue("$anchor_algorithm", trust.AnchorCredential.Algorithm);
        command.Parameters.AddWithValue("$anchor_id", trust.AnchorCredential.KeyId);
        command.Parameters.AddWithValue("$anchor_spki", trust.AnchorCredential.SubjectPublicKeyInfo);
        command.Parameters.AddWithValue("$receipt", trust.SignedAdmissionReceipt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertMemberCredentialAsync(
        CircleId circleId,
        MemberId memberId,
        PublicIdentityCredential credential,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO circle_member_credentials (
                circle_id, member_id, key_algorithm, key_id, public_key_spki)
            VALUES ($circle_id, $member_id, $algorithm, $key_id, $spki);
            """;
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        command.Parameters.AddWithValue("$member_id", memberId.ToString());
        command.Parameters.AddWithValue("$algorithm", credential.Algorithm);
        command.Parameters.AddWithValue("$key_id", credential.KeyId);
        command.Parameters.AddWithValue("$spki", credential.SubjectPublicKeyInfo);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertNodeSecurityAsync(
        CircleNodeSecurityState state,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO circle_node_credentials (
                circle_id, node_id, node_key_algorithm, node_key_id, node_public_key_spki,
                transport_key_algorithm, transport_key_id, transport_public_key_spki,
                signed_transport_binding)
            VALUES ($circle_id, $node_id, $node_algorithm, $node_key_id, $node_spki,
                    $transport_algorithm, $transport_key_id, $transport_spki, $binding);
            """;
        AddNodeSecurityParameters(command, state);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddNodeSecurityParameters(
        SqliteCommand command,
        CircleNodeSecurityState state)
    {
        command.Parameters.AddWithValue("$circle_id", state.CircleId.ToString());
        command.Parameters.AddWithValue("$node_id", state.NodeId.ToString());
        command.Parameters.AddWithValue("$node_algorithm", state.NodeCredential.Algorithm);
        command.Parameters.AddWithValue("$node_key_id", state.NodeCredential.KeyId);
        command.Parameters.AddWithValue("$node_spki", state.NodeCredential.SubjectPublicKeyInfo);
        command.Parameters.AddWithValue("$transport_algorithm", state.TransportCredential.Algorithm);
        command.Parameters.AddWithValue("$transport_key_id", state.TransportCredential.KeyId);
        command.Parameters.AddWithValue("$transport_spki", state.TransportCredential.SubjectPublicKeyInfo);
        command.Parameters.AddWithValue("$binding", state.SignedTransportBinding);
    }

    private async Task<CircleNodeSecurityState?> ReadCircleNodeSecurityAsync(
        CircleId circleId,
        NodeId nodeId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var values = await ReadCircleNodeSecurityListAsync(circleId, transaction, cancellationToken)
            .ConfigureAwait(false);
        return values.SingleOrDefault(value => value.NodeId == nodeId);
    }

    private async Task<IReadOnlyList<CircleNodeSecurityState>> ReadCircleNodeSecurityListAsync(
        CircleId circleId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var values = new List<CircleNodeSecurityState>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT node_id, node_key_algorithm, node_key_id, node_public_key_spki,
                   transport_key_algorithm, transport_key_id, transport_public_key_spki,
                   signed_transport_binding
            FROM circle_node_credentials WHERE circle_id = $id ORDER BY node_id;
            """;
        command.Parameters.AddWithValue("$id", circleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new CircleNodeSecurityState(
                circleId,
                new NodeId(Guid.Parse(reader.GetString(0))),
                ReadCredential(
                    IdentityKeyRole.Node,
                    reader.GetString(1),
                    reader.GetString(2),
                    (byte[])reader.GetValue(3)),
                ReadCredential(
                    IdentityKeyRole.Transport,
                    reader.GetString(4),
                    reader.GetString(5),
                    (byte[])reader.GetValue(6)),
                (byte[])reader.GetValue(7)));
        }

        return values;
    }

    private async Task<(byte[] RequestSha256, byte[] EncodedResponse)?> ReadAnchorAdmissionAsync(
        InvitationId invitationId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT request_sha256, encoded_response FROM circle_admissions WHERE invitation_id = $id;";
        command.Parameters.AddWithValue("$id", invitationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ((byte[])reader.GetValue(0), (byte[])reader.GetValue(1))
            : null;
    }

    private async Task InsertAuditEventAsync(
        CircleId circleId,
        string code,
        string outcome,
        DateTimeOffset occurredAtUtc,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO security_audit_events (
                event_id, circle_id, code, outcome, occurred_at_utc)
            VALUES ($event_id, $circle_id, $code, $outcome, $at);
            DELETE FROM security_audit_events
            WHERE event_id IN (
                SELECT event_id FROM security_audit_events
                WHERE circle_id = $circle_id
                ORDER BY occurred_at_utc DESC, event_id DESC
                LIMIT -1 OFFSET $maximum);
            """;
        command.Parameters.AddWithValue("$event_id", Guid.CreateVersion7().ToString("D"));
        command.Parameters.AddWithValue("$circle_id", circleId.ToString());
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$at", Format(occurredAtUtc));
        command.Parameters.AddWithValue("$maximum", MaximumSecurityAuditEvents);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateNodeSecurity(CircleNodeSecurityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.NodeCredential.Role != IdentityKeyRole.Node
            || state.TransportCredential.Role != IdentityKeyRole.Transport
            || !IdentityCryptography.IsValidCredential(state.NodeCredential)
            || !IdentityCryptography.IsValidCredential(state.TransportCredential)
            || state.SignedTransportBinding is not { Length: > 0 and <= 16 * 1024 })
        {
            throw new ArgumentException("Circle Node security state is invalid.", nameof(state));
        }
    }

    private static void ValidateAnchorCommit(AnchorAdmissionCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ValidateNodeSecurity(new CircleNodeSecurityState(
            commit.CircleId,
            commit.Node.NodeId,
            commit.NodeCredential,
            commit.TransportCredential,
            commit.SignedTransportBinding));
        if (commit.PackageSha256 is not { Length: 32 }
            || commit.RequestSha256 is not { Length: 32 }
            || commit.EncodedResponse is not { Length: > 0 and <= 64 * 1024 }
            || commit.Member.CircleId != commit.CircleId
            || commit.Member.Role != MemberRole.Member
            || commit.Node.CircleId != commit.CircleId
            || commit.MemberCredential.Role != IdentityKeyRole.Member
            || !IdentityCryptography.IsValidCredential(commit.MemberCredential)
            || commit.AuthoritySequence <= 0
            || commit.AdmittedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The Anchor admission commit is invalid.", nameof(commit));
        }
    }

    private static void ValidateJoinedCommit(JoinedCircleCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.PackageSha256 is not { Length: 32 }
            || commit.Trust.CircleId != commit.Circle.Circle.Id
            || commit.Trust.SignedAdmissionReceipt is not { Length: > 0 and <= 64 * 1024 }
            || commit.LocalMemberCredential.Role != IdentityKeyRole.Member
            || !IdentityCryptography.IsValidCredential(commit.LocalMemberCredential)
            || commit.NodeSecurity.Count != commit.Circle.Nodes.Count
            || commit.JoinedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The joined Circle commit is invalid.", nameof(commit));
        }

        foreach (var state in commit.NodeSecurity)
        {
            ValidateNodeSecurity(state);
        }
    }

    private static bool NodeSecurityEquals(
        CircleNodeSecurityState expected,
        CircleNodeSecurityState? actual) =>
        actual is not null
        && expected.CircleId == actual.CircleId
        && expected.NodeId == actual.NodeId
        && expected.NodeCredential.KeyId == actual.NodeCredential.KeyId
        && expected.TransportCredential.KeyId == actual.TransportCredential.KeyId
        && CryptographicOperations.FixedTimeEquals(
            expected.SignedTransportBinding,
            actual.SignedTransportBinding);
}
