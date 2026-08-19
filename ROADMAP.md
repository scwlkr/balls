# Roadmap

## Purpose

This is the compact delivery index. The deeper files-first program, milestone boundaries, and
candidate ticket maps live in [`docs/roadmap/files-first-v1.md`](docs/roadmap/files-first-v1.md).
Current execution state lives in [`docs/STATE.md`](docs/STATE.md) and GitHub Issues.

The roadmap protects both the long-term Circle vision and the need to ship small, useful releases.
Future milestones describe outcomes, not promises that every detail is already designed.

## Current checkpoint

`0.1.0-alpha.1` is implemented locally on Windows. It proves Core/Protocol/daemon/CLI boundaries,
persistent Node and Circle identity, protected SQLite state, Circle creation, participant listing,
and same-user HTTP/JSON control over a named pipe. Phase 1 is not complete: secure admission,
Node-to-Node communication, Unix runtime support, and persistent Circle communication remain.

## Files-first path to v1

| Target | Outcome | State |
| --- | --- | --- |
| `0.1.0-alpha.2` | **Open and Fast Foundation** — public-readiness, compact agent context, fast test lanes, issue workflow, and canary artifacts | Active |
| `0.2.0-alpha.1` | **Cross-platform Node and Web UI** — real Windows/Linux daemon and CLI plus the local TypeScript browser shell | Planned |
| `0.3.0-alpha.1` | **Trusted Circle** — invitation, authenticated membership/transport, two virtual Nodes, and one minimal persistent message | Planned |
| `0.4.0-alpha.1` | **LAN Circle Files** — secure contributed Windows folder mapped in Explorer for two Members | Planned |
| `0.5.0-alpha.1` | **Operable Remote Files** — Tailscale path, existing-folder adoption, repair, revocation, installer, and updates | Planned |
| `0.6.0-beta.1` | **Company Pilot** — the accepted candidate is used by the owner and one coworker | Planned |
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
