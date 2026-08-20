using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Balls.Core;
using Balls.Storage.Sqlite;

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
        var retried = await store.CommitAnchorAdmissionAsync(commit);
        var conflict = await store.CommitAnchorAdmissionAsync(
            commit with { RequestSha256 = RandomNumberGenerator.GetBytes(32) });
        var details = await store.GetCircleAsync(created.Circle.Id);

        Assert.AreEqual(AnchorAdmissionCommitStatus.Accepted, accepted.Status);
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
                    Now);
            await store.CommitJoinedCircleAsync(joined);
            await store.CommitJoinedCircleAsync(joined);
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

        await using var reopened = await OpenAsync(directory.Path);
        var persisted = await reopened.GetCircleAsync(circleId);
        Assert.AreEqual(2, persisted!.Members.Count);
        Assert.AreEqual(2, persisted.Nodes.Count);
        Assert.IsNull(await reopened.GetCircleAuthorityAsync(circleId));
        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => reopened.ExportCircleAuthorityAsync(
                circleId,
                "correct horse battery staple".AsMemory()));
        Assert.AreEqual("circle_authority_not_found", error.Code);
    }

    private static Task<SqliteLocalStateStore> OpenAsync(string path) =>
        SqliteLocalStateStore.OpenAsync(path, TestPrivateMaterialProtector.Instance);

    private static PublicIdentityCredential Credential(IdentityKeyRole role)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return IdentityCryptography.CreateCredential(role, key);
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
