# Revit Server Rapid Setup v0 Checklist

Use `PASS`, `FAILED`, `BLOCKED`, or `NOT RUN`. Record exact identities and observations without
credentials, private Circle material, Autodesk license data, or raw machine secrets.

## Existing-lab safety

- [ ] `docs/windows-development-lab.md` names the disposable VM, storage, network, ports, ownership,
  start/stop, recovery, and mutual-exclusion rules.
- [ ] Existing Windows 11, Neptune, Member, and historical VMs are identified before mutation.
- [ ] Existing high-memory Windows VMs are stopped for the rapid-setup run.
- [ ] The new VM uses separate storage, virtual disks, network identity, and loopback console ports.
- [ ] No existing VM disk, configuration, launcher, route, or shared folder is modified.
- [ ] No real company file or Revit model enters the disposable VM.

## Ready gate — outside the timer

- [ ] VM identity:
- [ ] VM host/hypervisor:
- [ ] Windows edition/build:
- [ ] Windows Server 2022 Desktop Experience confirmed; Server Core and Windows 11 rejected.
- [ ] CPU/RAM/system-disk/data-disk allocation:
- [ ] Updated Windows has no pending restart.
- [ ] Final temporary hostname is set before Revit installation.
- [ ] Fixed local NTFS `D:` volume is present and empty at `D:\RevitServer\2027`.
- [ ] No Linux shared mount, SMB share, Circle Files path, or reparse point reaches that root.
- [ ] Exact Balls Development tag/commit/package/SHA-256:
- [ ] Balls installs and opens without weakening Windows protection.
- [ ] Official Autodesk installer filename/version/publisher/SHA-256:
- [ ] Autodesk media is downloaded and extracted locally.
- [ ] Revit Server 2027 is absent and no conflicting Revit version/path is detected.
- [ ] Lab network is local/private with no Public-profile or public host exposure.
- [ ] Evidence directory and monotonic wall-clock source are ready.
- [ ] Ready result: `PASS | BLOCKED | NOT RUN`

## Timed setup

- Start timestamp:
- [ ] Select cached installer and press **Begin setup** on a Ready result.
- [ ] Preflight records fresh OS, restart, disk, path, IIS, network, and installer observations.
- [ ] Preview shows exactly Host+Admin, no Accelerator, versioned paths, prerequisites, ACLs,
  server-local `RSN.ini`, firewall effects, and verification actions.
- [ ] One plan-bound Windows elevation is approved.
- [ ] Balls installs the documented Server 2022 Revit/IIS prerequisites.
- [ ] No restart is required.
- [ ] Balls ensures Default Web Site and the approved data paths/ACLs.
- [ ] Balls persists `awaiting-autodesk` before launching the verified official installer.
- [ ] Owner accepts Autodesk's terms.
- [ ] Any Autodesk publisher consent is recorded separately from Balls' prerequisite elevation.
- [ ] Owner opens the Revit Server configuration page.
- [ ] Owner confirms Host and Admin enabled, Accelerator disabled, and exact paths.
- [ ] Autodesk installation completes without unsupported workaround.
- [ ] Owner returns to Balls and selects Verify.
- [ ] Balls generates the complete handoff bundle.
- [ ] Bundle export completes before the timer stops; merely reaching healthy postflight does not
  end the timed attempt.
- End timestamp:
- Wall-clock elapsed:
- Human-intervention elapsed:
- Human-intervention measurement: Balls records the conservative upper-bound `awaiting-autodesk`
  window (including installer waiting); optionally record separately observed hands-on time here:
- Required result: elapsed `< 00:30:00`

## Health-only proof

- [ ] Installed product identity is Revit Server 2027.
- [ ] Enabled roles are exactly `Host,Admin`.
- [ ] Accelerator role and `RSACCELERATOR2027` are absent.
- [ ] Projects and any required Cache path match the approved version-specific plan.
- [ ] Required service principals have the approved data-path access.
- [ ] Default Web Site exists and responds locally.
- [ ] Expected versioned Host/Admin IIS applications exist.
- [ ] Revit Server 2027 application pool is started in Integrated mode.
- [ ] Expected local Revit/IIS endpoints return Revit-specific success rather than an IIS error.
- [ ] Server-local `RSN.ini` contains exactly the temporary canonical Host name.
- [ ] Revit Server Administrator opens and displays that Host with an empty usable project tree.
- [ ] Revit Server and IIS logs show no fatal setup/runtime error.
- [ ] `D:\RevitServer\2027` has no SMB, Circle Files, Linux mount, or reparse exposure.
- [ ] No required listener/rule has Public-profile or public host exposure.

## Setup recovery

- Closing the browser does not cancel Windows feature installation. Reopen the normal Balls
  shortcut and read the persisted stage.
- `applying-prerequisites` left by a daemon interruption becomes `BLOCKED`; wait for Windows
  servicing to settle, then perform a fresh read-only inspection. Balls does not blindly replay
  DISM/ServerManager work.
- A prerequisite result that requires restart is `BLOCKED`. Restart Windows and begin a fresh
  attempt; do not move the restart outside the timed run.
- `awaiting-autodesk` means the Balls elevation has ended. Complete Autodesk's graphical terms and
  configuration as a human, selecting exactly Revit Server 2027, Host + Admin, Accelerator off,
  and the displayed version-specific paths.
- `incomplete` or `failed` can reopen the same digest-verified installer after health inspection.
  `blocked` requires selecting the installer and approving a fresh plan.
- Balls never deletes a repository, uninstalls Autodesk software, accepts Autodesk terms, or repairs
  ambiguous third-party state during retry.
- The prerequisite helper writes the documented server-local `RSN.ini` before Autodesk starts;
  postflight passes only if Autodesk preserves the exact single canonical Host line. Record the
  before/after file and ACL observation in the disposable-VM evidence.

## Portable handoff

- [ ] `setup-template.v1.json` contains portable intent only.
- [ ] `setup-receipt.v1.json` records exact artifact, plan, machine, health, timing, and limitations.
- [ ] `README.md` gives the boss the short repeatable setup flow.
- [ ] `bundle-manifest.json` versions the schemas and hashes every other bundle member.
- [ ] Bundle contains no credential, Circle private material, Windows SID, IP/network replay,
  Autodesk installer, executable script, VM image, company data, or model data.
- [ ] README requires a fresh Balls inspection and preview on the boss's future server.
- [ ] Template contains no hostname, absolute path, or replayable machine plan. The receipt may
  retain the temporary Host and paths only inside `temporaryEvidence` with `replayProhibited=true`;
  it cannot be executed as setup intent.
- [ ] Installed Balls identity comes from the strict official Development `installation.json`
  record; a missing, non-Development, substituted, or malformed record blocks export.
- [ ] The ZIP has exactly four members. The manifest hashes the other three members and strict
  validation rejects unknown fields, duplicate/extra members, hash drift, executable payloads,
  secrets, SIDs, private Circle material, company/model data, and template replay material.
- [ ] If ballsd restarts after Begin setup, monotonic timing cannot be recovered and export is
  `BLOCKED`; run a fresh timed attempt rather than deriving a PASS from wall-clock timestamps.

## Exact result

- Overall: `PASS | FAILED | BLOCKED | NOT RUN`
- Exact Balls identity:
- Exact Autodesk identity:
- Exact Windows identity:
- Plan digest:
- Setup bundle SHA-256:
- Elapsed wall-clock:
- Prompts/interventions:
- Failure/blocker:
- Residual state:

Use this sentence unchanged for PASS:

> **PASS — Revit Server 2027 Host+Admin installation and Administrator surface are healthy in the
> disposable QEMU/KVM lab. Revit client/model use, synchronization, concurrency, performance,
> backup, recovery, remote access, and production hardware were not tested.**

## Explicitly not proved

- [ ] Revit client/model use: `NOT RUN`
- [ ] Synchronize/new-local/multi-user behavior: `NOT RUN`
- [ ] Tailscale, WAN, or remote performance: `NOT RUN`
- [ ] Backup, restore, or model recovery: `NOT RUN`
- [ ] Reboot/unattended service operation: `NOT RUN`
- [ ] Boss hardware or Autodesk-supported hypervisor: `NOT RUN`
- [ ] Production readiness: `NOT RUN`
