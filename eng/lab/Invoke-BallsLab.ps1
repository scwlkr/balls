[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Inspect', 'PrepareImage', 'Create', 'Checkpoint', 'Identity', 'Reset', 'Cleanup')]
    [string] $Action,

    [string] $LabRoot = 'C:\BallsLab',

    [string] $ImageUri,

    [string] $ImageSha256,

    [string] $QemuImgWslPath = '/usr/bin/qemu-img',

    [string] $QemuLibraryWslPath,

    [switch] $ConfirmReset,

    [switch] $ConfirmCleanup
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$vmName = 'Balls.Lab.Ubuntu'
$checkpointName = 'Balls.Lab.Clean'
$guestUser = 'balls-lab'
$minimumOsDiskSize = 16GB
$labRootPath = [IO.Path]::GetFullPath($LabRoot)
$labRootLeaf = Split-Path -Leaf $labRootPath
if ($labRootPath -eq [IO.Path]::GetPathRoot($labRootPath) -or
    -not $labRootLeaf.StartsWith('BallsLab', [StringComparison]::Ordinal)) {
    throw "LabRoot must be a dedicated directory whose name starts with BallsLab: $labRootPath"
}

$imageRoot = Join-Path $labRootPath 'Images'
$vmCollectionRoot = Join-Path $labRootPath 'VMs'
$vmRoot = Join-Path $vmCollectionRoot $vmName
$keyRoot = Join-Path $labRootPath 'Keys'
$privateKeyPath = Join-Path $keyRoot 'balls-lab-ed25519'
$publicKeyPath = "$privateKeyPath.pub"
$knownHostsPath = Join-Path $keyRoot 'known_hosts'
$statePath = Join-Path $labRootPath 'lab-state.json'
$osDiskPath = Join-Path $vmRoot "$vmName.vhdx"
$seedIsoPath = Join-Path $vmRoot "$vmName.seed.iso"

function Assert-HyperVAvailable {
    if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) {
        throw 'Hyper-V PowerShell is required.'
    }
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    $canManage = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or
        $principal.IsInRole('Hyper-V Administrators')
    if (-not $canManage) {
        throw 'The current account must be an Administrator or Hyper-V Administrators member.'
    }
}

function Assert-Wsl2Available {
    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
        throw 'WSL2 is required.'
    }
    $kernelRelease = (& wsl.exe --exec uname -r).Trim()
    if ($LASTEXITCODE -ne 0 -or $kernelRelease -notmatch 'microsoft.*WSL2') {
        throw 'The default WSL distribution must run on WSL2.'
    }
}

function Get-OwnedVm {
    $vm = Get-VM -Name $vmName -ErrorAction SilentlyContinue
    if ($null -eq $vm) {
        return $null
    }

    $configurationPath = [IO.Path]::GetFullPath($vm.ConfigurationLocation)
    $ownedPrefix = [IO.Path]::GetFullPath($vmRoot) + [IO.Path]::DirectorySeparatorChar
    if ($configurationPath -ne [IO.Path]::GetFullPath($vmRoot) -and
        -not $configurationPath.StartsWith($ownedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to adopt existing VM '$vmName' outside $vmRoot."
    }
    foreach ($disk in Get-VMHardDiskDrive -VM $vm) {
        $diskPath = [IO.Path]::GetFullPath($disk.Path)
        if (-not $diskPath.StartsWith($ownedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to adopt existing VM '$vmName' with disk outside $vmRoot."
        }
    }
    return $vm
}

function Write-LabState([string] $baseVhdPath, [string] $sourceHash) {
    $state = [ordered]@{
        schemaVersion = 1
        vmName = $vmName
        checkpointName = $checkpointName
        labRoot = $labRootPath
        baseVhdPath = $baseVhdPath
        sourceSha256 = $sourceHash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText(
        $statePath,
        ($state | ConvertTo-Json) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Read-LabState {
    if (-not (Test-Path -LiteralPath $statePath)) {
        throw "Lab state is missing. Run PrepareImage first: $statePath"
    }
    return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
}

function Prepare-Image {
    Assert-Wsl2Available
    if ([string]::IsNullOrWhiteSpace($ImageUri) -or
        [string]::IsNullOrWhiteSpace($ImageSha256)) {
        throw 'PrepareImage requires ImageUri and ImageSha256.'
    }
    $uri = [Uri]$ImageUri
    if ($uri.Scheme -ne 'https' -or
        $uri.Host -ne 'cloud-images.ubuntu.com' -or
        -not $uri.LocalPath.EndsWith('.img', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'ImageUri must be an HTTPS generic cloud image from cloud-images.ubuntu.com.'
    }
    if ($ImageSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'ImageSha256 must be one full SHA-256 digest.'
    }
    if ([string]::IsNullOrWhiteSpace($QemuImgWslPath) -or
        -not $QemuImgWslPath.StartsWith('/', [StringComparison]::Ordinal)) {
        throw 'QemuImgWslPath must be one absolute WSL path.'
    }
    if (-not [string]::IsNullOrWhiteSpace($QemuLibraryWslPath) -and
        -not $QemuLibraryWslPath.StartsWith('/', [StringComparison]::Ordinal)) {
        throw 'QemuLibraryWslPath must be one absolute WSL path.'
    }
    & wsl.exe --exec test -x $QemuImgWslPath
    if ($LASTEXITCODE -ne 0) {
        throw "qemu-img is not executable in WSL: $QemuImgWslPath"
    }

    New-Item -ItemType Directory -Force -Path $imageRoot | Out-Null
    $archivePath = Join-Path $imageRoot ([IO.Path]::GetFileName($uri.LocalPath))
    if (-not (Test-Path -LiteralPath $archivePath)) {
        $partialPath = "$archivePath.partial"
        Invoke-WebRequest -Uri $uri -OutFile $partialPath
        Move-Item -LiteralPath $partialPath -Destination $archivePath
    }
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $ImageSha256) {
        throw "Ubuntu source image checksum mismatch: $archivePath"
    }

    $baseName = [IO.Path]::GetFileNameWithoutExtension($archivePath)
    $baseVhdPath = Join-Path $imageRoot "$baseName.base.vhdx"
    if (-not (Test-Path -LiteralPath $baseVhdPath)) {
        $archiveWslPath = (& wsl.exe --exec wslpath -a -u $archivePath).Trim()
        $baseVhdWslPath = (& wsl.exe --exec wslpath -a -u $baseVhdPath).Trim()
        if ($LASTEXITCODE -ne 0 -or
            [string]::IsNullOrWhiteSpace($archiveWslPath) -or
            [string]::IsNullOrWhiteSpace($baseVhdWslPath)) {
            throw 'WSL2 could not map the dedicated lab image paths.'
        }
        $qemuArguments = @('--exec')
        if (-not [string]::IsNullOrWhiteSpace($QemuLibraryWslPath)) {
            $qemuArguments += @('env', "LD_LIBRARY_PATH=$QemuLibraryWslPath")
        }
        $qemuArguments += @(
            $QemuImgWslPath,
            'convert',
            '-f', 'qcow2',
            '-O', 'vhdx',
            '-o', 'subformat=dynamic',
            $archiveWslPath,
            $baseVhdWslPath)
        & wsl.exe @qemuArguments
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $baseVhdPath)) {
            throw 'qemu-img could not convert the verified Ubuntu image to VHDX.'
        }
    }
    $baseVhd = Get-VHD -Path $baseVhdPath
    if ($baseVhd.VhdFormat -ne 'VHDX' -or $baseVhd.VhdType -ne 'Dynamic') {
        throw "Converted Ubuntu image is not a dynamic VHDX: $baseVhdPath"
    }
    if ($baseVhd.Size -lt $minimumOsDiskSize) {
        Resize-VHD -Path $baseVhdPath -SizeBytes $minimumOsDiskSize
        $baseVhd = Get-VHD -Path $baseVhdPath
    }
    if ($baseVhd.Size -lt $minimumOsDiskSize) {
        throw "Prepared Ubuntu VHD is smaller than the required 16 GB: $baseVhdPath"
    }
    Write-LabState $baseVhdPath $actualHash
    Write-Output "Prepared verified Ubuntu image: $baseVhdPath"
}

function New-LabKey {
    New-Item -ItemType Directory -Force -Path $keyRoot | Out-Null
    if (-not (Test-Path -LiteralPath $privateKeyPath)) {
        & ssh-keygen.exe -q -t ed25519 -N '' -C $vmName -f $privateKeyPath
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not create the dedicated lab SSH key.'
        }
    }
    if (-not (Test-Path -LiteralPath $publicKeyPath)) {
        throw "Lab SSH public key is missing: $publicKeyPath"
    }
}

function Assert-UsableBaseVhd([string] $path) {
    $item = Get-Item -LiteralPath $path
    if (($item.Attributes -band [IO.FileAttributes]::SparseFile) -ne 0) {
        & fsutil.exe sparse setflag $path 0 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not remove the sparse-file flag required by Hyper-V: $path"
        }
        $item = Get-Item -LiteralPath $path
        if (($item.Attributes -band [IO.FileAttributes]::SparseFile) -ne 0) {
            throw "The Ubuntu VHD is still sparse and cannot be converted by Hyper-V: $path"
        }
    }
}

function New-SeedIso {
    if (Test-Path -LiteralPath $seedIsoPath) {
        return
    }
    $publicKey = (Get-Content -LiteralPath $publicKeyPath -Raw).Trim()
    $userData = @"
#cloud-config
hostname: balls-lab-ubuntu
manage_etc_hosts: true
ssh_pwauth: false
disable_root: true
users:
  - name: $guestUser
    gecos: Balls Lab
    groups: [adm, sudo]
    sudo: ALL=(ALL) NOPASSWD:ALL
    shell: /bin/bash
    lock_passwd: true
    ssh_authorized_keys:
      - $publicKey
write_files:
  - path: /etc/balls-lab
    permissions: '0444'
    content: |
      $vmName
runcmd:
  - systemctl enable --now ssh
  - touch /var/lib/balls-lab-ready
"@.Replace("`r`n", "`n")
    $metaData = @"
instance-id: balls-lab-ubuntu-1
local-hostname: balls-lab-ubuntu
"@.Replace("`r`n", "`n")
    $seedRoot = Join-Path $vmRoot 'Seed'
    New-Item -ItemType Directory -Force -Path $seedRoot | Out-Null
    [IO.File]::WriteAllText((Join-Path $seedRoot 'user-data'), $userData, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $seedRoot 'meta-data'), $metaData, [Text.UTF8Encoding]::new($false))
    Remove-Item -LiteralPath (Join-Path $seedRoot 'ovf-env.xml') -Force -ErrorAction SilentlyContinue

    if ($null -eq ('BallsLabComStreamCopy' -as [type])) {
        Add-Type -TypeDefinition @'
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class BallsLabComStreamCopy
{
    public static void ToFile(object source, string path)
    {
        var input = (IStream)source;
        using var output = File.Create(path);
        var buffer = new byte[1024 * 1024];
        var countPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            while (true)
            {
                input.Read(buffer, buffer.Length, countPointer);
                var count = Marshal.ReadInt32(countPointer);
                if (count == 0)
                {
                    break;
                }
                output.Write(buffer, 0, count);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(countPointer);
        }
    }
}
'@
    }

    $fileSystemImage = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
    $fileSystemImage.ChooseImageDefaultsForMediaType(12)
    $fileSystemImage.FileSystemsToCreate = 3
    $fileSystemImage.VolumeName = 'CIDATA'
    $fileSystemImage.Root.AddTree($seedRoot, $false)
    $result = $fileSystemImage.CreateResultImage()
    $partialPath = "$seedIsoPath.partial"
    [BallsLabComStreamCopy]::ToFile($result.ImageStream, $partialPath)
    Move-Item -LiteralPath $partialPath -Destination $seedIsoPath
}

function Get-GuestIp {
    $vm = Get-OwnedVm
    if ($null -eq $vm) {
        throw "Lab VM does not exist: $vmName"
    }
    $adapter = Get-VMNetworkAdapter -VM $vm
    $linkLayerAddress = $adapter.MacAddress -replace '(..)(?=.)', '$1-'
    for ($attempt = 0; $attempt -lt 180; $attempt++) {
        $address = $adapter |
            Select-Object -ExpandProperty IPAddresses |
            Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' -and -not $_.StartsWith('169.254.') } |
            Select-Object -First 1
        if ($null -eq $address) {
            $address = Get-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.LinkLayerAddress -eq $linkLayerAddress -and
                    -not $_.IPAddress.StartsWith('169.254.')
                } |
                Select-Object -First 1 -ExpandProperty IPAddress
        }
        if ($null -ne $address) {
            return $address
        }
        Start-Sleep -Seconds 2
    }
    throw 'The Ubuntu lab VM did not report an IPv4 address.'
}

function Invoke-Guest([string] $command) {
    $address = Get-GuestIp
    $arguments = @(
        '-i', $privateKeyPath,
        '-o', 'BatchMode=yes',
        '-o', 'StrictHostKeyChecking=accept-new',
        '-o', "UserKnownHostsFile=$knownHostsPath",
        '-o', 'ConnectTimeout=5',
        "$guestUser@$address",
        $command
    )
    $output = & ssh.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Ubuntu lab command failed: $command"
    }
    return $output
}

function Wait-Guest {
    for ($attempt = 0; $attempt -lt 900; $attempt++) {
        try {
            $result = Invoke-Guest 'test -f /var/lib/balls-lab-ready && test "$(cat /etc/balls-lab)" = "Balls.Lab.Ubuntu"' 2>$null
            return $result
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    throw 'The Ubuntu lab VM did not finish its clean cloud-init bootstrap within 30 minutes.'
}

function Assert-CleanIdentity {
    Invoke-Guest @'
test ! -e "$HOME/.local/state/balls" &&
test ! -e "/run/user/$(id -u)/balls/control.sock" &&
! pgrep -x ballsd >/dev/null
'@ | Out-Null
}

function Create-Lab {
    Assert-HyperVAvailable
    $existing = Get-OwnedVm
    if ($null -ne $existing) {
        if ($existing.Generation -ne 2) {
            throw "Refusing to adopt '$vmName' with unsupported VM generation $($existing.Generation)."
        }
        New-LabKey
        New-SeedIso
        $seedDrive = Get-VMDvdDrive -VM $existing |
            Where-Object { $_.Path -eq $seedIsoPath } |
            Select-Object -First 1
        if ($null -eq $seedDrive) {
            throw "Refusing to adopt '$vmName' without its owned CIDATA drive."
        }
        if ($existing.State -eq [Microsoft.HyperV.PowerShell.VMState]::Off) {
            Start-VM -VM $existing | Out-Null
        }
        Wait-Guest | Out-Null
        Assert-CleanIdentity
        Write-Output "Lab VM already exists and passes the clean identity boundary: $vmName"
        return
    }
    $state = Read-LabState
    $baseVhdPath = [IO.Path]::GetFullPath([string]$state.baseVhdPath)
    if (-not (Test-Path -LiteralPath $baseVhdPath)) {
        throw "Prepared Ubuntu VHD is missing: $baseVhdPath"
    }
    Assert-UsableBaseVhd $baseVhdPath
    if (-not (Get-VMSwitch -Name 'Default Switch' -ErrorAction SilentlyContinue)) {
        throw "Hyper-V Default Switch is required; the harness never creates or modifies shared switches."
    }

    New-Item -ItemType Directory -Force -Path $vmRoot | Out-Null
    New-LabKey
    if (-not (Test-Path -LiteralPath $osDiskPath)) {
        Convert-VHD -Path $baseVhdPath -DestinationPath $osDiskPath -VHDType Dynamic
    }
    New-SeedIso
    $vmParameters = @{
        Name = $vmName
        Generation = 2
        MemoryStartupBytes = 4GB
        VHDPath = $osDiskPath
        Path = $vmCollectionRoot
        SwitchName = 'Default Switch'
    }
    $vm = New-VM @vmParameters
    Set-VMFirmware -VM $vm -EnableSecureBoot Off
    Set-VMProcessor -VM $vm -Count 2
    Set-VMMemory -VM $vm -DynamicMemoryEnabled $false -StartupBytes 4GB
    Set-VM -VM $vm -AutomaticCheckpointsEnabled $false
    Add-VMDvdDrive -VM $vm -Path $seedIsoPath
    Start-VM -VM $vm | Out-Null
    Wait-Guest | Out-Null
    Assert-CleanIdentity
    Write-Output "Created clean Ubuntu lab VM: $vmName"
}

function Checkpoint-Lab {
    Assert-HyperVAvailable
    $vm = Get-OwnedVm
    if ($null -eq $vm) {
        throw "Lab VM does not exist: $vmName"
    }
    if (Get-VMSnapshot -VM $vm -Name $checkpointName -ErrorAction SilentlyContinue) {
        Write-Output "Clean checkpoint already exists: $checkpointName"
        return
    }
    Assert-CleanIdentity
    Checkpoint-VM -VM $vm -SnapshotName $checkpointName | Out-Null
    Write-Output "Created pre-enrollment checkpoint: $checkpointName"
}

function Show-Identity {
    $machineId = Invoke-Guest 'cat /etc/machine-id'
    $ballsState = Invoke-Guest 'if test -e "$HOME/.local/state/balls"; then echo enrolled; else echo clean; fi'
    Write-Output "VM: $vmName"
    Write-Output "Machine ID: $machineId"
    Write-Output "Balls identity state: $ballsState"
}

function Reset-Lab {
    if (-not $ConfirmReset) {
        throw 'Reset requires -ConfirmReset because it discards all post-checkpoint VM changes.'
    }
    Assert-HyperVAvailable
    $vm = Get-OwnedVm
    $checkpoint = if ($null -ne $vm) {
        Get-VMSnapshot -VM $vm -Name $checkpointName -ErrorAction SilentlyContinue
    }
    else {
        $null
    }
    if ($null -eq $vm -or $null -eq $checkpoint) {
        throw "Owned VM and checkpoint '$checkpointName' are required."
    }
    Stop-VM -VM $vm -TurnOff -Force -Confirm:$false
    Restore-VMSnapshot -VMSnapshot $checkpoint -Confirm:$false
    Clear-Content -LiteralPath $knownHostsPath -ErrorAction SilentlyContinue
    Start-VM -VM $vm | Out-Null
    Wait-Guest | Out-Null
    Assert-CleanIdentity
    Write-Output 'Reset restored the verified pre-enrollment identity boundary.'
}

function Cleanup-Lab {
    if (-not $ConfirmCleanup) {
        throw 'Cleanup requires -ConfirmCleanup because it permanently removes owned lab resources.'
    }
    Assert-HyperVAvailable
    $vm = Get-OwnedVm
    if ($null -ne $vm) {
        if ($vm.State -ne [Microsoft.HyperV.PowerShell.VMState]::Off) {
            Stop-VM -VM $vm -TurnOff -Force -Confirm:$false
        }
        Remove-VM -VM $vm -Force -Confirm:$false
    }
    if (Test-Path -LiteralPath $labRootPath) {
        $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $labRootPath).Path)
        if ($resolved -ne $labRootPath) {
            throw "Refusing to remove unexpected lab path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Write-Output "Removed only $vmName and $labRootPath."
}

switch ($Action) {
    'Inspect' {
        Assert-HyperVAvailable
        Assert-Wsl2Available
        $vm = Get-OwnedVm
        Write-Output "Hyper-V: available"
        Write-Output "WSL2 distributions:"
        & wsl.exe --list --verbose
        Write-Output "Owned VM: $($null -ne $vm)"
        Write-Output "Lab root: $labRootPath"
    }
    'PrepareImage' { Prepare-Image }
    'Create' { Create-Lab }
    'Checkpoint' { Checkpoint-Lab }
    'Identity' { Show-Identity }
    'Reset' { Reset-Lab }
    'Cleanup' { Cleanup-Lab }
}
