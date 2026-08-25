using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Balls.Core;

public static class CircleFilesRemoteAuthorization
{
    private const string ContributionDomain = "balls-circle-files-contribution-create-v1";
    private const string GrantDomain = "balls-circle-files-access-grant-create-v1";

    public static (CircleFilesContributionRequestId ContributionRequestId,
        MemberAccessGrantRequestId GrantRequestId) Validate(
        CircleFilesContribution contribution,
        MemberAccessGrant grant,
        PublicIdentityCredential ownerCredential,
        CircleFilesAuthorizationContext context)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(ownerCredential);
        ArgumentNullException.ThrowIfNull(context);

        if (ownerCredential.Role != IdentityKeyRole.Member
            || !IdentityCryptography.IsValidCredential(ownerCredential)
            || contribution.CircleId != context.CircleId
            || grant.CircleId != context.CircleId
            || grant.ContributionId != contribution.Id
            || grant.MemberId != context.MemberId
            || contribution.Provider.NodeId == context.NodeId
            || contribution.Authorization.OwnerMemberId != grant.Authorization.OwnerMemberId
            || contribution.Authorization.AuthorityGeneration != context.AuthorityGeneration
            || grant.Authorization.AuthorityGeneration != context.AuthorityGeneration
            || contribution.Lifecycle != CircleFilesContributionLifecycle.Defined
            || grant.Lifecycle != MemberAccessGrantLifecycle.Defined
            || contribution.Generation != 1
            || grant.Generation != 1
            || contribution.CreatedAtUtc != contribution.Authorization.AuthorizedAtUtc
            || grant.CreatedAtUtc != grant.Authorization.AuthorizedAtUtc
            || string.IsNullOrWhiteSpace(contribution.DisplayName)
            || contribution.DisplayName.Length > 100
            || !Enum.IsDefined(grant.Access))
        {
            throw Rejected();
        }

        var contributionRequestId = new CircleFilesContributionRequestId(
            ReadRequestId(contribution.Authorization.Transcript, ContributionDomain));
        var grantRequestId = new MemberAccessGrantRequestId(
            ReadRequestId(grant.Authorization.Transcript, GrantDomain));
        ValidateSignedTranscript(
            contribution.Authorization,
            CircleFilesAuthorizationTranscript.EncodeContribution(
                contributionRequestId,
                contribution),
            ownerCredential,
            context.RootCredential);
        ValidateSignedTranscript(
            grant.Authorization,
            CircleFilesAuthorizationTranscript.EncodeGrant(grantRequestId, grant),
            ownerCredential,
            context.RootCredential);
        return (contributionRequestId, grantRequestId);
    }

    private static Guid ReadRequestId(byte[] transcript, string expectedDomain)
    {
        var domain = Encoding.UTF8.GetBytes(expectedDomain);
        if (transcript.Length < sizeof(int) + domain.Length + 16
            || BinaryPrimitives.ReadInt32BigEndian(transcript.AsSpan(0, sizeof(int)))
                != domain.Length
            || !transcript.AsSpan(sizeof(int), domain.Length).SequenceEqual(domain))
        {
            throw Rejected();
        }

        var requestId = new Guid(
            transcript.AsSpan(sizeof(int) + domain.Length, 16),
            bigEndian: true);
        if (requestId == Guid.Empty)
        {
            throw Rejected();
        }

        return requestId;
    }

    private static void ValidateSignedTranscript(
        CircleFilesOwnerAuthorization authorization,
        byte[] expected,
        PublicIdentityCredential ownerCredential,
        PublicIdentityCredential rootCredential)
    {
        if (!CryptographicOperations.FixedTimeEquals(expected, authorization.Transcript)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.MemberSignature,
                ownerCredential)
            || !IdentityCryptography.Verify(
                authorization.Transcript,
                authorization.CircleAuthoritySignature,
                rootCredential))
        {
            throw Rejected();
        }
    }

    private static LocalStateException Rejected() =>
        new(
            "circle_files_authorization_failed",
            "The remote Circle Files authorization is invalid or stale.");
}
