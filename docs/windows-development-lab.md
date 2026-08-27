# Windows development lab

Read this runbook before Windows VM automation, manual two-VM acceptance, unsigned product
execution, browser UI acceptance, installer, Development/Canary testing, or recovery of a dedicated
Windows guest. The lab accelerates risky Windows checks without weakening security controls on the
physical host.

## Current Linux-hosted company-pilot lab

The current workstation runs the working Windows environment through the existing Docker/KVM
`omarchy-windows` configuration and preserved disk. Its container is normally removed when the
standard launcher exits and recreated around the same disk on the next launch; absence from
`docker ps -a` is not by itself loss of the VM. Low-memory Windows guests named
`balls-issue61-provider-desktop`,
`balls-issue61-provider2025`, `balls-issue61-client`, and `balls-issue61-node2` are historical
disposable test fixtures on the same private `windows_default` Docker network.

The owner has explicitly selected the existing working Windows VM as the folder host and one
existing 2 GiB disposable Windows desktop guest, `balls-issue61-provider-desktop`, as the immediate
boss simulator. A separate,
freshly installed physical Windows laptop also exists, but its standard-user account reports an
enforced Smart App Control block on the unsigned pilot; physical-device testing is deferred until
its application-trust boundary can be legitimately resolved. It is not an issue #92 gate. The
Owner personally performs both roles in the selected VMs; passing evidence completes #92 as
same-host two-VM evidence and never becomes a claim about a separate physical network. Use the
[`Private Boss Demo checklist`](verification/private-boss-demo-v1-checklist.md). Inspect before
starting:

```bash
docker ps -a --format '{{.Names}} {{.Status}}'
free -h
docker network inspect windows_default --format '{{.Name}} {{.Driver}}'
```

- Never stop, recreate, checkpoint, replace, or change the working Windows VM's disk or GPU
  configuration merely to run a Balls test.
- Start only the explicitly selected disposable guest and monitor host memory; do not run the
  other historical test VMs concurrently.
- Use only the already selected 2 GiB disposable Windows desktop guest as the active boss
  simulator. Do not create a second small guest merely to run #92. Keep the physical laptop
  deferred; do not treat hardware availability or a browser download as permission to bypass its
  enforced application-trust policy.
- Use dedicated clean Owner and Member profiles. The Member profile must not belong to local
  Administrators and must have no prior Balls state, identity, credential, or Balls-created mapping.
- Keep desktop viewing loopback-only and Circle traffic on `windows_default`. Do not add Tailscale,
  public exposure, or host-forwarded SMB.
- Install in both profiles only with the command copied from the live Development or immutable
  previous-version section of `balls.wlkrlabs.com`; never copy the candidate between VMs.
- The working Owner VM is intentionally a clean recipient environment: do not assume
  `C:\Dev\balls`, Git, a repository checkout, release-engineering scripts, PowerShell 7, or a .NET
  runtime exists there. The Owner pastes only the website command into the Windows PowerShell prompt.
  `eng/canary/Test-WindowsDownload.ps1` is a release-engineering seam for an automated Windows
  runner or development checkout, not an Owner-test instruction.
- Use the pre-existing disposable local folder `C:\BallsDemo\Projects` and seed file
  `before-balls.txt`. Do not use a host-mounted, mapped, or network-backed contribution.
- Keep guest passwords, SSH private keys, provider credentials, and signed-in application state
  out of command output, repository files, evidence, and issue comments.
- Record the Owner's effective PowerShell execution policy before privileged grant provisioning.
  The native elevated helper passes its compiled fixed grant command directly to the built-in
  Windows PowerShell command interface, keeps structured data on standard input, and does not use a
  `.ps1`, `-File`, an execution-policy flag, or a policy change. The real packaged grant still needs
  observation under the clean profile's default `Restricted` policy. Never change or bypass that
  policy to obtain a passing result.
- The public installer uses a hash-bound native bootstrap specifically so a clean recipient with
  effective `Restricted` policy can install without running a PowerShell script. Keep that install
  proof separate from the later privileged grant-helper observation above.
- Record the actual `EnableLUA` setting before describing administrator approval. On a VM with
  existing UAC disabled, the normal elevated helper can complete without displaying a consent
  prompt; do not claim that the user saw or accepted a dialog.
- Treat the Hyper-V instructions below as historical Windows-host evidence. They do not describe
  the current Linux workstation or authorize creating nested Windows/Hyper-V infrastructure.

### Nested-NAT development feedback

The two Dockur containers share `windows_default`, but each Windows guest sits behind its own inner
NAT. Keep their guest subnets unique and privately routed: the Owner guest uses `172.30.0.2/24`
behind `omarchy-windows`, and the disposable Member guest uses `172.31.0.2/24` behind
`balls-issue61-provider-desktop`. The Member container starts with `IP=172.31.0.2` and installs a
route to `172.30.0.0/24` through the Owner container. The Owner Compose configuration installs the
reciprocal route to `172.31.0.0/24` through the Member container. These routes stay inside
`windows_default`; they do not publish either guest or SMB on the Linux host.

This topology lets the normal bootstrap use automatic private listeners without an advertised
address override. A mergeable or release acceptance run must use the website command or the exact
offline bootstrap with no `--advertised-private-address`, manual IP, port, or daemon flag. The
typed advertised-address option remains a bounded diagnostic for local experiments, not checklist
evidence.

Before invitation testing, verify the live identities and routes rather than assuming container
attachment means the guests are mutually reachable:

```bash
docker inspect --format '{{.Name}} {{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' \
  omarchy-windows balls-issue61-provider-desktop
docker exec omarchy-windows ip route show 172.31.0.0/24
docker exec balls-issue61-provider-desktop ip route show 172.30.0.0/24
```

After Balls starts, prove the Owner guest's selected admission and synchronization ports plus TCP
445 from the Member container over `172.30.0.2`. Keep the Windows firewall Private-profile rules
unchanged. Do not add an outer-address DNAT, host port, public listener, or separate SMB service.

A new versioned `ballsd.exe` path can cause Windows to ask again for private-network access. The
Owner must allow the Private profile and leave Public unchecked; dismissing the prompt creates a
per-version block rule and the Member sees the Owner as unreachable. For unattended lab diagnosis
only, an equivalent rule must be limited to that exact executable, its two persisted listener
ports, the Private profile, and the live Member container address on `windows_default`. Record that
automation as a lab intervention rather than interactive prompt evidence.

Automatic admission and synchronization ports are allocated once and stored in the protected Balls
state directory. Preserve that state during local package updates, and verify a normal shortcut
relaunch reuses the same two ports; rotating either port strands previously joined Members.

## Deferred physical coworker laptop

The freshly installed Windows laptop is owner-approved disposable test infrastructure, but its
available Windows account does not have administrator privileges. This is the stronger real
coworker test: the invited Member must not need elevation, Windows OpenSSH Server, a background
service, an inbound firewall exception, or a development toolchain.

The owner reports that Microsoft Smart App Control blocks the current unsigned Balls build on
this laptop. A standard account cannot override that application-trust decision. Until an
authorized device administrator independently establishes an appropriate trust policy or Balls
ships binaries signed by a genuinely trusted publisher, this physical device cannot execute the
product. Use the already authorized disposable Windows guest for executable end-to-end checks;
physical laptop browser and built-in network/SMB diagnostics remain partial evidence only.

Any later physical run starts from `balls.wlkrlabs.com` and the website-provided command in the
PowerShell included with Windows. It does not use a copied ZIP, LocalSend, SSH, or administrator
access. The installed shortcut starts the ordinary user-owned daemon, opens its loopback browser,
and makes only outbound connections to the Owner. Current-user Credential Manager and Explorer
drive mapping do not require administrator approval.

If remote administration is independently available and explicitly approved, it may simplify lab
automation, but it is optional and must never become a product or acceptance prerequisite. Never
attempt to install system OpenSSH, change protected firewall policy, reuse an unrelated allowed
port, run blocked code through another executable, remove download trust metadata, or otherwise
evade an operating-system access decision from an unprivileged account. User-level
diagnostics may inspect the Windows account, private IP, network profile, and outbound
reachability without exposing passwords or private keys.

Once application trust is legitimately resolved, a later physical observation may cover an
ordinary recipient running the website command, redeeming one invitation, mapping only the
authorized encrypted SMB share, and performing real file operations over the physical LAN. That is
additional evidence, not completion criteria for #92. Administrator approval remains limited to
the Owner when it creates the share, firewall rules, and limited Member account. The working
Owner's unrelated mapped drives, applications, and explicit Windows elevation boundaries remain
protected.

Restrict any later cleanup to lab-owned access and rules. Do not disable unrelated security
controls, expose SSH/SMB publicly, reuse an execution path to evade a firewall block, or treat
administrative automation access as permission to bypass an explicit operating-system decision.

## Historical Windows-host Hyper-V environment

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
- An enforced Smart App Control or Application Control decision on the physical coworker laptop
  is a hard unsigned-distribution blocker, not permission to disable protection or find an
  alternate execution path. Trusted code signing or an authorized managed policy is a separate
  owner-approved distribution decision.
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
| Read-only Windows SMB readiness contracts, real adapter, structured CLI, and no-mutation snapshot | `Test-BallsVmSmbReadiness.Guest.ps1` | `C:\BallsLab\smb-readiness\latest-result.json` |
| Dedicated Circle folder clean apply/retry, hostile paths, collisions, and injected rollback | machine-local `Test-BallsCircleFilesHelper.Guest.ps1` | structured PowerShell Direct result captured in the dated verification record |
| Circle Files revoke/cleanup, future-auth denial, busy confirmation, partial retry, hostile substitution, and byte preservation | machine-local `Test-BallsCircleFilesRevocation.Guest.ps1` | structured PowerShell Direct result plus before/after hashes captured in the dated verification record |
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

On 2026-08-21, the dedicated Circle Files helper gate passed from exact source commit
`7bf86c506591cfd18681c71ab988eb2e268657b6`. The guest fixture temporarily selected a Private
profile and disabled its 17 pre-existing broad Public/Any SMB allow rules so the already-proven
readiness contract could permit the mutation. Its `finally` path restored the original Public
profile and enabled states. Release apply/retry, Debug-only injected failure rollback, hostile
path refusal, pre-existing share collision, protected folder ACL, required share encryption, and
Private/LocalSubnet firewall scope were observed. Post-run inspection found no Balls share or
firewall rule and the VM was stopped. No checkpoint was restored. See the
[dedicated helper verification](verification/2026-08-21-dedicated-circle-folder-helper.md).

Two explicit checkpoints exist:

- `Balls.Dev.Base`: Windows installed before the development toolchain;
- `Balls.Dev.Toolchain.Core`: core toolchain ready, before ChatGPT installation and authentication.

Restoring either checkpoint discards later guest state and authentication. Treat restoration as a
destructive recovery action: verify the exact VM and checkpoint, preserve needed guest work, and
obtain owner confirmation before applying it. Do not create an authenticated checkpoint without a
separate owner decision about credential custody.
