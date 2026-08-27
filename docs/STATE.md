# Current State

**Updated:** 2026-08-27

This is the compact execution entry point. Read [`AGENTS.md`](../AGENTS.md), this file, and the one
active issue before loading deeper design or verification records.

## Urgent outcome

The Owner needs one fast, truthful Revit Server setup workflow working in the disposable Windows
Server lab:

```text
open Balls Development
  → inspect Ready / Blocked
  → approve Host+Admin / no-Accelerator plan
  → let Balls prepare Windows
  → complete Autodesk's graphical terms and configuration
  → verify healthy services and Administrator
  → export the boss handoff in under 30 minutes
```

This proves installation health only. It does not prove Revit client/model use, synchronization,
remote access, backup/recovery, production hardware, or an Autodesk-supported hypervisor.

## Active frontier

**Initiative:** Revit Server Rapid Setup v0

**Only ready issue:**
[#114 — Show Revit Server 2027 readiness and an exact setup
plan](https://github.com/scwlkr/balls/issues/114)

**Blocked chain:**
[#115 — Install and verify Revit Server 2027 Host+Admin](https://github.com/scwlkr/balls/issues/115),
blocked by #114; then
[#116 — Export the Revit Server handoff and prove setup under 30
minutes](https://github.com/scwlkr/balls/issues/116),
blocked by #115.

**Executable specification:**
[`specs/revit-server-rapid-setup-v0.md`](specs/revit-server-rapid-setup-v0.md)

Work only the unblocked frontier. Do not begin #115 or #116 early or create a parallel feature
issue.

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
- one graphical Owner flow that chooses a contributed folder and joined human Member, previews
  Read/write access, and applies the signed grant plus narrow Windows credential without exposing
  provider, account, credential, endpoint, object-ID, or plan details;
- a narrow Windows helper for the owned encrypted SMB share and limited per-Member credentials;
- Member-only grant synchronization and one guided browser action that selects `P:` or another
  free drive, maps the exact current-user grant, and opens its root in File Explorer without
  exposing the SMB password or mapping details;
- generation-bound Files revocation and ownership-proven server cleanup;
- real private-LAN two-computer ordinary-file create/read/edit/rename/delete evidence.

Reuse this machinery only where it shortens #114-#116. Keep Revit setup's Windows Server gate
separate from the existing Circle Files/SMB readiness policy.

## Rehearsed human journey

- The immutable `development-20260827T045203Z-39cd15e5ffdf` package is the exact green-`main`
  candidate for Alpha.
- The Owner confirmed normal shortcut launch, the graphical contribution and grant flow, and the
  preserved pre-existing file in `C:\BallsDemo\Projects`.
- A genuine nonadministrator Member confirmed normal shortcut launch, the one-action Explorer open,
  reading the pre-existing file, and creating, renaming, and deleting a disposable file with the
  same result visible on the Owner.
- This is same-host two-VM evidence over the current nested-NAT lab, not a physical-device claim.
- The final gate is to move only the Alpha pointer to those exact assets, read it back, and verify
  normal startup. Published Windows packages remain unsigned; do not bypass Windows
  application-control policy.

## Product reset boundary

The former `0.4.0-alpha.1 — LAN Circle Files` milestone and issue #62 were closed as superseded on
2026-08-26 without a release. Completed issues and verification records remain historical evidence;
they are not deleted and they no longer define the active roadmap.

After the bounded Revit Server Rapid Setup chain, the next product gate is one joined Member using
Circle Files, Circle Messaging, and Circle AI hosted by another Node, followed by coherent access
revocation. Balls Wizard is a separate optional local guide and is not on today's critical path. See
[`ADR 0009`](decisions/0009-reset-around-private-shared-ecosystem-proof.md) and the
[`roadmap`](../ROADMAP.md).

## Distribution boundary

[`balls.wlkrlabs.com`](https://balls.wlkrlabs.com) is the official human-facing software and update
entrypoint. Alpha remains the primary recommended package. A lower Development section may point to
incomplete or broken immutable GitHub prereleases and list older exact versions; it does not host
manually copied binaries. Active issue work may publish Development after package-integrity checks
and must record the prior pointer. Alpha, Beta, and Stable promotion requires separate Owner
approval. See [`ADR 0010`](decisions/0010-public-development-download-channel.md).

## Revit Server acceptance lab

Use one new isolated Windows Server 2022 Desktop Experience VM on the Linux laptop's existing
Docker/QEMU/KVM stack. Keep existing high-memory Windows VMs stopped, use separate storage, disks,
network identity, and loopback console ports, and never modify an existing VM or place company/model
data in the lab. A graphical console is allowed for Windows and Autodesk setup; normal operation may
be headless. Follow the [`manual checklist`](verification/revit-server-rapid-setup-v0-checklist.md)
and update [`windows-development-lab.md`](windows-development-lab.md) before operating the new VM.

Issue #114's source implementation provides the separate read-only Windows inspection, session-bound
native media selection, local-control/browser application path, and exact digest-bound preview. Its
deterministic focused tests pass, but the disposable Windows Server graphical and no-mutation risk
gate is `NOT RUN`; no Revit installation or health is claimed. See the
[dated verification record](verification/2026-08-27-revit-server-readiness-preview.md).

## Continue

1. Complete #114's read-only Windows Server/media inspection and graphical setup preview.
2. Remove #115's blocked label, add `ready-for-agent`, and implement the approved mutation,
   Autodesk handoff, and health verification path.
3. Remove #116's blocked label, add `ready-for-agent`, and implement the portable bundle plus exact
   Development-package timed proof.
4. Record the exact passing claim and limitations, then return to the Shared Ecosystem Proof.
