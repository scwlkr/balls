[CmdletBinding()]
param(
    [ValidateSet('Inspect', 'Configure', 'Finalize', 'Disable')]
    [string] $Action = 'Inspect',

    [string] $KeyComment,

    [switch] $ConfirmSystemChange
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$firewallRuleName = 'Balls-DevLink-SSH-Tailscale'
$tailnetRange = '100.64.0.0/10'
$sshdConfigPath = Join-Path $env:ProgramData 'ssh\sshd_config'
$authorizedKeysPath = Join-Path $env:ProgramData 'ssh\administrators_authorized_keys'

function Assert-Windows {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'This bootstrap must run on Windows.'
    }
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-SystemChangeConfirmed {
    if (-not $ConfirmSystemChange) {
        throw 'This action changes persistent remote-access settings. Re-run with -ConfirmSystemChange after reviewing docs/two-machine-development.md.'
    }

    if (-not (Test-Administrator)) {
        throw 'This action requires an elevated PowerShell session.'
    }
}

function Get-TailscaleCommand {
    $command = Get-Command 'tailscale.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $installedPath = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
    if (Test-Path -LiteralPath $installedPath -PathType Leaf) {
        return $installedPath
    }

    throw 'Tailscale is not installed. Install and sign in with the official Windows app first.'
}

function Get-TailscaleStatus {
    $tailscale = Get-TailscaleCommand
    $statusJson = & $tailscale status --json 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($statusJson -join ''))) {
        return [pscustomobject]@{ BackendState = 'Stopped'; TailscaleIPs = @() }
    }

    return (($statusJson -join "`n") | ConvertFrom-Json)
}

function Get-AgentPublicKey {
    if ([string]::IsNullOrWhiteSpace($KeyComment)) {
        throw 'Provide -KeyComment for exactly one public key exposed by the 1Password SSH agent.'
    }

    $keys = @(& ssh-add.exe -L 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw 'No SSH agent keys are available. Enable the 1Password SSH agent and expose the shared development key.'
    }

    $suffix = " $KeyComment"
    $matches = @($keys | Where-Object { $_.EndsWith($suffix, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one SSH agent key ending with '$KeyComment'; found $($matches.Count)."
    }

    if ($matches[0] -notmatch '^ssh-(ed25519|rsa) [A-Za-z0-9+/=]+ [^\r\n]{1,200}$') {
        throw 'The selected SSH public key has an unexpected format.'
    }

    return $matches[0]
}

function Install-OpenSshServer {
    $capability = Get-WindowsCapability -Online |
        Where-Object Name -Like 'OpenSSH.Server*' |
        Select-Object -First 1
    if ($null -eq $capability) {
        throw 'The Windows OpenSSH Server capability is unavailable.'
    }

    if ($capability.State -ne 'Installed') {
        Add-WindowsCapability -Online -Name $capability.Name | Out-Null
    }
}

function Set-AuthorizedKey {
    param([Parameter(Mandatory)][string] $PublicKey)

    $directory = Split-Path -Parent $authorizedKeysPath
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $keys = [Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $authorizedKeysPath -PathType Leaf) {
        $keys.AddRange([string[]](Get-Content -LiteralPath $authorizedKeysPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }))
    }
    if (-not $keys.Contains($PublicKey)) {
        $keys.Add($PublicKey)
    }
    [IO.File]::WriteAllLines($authorizedKeysPath, $keys, [Text.UTF8Encoding]::new($false))

    & icacls.exe $authorizedKeysPath '/inheritance:r' '/grant' '*S-1-5-32-544:F' '/grant' 'SYSTEM:F' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not restrict administrators_authorized_keys permissions.'
    }
}

function Set-SshdGlobalOption {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Value
    )

    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]](Get-Content -LiteralPath $sshdConfigPath))
    $matchIndex = $lines.Count
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\s*Match\s+') {
            $matchIndex = $index
            break
        }
    }

    $found = $false
    for ($index = 0; $index -lt $matchIndex; $index++) {
        if ($lines[$index] -match "^\s*#?\s*$([Regex]::Escape($Name))\s+") {
            $lines[$index] = "$Name $Value"
            $found = $true
            break
        }
    }

    if (-not $found) {
        $lines.Insert($matchIndex, "$Name $Value")
    }

    [IO.File]::WriteAllLines($sshdConfigPath, $lines, [Text.UTF8Encoding]::new($false))
}

function Set-TailnetFirewallRule {
    $defaultRule = Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue
    if ($null -ne $defaultRule) {
        $defaultRule | Disable-NetFirewallRule
    }

    $existing = Get-NetFirewallRule -Name $firewallRuleName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        $existing | Remove-NetFirewallRule
    }

    New-NetFirewallRule `
        -Name $firewallRuleName `
        -DisplayName 'Balls development SSH over Tailscale' `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort 22 `
        -Profile Any `
        -InterfaceAlias 'Tailscale' `
        -RemoteAddress $tailnetRange | Out-Null
}

function Write-Inspection {
    $tailscaleState = 'NotInstalled'
    $tailscaleIps = @()
    try {
        $status = Get-TailscaleStatus
        $tailscaleState = [string]$status.BackendState
        $tailscaleIps = @($status.TailscaleIPs)
    }
    catch {
        # Inspection reports absence without mutating the machine.
    }

    $service = Get-Service -Name 'sshd' -ErrorAction SilentlyContinue
    $firewall = Get-NetFirewallRule -Name $firewallRuleName -ErrorAction SilentlyContinue
    $passwordAuthentication = 'Unspecified'
    if (Test-Path -LiteralPath $sshdConfigPath -PathType Leaf) {
        $setting = Get-Content -LiteralPath $sshdConfigPath |
            Where-Object { $_ -match '^\s*PasswordAuthentication\s+' } |
            Select-Object -First 1
        if ($null -ne $setting) {
            $passwordAuthentication = ($setting -split '\s+')[1]
        }
    }

    [pscustomobject]@{
        ComputerName = $env:COMPUTERNAME
        IsAdministrator = (Test-Administrator)
        TailscaleState = $tailscaleState
        TailscaleIPs = $tailscaleIps
        SshdStatus = if ($null -eq $service) { 'NotInstalled' } else { [string]$service.Status }
        TailnetFirewallEnabled = $null -ne $firewall -and [string]$firewall.Enabled -eq 'True'
        AuthorizedKeyInstalled = (Test-Path -LiteralPath $authorizedKeysPath -PathType Leaf)
        PasswordAuthentication = $passwordAuthentication
    } | ConvertTo-Json -Depth 3
}

Assert-Windows

switch ($Action) {
    'Inspect' {
        Write-Inspection
    }
    'Configure' {
        Assert-SystemChangeConfirmed
        $tailscale = Get-TailscaleCommand
        & $tailscale up
        if ($LASTEXITCODE -ne 0) {
            throw 'Tailscale did not become active. Finish its browser sign-in, then retry.'
        }
        & $tailscale set --unattended=true
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not enable unattended Tailscale operation.'
        }

        $status = Get-TailscaleStatus
        if ($status.BackendState -ne 'Running' -or @($status.TailscaleIPs).Count -eq 0) {
            throw 'Tailscale is not connected to a tailnet.'
        }

        $publicKey = Get-AgentPublicKey
        Install-OpenSshServer
        Set-AuthorizedKey -PublicKey $publicKey
        Set-SshdGlobalOption -Name 'PubkeyAuthentication' -Value 'yes'
        Set-TailnetFirewallRule
        Set-Service -Name 'sshd' -StartupType Automatic
        Restart-Service -Name 'sshd'
        Write-Inspection
    }
    'Finalize' {
        Assert-SystemChangeConfirmed
        if (-not (Test-Path -LiteralPath $authorizedKeysPath -PathType Leaf)) {
            throw 'Refusing to disable password authentication before an authorized key is installed.'
        }

        Set-SshdGlobalOption -Name 'PubkeyAuthentication' -Value 'yes'
        Set-SshdGlobalOption -Name 'PasswordAuthentication' -Value 'no'
        Set-SshdGlobalOption -Name 'KbdInteractiveAuthentication' -Value 'no'
        Restart-Service -Name 'sshd'
        Write-Inspection
    }
    'Disable' {
        Assert-SystemChangeConfirmed
        $firewall = Get-NetFirewallRule -Name $firewallRuleName -ErrorAction SilentlyContinue
        if ($null -ne $firewall) {
            $firewall | Disable-NetFirewallRule
        }
        $service = Get-Service -Name 'sshd' -ErrorAction SilentlyContinue
        if ($null -ne $service) {
            Stop-Service -Name 'sshd' -Force
            Set-Service -Name 'sshd' -StartupType Disabled
        }
        Write-Inspection
    }
}
