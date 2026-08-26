[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [uri] $ManifestUri,

    [Parameter(Mandatory)]
    [string] $ExpectedTag,

    [Parameter(Mandatory)]
    [string] $ExpectedCommit,

    [uri] $BootstrapManifestUri = 'https://balls.wlkrlabs.com/bootstrap/windows-x64.json',

    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'Balls-Download-Smoke')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$bootstrapPath = Join-Path $env:TEMP ("balls-bootstrap-smoke-{0}.exe" -f [guid]::NewGuid().ToString('N'))
$resolvedInstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$localAppData = [IO.Path]::GetFullPath(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData))
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programs 'Balls.lnk'
$executionPolicyBefore = Get-ExecutionPolicy -List | Out-String
$daemon = $null
$shortcutLaunch = $null

function Test-PrivateIPv4Address {
    param([Parameter(Mandatory)] [string] $Address)

    $parsed = [Net.IPAddress]::None
    if (-not [Net.IPAddress]::TryParse($Address, [ref] $parsed) -or
        $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
        [Net.IPAddress]::IsLoopback($parsed)) {
        return $false
    }
    $bytes = $parsed.GetAddressBytes()
    return $bytes[0] -eq 10 -or
        ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
        ($bytes[0] -eq 192 -and $bytes[1] -eq 168) -or
        ($bytes[0] -eq 169 -and $bytes[1] -eq 254)
}

function Test-LoopbackAddress {
    param([Parameter(Mandatory)] [string] $Address)

    $parsed = [Net.IPAddress]::None
    return [Net.IPAddress]::TryParse($Address, [ref] $parsed) -and
        [Net.IPAddress]::IsLoopback($parsed)
}

function Get-VerifiedPrivateListenerCount {
    param([Parameter(Mandatory)] [int] $OwningProcessId)

    $tcpListeners = @(Get-NetTCPConnection `
        -OwningProcess $OwningProcessId `
        -State Listen `
        -ErrorAction Stop)
    $privateListeners = @($tcpListeners | Where-Object {
        Test-PrivateIPv4Address ([string] $_.LocalAddress)
    })
    $unsafeListeners = @($tcpListeners | Where-Object {
        -not (Test-PrivateIPv4Address ([string] $_.LocalAddress)) -and
        -not (Test-LoopbackAddress ([string] $_.LocalAddress))
    })
    if ($privateListeners.Count -ne 2 -or $unsafeListeners.Count -ne 0) {
        throw 'Balls did not expose exactly two safe private listeners plus loopback-only browser access.'
    }
    return $privateListeners.Count
}

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
    $bootstrap = Invoke-RestMethod -UseBasicParsing -Uri $BootstrapManifestUri
    $bootstrapName = [string] $bootstrap.asset.name
    $bootstrapUri = [uri] $bootstrap.asset.url
    if ($bootstrap.schemaVersion -ne 1 -or
        [string] $bootstrap.product -cne 'Balls' -or
        [string] $bootstrap.platform -cne 'windows' -or
        [string] $bootstrap.architecture -cne 'x64' -or
        [string] $bootstrap.release.commit -cne $ExpectedCommit -or
        $bootstrapName -notmatch '^balls-bootstrap-windows-x64-[0-9a-f]{12}\.exe$' -or
        [string] $bootstrap.asset.sha256 -notmatch '^[0-9a-f]{64}$' -or
        $bootstrapUri.Scheme -cne 'https' -or
        $bootstrapUri.Host -cne 'github.com' -or
        $bootstrapUri.AbsolutePath -cne "/scwlkr/balls/releases/download/$($bootstrap.release.tag)/$bootstrapName") {
        throw 'The official native Windows bootstrap manifest is invalid or does not match the selected release.'
    }
    Invoke-WebRequest -UseBasicParsing -Uri $bootstrapUri -OutFile $bootstrapPath
    if ((Get-FileHash -LiteralPath $bootstrapPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
        [string] $bootstrap.asset.sha256) {
        throw 'The native Windows bootstrap SHA-256 verification failed.'
    }

    $processPolicyBefore = [Environment]::GetEnvironmentVariable(
        'PSExecutionPolicyPreference',
        [EnvironmentVariableTarget]::Process)
    try {
        [Environment]::SetEnvironmentVariable(
            'PSExecutionPolicyPreference',
            'Restricted',
            [EnvironmentVariableTarget]::Process)
        & $bootstrapPath `
            --manifest-uri $ManifestUri.AbsoluteUri `
            --install-root $resolvedInstallRoot
        if ($LASTEXITCODE -ne 0) {
            throw "The official native Windows bootstrap failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            'PSExecutionPolicyPreference',
            $processPolicyBefore,
            [EnvironmentVariableTarget]::Process)
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

    $initialPrivateListenerCount = Get-VerifiedPrivateListenerCount $daemon.Id

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

    $daemon.Kill()
    $daemon.WaitForExit()
    $daemon = $null
    $shortcutLaunch = Start-Process -FilePath $shortcutPath -PassThru
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        $daemon = @(Get-Process -Name 'ballsd' -ErrorAction SilentlyContinue | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.Path) -and
            [IO.Path]::GetFullPath($_.Path) -ceq [IO.Path]::GetFullPath($expectedDaemon)
        } | Select-Object -First 1)[0]
        if ($null -eq $daemon) {
            Start-Sleep -Milliseconds 250
        }
    } while ($null -eq $daemon -and [DateTimeOffset]::UtcNow -lt $deadline)
    if ($null -eq $daemon) {
        throw 'The normal Balls shortcut did not relaunch the installed daemon.'
    }
    $shortcutPrivateListenerCount = Get-VerifiedPrivateListenerCount $daemon.Id
    if (-not $shortcutLaunch.WaitForExit(20000) -or $shortcutLaunch.ExitCode -ne 0) {
        throw 'The normal Balls shortcut did not finish opening the loopback workspace.'
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
        privateListenerCount = $shortcutPrivateListenerCount
        privateListenersVerified = $true
        automaticFirstLaunchListenersVerified = $initialPrivateListenerCount -eq 2
        shortcutListenersVerified = $shortcutPrivateListenerCount -eq 2
        shortcutVerified = $true
        executionPolicyUnchanged = $true
    } | ConvertTo-Json -Compress
}
finally {
    if ($null -ne $shortcutLaunch -and -not $shortcutLaunch.HasExited) {
        $shortcutLaunch.Kill()
        $shortcutLaunch.WaitForExit()
    }
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
    if (Test-Path -LiteralPath $bootstrapPath) {
        Remove-Item -LiteralPath $bootstrapPath -Force
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
