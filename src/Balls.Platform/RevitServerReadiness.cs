using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Balls.Platform;

public enum RevitServerReadinessStatus
{
    Ready,
    Blocked,
}

public sealed record RevitServerReadinessCheck(
    string Id,
    RevitServerReadinessStatus Status,
    string Code,
    string Summary);

public sealed record RevitServerMediaIdentity(
    string FileName,
    string Publisher,
    string Product,
    string Version,
    string Sha256);

public sealed record RevitServerInspectionSnapshot(
    string MachineName,
    string WindowsEdition,
    int WindowsBuild,
    string InstallationType,
    string DataVolume,
    long DataVolumeFreeBytes,
    string RepositoryRoot,
    string ApprovalSnapshotIdentity,
    RevitServerMediaIdentity Media,
    bool DefaultWebSitePresent,
    IReadOnlyList<string> PresentPrerequisites);

public sealed record RevitServerInspectionReport(
    RevitServerReadinessStatus Status,
    string Summary,
    IReadOnlyList<RevitServerReadinessCheck> Checks,
    RevitServerInspectionSnapshot? Snapshot);

public sealed record RevitServerSetupPlan(
    string PlanDigest,
    string Machine,
    string Windows,
    string Media,
    string MediaSha256,
    IReadOnlyList<string> EnabledRoles,
    IReadOnlyList<string> ForbiddenRoles,
    IReadOnlyList<string> DataPaths,
    IReadOnlyList<string> WindowsPrerequisites,
    IReadOnlyList<string> AclIntent,
    IReadOnlyList<string> DefaultWebSiteEffects,
    IReadOnlyList<string> RsnIni,
    IReadOnlyList<string> FirewallEffects,
    IReadOnlyList<string> VerificationActions,
    IReadOnlyList<string> BallsOwnedState,
    IReadOnlyList<string> AutodeskOwnedState);

public static class RevitServerSetupPlanFactory
{
    public static readonly IReadOnlyList<string> RequiredWindowsPrerequisites =
    [
        "Web Server (IIS) with its default role services",
        ".NET Framework 4.8 Features / ASP.NET 4.8",
        "WCF Services / HTTP Activation",
        "WCF Services / TCP Activation",
        "IIS Application Development / ASP",
        "IIS Application Development / CGI",
        "IIS Application Development / Server Side Includes",
        "IIS 6 Management Compatibility",
        "IIS 6 Scripting Tools",
        "IIS 6 WMI Compatibility",
    ];

    public static RevitServerSetupPlan Create(RevitServerInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var root = snapshot.RepositoryRoot.TrimEnd('\\');
        return new RevitServerSetupPlan(
            ComputeDigest(snapshot),
            snapshot.MachineName,
            $"{snapshot.WindowsEdition} build {snapshot.WindowsBuild} ({snapshot.InstallationType})",
            $"{snapshot.Media.Publisher} — {snapshot.Media.Product} {snapshot.Media.Version} ({snapshot.Media.FileName})",
            snapshot.Media.Sha256,
            ["Host", "Admin"],
            ["Accelerator"],
            [root, $"{root}\\Projects", $"{root}\\Cache"],
            RequiredWindowsPrerequisites,
            [
                @"NETWORK SERVICE: Full control on Projects and Cache, inherited by children",
                @"CREATOR OWNER: Full control on Projects and Cache children",
                "Resolve portable principals on the target; store no machine-specific SID",
            ],
            snapshot.DefaultWebSitePresent
                ? ["Keep the existing unambiguous Default Web Site and its local HTTP binding"]
                : ["Create Default Web Site with its standard local HTTP binding"],
            [$@"Write {snapshot.MachineName} as the only line in C:\ProgramData\Autodesk\Revit Server 2027\Config\RSN.ini"],
            [
                "Allow Revit Server HTTP on TCP 80 and TCP 808 from LocalSubnet on the Private profile only",
                "Allow ICMPv4 echo from LocalSubnet on the Private profile only",
                "Create no Public-profile or public-host exposure",
            ],
            [
                "Re-inspect the machine and media and require this plan digest",
                "Verify product version 2027 and exactly Host + Admin",
                "Verify Accelerator and RSACCELERATOR2027 are absent",
                "Verify Projects, Cache, ACLs, Default Web Site, IIS applications, and application pool",
                "Verify local endpoints, server-local RSN.ini, Administrator page, and logs",
                "Verify the repository is not shared, mounted, or reached through a reparse point",
            ],
            [
                "Documented Windows/IIS prerequisites added by the approved setup",
                "Version-isolated empty data folders and their portable-principal ACLs",
                "Server-local RSN.ini and narrowly scoped Private/LocalSubnet firewall rules",
                "Redacted plan and setup audit records",
            ],
            [
                "Autodesk installer, license acceptance, installed product, services, IIS applications, and repository content",
                "Balls will not silently uninstall Autodesk software or delete or repair ambiguous Autodesk data",
            ]);
    }

    public static string ComputeDigest(RevitServerInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var canonical = JsonSerializer.Serialize(new
        {
            schema = "revit-server-setup-plan-v1",
            snapshot.MachineName,
            snapshot.WindowsEdition,
            snapshot.WindowsBuild,
            snapshot.InstallationType,
            snapshot.DataVolume,
            snapshot.DataVolumeFreeBytes,
            snapshot.RepositoryRoot,
            snapshot.ApprovalSnapshotIdentity,
            media = snapshot.Media,
            snapshot.DefaultWebSitePresent,
            presentPrerequisites = snapshot.PresentPrerequisites.Order(StringComparer.Ordinal).ToArray(),
            enabledRoles = new[] { "Host", "Admin" },
            forbiddenRoles = new[] { "Accelerator" },
            requiredPrerequisites = RequiredWindowsPrerequisites,
            firewallPolicy = "private-local-subnet-tcp80-tcp808-icmpv4-no-public",
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public interface IRevitServerReadinessInspector
{
    ValueTask<RevitServerInspectionReport> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken);
}

public sealed record RevitServerMediaSelection(string Path, string FileName);

public interface IRevitServerMediaPicker
{
    ValueTask<RevitServerMediaSelection?> SelectAsync(CancellationToken cancellationToken);
}

public sealed class UnsupportedRevitServerMediaPicker : IRevitServerMediaPicker
{
    public ValueTask<RevitServerMediaSelection?> SelectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Choosing Autodesk media is available only on Windows.");
    }
}

public sealed class UnsupportedRevitServerReadinessInspector : IRevitServerReadinessInspector
{
    public ValueTask<RevitServerInspectionReport> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new RevitServerInspectionReport(
                RevitServerReadinessStatus.Blocked,
                "Revit Server setup requires Windows Server 2022 Desktop Experience.",
                [
                    new RevitServerReadinessCheck(
                        "windows-server",
                        RevitServerReadinessStatus.Blocked,
                        "windows_server_2022_required",
                        "Open this setup on a prepared Windows Server 2022 Desktop Experience Node."),
                ],
                null));
    }
}
