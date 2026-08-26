# Private Boss Demo v1 Specification

- **Status:** Ready for agent after this specification lands on `main`
- **Date:** 2026-08-26
- **Issue:** [#92 — Deliver the private boss demo from official download to shared Explorer
  file](https://github.com/scwlkr/balls/issues/92)

## Problem Statement

The Owner needs to prove, on the one available Omarchy Linux laptop, that Balls can give a trusted
Member one approved Windows project folder without exposing the machinery beneath Circle Files.
The laptop has one working Windows 11 environment and one authorized small Windows boss simulator;
there is no second physical computer available for this acceptance run.

Balls already contains most of the Circle Files machinery, but the human journey still depends on
an outdated Alpha package, command-line Owner choreography, transient browser connection data, and
a mapping action that has not been observed from the complete packaged browser flow. The download
site has no durable Development channel for testing incomplete packages through the same official
entrypoint that users will follow.

One copyable Windows terminal command is an accepted installation experience. The problem is not
the presence of PowerShell; it is requiring the user to understand or configure PowerShell,
runtime dependencies, daemon flags, IP addresses, ports, SMB credentials, internal identifiers,
provider terms, or drive-mapping mechanics.

The former requirement for a fully automated two-VM graphical harness would add a second product:
automation for two interactive desktops, Windows consent, folder pickers, and Explorer. It would
delay the human outcome and provide less immediate product evidence than the Owner personally
performing the complete journey. Physical-device evidence is also impossible with the available
hardware and is not required for this issue.

## Solution

`balls.wlkrlabs.com` keeps the latest accepted Alpha as its primary download. Beneath the main
section it presents clearly warned Development builds and a conventional previous-versions list.
Development packages may be incomplete or broken, but every published package is an immutable,
identity-verified GitHub prerelease asset. An agent may publish a Development package needed by an
active issue without a separate per-build approval; Alpha promotion remains Owner-gated.

The Windows lane presents one copyable command block that works in the PowerShell included with
supported Windows. The bootstrap downloads completely before execution, verifies the selected
manifest and package, installs the self-contained package for the current user, records its channel
and exact identity, opens Balls, and creates a normal shortcut for later launches. The user does not
install PowerShell 7, .NET, or development tools and does not bypass execution or application-trust
policy.

The Owner uses the existing Windows 11 VM in a clean dedicated test profile. The simulated boss
uses the existing authorized 2 GiB Windows desktop guest in a clean dedicated nonadministrator
profile. Both install the same website-delivered package. They communicate only over the private
`windows_default` bridge. The Owner creates the Circle graphically, selects a pre-existing
disposable local folder, grants one invited Member read/write access, and sends one private
invitation. The Member joins, reopens Balls to prove persistence, opens the Capability through one
ordinary action, and performs real file operations through the Explorer mapping.

Focused automated tests cover deterministic browser, application, package, persistence, and
Windows-provider behavior. The Owner personally performs the highest product seam as a
checklist-driven two-VM rehearsal and records the result honestly as same-host virtual evidence.
After the exact green-`main` Development package passes, Alpha promotion pauses for explicit Owner
approval. Promotion moves the Alpha pointer to the identical tested assets, followed by live
website identity and startup readback.

## User Stories

1. As the Owner, I want `balls.wlkrlabs.com` to remain the only human software entrypoint, so that
   every test and demonstration begins from the real distribution path.
2. As a prospective user, I want the latest accepted Alpha shown first, so that the recommended
   package is unmistakable.
3. As a tester, I want Development builds beneath the recommended release, so that I can obtain an
   incomplete candidate without presenting it as accepted software.
4. As a tester, I want every Development build labeled as possibly broken, so that its quality
   status is honest.
5. As a tester, I want a conventional previous-versions list, so that I can retrieve an older exact
   release without guessing URLs.
6. As a tester, I want the newest ten Development builds visible on the site, so that the page is
   useful without becoming an unbounded build ledger.
7. As a tester, I want a link to the complete GitHub Releases history, so that older Development
   builds remain discoverable.
8. As a tester, I want every historical install command bound to an immutable version manifest, so
   that it cannot silently start installing newer bytes.
9. As the Owner, I want Development publication to proceed without a per-build approval, so that
   packaged testing does not wait on release ceremony.
10. As the Owner, I want Alpha promotion to require my explicit approval, so that an unaccepted
    candidate never becomes the recommended release.
11. As a user, I want packages to remain on GitHub Releases while the website points to them, so
    that there is one durable artifact rather than an ad hoc copy.
12. As a user, I want tag, commit, filename, internal package identity, and SHA-256 to agree, so that
    a low-quality Development build is still exactly identifiable.
13. As a Windows user, I want one copyable website command block, so that installation does not
    require a ZIP extraction workflow.
14. As a Windows user, I want the command to work in the PowerShell included with Windows, so that
    PowerShell 7 is not a prerequisite.
15. As a Windows user, I want the bootstrap downloaded completely before it runs, so that the site
    does not teach a pipe-to-shell or policy-bypass workflow.
16. As a Windows user, I want a self-contained package, so that .NET and development tools are not
    prerequisites.
17. As a Windows user, I want installation to remain in my user profile, so that ordinary use does
    not require permanent elevation.
18. As a Windows user, I want successful installation to open Balls immediately, so that I know
    what to do next.
19. As a returning Windows user, I want a normal Balls shortcut, so that the terminal command is
    only an installation or update entrypoint.
20. As a future updater, I want the installed channel and exact package identity persisted, so that
    a later graphical updater can follow the same channel safely.
21. As the Owner, I want Balls to start the local UI and required private-network services, so that
    I do not supply daemon flags, addresses, or ports.
22. As the Owner, I want to create the test Circle graphically, so that acceptance proves the
    ordinary product path from clean Balls state.
23. As the Owner, I want to select a pre-existing local folder through the normal Windows picker,
    so that the demo proves adoption rather than creation of an empty special-purpose folder.
24. As the Owner, I want the selected folder and requested access summarized before mutation, so
    that my consent is informed.
25. As the Owner, I want only the exact host-side operation to request Windows administrator
    consent, so that the normal Balls process remains unelevated.
26. As the Owner, I want to select a human Member and `Read/write`, so that I do not manage SMB
    accounts, credentials, Access Grant identifiers, or provisioning plans.
27. As the Owner, I want one private invitation with the required Circle connection information,
    so that I do not send a second set of networking instructions.
28. As the simulated boss, I want to install Balls under a genuine nonadministrator Windows
    profile, so that Member onboarding proves it does not require administrative membership.
29. As the simulated boss, I want to paste one invitation and my name, so that I do not configure a
    provider or network.
30. As the simulated boss, I want a clear join success state, so that I know which Circle and Member
    identity are active.
31. As the simulated boss, I want the approved folder presented as a Circle Capability, so that I
    do not see an SMB share or provider credential.
32. As the simulated boss, I want closing and reopening Balls to preserve the joined Circle and
    guided Capability, so that the experience is not a one-session trick.
33. As the simulated boss, I want one `Open shared folder in Explorer` action, so that I do not
    choose a drive letter or apply a mapping plan.
34. As the simulated boss, I want Balls to select `P:` or another free supported drive without
    replacing unrelated mappings, so that existing Windows state remains intact.
35. As the simulated boss, I want File Explorer opened at the approved mapped root, so that success
    is immediately visible.
36. As the simulated boss, I want plain-language failures and a safe retry, so that a temporary host
    or network failure does not turn into a technical setup exercise.
37. As the Owner, I want both Nodes to edit the same disposable file, so that the demo proves one
    shared folder rather than two local copies.
38. As the Owner, I want create, open, edit, rename, and delete behavior observed through real
    Windows applications, so that read/write access is practical.
39. As the Owner, I want the seed file that existed before contribution preserved, so that Balls
    proves safe adoption of existing work.
40. As the Owner, I want the exact package, profiles, network, prompts, interventions, timing, file
    operations, and limitations recorded, so that the result can be audited.
41. As the Owner, I want to perform both sides of the same-host rehearsal personally, so that I can
    verify the experience before showing it to my boss.
42. As a future reader, I want the evidence labeled as same-host two-VM, so that it is never
    misreported as physical-device or physical-LAN proof.
43. As a developer, I want focused automated checks around hard and destructive branches, so that
    manual acceptance is supported by deterministic regression coverage.
44. As a developer, I want the manual journey captured as a replayable checklist, so that another
    run does not depend on memory.
45. As the Owner, I want elapsed time recorded without a hard automated threshold, so that the
    product's simplicity is visible without making VM speed a flaky release gate.
46. As the Owner, I want the exact green-`main` Development package rehearsed before Alpha
    promotion, so that accepted bytes are the bytes actually observed.
47. As the Owner, I want Alpha promotion to move only a channel pointer, so that approval never
    triggers an untested rebuild.
48. As the Owner, I want a final live website identity and startup check after promotion, so that
    the recommended command is known to resolve to the accepted assets.

## Implementation Decisions

- **Dominant workflow:** Deliver one Owner-to-Member Circle Files journey. Reuse the current Circle
  identity, invitation, admission, Contribution, Access Grant, synchronization, Windows hosting,
  limited Member credential, and Explorer-mapping behavior.
- **Application boundary:** The browser and installer call typed application behavior. Business
  rules remain in the daemon and domain; Windows mutations remain behind typed platform
  operations. The CLI is diagnostic and automation support, not a substitute for acceptance.
- **Website hierarchy:** The main section presents the latest accepted Alpha. Lower sections
  present the latest warned Development build and a previous-versions table containing all
  accepted releases plus the newest ten Development builds. The complete GitHub Releases archive
  remains linked.
- **Channel model:** Canary is ephemeral green-`main` CI evidence. Development is a durable public
  testing channel backed by immutable GitHub prereleases from an identified branch or `main`
  commit and may be incomplete or broken. Alpha is an Owner-accepted prerelease for one coherent
  product outcome. Beta and Stable retain their existing meanings.
- **Development publication authority:** An agent implementing an active issue may create an
  immutable Development tag and GitHub prerelease and move the Development pointer after build and
  package-integrity checks pass. The agent records the previous pointer for rollback. This authority
  does not extend to Alpha, Beta, or Stable publication.
- **Artifact discipline:** Functional failure is allowed in Development; identity ambiguity,
  corrupt packaging, secret inclusion, mutable assets, and policy bypass are not. Every manifest
  binds the release tag, full commit, platform asset names and URLs, SHA-256 values, internal
  package identity, and runtime contract.
- **Manifest compatibility:** Bootstrap validation accepts the explicitly supported `development`
  and `alpha` channel values, validates a bounded safe GitHub Release tag rather than hardcoding an
  Alpha-only version pattern, and binds every asset URL and escaped filename to the exact manifest
  tag and commit.
- **Historical releases:** A moving channel manifest selects the latest package in that channel.
  Each previous-version command selects an immutable version manifest. Published assets are not
  overwritten or silently removed; a known-broken build may be labeled accordingly.
- **Promotion:** Development packages may come from issue branches for pre-merge testing. After
  landing, the exact green-`main` package receives the decisive rehearsal. Alpha promotion points
  to those identical tested assets and never rebuilds them.
- **Windows command:** The website exposes one copyable command block for each Windows package. It
  uses the PowerShell included with supported Windows, downloads the bootstrap before execution,
  and never requests an execution-policy or application-trust bypass.
- **Windows package:** The bootstrap verifies its selected manifest and all downloaded assets,
  installs a self-contained package for the current user, records the source channel and exact
  package identity, creates a normal Balls shortcut, and opens Balls on success.
- **Packaged startup:** Normal launch starts the loopback browser UI plus the private-network
  admission and Circle Files synchronization services needed by invitations. The normal path has
  no manual addresses, ports, daemon flags, runtime setup, or separate service setup.
- **Future update contract:** Balls will later check its installed channel quietly, notify through
  the GUI, download and verify only after the user chooses to update, preserve Circle state, and
  restart cleanly. It will not silently install or change channels. Only persistence of installed
  channel and package identity belongs to this issue.
- **Lab topology:** Use the existing Windows 11 Owner environment and the one already authorized
  2 GiB Windows desktop boss simulator. Do not create another VM or alter the working Owner VM's
  CPU, memory, disk, or GPU configuration for this issue. Keep every unrelated and GPU VM stopped.
- **Clean state:** A fresh OS installation is unnecessary. Use a dedicated clean Owner test profile
  and a dedicated Member profile with no prior Balls state, Node identity, Balls-created mapping,
  or Balls-managed credential. The Member profile is not a member of local Administrators.
- **Owner identity:** The Owner starts Balls unelevated. Only the existing narrow helper may cross
  the Windows consent boundary for the exact approved host operation.
- **Network:** Both Windows Nodes communicate through the private `windows_default` bridge. Do not
  add Tailscale, public exposure, or host-forwarded SMB. Host access to the two desktops remains
  loopback-only.
- **Demo folder:** Before Balls starts, create a disposable pre-populated local folder at
  `C:\BallsDemo\Projects` with a seed file. The Owner selects that existing folder through the real
  Windows picker. Do not contribute a host-mounted or network-backed path.
- **Owner contribution and grant:** The browser shows a concise mutation summary, obtains the
  required consent, contributes the exact selected folder, and lets the Owner select the human
  Member and `Read/write`. Balls derives all internal identifiers, plans, accounts, and credentials.
- **Invitation and persistence:** One private invitation carries or resolves the supported private
  connection information. The Member's guided Circle and Capability state persists in protected
  local Node state across closing and reopening the browser.
- **Member action:** The normal UI exposes one `Open shared folder in Explorer` action. Balls uses
  `P:` when free or another supported free letter, preserves unrelated mappings, applies the exact
  authorized mapping as the current user, and opens Explorer at the mapped root.
- **Error behavior:** Offline host, invalid or consumed invitation, provider mismatch, mapping
  collision, blocked package, failed mapping, and failed Explorer launch produce bounded,
  plain-language states without exposing secrets or requiring product CLI recovery.
- **Application trust:** A Windows policy block is reported as `BLOCKED`. Balls does not disable,
  evade, or weaken Smart App Control, application control, execution policy, or network protection.
- **Safety floor:** Preserve contributed files and unrelated shares, accounts, firewall rules,
  credentials, mappings, and user state. Keep private invitations and provider credentials out of
  packages, website metadata, logs, screenshots, issues, and verification records.
- **Acceptance claim:** The Owner personally operates both Windows environments. Passing evidence
  is explicitly same-host two-VM evidence and completes this issue; it is not a claim about a
  physical boss device or physical LAN.
- **Publication sequence:** Development publication and rehearsal proceed under the bounded
  authority above. After the decisive green-`main` rehearsal, work pauses for Owner approval before
  moving Alpha. The live Alpha command is then checked against the identical asset identity.

## Testing Decisions

- Tests assert visible behavior and durable contracts, not private class structure, incidental HTML,
  exact internal plans, or implementation-only identifiers.
- The highest product seam is one manual, checklist-driven, packaged two-Node Windows journey. The
  Owner personally operates both interactive desktops from the Omarchy laptop. The journey uses
  website commands, real packaged processes, protected persistent state, the private bridge, the
  Windows Circle Files provider, current-user mapping, real Explorer, and bidirectional file I/O.
- A fully automated two-desktop harness is not required. Automation of Windows consent UI, folder
  pickers, Explorer windows, and the entire second desktop is deferred until the product journey is
  stable enough to justify that maintenance cost.
- Existing domain, local API, browser component, Playwright, package, Canary, Windows provider, and
  prior two-Node Circle Files tests are the prior art. Extend those seams rather than creating a
  parallel product harness.
- Focused application and platform tests cover protected connection persistence, folder-picker
  cancellation, drive collision, provider mismatch, offline retry, Explorer-launch failure,
  package identity, channel persistence, secret redaction, exact ownership, and rollback.
- Browser tests cover the Owner contribution/grant journey, the Member join/reopen/open journey,
  human vocabulary, absence of infrastructure controls, progress, success, and actionable failure.
- Download-site tests cover Alpha-first layout, the Development warning, previous-version rows,
  the ten-Development-build display limit, immutable version commands, built-in Windows PowerShell
  compatibility, channel-specific manifests, Release URL enforcement, and identity validation.
- Native Windows focused checks cover self-contained installation, current-user location, shortcut
  creation, automatic first launch, packaged listeners, narrow Owner elevation, nonadministrator
  Member operation, mapping collision behavior, and Explorer launch.
- Iterative issue-branch Development packages may exercise incomplete steps. They do not become
  final evidence. After squash merge, create a Development package from green `main` and perform
  the full manual checklist against that exact identity.
- Before the manual run, record both Windows versions, VM identities, profile privilege levels,
  network boundary, absence of prior Balls state, unrelated mappings, package identity, and the
  seed file's identity and bytes.
- The manual checklist starts from the Development section of the live website; runs the copied
  command in both profiles; completes graphical Circle creation, contribution, invitation, grant,
  join, browser reopen, and open action; observes the exact Explorer root; and performs create,
  open, edit, rename, and delete operations visible from both Nodes.
- Record start and finish time, download time, every prompt, Owner elevation, retry, explanation,
  intervention, and limitation. Elapsed time is product evidence, not a hard automated or
  five-minute pass threshold.
- A manual step fails if product behavior is replaced by CLI setup, copied binaries, manual network
  or provider configuration, manual drive selection, an administrator-capable Member, or a mapping
  that does not open the correct Explorer root.
- A blocked application-control decision is recorded as `BLOCKED` and is never bypassed.
- Passing manual evidence is labeled `same-host two-VM`; it must not be relabeled as physical-device
  or physical-LAN evidence.
- After explicit Owner approval, Alpha readback verifies the live website command, immutable
  manifest, GitHub Release asset, tag, commit, filename, internal package identity, and SHA-256,
  then repeats Windows installation and startup. The complete Circle journey need not repeat when
  Alpha resolves to the exact already-tested assets.

## Out of Scope

- Creating, cloning, resizing, reconnecting, or otherwise redesigning the Windows VM lab.
- A fully automated two-Windows-desktop end-to-end harness.
- A physical boss-device, physical-LAN, or actual-boss usability claim.
- A graphical Windows installer, ZIP-based human workflow, Microsoft Store package, package-manager
  integration, code signing, or Windows application-policy bypass.
- Implementing the future background update checker, graphical updater, automatic installation, or
  channel switching.
- Balls Wizard, any local model download, shared Circle AI, or new Circle Messaging work.
- SSH, terminal capability integration, RDP, whole-computer integration, or remote administration.
- Member removal, cross-Capability revocation, or new Circle Files lifecycle/security architecture.
- Generalized Capability Provider infrastructure or additional storage providers.
- Revit certification, multi-user Revit worksharing, application compatibility matrices, file
  replication, offline synchronization, conflict resolution, version history, or backup.
- Multi-Anchor replication, public-internet Circle operation, enterprise identity, scale hardening,
  macOS/Linux packaging expansion, or broad public launch.

## Further Notes

The Development channel is a public testing lane, not a quality claim. It exists because all human
package testing must begin at `balls.wlkrlabs.com`; it does not weaken artifact identity or package
integrity requirements.

The current Alpha predates Circle Files, the site requires PowerShell 7, and the earlier assisted
physical pilot used CLI mapping rather than the complete browser action. Those are gaps this issue
must cross, not prior evidence that it already passes.

The time pressure makes scope control decisive. If a blocker does not prevent the exact
website-command-to-Explorer journey or violate its safety floor, it does not enter this issue.
