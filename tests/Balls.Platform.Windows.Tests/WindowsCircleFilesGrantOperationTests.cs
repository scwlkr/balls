using System.Runtime.Versioning;
using System.Security.Cryptography;
using Balls.Core;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesGrantOperationTests
{
    private static readonly WindowsCircleFilesGrantHelperPlan Plan = CreatePlan();

    [TestMethod]
    public async Task Grant_apply_retry_collision_and_failure_rollback_are_exact()
    {
        var applied = new StubOperations();
        var status = await new WindowsCircleFilesGrantOperation(applied)
            .ExecuteAsync(Plan, CancellationToken.None);
        Assert.AreEqual(CircleFilesGrantCredentialApplyStatus.Applied, status);
        CollectionAssert.AreEqual(
            Enum.GetValues<WindowsCircleFilesGrantOperationStep>(),
            applied.Applied.ToArray());

        var retry = await new WindowsCircleFilesGrantOperation(applied)
            .ExecuteAsync(Plan, CancellationToken.None);
        Assert.AreEqual(CircleFilesGrantCredentialApplyStatus.AlreadyApplied, retry);

        var collision = new StubOperations();
        collision.States[WindowsCircleFilesGrantOperationStep.LocalAccount] =
            WindowsCircleFilesOwnedState.Collision;
        var collisionError = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => new WindowsCircleFilesGrantOperation(collision)
                .ExecuteAsync(Plan, CancellationToken.None).AsTask());
        Assert.AreEqual("grant_resource_collision", collisionError.Code);
        Assert.AreEqual(0, collision.Applied.Count);

        var failure = new StubOperations
        {
            FailOn = WindowsCircleFilesGrantOperationStep.ShareAccess,
        };
        var failureError = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => new WindowsCircleFilesGrantOperation(failure)
                .ExecuteAsync(Plan, CancellationToken.None).AsTask());
        Assert.AreEqual("grant_apply_failed", failureError.Code);
        CollectionAssert.AreEqual(
            new[]
            {
                WindowsCircleFilesGrantOperationStep.FolderAcl,
                WindowsCircleFilesGrantOperationStep.GrantMarker,
                WindowsCircleFilesGrantOperationStep.LocalAccount,
            },
            failure.RolledBack.ToArray());
    }

    [TestMethod]
    public void Elevated_grant_authorization_binds_exact_signed_grant_and_script_is_closed()
    {
        var request = CreateAuthorizedRequest();
        WindowsCircleFilesGrantAuthorizationVerifier.Validate(request);

        var tampered = request with { MemberId = Guid.NewGuid().ToString("D") };
        var error = Assert.ThrowsExactly<CircleFilesHostingException>(
            () => WindowsCircleFilesGrantAuthorizationVerifier.Validate(tampered));
        Assert.AreEqual("grant_authorization_invalid", error.Code);

        var script = WindowsCircleFilesGrantPowerShell.Script;
        StringAssert.Contains(script, "SeDenyInteractiveLogonRight");
        StringAssert.Contains(script, "SeDenyRemoteInteractiveLogonRight");
        StringAssert.Contains(script, "Grant-SmbShareAccess");
        StringAssert.Contains(script, "$user.SID.Translate([System.Security.Principal.NTAccount]).Value");
        Assert.IsFalse(script.Contains("-AccountName ('.\\'", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("ScriptBlock", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Generated_provider_secrets_are_bounded_random_and_complex()
    {
        var first = CircleFilesGrantSecret.Generate();
        var second = CircleFilesGrantSecret.Generate();
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(first);
            Assert.AreEqual(32, first.Length);
            Assert.IsFalse(first.SequenceEqual(second));
            Assert.IsTrue(text.Any(char.IsUpper));
            Assert.IsTrue(text.Any(char.IsLower));
            Assert.IsTrue(text.Any(char.IsDigit));
            Assert.IsTrue(text.Any(value => "!#$%+-_=".Contains(value, StringComparison.Ordinal)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(first);
            CryptographicOperations.ZeroMemory(second);
        }
    }

    [TestMethod]
    public void Helper_plan_comparison_is_structural_after_json_round_trip()
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new WindowsCircleFilesHelperEnvelope("grant", null, Plan));
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<WindowsCircleFilesHelperEnvelope>(bytes);

        Assert.IsNotNull(roundTrip?.Grant);
        Assert.IsTrue(WindowsCircleFilesHelperCommand.GrantPlansEqual(roundTrip.Grant, Plan));
    }

    private static WindowsCircleFilesGrantHelperPlan CreatePlan()
    {
        var proof = new CircleFilesHostAuthorizationProof(
            [1], [2], [3],
            new CircleFilesHostPublicCredential("member", "p256-sha256", "member:key", [4]),
            new CircleFilesHostPublicCredential("circle-authority", "p256-sha256", "root:key", [5]));
        var hostRequest = new CircleFilesHostRequest(
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8901",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8902",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8903",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8904",
            "Files", @"C:\BallsShares\Files", new string('a', 64), proof);
        var hostPlan = new WindowsCircleFilesHelperPlan(
            new CircleFilesHostPlan(
                1, new string('b', 64), "windows-smb-3.1.1", hostRequest.FolderPath,
                "balls-share", "Balls-rule", new string('c', 64), true, []),
            hostRequest,
            "S-1-5-21-1000");
        var request = new CircleFilesGrantCredentialRequest(
            hostRequest,
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8905",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8906",
            "read-write",
            1,
            new string('d', 64),
            proof);
        return new WindowsCircleFilesGrantHelperPlan(
            new CircleFilesGrantCredentialPlan(
                1, new string('e', 64), "windows-smb-3.1.1", hostRequest.FolderPath,
                hostPlan.PublicPlan.ShareName, "BallsG-abcdef0123456", new string('f', 64),
                "read-write", 1, []),
            request,
            hostPlan,
            hostPlan.OwnerSid,
            System.Text.Encoding.UTF8.GetBytes("Aa2!provider-secret-that-never-leaks"));
    }

    private static CircleFilesGrantCredentialRequest CreateAuthorizedRequest()
    {
        var circleId = new CircleId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8901"));
        var contributionId = new CircleFilesContributionId(
            Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8902"));
        var providerId = new CircleFilesProviderId(
            Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8903"));
        var nodeId = new NodeId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8904"));
        var ownerId = new MemberId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8905"));
        var targetId = new MemberId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8906"));
        var grantId = new MemberAccessGrantId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8907"));
        var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var unsignedAuthorization = new CircleFilesOwnerAuthorization(ownerId, 1, at, [], [], []);
        var contribution = new CircleFilesContribution(
            contributionId,
            circleId,
            new CircleFilesProviderIdentity(providerId, nodeId),
            "Files",
            CircleFilesContributionLifecycle.Defined,
            1,
            at,
            unsignedAuthorization);
        var grant = new MemberAccessGrant(
            grantId,
            circleId,
            contributionId,
            targetId,
            MemberAccessMode.ReadWrite,
            MemberAccessGrantLifecycle.Defined,
            1,
            at,
            unsignedAuthorization);
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var memberCredential = ToCredential(IdentityCryptography.CreateCredential(
            IdentityKeyRole.Member, memberKey));
        var rootCredential = ToCredential(IdentityCryptography.CreateCredential(
            IdentityKeyRole.CircleAuthority, rootKey));
        var contributionTranscript = CircleFilesAuthorizationTranscript.EncodeContribution(
            new CircleFilesContributionRequestId(
                Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8908")),
            contribution);
        var grantTranscript = CircleFilesAuthorizationTranscript.EncodeGrant(
            new MemberAccessGrantRequestId(
                Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8909")),
            grant);
        var hostProof = CreateProof(contributionTranscript, memberKey, rootKey, memberCredential, rootCredential);
        var grantProof = CreateProof(grantTranscript, memberKey, rootKey, memberCredential, rootCredential);
        var host = new CircleFilesHostRequest(
            circleId.ToString(), contributionId.ToString(), providerId.ToString(), nodeId.ToString(),
            "Files", @"C:\BallsShares\Files",
            CircleFilesHostAuthorizationDigest.Compute(hostProof), hostProof);
        return new CircleFilesGrantCredentialRequest(
            host, grantId.ToString(), targetId.ToString(), "read-write", 1,
            CircleFilesHostAuthorizationDigest.Compute(grantProof), grantProof);
    }

    private static CircleFilesHostAuthorizationProof CreateProof(
        byte[] transcript,
        ECDsa memberKey,
        ECDsa rootKey,
        CircleFilesHostPublicCredential memberCredential,
        CircleFilesHostPublicCredential rootCredential) =>
        new(
            transcript,
            IdentityCryptography.Sign(transcript, memberKey),
            IdentityCryptography.Sign(transcript, rootKey),
            memberCredential,
            rootCredential);

    private static CircleFilesHostPublicCredential ToCredential(PublicIdentityCredential value) =>
        new(
            value.Role == IdentityKeyRole.Member ? "member" : "circle-authority",
            value.Algorithm,
            value.KeyId,
            value.SubjectPublicKeyInfo);

    private sealed class StubOperations : IWindowsCircleFilesGrantOperations
    {
        public Dictionary<WindowsCircleFilesGrantOperationStep, WindowsCircleFilesOwnedState> States { get; } =
            Enum.GetValues<WindowsCircleFilesGrantOperationStep>()
                .ToDictionary(value => value, _ => WindowsCircleFilesOwnedState.Missing);
        public WindowsCircleFilesGrantOperationStep? FailOn { get; init; }
        public List<WindowsCircleFilesGrantOperationStep> Applied { get; } = [];
        public List<WindowsCircleFilesGrantOperationStep> RolledBack { get; } = [];

        public ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken) => ValueTask.FromResult(States[step]);

        public ValueTask ApplyAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken)
        {
            if (step == FailOn)
            {
                throw new InvalidOperationException("injected");
            }
            Applied.Add(step);
            States[step] = WindowsCircleFilesOwnedState.Owned;
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken)
        {
            RolledBack.Add(step);
            States[step] = WindowsCircleFilesOwnedState.Missing;
            return ValueTask.CompletedTask;
        }
    }
}
