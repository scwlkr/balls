[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [uri] $ManifestUri,

    [Parameter(Mandatory)]
    [string] $ExpectedTag,

    [Parameter(Mandatory)]
    [string] $ExpectedCommit,

    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'Balls-Download-Smoke')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$bootstrapPath = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..\web\Balls.Downloads\public\install.ps1'))
$resolvedInstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$localAppData = [IO.Path]::GetFullPath(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData))
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programs 'Balls.lnk'
$executionPolicyBefore = Get-ExecutionPolicy -List | Out-String
$daemon = $null

if ($env:OS -cne 'Windows_NT') {
    throw 'The Balls download smoke is Windows-only.'
}
if ($ExpectedTag -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$' -or
    $ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Expected release identity is invalid.'
}
if (-not $resolvedInstallRoot.StartsWith(
    ($localAppData + [IO.Path]::DirectorySeparatorChar),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Download smoke install root must remain in Local AppData.'
}
if (Test-Path -LiteralPath $resolvedInstallRoot) {
    throw "Download smoke install root already exists: $resolvedInstallRoot"
}
if (Test-Path -LiteralPath $shortcutPath) {
    throw "Download smoke requires a clean profile without an existing Balls shortcut: $shortcutPath"
}

try {
    & powershell.exe -NoLogo -NoProfile -File $bootstrapPath `
        -ManifestUri $ManifestUri.AbsoluteUri `
        -InstallRoot $resolvedInstallRoot
    if ($LASTEXITCODE -ne 0) {
        throw "The official Windows bootstrap failed with exit code $LASTEXITCODE."
    }

    $recordPath = Join-Path $resolvedInstallRoot 'installation.json'
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ([string] $record.release.tag -cne $ExpectedTag -or
        [string] $record.release.commit -cne $ExpectedCommit -or
        [string] $record.manifestUri -cne $ManifestUri.AbsoluteUri -or
        [string] $record.package.platform -cne 'windows' -or
        [string] $record.package.architecture -cne 'x64' -or
        [string] $record.package.name -notmatch [Regex]::Escape($ExpectedCommit.Substring(0, 12)) -or
        [string] $record.package.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'Installed identity does not match the exact selected release manifest.'
    }

    $pidPath = Join-Path $resolvedInstallRoot 'ballsd.pid'
    $daemonPid = [int](Get-Content -LiteralPath $pidPath -Raw)
    $daemon = Get-Process -Id $daemonPid
    $expectedDaemon = Get-ChildItem -LiteralPath (Join-Path $resolvedInstallRoot 'versions') `
        -Filter ballsd.exe -File -Recurse |
        Where-Object { $_.FullName -match '\\ballsd\\ballsd\.exe$' } |
        Select-Object -First 1 -ExpandProperty FullName
    if ($daemon.HasExited -or
        [string]::IsNullOrWhiteSpace($expectedDaemon) -or
        [IO.Path]::GetFullPath($daemon.Path) -cne [IO.Path]::GetFullPath($expectedDaemon)) {
        throw 'The running daemon is not the exact installed release binary.'
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcutTarget = [IO.Path]::GetFullPath([string] $shortcut.TargetPath)
        if (-not $shortcutTarget.StartsWith(
            ($resolvedInstallRoot + [IO.Path]::DirectorySeparatorChar),
            [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $shortcutTarget -PathType Leaf)) {
            throw 'The normal Balls shortcut does not target this verified installation.'
        }
    }
    finally {
        if ($null -ne $shortcut) {
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
        }
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }

    $executionPolicyAfter = Get-ExecutionPolicy -List | Out-String
    if ($executionPolicyAfter -cne $executionPolicyBefore) {
        throw 'The Windows bootstrap changed execution policy.'
    }

    [pscustomobject]@{
        success = $true
        tag = $record.release.tag
        commit = $record.release.commit
        channel = $record.channel
        archive = $record.package.name
        archiveSha256 = $record.package.sha256
        daemonPathVerified = $true
        shortcutVerified = $true
        executionPolicyUnchanged = $true
    } | ConvertTo-Json -Compress
}
finally {
    if ($null -ne $daemon -and -not $daemon.HasExited) {
        $daemon.Kill()
        $daemon.WaitForExit()
    }
    if (Test-Path -LiteralPath $shortcutPath) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $null
        try {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $target = [IO.Path]::GetFullPath([string] $shortcut.TargetPath)
            if (-not $target.StartsWith(
                ($resolvedInstallRoot + [IO.Path]::DirectorySeparatorChar),
                [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Refusing to remove a Balls shortcut not owned by this smoke run.'
            }
        }
        finally {
            if ($null -ne $shortcut) {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
            }
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
        }
        Remove-Item -LiteralPath $shortcutPath -Force
    }
    if (Test-Path -LiteralPath $resolvedInstallRoot) {
        if (-not $resolvedInstallRoot.StartsWith(
            ($localAppData + [IO.Path]::DirectorySeparatorChar),
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove an unexpected smoke path: $resolvedInstallRoot"
        }
        Remove-Item -LiteralPath $resolvedInstallRoot -Recurse -Force
    }
}
