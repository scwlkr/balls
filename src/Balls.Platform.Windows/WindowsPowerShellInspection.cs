using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Balls.Platform.Windows;

internal enum WindowsPowerShellQuery
{
    SmbReadiness,
}

internal interface IWindowsPowerShellJsonSource
{
    ValueTask<string> QueryAsync(
        WindowsPowerShellQuery query,
        CancellationToken cancellationToken);
}

internal sealed class WindowsInspectionException : Exception
{
    internal WindowsInspectionException(string message)
        : base(message)
    {
    }

    internal WindowsInspectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class StaticWindowsPowerShellJsonSource : IWindowsPowerShellJsonSource
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private const int MaximumOutputCharacters = 64 * 1024;

    public async ValueTask<string> QueryAsync(
        WindowsPowerShellQuery query,
        CancellationToken cancellationToken)
    {
        var script = GetScript(query);
        cancellationToken.ThrowIfCancellationRequested();

        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(executable))
        {
            throw new WindowsInspectionException("The Windows inspection host is unavailable.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

        return await BoundedWindowsInspectionProcessRunner.RunAsync(
            startInfo,
            QueryTimeout,
            MaximumOutputCharacters,
            cancellationToken).ConfigureAwait(false);
    }

    internal static string GetScript(WindowsPowerShellQuery query) => query switch
    {
        WindowsPowerShellQuery.SmbReadiness =>
            """
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'

            function Test-BallsSmbFirewallApplicability {
                param([AllowNull()][string]$Program, [AllowNull()][string]$Service)
                if (-not [string]::IsNullOrWhiteSpace($Service) -and $Service -notin @('Any', 'LanmanServer')) { return $false }
                if ([string]::IsNullOrWhiteSpace($Program) -or $Program -in @('Any', 'System')) { return $true }
                try {
                    $expanded = [Environment]::ExpandEnvironmentVariables($Program)
                    if (-not [System.IO.Path]::IsPathRooted($expanded) -or $expanded.Contains('*') -or $expanded.Contains('?') -or $expanded.Contains('%')) { return $true }
                    $name = [System.IO.Path]::GetFileName($expanded)
                    if ([string]::IsNullOrWhiteSpace($name) -or $name -eq 'svchost.exe' -or -not $name.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) { return $true }
                    return $false
                } catch { return $true }
            }

            function Test-BallsUnrestrictedFirewallValue {
                param([AllowNull()][object]$Value, [switch]$AllowEmpty, [switch]$AllowZero)
                $values = @($Value)
                if ($values.Count -eq 0) { return [bool]$AllowEmpty }
                if ($values.Count -ne 1) { return $false }
                $text = [string]$values[0]
                return $text -eq 'Any' -or ($AllowEmpty -and [string]::IsNullOrWhiteSpace($text)) -or ($AllowZero -and $text -eq '0')
            }

            function Test-BallsSmbFirewallBroadPublicBlock {
                param(
                    [AllowNull()][object]$Rule,
                    [AllowNull()][object]$Port,
                    [AllowNull()][object]$Application,
                    [AllowNull()][object]$Service,
                    [AllowNull()][object]$Address,
                    [AllowNull()][object]$Interface,
                    [AllowNull()][object]$InterfaceType,
                    [AllowNull()][object]$Security)
                if (@($Rule).Count -ne 1 -or @($Port).Count -ne 1 -or @($Application).Count -ne 1 -or @($Service).Count -ne 1 -or @($Address).Count -ne 1 -or @($Interface).Count -ne 1 -or @($InterfaceType).Count -ne 1 -or @($Security).Count -ne 1) { return $false }
                $ruleValue = @($Rule)[0]
                $portValue = @($Port)[0]
                $applicationValue = @($Application)[0]
                $serviceValue = @($Service)[0]
                $addressValue = @($Address)[0]
                $interfaceValue = @($Interface)[0]
                $interfaceTypeValue = @($InterfaceType)[0]
                $securityValue = @($Security)[0]
                if ([string]$ruleValue.Enabled -ne 'True' -or [string]$ruleValue.Direction -ne 'Inbound' -or [string]$ruleValue.Action -ne 'Block' -or [string]$ruleValue.Profile -ne 'Public') { return $false }
                $primaryStatus = [string]$ruleValue.PrimaryStatus
                if ($primaryStatus -ne 'OK' -and ($primaryStatus -ne 'Inactive' -or @($ruleValue.EnforcementStatus).Count -ne 1 -or [string]@($ruleValue.EnforcementStatus)[0] -ne 'ProfileInactive')) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $ruleValue.Owner -AllowEmpty)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $ruleValue.RemoteDynamicKeywordAddresses -AllowEmpty)) { return $false }
                if ([string]$portValue.Protocol -notin @('TCP', '6') -or @($portValue.LocalPort).Count -ne 1 -or [string]@($portValue.LocalPort)[0] -ne '445') { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $portValue.RemotePort)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $portValue.DynamicTarget -AllowEmpty)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $applicationValue.Program)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $applicationValue.Package -AllowEmpty)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $serviceValue.Service)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $addressValue.LocalAddress) -or -not (Test-BallsUnrestrictedFirewallValue -Value $addressValue.RemoteAddress)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $addressValue.RemoteDynamicKeywordAddresses -AllowEmpty)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $interfaceValue.InterfaceAlias)) { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $interfaceTypeValue.InterfaceType -AllowZero)) { return $false }
                if ([string]$securityValue.Authentication -ne 'NotRequired' -or [string]$securityValue.Encryption -ne 'NotRequired' -or [string]$securityValue.OverrideBlockRules -ne 'False') { return $false }
                if (-not (Test-BallsUnrestrictedFirewallValue -Value $securityValue.LocalUser -AllowEmpty) -or -not (Test-BallsUnrestrictedFirewallValue -Value $securityValue.RemoteUser -AllowEmpty) -or -not (Test-BallsUnrestrictedFirewallValue -Value $securityValue.RemoteMachine -AllowEmpty)) { return $false }
                return $true
            }

            function Test-BallsSmbFirewallBlockBypass {
                param([AllowNull()][object]$Security)
                if (@($Security).Count -ne 1) { return $true }
                return [string]@($Security)[0].OverrideBlockRules -ne 'False'
            }

            try {
                $currentVersion = Microsoft.PowerShell.Management\Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop
                $system = [PSCustomObject]@{
                    BuildNumber = [Environment]::OSVersion.Version.Build
                    InstallationType = [string]$currentVersion.InstallationType
                }
            } catch { $system = $null }

            try {
                $serverService = Microsoft.PowerShell.Management\Get-Service -Name 'LanmanServer' -ErrorAction Stop
                $firewallService = Microsoft.PowerShell.Management\Get-Service -Name 'MpsSvc' -ErrorAction Stop
                $services = [PSCustomObject]@{
                    LanmanServer = [string]$serverService.Status
                    WindowsFirewall = [string]$firewallService.Status
                }
            } catch { $services = $null }

            try {
                $server = SmbShare\Get-SmbServerConfiguration -ErrorAction Stop
                $shareCommand = @(Microsoft.PowerShell.Core\Get-Command -Name 'New-SmbShare' -CommandType Function,Cmdlet -ErrorAction Stop)[0]
                $ciphers = $null
                if ($null -ne $server.EncryptionCiphers) {
                    $ciphers = @([string]$server.EncryptionCiphers -split ',\s*')
                }
                $smbServer = [PSCustomObject]@{
                    EnableSMB1Protocol = $server.EnableSMB1Protocol
                    EnableSMB2Protocol = $server.EnableSMB2Protocol
                    Smb2DialectMax = [string]$server.Smb2DialectMax
                    RequireSecuritySignature = $server.RequireSecuritySignature
                    RejectUnencryptedAccess = $server.RejectUnencryptedAccess
                    ShareEncryptionSupported = [bool]($null -ne $shareCommand.Parameters['EncryptData'])
                    EncryptionCiphers = $ciphers
                }
            } catch { $smbServer = $null }

            try {
                $client = SmbShare\Get-SmbClientConfiguration -ErrorAction Stop
                $smbClient = [PSCustomObject]@{
                    EnableInsecureGuestLogons = $client.EnableInsecureGuestLogons
                }
            } catch { $smbClient = $null }

            try {
                $connectedPrivateProfiles = 0
                foreach ($profile in @(NetConnection\Get-NetConnectionProfile -ErrorAction Stop)) {
                    $connected = ([string]$profile.IPv4Connectivity -ne 'Disconnected') -or ([string]$profile.IPv6Connectivity -ne 'Disconnected')
                    if ($connected -and ([string]$profile.NetworkCategory -eq 'Private')) { $connectedPrivateProfiles++ }
                }
                $network = [PSCustomObject]@{ ConnectedPrivateProfiles = $connectedPrivateProfiles }
            } catch { $network = $null }

            try {
                $privateFirewall = NetSecurity\Get-NetFirewallProfile -Name 'Private' -PolicyStore ActiveStore -ErrorAction Stop
                $publicFirewall = NetSecurity\Get-NetFirewallProfile -Name 'Public' -PolicyStore ActiveStore -ErrorAction Stop
                $publicSmbInboundAllowRules = 0
                $publicSmbInboundBlockBypass = $false
                foreach ($rule in @(NetSecurity\Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True -Direction Inbound -Action Allow -ErrorAction Stop)) {
                    $profiles = @(([string]$rule.Profile -split ',') | ForEach-Object { $_.Trim() })
                    if (($profiles -notcontains 'Any') -and ($profiles -notcontains 'Public')) { continue }

                    $matchesSmbPort = $false
                    foreach ($portFilter in @($rule | NetSecurity\Get-NetFirewallPortFilter -ErrorAction Stop)) {
                        $protocol = [string]$portFilter.Protocol
                        if ($protocol -notin @('Any', 'TCP', '6')) { continue }

                        foreach ($localPort in @($portFilter.LocalPort)) {
                            $portText = [string]$localPort
                            if ($portText -in @('Any', '445')) {
                                $matchesSmbPort = $true
                                break
                            }

                            if ($portText -match '^(\d+)-(\d+)$') {
                                if (([int]$Matches[1] -le 445) -and ([int]$Matches[2] -ge 445)) {
                                    $matchesSmbPort = $true
                                    break
                                }
                            }
                        }
                        if ($matchesSmbPort) { break }
                    }

                    if (-not $matchesSmbPort) { continue }
                    $applicationFilters = @($rule | NetSecurity\Get-NetFirewallApplicationFilter -ErrorAction Stop)
                    $serviceFilters = @($rule | NetSecurity\Get-NetFirewallServiceFilter -ErrorAction Stop)
                    if ($applicationFilters.Count -ne 1 -or $serviceFilters.Count -ne 1) {
                        $publicSmbInboundAllowRules++
                        $publicSmbInboundBlockBypass = $true
                        continue
                    }

                    $couldTargetSmb = Test-BallsSmbFirewallApplicability -Program ([string]$applicationFilters[0].Program) -Service ([string]$serviceFilters[0].Service)
                    if ($couldTargetSmb) {
                        $publicSmbInboundAllowRules++
                        $securityFilters = @($rule | NetSecurity\Get-NetFirewallSecurityFilter -ErrorAction Stop)
                        if (Test-BallsSmbFirewallBlockBypass -Security $securityFilters) {
                            $publicSmbInboundBlockBypass = $true
                        }
                    }
                }

                if ($publicSmbInboundAllowRules -gt 0 -and -not $publicSmbInboundBlockBypass) {
                    $broadPublicSmbBlocks = 0
                    foreach ($rule in @(NetSecurity\Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True -Direction Inbound -Action Block -ErrorAction Stop)) {
                        if ([string]$rule.Profile -ne 'Public') { continue }
                        $ports = @($rule | NetSecurity\Get-NetFirewallPortFilter -ErrorAction Stop)
                        if ($ports.Count -ne 1 -or [string]$ports[0].Protocol -notin @('TCP', '6') -or @($ports[0].LocalPort).Count -ne 1 -or [string]@($ports[0].LocalPort)[0] -ne '445') { continue }

                        $applications = @($rule | NetSecurity\Get-NetFirewallApplicationFilter -ErrorAction Stop)
                        $blockServices = @($rule | NetSecurity\Get-NetFirewallServiceFilter -ErrorAction Stop)
                        $addresses = @($rule | NetSecurity\Get-NetFirewallAddressFilter -ErrorAction Stop)
                        $interfaces = @($rule | NetSecurity\Get-NetFirewallInterfaceFilter -ErrorAction Stop)
                        $interfaceTypes = @($rule | NetSecurity\Get-NetFirewallInterfaceTypeFilter -ErrorAction Stop)
                        $security = @($rule | NetSecurity\Get-NetFirewallSecurityFilter -ErrorAction Stop)
                        if (Test-BallsSmbFirewallBroadPublicBlock -Rule $rule -Port $ports -Application $applications -Service $blockServices -Address $addresses -Interface $interfaces -InterfaceType $interfaceTypes -Security $security) {
                            $broadPublicSmbBlocks++
                        }
                    }

                    if ($broadPublicSmbBlocks -eq 1) {
                        $publicSmbInboundAllowRules = 0
                    }
                }

                $firewall = [PSCustomObject]@{
                    PrivateEnabled = [bool]$privateFirewall.Enabled
                    PrivateDefaultInboundAction = [string]$privateFirewall.DefaultInboundAction
                    PublicEnabled = [bool]$publicFirewall.Enabled
                    PublicDefaultInboundAction = [string]$publicFirewall.DefaultInboundAction
                    PublicSmbInboundAllowRules = $publicSmbInboundAllowRules
                }
            } catch { $firewall = $null }

            [PSCustomObject]@{
                System = $system
                Services = $services
                SmbServer = $smbServer
                SmbClient = $smbClient
                Network = $network
                Firewall = $firewall
            } | Microsoft.PowerShell.Utility\ConvertTo-Json -Compress -Depth 5
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(query)),
    };

}

internal static class BoundedWindowsInspectionProcessRunner
{
    internal static async Task<string> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan queryTimeout,
        int maximumOutputCharacters,
        CancellationToken cancellationToken) =>
        await RunCoreAsync(
            startInfo,
            standardInput: null,
            queryTimeout,
            maximumOutputCharacters,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<string> RunWithInputAsync(
        ProcessStartInfo startInfo,
        string standardInput,
        TimeSpan queryTimeout,
        int maximumOutputCharacters,
        CancellationToken cancellationToken) =>
        await RunCoreAsync(
            startInfo,
            standardInput,
            queryTimeout,
            maximumOutputCharacters,
            cancellationToken).ConfigureAwait(false);

    private static async Task<string> RunCoreAsync(
        ProcessStartInfo startInfo,
        string? standardInput,
        TimeSpan queryTimeout,
        int maximumOutputCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumOutputCharacters, 0);
        if (startInfo.UseShellExecute
            || !startInfo.RedirectStandardOutput
            || !startInfo.RedirectStandardError
            || (standardInput is not null && !startInfo.RedirectStandardInput))
        {
            throw new ArgumentException(
                "Windows inspection must use redirected streams without a shell.",
                nameof(startInfo));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new WindowsInspectionException("The Windows inspection could not start.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new WindowsInspectionException("The Windows inspection could not start.", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(queryTimeout);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(
                    standardInput.AsMemory(),
                    timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var outputBudget = new OutputCharacterBudget(maximumOutputCharacters);
            var standardOutput = ReadBoundedAsync(
                process.StandardOutput,
                outputBudget,
                process,
                capture: true,
                timeout.Token);
            var standardError = ReadBoundedAsync(
                process.StandardError,
                outputBudget,
                process,
                capture: false,
                timeout.Token);
            await Task.WhenAll(
                process.WaitForExitAsync(timeout.Token),
                standardOutput,
                standardError).ConfigureAwait(false);
            var outputResult = await standardOutput.ConfigureAwait(false);
            var errorResult = await standardError.ConfigureAwait(false);

            if (outputResult.ExceededLimit || errorResult.ExceededLimit)
            {
                throw new WindowsInspectionException(
                    "The Windows inspection exceeded its output limit.");
            }

            if (process.ExitCode != 0 || outputResult.Text.Length == 0)
            {
                throw new WindowsInspectionException("The Windows inspection returned an invalid response.");
            }

            return outputResult.Text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new WindowsInspectionException("The Windows inspection timed out.");
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            TryTerminate(process);
            throw new WindowsInspectionException("The Windows inspection failed.", exception);
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static async Task<BoundedReadResult> ReadBoundedAsync(
        StreamReader reader,
        OutputCharacterBudget budget,
        Process process,
        bool capture,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var output = capture ? new StringBuilder() : null;
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return new BoundedReadResult(output?.ToString() ?? string.Empty, false);
            }

            if (!budget.TryConsume(count))
            {
                TryTerminate(process);
                return new BoundedReadResult(string.Empty, true);
            }

            output?.Append(buffer, 0, count);
        }
    }

    private sealed record BoundedReadResult(string Text, bool ExceededLimit);

    private sealed class OutputCharacterBudget(int limit)
    {
        private int consumed;

        internal bool TryConsume(int count) => Interlocked.Add(ref consumed, count) <= limit;
    }
}
