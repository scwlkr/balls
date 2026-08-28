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
        StringAssert.Contains(script, "Get-BallsSeedObservation");
        StringAssert.Contains(script, "Get-SmbShareAccess");
        StringAssert.Contains(script, "Get-NetFirewallAddressFilter");
        StringAssert.Contains(script, "Get-NetFirewallServiceFilter");
        StringAssert.Contains(script, "aclSha256");
        StringAssert.Contains(script, "ownerSidSha256");
        Assert.IsFalse(script.Contains("preMutationSddl =", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("authorizationDigest =", StringComparison.OrdinalIgnoreCase));
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
