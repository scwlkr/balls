[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [string] $ChecksumPath,

    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'Balls-Canary'),

    [string] $PipeName = 'balls-canary',

    [string] $NodeName = [Environment]::MachineName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$daemon = $null
$startedDaemon = $false
$pidPath = $null
$standardError = ''

if (-not $IsWindows) {
    throw 'The Balls Canary installer is Windows-only.'
}

$packageMatches = @(Resolve-Path -Path $PackagePath)
if ($packageMatches.Count -ne 1) {
    throw "PackagePath must resolve to exactly one Canary archive; found $($packageMatches.Count)."
}
$resolvedPackage = $packageMatches[0].Path
if ([string]::IsNullOrWhiteSpace($ChecksumPath)) {
    $ChecksumPath = "$resolvedPackage.sha256"
}
$resolvedChecksum = (Resolve-Path -LiteralPath $ChecksumPath).Path
$resolvedInstallRoot = [IO.Path]::GetFullPath($InstallRoot)

$checksumLine = (Get-Content -LiteralPath $resolvedChecksum -Raw).Trim()
if ($checksumLine -notmatch '^([0-9A-Fa-f]{64})  (.+)$') {
    throw "Invalid archive checksum file: $resolvedChecksum"
}
$expectedHash = $Matches[1]
$expectedName = $Matches[2]
if ($expectedName -ne [IO.Path]::GetFileName($resolvedPackage)) {
    throw 'The checksum file names a different archive.'
}
$actualHash = (Get-FileHash -LiteralPath $resolvedPackage -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) {
    throw 'The Canary archive SHA-256 checksum does not match.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("balls-canary-install-{0}" -f [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    Expand-Archive -LiteralPath $resolvedPackage -DestinationPath $temporaryRoot

    $manifestPath = Join-Path $temporaryRoot 'canary.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.platform -ne 'windows' -or $manifest.runtimeSupported -ne $true) {
        throw 'The selected archive is not a runnable Windows Canary.'
    }
    if ($manifest.version -notmatch '^[0-9A-Za-z.-]+$' -or $manifest.commit -notmatch '^[0-9a-f]{40}$') {
        throw 'The Canary manifest contains an invalid version or commit identity.'
    }

    $checksumManifest = Join-Path $temporaryRoot 'SHA256SUMS'
    foreach ($line in Get-Content -LiteralPath $checksumManifest) {
        if ($line -notmatch '^([0-9A-F]{64})  (.+)$') {
            throw "Invalid internal checksum entry: $line"
        }
        $relativePath = $Matches[2].Replace('/', [IO.Path]::DirectorySeparatorChar)
        $targetPath = [IO.Path]::GetFullPath((Join-Path $temporaryRoot $relativePath))
        if (-not $targetPath.StartsWith(
            ([IO.Path]::GetFullPath($temporaryRoot) + [IO.Path]::DirectorySeparatorChar),
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Internal checksum path escapes the package: $relativePath"
        }
        $fileHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
        if ($fileHash -ne $Matches[1]) {
            throw "Internal checksum mismatch: $relativePath"
        }
    }

    $versionId = "{0}-{1}" -f $manifest.version, $manifest.commit.Substring(0, 12)
    $versionsRoot = Join-Path $resolvedInstallRoot 'versions'
    $versionRoot = Join-Path $versionsRoot $versionId
    $stateRoot = Join-Path $resolvedInstallRoot 'state'
    New-Item -ItemType Directory -Force -Path $versionsRoot, $stateRoot | Out-Null

    if (Test-Path -LiteralPath $versionRoot) {
        $installedManifest = Get-Content -LiteralPath (Join-Path $versionRoot 'canary.json') -Raw | ConvertFrom-Json
        if ($installedManifest.commit -ne $manifest.commit) {
            throw "Install target already contains a different Canary: $versionRoot"
        }
    }
    else {
        Move-Item -LiteralPath $temporaryRoot -Destination $versionRoot
    }

    $pidPath = Join-Path $resolvedInstallRoot 'ballsd.pid'
    if (Test-Path -LiteralPath $pidPath) {
        $existingPid = [int](Get-Content -LiteralPath $pidPath -Raw)
        if (Get-Process -Id $existingPid -ErrorAction SilentlyContinue) {
            throw "A Balls Canary process is already recorded as PID $existingPid."
        }
        Remove-Item -LiteralPath $pidPath
    }

    $daemonPath = Join-Path $versionRoot 'ballsd\ballsd.exe'
    $cliPath = Join-Path $versionRoot 'balls\balls.exe'
    $startInfo = [Diagnostics.ProcessStartInfo]::new($daemonPath)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('--data-directory', $stateRoot, '--pipe-name', $PipeName, '--node-name', $NodeName)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $daemon = [Diagnostics.Process]::Start($startInfo)
    $startedDaemon = $true
    Set-Content -LiteralPath $pidPath -Value $daemon.Id -NoNewline

    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($daemon.HasExited) {
            throw "ballsd exited during Canary startup with code $($daemon.ExitCode)."
        }

        $cliInfo = [Diagnostics.ProcessStartInfo]::new($cliPath)
        $cliInfo.UseShellExecute = $false
        $cliInfo.RedirectStandardOutput = $true
        $cliInfo.RedirectStandardError = $true
        foreach ($argument in @('--pipe-name', $PipeName, 'status')) {
            $cliInfo.ArgumentList.Add($argument)
        }
        $cli = [Diagnostics.Process]::Start($cliInfo)
        $standardOutput = $cli.StandardOutput.ReadToEnd()
        $standardError = $cli.StandardError.ReadToEnd()
        $cli.WaitForExit()
        if ($cli.ExitCode -eq 0) {
            $ready = $true
            Write-Output $standardOutput.TrimEnd()
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not $ready) {
        throw "Balls Canary did not become ready. Last CLI error: $standardError"
    }

    Write-Output "Installed $versionId in $versionRoot"
    Write-Output "State: $stateRoot"
    Write-Output "Pipe: $PipeName"
    Write-Output "PID: $($daemon.Id)"
}
catch {
    if ($startedDaemon -and $null -ne $daemon -and -not $daemon.HasExited) {
        $daemon.Kill($true)
        $daemon.WaitForExit()
    }
    if ($startedDaemon -and $null -ne $pidPath -and (Test-Path -LiteralPath $pidPath)) {
        Remove-Item -LiteralPath $pidPath
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
