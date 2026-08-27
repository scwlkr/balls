using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Balls.Platform;

public sealed record RevitServerPackageIdentity(
    string Tag,
    string Commit,
    string Name,
    string Version,
    string Sha256);

public interface IRevitServerPackageIdentitySource
{
    RevitServerPackageIdentity Load();
}

public sealed record RevitServerHandoffRequest(
    RevitServerPackageIdentity BallsPackage,
    RevitServerSetupPlan Plan,
    IReadOnlyList<RevitServerHealthCheck> HealthChecks,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    TimeSpan WallClockElapsed,
    TimeSpan HumanInterventionElapsed,
    IReadOnlyList<string> HumanInterventions,
    string Outcome);

public sealed record RevitServerHandoffBundle(
    byte[] Content,
    string Sha256,
    string Outcome,
    TimeSpan WallClockElapsed);

public static partial class RevitServerHandoffBundleFactory
{
    public const string FileName = "revit-server-2027-setup-bundle.zip";
    public const string PassClaim =
        "PASS — Revit Server 2027 Host+Admin installation and Administrator surface are healthy in the disposable QEMU/KVM lab. Revit client/model use, synchronization, concurrency, performance, backup, recovery, remote access, and production hardware were not tested.";
    public static readonly TimeSpan MaximumPassingElapsed = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static readonly IReadOnlyList<string> UntestedScenarios =
    [
        "Revit client/model use",
        "synchronization",
        "concurrency",
        "performance",
        "backup",
        "recovery",
        "remote access",
        "production hardware",
    ];

    public static RevitServerHandoffBundle Create(RevitServerHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var template = new SetupTemplateDocument(
            1,
            new VersionConstraints("Windows Server 2022 Desktop Experience", "Revit Server 2027"),
            new RolePolicy(["Host", "Admin"], ["Accelerator"]),
            new PortableStorageLayout("fixed NTFS data volume", ["2027/Projects", "2027/Cache"]),
            request.Plan.WindowsPrerequisites,
            [
                "NETWORK SERVICE: Full control inherited by children",
                "CREATOR OWNER: Full control on created children",
            ],
            new ExpectedMedia(
                request.Plan.MediaPublisher,
                request.Plan.MediaProduct,
                request.Plan.MediaVersion,
                request.Plan.MediaSha256),
            "private-lan-only; LocalSubnet on the Private profile; no Public exposure",
            request.Plan.VerificationActions,
            [
                "Automatic bundle import or machine-plan replay",
                .. UntestedScenarios,
                "Autodesk-supported hypervisor or production certification",
            ]);

        var machineFingerprint = HashUtf8(
            $"revit-proof-v1\n{request.Plan.Machine.Trim().ToUpperInvariant()}\n{request.Plan.Windows.Trim()}");
        var outcome = request.Outcome.ToUpperInvariant();
        var receipt = new SetupReceiptDocument(
            1,
            outcome == "PASS" ? PassClaim : null,
            request.BallsPackage,
            new AutodeskReceipt(
                request.Plan.MediaPublisher,
                request.Plan.MediaProduct,
                request.Plan.MediaVersion,
                request.Plan.MediaFileName,
                request.Plan.MediaSha256,
                "Windows Authenticode signature verified before setup"),
            new WindowsReceipt(request.Plan.Windows, machineFingerprint),
            new ProofReceipt(
                request.Plan.PlanDigest,
                request.Plan.BallsOwnedState,
                request.Plan.MediaProduct,
                request.Plan.MediaVersion,
                ["Host", "Admin"],
                ["Accelerator"],
                request.HealthChecks.Select(check => new HealthReceipt(
                    check.Id,
                    check.Code,
                    check.Status == RevitServerHealthStatus.Healthy ? "healthy" : "not-healthy")).ToArray()),
            new TemporaryEvidence(
                request.Plan.Machine,
                request.Plan.DataPaths[0],
                request.Plan.DataPaths[1],
                request.Plan.DataPaths[2],
                true),
            new TimingReceipt(
                request.StartedAtUtc,
                request.EndedAtUtc,
                decimal.Round((decimal)request.WallClockElapsed.TotalSeconds, 3),
                decimal.Round((decimal)request.HumanInterventionElapsed.TotalSeconds, 3),
                "upper-bound awaiting-Autodesk window",
                request.HumanInterventions),
            outcome,
            UntestedScenarios);

        var readme = BuildReadme();
        var members = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["setup-template.v1.json"] = Serialize(template),
            ["setup-receipt.v1.json"] = Serialize(receipt),
            ["README.md"] = Encoding.UTF8.GetBytes(readme),
        };
        var manifest = new BundleManifestDocument(
            1,
            "revit-server-2027-setup-bundle-v1",
            members.OrderBy(member => member.Key, StringComparer.Ordinal)
                .Select(member => new BundleMember(
                    member.Key,
                    member.Key.EndsWith(".json", StringComparison.Ordinal) ? 1 : null,
                    member.Value.Length,
                    Hash(member.Value)))
                .ToArray());
        members["bundle-manifest.json"] = Serialize(manifest);

        byte[] content;
        using (var output = new MemoryStream())
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var member in members.OrderBy(member => member.Key, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(member.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var stream = entry.Open();
                    stream.Write(member.Value);
                }
            }
            content = output.ToArray();
        }

        RevitServerHandoffBundleValidator.Validate(content);
        return new RevitServerHandoffBundle(content, Hash(content), outcome, request.WallClockElapsed);
    }

    private static void ValidateRequest(RevitServerHandoffRequest request)
    {
        if (!DevelopmentTagPattern().IsMatch(request.BallsPackage.Tag)
            || !CommitPattern().IsMatch(request.BallsPackage.Commit)
            || !request.BallsPackage.Tag.EndsWith(request.BallsPackage.Commit[..12], StringComparison.Ordinal)
            || !PackageNamePattern().IsMatch(request.BallsPackage.Name)
            || !request.BallsPackage.Name.EndsWith($"-{request.BallsPackage.Commit[..12]}.zip", StringComparison.Ordinal)
            || !VersionPattern().IsMatch(request.BallsPackage.Version)
            || !Sha256Pattern().IsMatch(request.BallsPackage.Sha256)
            || !Sha256Pattern().IsMatch(request.Plan.MediaSha256)
            || !Sha256Pattern().IsMatch(request.Plan.PlanDigest))
        {
            throw new InvalidDataException("The handoff evidence contains an invalid immutable identity.");
        }
        if (request.EndedAtUtc < request.StartedAtUtc
            || request.WallClockElapsed < TimeSpan.Zero
            || request.HumanInterventionElapsed < TimeSpan.Zero
            || request.HumanInterventionElapsed > request.WallClockElapsed)
        {
            throw new InvalidDataException("The handoff timing evidence is invalid.");
        }
        var expectedOutcome = request.WallClockElapsed < MaximumPassingElapsed ? "PASS" : "FAILED";
        if (!string.Equals(request.Outcome, expectedOutcome, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The handoff outcome does not match the strict setup timer.");
        }
        if (request.HealthChecks.Count == 0
            || request.HealthChecks.Any(check => check.Status != RevitServerHealthStatus.Healthy)
            || !request.Plan.EnabledRoles.SequenceEqual(["Host", "Admin"], StringComparer.Ordinal)
            || !request.Plan.ForbiddenRoles.SequenceEqual(["Accelerator"], StringComparer.Ordinal))
        {
            throw new InvalidDataException("A handoff requires exact healthy Host and Admin evidence.");
        }
        if (request.Plan.DataPaths.Count != 3)
        {
            throw new InvalidDataException("The approved data-path evidence is incomplete.");
        }
    }

    private static byte[] Serialize<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions) + "\n");

    private static string BuildReadme() =>
        """
        # Revit Server 2027 setup handoff

        This bundle records a disposable-lab installation-health proof. It is not an installer,
        machine backup, or configuration-import package.

        On the future physical server:

        1. Install Windows Server 2022 Desktop Experience and attach a fixed local NTFS data volume.
        2. Install the exact Balls Development package through balls.wlkrlabs.com and open **Set up Revit Server 2027**.
        3. Select locally cached official Autodesk media and require a fresh Ready inspection.
        4. Compare the new target-specific preview with `setup-template.v1.json`; approve Host + Admin with Accelerator off.
        5. Personally accept Autodesk's terms, confirm its configuration page, install, verify, and export a new receipt.

        Do not copy or replay the lab machine's Host identity, paths, plan digest, network bindings,
        ACL identities, or firewall state. Balls must resolve and approve the physical server's own
        hostname, fixed volume, paths, principals, media, and private-LAN policy.

        `setup-receipt.v1.json` is evidence of what worked temporarily. It does not prove Revit
        model use, synchronization, concurrency, performance, backup, recovery, remote access,
        production hardware, or Autodesk-supported virtualization.
        """ + "\n";

    private static string Hash(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static string HashUtf8(string value) => Hash(Encoding.UTF8.GetBytes(value));

    [GeneratedRegex("^development-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex DevelopmentTagPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^balls-[0-9A-Za-z.-]+-canary-windows-x64-[0-9a-f]{12}\\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageNamePattern();

    [GeneratedRegex("^[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

public static partial class RevitServerHandoffBundleValidator
{
    private const int MaximumBundleBytes = 512 * 1024;
    private static readonly string[] ExactNames =
    [
        "README.md",
        "bundle-manifest.json",
        "setup-receipt.v1.json",
        "setup-template.v1.json",
    ];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static void Validate(ReadOnlySpan<byte> bundle)
    {
        if (bundle.Length is 0 or > MaximumBundleBytes)
        {
            throw new InvalidDataException("The handoff bundle is outside its size limit.");
        }

        var members = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var input = new MemoryStream(bundle.ToArray(), writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (!ExactNames.Contains(entry.FullName, StringComparer.Ordinal)
                || entry.Length is <= 0 or > 256 * 1024
                || !members.TryAdd(entry.FullName, ReadEntry(entry)))
            {
                throw new InvalidDataException("The handoff bundle contains an unexpected or duplicate member.");
            }
        }
        if (!members.Keys.Order(StringComparer.Ordinal).SequenceEqual(ExactNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The handoff bundle is incomplete.");
        }

        var manifest = Deserialize<BundleManifestDocument>(members["bundle-manifest.json"]);
        if (manifest.SchemaVersion != 1
            || manifest.BundleSchema != "revit-server-2027-setup-bundle-v1"
            || manifest.Files.Count != 3)
        {
            throw new InvalidDataException("The bundle manifest schema is invalid.");
        }
        foreach (var item in manifest.Files)
        {
            if (item.Name == "bundle-manifest.json"
                || !members.TryGetValue(item.Name, out var bytes)
                || item.ByteLength != bytes.Length
                || !FixedEquals(item.Sha256, Hash(bytes))
                || (item.Name.EndsWith(".json", StringComparison.Ordinal) && item.SchemaVersion != 1)
                || (item.Name == "README.md" && item.SchemaVersion is not null))
            {
                throw new InvalidDataException("A bundle member does not match its manifest identity.");
            }
        }
        if (!manifest.Files.Select(item => item.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(ExactNames.Where(name => name != "bundle-manifest.json"), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The bundle manifest does not name every other member exactly once.");
        }

        var template = Deserialize<SetupTemplateDocument>(members["setup-template.v1.json"]);
        var receipt = Deserialize<SetupReceiptDocument>(members["setup-receipt.v1.json"]);
        var readme = StrictUtf8(members["README.md"]);
        ValidateTemplate(template);
        ValidateReceipt(receipt);
        ValidateTextExclusions(JsonSerializer.Serialize(template, Options), portableOnly: true);
        ValidateTextExclusions(JsonSerializer.Serialize(receipt, Options), portableOnly: false);
        ValidateTextExclusions(readme, portableOnly: true);
    }

    private static void ValidateTemplate(SetupTemplateDocument value)
    {
        if (value.SchemaVersion != 1
            || value.Constraints != new VersionConstraints("Windows Server 2022 Desktop Experience", "Revit Server 2027")
            || !value.Roles.Enabled.SequenceEqual(["Host", "Admin"], StringComparer.Ordinal)
            || !value.Roles.Forbidden.SequenceEqual(["Accelerator"], StringComparer.Ordinal)
            || value.Storage.VolumeConstraint != "fixed NTFS data volume"
            || !value.Storage.RelativePaths.SequenceEqual(["2027/Projects", "2027/Cache"], StringComparer.Ordinal)
            || value.WindowsPrerequisites.Count == 0
            || value.PortableAclPrincipals.Count != 2
            || value.ExpectedOfficialMedia.Publisher != "Autodesk, Inc."
            || value.ExpectedOfficialMedia.Product != "Autodesk Revit Server 2027"
            || value.ExpectedOfficialMedia.Version != "27.0.4.412"
            || !Sha256Pattern().IsMatch(value.ExpectedOfficialMedia.Sha256)
            || value.PrivateLanPolicy != "private-lan-only; LocalSubnet on the Private profile; no Public exposure"
            || value.HealthChecks.Count == 0
            || value.NonGoals.Count == 0)
        {
            throw new InvalidDataException("The setup template is incomplete or non-portable.");
        }
    }

    private static void ValidateReceipt(SetupReceiptDocument value)
    {
        var pass = value.Outcome == "PASS";
        if (value.SchemaVersion != 1
            || (pass && value.Claim != RevitServerHandoffBundleFactory.PassClaim)
            || (!pass && value.Claim is not null)
            || value.Outcome is not ("PASS" or "FAILED")
            || !DevelopmentTagPattern().IsMatch(value.BallsPackage.Tag)
            || !CommitPattern().IsMatch(value.BallsPackage.Commit)
            || !value.BallsPackage.Tag.EndsWith(value.BallsPackage.Commit[..12], StringComparison.Ordinal)
            || !PackageNamePattern().IsMatch(value.BallsPackage.Name)
            || !value.BallsPackage.Name.EndsWith($"-{value.BallsPackage.Commit[..12]}.zip", StringComparison.Ordinal)
            || !VersionPattern().IsMatch(value.BallsPackage.Version)
            || !Sha256Pattern().IsMatch(value.BallsPackage.Sha256)
            || value.Autodesk.Publisher != "Autodesk, Inc."
            || value.Autodesk.Product != "Autodesk Revit Server 2027"
            || value.Autodesk.Version != "27.0.4.412"
            || value.Autodesk.FileName != "Revit_Server_2027_win_db.sfx.exe"
            || value.Autodesk.Signature != "Windows Authenticode signature verified before setup"
            || !Sha256Pattern().IsMatch(value.Autodesk.Sha256)
            || !value.Windows.EditionAndBuild.Contains("Windows Server 2022", StringComparison.Ordinal)
            || !Sha256Pattern().IsMatch(value.Windows.MachineFingerprint)
            || !Sha256Pattern().IsMatch(value.Proof.ApprovedPlanDigest)
            || !value.Proof.InstalledRoles.SequenceEqual(["Host", "Admin"], StringComparer.Ordinal)
            || !value.Proof.ForbiddenRoles.SequenceEqual(["Accelerator"], StringComparer.Ordinal)
            || value.Proof.InstalledProduct != "Autodesk Revit Server 2027"
            || value.Proof.InstalledVersion != "27.0.4.412"
            || value.Proof.BallsOwnedChanges.Count == 0
            || value.Proof.HealthStates.Count == 0
            || value.Proof.HealthStates.Any(check => check.Status != "healthy")
            || !value.TemporaryEvidence.ReplayProhibited
            || value.TemporaryEvidence.RepositoryRoot != @"D:\RevitServer\2027"
            || value.TemporaryEvidence.ProjectsPath != @"D:\RevitServer\2027\Projects"
            || value.TemporaryEvidence.CachePath != @"D:\RevitServer\2027\Cache"
            || value.Timing.EndedAtUtc < value.Timing.StartedAtUtc
            || value.Timing.WallClockSeconds < 0
            || value.Timing.HumanInterventionSeconds < 0
            || value.Timing.HumanInterventionSeconds > value.Timing.WallClockSeconds
            || value.Timing.HumanInterventionMeasurement != "upper-bound awaiting-Autodesk window"
            || (pass && value.Timing.WallClockSeconds >= 1800)
            || (!pass && value.Timing.WallClockSeconds < 1800)
            || value.Timing.HumanInterventions.Count == 0
            || !value.UntestedScenarios.SequenceEqual(RevitServerHandoffBundleFactory.UntestedScenarios, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The setup receipt is incomplete or inconsistent.");
        }
    }

    private static void ValidateTextExclusions(string value, bool portableOnly)
    {
        if (ForbiddenSensitivePattern().IsMatch(value))
        {
            throw new InvalidDataException("The handoff bundle contains excluded private material.");
        }
        if (SidPattern().IsMatch(value))
        {
            throw new InvalidDataException("The handoff bundle contains an excluded Windows SID.");
        }
        if (IpAddressPattern().IsMatch(value))
        {
            throw new InvalidDataException("The handoff bundle contains an excluded IP address.");
        }
        if (ExecutablePayloadPattern().IsMatch(value))
        {
            throw new InvalidDataException("The handoff bundle contains excluded payload material.");
        }
        if (portableOnly && (WindowsPathPattern().IsMatch(value) || HostIdentityPattern().IsMatch(value)))
        {
            throw new InvalidDataException("A portable handoff document contains machine-specific replay material.");
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        stream.CopyTo(output);
        if (output.Length != entry.Length)
        {
            throw new InvalidDataException("A bundle member is truncated.");
        }
        return output.ToArray();
    }

    private static T Deserialize<T>(byte[] bytes) =>
        JsonSerializer.Deserialize<T>(StrictUtf8(bytes), Options)
        ?? throw new InvalidDataException("A bundle document is empty.");

    private static string StrictUtf8(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("A bundle document is not UTF-8.", exception);
        }
    }

    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private static bool FixedEquals(string? left, string right)
    {
        if (left is null || left.Length != right.Length)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    }

    [GeneratedRegex("(?i)password|credential|private[ _-]?circle|circle[ _-]?(?:id|material)|access[ _-]?token|secret|company[ _-]?(?:data|model)|model[ _-]?data|vm[ _-]?image")]
    private static partial Regex ForbiddenSensitivePattern();

    [GeneratedRegex(@"(?i)S-1-[0-9]+(?:-[0-9]+){1,}")]
    private static partial Regex SidPattern();

    [GeneratedRegex(@"(?<![0-9])(?:(?:25[0-5]|2[0-4][0-9]|1?[0-9]{1,2})\.){3}(?:25[0-5]|2[0-4][0-9]|1?[0-9]{1,2})(?![0-9])")]
    private static partial Regex IpAddressPattern();

    [GeneratedRegex(@"(?i)(?:payload|content|bytes|base64)\s*[:=]|\.(?:msi|vhdx?|iso)(?:[\""'\s]|$)")]
    private static partial Regex ExecutablePayloadPattern();

    [GeneratedRegex(@"(?i)(?:[A-Z]:\\|\\\\[A-Za-z0-9])")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?i)BALLS-RS[0-9A-Z-]*")]
    private static partial Regex HostIdentityPattern();

    [GeneratedRegex("^development-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex DevelopmentTagPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^balls-[0-9A-Za-z.-]+-canary-windows-x64-[0-9a-f]{12}\\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageNamePattern();

    [GeneratedRegex("^[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

public sealed record SetupTemplateDocument(
    int SchemaVersion,
    VersionConstraints Constraints,
    RolePolicy Roles,
    PortableStorageLayout Storage,
    IReadOnlyList<string> WindowsPrerequisites,
    IReadOnlyList<string> PortableAclPrincipals,
    ExpectedMedia ExpectedOfficialMedia,
    string PrivateLanPolicy,
    IReadOnlyList<string> HealthChecks,
    IReadOnlyList<string> NonGoals);

public sealed record VersionConstraints(string Windows, string RevitServer);
public sealed record RolePolicy(IReadOnlyList<string> Enabled, IReadOnlyList<string> Forbidden);
public sealed record PortableStorageLayout(string VolumeConstraint, IReadOnlyList<string> RelativePaths);
public sealed record ExpectedMedia(string Publisher, string Product, string Version, string Sha256);

public sealed record SetupReceiptDocument(
    int SchemaVersion,
    string? Claim,
    RevitServerPackageIdentity BallsPackage,
    AutodeskReceipt Autodesk,
    WindowsReceipt Windows,
    ProofReceipt Proof,
    TemporaryEvidence TemporaryEvidence,
    TimingReceipt Timing,
    string Outcome,
    IReadOnlyList<string> UntestedScenarios);

public sealed record AutodeskReceipt(
    string Publisher,
    string Product,
    string Version,
    string FileName,
    string Sha256,
    string Signature);
public sealed record WindowsReceipt(string EditionAndBuild, string MachineFingerprint);
public sealed record ProofReceipt(
    string ApprovedPlanDigest,
    IReadOnlyList<string> BallsOwnedChanges,
    string InstalledProduct,
    string InstalledVersion,
    IReadOnlyList<string> InstalledRoles,
    IReadOnlyList<string> ForbiddenRoles,
    IReadOnlyList<HealthReceipt> HealthStates);
public sealed record HealthReceipt(string Id, string Code, string Status);
public sealed record TemporaryEvidence(
    string HostIdentity,
    string RepositoryRoot,
    string ProjectsPath,
    string CachePath,
    bool ReplayProhibited);
public sealed record TimingReceipt(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    decimal WallClockSeconds,
    decimal HumanInterventionSeconds,
    string HumanInterventionMeasurement,
    IReadOnlyList<string> HumanInterventions);
public sealed record BundleManifestDocument(
    int SchemaVersion,
    string BundleSchema,
    IReadOnlyList<BundleMember> Files);
public sealed record BundleMember(string Name, int? SchemaVersion, long ByteLength, string Sha256);
