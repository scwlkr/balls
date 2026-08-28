$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

function Write-BallsConformanceResult {
    param(
        [Parameter(Mandatory = $true)][object] $Value,
        [Parameter(Mandatory = $true)][int] $ExitCode)

    [Console]::Out.WriteLine(($Value | ConvertTo-Json -Compress -Depth 12))
    exit $ExitCode
}

function Get-BallsEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Pattern)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value) -or $value -notmatch $Pattern) {
        throw "invalid_environment"
    }
    return $value
}

function Get-BallsApplicationControlState {
    try {
        $policy = Get-ItemProperty `
            -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' `
            -ErrorAction Stop
        $value = $policy.VerifiedAndReputablePolicyState
        if ($value -eq 1) { return 'enforced' }
        if ($value -eq 2) { return 'evaluation' }
        return 'off'
    }
    catch {
        return 'unknown'
    }
}

function Get-BallsFeatureState {
    param([Parameter(Mandatory = $true)][string] $Name)

    try {
        return ([string](Get-WindowsOptionalFeature -Online -FeatureName $Name -ErrorAction Stop).State).ToLowerInvariant()
    }
    catch {
        return 'unknown'
    }
}

function Get-BallsPreflight {
    param(
        [AllowNull()][string] $IgnoredPackageName,
        [AllowNull()][string] $IgnoredRunId)

    if ($env:OS -ne 'Windows_NT') {
        throw 'windows_required'
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $isElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $accountKind = 'standard'
    $integrity = 'medium'
    if ($isElevated) {
        $accountKind = 'administrator'
        $integrity = 'high'
    }

    $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $currentVersion = Get-ItemProperty `
        -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' `
        -ErrorAction Stop
    $installationType = [string]$currentVersion.InstallationType
    $displayVersion = [string]$currentVersion.DisplayVersion
    if ([string]::IsNullOrWhiteSpace($displayVersion)) {
        $displayVersion = [string]$currentVersion.ReleaseId
    }

    $uacEnabled = $false
    try {
        $uacEnabled = (Get-ItemProperty `
            -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' `
            -Name EnableLUA `
            -ErrorAction Stop).EnableLUA -eq 1
    }
    catch {
        $uacEnabled = $false
    }

    $networkCategories = @(
        Get-NetConnectionProfile -ErrorAction Stop |
            ForEach-Object { ([string]$_.NetworkCategory).ToLowerInvariant() } |
            Sort-Object -Unique)
    if ($networkCategories.Count -eq 0) {
        $networkCategories = @('unknown')
    }
    $firewallProfiles = @(
        Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop |
            Where-Object Enabled |
            ForEach-Object { ([string]$_.Name).ToLowerInvariant() } |
            Sort-Object -Unique)
    if ($firewallProfiles.Count -eq 0) {
        $firewallProfiles = @('none')
    }

    $existingBallsProcesses = @(Get-Process -Name ballsd -ErrorAction SilentlyContinue).Count
    $ownedArtifacts = 0
    foreach ($artifact in @(Get-ChildItem -LiteralPath $env:USERPROFILE -Filter 'balls-smb-readiness-*.zip' -File -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($IgnoredPackageName) -or $artifact.Name -ne $IgnoredPackageName) {
            $ownedArtifacts++
        }
    }
    foreach ($artifact in @(Get-ChildItem -LiteralPath $env:TEMP -Filter 'BallsSmbReadiness-*' -Directory -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($IgnoredRunId) -or $artifact.Name -ne "BallsSmbReadiness-$IgnoredRunId") {
            $ownedArtifacts++
        }
    }
    $clean = $existingBallsProcesses -eq 0 -and $ownedArtifacts -eq 0
    $outcome = 'refused'
    if ($clean) { $outcome = 'ready' }

    return [ordered]@{
        schema = 'balls-windows-smb-readiness-preflight-v1'
        operation = 'windows-smb-readiness-v1'
        outcome = $outcome
        computerName = [Environment]::MachineName
        account = [ordered]@{
            kind = $accountKind
            elevated = $isElevated
            integrity = $integrity
        }
        windows = [ordered]@{
            productName = [string]$operatingSystem.Caption
            displayVersion = $displayVersion
            buildNumber = [string]$operatingSystem.BuildNumber
            installationType = $installationType
        }
        policy = [ordered]@{
            executionPolicy = [string](Get-ExecutionPolicy)
            uacEnabled = $uacEnabled
            applicationControl = Get-BallsApplicationControlState
        }
        network = [ordered]@{
            categories = $networkCategories
            firewallProfiles = $firewallProfiles
        }
        dirtyState = [ordered]@{
            existingBallsProcesses = $existingBallsProcesses
            ownedArtifacts = $ownedArtifacts
            clean = $clean
        }
    }
}

function Get-BallsPublicSmbRuleCounts {
    $allow = 0
    $block = 0
    foreach ($rule in @(Get-NetFirewallRule `
            -PolicyStore ActiveStore `
            -Enabled True `
            -Direction Inbound `
            -ErrorAction Stop)) {
        $profiles = @(([string]$rule.Profile -split ',') | ForEach-Object { $_.Trim() })
        if (($profiles -notcontains 'Any') -and ($profiles -notcontains 'Public')) { continue }
        $matchesPort = $false
        foreach ($filter in @($rule | Get-NetFirewallPortFilter -ErrorAction Stop)) {
            if ([string]$filter.Protocol -notin @('Any', 'TCP', '6')) { continue }
            foreach ($port in @($filter.LocalPort)) {
                if ([string]$port -in @('Any', '445')) {
                    $matchesPort = $true
                    break
                }
            }
            if ($matchesPort) { break }
        }
        if (-not $matchesPort) { continue }
        if ([string]$rule.Action -eq 'Allow') { $allow++ }
        if ([string]$rule.Action -eq 'Block') { $block++ }
    }
    return [ordered]@{ allow = $allow; block = $block }
}

function Get-BallsNativeObservation {
    $server = Get-SmbServerConfiguration -ErrorAction Stop
    $client = Get-SmbClientConfiguration -ErrorAction Stop
    $shareCommand = @(Get-Command -Name New-SmbShare -CommandType Function, Cmdlet -ErrorAction Stop)[0]
    $clientSigningRequired = $false
    if ($client.PSObject.Properties.Name -contains 'RequireSecuritySignature') {
        $clientSigningRequired = [bool]$client.RequireSecuritySignature
    }
    $clientEncryptionRequired = $false
    if ($client.PSObject.Properties.Name -contains 'RequireEncryption') {
        $clientEncryptionRequired = [bool]$client.RequireEncryption
    }
    $rules = Get-BallsPublicSmbRuleCounts
    $networkCategories = @(
        Get-NetConnectionProfile -ErrorAction Stop |
            ForEach-Object { ([string]$_.NetworkCategory).ToLowerInvariant() } |
            Sort-Object -Unique)
    $firewallProfiles = @(
        Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop |
            Where-Object Enabled |
            ForEach-Object { ([string]$_.Name).ToLowerInvariant() } |
            Sort-Object -Unique)

    return [ordered]@{
        serverSmb2Enabled = [bool]$server.EnableSMB2Protocol
        serverSigningRequired = [bool]$server.RequireSecuritySignature
        serverEncryptionSupported = [bool]($null -ne $shareCommand.Parameters['EncryptData'])
        serverRejectsUnencryptedAccess = [bool]$server.RejectUnencryptedAccess
        clientSigningRequired = $clientSigningRequired
        clientEncryptionRequired = $clientEncryptionRequired
        insecureGuestLogonsEnabled = [bool]$client.EnableInsecureGuestLogons
        serverSmb1FeatureState = Get-BallsFeatureState -Name 'SMB1Protocol-Server'
        clientSmb1FeatureState = Get-BallsFeatureState -Name 'SMB1Protocol-Client'
        networkCategories = $networkCategories
        firewallProfiles = $firewallProfiles
        publicSmbAllowRules = [int]$rules.allow
        publicSmbBlockRules = [int]$rules.block
    }
}

function Get-BallsObjectHash {
    param([Parameter(Mandatory = $true)][object] $Value)

    $json = $Value | ConvertTo-Json -Compress -Depth 10
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-BallsDaemonExitFailure {
    param(
        [Parameter(Mandatory = $true)][Diagnostics.Process] $Process,
        [AllowNull()][object] $StandardOutputTask,
        [AllowNull()][object] $StandardErrorTask)

    $Process.WaitForExit()
    $Process.Refresh()
    $exitCode = [int]$Process.ExitCode
    if ($exitCode -eq 0) {
        try {
            $output = [string]$StandardOutputTask.GetAwaiter().GetResult()
            if ($output.Length -le 32768 -and $output.Contains('ballsd ready on')) {
                return 'daemon_exited_after_ready'
            }
        }
        catch {}
        return 'daemon_exited_clean_before_ready'
    }

    switch ($exitCode) {
        2 { return 'daemon_exited_usage' }
        4 { return 'daemon_exited_startup' }
        5 { return 'daemon_exited_unsupported' }
    }
    if ($exitCode -ne -532462766) {
        $status = [BitConverter]::ToUInt32(
            [BitConverter]::GetBytes($exitCode),
            0)
        return "daemon_exited_status_$($status.ToString('X8'))"
    }

    try {
        $text = [string]$StandardErrorTask.GetAwaiter().GetResult()
        if ($text.Length -gt 32768) {
            return 'daemon_exited_dotnet_output_oversized'
        }
        if ($text.Contains('ProtectedData.Protect') `
                -or $text.Contains('WindowsCurrentUserPrivateMaterialProtector.Protect')) {
            return 'daemon_exited_dotnet_dpapi'
        }
        $types = [ordered]@{
            'System.InvalidOperationException' = 'invalid_operation'
            'System.PlatformNotSupportedException' = 'platform_unsupported'
            'System.Security.Cryptography.CryptographicException' = 'cryptographic'
            'System.IO.FileNotFoundException' = 'dependency'
            'System.IO.FileLoadException' = 'dependency'
            'System.DllNotFoundException' = 'dependency'
            'System.BadImageFormatException' = 'dependency'
            'System.UnauthorizedAccessException' = 'unauthorized'
            'System.IO.IOException' = 'io'
            'System.TypeInitializationException' = 'type_initialization'
            'System.ArgumentException' = 'argument'
        }
        foreach ($entry in $types.GetEnumerator()) {
            if ($text.Contains([string]$entry.Key)) {
                return "daemon_exited_dotnet_$($entry.Value)"
            }
        }
    }
    catch {}
    return 'daemon_exited_dotnet_other'
}

function Remove-BallsOwnedArtifacts {
    param(
        [Parameter(Mandatory = $true)][string] $RunId,
        [Parameter(Mandatory = $true)][string] $StagedPackageName,
        [AllowNull()][Diagnostics.Process] $DaemonProcess)

    $root = Join-Path $env:TEMP "BallsSmbReadiness-$RunId"
    $package = Join-Path $env:USERPROFILE $StagedPackageName
    $daemonStopped = $true
    if ($null -ne $DaemonProcess) {
        try {
            if (-not $DaemonProcess.HasExited) {
                Stop-Process -Id $DaemonProcess.Id -Force -ErrorAction Stop
                $DaemonProcess.WaitForExit(10000)
            }
            $daemonStopped = $DaemonProcess.HasExited
        }
        catch {
            $daemonStopped = $false
        }
    }
    else {
        foreach ($process in @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'ballsd.exe'" -ErrorAction SilentlyContinue)) {
            if ([string]$process.CommandLine -like "*$root*") {
                try { Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop }
                catch { $daemonStopped = $false }
            }
        }
    }

    try {
        if (Test-Path -LiteralPath $root) {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction Stop
        }
    }
    catch {}
    try {
        if (Test-Path -LiteralPath $package) {
            Remove-Item -LiteralPath $package -Force -ErrorAction Stop
        }
    }
    catch {}

    $stateRemoved = -not (Test-Path -LiteralPath $root)
    $packageRemoved = -not (Test-Path -LiteralPath $package)
    return [ordered]@{
        daemonStopped = $daemonStopped
        stateRemoved = $stateRemoved
        packageRemoved = $packageRemoved
        complete = $daemonStopped -and $stateRemoved -and $packageRemoved
    }
}

$mode = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_MODE' -Pattern '^(preflight|run|cleanup)$'

if ($mode -eq 'preflight') {
    try {
        Write-BallsConformanceResult -Value (Get-BallsPreflight) -ExitCode 0
    }
    catch {
        Write-BallsConformanceResult -Value ([ordered]@{
            schema = 'balls-windows-smb-readiness-preflight-v1'
            operation = 'windows-smb-readiness-v1'
            outcome = 'failed'
            code = 'preflight_failed'
        }) -ExitCode 1
    }
}

$runId = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_RUN_ID' -Pattern '^[0-9a-f]{32}$'
$stagedPackageName = Get-BallsEnvironmentValue `
    -Name 'BALLS_CONFORMANCE_STAGED_PACKAGE_NAME' `
    -Pattern '^balls-smb-readiness-[0-9a-f]{32}\.zip$'

if ($mode -eq 'cleanup') {
    $cleanup = Remove-BallsOwnedArtifacts `
        -RunId $runId `
        -StagedPackageName $stagedPackageName `
        -DaemonProcess $null
    $exitCode = 1
    if ($cleanup.complete) { $exitCode = 0 }
    Write-BallsConformanceResult -Value ([ordered]@{
        schema = 'balls-windows-smb-readiness-cleanup-v1'
        operation = 'windows-smb-readiness-v1'
        outcome = $(if ($cleanup.complete) { 'clean' } else { 'failed' })
    }) -ExitCode $exitCode
}

$expectedComputerName = Get-BallsEnvironmentValue `
    -Name 'BALLS_CONFORMANCE_EXPECTED_COMPUTER_NAME' `
    -Pattern '^[A-Za-z0-9][A-Za-z0-9-]{0,62}$'
$expectedAccountKind = Get-BallsEnvironmentValue `
    -Name 'BALLS_CONFORMANCE_EXPECTED_ACCOUNT_KIND' `
    -Pattern '^(administrator|standard)$'
$sourcePackageName = Get-BallsEnvironmentValue `
    -Name 'BALLS_CONFORMANCE_PACKAGE_NAME' `
    -Pattern '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.zip$'
$expectedPackageHash = Get-BallsEnvironmentValue `
    -Name 'BALLS_CONFORMANCE_PACKAGE_SHA256' `
    -Pattern '^[0-9A-F]{64}$'
$expectedCommit = Get-BallsEnvironmentValue `
    -Name 'BALLS_CONFORMANCE_COMMIT' `
    -Pattern '^[0-9a-f]{40}$'
$root = Join-Path $env:TEMP "BallsSmbReadiness-$runId"
$stagedPackagePath = Join-Path $env:USERPROFILE $stagedPackageName
$extractPath = Join-Path $root 'package'
$statePath = Join-Path $root 'state'
$daemonProcess = $null
$daemonStandardOutputTask = $null
$daemonStandardErrorTask = $null
$preflight = $null
$product = $null
$productReadiness = $null
$nativeObservation = $null
$nativeStateUnchanged = $false
$failureCode = 'product_execution_failed'
$succeeded = $false

try {
    $failureCode = 'target_precondition_mismatch'
    $preflight = Get-BallsPreflight -IgnoredPackageName $stagedPackageName -IgnoredRunId $runId
    if ($preflight.outcome -ne 'ready' `
            -or $preflight.computerName -ine $expectedComputerName `
            -or $preflight.account.kind -ne $expectedAccountKind `
            -or ($expectedAccountKind -eq 'administrator' -and -not $preflight.account.elevated)) {
        throw $failureCode
    }

    $failureCode = 'package_identity_mismatch'
    if (-not (Test-Path -LiteralPath $stagedPackagePath -PathType Leaf)) { throw $failureCode }
    $actualPackageHash = (Get-FileHash -LiteralPath $stagedPackagePath -Algorithm SHA256).Hash
    if ($actualPackageHash -ine $expectedPackageHash) { throw $failureCode }
    if (Test-Path -LiteralPath $root) { throw 'dirty_run_root' }
    New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
    Expand-Archive -LiteralPath $stagedPackagePath -DestinationPath $extractPath -ErrorAction Stop

    $manifest = Get-Content -LiteralPath (Join-Path $extractPath 'canary.json') -Raw -ErrorAction Stop |
        ConvertFrom-Json -ErrorAction Stop
    if ($manifest.product -ne 'Balls' `
            -or $manifest.platform -ne 'windows' `
            -or $manifest.architecture -ne 'x64' `
            -or $manifest.commit -ine $expectedCommit) {
        throw $failureCode
    }

    $extractFullPath = [IO.Path]::GetFullPath($extractPath).TrimEnd('\') + '\'
    foreach ($line in @(Get-Content -LiteralPath (Join-Path $extractPath 'SHA256SUMS') -ErrorAction Stop)) {
        if ($line -notmatch '^([0-9A-F]{64})  (.+)$') { throw $failureCode }
        $relative = $Matches[2].Replace('/', '\')
        $file = [IO.Path]::GetFullPath((Join-Path $extractPath $relative))
        if (-not $file.StartsWith($extractFullPath, [StringComparison]::OrdinalIgnoreCase) `
                -or -not (Test-Path -LiteralPath $file -PathType Leaf) `
                -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $Matches[1]) {
            throw $failureCode
        }
    }

    $cli = Join-Path $extractPath 'balls\balls.exe'
    $daemon = Join-Path $extractPath 'ballsd\ballsd.exe'
    $cliVersion = ((& $cli --version 2>$null) | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cliVersion)) { throw $failureCode }
    $daemonVersion = ((& $daemon --version 2>$null) | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($daemonVersion)) { throw $failureCode }

    $nativeBefore = Get-BallsNativeObservation
    $nativeBeforeHash = Get-BallsObjectHash -Value $nativeBefore
    $failureCode = 'daemon_start_failed'
    $pipeName = "balls-conformance-$runId"
    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $daemon
        $startInfo.Arguments = "--data-directory `"$statePath`" --pipe-name $pipeName --node-name Balls-Conformance --files-readiness-conformance"
        $startInfo.WorkingDirectory = $extractPath
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $daemonProcess = [Diagnostics.Process]::new()
        $daemonProcess.StartInfo = $startInfo
        if (-not $daemonProcess.Start()) { throw 'daemon_start_failed' }
        $daemonStandardOutputTask = $daemonProcess.StandardOutput.ReadToEndAsync()
        $daemonStandardErrorTask = $daemonProcess.StandardError.ReadToEndAsync()
    }
    catch {
        $failureCode = switch ($_.Exception.GetType().FullName) {
            'System.ComponentModel.Win32Exception' { 'daemon_start_win32' }
            'System.InvalidOperationException' { 'daemon_start_invalid_operation' }
            'System.IO.IOException' { 'daemon_start_io' }
            'System.UnauthorizedAccessException' { 'daemon_start_unauthorized' }
            default { 'daemon_start_other' }
        }
        throw $failureCode
    }

    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($daemonProcess.HasExited) { break }
        $statusOutput = ''
        $statusExitCode = -1
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $statusOutput = ((& $cli --output json --pipe-name $pipeName files readiness 2>$null) | Out-String).Trim()
            $statusExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        if ($statusExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($statusOutput)) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        if ($daemonProcess.HasExited) {
            $failureCode = Get-BallsDaemonExitFailure `
                -Process $daemonProcess `
                -StandardOutputTask $daemonStandardOutputTask `
                -StandardErrorTask $daemonStandardErrorTask
        }
        else {
            $failureCode = 'daemon_readiness_timeout'
        }
        throw $failureCode
    }

    $failureCode = 'readiness_cli_failed'
    $readinessJson = ((& $cli --output json --pipe-name $pipeName files readiness 2>$null) | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($readinessJson)) { throw $failureCode }
    $readinessEnvelope = $readinessJson | ConvertFrom-Json -ErrorAction Stop
    if ($readinessEnvelope.outputVersion -ne 1 -or $null -eq $readinessEnvelope.result) {
        throw $failureCode
    }
    $productReadiness = $readinessEnvelope.result
    if ($productReadiness.provider -ne 'windows-smb-3.1.1-v1' -or @($productReadiness.checks).Count -ne 9) {
        throw $failureCode
    }

    $nativeObservation = Get-BallsNativeObservation
    $nativeStateUnchanged = $nativeBeforeHash -eq (Get-BallsObjectHash -Value $nativeObservation)
    if (-not $nativeStateUnchanged) { throw 'native_state_changed' }
    $product = [ordered]@{
        commit = $expectedCommit
        packageSha256 = $actualPackageHash
        packageName = $sourcePackageName
        version = [string]$manifest.version
        cliVersion = $cliVersion
        daemonVersion = $daemonVersion
    }
    $succeeded = $true
}
catch {
    $succeeded = $false
}

$cleanup = Remove-BallsOwnedArtifacts `
    -RunId $runId `
    -StagedPackageName $stagedPackageName `
    -DaemonProcess $daemonProcess
if ($null -ne $daemonProcess) { $daemonProcess.Dispose() }

if (-not $succeeded -or -not $cleanup.complete) {
    Write-BallsConformanceResult -Value ([ordered]@{
        schema = 'balls-windows-smb-readiness-guest-v1'
        operation = 'windows-smb-readiness-v1'
        outcome = 'failed'
        code = $(if ($cleanup.complete) { $failureCode } else { 'cleanup_incomplete' })
    }) -ExitCode 1
}

Write-BallsConformanceResult -Value ([ordered]@{
    schema = 'balls-windows-smb-readiness-guest-v1'
    operation = 'windows-smb-readiness-v1'
    outcome = 'passed'
    preflight = $preflight
    product = $product
    productReadiness = $productReadiness
    nativeObservation = $nativeObservation
    nativeStateUnchanged = $nativeStateUnchanged
    cleanup = $cleanup
    limitations = @(
        'read-only Windows conformance; no operating-system mutation',
        'not GUI, UAC, Explorer, physical-device, or release acceptance')
}) -ExitCode 0
