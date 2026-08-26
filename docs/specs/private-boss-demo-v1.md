# Private Boss Demo v1 Specification

- **Status:** Ready for agent after this specification lands on `main`
- **Date:** 2026-08-26
- **Issue:** [#92 — Deliver the private boss demo from official download to shared Explorer
  file](https://github.com/scwlkr/balls/issues/92)

## Problem Statement

The Owner needs to let the boss use one approved Windows project folder from a separate Windows
computer. Balls already has most of the Circle Files machinery, but the observed path still depends
on an outdated public package, command-line Owner choreography, transient browser connection data,
and a mapping action that has not been proved from a genuine standard-user browser click.

The boss must not learn PowerShell, daemon flags, IP addresses, ports, SMB credentials, internal
object IDs, plan tokens, provider terminology, or drive-mapping mechanics. A technically successful
share that still requires those steps does not solve the problem.

## Solution

The Owner obtains one accepted Balls package through `balls.wlkrlabs.com`, creates or selects a
private Circle, chooses a folder graphically, approves one invited Member for read/write access, and
sends one private invitation separately.

On a fresh separate Windows user account, the boss obtains Balls from the same official entrypoint,
opens the local browser interface, pastes the invitation, and joins. The Circle shows one ordinary
action to open the approved shared folder. Balls chooses a free drive, establishes the existing
approved Circle Files access, and opens the folder in Windows File Explorer. Both people edit one
disposable ordinary file through their normal applications.

The specification passes only when this complete journey is observed from the exact accepted
release-candidate package. Source builds, CLI substitutions, manually copied binaries, or a mapped
folder that is never opened by the product do not pass.

## User Stories

1. As the Owner, I want one official Balls download entrypoint, so that I can give the boss a simple
   and trustworthy location.
2. As the Owner, I want the download entrypoint to identify the accepted package, so that I know the
   boss and I are using the intended build.
3. As the Owner, I want to install or open Balls without development tools, so that the pilot does
   not depend on the repository.
4. As the Owner, I want Balls to start the local UI and required private-network services, so that I
   do not supply daemon flags or ports.
5. As the Owner, I want to create or select the pilot Circle graphically, so that the workflow begins
   with the people and work rather than infrastructure.
6. As the Owner, I want to choose the host folder with a normal Windows folder picker, so that I do
   not paste a path into a terminal.
7. As the Owner, I want Balls to explain the exact folder and access being contributed before any
   system mutation, so that my approval is informed.
8. As the Owner, I want any required Windows administrator consent limited to the exact host-side
   operation, so that Balls does not run the ordinary product elevated.
9. As the Owner, I want to select the invited person and read/write access in human terms, so that I
   do not manage SMB accounts, grant IDs, or credentials.
10. As the Owner, I want one private invitation containing the needed Circle connection information,
    so that I do not send separate setup instructions.
11. As the boss, I want to reach Balls through `balls.wlkrlabs.com`, so that I do not receive an
    unofficial binary directly from another computer.
12. As the boss, I want to open Balls under a normal Windows user account, so that joining does not
    require administrator access.
13. As the boss, I want to paste one invitation in the local browser, so that I do not configure the
    network or provider.
14. As the boss, I want a clear success state after joining, so that I know which Circle and person
    identity are active.
15. As the boss, I want the approved folder presented as a Circle Capability, so that I do not see a
    machine share or provider credential.
16. As the boss, I want one action labeled in terms of opening the folder, so that I do not choose a
    drive letter or execute a mapping plan.
17. As the boss, I want Balls to choose a free drive without changing unrelated mappings, so that my
    existing Windows environment is preserved.
18. As the boss, I want the action to open the approved folder in File Explorer, so that success is
    immediately visible in the normal Windows file experience.
19. As the boss, I want the joined Circle and connection information to survive closing and
    reopening the local browser, so that the guided path is not a one-session trick.
20. As the boss, I want actionable plain-language failures and a safe retry, so that a temporary host
    or network problem does not send me to PowerShell.
21. As the Owner, I want the boss to create, open, edit, rename, and delete one disposable ordinary
    file, so that read/write access is proved through real work behavior.
22. As the boss, I want the Owner to observe the same file changes, so that the demo proves a shared
    folder rather than two local copies.
23. As the Owner, I want every intervention and elapsed onboarding time recorded, so that the demo's
    simplicity is measured honestly.
24. As the Owner, I want failed or incomplete setup to preserve contributed user files and unrelated
    Windows state, so that urgency does not make the pilot destructive.
25. As the Owner, I want publication to pause for my explicit approval, so that a tested candidate
    does not silently become the official download.
26. As a future implementer, I want the exact artifact identity and observed limitations recorded,
    so that later work begins from evidence rather than assumptions.

## Implementation Decisions

- **Dominant workflow:** Implement one owner-to-member graphical journey. Do not add a second setup
  mode, generalized provider framework, or administration console for this slice.
- **Existing foundation:** Reuse the current Circle identity, invitation, admission, Circle Files
  Contribution, Access Grant, synchronization, Windows hosting, limited Member credential, and
  Explorer mapping behavior. Change existing seams before creating parallel ones.
- **Application boundary:** The Owner and Member browser interfaces call the same local application
  behavior used by the CLI. Business rules remain in the daemon and domain; Windows mutations stay
  behind typed platform operations.
- **Packaged startup:** The Windows package starts the loopback browser UI plus the private-LAN
  admission and Circle Files synchronization services needed by the invitation. The normal path has
  no daemon flags, IP entry, port entry, or separate service setup.
- **Owner contribution:** The browser provides a normal Windows folder chooser, a concise approval
  summary, and one action that prepares and contributes the exact folder. Any privileged hosting
  change uses the existing narrow Windows helper and an explicit operating-system consent boundary.
- **Owner grant:** The Owner selects a human Member and `Read/write` access. Balls derives internal
  identifiers, provider credentials, provisioning plans, and synchronization metadata without
  displaying or requesting them.
- **Invitation:** One private invitation carries or resolves all connection information required by
  the supported private-LAN path. The invitation remains separate from public software
  distribution.
- **Persistence:** Guided connection and Capability state is persisted in the Node's protected local
  state. Closing and reopening the local browser cannot downgrade the Member to a technical setup
  form.
- **Member action:** The normal Member UI offers one `Open shared folder in Explorer` action. Balls
  selects `P:` when available or another free supported letter, applies the exact authorized
  mapping, and launches File Explorer at that mapped root.
- **Windows identity:** Member-side mapping and Explorer launch run as the current unelevated user.
  The boss does not need an inbound service, firewall exception, developer toolchain, or local
  administrator approval.
- **Collision behavior:** Balls never replaces or adopts an unrelated drive mapping. It reports a
  plain-language failure or chooses another free supported drive.
- **Error behavior:** Offline host, invalid or consumed invitation, revoked grant, blocked package,
  provider mismatch, and failed Explorer launch produce bounded, actionable states without exposing
  secrets or sending the user to the CLI.
- **Application trust:** Balls does not bypass Smart App Control, application-control policy, or
  download trust decisions. If the target computer blocks the accepted package, trusted signing or
  an independently authorized policy is a release blocker, not an alternate execution task.
- **Pilot network:** The supported environment is approximately two or three trusted people on one
  private LAN. Owner-managed Tailscale may carry the same private traffic, but Member Tailscale
  setup is not required or expanded in this slice.
- **Safety floor:** Do not expose SMB or Balls services publicly, leak provider credentials, weaken
  existing provider security, delete the contributed folder or its files, or mutate unrelated
  Windows shares, accounts, firewall rules, credentials, or mappings.
- **Distribution:** `balls.wlkrlabs.com` points to exact assets in an Owner-accepted GitHub Release;
  the website does not host a second package copy. Artifact identity includes tag, commit, package
  identity, and SHA-256.
- **Publication gate:** Builds, tests, and candidate preparation may proceed autonomously. Creating
  or changing a public tag, GitHub Release, stable channel, or website download pointer requires
  separate Owner approval immediately before publication.
- **Evidence:** Record package identity, both Node environments, user privilege level, network
  boundary, elapsed Member onboarding time, every Owner/admin intervention, each real file
  operation, and every limitation. Never report a physical-device result from VM evidence.

## Testing Decisions

- Tests verify behavior visible at the highest practical seam. They do not assert private class
  structure, exact internal plans, incidental HTML structure, or implementation-only identifiers.
- The dominant automated seam is one packaged two-Node Windows journey: start clean isolated Owner
  and Member Nodes from the same release-candidate package, complete both local browser workflows,
  use the live LAN transport and Windows Circle Files provider, open the real Explorer mapping, and
  perform bidirectional file I/O. This is one product seam across UI, daemon, persistence,
  transport, provider, and operating system.
- Existing packaged browser acceptance, Canary package verification, Windows provider lab, and the
  prior two-computer Circle Files pilot are the testing prior art. Extend those seams instead of
  adding a disconnected test harness.
- Focused contract tests cover only destructive or hard-to-observe branches at the local API and
  platform boundaries: protected persistence, folder-picker cancellation, drive collision,
  provider mismatch, offline retry, Explorer-launch failure, secret redaction, and exact ownership
  during rollback.
- Browser behavior tests assert the human vocabulary and visible state changes: no technical setup
  controls in the guided path, one Owner contribution/grant flow, one Member open action, reopening
  persistence, progress, success, and actionable failure.
- The automated two-VM journey is necessary before a physical pilot but is not physical evidence.
  Final boss-demo evidence must identify the actual separate Windows device and account used.
- A device whose application-control policy blocks the package is reported as `BLOCKED`; tests may
  not disable or evade that policy.
- Publication verification occurs only after separate Owner approval and then checks the live
  website pointer, exact GitHub Release asset, tag, commit, package identity, and SHA-256.
- The issue passes only with the full graphical path. CLI-driven setup or mapping may diagnose a
  failure but cannot substitute for acceptance.

## Out of Scope

- Balls Wizard, any local model download, and shared Circle AI.
- Circle Messaging beyond existing implementation that happens to remain intact.
- SSH, remote terminal commands, RDP, whole-computer integration, or remote administration.
- Member removal, cross-Capability revocation, or new Circle Files lifecycle/security architecture.
- Generalized Capability Provider infrastructure or additional storage providers.
- Revit certification, multi-user Revit worksharing, application compatibility matrices, file
  replication, offline synchronization, conflict resolution, version history, or backup.
- Multi-Anchor replication, public-internet operation, enterprise identity, or scale hardening.
- macOS/Linux packaging expansion or a broad public launch.
- Disabling, bypassing, or weakening Windows application-control or network protections.

## Further Notes

The current official release predates Circle Files, and the previous assisted physical pilot used
CLI mapping rather than the complete browser action. Those are known gaps, not evidence that the
new journey already passes.

The time pressure makes scope control more important. If a blocker does not prevent the exact
download-to-Explorer journey or violate its safety floor, it does not enter this issue.
