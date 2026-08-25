using System.Diagnostics;
using System.Text;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class WindowsPowerShellInspectionTests
{
    [TestMethod]
    public void Query_allow_list_contains_only_the_read_only_SMB_readiness_inspection()
    {
        CollectionAssert.AreEqual(
            new[] { WindowsPowerShellQuery.SmbReadiness },
            Enum.GetValues<WindowsPowerShellQuery>());

        var script = StaticWindowsPowerShellJsonSource.GetScript(
            WindowsPowerShellQuery.SmbReadiness);

        StringAssert.Contains(script, "Get-SmbServerConfiguration");
        StringAssert.Contains(script, "Get-SmbClientConfiguration");
        StringAssert.Contains(script, "Get-NetConnectionProfile");
        StringAssert.Contains(script, "Get-NetFirewallProfile");
        StringAssert.Contains(script, "Get-NetFirewallRule");
        StringAssert.Contains(script, "Get-NetFirewallPortFilter");
        StringAssert.Contains(script, "Get-NetFirewallApplicationFilter");
        StringAssert.Contains(script, "Get-NetFirewallServiceFilter");
        StringAssert.Contains(script, "Test-BallsSmbFirewallApplicability");
        StringAssert.Contains(script, "'LanmanServer'");
        StringAssert.Contains(script, "'svchost.exe'");
        StringAssert.Contains(script, "Get-Service");
        StringAssert.Contains(script, "Get-Command");
        Assert.IsFalse(script.Contains("Set-", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Remove-", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Enable-", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Disable-", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("$args", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("$input", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Query_refuses_values_outside_the_allow_list_before_starting_a_process()
    {
        var source = new StaticWindowsPowerShellJsonSource();

        var error = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => source.QueryAsync(
                (WindowsPowerShellQuery)int.MaxValue,
                CancellationToken.None).AsTask());

        Assert.AreEqual("query", error.ParamName);
    }
}

[TestClass]
[TestCategory("OSIntegration")]
public sealed class WindowsPowerShellProcessTests
{
    [TestMethod]
    public void Elevated_helper_rejects_a_pipe_server_that_is_not_the_adjacent_daemon()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Windows process identity test requires Windows.");
            return;
        }

        Assert.IsFalse(WindowsProcessIdentity.TryGetExpectedDaemonUserSid(
            Environment.ProcessId,
            out _));
    }

    [TestMethod]
    public async Task Bounded_runner_writes_redirected_input_without_weakening_output_bounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The bounded PowerShell process test requires Windows.");
            return;
        }

        var startInfo = CreatePowerShellStartInfo(
            "$value = [Console]::In.ReadToEnd(); [Console]::Out.Write($value)");

        var output = await BoundedWindowsInspectionProcessRunner.RunWithInputAsync(
            startInfo,
            "bounded input",
            TimeSpan.FromSeconds(5),
            1024,
            CancellationToken.None);

        Assert.AreEqual("bounded input", output);
    }

    [TestMethod]
    public async Task Bounded_runner_terminates_a_query_that_exceeds_its_timeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The bounded PowerShell process test requires Windows.");
            return;
        }

        var startInfo = CreatePowerShellStartInfo(
            "Start-Sleep -Seconds 30; 'unexpected'");

        var error = await Assert.ThrowsExactlyAsync<WindowsInspectionException>(
            () => BoundedWindowsInspectionProcessRunner.RunAsync(
                startInfo,
                TimeSpan.FromMilliseconds(250),
                1024,
                CancellationToken.None));

        Assert.AreEqual("The Windows inspection timed out.", error.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Bounded_runner_terminates_when_either_output_stream_exceeds_its_limit(
        bool standardError)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The bounded PowerShell process test requires Windows.");
            return;
        }

        var startInfo = CreatePowerShellStartInfo(standardError
            ? "[Console]::Error.Write(('x' * 4096)); Start-Sleep -Seconds 30"
            : "[Console]::Out.Write(('x' * 4096)); Start-Sleep -Seconds 30");
        var stopwatch = Stopwatch.StartNew();

        var error = await Assert.ThrowsExactlyAsync<WindowsInspectionException>(
            () => BoundedWindowsInspectionProcessRunner.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(5),
                1024,
                CancellationToken.None));

        stopwatch.Stop();
        Assert.AreEqual("The Windows inspection exceeded its output limit.", error.Message);
        Assert.IsLessThan(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
    }

    [TestMethod]
    public async Task Real_adapter_returns_a_complete_typed_report_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The real SMB readiness adapter requires Windows.");
            return;
        }

        var report = await new WindowsSmbReadinessInspector()
            .InspectAsync(CancellationToken.None);

        Assert.AreEqual(CircleFilesReadinessProviders.WindowsSmb311, report.Provider);
        Assert.HasCount(9, report.Checks);
        Assert.IsTrue(Enum.IsDefined(report.Status));
    }

    [TestMethod]
    public async Task Smb_firewall_predicate_excludes_only_provably_unrelated_applications()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Windows PowerShell firewall predicate requires Windows.");
            return;
        }

        var inspection = StaticWindowsPowerShellJsonSource.GetScript(
            WindowsPowerShellQuery.SmbReadiness);
        var start = inspection.IndexOf(
            "function Test-BallsSmbFirewallApplicability",
            StringComparison.Ordinal);
        var end = inspection.IndexOf("\n\n", start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        var command = inspection[start..end] +
            """

            $cases = @(
                [PSCustomObject]@{ Name = 'unrestricted'; Program = 'Any'; Service = 'Any'; Expected = $true },
                [PSCustomObject]@{ Name = 'system'; Program = 'System'; Service = 'Any'; Expected = $true },
                [PSCustomObject]@{ Name = 'server-service'; Program = 'Any'; Service = 'LanmanServer'; Expected = $true },
                [PSCustomObject]@{ Name = 'shared-service-host'; Program = 'C:\Windows\System32\svchost.exe'; Service = 'Any'; Expected = $true },
                [PSCustomObject]@{ Name = 'unrelated-executable'; Program = 'C:\Windows\System32\spoolsv.exe'; Service = 'Any'; Expected = $false },
                [PSCustomObject]@{ Name = 'unrelated-service'; Program = 'C:\Windows\System32\svchost.exe'; Service = 'stisvc'; Expected = $false },
                [PSCustomObject]@{ Name = 'missing-program'; Program = ''; Service = 'Any'; Expected = $true },
                [PSCustomObject]@{ Name = 'missing-service'; Program = 'Any'; Service = ''; Expected = $true },
                [PSCustomObject]@{ Name = 'relative-program'; Program = 'spoolsv.exe'; Service = 'Any'; Expected = $true },
                [PSCustomObject]@{ Name = 'wildcard-program'; Program = 'C:\Windows\System32\*.exe'; Service = 'Any'; Expected = $true },
                [PSCustomObject]@{ Name = 'unresolved-program'; Program = '%BALLS_UNKNOWN_ROOT%\tool.exe'; Service = 'Any'; Expected = $true },
                [PSCustomObject]@{ Name = 'unknown-program-kind'; Program = 'C:\Windows\System32\unknown'; Service = 'Any'; Expected = $true }
            )
            foreach ($case in $cases) {
                $actual = Test-BallsSmbFirewallApplicability -Program $case.Program -Service $case.Service
                if ($actual -ne $case.Expected) { throw ('Unexpected SMB applicability: ' + $case.Name) }
            }
            [Console]::Out.Write($cases.Count)
            """;
        var output = await BoundedWindowsInspectionProcessRunner.RunAsync(
            CreatePowerShellStartInfo(command),
            TimeSpan.FromSeconds(5),
            1024,
            CancellationToken.None);

        Assert.AreEqual("12", output);
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }
}
