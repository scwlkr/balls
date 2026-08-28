$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

$script:Operation = 'windows-circle-files-host-v1'
$script:StorageInspectionOperation = 'windows-circle-files-host-storage-inspection-v1'
$script:Stage = 'initializing'
$script:MaximumUnrelatedInventoryEntries = 4096
$script:MaximumUnrelatedFileBytes = 4 * 1024 * 1024
$script:MaximumUnrelatedTotalFileBytes = 16 * 1024 * 1024
$script:MaximumUnrelatedShares = 256
$script:MaximumUnrelatedFirewallRules = 4096
$script:MaximumUnrelatedAccounts = 512
$script:MaximumUnrelatedGroupMembers = 4096
$script:MaximumUnrelatedMappings = 512
$script:MaximumUnrelatedServices = 2048
$script:HostRemovalPolicies = @{
    remove = [pscustomobject][ordered]@{
        previewStage = 'remove-preview'
        applyStage = 'remove-apply'
        previewTimeoutCode = 'remove_preview_timeout'
        previewFailureCode = 'remove_preview_failed'
        applyTimeoutCode = 'remove_apply_timeout'
        applyFailureCode = 'remove_apply_failed'
        incompleteCode = 'remove_incomplete'
    }
    cleanup = [pscustomobject][ordered]@{
        previewStage = 'cleanup-preview'
        applyStage = 'cleanup-apply'
        previewTimeoutCode = 'cleanup_preview_timeout'
        previewFailureCode = 'cleanup_preview_failed'
        applyTimeoutCode = 'cleanup_apply_timeout'
        applyFailureCode = 'cleanup_apply_failed'
        incompleteCode = 'cleanup_remove_incomplete'
    }
}

function Write-BallsResult {
    param(
        [Parameter(Mandatory = $true)][object] $Value,
        [Parameter(Mandatory = $true)][int] $ExitCode)
    [Console]::Out.WriteLine(($Value | ConvertTo-Json -Compress -Depth 16))
    exit $ExitCode
}

function Get-BallsEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Pattern)
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value) -or $value -notmatch $Pattern) {
        throw 'invalid_environment'
    }
    return $value
}

function ConvertFrom-BallsBase64Url {
    param([Parameter(Mandatory = $true)][string] $Value)
    $base64 = $Value.Replace('-', '+').Replace('_', '/')
    while (($base64.Length % 4) -ne 0) { $base64 += '=' }
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($base64))
}

function Get-BallsSha256Bytes {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}

function Get-BallsSha256Text {
    param([Parameter(Mandatory = $true)][string] $Value)
    return Get-BallsSha256Bytes -Bytes ([Text.Encoding]::UTF8.GetBytes($Value))
}

function Get-BallsFingerprintHash {
    param([Parameter(Mandatory = $true)][object] $Value)
    return Get-BallsSha256Text -Value ($Value | ConvertTo-Json -Compress -Depth 16)
}

function Get-BallsPropertyText {
    param(
        [Parameter(Mandatory = $true)][object] $Value,
        [Parameter(Mandatory = $true)][string] $Name)
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }
    if ($property.Value -is [Array]) {
        return (@($property.Value | ForEach-Object { [string]$_ }) -join ',')
    }
    return [string]$property.Value
}

function Assert-BallsBoundedCount {
    param(
        [Parameter(Mandatory = $true)][object[]] $Values,
        [Parameter(Mandatory = $true)][int] $Maximum)
    if ($Values.Count -gt $Maximum) { throw 'unrelated_state_inventory_oversized' }
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
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            try { $process.Kill() } catch {}
            [void]$process.WaitForExit(5000)
            throw $TimeoutCode
        }
        $stdout = [string]$stdoutTask.GetAwaiter().GetResult()
        $stderr = [string]$stderrTask.GetAwaiter().GetResult()
        if ([Text.Encoding]::UTF8.GetByteCount($stdout + $stderr) -gt 65536) {
            throw 'process_output_oversized'
        }
        return [ordered]@{
            exitCode = [int]$process.ExitCode
            standardOutput = $stdout
            standardError = $stderr
        }
    }
    finally { $process.Dispose() }
}

function ConvertFrom-BallsCliResult {
    param(
        [Parameter(Mandatory = $true)][object] $ProcessResult,
        [Parameter(Mandatory = $true)][string] $FailureCode)
    if ($ProcessResult.exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($ProcessResult.standardOutput)) {
        throw $FailureCode
    }
    $envelope = $ProcessResult.standardOutput.Trim() | ConvertFrom-Json -ErrorAction Stop
    if ($envelope.outputVersion -ne 1 -or $null -eq $envelope.result) { throw $FailureCode }
    return $envelope.result
}

function Get-BallsCliErrorCode {
    param([Parameter(Mandatory = $true)][object] $ProcessResult)
    if ($ProcessResult.exitCode -eq 0 -or [string]::IsNullOrWhiteSpace($ProcessResult.standardError)) {
        return 'unexpected_success'
    }
    try {
        $envelope = $ProcessResult.standardError.Trim() | ConvertFrom-Json -ErrorAction Stop
        if ($envelope.outputVersion -eq 1 -and $null -ne $envelope.error) {
            return [string]$envelope.error.code
        }
    }
    catch {}
    return 'invalid_cli_error'
}

function Get-BallsIdentitySha256 {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return Get-BallsSha256Text -Value $identity.User.Value
}

function ConvertTo-BallsSidValue {
    param([Parameter(Mandatory = $true)][string] $Identity)
    try { return ([Security.Principal.SecurityIdentifier]::new($Identity)).Value }
    catch { return ([Security.Principal.NTAccount]::new($Identity)).Translate([Security.Principal.SecurityIdentifier]).Value }
}

function Get-BallsApplicationControlState {
    try {
        $value = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop).VerifiedAndReputablePolicyState
        if ($value -eq 1) { return 'enforced' }
        if ($value -eq 2) { return 'evaluation' }
        return 'off'
    }
    catch { return 'unknown' }
}

function Get-BallsStorageObservation {
    param([Parameter(Mandatory = $true)][string] $Path)
    $root = [IO.Path]::GetPathRoot($Path)
    if ($root -notmatch '^[C-Z]:\\$') { throw 'disposable_path_not_local_disk' }
    $driveLetter = $root.Substring(0, 1)
    $volumes = @(Get-Volume -DriveLetter $driveLetter -ErrorAction Stop)
    $partitions = @(Get-Partition -DriveLetter $driveLetter -ErrorAction Stop)
    if ($volumes.Count -ne 1 -or $partitions.Count -ne 1) {
        throw 'disposable_path_not_local_disk'
    }
    $volume = $volumes[0]
    $partition = $partitions[0]
    $disks = @(Get-Disk -Number $partition.DiskNumber -ErrorAction Stop)
    if ($disks.Count -ne 1) { throw 'disposable_path_not_local_disk' }
    $disk = $disks[0]
    $fileSystem = Get-BallsPropertyText -Value $volume -Name 'FileSystem'
    if ([string]::IsNullOrWhiteSpace($fileSystem)) {
        $fileSystem = Get-BallsPropertyText -Value $volume -Name 'FileSystemType'
    }
    $busType = Get-BallsPropertyText -Value $disk -Name 'BusType'
    $operationalStatus = Get-BallsPropertyText -Value $disk -Name 'OperationalStatus'
    $allowedBusTypes = @('ATA', 'NVMe', 'RAID', 'SAS', 'SATA', 'SCSI')
    if ([IO.DriveInfo]::new($root).DriveType -ne [IO.DriveType]::Fixed `
            -or $fileSystem -notin @('NTFS', 'ReFS') `
            -or (Get-BallsPropertyText -Value $volume -Name 'HealthStatus') -ne 'Healthy' `
            -or $busType -notin $allowedBusTypes `
            -or $busType -in @('File Backed Virtual', 'iSCSI', 'Unknown', 'Virtual') `
            -or [bool]$disk.IsOffline `
            -or [bool]$disk.IsReadOnly `
            -or $operationalStatus -notmatch 'Online' `
            -or [string]$partition.Type -in @('Unknown', 'Reserved')) {
        throw 'disposable_path_not_local_disk'
    }
    $volumeEvidence = [ordered]@{
        uniqueId = Get-BallsPropertyText -Value $volume -Name 'UniqueId'
        path = Get-BallsPropertyText -Value $volume -Name 'Path'
        driveLetter = $driveLetter.ToUpperInvariant()
        fileSystem = $fileSystem
        size = Get-BallsPropertyText -Value $volume -Name 'Size'
        allocationUnitSize = Get-BallsPropertyText -Value $volume -Name 'AllocationUnitSize'
        partitionDiskNumber = Get-BallsPropertyText -Value $partition -Name 'DiskNumber'
        partitionNumber = Get-BallsPropertyText -Value $partition -Name 'PartitionNumber'
        partitionOffset = Get-BallsPropertyText -Value $partition -Name 'Offset'
        partitionSize = Get-BallsPropertyText -Value $partition -Name 'Size'
    }
    $diskEvidence = [ordered]@{
        uniqueId = Get-BallsPropertyText -Value $disk -Name 'UniqueId'
        serialNumber = Get-BallsPropertyText -Value $disk -Name 'SerialNumber'
        number = Get-BallsPropertyText -Value $disk -Name 'Number'
        friendlyName = Get-BallsPropertyText -Value $disk -Name 'FriendlyName'
        busType = $busType
        partitionStyle = Get-BallsPropertyText -Value $disk -Name 'PartitionStyle'
        size = Get-BallsPropertyText -Value $disk -Name 'Size'
        location = Get-BallsPropertyText -Value $disk -Name 'Location'
    }
    $volumeIdentity = Get-BallsFingerprintHash -Value $volumeEvidence
    $diskIdentity = Get-BallsFingerprintHash -Value $diskEvidence
    return [ordered]@{
        localDiskBacked = $true
        volumeIdentitySha256 = $volumeIdentity
        diskIdentitySha256 = $diskIdentity
        fileSystem = $fileSystem
        busType = $busType
    }
}

function Get-BallsAuthorizedStorage {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ExpectedVolumeSha256,
        [Parameter(Mandatory = $true)][string] $ExpectedDiskSha256)
    $storage = Get-BallsStorageObservation -Path $Path
    if ($storage.volumeIdentitySha256 -ne $ExpectedVolumeSha256 `
            -or $storage.diskIdentitySha256 -ne $ExpectedDiskSha256) {
        throw 'disposable_storage_identity_mismatch'
    }
    return $storage
}

function Test-BallsDisposablePathShape {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [switch] $RequireMissing)
    if ($Path -notmatch '^[C-Z]:\\BallsConformance\\Issue124-[A-Za-z0-9][A-Za-z0-9-]{2,39}$' `
            -or $Path -ne [IO.Path]::GetFullPath($Path).TrimEnd('\')) {
        throw 'disposable_path_invalid'
    }
    for ($item = [IO.DirectoryInfo]::new($Path); $null -ne $item; $item = $item.Parent) {
        if ($item.Exists -and (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'disposable_path_reparse'
        }
    }
    if ($RequireMissing -and (Test-Path -LiteralPath $Path)) { throw 'disposable_path_not_clean' }
}

function Test-BallsPathSafe {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ExpectedVolumeSha256,
        [Parameter(Mandatory = $true)][string] $ExpectedDiskSha256,
        [switch] $RequireMissing)
    Test-BallsDisposablePathShape -Path $Path -RequireMissing:$RequireMissing
    $storage = Get-BallsAuthorizedStorage `
        -Path $Path `
        -ExpectedVolumeSha256 $ExpectedVolumeSha256 `
        -ExpectedDiskSha256 $ExpectedDiskSha256
    return $storage
}

function Get-BallsPreflight {
    param(
        [Parameter(Mandatory = $true)][string] $DisposablePath,
        [Parameter(Mandatory = $true)][string] $ExpectedVolumeSha256,
        [Parameter(Mandatory = $true)][string] $ExpectedDiskSha256,
        [AllowNull()][string] $IgnoredPackageName = $null,
        [AllowNull()][string] $IgnoredRunId = $null,
        [switch] $ProductOnly)
    if ($env:OS -ne 'Windows_NT') { throw 'windows_required' }
    $storage = Test-BallsPathSafe `
        -Path $DisposablePath `
        -ExpectedVolumeSha256 $ExpectedVolumeSha256 `
        -ExpectedDiskSha256 $ExpectedDiskSha256 `
        -RequireMissing
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $currentVersion = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop
    $uacEnabled = $false
    try {
        $uacEnabled = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name EnableLUA -ErrorAction Stop).EnableLUA -eq 1
    }
    catch {}
    $categories = @('product-account-only')
    $profiles = @('product-account-only')
    if (-not $ProductOnly) {
        $categories = @(Get-NetConnectionProfile -ErrorAction Stop | ForEach-Object { ([string]$_.NetworkCategory).ToLowerInvariant() } | Sort-Object -Unique)
        $profiles = @(Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop | Where-Object Enabled | ForEach-Object { ([string]$_.Name).ToLowerInvariant() } | Sort-Object -Unique)
    }
    if ($categories.Count -eq 0) { $categories = @('unknown') }
    if ($profiles.Count -eq 0) { $profiles = @('none') }
    $existingProcesses = @(Get-Process -Name ballsd -ErrorAction SilentlyContinue).Count
    $ownedArtifacts = @(Get-ChildItem -LiteralPath $env:USERPROFILE -Filter 'balls-host-conformance-*.zip' -File -ErrorAction SilentlyContinue | Where-Object Name -ne $IgnoredPackageName).Count
    $ownedArtifacts += @(Get-ChildItem -LiteralPath $env:TEMP -Filter 'BallsHostConformance-*' -Directory -ErrorAction SilentlyContinue | Where-Object Name -ne "BallsHostConformance-$IgnoredRunId").Count
    $clean = $elevated -and $existingProcesses -eq 0 -and $ownedArtifacts -eq 0
    return [ordered]@{
        schema = 'balls-windows-host-preflight-v1'
        operation = $script:Operation
        outcome = $(if ($clean) { 'ready' } else { 'refused' })
        computerName = [Environment]::MachineName
        account = [ordered]@{
            kind = $(if ($elevated) { 'administrator' } else { 'standard' })
            elevated = $elevated
            integrity = $(if ($elevated) { 'high' } else { 'medium' })
            identitySha256 = Get-BallsIdentitySha256
        }
        windows = [ordered]@{
            productName = [string]$currentVersion.ProductName
            displayVersion = [string]$currentVersion.DisplayVersion
            buildNumber = [string]$currentVersion.CurrentBuildNumber
            installationType = [string]$currentVersion.InstallationType
        }
        policy = [ordered]@{
            executionPolicy = [string](Get-ExecutionPolicy -ErrorAction Stop)
            uacEnabled = $uacEnabled
            applicationControl = Get-BallsApplicationControlState
        }
        network = [ordered]@{ categories = $categories; firewallProfiles = $profiles }
        dirtyState = [ordered]@{
            existingBallsProcesses = $existingProcesses
            ownedArtifacts = $ownedArtifacts
            clean = $clean
        }
        storage = $storage
    }
}

function Get-BallsStorageInspection {
    param([Parameter(Mandatory = $true)][string] $DisposablePath)
    if ($env:OS -ne 'Windows_NT') { throw 'windows_required' }
    Test-BallsDisposablePathShape -Path $DisposablePath -RequireMissing
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    return [ordered]@{
        schema = 'balls-windows-host-storage-inspection-v1'
        operation = $script:StorageInspectionOperation
        outcome = 'observed'
        computerName = [Environment]::MachineName
        account = [ordered]@{
            kind = $(if ($elevated) { 'administrator' } else { 'standard' })
            elevated = $elevated
            integrity = $(if ($elevated) { 'high' } else { 'medium' })
            identitySha256 = Get-BallsIdentitySha256
        }
        pathIdentitySha256 = Get-BallsSha256Text -Value $DisposablePath.ToUpperInvariant()
        storage = Get-BallsStorageObservation -Path $DisposablePath
    }
}

function Get-BallsPaths {
    param([Parameter(Mandatory = $true)][string] $RunId)
    $root = Join-Path $env:TEMP "BallsHostConformance-$RunId"
    return [ordered]@{
        root = $root
        extract = Join-Path $root 'product'
        state = Join-Path $root 'state'
        context = Join-Path $root 'context.json'
        seed = Join-Path $root 'seed.json'
        pid = Join-Path $root 'daemon.pid'
        stdout = Join-Path $root 'daemon.stdout.log'
        stderr = Join-Path $root 'daemon.stderr.log'
    }
}

function Get-BallsContext {
    param([Parameter(Mandatory = $true)][object] $Paths)
    if (-not (Test-Path -LiteralPath $Paths.context -PathType Leaf)) { throw 'context_missing' }
    return Get-Content -LiteralPath $Paths.context -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
}

function Stop-BallsDaemon {
    param([Parameter(Mandatory = $true)][object] $Paths)
    if (Test-Path -LiteralPath $Paths.pid -PathType Leaf) {
        $value = Get-Content -LiteralPath $Paths.pid -Raw -ErrorAction SilentlyContinue
        $pidValue = 0
        if ([int]::TryParse($value, [ref]$pidValue) -and $pidValue -gt 0) {
            $owned = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$pidValue" -ErrorAction SilentlyContinue
            if ($null -ne $owned `
                    -and [string]$owned.Name -ieq 'ballsd.exe' `
                    -and [string]$owned.CommandLine -like "*$($Paths.root)*") {
                $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
                if ($null -ne $process) {
                    try { Stop-Process -Id $pidValue -Force -ErrorAction Stop } catch {}
                    try { [void]$process.WaitForExit(10000) } catch {}
                }
            }
        }
        $stillOwned = @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue | Where-Object {
            [string]$_.Name -ieq 'ballsd.exe' -and [string]$_.CommandLine -like "*$($Paths.root)*"
        }).Count -gt 0
        if (-not $stillOwned) { Remove-Item -LiteralPath $Paths.pid -Force -ErrorAction SilentlyContinue }
    }
}

function Start-BallsDaemon {
    param(
        [Parameter(Mandatory = $true)][object] $Paths,
        [Parameter(Mandatory = $true)][string] $PipeName,
        [AllowNull()][string] $FailureStep = $null)
    Stop-BallsDaemon -Paths $Paths
    $daemon = Join-Path $Paths.extract 'ballsd\ballsd.exe'
    if (-not (Test-Path -LiteralPath $daemon -PathType Leaf)) { throw 'daemon_missing' }
    if ($null -ne $FailureStep) { $env:BALLS_TEST_WINDOWS_HOST_FAILURE_STEP = $FailureStep }
    try {
        $process = Start-Process -FilePath $daemon `
            -ArgumentList "--data-directory `"$($Paths.state)`" --pipe-name $PipeName --node-name Balls-Host-Conformance" `
            -WorkingDirectory $Paths.extract `
            -WindowStyle Hidden `
            -RedirectStandardOutput $Paths.stdout `
            -RedirectStandardError $Paths.stderr `
            -PassThru
    }
    finally { Remove-Item Env:BALLS_TEST_WINDOWS_HOST_FAILURE_STEP -ErrorAction SilentlyContinue }
    Set-Content -LiteralPath $Paths.pid -Value ([string]$process.Id) -Encoding Ascii -NoNewline
    $cli = Join-Path $Paths.extract 'balls\balls.exe'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    Start-Sleep -Seconds 2
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            [string]$errorText = ''
            if (Test-Path -LiteralPath $Paths.stderr -PathType Leaf) {
                $errorText = [string](Get-Content -LiteralPath $Paths.stderr -Raw -ErrorAction SilentlyContinue)
            }
            if ([Text.Encoding]::UTF8.GetByteCount($errorText) -gt 65536) {
                throw 'daemon_output_oversized'
            }
            if ($errorText -match 'System\.Security\.Cryptography\.CryptographicException' `
                    -and $errorText -match 'ProtectedData\.Protect') {
                throw 'daemon_private_material_unavailable'
            }
            throw 'daemon_exited_before_ready'
        }
        $status = Invoke-BallsBoundedProcess -FilePath $cli -Arguments "--output json --pipe-name $PipeName status" -WorkingDirectory $Paths.extract -TimeoutMilliseconds 15000 -TimeoutCode 'daemon_poll_timeout'
        if ($status.exitCode -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw 'daemon_readiness_timeout'
}

function Test-BallsCurrentUserDpapi {
    $plain = [byte[]]::new(32)
    $entropy = [Text.Encoding]::UTF8.GetBytes('balls/private-material/v1')
    $protected = $null
    $unprotected = $null
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($plain)
        $protected = [Security.Cryptography.ProtectedData]::Protect(
            $plain,
            $entropy,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        $unprotected = [Security.Cryptography.ProtectedData]::Unprotect(
            $protected,
            $entropy,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        if ($plain.Length -ne $unprotected.Length) { throw 'dpapi_roundtrip_mismatch' }
        $difference = 0
        for ($index = 0; $index -lt $plain.Length; $index++) {
            $difference = $difference -bor ($plain[$index] -bxor $unprotected[$index])
        }
        if ($difference -ne 0) { throw 'dpapi_roundtrip_mismatch' }
    }
    catch { throw 'daemon_private_material_unavailable' }
    finally {
        $random.Dispose()
        [Array]::Clear($plain, 0, $plain.Length)
        [Array]::Clear($entropy, 0, $entropy.Length)
        if ($null -ne $protected) { [Array]::Clear($protected, 0, $protected.Length) }
        if ($null -ne $unprotected) { [Array]::Clear($unprotected, 0, $unprotected.Length) }
    }
}

function Invoke-BallsCli {
    param(
        [Parameter(Mandatory = $true)][object] $Paths,
        [Parameter(Mandatory = $true)][string] $PipeName,
        [Parameter(Mandatory = $true)][string] $Arguments,
        [Parameter(Mandatory = $true)][string] $TimeoutCode)
    return Invoke-BallsBoundedProcess `
        -FilePath (Join-Path $Paths.extract 'balls\balls.exe') `
        -Arguments "--output json --pipe-name $PipeName $Arguments" `
        -WorkingDirectory $Paths.extract `
        -TimeoutMilliseconds 45000 `
        -TimeoutCode $TimeoutCode
}

function Expand-BallsPackage {
    param(
        [Parameter(Mandatory = $true)][object] $Paths,
        [Parameter(Mandatory = $true)][string] $StagedPackageName,
        [Parameter(Mandatory = $true)][string] $ExpectedHash,
        [Parameter(Mandatory = $true)][string] $ExpectedCommit)
    $package = Join-Path ([Environment]::CurrentDirectory) $StagedPackageName
    if (-not (Test-Path -LiteralPath $package -PathType Leaf) `
            -or (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ine $ExpectedHash) {
        throw 'package_identity_mismatch'
    }
    New-Item -ItemType Directory -Path $Paths.root -ErrorAction Stop | Out-Null
    Expand-Archive -LiteralPath $package -DestinationPath $Paths.extract -ErrorAction Stop
    $manifest = Get-Content -LiteralPath (Join-Path $Paths.extract 'canary.json') -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ($manifest.product -ne 'Balls' -or $manifest.platform -ne 'windows' -or $manifest.architecture -ne 'x64' -or $manifest.commit -ine $ExpectedCommit) {
        throw 'package_identity_mismatch'
    }
    $extractPrefix = [IO.Path]::GetFullPath($Paths.extract).TrimEnd('\') + '\'
    foreach ($line in @(Get-Content -LiteralPath (Join-Path $Paths.extract 'SHA256SUMS') -ErrorAction Stop)) {
        if ($line -notmatch '^([0-9A-F]{64})  (.+)$') { throw 'package_identity_mismatch' }
        $file = [IO.Path]::GetFullPath((Join-Path $Paths.extract $Matches[2].Replace('/', '\')))
        if (-not $file.StartsWith($extractPrefix, [StringComparison]::OrdinalIgnoreCase) `
                -or -not (Test-Path -LiteralPath $file -PathType Leaf) `
                -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $Matches[1]) {
            throw 'package_identity_mismatch'
        }
    }
    return $manifest
}

function Get-BallsSeedObservation {
    param([Parameter(Mandatory = $true)][string] $DisposablePath)
    $seed = Join-Path $DisposablePath 'before-balls.txt'
    if (-not (Test-Path -LiteralPath $seed -PathType Leaf)) {
        return [ordered]@{ fileName = 'before-balls.txt'; length = 0; sha256 = ('0' * 64) }
    }
    $item = Get-Item -LiteralPath $seed -Force -ErrorAction Stop
    return [ordered]@{
        fileName = 'before-balls.txt'
        length = [long]$item.Length
        sha256 = (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Get-BallsBoundedFileSystemInventory {
    param(
        [Parameter(Mandatory = $true)][string] $RootPath,
        [AllowEmptyString()][string] $ExcludedPath = '')
    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) {
        $emptyEntries = @()
        return [ordered]@{
            entries = $emptyEntries
            sha256 = Get-BallsFingerprintHash -Value ([ordered]@{ entries = $emptyEntries })
        }
    }
    $rootPrefix = [IO.Path]::GetFullPath($RootPath).TrimEnd('\') + '\'
    $excludedPrefix = ''
    if (-not [string]::IsNullOrWhiteSpace($ExcludedPath)) {
        $excludedPrefix = [IO.Path]::GetFullPath($ExcludedPath).TrimEnd('\') + '\'
    }
    $queue = [Collections.Queue]::new()
    $queue.Enqueue((Get-Item -LiteralPath $RootPath -Force -ErrorAction Stop))
    $entries = [Collections.Generic.List[object]]::new()
    [long]$totalFileBytes = 0
    while ($queue.Count -gt 0) {
        $directory = $queue.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force -ErrorAction Stop | Sort-Object FullName)) {
            $fullName = [IO.Path]::GetFullPath($item.FullName)
            if (-not [string]::IsNullOrWhiteSpace($ExcludedPath) `
                    -and ($fullName -ieq $ExcludedPath `
                        -or $fullName.StartsWith($excludedPrefix, [StringComparison]::OrdinalIgnoreCase))) {
                continue
            }
            if (-not $fullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'unrelated_state_path_escape'
            }
            if ($entries.Count -ge $script:MaximumUnrelatedInventoryEntries) {
                throw 'unrelated_state_inventory_oversized'
            }
            $relativePath = $fullName.Substring($rootPrefix.Length).Replace('\', '/')
            $reparse = ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
            $aclSha256 = Get-BallsSha256Text -Value ((Get-Acl -LiteralPath $fullName -ErrorAction Stop).Sddl)
            if ($item.PSIsContainer) {
                $entries.Add([ordered]@{
                    path = $relativePath
                    kind = $(if ($reparse) { 'directory-reparse' } else { 'directory' })
                    attributes = [int]$item.Attributes
                    aclSha256 = $aclSha256
                })
                if (-not $reparse) { $queue.Enqueue($item) }
                continue
            }
            if ($item.Length -gt $script:MaximumUnrelatedFileBytes) {
                throw 'unrelated_state_file_oversized'
            }
            $totalFileBytes += [long]$item.Length
            if ($totalFileBytes -gt $script:MaximumUnrelatedTotalFileBytes) {
                throw 'unrelated_state_inventory_oversized'
            }
            $entries.Add([ordered]@{
                path = $relativePath
                kind = $(if ($reparse) { 'file-reparse' } else { 'file' })
                attributes = [int]$item.Attributes
                length = [long]$item.Length
                contentSha256 = (Get-FileHash -LiteralPath $fullName -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
                aclSha256 = $aclSha256
            })
        }
    }
    $sortedEntries = @($entries | Sort-Object { $_.path })
    return [ordered]@{
        entries = $sortedEntries
        sha256 = Get-BallsFingerprintHash -Value ([ordered]@{ entries = $sortedEntries })
    }
}

function Get-BallsConformanceRootInventory {
    param([Parameter(Mandatory = $true)][string] $DisposablePath)
    $inventory = Get-BallsBoundedFileSystemInventory `
        -RootPath (Split-Path -Parent $DisposablePath) `
        -ExcludedPath $DisposablePath
    return $inventory.sha256
}

function Get-BallsContributedFolderInventory {
    param(
        [Parameter(Mandatory = $true)][string] $State,
        [Parameter(Mandatory = $true)][string] $DisposablePath)
    $inventory = Get-BallsBoundedFileSystemInventory -RootPath $DisposablePath
    $expectedPaths = @('before-balls.txt')
    if ($State -eq 'provisioned') {
        $expectedPaths += @('.balls-operation-v1.json', '.balls-owned-v1.json')
    }
    $actualPaths = @($inventory.entries | ForEach-Object { [string]$_.path } | Sort-Object)
    $unexpectedKinds = @($inventory.entries | Where-Object kind -ne 'file').Count
    $difference = @(Compare-Object `
        -ReferenceObject @($expectedPaths | Sort-Object) `
        -DifferenceObject $actualPaths `
        -CaseSensitive)
    return [ordered]@{
        sha256 = $inventory.sha256
        count = $inventory.entries.Count
        exact = $unexpectedKinds -eq 0 -and $difference.Count -eq 0
    }
}

function Get-BallsShareConfigurationFingerprint {
    param([Parameter(Mandatory = $true)][string] $OwnedShareName)
    $shares = @(Get-SmbShare -ErrorAction Stop | Where-Object Name -ne $OwnedShareName | Sort-Object Name)
    Assert-BallsBoundedCount -Values $shares -Maximum $script:MaximumUnrelatedShares
    $observations = [Collections.Generic.List[object]]::new()
    [int]$accessCount = 0
    foreach ($share in $shares) {
        $access = @(Get-SmbShareAccess -Name $share.Name -ErrorAction Stop | Sort-Object AccountName,AccessControlType,AccessRight)
        $accessCount += $access.Count
        if ($accessCount -gt $script:MaximumUnrelatedGroupMembers) {
            throw 'unrelated_state_inventory_oversized'
        }
        $accessShape = @($access | ForEach-Object {
            [ordered]@{
                accountName = Get-BallsPropertyText -Value $_ -Name 'AccountName'
                accessControlType = Get-BallsPropertyText -Value $_ -Name 'AccessControlType'
                accessRight = Get-BallsPropertyText -Value $_ -Name 'AccessRight'
            }
        })
        $observations.Add([ordered]@{
            name = Get-BallsPropertyText -Value $share -Name 'Name'
            path = Get-BallsPropertyText -Value $share -Name 'Path'
            description = Get-BallsPropertyText -Value $share -Name 'Description'
            scopeName = Get-BallsPropertyText -Value $share -Name 'ScopeName'
            encryptData = Get-BallsPropertyText -Value $share -Name 'EncryptData'
            folderEnumerationMode = Get-BallsPropertyText -Value $share -Name 'FolderEnumerationMode'
            cachingMode = Get-BallsPropertyText -Value $share -Name 'CachingMode'
            continuouslyAvailable = Get-BallsPropertyText -Value $share -Name 'ContinuouslyAvailable'
            concurrentUserLimit = Get-BallsPropertyText -Value $share -Name 'ConcurrentUserLimit'
            availabilityType = Get-BallsPropertyText -Value $share -Name 'AvailabilityType'
            shareState = Get-BallsPropertyText -Value $share -Name 'ShareState'
            special = Get-BallsPropertyText -Value $share -Name 'Special'
            temporary = Get-BallsPropertyText -Value $share -Name 'Temporary'
            access = $accessShape
        })
    }
    return Get-BallsFingerprintHash -Value @($observations)
}

function Get-BallsFirewallConfigurationFingerprint {
    param([Parameter(Mandatory = $true)][string] $OwnedFirewallRuleName)
    $rules = @(Get-NetFirewallRule -PolicyStore ActiveStore -ErrorAction Stop | Where-Object Name -ne $OwnedFirewallRuleName | Sort-Object Name)
    Assert-BallsBoundedCount -Values $rules -Maximum $script:MaximumUnrelatedFirewallRules
    $observations = @($rules | ForEach-Object {
        $rule = $_
        $ports = @($rule | Get-NetFirewallPortFilter -ErrorAction Stop | ForEach-Object {
            [ordered]@{
                protocol = Get-BallsPropertyText -Value $_ -Name 'Protocol'
                localPort = Get-BallsPropertyText -Value $_ -Name 'LocalPort'
                remotePort = Get-BallsPropertyText -Value $_ -Name 'RemotePort'
                icmpType = Get-BallsPropertyText -Value $_ -Name 'IcmpType'
                dynamicTarget = Get-BallsPropertyText -Value $_ -Name 'DynamicTarget'
            }
        })
        $addresses = @($rule | Get-NetFirewallAddressFilter -ErrorAction Stop | ForEach-Object {
            [ordered]@{
                localAddress = Get-BallsPropertyText -Value $_ -Name 'LocalAddress'
                remoteAddress = Get-BallsPropertyText -Value $_ -Name 'RemoteAddress'
            }
        })
        $applications = @($rule | Get-NetFirewallApplicationFilter -ErrorAction Stop | ForEach-Object {
            [ordered]@{
                program = Get-BallsPropertyText -Value $_ -Name 'Program'
                package = Get-BallsPropertyText -Value $_ -Name 'Package'
            }
        })
        $interfaces = @($rule | Get-NetFirewallInterfaceFilter -ErrorAction Stop | ForEach-Object {
            [ordered]@{
                interfaceAlias = Get-BallsPropertyText -Value $_ -Name 'InterfaceAlias'
                interfaceType = Get-BallsPropertyText -Value $_ -Name 'InterfaceType'
            }
        })
        $security = @($rule | Get-NetFirewallSecurityFilter -ErrorAction Stop | ForEach-Object {
            [ordered]@{
                authentication = Get-BallsPropertyText -Value $_ -Name 'Authentication'
                encryption = Get-BallsPropertyText -Value $_ -Name 'Encryption'
                overrideBlockRules = Get-BallsPropertyText -Value $_ -Name 'OverrideBlockRules'
                localUser = Get-BallsPropertyText -Value $_ -Name 'LocalUser'
                remoteUser = Get-BallsPropertyText -Value $_ -Name 'RemoteUser'
                remoteMachine = Get-BallsPropertyText -Value $_ -Name 'RemoteMachine'
            }
        })
        $services = @($rule | Get-NetFirewallServiceFilter -ErrorAction Stop | ForEach-Object {
            [ordered]@{ service = Get-BallsPropertyText -Value $_ -Name 'Service' }
        })
        [ordered]@{
            name = Get-BallsPropertyText -Value $rule -Name 'Name'
            displayName = Get-BallsPropertyText -Value $rule -Name 'DisplayName'
            description = Get-BallsPropertyText -Value $rule -Name 'Description'
            group = Get-BallsPropertyText -Value $rule -Name 'Group'
            enabled = Get-BallsPropertyText -Value $rule -Name 'Enabled'
            profile = Get-BallsPropertyText -Value $rule -Name 'Profile'
            direction = Get-BallsPropertyText -Value $rule -Name 'Direction'
            action = Get-BallsPropertyText -Value $rule -Name 'Action'
            edgeTraversalPolicy = Get-BallsPropertyText -Value $rule -Name 'EdgeTraversalPolicy'
            policyStoreSourceType = Get-BallsPropertyText -Value $rule -Name 'PolicyStoreSourceType'
            ports = $ports
            addresses = $addresses
            applications = $applications
            interfaces = $interfaces
            security = $security
            services = $services
        }
    })
    return Get-BallsFingerprintHash -Value $observations
}

function Get-BallsAccountConfigurationFingerprint {
    $users = @(Get-LocalUser -ErrorAction Stop | Sort-Object SID)
    $groups = @(Get-LocalGroup -ErrorAction Stop | Sort-Object SID)
    Assert-BallsBoundedCount -Values $users -Maximum $script:MaximumUnrelatedAccounts
    Assert-BallsBoundedCount -Values $groups -Maximum $script:MaximumUnrelatedAccounts
    $userShape = @($users | ForEach-Object {
        [ordered]@{
            sid = Get-BallsPropertyText -Value $_ -Name 'SID'
            enabled = Get-BallsPropertyText -Value $_ -Name 'Enabled'
            accountExpires = Get-BallsPropertyText -Value $_ -Name 'AccountExpires'
            passwordExpires = Get-BallsPropertyText -Value $_ -Name 'PasswordExpires'
            passwordLastSet = Get-BallsPropertyText -Value $_ -Name 'PasswordLastSet'
            passwordRequired = Get-BallsPropertyText -Value $_ -Name 'PasswordRequired'
            userMayChangePassword = Get-BallsPropertyText -Value $_ -Name 'UserMayChangePassword'
            principalSource = Get-BallsPropertyText -Value $_ -Name 'PrincipalSource'
        }
    })
    [int]$memberCount = 0
    $groupShape = @($groups | ForEach-Object {
        $group = $_
        $members = @(Get-LocalGroupMember -SID $group.SID -ErrorAction Stop | Sort-Object SID,Name)
        $memberCount += $members.Count
        if ($memberCount -gt $script:MaximumUnrelatedGroupMembers) {
            throw 'unrelated_state_inventory_oversized'
        }
        [ordered]@{
            sid = Get-BallsPropertyText -Value $group -Name 'SID'
            principalSource = Get-BallsPropertyText -Value $group -Name 'PrincipalSource'
            members = @($members | ForEach-Object {
                [ordered]@{
                    sid = Get-BallsPropertyText -Value $_ -Name 'SID'
                    objectClass = Get-BallsPropertyText -Value $_ -Name 'ObjectClass'
                    principalSource = Get-BallsPropertyText -Value $_ -Name 'PrincipalSource'
                }
            })
        }
    })
    return Get-BallsFingerprintHash -Value ([ordered]@{ users = $userShape; groups = $groupShape })
}

function Get-BallsSecureStoreInventoryFingerprint {
    $result = Invoke-BallsBoundedProcess `
        -FilePath "$env:SystemRoot\System32\cmdkey.exe" `
        -Arguments '/list' `
        -WorkingDirectory "$env:SystemRoot\System32" `
        -TimeoutMilliseconds 15000 `
        -TimeoutCode 'credential_inventory_timeout'
    return Get-BallsFingerprintHash -Value ([ordered]@{
        exitCode = $result.exitCode
        standardOutputSha256 = Get-BallsSha256Text -Value ([string]$result.standardOutput)
        standardErrorSha256 = Get-BallsSha256Text -Value ([string]$result.standardError)
    })
}

function Get-BallsMappingConfigurationFingerprint {
    $smbMappings = @(Get-SmbMapping -ErrorAction SilentlyContinue | Sort-Object LocalPath,RemotePath)
    $smbConnections = @(Get-SmbConnection -ErrorAction SilentlyContinue | Sort-Object ServerName,ShareName,UserName)
    $networkDrives = @(Get-PSDrive -PSProvider FileSystem -ErrorAction Stop | Where-Object { [string]$_.DisplayRoot -like '\\*' } | Sort-Object Name)
    $networkConnections = @(Get-CimInstance -ClassName Win32_NetworkConnection -ErrorAction Stop | Sort-Object LocalName,RemoteName)
    $mappingCount = $smbMappings.Count + $smbConnections.Count + $networkDrives.Count + $networkConnections.Count
    if ($mappingCount -gt $script:MaximumUnrelatedMappings) { throw 'unrelated_state_inventory_oversized' }
    return Get-BallsFingerprintHash -Value ([ordered]@{
        smbMappings = @($smbMappings | ForEach-Object {
            [ordered]@{
                localPath = Get-BallsPropertyText -Value $_ -Name 'LocalPath'
                remotePath = Get-BallsPropertyText -Value $_ -Name 'RemotePath'
                status = Get-BallsPropertyText -Value $_ -Name 'Status'
                persistent = Get-BallsPropertyText -Value $_ -Name 'Persistent'
            }
        })
        smbConnections = @($smbConnections | ForEach-Object {
            [ordered]@{
                serverName = Get-BallsPropertyText -Value $_ -Name 'ServerName'
                shareName = Get-BallsPropertyText -Value $_ -Name 'ShareName'
                userName = Get-BallsPropertyText -Value $_ -Name 'UserName'
                credential = Get-BallsPropertyText -Value $_ -Name 'Credential'
                dialect = Get-BallsPropertyText -Value $_ -Name 'Dialect'
            }
        })
        networkDrives = @($networkDrives | ForEach-Object {
            [ordered]@{
                name = Get-BallsPropertyText -Value $_ -Name 'Name'
                displayRoot = Get-BallsPropertyText -Value $_ -Name 'DisplayRoot'
            }
        })
        networkConnections = @($networkConnections | ForEach-Object {
            [ordered]@{
                localName = Get-BallsPropertyText -Value $_ -Name 'LocalName'
                remoteName = Get-BallsPropertyText -Value $_ -Name 'RemoteName'
                userName = Get-BallsPropertyText -Value $_ -Name 'UserName'
                connectionState = Get-BallsPropertyText -Value $_ -Name 'ConnectionState'
                persistent = Get-BallsPropertyText -Value $_ -Name 'Persistent'
            }
        })
    })
}

function Get-BallsServiceConfigurationFingerprint {
    $services = @(Get-CimInstance -ClassName Win32_Service -ErrorAction Stop | Sort-Object Name)
    Assert-BallsBoundedCount -Values $services -Maximum $script:MaximumUnrelatedServices
    return Get-BallsFingerprintHash -Value @($services | ForEach-Object {
        [ordered]@{
            name = Get-BallsPropertyText -Value $_ -Name 'Name'
            state = Get-BallsPropertyText -Value $_ -Name 'State'
            startMode = Get-BallsPropertyText -Value $_ -Name 'StartMode'
            startName = Get-BallsPropertyText -Value $_ -Name 'StartName'
            pathName = Get-BallsPropertyText -Value $_ -Name 'PathName'
            serviceType = Get-BallsPropertyText -Value $_ -Name 'ServiceType'
            errorControl = Get-BallsPropertyText -Value $_ -Name 'ErrorControl'
            delayedAutoStart = Get-BallsPropertyText -Value $_ -Name 'DelayedAutoStart'
        }
    })
}

function Get-BallsPolicyConfigurationFingerprint {
    $firewallProfiles = @(Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop | Sort-Object Name | ForEach-Object {
        [ordered]@{
            name = Get-BallsPropertyText -Value $_ -Name 'Name'
            enabled = Get-BallsPropertyText -Value $_ -Name 'Enabled'
            defaultInboundAction = Get-BallsPropertyText -Value $_ -Name 'DefaultInboundAction'
            defaultOutboundAction = Get-BallsPropertyText -Value $_ -Name 'DefaultOutboundAction'
            allowInboundRules = Get-BallsPropertyText -Value $_ -Name 'AllowInboundRules'
            allowLocalFirewallRules = Get-BallsPropertyText -Value $_ -Name 'AllowLocalFirewallRules'
            allowLocalIPsecRules = Get-BallsPropertyText -Value $_ -Name 'AllowLocalIPsecRules'
            notifyOnListen = Get-BallsPropertyText -Value $_ -Name 'NotifyOnListen'
        }
    })
    $executionPolicy = @(Get-ExecutionPolicy -List -ErrorAction Stop | Sort-Object Scope | ForEach-Object {
        [ordered]@{ scope = [string]$_.Scope; policy = [string]$_.ExecutionPolicy }
    })
    $registryShape = [Collections.Generic.List[object]]::new()
    foreach ($key in @(
        'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System',
        'HKLM\SOFTWARE\Policies\Microsoft\Windows\SrpV2',
        'HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy',
        'HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy')) {
        $result = Invoke-BallsBoundedProcess `
            -FilePath "$env:SystemRoot\System32\reg.exe" `
            -Arguments "query $key /s" `
            -WorkingDirectory "$env:SystemRoot\System32" `
            -TimeoutMilliseconds 15000 `
            -TimeoutCode 'policy_inventory_timeout'
        $registryShape.Add([ordered]@{
            keySha256 = Get-BallsSha256Text -Value $key
            exitCode = $result.exitCode
            standardOutputSha256 = Get-BallsSha256Text -Value ([string]$result.standardOutput)
            standardErrorSha256 = Get-BallsSha256Text -Value ([string]$result.standardError)
        })
    }
    return Get-BallsFingerprintHash -Value ([ordered]@{
        executionPolicy = $executionPolicy
        applicationControl = Get-BallsApplicationControlState
        firewallProfiles = $firewallProfiles
        registry = @($registryShape)
    })
}

function Get-BallsUnrelatedFingerprint {
    param(
        [Parameter(Mandatory = $true)][string] $DisposablePath,
        [Parameter(Mandatory = $true)][string] $ShareName,
        [Parameter(Mandatory = $true)][string] $FirewallRuleName)
    $components = [ordered]@{
        rootInventorySha256 = Get-BallsConformanceRootInventory -DisposablePath $DisposablePath
        shareConfigurationSha256 = Get-BallsShareConfigurationFingerprint -OwnedShareName $ShareName
        firewallConfigurationSha256 = Get-BallsFirewallConfigurationFingerprint -OwnedFirewallRuleName $FirewallRuleName
        accountConfigurationSha256 = Get-BallsAccountConfigurationFingerprint
        secureStoreInventorySha256 = Get-BallsSecureStoreInventoryFingerprint
        mappingConfigurationSha256 = Get-BallsMappingConfigurationFingerprint
        serviceConfigurationSha256 = Get-BallsServiceConfigurationFingerprint
        policyConfigurationSha256 = Get-BallsPolicyConfigurationFingerprint
    }
    $components['combinedSha256'] = Get-BallsFingerprintHash -Value $components
    return $components
}

function Get-BallsNativeObservation {
    param(
        [Parameter(Mandatory = $true)][string] $State,
        [Parameter(Mandatory = $true)][string] $DisposablePath,
        [Parameter(Mandatory = $true)][string] $ExpectedVolumeSha256,
        [Parameter(Mandatory = $true)][string] $ExpectedDiskSha256,
        [Parameter(Mandatory = $true)][string] $ExpectedOwnerSha256,
        [Parameter(Mandatory = $true)][string] $CircleId,
        [Parameter(Mandatory = $true)][string] $ContributionId,
        [Parameter(Mandatory = $true)][string] $PlanId,
        [Parameter(Mandatory = $true)][string] $ShareName,
        [Parameter(Mandatory = $true)][string] $FirewallRuleName,
        [Parameter(Mandatory = $true)][string] $OwnershipId,
        [Parameter(Mandatory = $true)][string] $ExpectedSeedHash,
        [Parameter(Mandatory = $true)][long] $ExpectedSeedLength)
    [void](Test-BallsPathSafe `
        -Path $DisposablePath `
        -ExpectedVolumeSha256 $ExpectedVolumeSha256 `
        -ExpectedDiskSha256 $ExpectedDiskSha256)
    $folder = Get-Item -LiteralPath $DisposablePath -Force -ErrorAction Stop
    $reparse = ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    $seed = Get-BallsSeedObservation -DisposablePath $DisposablePath
    if ($seed.sha256 -ne $ExpectedSeedHash -or $seed.length -ne $ExpectedSeedLength) { throw 'seed_bytes_changed' }
    $folderInventory = Get-BallsContributedFolderInventory `
        -State $State `
        -DisposablePath $DisposablePath
    $acl = Get-Acl -LiteralPath $DisposablePath -ErrorAction Stop
    $aclSddl = $acl.Sddl
    $ownerSid = ConvertTo-BallsSidValue -Identity ([string]$acl.Owner)
    $ownerSidHash = Get-BallsSha256Text -Value $ownerSid
    $accessRules = @($acl.Access)
    $applicableRules = @($accessRules | Where-Object {
        ($_.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -eq 0
    })
    $denyRules = @($applicableRules | Where-Object {
        $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Deny
    })
    [int]$ownerFullCount = 0
    [int]$systemFullCount = 0
    [int]$otherApplicableCount = 0
    $requiredInheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit `
        -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach ($rule in $applicableRules) {
        $sid = $null
        try { $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { $otherApplicableCount++; continue }
        $full = ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) `
            -eq [Security.AccessControl.FileSystemRights]::FullControl
        $allow = $rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow
        $inheritable = -not $rule.IsInherited `
            -and $rule.InheritanceFlags -eq $requiredInheritance `
            -and $rule.PropagationFlags -eq [Security.AccessControl.PropagationFlags]::None
        if ($allow -and $full -and $inheritable `
                -and (Get-BallsSha256Text -Value $sid) -eq $ExpectedOwnerSha256) {
            $ownerFullCount++
        }
        elseif ($allow -and $full -and $inheritable -and $sid -eq 'S-1-5-18') {
            $systemFullCount++
        }
        else { $otherApplicableCount++ }
    }
    $aclShapeExact = $acl.AreAccessRulesProtected `
        -and $accessRules.Count -eq 2 `
        -and $applicableRules.Count -eq 2 `
        -and $denyRules.Count -eq 0 `
        -and $ownerFullCount -eq 1 `
        -and $systemFullCount -eq 1 `
        -and $otherApplicableCount -eq 0
    $ownerFull = $aclShapeExact
    $systemFull = $aclShapeExact
    $markerPath = Join-Path $DisposablePath '.balls-owned-v1.json'
    $journalPath = Join-Path $DisposablePath '.balls-operation-v1.json'
    $recoveryPath = Join-Path $DisposablePath '.balls-firewall-recovery-v1.json'
    $markerExists = Test-Path -LiteralPath $markerPath -PathType Leaf
    $journalExists = Test-Path -LiteralPath $journalPath -PathType Leaf
    $markerMatches = $false
    $journalMatches = $false
    if ($markerExists) {
        try {
            $marker = Get-Content -LiteralPath $markerPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $markerMatches = $marker.contractVersion -eq 1 `
                -and $marker.circleId -eq $CircleId `
                -and $marker.contributionId -eq $ContributionId `
                -and $marker.folderPath -ieq $DisposablePath `
                -and $marker.ownershipId -eq $OwnershipId `
                -and (Get-BallsSha256Text -Value ([string]$marker.ownerSid)) -eq $ExpectedOwnerSha256
        }
        catch { $markerMatches = $false }
    }
    if ($journalExists) {
        try {
            $journal = Get-Content -LiteralPath $journalPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $journalMatches = $journal.contractVersion -eq 1 `
                -and $journal.ownershipId -eq $OwnershipId `
                -and $journal.planId -eq $PlanId `
                -and $journal.folderPath -ieq $DisposablePath `
                -and $journal.targetExisted `
                -and @($journal.createdDirectories).Count -eq 0 `
                -and (Get-BallsSha256Text -Value ([string]$journal.ownerSid)) -eq $ExpectedOwnerSha256
        }
        catch { $journalMatches = $false }
    }
    $shares = @(Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue)
    $sharePath = $shares.Count -eq 1 -and [string]$shares[0].Path -ieq $DisposablePath
    $shareEncryption = $shares.Count -eq 1 -and [bool]$shares[0].EncryptData
    $shareAccess = @()
    if ($shares.Count -eq 1) { $shareAccess = @(Get-SmbShareAccess -Name $ShareName -ErrorAction Stop) }
    $restricted = $false
    if ($shareAccess.Count -eq 1) {
        try { $accessSid = ConvertTo-BallsSidValue -Identity ([string]$shareAccess[0].AccountName) }
        catch { $accessSid = '' }
        $restricted = (Get-BallsSha256Text -Value $accessSid) -eq $ExpectedOwnerSha256 `
            -and [string]$shareAccess[0].AccessControlType -eq 'Allow' `
            -and [string]$shareAccess[0].AccessRight -eq 'Full'
    }
    $rules = @(Get-NetFirewallRule -Name $FirewallRuleName -ErrorAction SilentlyContinue)
    $privateOnly = $false
    $localSubnet = $false
    $tcp445 = $false
    $lanman = $false
    if ($rules.Count -eq 1) {
        $rule = $rules[0]
        $port = $rule | Get-NetFirewallPortFilter -ErrorAction Stop
        $address = $rule | Get-NetFirewallAddressFilter -ErrorAction Stop
        $service = $rule | Get-NetFirewallServiceFilter -ErrorAction Stop
        $privateOnly = [string]$rule.Enabled -eq 'True' -and [string]$rule.Profile -eq 'Private' -and [string]$rule.Direction -eq 'Inbound' -and [string]$rule.Action -eq 'Allow'
        $localSubnet = [string]$address.RemoteAddress -eq 'LocalSubnet'
        $tcp445 = [string]$port.Protocol -in @('TCP','6') -and [string]$port.LocalPort -eq '445'
        $lanman = [string]$service.Service -eq 'LanmanServer'
    }
    return [ordered]@{
        state = $State
        pathIdentitySha256 = Get-BallsSha256Text -Value $DisposablePath.ToUpperInvariant()
        folderExists = $true
        folderReparsePoint = $reparse
        folderInventorySha256 = $folderInventory.sha256
        folderInventoryCount = $folderInventory.count
        folderInventoryExact = $folderInventory.exact
        seed = $seed
        aclProtected = [bool]$acl.AreAccessRulesProtected
        aclSha256 = Get-BallsSha256Text -Value $aclSddl
        ownerSidSha256 = $ownerSidHash
        ownerFullControl = $ownerFull
        systemFullControl = $systemFull
        aclAccessRuleCount = $accessRules.Count
        aclApplicableRuleCount = $applicableRules.Count
        aclDenyRuleCount = $denyRules.Count
        aclShapeExact = $aclShapeExact
        markerExists = $markerExists
        markerMatches = $markerMatches
        journalExists = $journalExists
        journalMatches = $journalMatches
        firewallRecoveryExists = Test-Path -LiteralPath $recoveryPath -PathType Leaf
        shareCount = $shares.Count
        sharePathMatches = $sharePath
        shareEncryptionRequired = $shareEncryption
        shareAccessCount = $shareAccess.Count
        shareAccessRestrictedToOwner = $restricted
        firewallRuleCount = $rules.Count
        firewallPrivateOnly = $privateOnly
        firewallLocalSubnetOnly = $localSubnet
        firewallTcp445Only = $tcp445
        firewallLanmanServerOnly = $lanman
        unrelatedState = Get-BallsUnrelatedFingerprint `
            -DisposablePath $DisposablePath `
            -ShareName $ShareName `
            -FirewallRuleName $FirewallRuleName
    }
}

function Invoke-BallsHostRemoval {
    param(
        [Parameter(Mandatory = $true)][object] $Paths,
        [Parameter(Mandatory = $true)][object] $Context,
        [Parameter(Mandatory = $true)][string] $DisposablePath,
        [Parameter(Mandatory = $true)][object] $Policy)
    $script:Stage = $Policy.previewStage
    $preview = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli `
        -Paths $Paths `
        -PipeName ([string]$Context.pipeName) `
        -Arguments "files host remove-preview --circle $($Context.circleId) --contribution $($Context.contributionId) --path `"$DisposablePath`"" `
        -TimeoutCode $Policy.previewTimeoutCode) -FailureCode $Policy.previewFailureCode
    $script:Stage = $Policy.applyStage
    $result = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli `
        -Paths $Paths `
        -PipeName ([string]$Context.pipeName) `
        -Arguments "files host remove-apply --circle $($Context.circleId) --contribution $($Context.contributionId) --path `"$DisposablePath`" --plan $($preview.planId)" `
        -TimeoutCode $Policy.applyTimeoutCode) -FailureCode $Policy.applyFailureCode
    if ([string]$result.status -notin @('removed', 'already-removed')) {
        throw ([string]$Policy.incompleteCode)
    }
    return $result
}

function Remove-BallsProductArtifacts {
    param(
        [Parameter(Mandatory = $true)][object] $Paths,
        [Parameter(Mandatory = $true)][string] $StagedPackageName,
        [Parameter(Mandatory = $true)][bool] $ProductResourcesRemoved)
    Stop-BallsDaemon -Paths $Paths
    $daemonStopped = @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        [string]$_.Name -ieq 'ballsd.exe' -and [string]$_.CommandLine -like "*$($Paths.root)*"
    }).Count -eq 0
    if ($ProductResourcesRemoved -and $daemonStopped) {
        Remove-Item -LiteralPath $Paths.root -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path ([Environment]::CurrentDirectory) $StagedPackageName) -Force -ErrorAction SilentlyContinue
    }
    return [ordered]@{
        daemonStopped = $daemonStopped
        stateRemoved = $ProductResourcesRemoved -and $daemonStopped -and -not (Test-Path -LiteralPath $Paths.root)
        packageRemoved = $ProductResourcesRemoved -and $daemonStopped -and -not (Test-Path -LiteralPath (Join-Path ([Environment]::CurrentDirectory) $StagedPackageName))
        complete = $ProductResourcesRemoved -and $daemonStopped `
            -and -not (Test-Path -LiteralPath $Paths.root) `
            -and -not (Test-Path -LiteralPath (Join-Path ([Environment]::CurrentDirectory) $StagedPackageName))
    }
}

try {
    $mode = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_MODE' -Pattern '^(storage-inspect|preflight|product-preflight|prepare|inject-failure|apply|native|remove|cleanup)$'
    $disposablePath = ConvertFrom-BallsBase64Url (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_DISPOSABLE_PATH_B64' -Pattern '^[A-Za-z0-9_-]{16,256}$')
    if ($mode -eq 'storage-inspect') {
        $script:Operation = $script:StorageInspectionOperation
        $script:Stage = 'storage-inspect'
        Write-BallsResult -Value (Get-BallsStorageInspection -DisposablePath $disposablePath) -ExitCode 0
    }
    $expectedVolumeSha256 = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_EXPECTED_VOLUME_SHA256' -Pattern '^[0-9a-f]{64}$'
    $expectedDiskSha256 = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_EXPECTED_DISK_SHA256' -Pattern '^[0-9a-f]{64}$'
    if ($mode -in @('preflight','product-preflight')) {
        $script:Stage = $mode
        $preflight = Get-BallsPreflight `
            -DisposablePath $disposablePath `
            -ExpectedVolumeSha256 $expectedVolumeSha256 `
            -ExpectedDiskSha256 $expectedDiskSha256 `
            -ProductOnly:($mode -eq 'product-preflight')
        Write-BallsResult -Value $preflight -ExitCode $(if ($preflight.outcome -eq 'ready') { 0 } else { 1 })
    }

    if ($mode -eq 'native') {
        $script:Stage = 'native'
        $observation = Get-BallsNativeObservation `
            -State (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_NATIVE_STATE' -Pattern '^(prepared|rolled-back|provisioned|final)$') `
            -DisposablePath $disposablePath `
            -ExpectedVolumeSha256 $expectedVolumeSha256 `
            -ExpectedDiskSha256 $expectedDiskSha256 `
            -ExpectedOwnerSha256 (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_EXPECTED_PRODUCT_SID_SHA256' -Pattern '^[0-9a-f]{64}$') `
            -CircleId (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_CIRCLE_ID' -Pattern '^[0-9a-f-]{36}$') `
            -ContributionId (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_CONTRIBUTION_ID' -Pattern '^[0-9a-f-]{36}$') `
            -PlanId (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_PLAN_ID' -Pattern '^[0-9a-f]{64}$') `
            -ShareName (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_SHARE_NAME' -Pattern '^balls-[0-9a-f]{12}$') `
            -FirewallRuleName (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_FIREWALL_RULE_NAME' -Pattern '^Balls-SMB-[0-9a-f]{32}$') `
            -OwnershipId (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_OWNERSHIP_ID' -Pattern '^[0-9a-f]{64}$') `
            -ExpectedSeedHash (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_SEED_SHA256' -Pattern '^[0-9a-f]{64}$') `
            -ExpectedSeedLength ([long](Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_SEED_LENGTH' -Pattern '^[1-9][0-9]{0,6}$'))
        Write-BallsResult -Value ([ordered]@{
            schema = 'balls-windows-host-native-v1'; operation = $script:Operation; outcome = 'observed'; observation = $observation
        }) -ExitCode 0
    }

    $runId = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_RUN_ID' -Pattern '^[0-9a-f]{32}$'
    $stagedPackageName = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_STAGED_PACKAGE_NAME' -Pattern '^balls-host-conformance-[0-9a-f]{32}\.zip$'
    $expectedHash = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_PACKAGE_SHA256' -Pattern '^[0-9A-Fa-f]{64}$'
    $expectedCommit = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_COMMIT' -Pattern '^[0-9a-f]{40}$'
    $paths = Get-BallsPaths -RunId $runId

    if ($mode -eq 'prepare') {
        $script:Stage = 'prepare-preflight'
        $expectedComputerName = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_EXPECTED_COMPUTER_NAME' -Pattern '^[A-Za-z0-9-]{1,63}$'
        $expectedSidHash = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_EXPECTED_PRODUCT_SID_SHA256' -Pattern '^[0-9a-f]{64}$'
        $preflight = Get-BallsPreflight `
            -DisposablePath $disposablePath `
            -ExpectedVolumeSha256 $expectedVolumeSha256 `
            -ExpectedDiskSha256 $expectedDiskSha256 `
            -IgnoredPackageName $stagedPackageName `
            -IgnoredRunId $runId `
            -ProductOnly
        if ($preflight.outcome -ne 'ready' -or $preflight.computerName -ine $expectedComputerName -or $preflight.account.identitySha256 -ne $expectedSidHash) {
            throw 'target_precondition_mismatch'
        }
        $script:Stage = 'package'
        $manifest = Expand-BallsPackage -Paths $paths -StagedPackageName $stagedPackageName -ExpectedHash $expectedHash -ExpectedCommit $expectedCommit
        $cli = Join-Path $paths.extract 'balls\balls.exe'
        $daemon = Join-Path $paths.extract 'ballsd\ballsd.exe'
        $cliVersion = (Invoke-BallsBoundedProcess -FilePath $cli -Arguments '--version' -WorkingDirectory $paths.extract -TimeoutMilliseconds 10000 -TimeoutCode 'package_probe_timeout').standardOutput.Trim()
        $daemonVersion = (Invoke-BallsBoundedProcess -FilePath $daemon -Arguments '--version' -WorkingDirectory $paths.extract -TimeoutMilliseconds 10000 -TimeoutCode 'package_probe_timeout').standardOutput.Trim()
        if ([string]::IsNullOrWhiteSpace($cliVersion) -or [string]::IsNullOrWhiteSpace($daemonVersion)) { throw 'package_identity_mismatch' }
        $pipeName = "balls-host-$runId"
        $script:Stage = 'private-material-preflight'
        Test-BallsCurrentUserDpapi
        $script:Stage = 'seed-setup'
        $parent = Split-Path -Parent $disposablePath
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -ErrorAction Stop | Out-Null }
        [void](Test-BallsPathSafe `
            -Path $disposablePath `
            -ExpectedVolumeSha256 $expectedVolumeSha256 `
            -ExpectedDiskSha256 $expectedDiskSha256 `
            -RequireMissing)
        New-Item -ItemType Directory -Path $disposablePath -ErrorAction Stop | Out-Null
        $seedBytes = [Text.Encoding]::UTF8.GetBytes("Balls issue 124 seed bytes`r`n")
        [IO.File]::WriteAllBytes((Join-Path $disposablePath 'before-balls.txt'), $seedBytes)
        $seed = Get-BallsSeedObservation -DisposablePath $disposablePath
        $seed | ConvertTo-Json -Compress | Set-Content -LiteralPath $paths.seed -Encoding UTF8
        $script:Stage = 'daemon-start'
        Start-BallsDaemon -Paths $paths -PipeName $pipeName
        $script:Stage = 'circle-create'
        $circleRequest = [Guid]::NewGuid().ToString('D')
        $circle = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName $pipeName -Arguments "circle create Balls-Host-$runId --owner Conformance-Owner --request-id $circleRequest" -TimeoutCode 'circle_create_timeout') -FailureCode 'circle_create_failed'
        $circleId = [string]$circle.circle.id
        $script:Stage = 'contribution-create'
        $contributionRequest = [Guid]::NewGuid().ToString('D')
        $contribution = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName $pipeName -Arguments "files contribution create --circle $circleId --name Conformance-Files --request-id $contributionRequest" -TimeoutCode 'contribution_create_timeout') -FailureCode 'contribution_create_failed'
        $contributionId = [string]$contribution.id
        $script:Stage = 'host-preview'
        $preview = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName $pipeName -Arguments "files host preview --circle $circleId --contribution $contributionId --path `"$disposablePath`"" -TimeoutCode 'host_preview_timeout') -FailureCode 'host_preview_failed'
        $context = [ordered]@{
            pipeName = $pipeName
            circleId = $circleId
            contributionId = $contributionId
            folderPath = $disposablePath
            planId = [string]$preview.planId
            shareName = [string]$preview.shareName
            firewallRuleName = [string]$preview.firewallRuleName
            ownershipId = [string]$preview.ownershipId
            seed = $seed
        }
        $context | ConvertTo-Json -Compress -Depth 8 | Set-Content -LiteralPath $paths.context -Encoding UTF8
        Write-BallsResult -Value ([ordered]@{
            schema = 'balls-windows-host-prepare-v1'
            operation = $script:Operation
            outcome = 'prepared'
            preflight = $preflight
            product = [ordered]@{
                commit = $expectedCommit
                packageSha256 = $expectedHash.ToUpperInvariant()
                packageName = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_PACKAGE_NAME' -Pattern '^[A-Za-z0-9._-]{1,160}$'
                version = [string]$manifest.version
                cliVersion = $cliVersion
                daemonVersion = $daemonVersion
                daemonPrivilege = 'administrative'
                buildConfiguration = 'debug-conformance'
            }
            context = [ordered]@{
                circleId = $circleId; contributionId = $contributionId; folderPath = $disposablePath; planId = [string]$preview.planId
                shareName = [string]$preview.shareName; firewallRuleName = [string]$preview.firewallRuleName; ownershipId = [string]$preview.ownershipId
            }
            seed = $seed
        }) -ExitCode 0
    }

    if ($mode -eq 'cleanup' -and -not (Test-Path -LiteralPath $paths.context -PathType Leaf)) {
        $cleanup = Remove-BallsProductArtifacts -Paths $paths -StagedPackageName $stagedPackageName -ProductResourcesRemoved $true
        Write-BallsResult -Value ([ordered]@{
            schema = 'balls-windows-host-cleanup-v1'; operation = $script:Operation; outcome = $(if ($cleanup.complete) { 'clean' } else { 'failed' })
            productRemovalAttempted = $false; productResourcesRemoved = $true; cleanup = $cleanup; code = $(if ($cleanup.complete) { 'clean' } else { 'cleanup_incomplete' })
        }) -ExitCode $(if ($cleanup.complete) { 0 } else { 1 })
    }

    $context = Get-BallsContext -Paths $paths
    if ($context.folderPath -ine $disposablePath) { throw 'context_path_mismatch' }

    if ($mode -eq 'inject-failure') {
        $script:Stage = 'plan-mismatch'
        Stop-BallsDaemon -Paths $paths
        Start-BallsDaemon -Paths $paths -PipeName ([string]$context.pipeName) -FailureStep 'EncryptedShare'
        $wrongPlan = '0' * 64
        if ($wrongPlan -eq [string]$context.planId) { $wrongPlan = '1' * 64 }
        $badPlanResult = Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host apply --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`" --plan $wrongPlan" -TimeoutCode 'plan_mismatch_timeout'
        $planMismatch = Get-BallsCliErrorCode -ProcessResult $badPlanResult
        if ($planMismatch -ne 'hosting_plan_changed') { throw 'plan_mismatch_not_refused' }
        $script:Stage = 'fault-injection'
        $failureResult = Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host apply --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`" --plan $($context.planId)" -TimeoutCode 'injected_failure_timeout'
        $injected = Get-BallsCliErrorCode -ProcessResult $failureResult
        Stop-BallsDaemon -Paths $paths
        if ($injected -ne 'hosting_apply_failed') { throw 'fault_injection_not_observed' }
        Write-BallsResult -Value ([ordered]@{
            schema = 'balls-windows-host-refusal-v1'; operation = $script:Operation; outcome = 'rolled-back'
            planMismatchCode = $planMismatch; injectedFailureCode = $injected
        }) -ExitCode 0
    }

    if ($mode -eq 'apply') {
        $script:Stage = 'apply-start'
        Start-BallsDaemon -Paths $paths -PipeName ([string]$context.pipeName)
        $preview = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host preview --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`"" -TimeoutCode 'host_preview_timeout') -FailureCode 'host_preview_failed'
        if ([string]$preview.planId -ne [string]$context.planId) { throw 'hosting_plan_changed' }
        $first = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host apply --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`" --plan $($context.planId)" -TimeoutCode 'host_apply_timeout') -FailureCode 'host_apply_failed'
        $retry = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host apply --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`" --plan $($context.planId)" -TimeoutCode 'host_retry_timeout') -FailureCode 'host_retry_failed'
        Write-BallsResult -Value ([ordered]@{
            schema = 'balls-windows-host-apply-v1'; operation = $script:Operation; outcome = 'provisioned'
            applyStatus = [string]$first.status; retryStatus = [string]$retry.status; planId = [string]$first.plan.planId
        }) -ExitCode 0
    }

    if ($mode -eq 'remove') {
        $result = Invoke-BallsHostRemoval `
            -Paths $paths `
            -Context $context `
            -DisposablePath $disposablePath `
            -Policy $script:HostRemovalPolicies.remove
        $cleanup = Remove-BallsProductArtifacts -Paths $paths -StagedPackageName $stagedPackageName -ProductResourcesRemoved $true
        Write-BallsResult -Value ([ordered]@{
            schema = 'balls-windows-host-removal-v1'; operation = $script:Operation; outcome = 'removed'
            removalStatus = [string]$result.status; openSessionCount = [int]$result.openSessionCount; planId = [string]$result.plan.planId; cleanup = $cleanup
        }) -ExitCode $(if ($cleanup.complete) { 0 } else { 1 })
    }

    if ($mode -eq 'cleanup') {
        $script:Stage = 'emergency-cleanup'
        $attempted = $false
        $resourcesRemoved = $false
        try {
            $attempted = $true
            Start-BallsDaemon -Paths $paths -PipeName ([string]$context.pipeName)
            $removed = Invoke-BallsHostRemoval `
                -Paths $paths `
                -Context $context `
                -DisposablePath $disposablePath `
                -Policy $script:HostRemovalPolicies.cleanup
            $resourcesRemoved = [string]$removed.status -in @('removed', 'already-removed')
        }
        catch { $resourcesRemoved = $false }
        $cleanup = Remove-BallsProductArtifacts -Paths $paths -StagedPackageName $stagedPackageName -ProductResourcesRemoved $resourcesRemoved
        Write-BallsResult -Value ([ordered]@{
            schema = 'balls-windows-host-cleanup-v1'; operation = $script:Operation; outcome = $(if ($cleanup.complete) { 'clean' } else { 'failed' })
            productRemovalAttempted = $attempted; productResourcesRemoved = $resourcesRemoved; cleanup = $cleanup; code = $(if ($cleanup.complete) { 'clean' } else { 'cleanup_incomplete' })
        }) -ExitCode $(if ($cleanup.complete) { 0 } else { 1 })
    }
}
catch {
    $code = [string]$_.Exception.Message
    if ($code -notmatch '^[a-z0-9_]{1,80}$') { $code = "guest_operation_unhandled_$($script:Stage.Replace('-', '_'))" }
    Write-BallsResult -Value ([ordered]@{
        schema = 'balls-windows-host-failure-v1'; operation = $script:Operation; outcome = 'failed'; code = $code
    }) -ExitCode 1
}
