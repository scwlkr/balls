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
                foreach ($rule in @(NetSecurity\Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True -Direction Inbound -Action Allow -ErrorAction Stop)) {
                    $profiles = @(([string]$rule.Profile -split ',') | ForEach-Object { $_.Trim() })
                    if (($profiles -notcontains 'Any') -and ($profiles -notcontains 'Public')) { continue }

                    foreach ($portFilter in @($rule | NetSecurity\Get-NetFirewallPortFilter -ErrorAction Stop)) {
                        $protocol = [string]$portFilter.Protocol
                        if ($protocol -notin @('Any', 'TCP', '6')) { continue }

                        foreach ($localPort in @($portFilter.LocalPort)) {
                            $portText = [string]$localPort
                            if ($portText -in @('Any', '445')) {
                                $publicSmbInboundAllowRules++
                                break
                            }

                            if ($portText -match '^(\d+)-(\d+)$') {
                                if (([int]$Matches[1] -le 445) -and ([int]$Matches[2] -ge 445)) {
                                    $publicSmbInboundAllowRules++
                                    break
                                }
                            }
                        }
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumOutputCharacters, 0);
        if (startInfo.UseShellExecute
            || !startInfo.RedirectStandardOutput
            || !startInfo.RedirectStandardError)
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
