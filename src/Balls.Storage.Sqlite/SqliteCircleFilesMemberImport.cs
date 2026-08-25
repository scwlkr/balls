using System.Security.Cryptography;
using Balls.Core;
using Microsoft.Data.Sqlite;

namespace Balls.Storage.Sqlite;

public sealed partial class SqliteLocalStateStore
{
    public Task ImportAuthorizedCircleFilesAccessAsync(
        CircleFilesContribution contribution,
        MemberAccessGrant grant,
        PublicIdentityCredential ownerCredential,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(
            async token =>
            {
                using var transaction = connection.BeginTransaction();
                var context = await ReadCircleFilesAuthorizationContextAsync(
                    contribution.CircleId,
                    transaction,
                    token).ConfigureAwait(false)
                    ?? throw new LocalStateException(
                        "circle_not_found",
                        "The requested Circle is not known to this Node.");
                if (context.MemberRole != MemberRole.Member)
                {
                    throw new LocalStateException(
                        "circle_files_member_required",
                        "Only an ordinary Circle Member can import remote access.");
                }

                var requestIds = CircleFilesRemoteAuthorization.Validate(
                    contribution,
                    grant,
                    ownerCredential,
                    context);
                await EnsureRemoteOwnerAsync(
                    contribution.CircleId,
                    contribution.Authorization.OwnerMemberId,
                    contribution.Provider.NodeId,
                    ownerCredential,
                    transaction,
                    token).ConfigureAwait(false);

                var existingContribution = await ReadContributionByRequestAsync(
                    requestIds.ContributionRequestId,
                    transaction,
                    token).ConfigureAwait(false);
                if (existingContribution is null)
                {
                    using var insertContribution = connection.CreateCommand();
                    insertContribution.Transaction = transaction;
                    insertContribution.CommandText =
                        """
                        INSERT INTO circle_files_contributions (
                            contribution_id, request_id, circle_id, provider_id, provider_node_id,
                            display_name, lifecycle, generation, created_at_utc, owner_member_id,
                            authority_generation, authorized_at_utc, authorization_transcript,
                            member_signature, circle_authority_signature)
                        VALUES (
                            $contribution_id, $request_id, $circle_id, $provider_id, $provider_node_id,
                            $display_name, $lifecycle, $generation, $created_at_utc, $owner_member_id,
                            $authority_generation, $authorized_at_utc, $transcript,
                            $member_signature, $authority_signature);
                        """;
                    AddContributionParameters(
                        insertContribution,
                        requestIds.ContributionRequestId,
                        contribution);
                    await insertContribution.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                else
                {
                    EnsureEquivalentContribution(existingContribution, contribution);
                    if (existingContribution.Id != contribution.Id)
                    {
                        throw new LocalStateConflictException(
                            "circle_files_contribution_request_conflict",
                            "The remote contribution identity conflicts with existing state.");
                    }
                }

                var existingGrant = await ReadGrantByRequestAsync(
                    requestIds.GrantRequestId,
                    transaction,
                    token).ConfigureAwait(false);
                if (existingGrant is null)
                {
                    await ValidateGrantReferencesAsync(grant, transaction, token)
                        .ConfigureAwait(false);
                    using var insertGrant = connection.CreateCommand();
                    insertGrant.Transaction = transaction;
                    insertGrant.CommandText =
                        """
                        INSERT INTO circle_files_access_grants (
                            grant_id, request_id, circle_id, contribution_id, member_id, access,
                            lifecycle, generation, created_at_utc, owner_member_id,
                            authority_generation, authorized_at_utc, authorization_transcript,
                            member_signature, circle_authority_signature)
                        VALUES (
                            $grant_id, $request_id, $circle_id, $contribution_id, $member_id, $access,
                            $lifecycle, $generation, $created_at_utc, $owner_member_id,
                            $authority_generation, $authorized_at_utc, $transcript,
                            $member_signature, $authority_signature);
                        """;
                    AddGrantParameters(insertGrant, requestIds.GrantRequestId, grant);
                    await insertGrant.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                else
                {
                    EnsureEquivalentGrant(existingGrant, grant);
                    if (existingGrant.Id != grant.Id)
                    {
                        throw new LocalStateConflictException(
                            "circle_files_grant_request_conflict",
                            "The remote grant identity conflicts with existing state.");
                    }
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task EnsureRemoteOwnerAsync(
        CircleId circleId,
        MemberId memberId,
        NodeId providerNodeId,
        PublicIdentityCredential credential,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var owner = connection.CreateCommand();
        owner.Transaction = transaction;
        owner.CommandText =
            """
            SELECT m.role, c.key_algorithm, c.key_id, c.public_key_spki,
                   EXISTS(SELECT 1 FROM circle_nodes n
                          WHERE n.circle_id = m.circle_id AND n.node_id = $node_id)
            FROM members m
            LEFT JOIN circle_member_credentials c
                ON c.circle_id = m.circle_id AND c.member_id = m.member_id
            WHERE m.circle_id = $circle_id AND m.member_id = $member_id;
            """;
        owner.Parameters.AddWithValue("$circle_id", circleId.ToString());
        owner.Parameters.AddWithValue("$member_id", memberId.ToString());
        owner.Parameters.AddWithValue("$node_id", providerNodeId.ToString());

        bool needsCredential;
        await using (var reader = await owner.ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || (MemberRole)reader.GetInt32(0) != MemberRole.Owner
                || reader.GetInt32(4) != 1)
            {
                throw new LocalStateException(
                    "circle_files_authorization_failed",
                    "The remote Circle Files Owner or provider Node is not trusted.");
            }

            needsCredential = reader.IsDBNull(1);
            if (!needsCredential
                && (reader.GetString(1) != credential.Algorithm
                    || reader.GetString(2) != credential.KeyId
                    || !CryptographicOperations.FixedTimeEquals(
                        (byte[])reader.GetValue(3),
                        credential.SubjectPublicKeyInfo)))
            {
                throw new LocalStateException(
                    "circle_files_authorization_failed",
                    "The remote Circle Files Owner credential conflicts with trusted state.");
            }
        }

        if (needsCredential)
        {
            await InsertMemberCredentialAsync(
                circleId,
                memberId,
                credential,
                transaction,
                cancellationToken).ConfigureAwait(false);
        }

        await InsertMemberNodeAuthorizationAsync(
            circleId,
            memberId,
            providerNodeId,
            transaction,
            cancellationToken).ConfigureAwait(false);
    }
}
