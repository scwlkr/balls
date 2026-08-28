using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class GuestOperationBoundaryTests
{
    [TestMethod]
    public void Guest_operation_is_read_only_outside_its_exact_owned_temporary_state()
    {
        var script = File.ReadAllText(GuestScriptPath());

        StringAssert.Contains(script, "files readiness");
        StringAssert.Contains(script, "--files-readiness-conformance");
        StringAssert.Contains(script, "function Invoke-BallsBoundedProcess");
        StringAssert.Contains(script, "function Initialize-BallsRestrictedProcessType");
        StringAssert.Contains(script, "function Start-BallsRestrictedDaemon");
        StringAssert.Contains(script, "function Invoke-BallsBoundedRestrictedProcess");
        StringAssert.Contains(script, "CreateRestrictedToken");
        StringAssert.Contains(script, "DisableMaxPrivilege | LuaToken");
        StringAssert.Contains(script, "StartRedirected");
        StringAssert.Contains(script, "StartfUseStdHandles");
        StringAssert.Contains(script, "GetSessionToken");
        StringAssert.Contains(script, "Close-BallsRestrictedProcessSession");
        Assert.IsFalse(script.Contains("SaferComputeTokenFromLevel", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("$env:ComSpec", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(script, ".WaitForExit($TimeoutMilliseconds)");
        StringAssert.Contains(script, "native_inspection_timeout");
        StringAssert.Contains(script, "$readinessEnvelope.outputVersion -ne 1");
        StringAssert.Contains(script, "Get-SmbServerConfiguration");
        StringAssert.Contains(script, "Get-NetFirewallRule");
        StringAssert.Contains(script, "BallsSmbReadiness-$RunId");
        Assert.IsFalse(Regex.IsMatch(
            script,
            @"(?im)(powershell|pwsh)(\.exe)?[^\r\n]*-ExecutionPolicy\b"));
        Assert.IsFalse(script.Contains("Set-ExecutionPolicy", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*New-SmbShare\b"));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*Set-Smb\w*\b"));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*New-NetFirewallRule\b"));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*Set-NetFirewall\w*\b"));
        Assert.IsFalse(Regex.IsMatch(script, @"(?im)^\s*New-LocalUser\b"));
    }

    [TestMethod]
    public void Guest_cleanup_does_not_leak_process_wait_output_into_its_receipt()
    {
        var script = File.ReadAllText(GuestScriptPath());

        StringAssert.Contains(script, "[void]$DaemonProcess.WaitForExit(10000)");
    }

    [TestMethod]
    public void Windows_PowerShell_parses_the_fixed_guest_operation()
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

    private static string GuestScriptPath() => Path.Combine(
        FindRepositoryRoot(),
        "eng",
        "conformance",
        "Invoke-WindowsSmbReadinessConformance.ps1");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
