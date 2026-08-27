using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class WindowsRevitServerReadinessInspectorTests
{
    [TestMethod]
    public void Supported_server_and_official_media_produce_the_exact_read_only_plan()
    {
        var report = ReadyReport();
        var plan = RevitServerSetupPlanFactory.Create(report.Snapshot!);

        Assert.AreEqual(RevitServerReadinessStatus.Ready, report.Status);
        CollectionAssert.AreEqual(new[] { "Host", "Admin" }, plan.EnabledRoles.ToArray());
        CollectionAssert.AreEqual(new[] { "Accelerator" }, plan.ForbiddenRoles.ToArray());
        CollectionAssert.Contains(plan.DataPaths.ToArray(), @"D:\RevitServer\2027\Projects");
        CollectionAssert.Contains(plan.DataPaths.ToArray(), @"D:\RevitServer\2027\Cache");
        Assert.IsTrue(plan.AclIntent.Any(value => value.Contains("NETWORK SERVICE", StringComparison.Ordinal)));
        Assert.IsTrue(plan.AclIntent.Any(value => value.Contains("CREATOR OWNER", StringComparison.Ordinal)));
        Assert.IsTrue(plan.FirewallEffects.Any(value => value.Contains("TCP 80 and TCP 808", StringComparison.Ordinal)));
        Assert.IsTrue(plan.FirewallEffects.Any(value => value.Contains("ICMPv4", StringComparison.Ordinal)));
        Assert.IsTrue(plan.RsnIni.Single().Contains("BALLS-RS27", StringComparison.Ordinal));
        Assert.AreEqual("Autodesk Revit Server 2027", report.Snapshot!.Media.Product);
        Assert.AreEqual("27.0.4.412", report.Snapshot.Media.Version);
        StringAssert.Contains(
            report.Checks.Single(check => check.Id == "installer").Summary,
            "trusted Autodesk Revit Server 2027 media catalog entry");
        Assert.AreEqual(64, plan.PlanDigest.Length);
    }

    [TestMethod]
    [DataRow("Microsoft Windows Server 2022 Standard", 20348, "Server Core", 3, "server_core_unsupported")]
    [DataRow("Microsoft Windows 11 Pro", 26100, "Client", 1, "windows_client_unsupported")]
    [DataRow("Microsoft Windows Server 2019 Standard", 17763, "Server", 3, "windows_server_build_unsupported")]
    [DataRow("Microsoft Windows Server 2025 Standard", 26100, "Server", 3, "windows_server_build_unsupported")]
    public void Unsupported_operating_systems_are_blocked(
        string caption,
        int build,
        string installationType,
        int productType,
        string expectedCode)
    {
        var value = ReadyObservation() with
        {
            System = new WindowsRevitSystemObservation(caption, build, installationType, productType),
        };

        var report = WindowsRevitServerReadinessInspector.Evaluate(value, MediaPath);

        Assert.AreEqual(RevitServerReadinessStatus.Blocked, report.Status);
        Assert.IsNull(report.Snapshot);
        Assert.AreEqual(expectedCode, report.Checks.Single(check => check.Id == "windows-server").Code);
    }

    [TestMethod]
    public void Pending_restart_is_blocked()
    {
        var report = WindowsRevitServerReadinessInspector.Evaluate(
            ReadyObservation() with { PendingRestart = true },
            MediaPath);

        Assert.AreEqual("restart_required", report.Checks.Single(check => check.Id == "pending-restart").Code);
        Assert.IsNull(report.Snapshot);
    }

    [TestMethod]
    public void Pending_hostname_rename_is_blocked_until_the_final_name_is_active()
    {
        var report = WindowsRevitServerReadinessInspector.Evaluate(
            ReadyObservation() with { PendingHostnameRename = true },
            MediaPath);

        Assert.AreEqual("hostname_restart_pending", report.Checks.Single(check => check.Id == "hostname").Code);
    }

    [TestMethod]
    [DataRow(true, false, 0, 0, "repository_not_empty")]
    [DataRow(false, true, 0, 0, "repository_reparse_path")]
    [DataRow(false, false, 1, 0, "repository_exposed")]
    [DataRow(false, false, 0, 1, "repository_exposed")]
    public void Foreign_reparse_shared_and_mounted_destinations_are_blocked(
        bool nonEmpty,
        bool reparse,
        int shares,
        int mounts,
        string expectedCode)
    {
        var value = ReadyObservation() with
        {
            Repository = new WindowsRevitRepositoryObservation(nonEmpty, reparse, shares, mounts),
        };

        var report = WindowsRevitServerReadinessInspector.Evaluate(value, MediaPath);

        Assert.IsTrue(report.Checks.Any(check => check.Code == expectedCode));
        Assert.AreEqual(RevitServerReadinessStatus.Blocked, report.Status);
    }

    [TestMethod]
    public void Missing_default_site_and_prerequisites_remain_a_ready_plan_action()
    {
        var value = ReadyObservation() with
        {
            Iis = new WindowsRevitIisObservation(false, 0, 0, []),
        };

        var report = WindowsRevitServerReadinessInspector.Evaluate(value, MediaPath);
        var plan = RevitServerSetupPlanFactory.Create(report.Snapshot!);

        Assert.AreEqual(RevitServerReadinessStatus.Ready, report.Status);
        StringAssert.Contains(plan.DefaultWebSiteEffects.Single(), "Create Default Web Site");
        Assert.IsTrue(plan.WindowsPrerequisites.Count >= 10);
    }

    [TestMethod]
    public void Public_network_or_exposure_is_refused()
    {
        var value = ReadyObservation() with
        {
            Network = new WindowsRevitNetworkObservation(1, 1, 2, true, true, "Block", "Block"),
        };

        var report = WindowsRevitServerReadinessInspector.Evaluate(value, MediaPath);

        Assert.AreEqual("public_network_refused", report.Checks.Single(check => check.Id == "network").Code);
    }

    [TestMethod]
    public void Unsafe_firewall_defaults_and_ambiguous_iis_bindings_are_blocked()
    {
        var value = ReadyObservation() with
        {
            Network = new WindowsRevitNetworkObservation(1, 0, 0, true, false, "Allow", "Block"),
            Iis = new WindowsRevitIisObservation(true, 0, 1, ["Web-Server"]),
        };

        var report = WindowsRevitServerReadinessInspector.Evaluate(value, MediaPath);

        Assert.AreEqual("firewall_boundary_unsafe", report.Checks.Single(check => check.Id == "network").Code);
        Assert.AreEqual("iis_default_site_conflict", report.Checks.Single(check => check.Id == "iis").Code);
    }

    [TestMethod]
    public void Foreign_revit_registry_programdata_or_iis_state_is_blocked()
    {
        var report = WindowsRevitServerReadinessInspector.Evaluate(
            ReadyObservation() with { RevitState = new WindowsRevitStateObservation([], 1) },
            MediaPath);

        Assert.AreEqual("foreign_revit_state", report.Checks.Single(check => check.Id == "foreign-state").Code);
    }

    [TestMethod]
    public void Reparse_nonfixed_unstable_or_hash_substituted_media_is_blocked()
    {
        var media = ReadyObservation().Media!;
        var variants = new[]
        {
            media with { LocalFixed = false },
            media with { ReparseTraversal = true },
            media with { StableIdentity = false },
            media with { Sha256 = new string('c', 64) },
        };

        foreach (var variant in variants)
        {
            var report = WindowsRevitServerReadinessInspector.Evaluate(
                ReadyObservation() with { Media = variant },
                MediaPath);
            Assert.AreEqual(RevitServerReadinessStatus.Blocked, report.Status);
        }
    }

    [TestMethod]
    public void Substituted_or_ambiguous_media_is_refused()
    {
        var value = ReadyObservation() with
        {
            Media = ReadyObservation().Media! with
            {
                FileName = "setup.exe",
                SignerName = "Example Publisher",
                ProductName = "Example Product",
            },
        };

        var report = WindowsRevitServerReadinessInspector.Evaluate(value, @"C:\Media\setup.exe");

        Assert.AreEqual(RevitServerReadinessStatus.Blocked, report.Status);
        Assert.IsTrue(report.Checks.Single(check => check.Id == "installer").Code is
            "installer_signature_untrusted" or "installer_identity_substituted");
    }

    [TestMethod]
    public void Every_approval_relevant_drift_replaces_the_plan_digest()
    {
        var original = ReadyReport().Snapshot!;
        var digest = RevitServerSetupPlanFactory.ComputeDigest(original);
        var variants = new[]
        {
            original with { MachineName = "BALLS-RS28" },
            original with { WindowsBuild = 20349 },
            original with { DataVolumeFreeBytes = original.DataVolumeFreeBytes - 1 },
            original with { ApprovalSnapshotIdentity = "changed-observation" },
            original with { Media = original.Media with { Sha256 = new string('c', 64) } },
            original with { DefaultWebSitePresent = false },
            original with { PresentPrerequisites = ["changed"] },
        };

        foreach (var variant in variants)
        {
            Assert.AreNotEqual(digest, RevitServerSetupPlanFactory.ComputeDigest(variant));
        }
    }

    [TestMethod]
    public void Inspection_script_is_closed_and_read_only()
    {
        var script = WindowsRevitServerPowerShellSource.Script;

        foreach (var forbidden in new[]
                 {
                     "Set-", "New-", "Remove-", "Enable-", "Disable-", "Invoke-Expression", "Win32_Product",
                 })
        {
            Assert.IsFalse(script.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
        StringAssert.Contains(script, "Get-AuthenticodeSignature");
        StringAssert.Contains(script, "Get-FileHash");
        StringAssert.Contains(script, "BALLS_REVIT_MEDIA_B64");
        StringAssert.Contains(script, "Get-Partition");
        StringAssert.Contains(script, "Get-NetFirewallPortFilter");
        StringAssert.Contains(script, "Get-WebApplication");
        StringAssert.Contains(script, "NET-WCF-TCP-Activation45");
        StringAssert.Contains(script, "$beforeLength");
        StringAssert.Contains(script, "$after");
    }

    private static RevitServerInspectionReport ReadyReport() =>
        WindowsRevitServerReadinessInspector.Evaluate(ReadyObservation(), MediaPath);

    private static WindowsRevitServerObservation ReadyObservation() => new(
        new WindowsRevitSystemObservation(
            "Microsoft Windows Server 2022 Standard",
            20348,
            "Server",
            3),
        false,
        "BALLS-RS27",
        false,
        new WindowsRevitDataVolumeObservation(3, "NTFS", 80L * 1024 * 1024 * 1024),
        new WindowsRevitRepositoryObservation(false, false, 0, 0),
        new WindowsRevitIisObservation(true, 0, 0, ["Web-Server"]),
        new WindowsRevitNetworkObservation(1, 0, 0, true, true, "Block", "Block"),
        new WindowsRevitStateObservation([], 0),
        new WindowsRevitMediaObservation(
            true,
            true,
            false,
            true,
            912_600_144,
            "Valid",
            "Autodesk, Inc.",
            "Revit_Server_2027_win_db.sfx.exe",
            "Autodesk Create Installer",
            "19.00",
            "295b30779868b9d58d78d9ff4353e4b9c6412418274a8034db6c6e7e0d348518"));

    private const string MediaPath = @"C:\Media\Revit_Server_2027_win_db.sfx.exe";
}
