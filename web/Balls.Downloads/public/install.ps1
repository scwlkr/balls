#Requires -Version 7.0

[CmdletBinding()]
param(
    [uri] $ManifestUri = 'https://balls.wlkrlabs.com/channels/alpha.json',
    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'Balls-Canary')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-ManifestAsset {
    param(
        [Parameter(Mandatory)] [object] $Asset,
        [Parameter(Mandatory)] [string] $Tag,
        [Parameter(Mandatory)] [string] $NamePattern
    )

    $name = [string] $Asset.name
    if ($name -notmatch $NamePattern -or [IO.Path]::GetFileName($name) -ne $name) {
        throw "The Alpha manifest contains an invalid asset name: $name"
    }
    if ([string] $Asset.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "The Alpha manifest contains an invalid SHA-256 for $name."
    }

    $uri = [uri] $Asset.url
    $expectedPrefix = "/scwlkr/balls/releases/download/$Tag/"
    if ($uri.Scheme -ne 'https' -or
        $uri.Host -ne 'github.com' -or
        -not $uri.AbsolutePath.StartsWith($expectedPrefix, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($uri.AbsolutePath) -ne $name) {
        throw "The Alpha manifest contains an unexpected download URL for $name."
    }
}

function Save-VerifiedAsset {
    param(
        [Parameter(Mandatory)] [object] $Asset,
        [Parameter(Mandatory)] [string] $Directory
    )

    $path = Join-Path $Directory ([string] $Asset.name)
    Invoke-WebRequest -Uri ([uri] $Asset.url) -OutFile $path
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne [string] $Asset.sha256) {
        throw "SHA-256 verification failed for $($Asset.name)."
    }
    return $path
}

function Assert-PackageIdentity {
    param(
        [Parameter(Mandatory)] [string] $PackagePath,
        [Parameter(Mandatory)] [string] $Tag,
        [Parameter(Mandatory)] [string] $Commit
    )

    $archive = $null
    $entryStream = $null
    $reader = $null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
        $manifestEntries = @($archive.Entries | Where-Object { $_.FullName -ceq 'canary.json' })
        if ($manifestEntries.Count -ne 1 -or $manifestEntries[0].Length -gt 65536) {
            throw 'The Windows package does not contain one bounded Canary manifest.'
        }

        $entryStream = $manifestEntries[0].Open()
        $reader = [IO.StreamReader]::new($entryStream)
        $packageManifest = $reader.ReadToEnd() | ConvertFrom-Json
        if ([string] $packageManifest.platform -cne 'windows' -or
            [string] $packageManifest.version -cne $Tag -or
            [string] $packageManifest.commit -cne $Commit) {
            throw 'The Windows package identity does not match the accepted Alpha manifest.'
        }
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

function Test-X64PortableExecutable {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $stream = $null
    $reader = $null
    try {
        $stream = [IO.File]::Open(
            $Path,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        if ($stream.Length -lt 70) {
            return $false
        }

        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5a4d) {
            return $false
        }

        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset + 6 -gt $stream.Length) {
            return $false
        }

        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            return $false
        }
        return $reader.ReadUInt16() -eq 0x8664
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
        $installKey = $baseKey.OpenSubKey(
            'SOFTWARE\dotnet\Setup\InstalledVersions\x64')
        if ($null -ne $installKey) {
            $runtimeRoot = [string] $installKey.GetValue('InstallLocation', $null)
            if (-not [string]::IsNullOrWhiteSpace($runtimeRoot) -and
                (Test-Path -LiteralPath (Join-Path $runtimeRoot 'dotnet.exe') -PathType Leaf)) {
                return [IO.Path]::GetFullPath($runtimeRoot)
            }
        }
    }
    finally {
        if ($null -ne $installKey) {
            $installKey.Dispose()
        }
        if ($null -ne $baseKey) {
            $baseKey.Dispose()
        }
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

    if ($Frameworks.Count -eq 0) {
        return $false
    }

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

function Assert-RuntimeRequirements {
    param([Parameter(Mandatory)] [object] $Runtime)

    $kind = [string] $Runtime.kind
    if ([string] $Runtime.architecture -cne 'x64') {
        throw 'The Alpha manifest contains an unsupported Windows runtime architecture.'
    }
    if ($kind -ceq 'self-contained') {
        return
    }
    if ($kind -cne 'framework-dependent') {
        throw 'The Alpha manifest contains an unsupported Windows runtime contract.'
    }

    $frameworks = @($Runtime.frameworks)
    $runtimeRoot = Get-X64DotnetRoot
    $dotnetPath = Join-Path $runtimeRoot 'dotnet.exe'
    if (-not (Test-X64PortableExecutable $dotnetPath)) {
        throw 'The published Balls Windows Alpha requires the x64 .NET 10 and ASP.NET Core 10 runtimes.'
    }

    $installedRuntimes = @(& $dotnetPath --list-runtimes 2>$null)
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-RuntimeInventory $installedRuntimes $frameworks)) {
        throw 'The published Balls Windows Alpha requires the x64 .NET 10 and ASP.NET Core 10 runtimes.'
    }
}

if (-not $IsWindows) {
    throw 'This Balls Alpha bootstrap is Windows-only.'
}
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne
    [Runtime.InteropServices.Architecture]::X64) {
    throw 'The published Balls Windows Alpha requires x64 Windows.'
}

$manifest = Invoke-RestMethod -Uri $ManifestUri
if ($manifest.schemaVersion -ne 1 -or $manifest.channel -ne 'alpha') {
    throw 'The Balls Alpha manifest has an unsupported schema or channel.'
}
$tag = [string] $manifest.release.tag
$commit = [string] $manifest.release.commit
if ($tag -notmatch '^\d+\.\d+\.\d+-alpha\.\d+$' -or $commit -notmatch '^[0-9a-f]{40}$') {
    throw 'The Balls Alpha manifest has an invalid release identity.'
}

$delivery = $manifest.platforms.'windows-x64'
if ($delivery.delivery -ne 'package') {
    throw 'The Alpha manifest does not contain a Windows package.'
}
$tagPattern = [Regex]::Escape($tag)
$commitPrefix = [Regex]::Escape($commit.Substring(0, 12))
Assert-ManifestAsset $delivery.archive $tag "^balls-$tagPattern-canary-windows-x64-$commitPrefix\.zip$"
Assert-ManifestAsset $delivery.checksum $tag "^$([Regex]::Escape([string] $delivery.archive.name))\.sha256$"
Assert-ManifestAsset $delivery.installer $tag '^Install-BallsCanary\.ps1$'
if ($null -eq $delivery.PSObject.Properties['runtime']) {
    throw 'The Alpha manifest does not declare the Windows runtime contract.'
}
Assert-RuntimeRequirements $delivery.runtime

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("balls-alpha-{0}" -f [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $packagePath = Save-VerifiedAsset $delivery.archive $temporaryRoot
    $checksumPath = Save-VerifiedAsset $delivery.checksum $temporaryRoot
    $installerPath = Save-VerifiedAsset $delivery.installer $temporaryRoot
    Assert-PackageIdentity $packagePath $tag $commit

    Write-Output "Verified Balls $tag ($($commit.Substring(0, 12)))."
    Write-Output 'The current Alpha is unsigned. No Windows policy is bypassed.'
    & $installerPath -PackagePath $packagePath -ChecksumPath $checksumPath -InstallRoot $InstallRoot
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
