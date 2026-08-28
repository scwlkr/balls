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

function Invoke-BallsBoundedProcess {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string] $Arguments,
        [Parameter(Mandatory = $true)][string] $WorkingDirectory,
        [Parameter(Mandatory = $true)][int] $TimeoutMilliseconds,
        [Parameter(Mandatory = $true)][string] $TimeoutCode)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'process_start_failed' }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            try { $process.Kill() } catch {}
            [void]$process.WaitForExit(5000)
            throw $TimeoutCode
        }
        $standardOutput = [string]$standardOutputTask.GetAwaiter().GetResult()
        $standardError = [string]$standardErrorTask.GetAwaiter().GetResult()
        if ($standardOutput.Length + $standardError.Length -gt 65536) {
            throw 'process_output_oversized'
        }
        return [ordered]@{
            exitCode = [int]$process.ExitCode
            standardOutput = $standardOutput
            standardError = $standardError
        }
    }
    finally {
        $process.Dispose()
    }
}

function Start-BallsProductDaemon {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string] $Arguments,
        [Parameter(Mandatory = $true)][string] $WorkingDirectory)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        $process.Dispose()
        throw 'process_start_failed'
    }
    return $process
}

function Stop-BallsOwnedProductProcesses {
    param([Parameter(Mandatory = $true)][string] $OwnedRoot)

    foreach ($process in @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue)) {
        if ($process.Name -notin @('balls.exe', 'ballsd.exe') `
                -or [string]$process.CommandLine -notlike "*$OwnedRoot*") {
            continue
        }
        try { Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop }
        catch {}
    }
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
        [AllowNull()][string] $IgnoredRunId,
        [switch] $ProductOnly)

    if ($env:OS -ne 'Windows_NT') {
        throw 'windows_required'
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $identityBytes = [Text.Encoding]::UTF8.GetBytes($identity.User.Value)
    $identitySha256 = ([BitConverter]::ToString(
        [Security.Cryptography.SHA256]::Create().ComputeHash($identityBytes))).Replace(
            '-',
            '').ToLowerInvariant()
    $isElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $accountKind = 'standard'
    $integrity = 'medium'
    if ($isElevated) {
        $accountKind = 'administrator'
        $integrity = 'high'
    }

    $currentVersion = Get-ItemProperty `
        -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' `
        -ErrorAction Stop
    $productName = [string]$currentVersion.ProductName
    $buildNumber = [string]$currentVersion.CurrentBuildNumber
    if (-not $ProductOnly) {
        $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
        $productName = [string]$operatingSystem.Caption
        $buildNumber = [string]$operatingSystem.BuildNumber
    }
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

    $networkCategories = @('inspection-account-only')
    $firewallProfiles = @('inspection-account-only')
    if (-not $ProductOnly) {
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
            identitySha256 = $identitySha256
        }
        windows = [ordered]@{
            productName = $productName
            displayVersion = $displayVersion
            buildNumber = $buildNumber
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
                $portText = [string]$port
                if ($portText -in @('Any', '445')) {
                    $matchesPort = $true
                    break
                }
                if ($portText -match '^(\d+)-(\d+)$' `
                        -and [int]$Matches[1] -le 445 `
                        -and [int]$Matches[2] -ge 445) {
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
    foreach ($property in @(
            'EnableSMB1Protocol',
            'EnableSMB2Protocol',
            'RequireSecuritySignature',
            'RejectUnencryptedAccess')) {
        if ($server.PSObject.Properties.Name -notcontains $property `
                -or $null -eq $server.$property) {
            throw 'native_inspection_incomplete'
        }
    }
    if ($client.PSObject.Properties.Name -notcontains 'EnableInsecureGuestLogons' `
            -or $null -eq $client.EnableInsecureGuestLogons) {
        throw 'native_inspection_incomplete'
    }
    $serverService = Get-Service -Name 'LanmanServer' -ErrorAction Stop
    $firewallService = Get-Service -Name 'MpsSvc' -ErrorAction Stop
    $shareCommand = @(Get-Command -Name New-SmbShare -CommandType Function, Cmdlet -ErrorAction Stop)[0]
    $serverEncryptionCiphers = @()
    if ($null -ne $server.EncryptionCiphers) {
        $serverEncryptionCiphers = @([string]$server.EncryptionCiphers -split ',\s*')
    }
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
    $connectedPrivateProfiles = @(
        Get-NetConnectionProfile -ErrorAction Stop |
            Where-Object {
                (([string]$_.IPv4Connectivity -ne 'Disconnected') `
                    -or ([string]$_.IPv6Connectivity -ne 'Disconnected')) `
                    -and ([string]$_.NetworkCategory -eq 'Private')
            }).Count
    $privateFirewall = Get-NetFirewallProfile `
        -Name 'Private' `
        -PolicyStore ActiveStore `
        -ErrorAction Stop
    $publicFirewall = Get-NetFirewallProfile `
        -Name 'Public' `
        -PolicyStore ActiveStore `
        -ErrorAction Stop
    $firewallProfiles = @(
        Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop |
            Where-Object Enabled |
            ForEach-Object { ([string]$_.Name).ToLowerInvariant() } |
            Sort-Object -Unique)

    return [ordered]@{
        serverServiceRunning = [string]$serverService.Status -eq 'Running'
        firewallServiceRunning = [string]$firewallService.Status -eq 'Running'
        serverSmb1Enabled = [bool]$server.EnableSMB1Protocol
        serverSmb2Enabled = [bool]$server.EnableSMB2Protocol
        serverMaximumDialect = [string]$server.Smb2DialectMax
        serverSigningRequired = [bool]$server.RequireSecuritySignature
        serverEncryptionSupported = [bool]($null -ne $shareCommand.Parameters['EncryptData'])
        serverRejectsUnencryptedAccess = [bool]$server.RejectUnencryptedAccess
        serverEncryptionCiphers = @($serverEncryptionCiphers)
        clientSigningRequired = $clientSigningRequired
        clientEncryptionRequired = $clientEncryptionRequired
        insecureGuestLogonsEnabled = [bool]$client.EnableInsecureGuestLogons
        serverSmb1FeatureState = Get-BallsFeatureState -Name 'SMB1Protocol-Server'
        clientSmb1FeatureState = Get-BallsFeatureState -Name 'SMB1Protocol-Client'
        connectedPrivateProfiles = [int]$connectedPrivateProfiles
        networkCategories = $networkCategories
        privateFirewallEnabled = [bool]$privateFirewall.Enabled
        privateDefaultInboundAction = [string]$privateFirewall.DefaultInboundAction
        publicFirewallEnabled = [bool]$publicFirewall.Enabled
        publicDefaultInboundAction = [string]$publicFirewall.DefaultInboundAction
        firewallProfiles = $firewallProfiles
        publicSmbAllowRules = [int]$rules.allow
        publicSmbBlockRules = [int]$rules.block
    }
}

function Invoke-BallsBoundedNativeObservation {
    $script = @(
        '$ErrorActionPreference = ''Stop''',
        '$ProgressPreference = ''SilentlyContinue''',
        "function Get-BallsFeatureState {`n$(${function:Get-BallsFeatureState}.ToString())`n}",
        "function Get-BallsPublicSmbRuleCounts {`n$(${function:Get-BallsPublicSmbRuleCounts}.ToString())`n}",
        "function Get-BallsNativeObservation {`n$(${function:Get-BallsNativeObservation}.ToString())`n}",
        'Get-BallsNativeObservation | ConvertTo-Json -Compress -Depth 8') -join "`n"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $result = Invoke-BallsBoundedProcess `
        -FilePath (Join-Path $PSHOME 'powershell.exe') `
        -Arguments "-NoLogo -NoProfile -NonInteractive -EncodedCommand $encoded" `
        -WorkingDirectory $env:TEMP `
        -TimeoutMilliseconds 30000 `
        -TimeoutCode 'native_inspection_timeout'
    if ($result.exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($result.standardOutput)) {
        throw 'native_inspection_failed'
    }
    return $result.standardOutput | ConvertFrom-Json -ErrorAction Stop
}

function Get-BallsDaemonExitFailure {
    param(
        [Parameter(Mandatory = $true)] $Process,
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
        [AllowNull()] $DaemonProcess)

    $root = Join-Path $env:TEMP "BallsSmbReadiness-$RunId"
    $package = Join-Path $env:USERPROFILE $StagedPackageName
    $daemonStopped = $true
    if ($null -ne $DaemonProcess) {
        try {
            if (-not $DaemonProcess.HasExited) {
                $DaemonProcess.Kill()
                [void]$DaemonProcess.WaitForExit(10000)
            }
            $daemonStopped = $DaemonProcess.HasExited
        }
        catch {
            try { $daemonStopped = $DaemonProcess.HasExited }
            catch { $daemonStopped = $false }
        }
    }
    else {
        Stop-BallsOwnedProductProcesses -OwnedRoot $root
    }
    Stop-BallsOwnedProductProcesses -OwnedRoot $root

    $cleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        Stop-BallsOwnedProductProcesses -OwnedRoot $root
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
        if ($daemonStopped -and $stateRemoved -and $packageRemoved) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $cleanupDeadline)
    return [ordered]@{
        daemonStopped = $daemonStopped
        stateRemoved = $stateRemoved
        packageRemoved = $packageRemoved
        complete = $daemonStopped -and $stateRemoved -and $packageRemoved
    }
}

$operationStage = 'environment'
$mode = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_MODE' -Pattern '^(preflight|product-preflight|native|run|cleanup)$'

if ($mode -eq 'preflight') {
    $operationStage = 'preflight'
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

if ($mode -eq 'product-preflight') {
    $operationStage = 'product_preflight'
    try {
        Write-BallsConformanceResult -Value (Get-BallsPreflight -ProductOnly) -ExitCode 0
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

if ($mode -eq 'native') {
    $operationStage = 'native_inspection'
    try {
        Write-BallsConformanceResult -Value ([ordered]@{
            schema = 'balls-windows-smb-readiness-native-v1'
            operation = 'windows-smb-readiness-v1'
            outcome = 'observed'
            observation = Invoke-BallsBoundedNativeObservation
        }) -ExitCode 0
    }
    catch {
        Write-BallsConformanceResult -Value ([ordered]@{
            schema = 'balls-windows-smb-readiness-native-v1'
            operation = 'windows-smb-readiness-v1'
            outcome = 'failed'
            code = 'native_inspection_failed'
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
$failureCode = 'product_execution_failed'
$succeeded = $false

try {
    $operationStage = 'preconditions'
    $failureCode = 'target_precondition_mismatch'
    $preflight = Get-BallsPreflight `
        -IgnoredPackageName $stagedPackageName `
        -IgnoredRunId $runId `
        -ProductOnly
    if ($preflight.outcome -ne 'ready' `
            -or $preflight.computerName -ine $expectedComputerName `
            -or $preflight.account.kind -ne $expectedAccountKind `
            -or ($expectedAccountKind -eq 'administrator' -and -not $preflight.account.elevated)) {
        throw $failureCode
    }

    $operationStage = 'package'
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
    $failureCode = 'package_probe_timeout'
    $cliVersionResult = Invoke-BallsBoundedProcess `
        -FilePath $cli `
        -Arguments '--version' `
        -WorkingDirectory $extractPath `
        -TimeoutMilliseconds 10000 `
        -TimeoutCode $failureCode
    $cliVersion = $cliVersionResult.standardOutput.Trim()
    if ($cliVersionResult.exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($cliVersion)) {
        $failureCode = 'package_identity_mismatch'
        throw $failureCode
    }
    $daemonVersionResult = Invoke-BallsBoundedProcess `
        -FilePath $daemon `
        -Arguments '--version' `
        -WorkingDirectory $extractPath `
        -TimeoutMilliseconds 10000 `
        -TimeoutCode $failureCode
    $daemonVersion = $daemonVersionResult.standardOutput.Trim()
    if ($daemonVersionResult.exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($daemonVersion)) {
        $failureCode = 'package_identity_mismatch'
        throw $failureCode
    }

    $operationStage = 'daemon_start'
    $failureCode = 'daemon_start_failed'
    $pipeName = "balls-conformance-$runId"
    try {
        $daemonProcess = Start-BallsProductDaemon `
            -FilePath $daemon `
            -Arguments "--data-directory `"$statePath`" --pipe-name $pipeName --node-name Balls-Conformance --files-readiness-conformance" `
            -WorkingDirectory $extractPath
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

    $operationStage = 'daemon_poll'
    $failureCode = 'readiness_cli_timeout'
    $ready = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    Start-Sleep -Seconds 2
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($daemonProcess.HasExited) { break }
        $statusResult = Invoke-BallsBoundedProcess `
            -FilePath $cli `
            -Arguments "--output json --pipe-name $pipeName files readiness" `
            -WorkingDirectory $extractPath `
            -TimeoutMilliseconds 25000 `
            -TimeoutCode $failureCode
        $statusOutput = $statusResult.standardOutput.Trim()
        if ($statusResult.exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($statusOutput)) {
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

    $operationStage = 'readiness'
    $failureCode = 'readiness_cli_failed'
    $readinessResult = Invoke-BallsBoundedProcess `
        -FilePath $cli `
        -Arguments "--output json --pipe-name $pipeName files readiness" `
        -WorkingDirectory $extractPath `
        -TimeoutMilliseconds 25000 `
        -TimeoutCode 'readiness_cli_timeout'
    $readinessJson = $readinessResult.standardOutput.Trim()
    if ($readinessResult.exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($readinessJson)) {
        throw $failureCode
    }
    $readinessEnvelope = $readinessJson | ConvertFrom-Json -ErrorAction Stop
    if ($readinessEnvelope.outputVersion -ne 1 -or $null -eq $readinessEnvelope.result) {
        throw $failureCode
    }
    $productReadiness = $readinessEnvelope.result
    if ($productReadiness.provider -ne 'windows-smb-3.1.1-v1' -or @($productReadiness.checks).Count -ne 9) {
        throw $failureCode
    }

    $product = [ordered]@{
        commit = $expectedCommit
        packageSha256 = $actualPackageHash
        packageName = $sourcePackageName
        version = [string]$manifest.version
        cliVersion = $cliVersion
        daemonVersion = $daemonVersion
        daemonPrivilege = 'unelevated'
    }
    $succeeded = $true
}
catch {
    if ($_.Exception.Message -in @(
            'package_probe_timeout',
            'readiness_cli_timeout',
            'native_inspection_timeout',
            'native_inspection_failed')) {
        $failureCode = $_.Exception.Message
    }
    $succeeded = $false
}

$operationStage = 'cleanup'
$cleanup = Remove-BallsOwnedArtifacts `
    -RunId $runId `
    -StagedPackageName $stagedPackageName `
    -DaemonProcess $daemonProcess
if ($null -ne $daemonProcess) { $daemonProcess.Dispose() }

$operationStage = 'receipt'
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
    cleanup = $cleanup
    limitations = @(
        'read-only Windows conformance; no operating-system mutation',
        'not GUI, UAC, Explorer, physical-device, or release acceptance')
}) -ExitCode 0
