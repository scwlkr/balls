# Current State

**Updated:** 2026-08-26

This is the compact execution entry point. Read [`AGENTS.md`](../AGENTS.md), this file, and the one
active issue before loading deeper design or verification records.

## Urgent outcome

The owner needs one boss-visible private Windows workflow working today:

```text
balls.wlkrlabs.com
  → paste one Windows install command
  → paste one private Circle invitation
  → join in the local browser
  → open the approved folder in File Explorer
  → edit a real ordinary work file
```

The install command runs in the PowerShell included with Windows. The user should not configure
PowerShell or runtimes and should not handle daemon flags, IP addresses, ports, SMB passwords,
object IDs, plan tokens, provider language, or manual drive selection.

## Active frontier

**Milestone:** [Private Boss Demo](https://github.com/scwlkr/balls/milestone/8)

**Only active issue:**
[#92 — Deliver the private boss demo from official download to shared Explorer file](https://github.com/scwlkr/balls/issues/92)

**Executable specification:**
[`specs/private-boss-demo-v1.md`](specs/private-boss-demo-v1.md)

Do not create or begin another feature issue until #92 is observed end to end or a concrete blocker
is split from it.

## Private-pilot delivery posture

Balls currently serves approximately two or three personally trusted people over a private LAN or
owner-managed Tailscale network. Prioritize the working end-to-end product, simple human workflows,
and rapid feedback over security architecture for hypothetical scale or public-internet threats.

Preserve the narrow safety floor: never bypass operating-system protections, expose private
services publicly, mishandle credentials, delete user data, grant unapproved machine access, or
weaken existing provider security. Additional security work requires a concrete pilot risk,
observed failure, or accepted release requirement.

## Usable implementation to preserve

Current `main` contains substantial reusable work:

- native Windows/Linux `ballsd`, typed CLI, protected local state, and local React workspace;
- signed single-use Circle invitations and authenticated two-Node LAN admission;
- protected invitation-derived Member connection state that survives browser and daemon relaunch;
- persisted Members, Nodes, and one minimal Circle message;
- Owner-authorized Circle Files contributions and Member Access Grants;
- a narrow Windows helper for the owned encrypted SMB share and limited per-Member credentials;
- Member-only grant synchronization and a guided browser action that selects `P:` or another free
  drive without exposing the SMB password;
- generation-bound Files revocation and ownership-proven server cleanup;
- real private-LAN two-computer ordinary-file create/read/edit/rename/delete evidence.

Reuse this machinery where it shortens #92. Do not expand it for hypothetical future requirements.

## Known blockers in the human journey

- `balls.wlkrlabs.com` currently points to published `0.3.0-alpha.1`, which predates Circle Files.
- The website has no warned Development section, immutable previous-version commands, or built-in
  Windows PowerShell bootstrap; its current Windows lane requires PowerShell 7.
- The self-contained Canary launcher does not automatically configure the private-LAN admission and
  Circle Files synchronization listeners required by browser invitations.
- The Owner still creates, hosts, grants, and provisions a folder through ID- and plan-heavy CLI
  choreography rather than one graphical flow.
- Real Windows mapping was previously triggered through the CLI; the full browser click under a
  genuine standard user remains unobserved.
- The current button maps the drive but does not itself open File Explorer.
- Published Windows packages are unsigned; do not bypass Windows application-control policy.

Issue #92 owns only the smallest fixes and evidence needed to cross these blockers.

## Product reset boundary

The former `0.4.0-alpha.1 — LAN Circle Files` milestone and issue #62 were closed as superseded on
2026-08-26 without a release. Completed issues and verification records remain historical evidence;
they are not deleted and they no longer define the active roadmap.

The next product gate, after #92, is one joined Member using Circle Files, Circle Messaging, and
Circle AI hosted by another Node, followed by coherent access revocation. Balls Wizard is a
separate optional local guide and is not on today's critical path. See
[`ADR 0009`](decisions/0009-reset-around-private-shared-ecosystem-proof.md) and the
[`roadmap`](../ROADMAP.md).

## Distribution boundary

[`balls.wlkrlabs.com`](https://balls.wlkrlabs.com) is the official human-facing software and update
entrypoint. Alpha remains the primary recommended package. A lower Development section may point to
incomplete or broken immutable GitHub prereleases and list older exact versions; it does not host
manually copied binaries. Active issue work may publish Development after package-integrity checks
and must record the prior pointer. Alpha, Beta, and Stable promotion requires separate Owner
approval. See [`ADR 0010`](decisions/0010-public-development-download-channel.md).

## Issue #92 acceptance lab

Use the existing Windows 11 Owner environment plus `balls-issue61-provider-desktop`; do not create
two new VMs. Keep every other historical and GPU VM stopped, use dedicated clean Owner and
nonadministrator Member profiles, and carry Circle traffic only over `windows_default`. The Owner
personally performs both sides of the manual product journey. Passing evidence is same-host two-VM
evidence and completes #92 without making a physical-device claim. Follow the
[`manual checklist`](verification/private-boss-demo-v1-checklist.md).

## Continue

1. Work only #92.
2. Read and preserve the accepted boundaries in the executable specification.
3. Reproduce the current official-download-to-boss journey before changing it.
4. Implement the smallest missing graphical path and packaging changes.
5. Run focused automated tests, then the checklist-driven two-VM Windows journey.
6. Record exact elapsed time, interventions, artifact identity, and limitations.
7. After the exact green-`main` Development package passes, stop for Owner approval before moving
   Alpha to the identical assets.
