using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Balls.Core;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesHostingTests
{
    private static readonly CircleFilesHostRequest Request = CreateAuthorizedRequest();

    [TestMethod]
    public async Task Preview_is_deterministic_bounded_and_requires_no_elevation()
    {
        var helper = new StubHelper();
        var provisioner = CreateProvisioner(new StubEnvironment(), helper);

        var first = await provisioner.PreviewAsync(Request, CancellationToken.None);
        var second = await provisioner.PreviewAsync(Request, CancellationToken.None);

        Assert.AreEqual(CircleFilesHostingContract.Version, first.ContractVersion);
        Assert.AreEqual(first.PlanId, second.PlanId);
        CollectionAssert.AreEqual(first.Actions.ToArray(), second.Actions.ToArray());
        Assert.AreEqual("balls-019d2a6b1b66", first.ShareName);
        Assert.AreEqual(4, first.Actions.Count);
        Assert.AreEqual(64, first.PlanId.Length);
        Assert.AreEqual(0, helper.CallCount);
    }

    [TestMethod]
    public async Task Apply_requires_the_exact_preview_and_calls_one_narrow_helper_operation()
    {
        var helper = new StubHelper();
        var provisioner = CreateProvisioner(new StubEnvironment(), helper);
        var preview = await provisioner.PreviewAsync(Request, CancellationToken.None);

        var result = await provisioner.ApplyAsync(
            Request,
            preview.PlanId,
            CancellationToken.None);

        Assert.AreEqual(CircleFilesHostApplyStatus.Applied, result.Status);
        Assert.AreEqual(preview.PlanId, result.Plan.PlanId);
        Assert.AreEqual(1, helper.CallCount);
        Assert.AreEqual(preview.PlanId, helper.LastPlan?.PublicPlan.PlanId);
    }

    [TestMethod]
    public async Task Changed_or_substituted_plan_is_rejected_before_elevation()
    {
        var helper = new StubHelper();
        var provisioner = CreateProvisioner(new StubEnvironment(), helper);

        var exception = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => provisioner.ApplyAsync(Request, new string('0', 64), CancellationToken.None).AsTask());

        Assert.AreEqual("hosting_plan_changed", exception.Code);
        Assert.AreEqual(0, helper.CallCount);
    }

    [TestMethod]
    public async Task Unsafe_or_ambiguous_paths_are_rejected_before_elevation()
    {
        var cases = new[]
        {
            Request with { FolderPath = @"\\server\share" },
            Request with { FolderPath = @"C:\" },
            Request with { FolderPath = @"C:\Users\Owner\Circle" },
            Request with { FolderPath = @"Z:\Circle" },
        };

        foreach (var request in cases)
        {
            var helper = new StubHelper();
            var environment = new StubEnvironment
            {
                FixedDrive = !request.FolderPath.StartsWith("Z:", StringComparison.Ordinal),
            };
            var exception = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
                () => CreateProvisioner(environment, helper)
                    .PreviewAsync(request, CancellationToken.None).AsTask());
            Assert.AreEqual("hosting_path_invalid", exception.Code, request.FolderPath);
            Assert.AreEqual(0, helper.CallCount);
        }
    }

    [TestMethod]
    public async Task Reparse_nonempty_and_preexisting_file_collisions_fail_closed()
    {
        var environments = new[]
        {
            new StubEnvironment { HasReparsePoint = true },
            new StubEnvironment { FileAtTarget = true },
            new StubEnvironment { ExistingDirectory = true, Entries = [@"C:\BallsShares\Company\user.txt"] },
        };

        foreach (var environment in environments)
        {
            var exception = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
                () => CreateProvisioner(environment, new StubHelper())
                    .PreviewAsync(Request, CancellationToken.None).AsTask());
            Assert.IsTrue(
                exception.Code is "hosting_path_invalid" or "hosting_folder_not_empty",
                exception.Code);
        }
    }

    [TestMethod]
    public async Task Not_ready_host_is_rejected_without_disclosing_raw_readiness_details()
    {
        var provisioner = new WindowsCircleFilesHostProvisioner(
            new StubReadiness(CircleFilesReadinessStatus.NotReady),
            new StubEnvironment(),
            new StubHelper());

        var exception = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => provisioner.PreviewAsync(Request, CancellationToken.None).AsTask());

        Assert.AreEqual("hosting_prerequisites_not_ready", exception.Code);
        Assert.IsFalse(exception.Message.Contains("PowerShell", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Fixed_mutation_script_enforces_encryption_private_profile_and_local_subnet_without_evaluation()
    {
        var script = WindowsCircleFilesPowerShell.Script;

        StringAssert.Contains(script, "-EncryptData $true");
        StringAssert.Contains(script, "-Profile Private");
        StringAssert.Contains(script, "-RemoteAddress LocalSubnet");
        StringAssert.Contains(script, "-Service LanmanServer");
        StringAssert.Contains(script, "Translate([System.Security.Principal.NTAccount])");
        Assert.IsFalse(script.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("ScriptBlock", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("-Profile Public", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Fixed_mutation_command_selector_is_a_closed_typed_allow_list()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                WindowsCircleFilesPowerShellCommand.InspectShare,
                WindowsCircleFilesPowerShellCommand.CreateShare,
                WindowsCircleFilesPowerShellCommand.RemoveShare,
                WindowsCircleFilesPowerShellCommand.InspectFirewall,
                WindowsCircleFilesPowerShellCommand.CreateFirewall,
                WindowsCircleFilesPowerShellCommand.RemoveFirewall,
            },
            Enum.GetValues<WindowsCircleFilesPowerShellCommand>());
    }

    [TestMethod]
    public void Elevated_helper_authorization_verifies_both_signatures_and_exact_contribution_fields()
    {
        WindowsCircleFilesHostAuthorizationVerifier.Validate(Request);

        var tampered = Request with
        {
            ContributionId = "019d2a6b-1b66-7d38-9c35-8d64ca8f8999",
        };
        var error = Assert.ThrowsExactly<CircleFilesHostingException>(
            () => WindowsCircleFilesHostAuthorizationVerifier.Validate(tampered));

        Assert.AreEqual("hosting_authorization_invalid", error.Code);
    }

    [TestMethod]
    public void Elevated_helper_accepts_the_full_UTF8_size_of_a_valid_contribution_name()
    {
        WindowsCircleFilesHostAuthorizationVerifier.Validate(
            CreateAuthorizedRequest(new string('\u754c', 100)));
    }

    [TestMethod]
    public void Elevated_path_revalidation_keeps_the_authenticated_daemon_owner_sid()
    {
        const string daemonOwnerSid = "S-1-5-21-1000";

        Assert.AreEqual(
            daemonOwnerSid,
            new WindowsCircleFilesPathEnvironment(daemonOwnerSid).CurrentUserSid);
    }

    [TestMethod]
    public async Task Helper_protocol_rejects_oversized_messages_before_deserialization()
    {
        await using var stream = new MemoryStream();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => WindowsCircleFilesHelperProtocol.WriteAsync(
                stream,
                new string('x', 100),
                maximumBytes: 20,
            CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task Exact_partial_journal_can_be_previewed_for_recovery_after_restart()
    {
        var initial = await CreateProvisioner(new StubEnvironment(), new StubHelper())
            .PreviewAsync(Request, CancellationToken.None);
        var journalPath = Path.Combine(
            Request.FolderPath,
            WindowsCircleFilesSystemOperations.JournalFileName);
        var journal = System.Text.Json.JsonSerializer.Serialize(new
        {
            ContractVersion = 1,
            initial.OwnershipId,
            initial.PlanId,
            FolderPath = Request.FolderPath,
            OwnerSid = "S-1-5-21-1000",
            TargetExisted = false,
            PreMutationSddl = "O:S-1-5-21-1000D:",
            CreatedDirectories = new[] { Request.FolderPath },
        }) + "\n";
        var environment = new StubEnvironment
        {
            ExistingDirectory = true,
            Entries = [journalPath],
            Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [journalPath] = journal,
            },
        };

        var recovered = await CreateProvisioner(environment, new StubHelper())
            .PreviewAsync(Request, CancellationToken.None);

        Assert.AreEqual(initial.PlanId, recovered.PlanId);
        Assert.IsTrue(recovered.TargetExists);
    }

    [TestMethod]
    public async Task Journal_cannot_claim_or_delete_an_ancestor_directory()
    {
        var initial = await CreateProvisioner(new StubEnvironment(), new StubHelper())
            .PreviewAsync(Request, CancellationToken.None);
        var journalPath = Path.Combine(
            Request.FolderPath,
            WindowsCircleFilesSystemOperations.JournalFileName);
        var journal = System.Text.Json.JsonSerializer.Serialize(new
        {
            ContractVersion = 1,
            initial.OwnershipId,
            initial.PlanId,
            FolderPath = Request.FolderPath,
            OwnerSid = "S-1-5-21-1000",
            TargetExisted = false,
            PreMutationSddl = "O:S-1-5-21-1000D:",
            CreatedDirectories = new[] { @"C:\BallsShares" },
        }) + "\n";
        var environment = new StubEnvironment
        {
            ExistingDirectory = true,
            Entries = [journalPath],
            Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [journalPath] = journal,
            },
        };

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => CreateProvisioner(environment, new StubHelper())
                .PreviewAsync(Request, CancellationToken.None).AsTask());

        Assert.AreEqual("hosting_folder_not_empty", error.Code);
    }

    [TestMethod]
    public async Task Preview_rejects_a_new_target_whose_parent_does_not_exist()
    {
        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => CreateProvisioner(
                    new StubEnvironment { ParentExists = false },
                    new StubHelper())
                .PreviewAsync(Request, CancellationToken.None).AsTask());

        Assert.AreEqual("hosting_path_invalid", error.Code);
    }

    private static WindowsCircleFilesHostProvisioner CreateProvisioner(
        StubEnvironment environment,
        StubHelper helper) =>
        new(new StubReadiness(CircleFilesReadinessStatus.Ready), environment, helper);

    private sealed class StubReadiness(CircleFilesReadinessStatus status)
        : ICircleFilesReadinessInspector
    {
        public ValueTask<CircleFilesReadinessReport> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new CircleFilesReadinessReport(
                    CircleFilesReadinessProviders.WindowsSmb311,
                    status,
                    []));
        }
    }

    private sealed class StubHelper : IWindowsCircleFilesHelperClient
    {
        public int CallCount { get; private set; }

        public WindowsCircleFilesHelperPlan? LastPlan { get; private set; }

        public ValueTask<CircleFilesHostApplyStatus> ApplyAsync(
            WindowsCircleFilesHelperPlan plan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastPlan = plan;
            return ValueTask.FromResult(CircleFilesHostApplyStatus.Applied);
        }
    }

    private static CircleFilesHostRequest CreateAuthorizedRequest(string displayName = "Company files")
    {
        var circleId = new CircleId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8901"));
        var contributionId = new CircleFilesContributionId(
            Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8902"));
        var providerId = new CircleFilesProviderId(
            Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8903"));
        var nodeId = new NodeId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8904"));
        var authorizedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var unsignedAuthorization = new CircleFilesOwnerAuthorization(
            new MemberId(Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8905")),
            1,
            authorizedAt,
            [],
            [],
            []);
        var contribution = new CircleFilesContribution(
            contributionId,
            circleId,
            new CircleFilesProviderIdentity(providerId, nodeId),
            displayName,
            CircleFilesContributionLifecycle.Defined,
            1,
            authorizedAt,
            unsignedAuthorization);
        var transcript = CircleFilesAuthorizationTranscript.EncodeContribution(
            new CircleFilesContributionRequestId(
                Guid.Parse("019d2a6b-1b66-7d38-9c35-8d64ca8f8906")),
            contribution);
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var memberSignature = IdentityCryptography.Sign(transcript, memberKey);
        var rootSignature = IdentityCryptography.Sign(transcript, rootKey);
        var proof = new CircleFilesHostAuthorizationProof(
            transcript,
            memberSignature,
            rootSignature,
            ToHostCredential(IdentityCryptography.CreateCredential(IdentityKeyRole.Member, memberKey)),
            ToHostCredential(IdentityCryptography.CreateCredential(
                IdentityKeyRole.CircleAuthority,
                rootKey)));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, transcript);
        Append(hash, memberSignature);
        Append(hash, rootSignature);
        return new CircleFilesHostRequest(
            circleId.ToString(),
            contributionId.ToString(),
            providerId.ToString(),
            nodeId.ToString(),
            contribution.DisplayName,
            @"C:\BallsShares\Company",
            Convert.ToHexStringLower(hash.GetHashAndReset()),
            proof);
    }

    private static CircleFilesHostPublicCredential ToHostCredential(
        PublicIdentityCredential credential) =>
        new(
            credential.Role == IdentityKeyRole.Member ? "member" : "circle-authority",
            credential.Algorithm,
            credential.KeyId,
            credential.SubjectPublicKeyInfo);

    private static void Append(IncrementalHash hash, byte[] value)
    {
        hash.AppendData(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value.Length)));
        hash.AppendData(value);
    }

    private sealed class StubEnvironment : IWindowsCircleFilesPathEnvironment
    {
        public string CurrentUserSid => "S-1-5-21-1000";

        public bool FixedDrive { get; init; } = true;

        public bool FileAtTarget { get; init; }

        public bool ExistingDirectory { get; init; }

        public bool ParentExists { get; init; } = true;

        public bool HasReparsePoint { get; init; }

        public IReadOnlyList<string> Entries { get; init; } = [];

        public string? MarkerContent { get; init; }

        public IReadOnlyDictionary<string, string> Files { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> RefusedRoots => [@"C:\Users", @"C:\Windows", @"C:\Program Files", @"C:\ProgramData"];

        public string GetFullPath(string path) => path;

        public string GetPathRoot(string path) => path[..3];

        public bool IsFixedLocalDrive(string root) => FixedDrive;

        public bool FileExists(string path) => FileAtTarget;

        public bool DirectoryExists(string path) =>
            path.Equals(Request.FolderPath, StringComparison.OrdinalIgnoreCase)
                ? ExistingDirectory
                : ParentExists;

        public bool HasReparsePointInExistingPath(string path) => HasReparsePoint;

        public IReadOnlyList<string> EnumerateEntries(string path) => Entries;

        public string? ReadAllText(string path) =>
            Files.GetValueOrDefault(path) ?? MarkerContent;
    }
}
