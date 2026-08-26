# Current State

**Updated:** 2026-08-26

This is the compact execution entry point. Read [`AGENTS.md`](../AGENTS.md), this file, and the one
active issue before loading deeper design or verification records.

## Urgent outcome

The owner needs one boss-visible private Windows workflow working today:

```text
balls.wlkrlabs.com
  → install Balls
  → paste one private Circle invitation
  → join in the local browser
  → open the approved folder in File Explorer
  → edit a real ordinary work file
```

The boss should not use PowerShell, daemon flags, IP addresses, ports, SMB passwords, object IDs,
plan tokens, provider language, or manual drive selection.

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
- The self-contained Canary launcher does not automatically configure the private-LAN admission and
  Circle Files synchronization listeners required by browser invitations.
- The Owner still creates, hosts, grants, and provisions a folder through ID- and plan-heavy CLI
  choreography rather than one graphical flow.
- Real Windows mapping was previously triggered through the CLI; the full browser click under a
  genuine standard user remains unobserved.
- Invitation-derived connection information is kept in browser session storage, so the guided path
  may fall back to technical mapping controls after a fresh browser session.
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
entrypoint. It points to exact owner-accepted GitHub Release assets; it does not host manually copied
binaries. A public tag, GitHub Release, or channel-pointer change requires separate owner approval
immediately before publication.

## Continue

1. Work only #92.
2. Read and preserve the accepted boundaries in the executable specification.
3. Reproduce the current official-download-to-boss journey before changing it.
4. Implement the smallest missing graphical path and packaging changes.
5. Run focused tests plus the risk-triggered Windows journey.
6. Record exact elapsed time, interventions, artifact identity, and limitations.
7. Stop for owner approval immediately before any public tag, Release, or download-channel change.
