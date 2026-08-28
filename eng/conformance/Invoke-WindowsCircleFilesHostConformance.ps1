$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

$script:Operation = 'windows-circle-files-host-v1'
$script:Stage = 'initializing'

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

function Get-BallsApplicationControlState {
    try {
        $value = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop).VerifiedAndReputablePolicyState
        if ($value -eq 1) { return 'enforced' }
        if ($value -eq 2) { return 'evaluation' }
        return 'off'
    }
    catch { return 'unknown' }
}

function Test-BallsPathSafe {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [switch] $RequireMissing)
    if ($Path -notmatch '^[C-Z]:\\BallsConformance\\Issue124-[A-Za-z0-9][A-Za-z0-9-]{2,39}$' `
            -or $Path -ne [IO.Path]::GetFullPath($Path).TrimEnd('\')) {
        throw 'disposable_path_invalid'
    }
    $root = [IO.Path]::GetPathRoot($Path)
    if ([IO.DriveInfo]::new($root).DriveType -ne [IO.DriveType]::Fixed) {
        throw 'disposable_path_not_fixed_local'
    }
    for ($item = [IO.DirectoryInfo]::new($Path); $null -ne $item; $item = $item.Parent) {
        if ($item.Exists -and (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'disposable_path_reparse'
        }
    }
    if ($RequireMissing -and (Test-Path -LiteralPath $Path)) { throw 'disposable_path_not_clean' }
}

function Get-BallsPreflight {
    param(
        [Parameter(Mandatory = $true)][string] $DisposablePath,
        [AllowNull()][string] $IgnoredPackageName = $null,
        [AllowNull()][string] $IgnoredRunId = $null,
        [switch] $ProductOnly)
    if ($env:OS -ne 'Windows_NT') { throw 'windows_required' }
    Test-BallsPathSafe -Path $DisposablePath -RequireMissing
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
            $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
            if ($null -ne $process) {
                try { Stop-Process -Id $pidValue -Force -ErrorAction Stop } catch {}
                try { [void]$process.WaitForExit(10000) } catch {}
            }
        }
        Remove-Item -LiteralPath $Paths.pid -Force -ErrorAction SilentlyContinue
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
        if ($process.HasExited) { throw 'daemon_exited_before_ready' }
        $status = Invoke-BallsBoundedProcess -FilePath $cli -Arguments "--output json --pipe-name $PipeName status" -WorkingDirectory $Paths.extract -TimeoutMilliseconds 15000 -TimeoutCode 'daemon_poll_timeout'
        if ($status.exitCode -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw 'daemon_readiness_timeout'
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

function Get-BallsUnrelatedFingerprint {
    param(
        [Parameter(Mandatory = $true)][string] $ShareName,
        [Parameter(Mandatory = $true)][string] $FirewallRuleName)
    $shares = @(Get-SmbShare -ErrorAction Stop | Where-Object Name -ne $ShareName | Sort-Object Name | ForEach-Object {
        [ordered]@{ name = [string]$_.Name; path = [string]$_.Path; encrypt = [bool]$_.EncryptData; description = [string]$_.Description }
    })
    $rules = @(Get-NetFirewallRule -PolicyStore ActiveStore -ErrorAction Stop | Where-Object Name -ne $FirewallRuleName | Sort-Object Name | ForEach-Object {
        [ordered]@{ name = [string]$_.Name; enabled = [string]$_.Enabled; profile = [string]$_.Profile; direction = [string]$_.Direction; action = [string]$_.Action }
    })
    $users = @(Get-LocalUser -ErrorAction Stop | Sort-Object SID | ForEach-Object {
        [ordered]@{ sid = [string]$_.SID; enabled = [bool]$_.Enabled }
    })
    $mappings = @(Get-SmbMapping -ErrorAction SilentlyContinue | Sort-Object LocalPath,RemotePath | ForEach-Object {
        [ordered]@{ local = [string]$_.LocalPath; remote = [string]$_.RemotePath; status = [string]$_.Status }
    })
    return Get-BallsSha256Text -Value (([ordered]@{ shares = $shares; rules = $rules; users = $users; mappings = $mappings } | ConvertTo-Json -Compress -Depth 8))
}

function Get-BallsNativeObservation {
    param(
        [Parameter(Mandatory = $true)][string] $State,
        [Parameter(Mandatory = $true)][string] $DisposablePath,
        [Parameter(Mandatory = $true)][string] $ExpectedOwnerSha256,
        [Parameter(Mandatory = $true)][string] $CircleId,
        [Parameter(Mandatory = $true)][string] $ContributionId,
        [Parameter(Mandatory = $true)][string] $PlanId,
        [Parameter(Mandatory = $true)][string] $ShareName,
        [Parameter(Mandatory = $true)][string] $FirewallRuleName,
        [Parameter(Mandatory = $true)][string] $OwnershipId,
        [Parameter(Mandatory = $true)][string] $ExpectedSeedHash,
        [Parameter(Mandatory = $true)][long] $ExpectedSeedLength)
    Test-BallsPathSafe -Path $DisposablePath
    $folder = Get-Item -LiteralPath $DisposablePath -Force -ErrorAction Stop
    $reparse = ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    $seed = Get-BallsSeedObservation -DisposablePath $DisposablePath
    if ($seed.sha256 -ne $ExpectedSeedHash -or $seed.length -ne $ExpectedSeedLength) { throw 'seed_bytes_changed' }
    $acl = Get-Acl -LiteralPath $DisposablePath -ErrorAction Stop
    $aclSddl = $acl.Sddl
    $ownerSid = ([Security.Principal.NTAccount]$acl.Owner).Translate([Security.Principal.SecurityIdentifier]).Value
    $ownerSidHash = Get-BallsSha256Text -Value $ownerSid
    $ownerFull = $false
    $systemFull = $false
    foreach ($rule in @($acl.Access)) {
        try { $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { continue }
        $full = ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl
        if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and $full) {
            if ((Get-BallsSha256Text -Value $sid) -eq $ExpectedOwnerSha256) { $ownerFull = $true }
            if ($sid -eq 'S-1-5-18') { $systemFull = $true }
        }
    }
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
        try { $accessSid = ([Security.Principal.NTAccount]$shareAccess[0].AccountName).Translate([Security.Principal.SecurityIdentifier]).Value }
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
        seed = $seed
        aclProtected = [bool]$acl.AreAccessRulesProtected
        aclSha256 = Get-BallsSha256Text -Value $aclSddl
        ownerSidSha256 = $ownerSidHash
        ownerFullControl = $ownerFull
        systemFullControl = $systemFull
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
        unrelatedInfrastructureSha256 = Get-BallsUnrelatedFingerprint -ShareName $ShareName -FirewallRuleName $FirewallRuleName
    }
}

function Remove-BallsProductArtifacts {
    param(
        [Parameter(Mandatory = $true)][object] $Paths,
        [Parameter(Mandatory = $true)][string] $StagedPackageName,
        [Parameter(Mandatory = $true)][bool] $ProductResourcesRemoved)
    Stop-BallsDaemon -Paths $Paths
    if ($ProductResourcesRemoved) {
        Remove-Item -LiteralPath $Paths.root -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path ([Environment]::CurrentDirectory) $StagedPackageName) -Force -ErrorAction SilentlyContinue
    }
    return [ordered]@{
        daemonStopped = -not (Test-Path -LiteralPath $Paths.pid)
        stateRemoved = $ProductResourcesRemoved -and -not (Test-Path -LiteralPath $Paths.root)
        packageRemoved = $ProductResourcesRemoved -and -not (Test-Path -LiteralPath (Join-Path ([Environment]::CurrentDirectory) $StagedPackageName))
        complete = $ProductResourcesRemoved `
            -and -not (Test-Path -LiteralPath $Paths.root) `
            -and -not (Test-Path -LiteralPath (Join-Path ([Environment]::CurrentDirectory) $StagedPackageName))
    }
}

try {
    $mode = Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_MODE' -Pattern '^(preflight|product-preflight|prepare|inject-failure|apply|native|remove|cleanup)$'
    $disposablePath = ConvertFrom-BallsBase64Url (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_DISPOSABLE_PATH_B64' -Pattern '^[A-Za-z0-9_-]{16,256}$')
    if ($mode -in @('preflight','product-preflight')) {
        $script:Stage = $mode
        $preflight = Get-BallsPreflight -DisposablePath $disposablePath -ProductOnly:($mode -eq 'product-preflight')
        Write-BallsResult -Value $preflight -ExitCode $(if ($preflight.outcome -eq 'ready') { 0 } else { 1 })
    }

    if ($mode -eq 'native') {
        $script:Stage = 'native'
        $observation = Get-BallsNativeObservation `
            -State (Get-BallsEnvironmentValue -Name 'BALLS_CONFORMANCE_NATIVE_STATE' -Pattern '^(prepared|rolled-back|provisioned|final)$') `
            -DisposablePath $disposablePath `
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
        $preflight = Get-BallsPreflight -DisposablePath $disposablePath -IgnoredPackageName $stagedPackageName -IgnoredRunId $runId -ProductOnly
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
        $script:Stage = 'seed-setup'
        $parent = Split-Path -Parent $disposablePath
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -ErrorAction Stop | Out-Null }
        Test-BallsPathSafe -Path $disposablePath -RequireMissing
        New-Item -ItemType Directory -Path $disposablePath -ErrorAction Stop | Out-Null
        $seedBytes = [Text.Encoding]::UTF8.GetBytes("Balls issue 124 seed bytes`r`n")
        [IO.File]::WriteAllBytes((Join-Path $disposablePath 'before-balls.txt'), $seedBytes)
        $seed = Get-BallsSeedObservation -DisposablePath $disposablePath
        $pipeName = "balls-host-$runId"
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
        $script:Stage = 'remove-preview'
        $preview = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host remove-preview --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`"" -TimeoutCode 'remove_preview_timeout') -FailureCode 'remove_preview_failed'
        $script:Stage = 'remove-apply'
        $result = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host remove-apply --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`" --plan $($preview.planId)" -TimeoutCode 'remove_apply_timeout') -FailureCode 'remove_apply_failed'
        if ([string]$result.status -notin @('removed','already-removed')) { throw 'remove_incomplete' }
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
            $preview = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host remove-preview --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`"" -TimeoutCode 'cleanup_preview_timeout') -FailureCode 'cleanup_preview_failed'
            $removed = ConvertFrom-BallsCliResult -ProcessResult (Invoke-BallsCli -Paths $paths -PipeName ([string]$context.pipeName) -Arguments "files host remove-apply --circle $($context.circleId) --contribution $($context.contributionId) --path `"$disposablePath`" --plan $($preview.planId)" -TimeoutCode 'cleanup_apply_timeout') -FailureCode 'cleanup_apply_failed'
            $resourcesRemoved = [string]$removed.status -in @('removed','already-removed')
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
