using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class HostGuestOperationBoundaryTests
{
    [TestMethod]
    public void Guest_driver_mutates_product_state_only_through_the_canonical_cli()
    {
        var script = File.ReadAllText(GuestScriptPath());

        StringAssert.Contains(script, "circle create");
        StringAssert.Contains(script, "files contribution create");
        StringAssert.Contains(script, "files host preview");
        StringAssert.Contains(script, "files host apply");
        StringAssert.Contains(script, "files host remove-preview");
        StringAssert.Contains(script, "files host remove-apply");
        StringAssert.Contains(script, "BALLS_TEST_WINDOWS_HOST_FAILURE_STEP");
        StringAssert.Contains(script, "hosting_plan_changed");
        StringAssert.Contains(script, "hosting_apply_failed");
        Assert.IsFalse(script.Contains("balls-windows-helper", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*(New|Set|Remove)-SmbShare\b"));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*(New|Set|Remove|Enable|Disable)-NetFirewall\w*\b"));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*(Set-Acl|icacls|net\s+share|cmdkey)\b"));
        Assert.IsFalse(script.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Set-ExecutionPolicy", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(Regex.IsMatch(
            script,
            @"(?im)(powershell|pwsh)(\.exe)?[^\r\n]*-ExecutionPolicy\b"));
    }

    [TestMethod]
    public void Native_observation_is_bounded_and_returns_hashes_not_private_state()
    {
        var script = File.ReadAllText(GuestScriptPath());

        StringAssert.Contains(script, "Get-BallsUnrelatedFingerprint");
        StringAssert.Contains(script, "$script:MaximumUnrelatedInventoryEntries");
        StringAssert.Contains(script, "$script:MaximumUnrelatedFileBytes");
        StringAssert.Contains(script, "$script:MaximumUnrelatedTotalFileBytes");
        StringAssert.Contains(script, "Get-BallsConformanceRootInventory");
        StringAssert.Contains(script, "Get-FileHash -LiteralPath");
        StringAssert.Contains(script, "Get-BallsSeedObservation");
        StringAssert.Contains(script, "Get-SmbShareAccess");
        StringAssert.Contains(script, "Get-NetFirewallAddressFilter");
        StringAssert.Contains(script, "Get-NetFirewallApplicationFilter");
        StringAssert.Contains(script, "Get-NetFirewallInterfaceFilter");
        StringAssert.Contains(script, "Get-NetFirewallSecurityFilter");
        StringAssert.Contains(script, "Get-NetFirewallServiceFilter");
        StringAssert.Contains(script, "Get-LocalGroupMember");
        StringAssert.Contains(script, "PasswordRequired");
        StringAssert.Contains(script, "UserMayChangePassword");
        StringAssert.Contains(script, "aclSha256");
        StringAssert.Contains(script, "ownerSidSha256");
        StringAssert.Contains(script, "rootInventorySha256");
        StringAssert.Contains(script, "shareConfigurationSha256");
        StringAssert.Contains(script, "firewallConfigurationSha256");
        StringAssert.Contains(script, "accountConfigurationSha256");
        StringAssert.Contains(script, "secureStoreInventorySha256");
        Assert.IsFalse(script.Contains("preMutationSddl =", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("authorizationDigest =", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Authorized_path_requires_an_explicit_local_disk_backed_volume()
    {
        var script = File.ReadAllText(GuestScriptPath());

        StringAssert.Contains(script, "Get-Volume");
        StringAssert.Contains(script, "Get-Partition");
        StringAssert.Contains(script, "Get-Disk");
        StringAssert.Contains(script, "File Backed Virtual");
        StringAssert.Contains(script, "iSCSI");
        StringAssert.Contains(script, "volumeIdentitySha256");
        StringAssert.Contains(script, "diskIdentitySha256");
        StringAssert.Contains(script, "disposable_path_not_local_disk");
    }

    [TestMethod]
    public void Dpapi_preflight_and_seed_record_precede_product_mutation()
    {
        var script = File.ReadAllText(GuestScriptPath());

        var dpapiPreflight = script.IndexOf("$script:Stage = 'private-material-preflight'", StringComparison.Ordinal);
        var seedSetup = script.IndexOf("$script:Stage = 'seed-setup'", StringComparison.Ordinal);
        var seedRecord = script.IndexOf("Set-Content -LiteralPath $paths.seed", StringComparison.Ordinal);
        var daemonStart = script.IndexOf("$script:Stage = 'daemon-start'", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, dpapiPreflight);
        Assert.IsGreaterThan(dpapiPreflight, seedSetup);
        Assert.IsGreaterThan(seedSetup, seedRecord);
        Assert.IsGreaterThan(seedRecord, daemonStart);
        Assert.IsGreaterThanOrEqualTo(0, daemonStart);
        StringAssert.Contains(script, "daemon_private_material_unavailable");
        StringAssert.Contains(script, "Test-BallsCurrentUserDpapi");
        StringAssert.Contains(script, "DataProtectionScope]::CurrentUser");
        Assert.IsFalse(script.Contains("Access is denied", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Hosted_acl_requires_exact_applicable_owner_and_system_allows_without_denies()
    {
        var script = File.ReadAllText(GuestScriptPath());

        StringAssert.Contains(script, "PropagationFlags]::InheritOnly");
        StringAssert.Contains(script, "AccessControlType]::Deny");
        StringAssert.Contains(script, "aclApplicableRuleCount");
        StringAssert.Contains(script, "aclDenyRuleCount");
        StringAssert.Contains(script, "aclShapeExact");
    }

    [TestMethod]
    public void Normal_and_emergency_cleanup_share_one_canonical_product_removal_sequence()
    {
        var script = File.ReadAllText(GuestScriptPath());

        StringAssert.Contains(script, "function Invoke-BallsHostRemoval");
        Assert.AreEqual(1, Regex.Matches(script, "files host remove-preview").Count);
        Assert.AreEqual(1, Regex.Matches(script, "files host remove-apply").Count);
    }

    [TestMethod]
    public void Windows_PowerShell_parses_the_fixed_host_guest_operation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows PowerShell parsing requires Windows.");
            return;
        }

        var encodedPath = Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(GuestScriptPath()));
        var command =
            "$path=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" +
            encodedPath +
            "'));$tokens=$null;$errors=$null;" +
            "[void][Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors);" +
            "if($errors.Count -ne 0){exit 1}";
        var encodedCommand = Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(command));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-EncodedCommand",
                encodedCommand,
            },
        });

        Assert.IsNotNull(process);
        Assert.IsTrue(process.WaitForExit(10000));
        Assert.AreEqual(0, process.ExitCode);
    }

    private static string GuestScriptPath() => RepositoryPath(
        "eng",
        "conformance",
        "Invoke-WindowsCircleFilesHostConformance.ps1");

    private static string RepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
