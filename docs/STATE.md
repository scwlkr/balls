# Current State

**Updated:** 2026-08-28

This is the compact execution entry point. Read [`AGENTS.md`](../AGENTS.md), this file, and the one
active issue before loading deeper design or verification records.

## Achieved private-boss-demo outcome

The boss-visible private Windows workflow was completed through #92 and #103:

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

**Active process initiative:**
[#120 — Make Linux the development authority with CLI-first Windows conformance](https://github.com/scwlkr/balls/issues/120)

**In-progress lanes:**

- [#118 — Install and use the local Balls Wizard product guide](https://github.com/scwlkr/balls/issues/118)
  in [PR #119](https://github.com/scwlkr/balls/pull/119).

[#122](https://github.com/scwlkr/balls/issues/122) remains blocked by #118.
[#124 — Provision and remove one Circle Files host headlessly from Linux](https://github.com/scwlkr/balls/issues/124)
is in progress in the completed #123 conformance lane. The Linux fast gate passes at
`b9d709f2bf95`. The authorized disposable Windows target refused before host mutation because its
high-integrity SSH administrator cannot use CurrentUser DPAPI, while its DPAPI-capable standard
product identity is not an administrator. Cleanup was independently confirmed; only the
harness-created 28-byte seed fixture initially remained, then its exact path, sole-file inventory,
and expected hash were reverified before the fixture was removed and the path restored to absent.
Do not change accounts, UAC, credentials, DPAPI, or policy to force a pass. Its implementation
remains incomplete until the exact final commit passes one
authorized native Windows run on a target whose administrative product token can start the normal
daemon and launch the typed helper, proving product-driven provision, rollback, retry, removal, and
seed-byte preservation. Preserve the
two-lane limit.

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

Reuse this machinery where it shortens #92. Do not expand it for hypothetical future requirements.

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

1. Implement #124 using #123's target, transport, identity, timeout, cleanup, redaction, and
   evidence contracts.
2. Let PR #119 complete independently, then unblock #122 without interrupting the #124 lane.
3. Close #120 only after #122 and #124 land with their required evidence.
