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
        StringAssert.Contains(script, "Get-NetFirewallAddressFilter");
        StringAssert.Contains(script, "Get-NetFirewallInterfaceFilter");
        StringAssert.Contains(script, "Get-NetFirewallInterfaceTypeFilter");
        StringAssert.Contains(script, "Get-NetFirewallSecurityFilter");
        StringAssert.Contains(script, "Test-BallsSmbFirewallApplicability");
        StringAssert.Contains(script, "Test-BallsSmbFirewallBroadPublicBlock");
        StringAssert.Contains(script, "Test-BallsSmbFirewallBlockBypass");
        StringAssert.Contains(script, "Select-BallsInboundFirewallRules");
        StringAssert.Contains(script, "OverrideBlockRules");
        StringAssert.Contains(script, "RemoteDynamicKeywordAddresses");
        StringAssert.Contains(script, "ProfileInactive");
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

        const string inboundFirewallInventory =
            "NetSecurity\\Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True -Direction Inbound -ErrorAction Stop";
        Assert.AreEqual(1, script.Split(inboundFirewallInventory, StringSplitOptions.None).Length - 1);
        Assert.IsFalse(script.Contains("-Direction Inbound -Action", StringComparison.OrdinalIgnoreCase));
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

    [TestMethod]
    public async Task Inbound_firewall_inventory_handles_missing_block_rules_and_rejects_unknown_actions()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Windows PowerShell firewall inventory requires Windows.");
            return;
        }

        var inspection = StaticWindowsPowerShellJsonSource.GetScript(
            WindowsPowerShellQuery.SmbReadiness);
        var start = inspection.IndexOf(
            "function Select-BallsInboundFirewallRules",
            StringComparison.Ordinal);
        var end = inspection.IndexOf("\n\n", start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        var command = inspection[start..end] +
            """

            $allow = [PSCustomObject]@{ Action = 'Allow'; Name = 'allow' }
            $block = [PSCustomObject]@{ Action = 'Block'; Name = 'block' }
            $allowOnly = @($allow)
            if (@(Select-BallsInboundFirewallRules -Rules $allowOnly -Action 'Allow').Count -ne 1) { throw 'An existing allow rule was lost.' }
            if (@(Select-BallsInboundFirewallRules -Rules $allowOnly -Action 'Block').Count -ne 0) { throw 'A missing block rule was fabricated.' }
            if (@(Select-BallsInboundFirewallRules -Rules @($allow, $block) -Action 'Block').Count -ne 1) { throw 'An existing block rule was lost.' }

            $unknownRejected = $false
            try {
                @(Select-BallsInboundFirewallRules -Rules @($allow, [PSCustomObject]@{ Action = 'NotConfigured' }) -Action 'Allow') | Out-Null
            } catch {
                $unknownRejected = $_.Exception.Message -eq 'Windows returned an unsupported inbound firewall rule action.'
            }
            if (-not $unknownRejected) { throw 'An unknown firewall rule action was accepted.' }

            $selectionRejected = $false
            try {
                @(Select-BallsInboundFirewallRules -Rules $allowOnly -Action 'NotConfigured') | Out-Null
            } catch {
                $selectionRejected = $_.Exception.Message -eq 'Windows returned an unsupported inbound firewall rule action.'
            }
            if (-not $selectionRejected) { throw 'An unknown firewall selection action was accepted.' }

            [Console]::Out.Write('5')
            """;
        var output = await BoundedWindowsInspectionProcessRunner.RunAsync(
            CreatePowerShellStartInfo(command),
            TimeSpan.FromSeconds(5),
            1024,
            CancellationToken.None);

        Assert.AreEqual("5", output);
    }

    [TestMethod]
    public async Task Public_SMB_block_must_be_broad_effective_and_free_of_authenticated_bypass()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Windows PowerShell firewall block predicates require Windows.");
            return;
        }

        var inspection = StaticWindowsPowerShellJsonSource.GetScript(
            WindowsPowerShellQuery.SmbReadiness);
        var start = inspection.IndexOf(
            "function Test-BallsUnrestrictedFirewallValue",
            StringComparison.Ordinal);
        var end = inspection.IndexOf("\n\ntry {", start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        var command = inspection[start..end] +
            """

            function New-BallsBroadPublicBlockFixture {
                return [PSCustomObject]@{
                    Rule = [PSCustomObject]@{ Enabled = 'True'; Direction = 'Inbound'; Action = 'Block'; Profile = 'Public'; PrimaryStatus = 'OK'; EnforcementStatus = @(); Owner = '' }
                    Port = [PSCustomObject]@{ Protocol = 'TCP'; LocalPort = '445'; RemotePort = 'Any'; DynamicTarget = 'Any' }
                    Application = [PSCustomObject]@{ Program = 'Any'; Package = '' }
                    Service = [PSCustomObject]@{ Service = 'Any' }
                    Address = [PSCustomObject]@{ LocalAddress = 'Any'; RemoteAddress = 'Any' }
                    Interface = [PSCustomObject]@{ InterfaceAlias = 'Any' }
                    InterfaceType = [PSCustomObject]@{ InterfaceType = 0 }
                    Security = [PSCustomObject]@{ Authentication = 'NotRequired'; Encryption = 'NotRequired'; OverrideBlockRules = 'False'; LocalUser = 'Any'; RemoteUser = 'Any'; RemoteMachine = 'Any' }
                }
            }

            $cases = @(
                [PSCustomObject]@{ Name = 'broad-public-block'; Change = {}; Expected = $true },
                [PSCustomObject]@{ Name = 'dormant-public-block'; Change = { param($value) $value.Rule.PrimaryStatus = 'Inactive'; $value.Rule.EnforcementStatus = @('ProfileInactive') }; Expected = $true },
                [PSCustomObject]@{ Name = 'private-profile'; Change = { param($value) $value.Rule.Profile = 'Private' }; Expected = $false },
                [PSCustomObject]@{ Name = 'any-profile'; Change = { param($value) $value.Rule.Profile = 'Any' }; Expected = $false },
                [PSCustomObject]@{ Name = 'inactive-missing-reason'; Change = { param($value) $value.Rule.PrimaryStatus = 'Inactive' }; Expected = $false },
                [PSCustomObject]@{ Name = 'inactive-wrong-reason'; Change = { param($value) $value.Rule.PrimaryStatus = 'Inactive'; $value.Rule.EnforcementStatus = @('Disabled') }; Expected = $false },
                [PSCustomObject]@{ Name = 'inactive-multiple-reasons'; Change = { param($value) $value.Rule.PrimaryStatus = 'Inactive'; $value.Rule.EnforcementStatus = @('ProfileInactive', 'Disabled') }; Expected = $false },
                [PSCustomObject]@{ Name = 'owner-restricted'; Change = { param($value) $value.Rule.Owner = 'S-1-5-18' }; Expected = $false },
                [PSCustomObject]@{ Name = 'wrong-port'; Change = { param($value) $value.Port.LocalPort = '446' }; Expected = $false },
                [PSCustomObject]@{ Name = 'remote-port-restricted'; Change = { param($value) $value.Port.RemotePort = '445' }; Expected = $false },
                [PSCustomObject]@{ Name = 'dynamic-target-restricted'; Change = { param($value) $value.Port.DynamicTarget = 'ProximityApps' }; Expected = $false },
                [PSCustomObject]@{ Name = 'program-restricted'; Change = { param($value) $value.Application.Program = 'System' }; Expected = $false },
                [PSCustomObject]@{ Name = 'package-restricted'; Change = { param($value) $value.Application.Package = 'S-1-15-2-123' }; Expected = $false },
                [PSCustomObject]@{ Name = 'service-restricted'; Change = { param($value) $value.Service.Service = 'LanmanServer' }; Expected = $false },
                [PSCustomObject]@{ Name = 'address-restricted'; Change = { param($value) $value.Address.RemoteAddress = 'LocalSubnet' }; Expected = $false },
                [PSCustomObject]@{ Name = 'interface-restricted'; Change = { param($value) $value.Interface.InterfaceAlias = 'Ethernet' }; Expected = $false },
                [PSCustomObject]@{ Name = 'interface-type-restricted'; Change = { param($value) $value.InterfaceType.InterfaceType = 'Wired' }; Expected = $false },
                [PSCustomObject]@{ Name = 'authentication-restricted'; Change = { param($value) $value.Security.Authentication = 'Required' }; Expected = $false },
                [PSCustomObject]@{ Name = 'user-restricted'; Change = { param($value) $value.Security.RemoteUser = 'S-1-5-18' }; Expected = $false },
                [PSCustomObject]@{ Name = 'missing-security'; Change = { param($value) $value.Security = $null }; Expected = $false },
                [PSCustomObject]@{ Name = 'multiple-ports'; Change = { param($value) $value.Port = @($value.Port, $value.Port) }; Expected = $false }
            )
            foreach ($case in $cases) {
                $fixture = New-BallsBroadPublicBlockFixture
                & $case.Change $fixture
                $actual = Test-BallsSmbFirewallBroadPublicBlock -Rule $fixture.Rule -Port $fixture.Port -Application $fixture.Application -Service $fixture.Service -Address $fixture.Address -Interface $fixture.Interface -InterfaceType $fixture.InterfaceType -Security $fixture.Security
                if ($actual -ne $case.Expected) { throw ('Unexpected public SMB block coverage: ' + $case.Name) }
            }

            $bypassCases = @(
                [PSCustomObject]@{ Name = 'ordinary-allow'; Security = [PSCustomObject]@{ OverrideBlockRules = 'False' }; Expected = $false },
                [PSCustomObject]@{ Name = 'authenticated-bypass'; Security = [PSCustomObject]@{ OverrideBlockRules = 'True' }; Expected = $true },
                [PSCustomObject]@{ Name = 'unknown-bypass'; Security = [PSCustomObject]@{ OverrideBlockRules = 'Unknown' }; Expected = $true },
                [PSCustomObject]@{ Name = 'missing-bypass'; Security = $null; Expected = $true },
                [PSCustomObject]@{ Name = 'multiple-bypass'; Security = @([PSCustomObject]@{ OverrideBlockRules = 'False' }, [PSCustomObject]@{ OverrideBlockRules = 'False' }); Expected = $true }
            )
            foreach ($case in $bypassCases) {
                $actual = Test-BallsSmbFirewallBlockBypass -Security $case.Security
                if ($actual -ne $case.Expected) { throw ('Unexpected SMB authenticated bypass: ' + $case.Name) }
            }

            [Console]::Out.Write($cases.Count + $bypassCases.Count)
            """;
        var output = await BoundedWindowsInspectionProcessRunner.RunAsync(
            CreatePowerShellStartInfo(command),
            TimeSpan.FromSeconds(5),
            1024,
            CancellationToken.None);

        Assert.AreEqual("26", output);
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
