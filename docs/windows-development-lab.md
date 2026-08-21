# Windows development lab

Read this runbook before Windows VM automation, unsigned product execution, browser UI acceptance,
installer or Canary testing, or recovery of the dedicated Windows guest. The lab accelerates risky
Windows checks without weakening security controls on the physical host.

## Owned environment

The machine-local lab owns these resources:

- Hyper-V VM `Balls.Dev.Windows11`, a Generation 2 Windows 11 Enterprise Evaluation guest;
- VM storage below `C:\BallsLab\VMs\Balls.Dev.Windows11`;
- guest repository `C:\Dev\balls` with SSH remote `git@github.com:scwlkr/balls.git`;
- host automation below `C:\BallsLab\Setup` and guest evidence below `C:\BallsLab`;
- DPAPI-protected PowerShell Direct credential
  `C:\BallsLab\Secrets\Balls.Dev.Windows11.credential.xml`.

The guest has 4 virtual processors and dynamic memory from 4–10 GB, uses the existing Hyper-V
`Default Switch`, has TPM enabled, and has automatic checkpoints disabled. Run it serially with
other heavy VMs when host memory is constrained.

The 2026-08-21 checkpoint recorded .NET SDK 10.0.400, Git 2.55.0, PowerShell 7.6.5, GitHub CLI
2.98.0, Node 24.19.0, Chrome 151, global pnpm 11.22.0, and repository-pinned pnpm 11.19.0. Treat
versions as observed evidence, not permanent requirements; repository manifests remain
authoritative.

## Security boundary

- Smart App Control is off inside this disposable VM so locally built unsigned binaries can run.
  This setting does not authorize changing the physical host.
- Production code signing is intentionally out of scope. Local `balls.exe` and `ballsd.exe` are
  expected to report `NotSigned` in the source smoke.
- Import the encrypted credential only through the current host Windows account. Never print,
  export, copy, commit, or checkpoint credentials, GitHub tokens, SSH private keys, or signed-in
  application state.
- Never put the guest password or the contents of any password source in repository files,
  evidence, logs, issues, pull requests, prompts, or memory.
- Authentication and UAC remain protected boundaries. Use the VM console for interactive sign-in
  or consent when PowerShell Direct cannot perform the operation safely.

The owner-confirmed ChatGPT desktop sign-in is an interactive convenience, not automation
evidence. Do not automate or inspect its signed-in state.

## Start and prove automation

Run these commands from an elevated host PowerShell session:

```powershell
Get-VM -Name Balls.Dev.Windows11
Start-VM -Name Balls.Dev.Windows11
pwsh -NoProfile -File C:\BallsLab\Setup\Test-BallsDevPowerShellDirect.ps1
```

Success means the VM is `Running`, the identity is `DESKTOP-6MHSLNM\balls-dev`, password setup is
reported successful, no transient password files remain, and `C:\Windows` is accessible. Stop if
the encrypted credential is missing or PowerShell Direct fails; do not recreate secrets from
documentation.

Use the common runner for an existing host-side guest script:

```powershell
pwsh -NoProfile -File C:\BallsLab\Setup\Invoke-BallsDevGuestScript.ps1 `
  -ScriptPath C:\BallsLab\Setup\Test-BallsVmSourceBuild.Guest.ps1
```

The runner imports the DPAPI-protected credential, invokes only the named script through
PowerShell Direct, and releases the credential object afterward. Inspect machine-local scripts
before first use in a new session because they are outside repository version control.

## Synchronize source

The guest repository must be clean before setup or smoke scripts run. Inspect it, fetch, and use a
fast-forward-only update; preserve or stop for unexpected guest work:

```powershell
$ballsVmCredential = Import-Clixml -LiteralPath `
  C:\BallsLab\Secrets\Balls.Dev.Windows11.credential.xml
try {
  Invoke-Command -VMName Balls.Dev.Windows11 -Credential $ballsVmCredential -ScriptBlock {
    git -C C:\Dev\balls status --short --branch
    if (git -C C:\Dev\balls status --porcelain) {
      throw 'The guest repository has changes; preserve and investigate them before syncing.'
    }
    git -C C:\Dev\balls fetch origin
    git -C C:\Dev\balls switch main
    git -C C:\Dev\balls merge --ff-only origin/main
  }
}
finally {
  $ballsVmCredential = $null
  [GC]::Collect()
}
```

Run `Setup-BallsVmProject.Guest.ps1` after a clean clone or dependency-manifest change. It performs
the frozen pnpm install, installs project-local Playwright Chromium, launches Chromium, and fails
if repository content changes.

## Acceptance entry points

Invoke each applicable script through `Invoke-BallsDevGuestScript.ps1`:

| Outcome | Guest script | Completion evidence in the guest |
| --- | --- | --- |
| Restore, format, Release build, unsigned execution, Circle create/list, restart persistence | `Test-BallsVmSourceBuild.Guest.ps1` | `C:\BallsLab\source-smoke\latest-result.json` |
| Generated client, component tests, real Playwright Chromium journey | `Test-BallsVmBrowser.Guest.ps1` | `C:\BallsLab\browser-smoke\latest-result.json` |
| Launch the unsigned Balls browser UI for interactive review | `Launch-BallsVmUi.Guest.ps1` | `Get-BallsVmUiLaunchStatus.Guest.ps1` reports daemon and Chrome state |
| Historical `0.2.0-alpha.1` release download and installer proof | `Test-BallsVmReleaseInstaller.Guest.ps1` | `C:\BallsLab\release-installer\0.2.0-alpha.1\installer-result.json` |

For interactive UI review, sign in at the VM console first, invoke the launch script, open the
console with `vmconnect.exe localhost Balls.Dev.Windows11`, and verify the rendered behavior.
`Stage-BallsVmUiShortcut.Guest.ps1` replaces the temporary scheduled launch task with the public
desktop shortcut `Open Balls UI.lnk`.

The release-installer script is pinned historical evidence. Review and update its exact tag,
asset names, hashes, and expected outcome before using the pattern for another release; never
claim it tested a newer artifact unchanged.

## Evidence and recovery

Host-readable setup evidence lives at:

- `C:\BallsLab\VMs\Balls.Dev.Windows11\provisioning-result.json`;
- `C:\BallsLab\VMs\Balls.Dev.Windows11\toolchain-readiness.json`;
- `C:\BallsLab\VMs\Balls.Dev.Windows11\toolchain-result.json`.

On 2026-08-21, PowerShell Direct passed, Windows Enterprise Evaluation reported licensed,
Smart App Control reported off in the guest, and source, browser, and installer smokes passed from
clean repository commit `4aae91a02e27d4268b100e0f320babd79e004bb8`. Re-run the applicable gate
after source, toolchain, VM, or policy changes; the dated result is not evidence for later code.

Two explicit checkpoints exist:

- `Balls.Dev.Base`: Windows installed before the development toolchain;
- `Balls.Dev.Toolchain.Core`: core toolchain ready, before ChatGPT installation and authentication.

Restoring either checkpoint discards later guest state and authentication. Treat restoration as a
destructive recovery action: verify the exact VM and checkpoint, preserve needed guest work, and
obtain owner confirmation before applying it. Do not create an authenticated checkpoint without a
separate owner decision about credential custody.
