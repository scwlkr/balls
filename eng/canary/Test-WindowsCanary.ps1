[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [string] $ChecksumPath,

    [Parameter(Mandatory)]
    [string] $InstallerPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$daemon = $null

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$installRoot = Join-Path $temporaryBase ("balls-canary-smoke-{0}" -f [guid]::NewGuid().ToString('N'))
$pipeName = "balls-canary-smoke-{0}" -f [guid]::NewGuid().ToString('N')

try {
    & $InstallerPath `
        -PackagePath $PackagePath `
        -ChecksumPath $ChecksumPath `
        -InstallRoot $installRoot `
        -PipeName $pipeName `
        -NodeName 'Balls Canary Smoke'

    $pidPath = Join-Path $installRoot 'ballsd.pid'
    $daemonPid = [int](Get-Content -LiteralPath $pidPath -Raw)
    $daemon = Get-Process -Id $daemonPid
    if ($daemon.HasExited) {
        throw 'The installed Canary daemon exited after its readiness check.'
    }

    Write-Output "Windows Canary archive smoke passed with fresh state: $installRoot"
}
finally {
    if ($null -ne $daemon -and -not $daemon.HasExited) {
        $daemon.Kill($true)
        $daemon.WaitForExit()
    }
    if (Test-Path -LiteralPath $installRoot) {
        $resolvedInstallRoot = [IO.Path]::GetFullPath($installRoot)
        if (-not $resolvedInstallRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected smoke path: $resolvedInstallRoot"
        }
        Remove-Item -LiteralPath $resolvedInstallRoot -Recurse -Force
    }
}
