# Roadmap

## Purpose

This is the compact delivery index. The deeper files-first program, milestone boundaries, and
candidate ticket maps live in [`docs/roadmap/files-first-v1.md`](docs/roadmap/files-first-v1.md).
Current execution state lives in [`docs/STATE.md`](docs/STATE.md) and GitHub Issues.

The roadmap protects both the long-term Circle vision and the need to ship small, useful releases.
Future milestones describe outcomes, not promises that every detail is already designed.

## Immediate company priority

The current priority is one privately verified, two-person company workflow: create a new
Windows-hosted project folder, invite a trusted coworker over the private LAN, and make the
approved folder usable on that coworker's separate Windows computer through File Explorer.

Existing-folder adoption, remote/Tailscale access, general-purpose providers, AI, Apps, and other
future integrations must not delay this first useful company outcome. The source remains open and
the broader Circle architecture remains intact.

## Current checkpoint

[`0.2.0-alpha.1`](https://github.com/scwlkr/balls/releases/tag/0.2.0-alpha.1) is published: the
same native daemon, structured CLI, protected state, local IPC, and hardened React workspace run
on Windows and Linux. [`0.3.0-alpha.1`](https://github.com/scwlkr/balls/releases/tag/0.3.0-alpha.1)
Trusted Circle is also published. Its remote v1 identity/admission security design, protected
production credentials, bounded single-use invitations, authenticated LAN transport, persisted
two-Node Circle admission, and one persistent Circle message passed exact-artifact independent
and shared verification. The executable LAN Circle Files successor includes completed #56–#61,
the real two-person onboarding outcome in #73, and the final acceptance gate in #62.

In parallel, [#48](https://github.com/scwlkr/balls/issues/48) establishes an Apple-Silicon,
source-run macOS developer Node and required Mac fast lane. This accelerates portable, browser,
brand, and macOS compatibility work without expanding the supported files-first v1 platform claim
or introducing a native Mac GUI. A physical Mac joining client has now joined a Windows-anchored
Circle over private LAN, persisted one message on both Nodes, and retained the outcome across both
daemon restarts; a Mac Anchor/listener remains outside the claim.

## Files-first path to v1

| Target | Outcome | State |
| --- | --- | --- |
| `0.1.0-alpha.2` | **Open and Fast Foundation** — public-readiness, compact agent context, fast test lanes, issue workflow, and canary artifacts | Published |
| `0.2.0-alpha.1` | **Cross-platform Node and Web UI** — real Windows/Linux daemon and CLI plus the local TypeScript browser shell | Published |
| `0.3.0-alpha.1` | **Trusted Circle** — invitation, authenticated membership/transport, two virtual Nodes, and one minimal persistent message | Published |
| `0.4.0-alpha.1` | **LAN Circle Files** — privately verified two-person company onboarding and secure Windows Explorer folder access | Active |
| `0.5.0-alpha.1` | **Operable Remote Files** — Tailscale path, existing-folder adoption, repair, revocation, installer, and updates | Planned |
| `0.6.0-beta.1` | **Company Pilot** — formal supported Beta after the immediate two-person LAN pilot is already useful | Planned |
| `1.0.0` | **Public Files Release** — a supported files-first Circle product | Planned |

## v1 boundary

The supported v1 outcome is focused:

- create and join a Circle;
- see Members and Nodes;
- run native Windows and Linux Nodes;
- use the local browser UI and first-class CLI;
- contribute one Windows folder to a Circle;
- map and edit it through Windows File Explorer;
- grant, revoke, repair, install, update, and remove access safely;
- use LAN first and Tailscale as an optional remote transport provider.

One Anchor may be authoritative in v1, without automatic failover. Circle Files v1 is one live
folder on one Node: no replication, offline sync, conflict merging, version history, or
Balls-managed trash. Rich messaging, multiple Anchors, macOS polish, Circle AI, Circle Apps,
distributed storage, and Circle Compute remain later work.

## Later horizons

After the files-first release, grow the same Circle foundation into:

1. rich chat, durable history, roles, and multiple Anchors;
2. macOS and headless/VPS polish;
3. resilient and replicated Circle Files;
4. Circle AI;
5. Circle Apps and services;
6. honest distributed workloads and Circle Compute.

## Roadmap rule

Every milestone must state:

1. the useful user outcome;
2. the architectural capability proved;
3. explicit non-goals;
4. the smallest honest automated or environment-specific evidence;
5. the exact artifact promoted to the next release channel.
