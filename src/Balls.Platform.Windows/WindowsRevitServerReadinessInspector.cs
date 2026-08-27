using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

public sealed class WindowsRevitServerReadinessInspector : IRevitServerReadinessInspector
{
    internal const string RepositoryRoot = @"D:\RevitServer\2027";
    private const string ExpectedMediaSha256 = "295b30779868b9d58d78d9ff4353e4b9c6412418274a8034db6c6e7e0d348518";
    private const long ExpectedMediaLength = 912_600_144;
    private readonly IWindowsRevitServerJsonSource source;

    public WindowsRevitServerReadinessInspector()
        : this(new WindowsRevitServerPowerShellSource())
    {
    }

    internal WindowsRevitServerReadinessInspector(IWindowsRevitServerJsonSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async ValueTask<RevitServerInspectionReport> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || mediaPath.Length > 4096)
        {
            return Failed("installer_path_required", "Choose the locally cached Autodesk installer.");
        }

        try
        {
            var fullPath = Path.GetFullPath(mediaPath);
            var json = await source.QueryAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return Evaluate(Parse(json), fullPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedInspectionFailure(exception))
        {
            return Failed("inspection_failed", "Windows could not complete the read-only setup inspection. Try again from the prepared server.");
        }
    }

    internal static RevitServerInspectionReport Evaluate(
        WindowsRevitServerObservation value,
        string mediaPath)
    {
        ArgumentNullException.ThrowIfNull(value);
        var checks = new[]
        {
            Platform(value),
            PendingRestart(value),
            Hostname(value),
            DataVolume(value),
            Repository(value),
            RepositoryExposure(value),
            Iis(value),
            Network(value),
            ExistingRoles(value),
            ForeignState(value),
            Media(value),
        };
        var ready = checks.All(check => check.Status == RevitServerReadinessStatus.Ready);
        if (!ready)
        {
            return new RevitServerInspectionReport(
                RevitServerReadinessStatus.Blocked,
                "Setup is blocked. Resolve the listed items, then inspect again. Nothing was changed.",
                checks,
                null);
        }

        var media = value.Media!;
        var normalizedPath = Path.GetFullPath(mediaPath).ToUpperInvariant();
        var approvalSnapshotIdentity = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                normalizedPath + "\n" + JsonSerializer.Serialize(value)))).ToLowerInvariant();
        return new RevitServerInspectionReport(
            RevitServerReadinessStatus.Ready,
            "Ready to review the exact Host + Admin setup plan. Nothing has changed yet.",
            checks,
            new RevitServerInspectionSnapshot(
                value.Hostname!,
                value.System!.Caption!,
                value.System.BuildNumber!.Value,
                value.System.InstallationType!,
                "D:",
                value.DataVolume!.FreeBytes!.Value,
                RepositoryRoot,
                approvalSnapshotIdentity,
                new RevitServerMediaIdentity(
                    Path.GetFileName(mediaPath),
                    media.SignerName!,
                    media.ProductName!,
                    media.ProductVersion!,
                    media.Sha256!.ToLowerInvariant()),
                value.Iis!.DefaultWebSitePresent!.Value,
                value.Iis.PresentPrerequisites ?? []));
    }

    private static RevitServerReadinessCheck Platform(WindowsRevitServerObservation value)
    {
        var system = value.System;
        if (system?.BuildNumber is null
            || system.Caption is null
            || system.InstallationType is null
            || system.ProductType is null)
        {
            return Blocked("windows-server", "windows_identity_unknown", "Windows did not report a complete server identity.");
        }

        if (system.ProductType == 1)
        {
            return Blocked("windows-server", "windows_client_unsupported", "Revit Server setup is supported here only on Windows Server 2022 Desktop Experience, not Windows 11.");
        }

        if (system.BuildNumber != 20348 || !system.Caption.Contains("Windows Server 2022", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("windows-server", "windows_server_build_unsupported", "Use Windows Server 2022 Desktop Experience build 20348 for this setup.");
        }

        if (system.InstallationType.Contains("Core", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("windows-server", "server_core_unsupported", "Server Core is not supported. Use Windows Server 2022 Desktop Experience.");
        }

        if (!system.InstallationType.Contains("Server", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("windows-server", "desktop_experience_unknown", "Windows did not confirm the Desktop Experience installation type.");
        }

        return Ready("windows-server", "windows_server_2022_desktop_ready", "Windows Server 2022 Desktop Experience is supported.");
    }

    private static RevitServerReadinessCheck PendingRestart(WindowsRevitServerObservation value) =>
        value.PendingRestart switch
        {
            false => Ready("pending-restart", "no_pending_restart", "Windows reports no pending restart."),
            true => Blocked("pending-restart", "restart_required", "Restart Windows, finish updates, and inspect again."),
            _ => Blocked("pending-restart", "restart_state_unknown", "Windows did not report whether a restart is pending."),
        };

    private static RevitServerReadinessCheck Hostname(WindowsRevitServerObservation value)
    {
        if (string.IsNullOrWhiteSpace(value.Hostname))
        {
            return Blocked("hostname", "hostname_unknown", "Set the server's final temporary hostname, then inspect again.");
        }

        if (value.PendingHostnameRename is true)
        {
            return Blocked("hostname", "hostname_restart_pending", "Restart Windows to finish the hostname change, then inspect the final name again.");
        }
        if (value.PendingHostnameRename is null)
        {
            return Blocked("hostname", "hostname_state_unknown", "Windows did not confirm that the active and configured hostnames match.");
        }

        if (value.Hostname.Length > 63 || value.Hostname.StartsWith('_'))
        {
            return Blocked("hostname", "hostname_unsupported", "Use a hostname of at most 63 characters that does not begin with an underscore.");
        }

        return Ready("hostname", "hostname_ready", $"The setup plan will use {value.Hostname} as the Revit Host name.");
    }

    private static RevitServerReadinessCheck DataVolume(WindowsRevitServerObservation value)
    {
        var volume = value.DataVolume;
        if (volume?.DriveType is null || volume.FileSystem is null || volume.FreeBytes is null)
        {
            return Blocked("data-volume", "data_volume_missing", "Attach the fixed local NTFS D: data volume, then inspect again.");
        }

        if (volume.DriveType != 3 || !string.Equals(volume.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("data-volume", "data_volume_unsupported", "D: must be a fixed local NTFS data volume.");
        }

        if (volume.FreeBytes <= 0)
        {
            return Blocked("data-volume", "data_volume_full", "D: has no reported free space. Free capacity and inspect again.");
        }

        var gib = volume.FreeBytes.Value / (1024L * 1024 * 1024);
        return Ready("data-volume", "data_volume_ready", $"The fixed local NTFS D: volume reports {gib} GiB free.");
    }

    private static RevitServerReadinessCheck Repository(WindowsRevitServerObservation value)
    {
        var repository = value.Repository;
        if (repository is null)
        {
            return Blocked("repository", "repository_state_unknown", "Windows did not report the planned repository state.");
        }

        if (repository.ReparseTraversal is true)
        {
            return Blocked("repository", "repository_reparse_path", "D:\\RevitServer\\2027 cannot traverse a link or reparse point.");
        }

        if (repository.NonEmpty is true)
        {
            return Blocked("repository", "repository_not_empty", "D:\\RevitServer\\2027 contains foreign files. Choose a clean disposable server; Balls will not delete them.");
        }

        if (repository.ReparseTraversal is null || repository.NonEmpty is null)
        {
            return Blocked("repository", "repository_state_unknown", "Windows did not completely inspect the planned repository.");
        }

        return Ready("repository", "repository_ready", "The version-isolated repository destination is absent or empty and has no reparse traversal.");
    }

    private static RevitServerReadinessCheck RepositoryExposure(WindowsRevitServerObservation value)
    {
        var repository = value.Repository;
        if (repository?.ShareOverlapCount is null || repository.MountOverlapCount is null)
        {
            return Blocked("repository-exposure", "repository_exposure_unknown", "Windows did not completely inspect shares and mounted paths reaching the repository.");
        }

        if (repository.ShareOverlapCount > 0 || repository.MountOverlapCount > 0)
        {
            return Blocked("repository-exposure", "repository_exposed", "The Revit repository cannot be reached through a share or mounted path.");
        }

        return Ready("repository-exposure", "repository_private", "No Windows share or mounted path reaches the Revit repository.");
    }

    private static RevitServerReadinessCheck Iis(WindowsRevitServerObservation value)
    {
        var iis = value.Iis;
        if (iis?.DefaultWebSitePresent is null
            || iis.ConflictingDefaultSiteCount is null
            || iis.AmbiguousBindingCount is null)
        {
            return Blocked("iis", "iis_state_unknown", "Windows did not report an unambiguous IIS Default Web Site state.");
        }

        if (iis.ConflictingDefaultSiteCount > 0 || iis.AmbiguousBindingCount > 0)
        {
            return Blocked("iis", "iis_default_site_conflict", "Resolve the ambiguous Default Web Site configuration before setup.");
        }

        return Ready(
            "iis",
            iis.DefaultWebSitePresent.Value ? "default_web_site_ready" : "default_web_site_will_be_created",
            iis.DefaultWebSitePresent.Value
                ? "The existing unambiguous Default Web Site will be preserved."
                : "The approved setup will create the missing Default Web Site; this inspection did not change IIS.");
    }

    private static RevitServerReadinessCheck Network(WindowsRevitServerObservation value)
    {
        var network = value.Network;
        if (network?.ConnectedPrivateProfiles is null
            || network.ConnectedPublicProfiles is null
            || network.PublicExposureCount is null
            || network.PrivateFirewallEnabled is null
            || network.PublicFirewallEnabled is null
            || network.PrivateDefaultInboundAction is null
            || network.PublicDefaultInboundAction is null)
        {
            return Blocked("network", "network_state_unknown", "Windows did not completely inspect the local network and Public-profile exposure.");
        }

        if (network.ConnectedPublicProfiles > 0 || network.PublicExposureCount > 0)
        {
            return Blocked("network", "public_network_refused", "Disconnect or reclassify Public-network exposure before Revit Server setup.");
        }

        if (!network.PrivateFirewallEnabled.Value
            || !network.PublicFirewallEnabled.Value
            || !string.Equals(network.PrivateDefaultInboundAction, "Block", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(network.PublicDefaultInboundAction, "Block", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("network", "firewall_boundary_unsafe", "Enable Windows Firewall with default inbound blocking on Private and Public profiles.");
        }

        if (network.ConnectedPrivateProfiles < 1)
        {
            return Blocked("network", "private_network_required", "Connect the server to the isolated Private lab network, then inspect again.");
        }

        return Ready("network", "private_network_ready", "The server is on a Private network with no detected Public-profile exposure.");
    }

    private static RevitServerReadinessCheck ExistingRoles(WindowsRevitServerObservation value)
    {
        var roles = value.RevitState;
        if (roles?.RoleMarkers is null)
        {
            return Blocked("existing-roles", "revit_roles_unknown", "Windows did not completely inspect existing Revit Server roles.");
        }

        return roles.RoleMarkers.Count > 0
            ? Blocked("existing-roles", "revit_roles_present", "Existing Revit Server roles were detected. This clean setup will not rewrite them.")
            : Ready("existing-roles", "revit_roles_absent", "No existing Host, Admin, or Accelerator role markers were detected.");
    }

    private static RevitServerReadinessCheck ForeignState(WindowsRevitServerObservation value)
    {
        var state = value.RevitState;
        if (state?.ForeignStateCount is null)
        {
            return Blocked("foreign-state", "revit_state_unknown", "Windows did not completely inspect installed Revit products, services, and IIS applications.");
        }

        return state.ForeignStateCount > 0
            ? Blocked("foreign-state", "foreign_revit_state", "Existing Revit Server or conflicting Revit state was detected. Balls will not remove it.")
            : Ready("foreign-state", "foreign_revit_state_absent", "No conflicting Revit Server product, service, or IIS application was detected.");
    }

    private static RevitServerReadinessCheck Media(WindowsRevitServerObservation value)
    {
        var media = value.Media;
        if (media?.Exists is not true)
        {
            return Blocked("installer", "installer_missing", "Choose an extracted local Autodesk Revit Server 2027 installer executable.");
        }

        if (media.LocalFixed is not true || media.ReparseTraversal is not false)
        {
            return Blocked("installer", "installer_location_unsafe", "Choose one regular installer file on a fixed local path with no link or reparse traversal.");
        }

        if (media.StableIdentity is not true)
        {
            return Blocked("installer", "installer_changed_during_inspection", "The installer changed while it was being verified. Choose it again after the copy finishes.");
        }

        if (!string.Equals(media.SignatureStatus, "Valid", StringComparison.Ordinal)
            || !string.Equals(media.SignerName, "Autodesk, Inc.", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("installer", "installer_signature_untrusted", "The selected installer does not have a valid Autodesk, Inc. signature.");
        }

        if (!string.Equals(media.FileName, "Revit_Server_2027_win_db.sfx.exe", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(media.ProductName)
            || !media.ProductName.Contains("Revit", StringComparison.OrdinalIgnoreCase)
            || (!media.ProductName.Contains("2027", StringComparison.OrdinalIgnoreCase)
                && !(media.ProductVersion?.StartsWith("27.", StringComparison.Ordinal) ?? false)))
        {
            return Blocked("installer", "installer_product_ambiguous", "The signed file does not identify the official Revit_Server_2027_win_db.sfx.exe media unambiguously.");
        }

        if (string.IsNullOrWhiteSpace(media.ProductVersion)
            || string.IsNullOrWhiteSpace(media.Sha256)
            || media.Sha256.Length != 64
            || !media.Sha256.All(Uri.IsHexDigit))
        {
            return Blocked("installer", "installer_identity_incomplete", "Windows could not establish the selected installer's exact version and SHA-256.");
        }

        if (media.Length != ExpectedMediaLength
            || !string.Equals(media.Sha256, ExpectedMediaSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("installer", "installer_identity_substituted", "The selected file does not match the owner-accepted official Revit Server 2027 media identity.");
        }

        return Ready("installer", "official_revit_2027_media_verified", "The selected Autodesk Revit Server 2027 media has a valid publisher signature and SHA-256 identity.");
    }

    private static RevitServerReadinessCheck Ready(string id, string code, string summary) =>
        new(id, RevitServerReadinessStatus.Ready, code, summary);

    private static RevitServerReadinessCheck Blocked(string id, string code, string summary) =>
        new(id, RevitServerReadinessStatus.Blocked, code, summary);

    private static RevitServerInspectionReport Failed(string code, string summary) =>
        new(
            RevitServerReadinessStatus.Blocked,
            "Setup is blocked. Nothing was changed.",
            [Blocked("inspection", code, summary)],
            null);

    private static WindowsRevitServerObservation Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 256 * 1024)
        {
            throw new JsonException("The Revit Server inspection response is empty or too large.");
        }

        return JsonSerializer.Deserialize<WindowsRevitServerObservation>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("The Revit Server inspection response is invalid.");
    }

    private static bool IsExpectedInspectionFailure(Exception exception) => exception is
        WindowsInspectionException or Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or SecurityException or JsonException or ArgumentException;
}

internal interface IWindowsRevitServerJsonSource
{
    ValueTask<string> QueryAsync(string mediaPath, CancellationToken cancellationToken);
}

internal sealed class WindowsRevitServerPowerShellSource : IWindowsRevitServerJsonSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public async ValueTask<string> QueryAsync(string mediaPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable))
        {
            throw new WindowsInspectionException("The Windows inspection host is unavailable.");
        }

        var start = new ProcessStartInfo
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
        start.Environment["BALLS_REVIT_MEDIA_B64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(mediaPath));
        return await BoundedWindowsInspectionProcessRunner.RunAsync(
            start, Timeout, 256 * 1024, cancellationToken).ConfigureAwait(false);
    }

    internal static string Script =>
        """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $root = 'D:\RevitServer\2027'
        $mediaPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:BALLS_REVIT_MEDIA_B64))

        $os = Get-CimInstance Win32_OperatingSystem
        $productType = (Get-CimInstance Win32_ComputerSystem).DomainRole
        $installType = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop).InstallationType
        $activeName = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName' -ErrorAction Stop).ComputerName
        $configuredName = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName' -ErrorAction Stop).ComputerName
        $pendingHostnameRename = -not [string]::Equals($activeName, $configuredName, [StringComparison]::OrdinalIgnoreCase)
        $pendingRestart = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
          (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or
          ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations.Count -gt 0)

        $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='D:'" -ErrorAction SilentlyContinue
        $parts = @($root.Split('\') | Where-Object { $_ })
        $current = 'D:\'
        $reparse = $false
        foreach ($part in $parts | Select-Object -Skip 1) {
          $current = Join-Path $current $part
          if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $reparse = $true }
          }
        }
        $nonEmpty = (Test-Path -LiteralPath $root) -and (@(Get-ChildItem -LiteralPath $root -Force -ErrorAction Stop).Count -gt 0)
        $shareOverlap = 0
        if (Get-Command Get-SmbShare -ErrorAction SilentlyContinue) {
          foreach ($share in @(Get-SmbShare -ErrorAction Stop | Where-Object Path)) {
            $sharePath = [IO.Path]::GetFullPath($share.Path).TrimEnd('\')
            if ($root.StartsWith($sharePath + '\', [StringComparison]::OrdinalIgnoreCase) -or $sharePath.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or $sharePath -eq $root) { $shareOverlap++ }
          }
        }
        $mountOverlap = 0
        if (Get-Command Get-Partition -ErrorAction SilentlyContinue) {
          foreach ($accessPath in @(Get-Partition | Select-Object -ExpandProperty AccessPaths | Where-Object { $_ })) {
            $normalizedAccess = $accessPath.TrimEnd('\')
            if ($normalizedAccess -ne 'D:' -and ($root.StartsWith($normalizedAccess + '\', [StringComparison]::OrdinalIgnoreCase) -or $normalizedAccess.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or $normalizedAccess -eq $root)) { $mountOverlap++ }
          }
        }
        $mountOverlap += @(Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -in 4,5 -and $_.DeviceID -eq 'D:' }).Count

        $features = @()
        if (Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue) {
          $requiredFeatures = @('Web-Server','NET-Framework-45-ASPNET','NET-WCF-HTTP-Activation45','NET-WCF-TCP-Activation45','Web-ASP','Web-CGI','Web-Includes','Web-Mgmt-Compat','Web-Scripting-Tools','Web-WMI')
          $features = @(Get-WindowsFeature -Name $requiredFeatures | Where-Object Installed | Select-Object -ExpandProperty Name)
        }
        $defaultSite = $false
        $siteConflicts = 0
        $ambiguousBindings = 0
        $foreignIis = 0
        if (Get-Module -ListAvailable WebAdministration) {
          Import-Module WebAdministration
          $sites = @(Get-Website -Name 'Default Web Site' -ErrorAction SilentlyContinue)
          $defaultSite = $sites.Count -eq 1
          if ($sites.Count -gt 1) { $siteConflicts = $sites.Count - 1 }
          if ($defaultSite) {
            $bindings = @(Get-WebBinding -Name 'Default Web Site')
            if ($bindings.Count -ne 1 -or $bindings[0].protocol -ne 'http' -or $bindings[0].bindingInformation -ne '*:80:') { $ambiguousBindings = 1 }
          }
          $foreignIis += @(Get-WebApplication | Where-Object { $_.Path -match 'Revit|RSHost|RSAdmin|RSAccelerator' }).Count
          $foreignIis += @(Get-ChildItem IIS:\AppPools | Where-Object { $_.Name -match 'Revit|RSHost|RSAdmin|RSAccelerator' }).Count
        }

        $profiles = @(Get-NetConnectionProfile -ErrorAction Stop)
        $firewallProfiles = @(Get-NetFirewallProfile -ErrorAction Stop)
        $privateFirewall = @($firewallProfiles | Where-Object Name -eq Private | Select-Object -First 1)
        $publicFirewall = @($firewallProfiles | Where-Object Name -eq Public | Select-Object -First 1)
        $publicExposure = 0
        foreach ($rule in @(Get-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -ErrorAction Stop | Where-Object { [string]$_.Profile -match 'Public|Any' })) {
          foreach ($filter in @($rule | Get-NetFirewallPortFilter -ErrorAction Stop)) {
            $protocol = [string]$filter.Protocol
            $ports = @(([string]$filter.LocalPort).Split(','))
            if (($protocol -in '6','TCP' -and @($ports | Where-Object { $_ -in 'Any','80','808' }).Count -gt 0) -or $protocol -in '1','ICMPv4') { $publicExposure++ }
          }
        }
        $roleMarkers = @()
        foreach ($name in 'RSROLE2027','RSACCELERATOR2027') {
          foreach ($target in 'Machine','User') {
            $found = [Environment]::GetEnvironmentVariable($name, $target)
            if (-not [string]::IsNullOrWhiteSpace($found)) { $roleMarkers += "$name:$target" }
          }
        }
        $foreign = 0
        foreach ($uninstall in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*') {
          $foreign += @(Get-ItemProperty $uninstall -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'Revit Server' }).Count
        }
        $foreign += @(Get-Service -ErrorAction Stop | Where-Object { $_.Name -match 'Revit.*Server|RSHost|RSAdmin|RSAccelerator' }).Count
        foreach ($registryPath in 'HKLM:\SOFTWARE\Autodesk\Revit Server 2027','HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit Server 2027') {
          if (Test-Path $registryPath) { $foreign++ }
        }
        foreach ($programPath in 'C:\ProgramData\Autodesk\Revit Server 2027','C:\Program Files\Autodesk\Revit Server 2027') {
          if (Test-Path -LiteralPath $programPath) { $foreign++ }
        }
        $foreign += $foreignIis

        $media = [ordered]@{ Exists = $false }
        if (Test-Path -LiteralPath $mediaPath -PathType Leaf) {
          $item = Get-Item -LiteralPath $mediaPath -Force
          $beforeLength = [long]$item.Length
          $beforeWrite = $item.LastWriteTimeUtc.Ticks
          $mediaReparse = [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
          $ancestor = $item.Directory
          while ($ancestor) {
            if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { $mediaReparse = $true }
            $ancestor = $ancestor.Parent
          }
          $mediaDrive = Get-CimInstance Win32_LogicalDisk -Filter ("DeviceID='" + $item.Directory.Root.Name.TrimEnd('\') + "'") -ErrorAction SilentlyContinue
          $sig = Get-AuthenticodeSignature -LiteralPath $mediaPath
          $hash = (Get-FileHash -LiteralPath $mediaPath -Algorithm SHA256).Hash
          $after = Get-Item -LiteralPath $mediaPath -Force
          $media = [ordered]@{
            Exists = $true
            LocalFixed = [bool]($mediaDrive -and $mediaDrive.DriveType -eq 3)
            ReparseTraversal = [bool]$mediaReparse
            StableIdentity = [bool]($beforeLength -eq [long]$after.Length -and $beforeWrite -eq $after.LastWriteTimeUtc.Ticks)
            Length = $beforeLength
            SignatureStatus = [string]$sig.Status
            SignerName = if ($sig.SignerCertificate) { $sig.SignerCertificate.GetNameInfo('SimpleName',$false) } else { $null }
            FileName = $item.Name
            ProductName = $item.VersionInfo.ProductName
            ProductVersion = $item.VersionInfo.ProductVersion
            Sha256 = $hash
          }
        }

        [ordered]@{
          System = [ordered]@{
            Caption = $os.Caption
            BuildNumber = [int]$os.BuildNumber
            InstallationType = $installType
            ProductType = if ($productType -in 0,1) { 1 } else { 3 }
          }
          PendingRestart = [bool]$pendingRestart
          Hostname = $activeName
          PendingHostnameRename = [bool]$pendingHostnameRename
          DataVolume = if ($disk) { [ordered]@{ DriveType = [int]$disk.DriveType; FileSystem = $disk.FileSystem; FreeBytes = [long]$disk.FreeSpace } } else { $null }
          Repository = [ordered]@{ NonEmpty = [bool]$nonEmpty; ReparseTraversal = [bool]$reparse; ShareOverlapCount = $shareOverlap; MountOverlapCount = $mountOverlap }
          Iis = [ordered]@{ DefaultWebSitePresent = [bool]$defaultSite; ConflictingDefaultSiteCount = $siteConflicts; AmbiguousBindingCount = $ambiguousBindings; PresentPrerequisites = $features }
          Network = [ordered]@{
            ConnectedPrivateProfiles = @($profiles | Where-Object NetworkCategory -eq Private).Count
            ConnectedPublicProfiles = @($profiles | Where-Object NetworkCategory -eq Public).Count
            PublicExposureCount = $publicExposure
            PrivateFirewallEnabled = if ($privateFirewall.Count -eq 1) { [bool]$privateFirewall[0].Enabled } else { $null }
            PublicFirewallEnabled = if ($publicFirewall.Count -eq 1) { [bool]$publicFirewall[0].Enabled } else { $null }
            PrivateDefaultInboundAction = if ($privateFirewall.Count -eq 1) { [string]$privateFirewall[0].DefaultInboundAction } else { $null }
            PublicDefaultInboundAction = if ($publicFirewall.Count -eq 1) { [string]$publicFirewall[0].DefaultInboundAction } else { $null }
          }
          RevitState = [ordered]@{ RoleMarkers = $roleMarkers; ForeignStateCount = $foreign }
          Media = $media
        } | ConvertTo-Json -Depth 6 -Compress
        """;
}

internal sealed record WindowsRevitServerObservation(
    WindowsRevitSystemObservation? System,
    bool? PendingRestart,
    string? Hostname,
    bool? PendingHostnameRename,
    WindowsRevitDataVolumeObservation? DataVolume,
    WindowsRevitRepositoryObservation? Repository,
    WindowsRevitIisObservation? Iis,
    WindowsRevitNetworkObservation? Network,
    WindowsRevitStateObservation? RevitState,
    WindowsRevitMediaObservation? Media);

internal sealed record WindowsRevitSystemObservation(string? Caption, int? BuildNumber, string? InstallationType, int? ProductType);
internal sealed record WindowsRevitDataVolumeObservation(int? DriveType, string? FileSystem, long? FreeBytes);
internal sealed record WindowsRevitRepositoryObservation(bool? NonEmpty, bool? ReparseTraversal, int? ShareOverlapCount, int? MountOverlapCount);
internal sealed record WindowsRevitIisObservation(bool? DefaultWebSitePresent, int? ConflictingDefaultSiteCount, int? AmbiguousBindingCount, IReadOnlyList<string>? PresentPrerequisites);
internal sealed record WindowsRevitNetworkObservation(int? ConnectedPrivateProfiles, int? ConnectedPublicProfiles, int? PublicExposureCount, bool? PrivateFirewallEnabled, bool? PublicFirewallEnabled, string? PrivateDefaultInboundAction, string? PublicDefaultInboundAction);
internal sealed record WindowsRevitStateObservation(IReadOnlyList<string>? RoleMarkers, int? ForeignStateCount);
internal sealed record WindowsRevitMediaObservation(bool? Exists, bool? LocalFixed, bool? ReparseTraversal, bool? StableIdentity, long? Length, string? SignatureStatus, string? SignerName, string? FileName, string? ProductName, string? ProductVersion, string? Sha256);
