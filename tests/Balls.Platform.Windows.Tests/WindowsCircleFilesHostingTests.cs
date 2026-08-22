using System.Runtime.Versioning;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesHostingTests
{
    private static readonly CircleFilesHostRequest Request = new(
        "019d2a6b-1b66-7d38-9c35-8d64ca8f8901",
        "019d2a6b-1b66-7d38-9c35-8d64ca8f8902",
        "019d2a6b-1b66-7d38-9c35-8d64ca8f8903",
        "019d2a6b-1b66-7d38-9c35-8d64ca8f8904",
        "Company files",
        @"C:\BallsShares\Company",
        new string('a', 64));

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
        Assert.IsFalse(script.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("ScriptBlock", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("-Profile Public", StringComparison.OrdinalIgnoreCase));
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

    private sealed class StubEnvironment : IWindowsCircleFilesPathEnvironment
    {
        public string CurrentUserSid => "S-1-5-21-1000";

        public bool FixedDrive { get; init; } = true;

        public bool FileAtTarget { get; init; }

        public bool ExistingDirectory { get; init; }

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

        public bool DirectoryExists(string path) => ExistingDirectory;

        public bool HasReparsePointInExistingPath(string path) => HasReparsePoint;

        public IReadOnlyList<string> EnumerateEntries(string path) => Entries;

        public string? ReadAllText(string path) =>
            Files.GetValueOrDefault(path) ?? MarkerContent;
    }
}
