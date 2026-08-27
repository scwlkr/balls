# AGENTS.md

## Purpose

This repository builds **Balls**, an open-source platform for trusted Circles.

## Private pilot delivery posture

Balls currently serves approximately two or three personally trusted people over a private LAN or
an owner-managed Tailscale network. Prioritize a working end-to-end product, simple human
workflows, and rapid feedback over security architecture for hypothetical scale or public-internet
threats.

Preserve a narrow safety floor: never bypass operating-system protections, expose private services
publicly, mishandle credentials, delete user data, grant unapproved machine access, or weaken
existing provider security. Add further controls only for a concrete pilot risk, observed failure,
or accepted release requirement. Security work that does not protect the active private-pilot
journey must not displace delivery of that journey.

The urgent active outcome is the
[Private Boss Demo](https://github.com/scwlkr/balls/issues/92): official download, graphical Circle
join, and usable shared File Explorer access across the two authorized Windows environments on the
current Omarchy laptop. Balls Wizard, Circle AI, richer messaging, generalized providers, and
further security architecture are off that issue's critical path.

Before implementing that outcome, read
[`docs/specs/private-boss-demo-v1.md`](docs/specs/private-boss-demo-v1.md). The broader accepted
product contract is
[`docs/specs/private-shared-ecosystem-v1.md`](docs/specs/private-shared-ecosystem-v1.md), but its
later slices are not implementation authority.

## Start with current state

For ordinary ticket work, read only:

1. this file;
2. [`docs/STATE.md`](docs/STATE.md);
3. the active GitHub Issue or pull request;
4. the specific design, contract, security, or decision documents linked by that issue.

Read the complete product canon when planning a milestone or changing product meaning,
architecture, trust boundaries, public protocols, or durable state:

1. `VISION.md`
2. `PRODUCT.md`
3. `PRINCIPLES.md`
4. `ARCHITECTURE.md`
5. `GLOSSARY.md`
6. `DECISIONS.md`
7. `ROADMAP.md`
8. `LEGACY.md`

## Protect these truths

- Circle is the top-level object.
- People are first-class; Nodes support Circles.
- Resource contribution is explicit.
- One ordinary PC must not permanently define the Circle.
- LAN/offline capability matters.
- Cross-platform support is architectural.
- Open source and eventual self-hostability matter.
- Circle AI and Circle Apps remain real long-term pillars.
- Distributed compute must be technically honest.
- Simple UX and technical inspectability should coexist.

Do not reinterpret Balls as merely a server manager, file-sharing utility, homelab dashboard,
Discord clone, or self-hosted Google Workspace clone. Those can be pieces of the system; the
product is the Circle.

## Visual direction

Before any brand or product-UI work, inspect [`balls-brand.png`](balls-brand.png). It is the
canonical visual reference; derive new visual assets from it instead of inventing a parallel look.

## Architecture discipline

Prefer:

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

- Keep platform commands and types out of Core.
- Keep `ballsd` native; WSL is a development or workload provider, not the product foundation.
- Keep Tailscale a transport provider and SMB a Circle Files provider.
- Keep ordinary processes unelevated; isolate typed privileged operations behind a narrow helper.
- Keep the CLI and browser UI as clients of the same application behavior.

## Delivery discipline

- Follow the active milestone and next ready issue in `docs/STATE.md`.
- Build one independently mergeable vertical outcome per issue.
- Use mainstream, stable, typed, well-documented tools unless evidence justifies an exception.
- Preserve unrelated work and keep no more than two non-overlapping tickets in progress.
- Use a short-lived `codex/` branch, one issue per pull request, squash merge, and delete the branch.
- After a verified merge, take the next unblocked issue without routine owner check-ins.
- Distribute software and updates only through [balls.wlkrlabs.com](https://balls.wlkrlabs.com)
  using immutable GitHub Release assets. Follow
  [`web/Balls.Downloads/README.md`](web/Balls.Downloads/README.md). An active issue may publish a
  warned Development prerelease after package-integrity checks and must record the prior pointer;
  Alpha, Beta, and Stable promotion remains owner-gated.
- Stop for Alpha/Beta/Stable publication, secrets, spending, irreversible actions, or a material
  product/security decision not already recorded.

Every slice needs a user outcome, typed contracts, practical automated tests, explicit non-goals,
and current documentation. Evidence must say what was actually observed; physical-machine proof
is opportunistic unless an issue explicitly requires it.

## Verification

Use the smallest honest gate from [`docs/development-process.md`](docs/development-process.md).
Fast checks belong on every pull request. Expensive VM, installer, migration, recovery, UI, or
multi-machine checks run only for a release candidate or a change that touches the corresponding
risk. Never weaken checks merely to obtain green status, and never claim an unobserved scenario.

## Tight private-pilot iteration

For active private-pilot product work, use a tight local/Windows-VM loop: focused test, locally or
VM-built package, VM install, observed Owner/Member behavior, repeat. Verify the live guest and
topology before issuing commands. At a mergeable checkpoint, run the full local fast gate once,
one required PR CI cycle, and one exact-main Canary/release cycle only when distribution risk
changed. GitHub CI certifies a completed iteration; it is not the inner development loop. Keep
detailed commands and exceptional mechanics in the development-process or Windows-lab runbook.

For Windows VM automation, manual two-VM acceptance, unsigned UI or installer execution, Canary
checks, or lab recovery, read
[`docs/windows-development-lab.md`](docs/windows-development-lab.md) before acting.

## Prior research

`scwlkr/balls-server` is archived prior research. Inspect it only for a specific
Windows/networking/security problem. Port concepts deliberately; do not transplant its
server-first architecture.
