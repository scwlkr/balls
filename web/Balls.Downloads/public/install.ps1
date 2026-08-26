#Requires -Version 5.1

[CmdletBinding()]
param(
    [uri] $ManifestUri = 'https://balls.wlkrlabs.com/channels/alpha.json',
    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'Balls')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-OfficialManifestUri {
    param([Parameter(Mandatory)] [uri] $Uri)

    if ($Uri.Scheme -cne 'https' -or $Uri.Host -cne 'balls.wlkrlabs.com' -or
        $Uri.Query.Length -ne 0 -or $Uri.Fragment.Length -ne 0 -or
        $Uri.AbsolutePath -notmatch '^/(channels/(alpha|development)|versions/[0-9A-Za-z][0-9A-Za-z._-]{0,127})\.json$') {
        throw 'Balls installs only from an official channel or immutable version manifest.'
    }
}

function Assert-ManifestAsset {
    param(
        [Parameter(Mandatory)] [object] $Asset,
        [Parameter(Mandatory)] [string] $Tag,
        [Parameter(Mandatory)] [string] $NamePattern
    )

    $name = [string] $Asset.name
    if ($name -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]{0,191}$' -or
        $name.Contains('..') -or $name -notmatch $NamePattern -or
        [IO.Path]::GetFileName($name) -cne $name) {
        throw "The Balls manifest contains an invalid asset name: $name"
    }
    if ([string] $Asset.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "The Balls manifest contains an invalid SHA-256 for $name."
    }

    $uri = [uri] $Asset.url
    $expectedPath = "/scwlkr/balls/releases/download/$Tag/$name"
    if ($uri.Scheme -cne 'https' -or $uri.Host -cne 'github.com' -or
        $uri.Query.Length -ne 0 -or $uri.Fragment.Length -ne 0 -or
        $uri.AbsolutePath -cne $expectedPath) {
        throw "The Balls manifest contains an unexpected download URL for $name."
    }
}

function Save-VerifiedAsset {
    param(
        [Parameter(Mandatory)] [object] $Asset,
        [Parameter(Mandatory)] [string] $Directory
    )

    $path = Join-Path $Directory ([string] $Asset.name)
    Invoke-WebRequest -UseBasicParsing -Uri ([uri] $Asset.url) -OutFile $path
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne [string] $Asset.sha256) {
        throw "SHA-256 verification failed for $($Asset.name)."
    }
    return $path
}

function Read-PackageManifest {
    param([Parameter(Mandatory)] [string] $PackagePath)

    $archive = $null
    $entryStream = $null
    $reader = $null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
        $manifestEntries = @($archive.Entries | Where-Object { $_.FullName -ceq 'canary.json' })
        if ($manifestEntries.Count -ne 1 -or $manifestEntries[0].Length -gt 65536) {
            throw 'The Windows package does not contain one bounded package manifest.'
        }

        $entryStream = $manifestEntries[0].Open()
        $reader = New-Object IO.StreamReader($entryStream)
        return ($reader.ReadToEnd() | ConvertFrom-Json)
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        elseif ($null -ne $entryStream) {
            $entryStream.Dispose()
        }
        if ($null -ne $archive) {
            $archive.Dispose()
        }
    }
}

function Assert-PackageIdentity {
    param(
        [Parameter(Mandatory)] [object] $PackageManifest,
        [Parameter(Mandatory)] [object] $Identity
    )

    if ([string] $Identity.product -cne 'Balls' -or
        [string] $Identity.version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$' -or
        [string] $Identity.commit -notmatch '^[0-9a-f]{40}$' -or
        [string] $Identity.platform -cne 'windows' -or
        [string] $Identity.architecture -cne 'x64' -or
        [string] $PackageManifest.product -cne [string] $Identity.product -or
        [string] $PackageManifest.version -cne [string] $Identity.version -or
        [string] $PackageManifest.commit -cne [string] $Identity.commit -or
        [string] $PackageManifest.platform -cne [string] $Identity.platform -or
        [string] $PackageManifest.architecture -cne [string] $Identity.architecture -or
        $PackageManifest.runtimeSupported -ne $true) {
        throw 'The Windows package identity does not match the selected Balls manifest.'
    }
}

function Assert-ChecksumBinding {
    param(
        [Parameter(Mandatory)] [string] $ChecksumPath,
        [Parameter(Mandatory)] [object] $Archive
    )

    $checksumLine = (Get-Content -LiteralPath $ChecksumPath -Raw).Trim()
    if ($checksumLine -notmatch '^([0-9A-Fa-f]{64})  ([0-9A-Za-z][0-9A-Za-z._-]{0,191})$' -or
        $Matches[1].ToLowerInvariant() -cne [string] $Archive.sha256 -or
        $Matches[2] -cne [string] $Archive.name) {
        throw 'The package checksum file does not bind the selected Windows archive.'
    }
}

function Expand-VerifiedPackage {
    param(
        [Parameter(Mandatory)] [string] $PackagePath,
        [Parameter(Mandatory)] [string] $Destination
    )

    $archive = $null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
        if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 10000) {
            throw 'The Windows package has an unsafe number of archive entries.'
        }

        $destinationRoot = [IO.Path]::GetFullPath($Destination) + [IO.Path]::DirectorySeparatorChar
        [int64] $expandedBytes = 0
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.Length -eq 0 -or $entry.FullName.Length -gt 240) {
                throw 'The Windows package contains an invalid archive path.'
            }
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            if (-not $target.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'The Windows package contains a path outside its install root.'
            }
            $expandedBytes += $entry.Length
            if ($expandedBytes -gt 2147483648) {
                throw 'The Windows package expands beyond the supported size.'
            }
        }
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
    }

    Expand-Archive -LiteralPath $PackagePath -DestinationPath $Destination
}

function Assert-InternalChecksums {
    param([Parameter(Mandatory)] [string] $PackageRoot)

    $root = [IO.Path]::GetFullPath($PackageRoot)
    $rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
    $checksumManifest = Join-Path $root 'SHA256SUMS'
    if (-not (Test-Path -LiteralPath $checksumManifest -PathType Leaf)) {
        throw 'The Windows package is missing its internal checksum manifest.'
    }

    $seen = @{}
    foreach ($line in Get-Content -LiteralPath $checksumManifest) {
        if ($line -notmatch '^([0-9A-F]{64})  (.+)$') {
            throw "Invalid internal checksum entry: $line"
        }
        $expectedHash = $Matches[1]
        $relativeName = $Matches[2]
        if ($relativeName.Length -eq 0 -or $relativeName.Length -gt 240 -or
            $seen.ContainsKey($relativeName)) {
            throw "Invalid internal checksum path: $relativeName"
        }
        $relativePath = $relativeName.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $targetPath = [IO.Path]::GetFullPath((Join-Path $root $relativePath))
        if (-not $targetPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            throw "Internal checksum path escapes or is missing from the package: $relativeName"
        }
        $fileHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
        if ($fileHash -cne $expectedHash) {
            throw "Internal checksum mismatch: $relativeName"
        }
        $seen[$relativeName] = $true
    }

    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse) {
        if ($file.FullName -ceq $checksumManifest) {
            continue
        }
        $relativeName = $file.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        if (-not $seen.ContainsKey($relativeName)) {
            throw "The Windows package contains an unhashed file: $relativeName"
        }
    }
}

function Test-X64PortableExecutable {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $stream = $null
    $reader = $null
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        if ($stream.Length -lt 70) {
            return $false
        }
        $reader = New-Object IO.BinaryReader($stream)
        if ($reader.ReadUInt16() -ne 0x5a4d) {
            return $false
        }
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset + 6 -gt $stream.Length) {
            return $false
        }
        $stream.Position = $peOffset
        return $reader.ReadUInt32() -eq 0x00004550 -and $reader.ReadUInt16() -eq 0x8664
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        elseif ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Get-X64DotnetRoot {
    $runtimeRoot = [Environment]::GetEnvironmentVariable('DOTNET_ROOT_X64')
    if (-not [string]::IsNullOrWhiteSpace($runtimeRoot)) {
        return [IO.Path]::GetFullPath($runtimeRoot)
    }

    $runtimeRoot = [Environment]::GetEnvironmentVariable('DOTNET_ROOT')
    if (-not [string]::IsNullOrWhiteSpace($runtimeRoot)) {
        return [IO.Path]::GetFullPath($runtimeRoot)
    }

    $baseKey = $null
    $installKey = $null
    try {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
            [Microsoft.Win32.RegistryHive]::LocalMachine,
            [Microsoft.Win32.RegistryView]::Registry64)
        $installKey = $baseKey.OpenSubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x64')
        if ($null -ne $installKey) {
            $runtimeRoot = [string] $installKey.GetValue('InstallLocation', $null)
            if (-not [string]::IsNullOrWhiteSpace($runtimeRoot) -and
                (Test-Path -LiteralPath (Join-Path $runtimeRoot 'dotnet.exe') -PathType Leaf)) {
                return [IO.Path]::GetFullPath($runtimeRoot)
            }
        }
    }
    finally {
        if ($null -ne $installKey) { $installKey.Dispose() }
        if ($null -ne $baseKey) { $baseKey.Dispose() }
    }

    $programFiles = [Environment]::GetEnvironmentVariable('ProgramW6432')
    if ([string]::IsNullOrWhiteSpace($programFiles)) {
        $programFiles = [Environment]::GetEnvironmentVariable('ProgramFiles')
    }
    if ([string]::IsNullOrWhiteSpace($programFiles)) {
        throw 'The x64 Program Files location is unavailable.'
    }
    return [IO.Path]::GetFullPath((Join-Path $programFiles 'dotnet'))
}

function Test-RuntimeInventory {
    param(
        [Parameter(Mandatory)] [string[]] $InstalledRuntimes,
        [Parameter(Mandatory)] [object[]] $Frameworks
    )

    if ($Frameworks.Count -eq 0) { return $false }
    foreach ($framework in $Frameworks) {
        $name = [string] $framework.name
        $major = [string] $framework.major
        if ($name -notmatch '^[A-Za-z][A-Za-z0-9.]{0,127}$' -or
            $major -notmatch '^[1-9][0-9]{0,2}$') {
            return $false
        }
        $pattern = '^{0}\s+{1}\.' -f [Regex]::Escape($name), [Regex]::Escape($major)
        if (@($InstalledRuntimes | Where-Object { $_ -match $pattern }).Count -eq 0) {
            return $false
        }
    }
    return $true
}

function Get-RuntimeRequirementLabel {
    param([Parameter(Mandatory)] [object[]] $Frameworks)

    if ($Frameworks.Count -eq 0) {
        throw 'The Balls manifest contains an empty Windows runtime contract.'
    }
    $labels = @()
    foreach ($framework in $Frameworks) {
        $name = [string] $framework.name
        $major = [string] $framework.major
        if ($name -notmatch '^[A-Za-z][A-Za-z0-9.]{0,127}$' -or
            $major -notmatch '^[1-9][0-9]{0,2}$') {
            throw 'The Balls manifest contains an invalid Windows runtime framework.'
        }
        $displayName = switch ($name) {
            'Microsoft.NETCore.App' { '.NET' }
            'Microsoft.AspNetCore.App' { 'ASP.NET Core' }
            default { $name }
        }
        $labels += "$displayName $major"
    }
    return ($labels -join ' and ')
}

function Assert-RuntimeRequirements {
    param([Parameter(Mandatory)] [object] $Runtime)

    $kind = [string] $Runtime.kind
    if ([string] $Runtime.architecture -cne 'x64') {
        throw 'The Balls manifest contains an unsupported Windows runtime architecture.'
    }
    if ($kind -ceq 'self-contained') { return }
    if ($kind -cne 'framework-dependent') {
        throw 'The Balls manifest contains an unsupported Windows runtime contract.'
    }

    $frameworks = @($Runtime.frameworks)
    $requirementLabel = Get-RuntimeRequirementLabel $frameworks
    $runtimeError = "This Balls package requires the x64 $requirementLabel runtime"
    if ($frameworks.Count -ne 1) { $runtimeError += 's' }
    $runtimeError += '.'

    $runtimeRoot = Get-X64DotnetRoot
    $dotnetPath = Join-Path $runtimeRoot 'dotnet.exe'
    if (-not (Test-X64PortableExecutable $dotnetPath)) { throw $runtimeError }
    $installedRuntimes = @(& $dotnetPath --list-runtimes 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not (Test-RuntimeInventory $installedRuntimes $frameworks)) {
        throw $runtimeError
    }
}

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory)] [string] $Value)

    if ($Value.Contains('"')) {
        throw 'Balls cannot start from a path or Node name containing a quotation mark.'
    }
    return '"' + $Value + '"'
}

function New-BallsShortcut {
    param(
        [Parameter(Mandatory)] [string] $LauncherPath,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if ([string]::IsNullOrWhiteSpace($programs)) {
        throw 'The current user Start Menu is unavailable.'
    }
    $shortcutPath = Join-Path $programs 'Balls.lnk'
    $temporaryShortcut = Join-Path $programs ("Balls-{0}.lnk" -f [guid]::NewGuid().ToString('N'))
    $shell = $null
    $shortcut = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($temporaryShortcut)
        $shortcut.TargetPath = $LauncherPath
        $shortcut.WorkingDirectory = $WorkingDirectory
        $shortcut.Description = 'Open Balls'
        $shortcut.Save()
        Move-Item -LiteralPath $temporaryShortcut -Destination $shortcutPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryShortcut) {
            Remove-Item -LiteralPath $temporaryShortcut -Force
        }
        if ($null -ne $shortcut) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
        }
        if ($null -ne $shell) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
        }
    }
    return $shortcutPath
}

function Write-BallsLauncher {
    param(
        [Parameter(Mandatory)] [string] $InstallRoot,
        [Parameter(Mandatory)] [string] $VersionId
    )

    if ($VersionId -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]{0,159}$') {
        throw 'The installed package produced an unsafe launcher identity.'
    }
    $launchersRoot = Join-Path $InstallRoot 'launchers'
    New-Item -ItemType Directory -Force -Path $launchersRoot | Out-Null
    $launcherPath = Join-Path $launchersRoot "$VersionId.cmd"
    $temporaryLauncher = "$launcherPath.new"
    $content = @'
@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "BALLS_PACKAGE=%~dp0..\versions\{VERSION_ID}"
set "BALLS_HOME=%~dp0.."
set "BALLS_PIPE=balls"
set "BALLS_CLI=%BALLS_PACKAGE%\balls\balls.exe"
set "BALLS_DAEMON=%BALLS_PACKAGE%\ballsd\ballsd.exe"
set "BALLS_DAEMON_DIRECTORY=%BALLS_PACKAGE%\ballsd"
set "BALLS_STATE=%BALLS_HOME%\state"
set "BALLS_LOGS=%BALLS_HOME%\logs"
set "BALLS_STDOUT=%BALLS_LOGS%\ballsd.stdout.log"
set "BALLS_STDERR=%BALLS_LOGS%\ballsd.stderr.log"
set "BALLS_DAEMON_ARGUMENTS=--data-directory "%BALLS_STATE%" --pipe-name "%BALLS_PIPE%" --node-name "%COMPUTERNAME%""

if not exist "%BALLS_CLI%" goto missing_files
if not exist "%BALLS_DAEMON%" goto missing_files
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" status >nul 2>&1
if not errorlevel 1 goto open_workspace
if not exist "%BALLS_STATE%" mkdir "%BALLS_STATE%"
if not exist "%BALLS_LOGS%" mkdir "%BALLS_LOGS%"
powershell.exe -NoLogo -NoProfile -NonInteractive -Command ^
  "try { Start-Process -FilePath $env:BALLS_DAEMON -ArgumentList $env:BALLS_DAEMON_ARGUMENTS -WorkingDirectory $env:BALLS_DAEMON_DIRECTORY -WindowStyle Hidden -RedirectStandardOutput $env:BALLS_STDOUT -RedirectStandardError $env:BALLS_STDERR -ErrorAction Stop; exit 0 } catch { $_ | Out-String | Set-Content -LiteralPath $env:BALLS_STDERR; exit 1 }"
if errorlevel 1 goto startup_failed
set /a BALLS_ATTEMPTS=30
:wait_for_node
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" status >nul 2>&1
if not errorlevel 1 goto open_workspace
set /a BALLS_ATTEMPTS-=1
if %BALLS_ATTEMPTS% leq 0 goto startup_failed
ping -n 2 127.0.0.1 >nul
goto wait_for_node
:open_workspace
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" ui
if errorlevel 1 goto workspace_failed
exit /b 0
:missing_files
echo Balls is incomplete. Run the install command again.
pause
exit /b 1
:startup_failed
echo Balls could not start. Windows application policy was not changed.
echo Startup log: %BALLS_STDERR%
if exist "%BALLS_STDERR%" type "%BALLS_STDERR%"
pause
exit /b 1
:workspace_failed
echo Balls is running, but its workspace could not open. Try again.
pause
exit /b 1
'@.Replace('{VERSION_ID}', $VersionId)
    [IO.File]::WriteAllText(
        $temporaryLauncher,
        $content,
        (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporaryLauncher -Destination $launcherPath -Force
    return $launcherPath
}

if ($env:OS -cne 'Windows_NT') {
    throw 'This Balls bootstrap is Windows-only.'
}
if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'The published Balls Windows package requires x64 Windows.'
}

Assert-OfficialManifestUri $ManifestUri
$manifest = Invoke-RestMethod -UseBasicParsing -Uri $ManifestUri
$channel = [string] $manifest.channel
if ($manifest.schemaVersion -ne 1 -or $channel -notin @('alpha', 'development')) {
    throw 'The Balls manifest has an unsupported schema or channel.'
}
$tag = [string] $manifest.release.tag
$commit = [string] $manifest.release.commit
if ($tag -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$' -or $tag.Contains('..') -or
    $commit -notmatch '^[0-9a-f]{40}$') {
    throw 'The Balls manifest has an invalid release identity.'
}
$releaseUri = [uri] $manifest.release.url
if ($releaseUri.AbsoluteUri -cne "https://github.com/scwlkr/balls/releases/tag/$tag") {
    throw 'The Balls manifest has an unexpected release URL.'
}

$delivery = $manifest.platforms.'windows-x64'
if ($delivery.delivery -cne 'package') {
    throw 'The Balls manifest does not contain a Windows package.'
}
$identity = $delivery.identity
if ([string] $identity.commit -cne $commit) {
    throw 'The Windows package identity does not match the release commit.'
}
$versionPattern = [Regex]::Escape([string] $identity.version)
$commitPrefix = [Regex]::Escape($commit.Substring(0, 12))
Assert-ManifestAsset $delivery.archive $tag "^balls-$versionPattern-canary-windows-x64-$commitPrefix\.zip$"
Assert-ManifestAsset $delivery.checksum $tag "^$([Regex]::Escape([string] $delivery.archive.name))\.sha256$"
Assert-ManifestAsset $delivery.installer $tag '^Install-BallsCanary\.ps1$'
if ($null -eq $delivery.PSObject.Properties['runtime']) {
    throw 'The Balls manifest does not declare the Windows runtime contract.'
}
Assert-RuntimeRequirements $delivery.runtime

$resolvedInstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$localAppData = [IO.Path]::GetFullPath([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData))
if (-not $resolvedInstallRoot.StartsWith(
    ($localAppData + [IO.Path]::DirectorySeparatorChar),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Balls installs only inside the current user profile.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("balls-install-{0}" -f [guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $temporaryRoot 'package'
$daemon = $null
$startedDaemon = $false
$pidPath = $null
$recordPath = Join-Path $resolvedInstallRoot 'installation.json'
$recordTemporary = "$recordPath.new"
$programsPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$expectedShortcutPath = Join-Path $programsPath 'Balls.lnk'
$previousRecordBytes = if (Test-Path -LiteralPath $recordPath -PathType Leaf) {
    [IO.File]::ReadAllBytes($recordPath)
}
else { $null }
$previousShortcutBytes = if (Test-Path -LiteralPath $expectedShortcutPath -PathType Leaf) {
    [IO.File]::ReadAllBytes($expectedShortcutPath)
}
else { $null }
$recordChanged = $false
$shortcutChanged = $false
$installationCommitted = $false
try {
    New-Item -ItemType Directory -Path $temporaryRoot, $extractRoot | Out-Null
    $packagePath = Save-VerifiedAsset $delivery.archive $temporaryRoot
    $checksumPath = Save-VerifiedAsset $delivery.checksum $temporaryRoot
    $null = Save-VerifiedAsset $delivery.installer $temporaryRoot
    Assert-ChecksumBinding $checksumPath $delivery.archive

    $packageManifest = Read-PackageManifest $packagePath
    Assert-PackageIdentity $packageManifest $identity
    Expand-VerifiedPackage $packagePath $extractRoot
    Assert-InternalChecksums $extractRoot

    foreach ($requiredPath in @('balls\balls.exe', 'ballsd\ballsd.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extractRoot $requiredPath) -PathType Leaf)) {
            throw "The Windows package is missing $requiredPath."
        }
    }

    $versionId = "{0}-{1}" -f $identity.version, $commit.Substring(0, 12)
    $versionsRoot = Join-Path $resolvedInstallRoot 'versions'
    $versionRoot = Join-Path $versionsRoot $versionId
    $stateRoot = Join-Path $resolvedInstallRoot 'state'
    New-Item -ItemType Directory -Force -Path $versionsRoot, $stateRoot | Out-Null

    if (Test-Path -LiteralPath $versionRoot) {
        $installedManifest = Get-Content -LiteralPath (Join-Path $versionRoot 'canary.json') -Raw | ConvertFrom-Json
        Assert-PackageIdentity $installedManifest $identity
        Assert-InternalChecksums $versionRoot
    }
    else {
        Move-Item -LiteralPath $extractRoot -Destination $versionRoot
    }

    $pidPath = Join-Path $resolvedInstallRoot 'ballsd.pid'
    if (Test-Path -LiteralPath $pidPath) {
        $existingPid = [int](Get-Content -LiteralPath $pidPath -Raw)
        if (Get-Process -Id $existingPid -ErrorAction SilentlyContinue) {
            throw "Balls is already running as PID $existingPid. Close Balls before updating."
        }
        Remove-Item -LiteralPath $pidPath
    }

    $daemonPath = Join-Path $versionRoot 'ballsd\ballsd.exe'
    $cliPath = Join-Path $versionRoot 'balls\balls.exe'
    $launcherPath = Write-BallsLauncher $resolvedInstallRoot $versionId
    $pipeName = 'balls'
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $daemonPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = @(
        '--data-directory', (ConvertTo-ProcessArgument $stateRoot),
        '--pipe-name', (ConvertTo-ProcessArgument $pipeName),
        '--node-name', (ConvertTo-ProcessArgument ([Environment]::MachineName))
    ) -join ' '
    try {
        $daemon = [Diagnostics.Process]::Start($startInfo)
    }
    catch {
        throw "BLOCKED: Windows did not allow Balls to start. No application policy was changed. $($_.Exception.Message)"
    }
    $startedDaemon = $true
    Set-Content -LiteralPath $pidPath -Value $daemon.Id -NoNewline

    $ready = $false
    $lastCliError = ''
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($daemon.HasExited) {
            throw "Balls exited during startup with code $($daemon.ExitCode)."
        }
        $cliInfo = New-Object Diagnostics.ProcessStartInfo
        $cliInfo.FileName = $cliPath
        $cliInfo.UseShellExecute = $false
        $cliInfo.RedirectStandardOutput = $true
        $cliInfo.RedirectStandardError = $true
        $cliInfo.Arguments = '--pipe-name "balls" status'
        $cli = [Diagnostics.Process]::Start($cliInfo)
        $standardOutput = $cli.StandardOutput.ReadToEnd()
        $lastCliError = $cli.StandardError.ReadToEnd()
        $cli.WaitForExit()
        if ($cli.ExitCode -eq 0) {
            $ready = $true
            Write-Output $standardOutput.TrimEnd()
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        throw "Balls did not become ready. Last startup check: $lastCliError"
    }

    $uiOutput = (& $cliPath --pipe-name $pipeName ui | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $uiOutput -cne 'Opened the local Balls workspace.') {
        throw 'Balls started, but its workspace could not open.'
    }

    $shortcutChanged = $true
    $shortcutPath = New-BallsShortcut $launcherPath $versionRoot
    $record = [ordered]@{
        schemaVersion = 1
        product = 'Balls'
        channel = $channel
        manifestUri = $ManifestUri.AbsoluteUri
        installedAt = [DateTimeOffset]::UtcNow.ToString('O')
        release = [ordered]@{ tag = $tag; commit = $commit }
        package = [ordered]@{
            name = [string] $delivery.archive.name
            sha256 = [string] $delivery.archive.sha256
            version = [string] $identity.version
            platform = 'windows'
            architecture = 'x64'
        }
    }
    [IO.File]::WriteAllText(
        $recordTemporary,
        (($record | ConvertTo-Json -Depth 5) + [Environment]::NewLine),
        (New-Object Text.UTF8Encoding($false)))
    $recordChanged = $true
    Move-Item -LiteralPath $recordTemporary -Destination $recordPath -Force
    $installationCommitted = $true

    Write-Output "Installed Balls $($identity.version) from $channel release $tag ($($commit.Substring(0, 12)))."
    Write-Output "Shortcut: $shortcutPath"
    Write-Output 'This prerelease is unsigned. No Windows policy was bypassed.'
}
catch {
    if ($startedDaemon -and $null -ne $daemon -and -not $daemon.HasExited) {
        $daemon.Kill()
        $daemon.WaitForExit()
    }
    if ($startedDaemon -and $null -ne $pidPath -and (Test-Path -LiteralPath $pidPath)) {
        Remove-Item -LiteralPath $pidPath -Force
    }
    if (-not $installationCommitted -and $recordChanged) {
        Remove-Item -LiteralPath $recordTemporary -Force -ErrorAction SilentlyContinue
        if ($null -eq $previousRecordBytes) {
            Remove-Item -LiteralPath $recordPath -Force -ErrorAction SilentlyContinue
        }
        else {
            [IO.File]::WriteAllBytes($recordPath, $previousRecordBytes)
        }
    }
    if (-not $installationCommitted -and $shortcutChanged) {
        if ($null -eq $previousShortcutBytes) {
            Remove-Item -LiteralPath $expectedShortcutPath -Force -ErrorAction SilentlyContinue
        }
        else {
            [IO.File]::WriteAllBytes($expectedShortcutPath, $previousShortcutBytes)
        }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        if (-not $resolvedTemporary.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected temporary path: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
