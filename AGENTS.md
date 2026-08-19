# AGENTS.md

## Purpose

This repository is the fresh start for **Balls**.

Before writing production code, read:

1. `VISION.md`
2. `PRODUCT.md`
3. `PRINCIPLES.md`
4. `ARCHITECTURE.md`
5. `GLOSSARY.md`
6. `DECISIONS.md`
7. `ROADMAP.md`
8. `LEGACY.md`

These documents are the source of truth for project intent.

## Critical instruction

**Do not reinterpret Balls as merely a server manager, file-sharing utility, homelab dashboard, Discord clone, or self-hosted Google Workspace clone.**

Those can be pieces of the system.

The product is the **Circle**: a trusted digital environment whose people and approved infrastructure become more powerful together.

## Protect these product truths

- Circle is the top-level object.
- People are first-class.
- Nodes support Circles.
- Resource contribution is explicit.
- One ordinary PC must not define the Circle.
- LAN/offline capability matters.
- Cross-platform support is architectural.
- Open source and eventual self-hostability matter.
- Circle AI and Circle Apps are real long-term pillars.
- Distributed compute must be technically honest.
- Simple UX and technical inspectability should coexist.

## Architecture discipline

Prefer dependency direction:

```text
UI / CLI / integrations
        ↓
    local API
        ↓
      ballsd
        ↓
    core/domain
        ↓
platform contracts
        ↓
OS adapters
```

Platform-specific shell commands must not leak into core/domain logic.

Do not run the whole product as Administrator/root.

Do not define Balls around WSL.

Do not define Balls around Tailscale.

Do not define Circle Files around SMB.

Those may all be useful providers.

## Development style

Build small vertical slices.

Every slice should have:

- a real user outcome;
- typed contracts;
- automated tests where practical;
- real-machine verification when OS/network behavior matters;
- explicit non-goals;
- docs updated when product behavior changes.

Avoid speculative subsystems that are not required by the active milestone.

## First implementation objective

Start with **Phase 1 — First Circle** from `ROADMAP.md`.

The first architectural proof should establish:

- Balls Core;
- Protocol contracts;
- `ballsd`;
- `balls` CLI;
- persistent Node identity;
- persistent Circle identity;
- create/join Circle flow;
- two-machine membership;
- basic Member/Node listing;
- one simple persistent Circle message path.

Use Windows as the first real environment while keeping the boundaries required for future Linux/macOS Nodes.

Do not begin with AI, apps, distributed compute, or a universal filesystem.

## Use of the old prototype

`scwlkr/balls-server` is prior research.

Inspect it only when a specific implementation problem could benefit from its Windows/networking/security work.

Port concepts deliberately.

Do not transplant the old architecture wholesale.

## When uncertain

If a technical choice is unclear:

1. preserve product principles;
2. prefer a small prototype;
3. record the decision;
4. avoid turning a temporary implementation into a permanent product definition.

## Owner intent

The owner's long-term dream is that a trusted group can invite people and machines into a Circle and gain a private, customizable workspace for communication, data, AI, applications, services, and collective computing power.

Do not optimize that dream out of the project while making the first version smaller.
