using System.Diagnostics;
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
            Check("product", value.ProductCount == 1 && value.ProductVersion == "27.0.4.412",
                "product_exact", "product_missing_or_ambiguous", "Autodesk Revit Server 2027 is installed.", "Install exactly Revit Server 2027."),
            Check("roles", RolesAreExact(value.RoleValue) && !value.AcceleratorPresent,
                "roles_exact", "roles_incorrect", "Host + Admin are enabled and Accelerator is off.", "Return to Autodesk setup and configure Host + Admin with Accelerator off."),
            Check("storage", value.ProjectsPresent && value.CachePresent && value.PathsNotReparse && value.ProjectsEmpty,
                "storage_healthy", "storage_incomplete", "The approved Projects and Cache paths are usable.", "Complete Autodesk configuration using the displayed D: paths."),
            Check("folder-access", value.ProjectsNetworkServiceAcl && value.ProjectsCreatorOwnerAcl
                && value.CacheNetworkServiceAcl && value.CacheCreatorOwnerAcl,
                "acl_exact", "acl_incomplete", "The portable repository access rules are present.", "Retry Windows preparation to restore the approved folder access."),
            Check("iis", value.DefaultWebSiteStarted && value.AppPoolStarted && value.AppPoolIntegrated
                && value.AppPoolRuntimeV4 && value.ApplicationsExact,
                "iis_healthy", "iis_incomplete", "The Revit Server IIS applications and Integrated application pool are running.", "Finish Autodesk configuration, then verify again."),
            Check("local-endpoints", value.HostEndpointResponded && value.AdminEndpointResponded && value.AdminSurfaceHealthy,
                "endpoints_healthy", "endpoints_unavailable", "The Host services and empty Revit Server Administrator Host tree respond locally.", "Open Revit Server Administrator locally, resolve its error, then verify again."),
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
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "-" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException();
        await process.StandardInput.WriteAsync(Script.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
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
        $productIdentities = @($products | ForEach-Object { "$($_.DisplayName)|$($_.DisplayVersion)" } | Sort-Object -Unique)
        $role = [Environment]::GetEnvironmentVariable('RSROLE2027', 'Machine')
        $accelerator = @(
          [Environment]::GetEnvironmentVariable('RSACCELERATOR2027', 'Machine'),
          [Environment]::GetEnvironmentVariable('RSACCELERATOR2027', 'User'),
          [Environment]::GetEnvironmentVariable('RSACCELERATOR2027', 'Process')
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $root = 'D:\RevitServer\2027'
        $projects = "$root\Projects"
        $cache = "$root\Cache"
        $paths = @($root,$projects,$cache) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-Item -LiteralPath $_ -Force }
        function Test-AclPrincipal([string]$path, [string]$sid) {
          if (-not (Test-Path -LiteralPath $path)) { return $false }
          $acl = Get-Acl -LiteralPath $path
          return @($acl.Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq $sid -and ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) }).Count -gt 0
        }

        Import-Module WebAdministration
        $site = Get-Website -Name 'Default Web Site' -ErrorAction SilentlyContinue
        $pool = Get-Item 'IIS:\AppPools\RevitServerAppPool2027' -ErrorAction SilentlyContinue
        $apps = @(Get-WebApplication -Site 'Default Web Site' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path)
        $expectedApps = @('/AdminService2027','/LocalService2027','/ModelService2027','/RevitServerAdmin2027','/RevitServerAdminRESTService2027')
        $revitApps = @($apps | Where-Object { $_ -match '^/(AdminService2027|LocalService2027|ModelService2027|RevitServer)' } | Sort-Object -Unique)
        function Test-LocalUrl([string]$url) {
          try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 10
            return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
          } catch {
            if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode -lt 500 }
            return $false
          }
        }
        $localServiceOk = Test-LocalUrl 'http://127.0.0.1/LocalService2027/LocalService.svc'
        $modelServiceOk = Test-LocalUrl 'http://127.0.0.1/ModelService2027/ModelService.svc'
        $adminServiceOk = Test-LocalUrl 'http://127.0.0.1/AdminService2027/AdminService.svc'
        $adminUiOk = Test-LocalUrl 'http://127.0.0.1/RevitServerAdmin2027/'
        $encodedHost = [uri]::EscapeDataString($env:COMPUTERNAME)
        $adminSurfaceOk = $false
        try {
          $servers = @((Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1/RevitServerAdmin2027/api/server/servers?id=127.0.0.1&refresh=true' -TimeoutSec 10).Content | ConvertFrom-Json)
          $tree = (Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1/RevitServerAdmin2027/api/folder/SubItems?id=$encodedHost&depth=2" -TimeoutSec 10).Content | ConvertFrom-Json
          $details = (Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1/RevitServerAdmin2027/api/server/details?id=$encodedHost" -TimeoutSec 10).Content | ConvertFrom-Json
          $matchingServers = @($servers | Where-Object {
            $_.Id -eq $env:COMPUTERNAME -and $_.Name -eq $env:COMPUTERNAME -and $_.Roles -eq 'Host, Admin' -and
            $_.IsAlive -eq $true -and $_.ModelCount -eq 0 -and $_.FolderCount -eq 0
          })
          $adminSurfaceOk = $matchingServers.Count -eq 1 -and
            $tree.Id -eq $env:COMPUTERNAME -and $tree.Name -eq $env:COMPUTERNAME -and $tree.IsAlive -eq $true -and $null -eq $tree.Children -and
            $details.Id -eq $env:COMPUTERNAME -and $details.Name -eq $env:COMPUTERNAME -and $details.Roles -eq 'Host, Admin' -and
            $details.IsAlive -eq $true -and $details.ModelCount -eq 0 -and $details.FolderCount -eq 0
        } catch {
          $adminSurfaceOk = $false
        }

        $rsnPath = 'C:\ProgramData\Autodesk\Revit Server 2027\Config\RSN.ini'
        $rsnLines = @()
        if (Test-Path -LiteralPath $rsnPath) {
          $rsnLines = @(Get-Content -LiteralPath $rsnPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        $profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
        $httpRule = Get-NetFirewallRule -Name 'Balls-RevitServer-2027-HTTP' -ErrorAction SilentlyContinue
        $icmpRule = Get-NetFirewallRule -Name 'Balls-RevitServer-2027-ICMPv4' -ErrorAction SilentlyContinue
        $httpPort = if ($httpRule) { Get-NetFirewallPortFilter -AssociatedNetFirewallRule $httpRule } else { $null }
        $httpAddress = if ($httpRule) { Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $httpRule } else { $null }
        $icmpPort = if ($icmpRule) { Get-NetFirewallPortFilter -AssociatedNetFirewallRule $icmpRule } else { $null }
        $icmpAddress = if ($icmpRule) { Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $icmpRule } else { $null }
        $firewallExact = $httpRule -and $icmpRule -and
          $httpRule.Enabled -eq 'True' -and $icmpRule.Enabled -eq 'True' -and
          $httpRule.Profile -eq 'Private' -and $icmpRule.Profile -eq 'Private' -and
          $httpRule.Direction -eq 'Inbound' -and $icmpRule.Direction -eq 'Inbound' -and
          $httpRule.Action -eq 'Allow' -and $icmpRule.Action -eq 'Allow' -and
          @($httpPort.Protocol).Count -eq 1 -and $httpPort.Protocol -eq 'TCP' -and
          (@($httpPort.LocalPort | ForEach-Object { $_ -split ',' }) | Sort-Object) -join ',' -eq '80,808' -and
          $httpAddress.RemoteAddress -eq 'LocalSubnet' -and
          $icmpPort.Protocol -eq 'ICMPv4' -and $icmpPort.IcmpType -eq '8' -and
          $icmpAddress.RemoteAddress -eq 'LocalSubnet'
        $shared = @(Get-SmbShare -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
        $fatal = 0
        $logRoot = 'C:\ProgramData\Autodesk\Revit Server 2027\Logs'
        if (Test-Path -LiteralPath $logRoot) {
          $fatal = @(Get-ChildItem -LiteralPath $logRoot -File -ErrorAction SilentlyContinue | Select-Object -First 20 | Select-String -Pattern 'fatal|unhandled exception' -CaseSensitive:$false -ErrorAction SilentlyContinue).Count
        }
        [pscustomobject]@{
          ProductCount=$productIdentities.Count
          ProductVersion=if ($productIdentities.Count -eq 1) { [string]$products[0].DisplayVersion } else { '' }
          RoleValue=[string]$role
          AcceleratorPresent=$accelerator.Count -gt 0 -or @($apps | Where-Object { $_ -match 'Accelerator' }).Count -gt 0
          ProjectsPresent=Test-Path -LiteralPath $projects -PathType Container
          CachePresent=Test-Path -LiteralPath $cache -PathType Container
          PathsNotReparse=@($paths | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -eq 0 -and $paths.Count -eq 3
          ProjectsEmpty=(Test-Path -LiteralPath $projects) -and @(Get-ChildItem -LiteralPath $projects -Force -ErrorAction SilentlyContinue).Count -eq 0
          ProjectsNetworkServiceAcl=Test-AclPrincipal $projects 'S-1-5-20'
          ProjectsCreatorOwnerAcl=Test-AclPrincipal $projects 'S-1-3-0'
          CacheNetworkServiceAcl=Test-AclPrincipal $cache 'S-1-5-20'
          CacheCreatorOwnerAcl=Test-AclPrincipal $cache 'S-1-3-0'
          DefaultWebSiteStarted=$site -and $site.State -eq 'Started'
          AppPoolStarted=$pool -and $pool.State -eq 'Started'
          AppPoolIntegrated=$pool -and $pool.managedPipelineMode -eq 'Integrated'
          AppPoolRuntimeV4=$pool -and $pool.managedRuntimeVersion -eq 'v4.0'
          ApplicationsExact=(@(Compare-Object $expectedApps $revitApps).Count -eq 0)
          HostEndpointResponded=$localServiceOk -and $modelServiceOk
          AdminEndpointResponded=$adminServiceOk -and $adminUiOk
          AdminSurfaceHealthy=$adminSurfaceOk
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
    bool ProjectsEmpty,
    bool ProjectsNetworkServiceAcl,
    bool ProjectsCreatorOwnerAcl,
    bool CacheNetworkServiceAcl,
    bool CacheCreatorOwnerAcl,
    bool DefaultWebSiteStarted,
    bool AppPoolStarted,
    bool AppPoolIntegrated,
    bool AppPoolRuntimeV4,
    bool ApplicationsExact,
    bool HostEndpointResponded,
    bool AdminEndpointResponded,
    bool AdminSurfaceHealthy,
    bool RsnExact,
    bool PrivateProfileOnly,
    bool FirewallExact,
    bool RepositoryShared,
    int FatalLogCount);
