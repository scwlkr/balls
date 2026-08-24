using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

        var blocked = new StubOperations();
        blocked.States[WindowsCircleFilesGrantOperationStep.LocalAccount] =
            WindowsCircleFilesOwnedState.Owned;
        blocked.States[WindowsCircleFilesGrantOperationStep.ShareAccess] =
            WindowsCircleFilesOwnedState.Blocked;
        var blockedError = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => new WindowsCircleFilesGrantOperation(blocked)
                .ExecuteAsync(Plan, CancellationToken.None).AsTask());
        Assert.AreEqual("grant_resource_collision", blockedError.Code);
        CollectionAssert.AreEqual(
            new[] { WindowsCircleFilesGrantOperationStep.LocalAccount },
            blocked.RolledBack.ToArray());

        var blockedOwned = new StubOperations();
        foreach (var step in Enum.GetValues<WindowsCircleFilesGrantOperationStep>())
        {
            blockedOwned.States[step] = WindowsCircleFilesOwnedState.Owned;
        }
        blockedOwned.States[WindowsCircleFilesGrantOperationStep.ShareAccess] =
            WindowsCircleFilesOwnedState.BlockedOwned;
        var blockedOwnedError = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => new WindowsCircleFilesGrantOperation(blockedOwned)
                .ExecuteAsync(Plan, CancellationToken.None).AsTask());
        Assert.AreEqual("grant_resource_collision", blockedOwnedError.Code);
        CollectionAssert.AreEqual(
            Enum.GetValues<WindowsCircleFilesGrantOperationStep>().Reverse().ToArray(),
            blockedOwned.RolledBack.ToArray());

        var recoverable = new StubOperations();
        recoverable.States[WindowsCircleFilesGrantOperationStep.LocalAccount] =
            WindowsCircleFilesOwnedState.Recoverable;
        var recovered = await new WindowsCircleFilesGrantOperation(recoverable)
            .ExecuteAsync(Plan, CancellationToken.None);
        Assert.AreEqual(CircleFilesGrantCredentialApplyStatus.Applied, recovered);
        CollectionAssert.AreEqual(
            new[] { WindowsCircleFilesGrantOperationStep.LocalAccount },
            recoverable.RolledBack.ToArray());

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
    public async Task Grant_removal_requires_a_second_open_session_confirmation_and_recovers_partials()
    {
        var busy = new StubRemovalOperations(openSessionCount: 2);
        var first = await new WindowsCircleFilesGrantRemovalOperation(busy, busy)
            .ExecuteAsync(Plan, terminateOpenSessions: false, CancellationToken.None);

        Assert.AreEqual(CircleFilesCleanupStatus.Busy, first.Status);
        Assert.AreEqual(2, first.OpenSessionCount);
        Assert.AreEqual(0, busy.TerminationCount);
        Assert.AreEqual(0, busy.RolledBack.Count);

        var confirmed = await new WindowsCircleFilesGrantRemovalOperation(busy, busy)
            .ExecuteAsync(Plan, terminateOpenSessions: true, CancellationToken.None);

        Assert.AreEqual(CircleFilesCleanupStatus.Removed, confirmed.Status);
        Assert.AreEqual(2, confirmed.OpenSessionCount);
        Assert.AreEqual(1, busy.TerminationCount);
        CollectionAssert.AreEqual(
            Enum.GetValues<WindowsCircleFilesGrantOperationStep>().Reverse().ToArray(),
            busy.RolledBack.ToArray());

        var partial = new StubRemovalOperations(openSessionCount: 0)
        {
            FailRollbackOn = WindowsCircleFilesGrantOperationStep.FolderAcl,
        };
        var partialResult = await new WindowsCircleFilesGrantRemovalOperation(partial, partial)
            .ExecuteAsync(Plan, terminateOpenSessions: false, CancellationToken.None);
        Assert.AreEqual(CircleFilesCleanupStatus.Partial, partialResult.Status);
        CollectionAssert.AreEqual(
            new[] { WindowsCircleFilesGrantOperationStep.ShareAccess },
            partial.RolledBack.ToArray());

        partial.FailRollbackOn = null;
        var recovered = await new WindowsCircleFilesGrantRemovalOperation(partial, partial)
            .ExecuteAsync(Plan, terminateOpenSessions: false, CancellationToken.None);
        Assert.AreEqual(CircleFilesCleanupStatus.Removed, recovered.Status);
        Assert.IsTrue(partial.States.Values.All(value => value == WindowsCircleFilesOwnedState.Missing));

        var substituted = new StubRemovalOperations(openSessionCount: 1);
        substituted.States[WindowsCircleFilesGrantOperationStep.LocalAccount] =
            WindowsCircleFilesOwnedState.Collision;
        var collision = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => new WindowsCircleFilesGrantRemovalOperation(substituted, substituted)
                .ExecuteAsync(Plan, terminateOpenSessions: true, CancellationToken.None).AsTask());
        Assert.AreEqual("grant_resource_collision", collision.Code);
        Assert.AreEqual(0, substituted.TerminationCount);
        Assert.AreEqual(0, substituted.RolledBack.Count);
    }

    [TestMethod]
    public void Elevated_grant_authorization_binds_exact_signed_grant_and_script_is_closed()
    {
        var (request, cleanup) = CreateAuthorizedRequests();
        WindowsCircleFilesGrantAuthorizationVerifier.Validate(request);
        WindowsCircleFilesGrantAuthorizationVerifier.ValidateCleanup(cleanup);

        var tampered = request with { MemberId = Guid.NewGuid().ToString("D") };
        var error = Assert.ThrowsExactly<CircleFilesHostingException>(
            () => WindowsCircleFilesGrantAuthorizationVerifier.Validate(tampered));
        Assert.AreEqual("grant_authorization_invalid", error.Code);
        var tamperedCleanup = cleanup with
        {
            Revocation = cleanup.Revocation with { RevokedGeneration = 2 },
        };
        var cleanupError = Assert.ThrowsExactly<CircleFilesHostingException>(
            () => WindowsCircleFilesGrantAuthorizationVerifier.ValidateCleanup(tamperedCleanup));
        Assert.AreEqual("grant_authorization_invalid", cleanupError.Code);

        var script = WindowsCircleFilesGrantPowerShell.Script;
        StringAssert.Contains(script, "SeDenyInteractiveLogonRight");
        StringAssert.Contains(script, "SeDenyRemoteInteractiveLogonRight");
        StringAssert.Contains(script, "LsaRemoveAccountRights");
        StringAssert.Contains(script, "[BallsGrantRights]::RemoveOwnedSubset");
        StringAssert.Contains(script, "[BallsGrantRights]::OwnedSubset");
        StringAssert.Contains(script, "[BallsGrantRights]::TokenGroups");
        StringAssert.Contains(script, "S-1-5-11");
        StringAssert.Contains(script, "S-1-5-2");
        StringAssert.Contains(script, "S-1-5-32-545");
        StringAssert.Contains(script, "S-1-5-113");
        StringAssert.Contains(script, "InjectAccountTerminationStep");
        StringAssert.Contains(script, "InjectAccountFailure");
        StringAssert.Contains(script, "Grant-SmbShareAccess");
        StringAssert.Contains(script, "GrantMarkersValid");
        StringAssert.Contains(script, "Select-Object -First 1001");
        StringAssert.Contains(script, "CmdletizationQuery_NotFound_ClientUserName,Get-SmbSession");
        StringAssert.Contains(script, "Close-SmbSession -SessionId");
        StringAssert.Contains(script, "BlockedOwned");
        StringAssert.Contains(script, "$expectedTarget");
        StringAssert.Contains(script, "$user.SID.Translate([System.Security.Principal.NTAccount]).Value");
        Assert.IsFalse(script.Contains("-AccountName ('.\\'", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("ScriptBlock", StringComparison.OrdinalIgnoreCase));
        var description = WindowsCircleFilesGrantPowerShell.AccountDescription(Plan);
        Assert.AreEqual("Balls grant v1 " + new string('f', 32), description);
        Assert.IsTrue(description.Length <= 48);
    }

    [TestMethod]
    public async Task Protected_fixed_script_preserves_stdin_and_is_removed_after_use()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The protected fixed-script transport requires Windows.");
            return;
        }

        const string script =
            "$value=[Console]::In.ReadToEnd();"
            + "[PSCustomObject]@{Value=$value}|ConvertTo-Json -Compress";
        string directoryPath;
        string scriptPath;
        using (var fixedScript = WindowsProtectedPowerShellScript.Create(script))
        {
            directoryPath = fixedScript.DirectoryPath;
            scriptPath = fixedScript.Path;
            Assert.AreEqual(script, File.ReadAllText(scriptPath));

            var currentUser = WindowsIdentity.GetCurrent().User!;
            var security = new FileInfo(scriptPath).GetAccessControl(AccessControlSections.All);
            Assert.IsTrue(security.AreAccessRulesProtected);
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
                ?? throw new AssertFailedException("The fixed script has no SID owner.");
            Assert.AreEqual(
                currentUser.Value,
                owner.Value);
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                currentUser.Value,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            };
            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().ToArray();
            Assert.IsTrue(rules.All(rule =>
                rule.AccessControlType == AccessControlType.Allow
                && rule.FileSystemRights == FileSystemRights.FullControl
                && rule.IdentityReference is SecurityIdentifier sid
                && expected.Remove(sid.Value)));
            Assert.AreEqual(0, expected.Count);

            var startInfo = fixedScript.CreateStartInfo();
            Assert.IsFalse(startInfo.Environment.ContainsKey("BALLS_FIXED_SCRIPT"));
            Assert.AreEqual(directoryPath, startInfo.WorkingDirectory);
            CollectionAssert.Contains(startInfo.ArgumentList.ToArray(), "-File");
            CollectionAssert.Contains(startInfo.ArgumentList.ToArray(), scriptPath);
            var output = await BoundedWindowsInspectionProcessRunner.RunWithInputAsync(
                startInfo,
                "stdin-probe",
                TimeSpan.FromSeconds(10),
                1024,
                CancellationToken.None);
            Assert.AreEqual("{\"Value\":\"stdin-probe\"}", output.Trim());
        }

        Assert.IsFalse(File.Exists(scriptPath));
        Assert.IsFalse(Directory.Exists(directoryPath));
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
    public void Host_folder_acl_requires_exact_protected_owner_system_baseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The exact Windows folder ACL contract requires Windows.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "balls-grant-acl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var currentSid = WindowsIdentity.GetCurrent().User!;
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(currentSid);
            security.AddAccessRule(new FileSystemAccessRule(
                currentSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
            Assert.IsTrue(WindowsCircleFilesGrantSystemOperations.HasExactHostFolderSecurity(
                directory,
                currentSid.Value));

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Deny));
            new DirectoryInfo(directory).SetAccessControl(security);
            Assert.IsFalse(WindowsCircleFilesGrantSystemOperations.HasExactHostFolderSecurity(
                directory,
                currentSid.Value));

            security.RemoveAccessRuleAll(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadAndExecute,
                AccessControlType.Deny));
            security.RemoveAccessRuleAll(new FileSystemAccessRule(
                currentSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                currentSid,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
            Assert.IsFalse(WindowsCircleFilesGrantSystemOperations.HasExactHostFolderSecurity(
                directory,
                currentSid.Value));

            security.RemoveAccessRuleAll(new FileSystemAccessRule(
                currentSid,
                FileSystemRights.ReadAndExecute,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                currentSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier("S-1-5-21-111111111-222222222-333333333-4444"),
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
            Assert.IsFalse(WindowsCircleFilesGrantSystemOperations.HasExactHostFolderSecurity(
                directory,
                currentSid.Value));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Marker_acl_failure_removes_the_exact_file_created_by_the_attempt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The protected Windows marker contract requires Windows.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "balls-grant-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var markerPath = Path.Combine(directory, ".balls-grant-test-v1.json");
        try
        {
            Assert.ThrowsExactly<IOException>(() =>
                WindowsCircleFilesGrantSystemOperations.WriteProtectedMarkerFile(
                    markerPath,
                    "{\"ownershipId\":\"test\"}\n",
                    WindowsIdentity.GetCurrent().User!.Value,
                    injectPartialWriteFailure: false,
                    injectAclFailure: true));
            Assert.IsFalse(File.Exists(markerPath));

            WindowsCircleFilesGrantSystemOperations.WriteProtectedMarkerFile(
                markerPath,
                "{\"ownershipId\":\"test\"}\n",
                WindowsIdentity.GetCurrent().User!.Value,
                injectPartialWriteFailure: false,
                injectAclFailure: false);
            Assert.IsTrue(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Marker_partial_write_failure_deletes_the_file_and_rolls_back_the_account()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The protected Windows marker contract requires Windows.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "balls-grant-marker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var markerPath = Path.Combine(directory, ".balls-grant-test-v1.json");
        try
        {
            var operations = new PartialMarkerFailureOperations(markerPath);
            var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
                () => new WindowsCircleFilesGrantOperation(operations)
                    .ExecuteAsync(Plan, CancellationToken.None).AsTask());

            Assert.AreEqual("grant_apply_failed", error.Code);
            Assert.IsFalse(File.Exists(markerPath));
            Assert.IsFalse(operations.AccountExists);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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

    [TestMethod]
    public void Mixed_helper_envelope_still_zeroes_deserialized_grant_secret()
    {
        var secret = Plan.Secret.ToArray();
        var envelope = new WindowsCircleFilesHelperEnvelope(
            "host",
            Plan.HostPlan,
            Plan with { Secret = secret });

        WindowsCircleFilesHelperCommand.ZeroGrantSecret(envelope);

        Assert.IsTrue(secret.All(value => value == 0));
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

    private static (CircleFilesGrantCredentialRequest Grant, CircleFilesGrantCleanupRequest Cleanup)
        CreateAuthorizedRequests()
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
        var revocation = new MemberAccessGrantRevocation(
            new MemberAccessGrantRevocationRequestId(
                Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8910")),
            circleId,
            contributionId,
            grantId,
            1,
            at,
            unsignedAuthorization);
        var revocationTranscript = CircleFilesAuthorizationTranscript.EncodeGrantRevocation(revocation);
        var revocationProof = CreateProof(
            revocationTranscript,
            memberKey,
            rootKey,
            memberCredential,
            rootCredential);
        var host = new CircleFilesHostRequest(
            circleId.ToString(), contributionId.ToString(), providerId.ToString(), nodeId.ToString(),
            "Files", @"C:\BallsShares\Files",
            CircleFilesHostAuthorizationDigest.Compute(hostProof), hostProof);
        var grantRequest = new CircleFilesGrantCredentialRequest(
            host, grantId.ToString(), targetId.ToString(), "read-write", 1,
            CircleFilesHostAuthorizationDigest.Compute(grantProof), grantProof);
        return (
            grantRequest,
            new CircleFilesGrantCleanupRequest(
                grantRequest,
                new CircleFilesGrantRevocationProof(
                    revocation.RequestId.ToString(),
                    circleId.ToString(),
                    contributionId.ToString(),
                    grantId.ToString(),
                    1,
                    CircleFilesHostAuthorizationDigest.Compute(revocationProof),
                    revocationProof)));
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

    private sealed class StubRemovalOperations :
        IWindowsCircleFilesGrantOperations,
        IWindowsCircleFilesGrantSessionOperations
    {
        private int openSessionCount;

        internal StubRemovalOperations(int openSessionCount)
        {
            this.openSessionCount = openSessionCount;
            States = Enum.GetValues<WindowsCircleFilesGrantOperationStep>()
                .ToDictionary(value => value, _ => WindowsCircleFilesOwnedState.Owned);
        }

        internal Dictionary<WindowsCircleFilesGrantOperationStep, WindowsCircleFilesOwnedState> States
        { get; }

        internal WindowsCircleFilesGrantOperationStep? FailRollbackOn { get; set; }

        internal int TerminationCount { get; private set; }

        internal List<WindowsCircleFilesGrantOperationStep> RolledBack { get; } = [];

        public ValueTask<int> CountOpenSessionsAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            CancellationToken cancellationToken) => ValueTask.FromResult(openSessionCount);

        public ValueTask TerminateOpenSessionsAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            CancellationToken cancellationToken)
        {
            TerminationCount++;
            openSessionCount = 0;
            return ValueTask.CompletedTask;
        }

        public ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken) => ValueTask.FromResult(States[step]);

        public ValueTask ApplyAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask RollbackAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken)
        {
            if (step == FailRollbackOn)
            {
                throw new InvalidOperationException("injected partial cleanup");
            }

            RolledBack.Add(step);
            States[step] = WindowsCircleFilesOwnedState.Missing;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PartialMarkerFailureOperations(string markerPath) : IWindowsCircleFilesGrantOperations
    {
        public bool AccountExists { get; private set; }

        public ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken) => ValueTask.FromResult(step switch
            {
                WindowsCircleFilesGrantOperationStep.LocalAccount when AccountExists =>
                    WindowsCircleFilesOwnedState.Owned,
                WindowsCircleFilesGrantOperationStep.GrantMarker when File.Exists(markerPath) =>
                    WindowsCircleFilesOwnedState.Collision,
                _ => WindowsCircleFilesOwnedState.Missing,
            });

        public ValueTask ApplyAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken)
        {
            if (step == WindowsCircleFilesGrantOperationStep.LocalAccount)
            {
                AccountExists = true;
                return ValueTask.CompletedTask;
            }
            if (step == WindowsCircleFilesGrantOperationStep.GrantMarker)
            {
                WindowsCircleFilesGrantSystemOperations.WriteProtectedMarkerFile(
                    markerPath,
                    "{\"ownershipId\":\"test\"}\n",
                    WindowsIdentity.GetCurrent().User!.Value,
                    injectPartialWriteFailure: true,
                    injectAclFailure: false);
            }
            throw new InvalidOperationException("Unexpected operation step.");
        }

        public ValueTask RollbackAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            WindowsCircleFilesGrantOperationStep step,
            CancellationToken cancellationToken)
        {
            if (step == WindowsCircleFilesGrantOperationStep.LocalAccount)
            {
                AccountExists = false;
            }
            return ValueTask.CompletedTask;
        }
    }
}
