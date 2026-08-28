using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balls.WindowsConformance;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class HostConformanceRunnerTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Folder = @"C:\BallsConformance\Issue124-contract-a";
    private const string CircleId = "019d2a6b-1b66-7d38-9c35-8d64ca8f8111";
    private const string ContributionId = "019d2a6b-1b66-7d38-9c35-8d64ca8f8222";
    private static readonly string PlanId = new('1', 64);
    private static readonly string OwnershipId = new('2', 64);
    private static readonly string SeedSha256 = new('3', 64);
    private static readonly string BaselineAcl = new('4', 64);
    private static readonly string HostedAcl = new('5', 64);
    private static readonly string Infrastructure = new('6', 64);
    private static readonly string RootInventory = new('7', 64);
    private static readonly string ShareConfiguration = new('8', 64);
    private static readonly string FirewallConfiguration = new('9', 64);
    private static readonly string AccountConfiguration = new('a', 64);
    private static readonly string SecureStoreInventory = new('b', 64);
    private static readonly string MappingConfiguration = new('c', 64);
    private static readonly string ServiceConfiguration = new('d', 64);
    private static readonly string PolicyConfiguration = new('e', 64);

    [TestMethod]
    public async Task Complete_product_driven_lifecycle_returns_exact_redacted_evidence()
    {
        using var context = HostContext.Create();
        var processes = SuccessfulProcesses(context.Package);
        var receiptPath = Path.Combine(context.ReceiptDirectory.Path, "host-receipt.json");

        var receipt = await new WindowsCircleFilesHostConformanceRunner(
                processes,
                "Write-Output fixed-host-operation")
            .RunAsync(context.Target, context.Package, receiptPath, CancellationToken.None);

        Assert.AreEqual("passed", receipt.Outcome);
        Assert.AreEqual("complete", receipt.Phase);
        Assert.AreEqual("hosting_plan_changed", receipt.ProductOutcomes?.PlanMismatch);
        Assert.AreEqual("hosting_apply_failed", receipt.ProductOutcomes?.InjectedFailure);
        Assert.AreEqual("applied", receipt.ProductOutcomes?.Apply);
        Assert.AreEqual("already-applied", receipt.ProductOutcomes?.Retry);
        Assert.AreEqual("removed", receipt.ProductOutcomes?.Removal);
        Assert.IsTrue(receipt.NativeEvidence?.SeedBytesPreserved);
        Assert.IsTrue(receipt.NativeEvidence?.FolderAclRestored);
        Assert.IsTrue(receipt.NativeEvidence?.UnrelatedInfrastructureUnchanged);
        Assert.IsTrue(receipt.Cleanup.Complete);
        Assert.IsEmpty(receipt.Interventions);
        Assert.HasCount(11, processes.Requests);
        Assert.AreEqual("scp", processes.Requests[2].FileName);
        Assert.IsTrue(processes.Requests
            .Where(request => request.FileName == "ssh")
            .All(request => request.Arguments.Contains("ClearAllForwardings=yes")
                && request.Arguments.Contains("StrictHostKeyChecking=yes")
                && request.StandardInput!.EndsWith(
                    "__BALLS_HOST_CONFORMANCE_OPERATION_END__" + Environment.NewLine,
                    StringComparison.Ordinal)));
        var json = File.ReadAllText(receiptPath);
        Assert.IsFalse(json.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("privateKey", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("S-1-5-", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("preMutationSddl", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(json, "\"interventions\": []");
    }

    [TestMethod]
    public async Task Plan_mismatch_contract_failure_writes_a_partial_receipt_and_cleans_up()
    {
        using var context = HostContext.Create();
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(product: true)),
            Result(),
            Result(PrepareJson(context.Package)),
            Result(NativeJson("prepared")),
            Result(RefusalJson(planCode: "unexpected_success")),
            Result(CleanupJson()));
        var receiptPath = Path.Combine(context.ReceiptDirectory.Path, "partial.json");

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(processes, "fixed")
                .RunAsync(context.Target, context.Package, receiptPath, CancellationToken.None));

        Assert.AreEqual("refusal_contract_mismatch", error.Code);
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        Assert.AreEqual("failed", receipt.RootElement.GetProperty("outcome").GetString());
        Assert.AreEqual("injected-failure", receipt.RootElement.GetProperty("phase").GetString());
        Assert.IsTrue(receipt.RootElement.GetProperty("cleanup").GetProperty("complete").GetBoolean());
    }

    [TestMethod]
    public async Task Timeout_returns_a_stable_partial_receipt_before_any_mutation()
    {
        using var context = HostContext.Create();
        var receiptPath = Path.Combine(context.ReceiptDirectory.Path, "timeout.json");

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(
                    new FakeConformanceProcessRunner(new ConformanceRefusalException("transport_timeout")),
                    "fixed")
                .RunAsync(context.Target, context.Package, receiptPath, CancellationToken.None));

        Assert.AreEqual("transport_timeout", error.Code);
        var json = File.ReadAllText(receiptPath);
        StringAssert.Contains(json, "\"phase\": \"target-preflight\"");
        StringAssert.Contains(json, "\"code\": \"transport_timeout\"");
    }

    [TestMethod]
    public async Task Cleanup_failure_overrides_an_earlier_failure_and_preserves_retryable_scope()
    {
        using var context = HostContext.Create();
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(product: true)),
            Result(),
            new ConformanceProcessResult(1, FailureJson("prepare_failed"), string.Empty),
            new ConformanceProcessResult(1, FailureJson("cleanup_incomplete"), string.Empty));
        var receiptPath = Path.Combine(context.ReceiptDirectory.Path, "cleanup-failed.json");

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(processes, "fixed")
                .RunAsync(context.Target, context.Package, receiptPath, CancellationToken.None));

        Assert.AreEqual("cleanup_unconfirmed", error.Code);
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        Assert.IsFalse(receipt.RootElement.GetProperty("cleanup").GetProperty("complete").GetBoolean());
        Assert.AreEqual("cleanup_unconfirmed", receipt.RootElement.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task Secret_bearing_preflight_is_refused_without_echoing_private_material()
    {
        using var context = HostContext.Create();
        var poisoned = PreflightJson()[..^1] + ",\"providerSecret\":\"never-echo-this\"}";
        var receiptPath = Path.Combine(context.ReceiptDirectory.Path, "redacted.json");

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(
                    new FakeConformanceProcessRunner(Result(poisoned)),
                    "fixed")
                .RunAsync(context.Target, context.Package, receiptPath, CancellationToken.None));

        Assert.AreEqual("receipt_contains_secret", error.Code);
        Assert.IsFalse(error.Message.Contains("never-echo-this", StringComparison.Ordinal));
        Assert.IsFalse(File.ReadAllText(receiptPath).Contains("never-echo-this", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Prepared_product_identity_must_match_the_exact_package()
    {
        using var context = HostContext.Create();
        var wrong = PrepareJson(context.Package).Replace(Commit, new string('f', 40), StringComparison.Ordinal);
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(product: true)),
            Result(),
            Result(wrong),
            Result(CleanupJson()));

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(processes, "fixed")
                .RunAsync(
                    context.Target,
                    context.Package,
                    Path.Combine(context.ReceiptDirectory.Path, "identity.json"),
                    CancellationToken.None));

        Assert.AreEqual("prepare_identity_or_contract_mismatch", error.Code);
    }

    [TestMethod]
    public async Task Preflight_refuses_a_non_local_or_storage_identity_mismatch_before_transfer()
    {
        using var context = HostContext.Create();
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson(localDiskBacked: false)));

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(processes, "fixed")
                .RunAsync(
                    context.Target,
                    context.Package,
                    Path.Combine(context.ReceiptDirectory.Path, "storage.json"),
                    CancellationToken.None));

        Assert.AreEqual("target_identity_or_precondition_mismatch", error.Code);
        Assert.HasCount(1, processes.Requests);
    }

    [TestMethod]
    public async Task Provisioned_native_state_requires_the_exact_applicable_acl_shape()
    {
        using var context = HostContext.Create();
        var invalidAcl = NativeJson("provisioned", provisioned: true)
            .Replace("\"aclShapeExact\":true", "\"aclShapeExact\":false", StringComparison.Ordinal);
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(product: true)),
            Result(),
            Result(PrepareJson(context.Package)),
            Result(NativeJson("prepared")),
            Result(RefusalJson()),
            Result(NativeJson("rolled-back")),
            Result(ApplyJson()),
            Result(invalidAcl),
            Result(CleanupJson()));

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(processes, "fixed")
                .RunAsync(
                    context.Target,
                    context.Package,
                    Path.Combine(context.ReceiptDirectory.Path, "acl.json"),
                    CancellationToken.None));

        Assert.AreEqual("native_provisioning_mismatch", error.Code);
    }

    [TestMethod]
    public async Task Provisioned_native_state_cannot_change_any_unrelated_state_component()
    {
        using var context = HostContext.Create();
        var changedRoot = NativeJson(
            "provisioned",
            provisioned: true,
            rootInventory: new string('f', 64));
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(product: true)),
            Result(),
            Result(PrepareJson(context.Package)),
            Result(NativeJson("prepared")),
            Result(RefusalJson()),
            Result(NativeJson("rolled-back")),
            Result(ApplyJson()),
            Result(changedRoot),
            Result(CleanupJson()));

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(processes, "fixed")
                .RunAsync(
                    context.Target,
                    context.Package,
                    Path.Combine(context.ReceiptDirectory.Path, "provisioned-unrelated.json"),
                    CancellationToken.None));

        Assert.AreEqual("native_provisioning_mismatch", error.Code);
    }

    [TestMethod]
    public async Task Any_unrelated_state_component_change_refuses_the_final_receipt()
    {
        using var context = HostContext.Create();
        var changedRoot = NativeJson("final", rootInventory: new string('f', 64));
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(product: true)),
            Result(),
            Result(PrepareJson(context.Package)),
            Result(NativeJson("prepared")),
            Result(RefusalJson()),
            Result(NativeJson("rolled-back")),
            Result(ApplyJson()),
            Result(NativeJson("provisioned", provisioned: true)),
            Result(RemovalJson()),
            Result(changedRoot),
            Result(CleanupJson()));

        var error = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            new WindowsCircleFilesHostConformanceRunner(processes, "fixed")
                .RunAsync(
                    context.Target,
                    context.Package,
                    Path.Combine(context.ReceiptDirectory.Path, "unrelated.json"),
                    CancellationToken.None));

        Assert.AreEqual("final_cleanup_mismatch", error.Code);
    }

    private static FakeConformanceProcessRunner SuccessfulProcesses(WindowsPackageIdentity package) =>
        new(
            Result(PreflightJson()),
            Result(PreflightJson(product: true)),
            Result(),
            Result(PrepareJson(package)),
            Result(NativeJson("prepared")),
            Result(RefusalJson()),
            Result(NativeJson("rolled-back")),
            Result(ApplyJson()),
            Result(NativeJson("provisioned", provisioned: true)),
            Result(RemovalJson()),
            Result(NativeJson("final")));

    private static ConformanceProcessResult Result(string output = "") => new(0, output, string.Empty);

    private static string PreflightJson(
        bool product = false,
        bool localDiskBacked = true) => JsonSerializer.Serialize(new
        {
            schema = "balls-windows-host-preflight-v1",
            operation = "windows-circle-files-host-v1",
            outcome = "ready",
            computerName = "BALLS-LAB",
            account = new
            {
                kind = "administrator",
                elevated = true,
                integrity = "high",
                identitySha256 = product ? new string('a', 64) : new string('b', 64),
            },
            windows = new
            {
                productName = "Windows Server 2025",
                displayVersion = "24H2",
                buildNumber = "26100",
                installationType = "Server",
            },
            policy = new { executionPolicy = "Restricted", uacEnabled = true, applicationControl = "off" },
            network = new { categories = new[] { "private" }, firewallProfiles = new[] { "private", "public" } },
            dirtyState = new { existingBallsProcesses = 0, ownedArtifacts = 0, clean = true },
            storage = new
            {
                localDiskBacked,
                volumeIdentitySha256 = new string('b', 64),
                diskIdentitySha256 = new string('c', 64),
                fileSystem = "NTFS",
                busType = "SCSI",
            },
        });

    private static string PrepareJson(WindowsPackageIdentity package) => JsonSerializer.Serialize(new
    {
        schema = "balls-windows-host-prepare-v1",
        operation = "windows-circle-files-host-v1",
        outcome = "prepared",
        preflight = JsonSerializer.Deserialize<JsonElement>(PreflightJson(product: true)),
        product = new
        {
            commit = Commit,
            packageSha256 = package.Sha256,
            packageName = package.FileName,
            version = package.Version,
            cliVersion = package.Version,
            daemonVersion = package.Version,
            daemonPrivilege = "administrative",
            buildConfiguration = "debug-conformance",
        },
        context = new
        {
            circleId = CircleId,
            contributionId = ContributionId,
            folderPath = Folder,
            planId = PlanId,
            shareName = "balls-0123456789ab",
            firewallRuleName = "Balls-SMB-0123456789abcdef0123456789abcdef",
            ownershipId = OwnershipId,
        },
        seed = Seed(),
    });

    private static string RefusalJson(string planCode = "hosting_plan_changed") => JsonSerializer.Serialize(new
    {
        schema = "balls-windows-host-refusal-v1",
        operation = "windows-circle-files-host-v1",
        outcome = "rolled-back",
        planMismatchCode = planCode,
        injectedFailureCode = "hosting_apply_failed",
    });

    private static string ApplyJson() => JsonSerializer.Serialize(new
    {
        schema = "balls-windows-host-apply-v1",
        operation = "windows-circle-files-host-v1",
        outcome = "provisioned",
        applyStatus = "applied",
        retryStatus = "already-applied",
        planId = PlanId,
    });

    private static string RemovalJson() => JsonSerializer.Serialize(new
    {
        schema = "balls-windows-host-removal-v1",
        operation = "windows-circle-files-host-v1",
        outcome = "removed",
        removalStatus = "removed",
        openSessionCount = 0,
        planId = new string('7', 64),
        cleanup = Cleanup(),
    });

    private static string CleanupJson() => JsonSerializer.Serialize(new
    {
        schema = "balls-windows-host-cleanup-v1",
        operation = "windows-circle-files-host-v1",
        outcome = "clean",
        productRemovalAttempted = true,
        productResourcesRemoved = true,
        cleanup = Cleanup(),
        code = "clean",
    });

    private static object Cleanup() => new
    {
        daemonStopped = true,
        stateRemoved = true,
        packageRemoved = true,
        complete = true,
    };

    private static object Seed() => new
    {
        fileName = "before-balls.txt",
        length = 27,
        sha256 = SeedSha256,
    };

    private static string NativeJson(
        string state,
        bool provisioned = false,
        string? rootInventory = null) => JsonSerializer.Serialize(new
        {
            schema = "balls-windows-host-native-v1",
            operation = "windows-circle-files-host-v1",
            outcome = "observed",
            observation = new
            {
                state,
                pathIdentitySha256 = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(Folder.ToUpperInvariant()))),
                folderExists = true,
                folderReparsePoint = false,
                seed = Seed(),
                aclProtected = provisioned,
                aclSha256 = provisioned ? HostedAcl : BaselineAcl,
                ownerSidSha256 = new string('a', 64),
                ownerFullControl = provisioned,
                systemFullControl = provisioned,
                aclAccessRuleCount = provisioned ? 2 : 4,
                aclApplicableRuleCount = provisioned ? 2 : 4,
                aclDenyRuleCount = 0,
                aclShapeExact = provisioned,
                markerExists = provisioned,
                markerMatches = provisioned,
                journalExists = provisioned,
                journalMatches = provisioned,
                firewallRecoveryExists = false,
                shareCount = provisioned ? 1 : 0,
                sharePathMatches = provisioned,
                shareEncryptionRequired = provisioned,
                shareAccessCount = provisioned ? 1 : 0,
                shareAccessRestrictedToOwner = provisioned,
                firewallRuleCount = provisioned ? 1 : 0,
                firewallPrivateOnly = provisioned,
                firewallLocalSubnetOnly = provisioned,
                firewallTcp445Only = provisioned,
                firewallLanmanServerOnly = provisioned,
                unrelatedState = new
                {
                    rootInventorySha256 = rootInventory ?? RootInventory,
                    shareConfigurationSha256 = ShareConfiguration,
                    firewallConfigurationSha256 = FirewallConfiguration,
                    accountConfigurationSha256 = AccountConfiguration,
                    secureStoreInventorySha256 = SecureStoreInventory,
                    mappingConfigurationSha256 = MappingConfiguration,
                    serviceConfigurationSha256 = ServiceConfiguration,
                    policyConfigurationSha256 = PolicyConfiguration,
                    combinedSha256 = Infrastructure,
                },
            },
        });

    private static string FailureJson(string code) => JsonSerializer.Serialize(new
    {
        schema = "balls-windows-host-failure-v1",
        operation = "windows-circle-files-host-v1",
        outcome = "failed",
        code,
    });

    private sealed class HostContext : IDisposable
    {
        private HostContext(
            TargetProfileFixture profile,
            PackageFixture packageFixture,
            TemporaryDirectory receiptDirectory,
            WindowsConformanceTargetProfile target,
            WindowsPackageIdentity package)
        {
            Profile = profile;
            PackageFixture = packageFixture;
            ReceiptDirectory = receiptDirectory;
            Target = target;
            Package = package;
        }

        public TargetProfileFixture Profile { get; }
        public PackageFixture PackageFixture { get; }
        public TemporaryDirectory ReceiptDirectory { get; }
        public WindowsConformanceTargetProfile Target { get; }
        public WindowsPackageIdentity Package { get; }

        public static HostContext Create()
        {
            var profile = TargetProfileFixture.Create(
                operation: "windows-circle-files-host-v1",
                disposablePath: Folder);
            var packageFixture = Balls.WindowsConformance.Tests.PackageFixture.Create(Commit);
            return new HostContext(
                profile,
                packageFixture,
                TemporaryDirectory.Create(),
                WindowsConformanceTargetProfileLoader.Load(profile.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit));
        }

        public void Dispose()
        {
            ReceiptDirectory.Dispose();
            PackageFixture.Dispose();
            Profile.Dispose();
        }
    }
}
