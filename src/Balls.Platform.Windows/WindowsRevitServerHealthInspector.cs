using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Balls.Platform;

namespace Balls.Platform.Windows;

public sealed class WindowsRevitServerHealthInspector : IRevitServerHealthInspector
{
    private readonly IWindowsRevitServerHealthJsonSource source;

    public WindowsRevitServerHealthInspector()
        : this(new WindowsRevitServerHealthPowerShellSource())
    {
    }

    internal WindowsRevitServerHealthInspector(IWindowsRevitServerHealthJsonSource source)
    {
        this.source = source;
    }

    public async ValueTask<RevitServerHealthReport> InspectAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Evaluate(Parse(await source.QueryAsync(cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new RevitServerHealthReport(
                RevitServerHealthStatus.Blocked,
                "Windows could not completely verify Revit Server health.",
                [new RevitServerHealthCheck(
                    "inspection",
                    RevitServerHealthStatus.Blocked,
                    "health_inspection_incomplete",
                    "Reopen Balls as the same server administrator and verify again.")]);
        }
    }

    internal static RevitServerHealthReport Evaluate(WindowsRevitServerHealthObservation value)
    {
        var checks = new[]
        {
            Check("product", value.ProductCount == 1 && value.ProductVersion.StartsWith("27.", StringComparison.Ordinal),
                "product_exact", "product_missing_or_ambiguous", "Autodesk Revit Server 2027 is installed.", "Install exactly Revit Server 2027."),
            Check("roles", RolesAreExact(value.RoleValue) && !value.AcceleratorPresent,
                "roles_exact", "roles_incorrect", "Host + Admin are enabled and Accelerator is off.", "Return to Autodesk setup and configure Host + Admin with Accelerator off."),
            Check("storage", value.ProjectsPresent && value.CachePresent && value.PathsNotReparse && value.ProjectTreeUsable,
                "storage_healthy", "storage_incomplete", "The approved Projects and Cache paths are usable.", "Complete Autodesk configuration using the displayed D: paths."),
            Check("folder-access", value.NetworkServiceAcl && value.CreatorOwnerAcl,
                "acl_exact", "acl_incomplete", "The portable repository access rules are present.", "Retry Windows preparation to restore the approved folder access."),
            Check("iis", value.DefaultWebSiteStarted && value.AppPoolStarted && value.AppPoolIntegrated
                && value.HostApplication && value.AdminApplication && value.AdminRestApplication,
                "iis_healthy", "iis_incomplete", "The Revit Server IIS applications and Integrated application pool are running.", "Finish Autodesk configuration, then verify again."),
            Check("local-endpoints", value.HostEndpointResponded && value.AdminEndpointResponded,
                "endpoints_healthy", "endpoints_unavailable", "The Host service and Revit Server Administrator respond locally.", "Open Revit Server Administrator locally, resolve its error, then verify again."),
            Check("host-list", value.RsnExact,
                "rsn_exact", "rsn_incomplete", "Server-local RSN.ini names this Host exactly once.", "Retry Windows preparation, then verify the local Host list."),
            Check("network", value.PrivateProfileOnly && value.FirewallExact && !value.RepositoryShared,
                "network_private", "network_exposure", "Revit Server is limited to the Private local subnet on ports 80 and 808 with private ICMP.", "Remove Public-network exposure or repository sharing, then verify again."),
            Check("logs", value.FatalLogCount == 0,
                "logs_clear", "fatal_log_detected", "No fatal Revit Server setup error was found.", "Review the Revit Server setup log and correct the fatal error."),
        };
        var healthy = checks.All(check => check.Status == RevitServerHealthStatus.Healthy);
        return new RevitServerHealthReport(
            healthy ? RevitServerHealthStatus.Healthy : RevitServerHealthStatus.Incomplete,
            healthy
                ? "Revit Server 2027 Host + Admin is healthy on this local server."
                : "Revit Server setup is incomplete. Follow the blocked checks and verify again.",
            checks);
    }

    private static bool RolesAreExact(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(new[] { "Admin", "Host" }, StringComparer.OrdinalIgnoreCase);

    private static RevitServerHealthCheck Check(
        string id,
        bool healthy,
        string passCode,
        string failCode,
        string passSummary,
        string failSummary) =>
        new(
            id,
            healthy ? RevitServerHealthStatus.Healthy : RevitServerHealthStatus.Incomplete,
            healthy ? passCode : failCode,
            healthy ? passSummary : failSummary);

    private static WindowsRevitServerHealthObservation Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 64 * 1024)
        {
            throw new JsonException();
        }

        return JsonSerializer.Deserialize<WindowsRevitServerHealthObservation>(json, JsonOptions)
            ?? throw new JsonException();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}

internal interface IWindowsRevitServerHealthJsonSource
{
    ValueTask<string> QueryAsync(CancellationToken cancellationToken);
}

internal sealed class WindowsRevitServerHealthPowerShellSource : IWindowsRevitServerHealthJsonSource
{
    public async ValueTask<string> QueryAsync(CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        _ = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new IOException();
        }

        return output;
    }

    internal const string Script = """
        $ErrorActionPreference = 'Stop'
        $products = @()
        foreach ($key in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*') {
          $products += @(Get-ItemProperty $key -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq 'Autodesk Revit Server 2027' })
        }
        $role = [Environment]::GetEnvironmentVariable('RSROLE 2027 Release', 'Machine')
        $accelerator = @(
          [Environment]::GetEnvironmentVariable('RSACCELERATOR2027', 'Machine'),
          [Environment]::GetEnvironmentVariable('RSACCELERATOR2027', 'User'),
          [Environment]::GetEnvironmentVariable('RSACCELERATOR2027', 'Process')
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $root = 'D:\RevitServer\2027'
        $projects = "$root\Projects"
        $cache = "$root\Cache"
        $paths = @($root,$projects,$cache) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-Item -LiteralPath $_ -Force }
        $acl = if (Test-Path -LiteralPath $projects) { Get-Acl -LiteralPath $projects } else { $null }
        $networkService = $acl -and @($acl.Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq 'S-1-5-20' -and ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) }).Count -gt 0
        $creatorOwner = $acl -and @($acl.Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq 'S-1-3-0' -and ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) }).Count -gt 0

        Import-Module WebAdministration
        $site = Get-Website -Name 'Default Web Site' -ErrorAction SilentlyContinue
        $pool = Get-Item 'IIS:\AppPools\RevitServerAppPool 2027 Release' -ErrorAction SilentlyContinue
        $apps = @(Get-WebApplication -Site 'Default Web Site' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path)
        function Test-LocalUrl([string]$url) {
          try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 10
            return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
          } catch {
            if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode -lt 500 }
            return $false
          }
        }
        $hostOk = Test-LocalUrl 'http://127.0.0.1/RevitServer2027/HostService.svc'
        $adminOk = Test-LocalUrl 'http://127.0.0.1/RevitServerAdmin2027/'

        $rsnPath = 'C:\ProgramData\Autodesk\Revit Server 2027\Config\RSN.ini'
        $rsnLines = if (Test-Path -LiteralPath $rsnPath) { @(Get-Content -LiteralPath $rsnPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) } else { @() }
        $profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
        $httpRule = Get-NetFirewallRule -Name 'Balls-RevitServer-2027-HTTP' -ErrorAction SilentlyContinue
        $icmpRule = Get-NetFirewallRule -Name 'Balls-RevitServer-2027-ICMPv4' -ErrorAction SilentlyContinue
        $firewallExact = $httpRule -and $icmpRule -and $httpRule.Enabled -eq 'True' -and $icmpRule.Enabled -eq 'True' -and $httpRule.Profile -eq 'Private' -and $icmpRule.Profile -eq 'Private'
        $shared = @(Get-SmbShare -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
        $fatal = 0
        $logRoot = 'C:\ProgramData\Autodesk\Revit Server 2027\Logs'
        if (Test-Path -LiteralPath $logRoot) {
          $fatal = @(Get-ChildItem -LiteralPath $logRoot -File -ErrorAction SilentlyContinue | Select-Object -First 20 | Select-String -Pattern 'fatal|unhandled exception' -CaseSensitive:$false -ErrorAction SilentlyContinue).Count
        }
        [pscustomobject]@{
          ProductCount=$products.Count
          ProductVersion=if ($products.Count -eq 1) { [string]$products[0].DisplayVersion } else { '' }
          RoleValue=[string]$role
          AcceleratorPresent=$accelerator.Count -gt 0
          ProjectsPresent=Test-Path -LiteralPath $projects -PathType Container
          CachePresent=Test-Path -LiteralPath $cache -PathType Container
          PathsNotReparse=@($paths | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -eq 0 -and $paths.Count -eq 3
          ProjectTreeUsable=(Test-Path -LiteralPath $projects) -and (Test-Path -LiteralPath $cache)
          NetworkServiceAcl=[bool]$networkService
          CreatorOwnerAcl=[bool]$creatorOwner
          DefaultWebSiteStarted=$site -and $site.State -eq 'Started'
          AppPoolStarted=$pool -and $pool.State -eq 'Started'
          AppPoolIntegrated=$pool -and $pool.managedPipelineMode -eq 'Integrated'
          HostApplication=$apps -contains '/RevitServer2027'
          AdminApplication=$apps -contains '/RevitServerAdmin2027'
          AdminRestApplication=$apps -contains '/RevitServerAdminRESTService2027'
          HostEndpointResponded=$hostOk
          AdminEndpointResponded=$adminOk
          RsnExact=$rsnLines.Count -eq 1 -and $rsnLines[0].Trim() -eq $env:COMPUTERNAME
          PrivateProfileOnly=$profiles.Count -gt 0 -and @($profiles | Where-Object { $_.NetworkCategory -ne 'Private' }).Count -eq 0
          FirewallExact=[bool]$firewallExact
          RepositoryShared=$shared
          FatalLogCount=$fatal
        } | ConvertTo-Json -Compress
        """;
}

internal sealed record WindowsRevitServerHealthObservation(
    int ProductCount,
    string ProductVersion,
    string RoleValue,
    bool AcceleratorPresent,
    bool ProjectsPresent,
    bool CachePresent,
    bool PathsNotReparse,
    bool ProjectTreeUsable,
    bool NetworkServiceAcl,
    bool CreatorOwnerAcl,
    bool DefaultWebSiteStarted,
    bool AppPoolStarted,
    bool AppPoolIntegrated,
    bool HostApplication,
    bool AdminApplication,
    bool AdminRestApplication,
    bool HostEndpointResponded,
    bool AdminEndpointResponded,
    bool RsnExact,
    bool PrivateProfileOnly,
    bool FirewallExact,
    bool RepositoryShared,
    int FatalLogCount);
