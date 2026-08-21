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
                $firewall = [PSCustomObject]@{
                    PrivateEnabled = [bool]$privateFirewall.Enabled
                    PrivateDefaultInboundAction = [string]$privateFirewall.DefaultInboundAction
                    PublicEnabled = [bool]$publicFirewall.Enabled
                    PublicDefaultInboundAction = [string]$publicFirewall.DefaultInboundAction
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
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await standardOutput.ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);

            if (process.ExitCode != 0 || output.Length == 0 || output.Length > maximumOutputCharacters)
            {
                throw new WindowsInspectionException("The Windows inspection returned an invalid response.");
            }

            return output;
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
}
