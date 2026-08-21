using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Balls.Core;

namespace Balls.Core.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class CircleFilesApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 18, 0, 0, TimeSpan.Zero);
    private static readonly CircleId CircleId = new(
        Guid.Parse("0198d000-1000-7000-8000-000000000001"));
    private static readonly MemberId OwnerId = new(
        Guid.Parse("0198d000-1000-7000-8000-000000000002"));
    private static readonly NodeId NodeId = new(
        Guid.Parse("0198d000-1000-7000-8000-000000000003"));

    [TestMethod]
    public async Task Owner_creates_a_provider_neutral_contribution_with_dual_signed_authorization()
    {
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var anchorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = new CircleFilesAuthorizationContext(
            CircleId,
            OwnerId,
            MemberRole.Owner,
            IdentityCryptography.CreateCredential(IdentityKeyRole.Member, memberKey),
            NodeId,
            4,
            IdentityCryptography.CreateCredential(IdentityKeyRole.CircleAuthority, rootKey));
        var state = new InMemoryCircleFilesStateStore(context, memberKey);
        var identities = new TestIdentityAuthorityStore(
            new CircleAuthorityIdentity(
                CircleId,
                4,
                context.RootCredential,
                IdentityCryptography.CreateCredential(IdentityKeyRole.Anchor, anchorKey)),
            rootKey);
        var application = new CircleFilesApplication(
            state,
            identities,
            new FixedTimeProvider(Now));
        var requestId = new CircleFilesContributionRequestId(
            Guid.Parse("0198d000-1000-7000-8000-000000000004"));

        var contribution = await application.CreateContributionAsync(
            new CreateCircleFilesContributionCommand(
                requestId,
                CircleId,
                "  Project Files  "));

        Assert.AreEqual(CircleId, contribution.CircleId);
        Assert.AreEqual("Project Files", contribution.DisplayName);
        Assert.AreEqual(NodeId, contribution.Provider.NodeId);
        Assert.AreNotEqual(Guid.Empty, contribution.Provider.Id.Value);
        Assert.AreEqual(CircleFilesContributionLifecycle.Defined, contribution.Lifecycle);
        Assert.AreEqual(1, contribution.Generation);
        Assert.AreEqual(Now, contribution.CreatedAtUtc);
        Assert.AreEqual(OwnerId, contribution.Authorization.OwnerMemberId);
        Assert.AreEqual(4, contribution.Authorization.AuthorityGeneration);
        Assert.AreEqual(Now, contribution.Authorization.AuthorizedAtUtc);
        Assert.IsTrue(IdentityCryptography.Verify(
            contribution.Authorization.Transcript,
            contribution.Authorization.MemberSignature,
            context.MemberCredential));
        Assert.IsTrue(IdentityCryptography.Verify(
            contribution.Authorization.Transcript,
            contribution.Authorization.CircleAuthoritySignature,
            context.RootCredential));
        Assert.AreEqual(contribution, state.Contributions.Single());
    }

    [TestMethod]
    public async Task Owner_grants_whole_folder_access_with_dual_signed_authorization()
    {
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var anchorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = new CircleFilesAuthorizationContext(
            CircleId,
            OwnerId,
            MemberRole.Owner,
            IdentityCryptography.CreateCredential(IdentityKeyRole.Member, memberKey),
            NodeId,
            4,
            IdentityCryptography.CreateCredential(IdentityKeyRole.CircleAuthority, rootKey));
        var state = new InMemoryCircleFilesStateStore(context, memberKey);
        var identities = new TestIdentityAuthorityStore(
            new CircleAuthorityIdentity(
                CircleId,
                4,
                context.RootCredential,
                IdentityCryptography.CreateCredential(IdentityKeyRole.Anchor, anchorKey)),
            rootKey);
        var application = new CircleFilesApplication(
            state,
            identities,
            new FixedTimeProvider(Now));
        var contribution = await application.CreateContributionAsync(
            new CreateCircleFilesContributionCommand(
                new CircleFilesContributionRequestId(
                    Guid.Parse("0198d000-1000-7000-8000-000000000005")),
                CircleId,
                "Project Files"));

        var grant = await application.CreateAccessGrantAsync(
            new CreateMemberAccessGrantCommand(
                new MemberAccessGrantRequestId(
                    Guid.Parse("0198d000-1000-7000-8000-000000000006")),
                CircleId,
                contribution.Id,
                OwnerId,
                MemberAccessMode.ReadWrite));

        Assert.AreEqual(CircleId, grant.CircleId);
        Assert.AreEqual(contribution.Id, grant.ContributionId);
        Assert.AreEqual(OwnerId, grant.MemberId);
        Assert.AreEqual(MemberAccessMode.ReadWrite, grant.Access);
        Assert.AreEqual(MemberAccessGrantLifecycle.Defined, grant.Lifecycle);
        Assert.AreEqual(1, grant.Generation);
        Assert.AreEqual(Now, grant.CreatedAtUtc);
        Assert.AreEqual(OwnerId, grant.Authorization.OwnerMemberId);
        Assert.AreEqual(4, grant.Authorization.AuthorityGeneration);
        Assert.IsTrue(IdentityCryptography.Verify(
            grant.Authorization.Transcript,
            grant.Authorization.MemberSignature,
            context.MemberCredential));
        Assert.IsTrue(IdentityCryptography.Verify(
            grant.Authorization.Transcript,
            grant.Authorization.CircleAuthoritySignature,
            context.RootCredential));
        Assert.AreEqual(grant, state.Grants.Single());
    }

    [TestMethod]
    public async Task Non_owner_cannot_authorize_a_contribution_mutation()
    {
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var anchorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = new CircleFilesAuthorizationContext(
            CircleId,
            OwnerId,
            MemberRole.Member,
            IdentityCryptography.CreateCredential(IdentityKeyRole.Member, memberKey),
            NodeId,
            4,
            IdentityCryptography.CreateCredential(IdentityKeyRole.CircleAuthority, rootKey));
        var state = new InMemoryCircleFilesStateStore(context, memberKey);
        var identities = new TestIdentityAuthorityStore(
            new CircleAuthorityIdentity(
                CircleId,
                4,
                context.RootCredential,
                IdentityCryptography.CreateCredential(IdentityKeyRole.Anchor, anchorKey)),
            rootKey);
        var application = new CircleFilesApplication(
            state,
            identities,
            new FixedTimeProvider(Now));

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => application.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    new CircleFilesContributionRequestId(
                        Guid.Parse("0198d000-1000-7000-8000-000000000007")),
                    CircleId,
                    "Project Files")));

        Assert.AreEqual("circle_files_owner_required", error.Code);
        Assert.AreEqual(0, state.MemberSignatureCount);
        Assert.AreEqual(0, identities.AuthoritySignatureCount);
        Assert.AreEqual(0, state.Contributions.Count);
    }

    [TestMethod]
    public async Task Stale_or_substituted_Circle_authority_cannot_authorize_a_mutation()
    {
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustedRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var substitutedRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var anchorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = new CircleFilesAuthorizationContext(
            CircleId,
            OwnerId,
            MemberRole.Owner,
            IdentityCryptography.CreateCredential(IdentityKeyRole.Member, memberKey),
            NodeId,
            4,
            IdentityCryptography.CreateCredential(
                IdentityKeyRole.CircleAuthority,
                trustedRootKey));
        var state = new InMemoryCircleFilesStateStore(context, memberKey);
        var identities = new TestIdentityAuthorityStore(
            new CircleAuthorityIdentity(
                CircleId,
                4,
                IdentityCryptography.CreateCredential(
                    IdentityKeyRole.CircleAuthority,
                    substitutedRootKey),
                IdentityCryptography.CreateCredential(IdentityKeyRole.Anchor, anchorKey)),
            substitutedRootKey);
        var application = new CircleFilesApplication(
            state,
            identities,
            new FixedTimeProvider(Now));

        var error = await Assert.ThrowsExactlyAsync<LocalStateException>(
            () => application.CreateContributionAsync(
                new CreateCircleFilesContributionCommand(
                    new CircleFilesContributionRequestId(
                        Guid.Parse("0198d000-1000-7000-8000-000000000008")),
                    CircleId,
                    "Project Files")));

        Assert.AreEqual("circle_files_authority_unavailable", error.Code);
        Assert.AreEqual(0, state.MemberSignatureCount);
        Assert.AreEqual(0, identities.AuthoritySignatureCount);
        Assert.AreEqual(0, state.Contributions.Count);
    }

    private sealed class InMemoryCircleFilesStateStore(
        CircleFilesAuthorizationContext context,
        ECDsa memberKey) : ICircleFilesStateStore
    {
        internal List<CircleFilesContribution> Contributions { get; } = [];

        internal List<MemberAccessGrant> Grants { get; } = [];

        internal int MemberSignatureCount { get; private set; }

        public Task<CircleFilesAuthorizationContext?> GetAuthorizationContextAsync(
            CircleId circleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CircleFilesAuthorizationContext?>(
                circleId == context.CircleId ? context : null);

        public Task<byte[]> SignWithLocalMemberAsync(
            CircleId circleId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            MemberSignatureCount++;
            return Task.FromResult(IdentityCryptography.Sign(data.Span, memberKey));
        }

        public Task<CircleFilesContribution> CreateContributionAsync(
            CircleFilesContributionRequestId requestId,
            CircleFilesContribution contribution,
            CancellationToken cancellationToken = default)
        {
            Contributions.Add(contribution);
            return Task.FromResult(contribution);
        }

        public Task<IReadOnlyList<CircleFilesContribution>> ListContributionsAsync(
            CircleId circleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CircleFilesContribution>>(
                Contributions.Where(value => value.CircleId == circleId).ToArray());

        public Task<MemberAccessGrant> CreateAccessGrantAsync(
            MemberAccessGrantRequestId requestId,
            MemberAccessGrant grant,
            CancellationToken cancellationToken = default)
        {
            Grants.Add(grant);
            return Task.FromResult(grant);
        }

        public Task<IReadOnlyList<MemberAccessGrant>> ListAccessGrantsAsync(
            CircleId circleId,
            CircleFilesContributionId contributionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemberAccessGrant>>(
                Grants.Where(value =>
                    value.CircleId == circleId && value.ContributionId == contributionId).ToArray());
    }

    private sealed class TestIdentityAuthorityStore(
        CircleAuthorityIdentity authority,
        ECDsa rootKey) : IIdentityAuthorityStore
    {
        internal int AuthoritySignatureCount { get; private set; }

        public Task<CircleAuthorityIdentity?> GetCircleAuthorityAsync(
            CircleId circleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CircleAuthorityIdentity?>(circleId == authority.CircleId ? authority : null);

        public Task<byte[]> SignWithCircleAuthorityAsync(
            CircleId circleId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            AuthoritySignatureCount++;
            return Task.FromResult(IdentityCryptography.Sign(data.Span, rootKey));
        }

        public Task<NodeCryptographicIdentity?> GetNodeCryptographicIdentityAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LocalTransportIdentity?> GetLocalTransportIdentityAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<X509Certificate2> CreateTransportCertificateAsync(
            string dnsName,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<byte[]> SignWithNodeAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<byte[]> SignWithTransportAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<byte[]> SignWithCircleAnchorAsync(
            CircleId circleId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AuthorityBackupEnvelope> ExportCircleAuthorityAsync(
            CircleId circleId,
            ReadOnlyMemory<char> passphrase,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
