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
Assert-ManifestAsset $delivery.archive $tag "^balls-$tagPattern-canary-windows-x64-[0-9a-f]{12}\.zip$"
Assert-ManifestAsset $delivery.checksum $tag "^$([Regex]::Escape([string] $delivery.archive.name))\.sha256$"
Assert-ManifestAsset $delivery.installer $tag '^Install-BallsCanary\.ps1$'

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("balls-alpha-{0}" -f [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $packagePath = Save-VerifiedAsset $delivery.archive $temporaryRoot
    $checksumPath = Save-VerifiedAsset $delivery.checksum $temporaryRoot
    $installerPath = Save-VerifiedAsset $delivery.installer $temporaryRoot

    Write-Output "Verified Balls $tag ($($commit.Substring(0, 12)))."
    Write-Output 'The current Alpha is unsigned. No Windows policy is bypassed.'
    & $installerPath -PackagePath $packagePath -ChecksumPath $checksumPath -InstallRoot $InstallRoot
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
