# Roadmap

## Purpose

This roadmap is an outcome index, not an architecture queue. Current execution state lives in
[`docs/STATE.md`](docs/STATE.md), and GitHub Issues contain only the smallest active frontier.

The 2026-08-26 product reset preserves the implemented Circle and Circle Files foundation but
replaces the files-only release sequence. Balls now proves the working private human experience
before expanding the platform. See
[`ADR 0009`](docs/decisions/0009-reset-around-private-shared-ecosystem-proof.md).

## Completed — Private Boss Demo

**User outcome:** the Owner proves the boss journey across the two authorized Windows environments
on the current Omarchy laptop. Each side installs Balls from the official website command, the
simulated boss joins through the local browser, opens the approved project folder in File Explorer,
and edits a real ordinary work file.

```text
balls.wlkrlabs.com
  → paste one Windows install command
  → join Circle
  → open shared folder
  → work
```

**Human boundary:** one copied command in the PowerShell included with Windows is acceptable. No
PowerShell configuration, runtime setup, daemon flags, IP addresses, ports, SMB credentials, object
IDs, plan tokens, provider jargon, or manual drive-letter selection. An explicit Windows elevation
prompt is acceptable for the exact Owner-side host mutation that requires it.

**Environment:** approximately two or three personally trusted people over a private LAN or
owner-managed Tailscale network.

**Evidence:** exact Development and accepted Alpha identities; website channel readback; clean
dedicated Owner and nonadministrator Member profiles; real graphical invitation/join/map/open;
ordinary two-way file editing; elapsed time and every intervention; no weakened Windows protection
or public service exposure. The Owner personally performs both roles, and the result is labeled
same-host two-VM rather than physical-device evidence.

**Completed issues:**
[#92 — Deliver the private boss demo from official download to shared Explorer
file](https://github.com/scwlkr/balls/issues/92) and
[#103 — Promote the rehearsed assets to Alpha and verify live
startup](https://github.com/scwlkr/balls/issues/103).

**Executable specification:**
[`docs/specs/private-boss-demo-v1.md`](docs/specs/private-boss-demo-v1.md).

**Non-goals:** Balls Wizard, shared Circle AI, rich messaging, SSH/RDP, generalized providers,
multi-Anchor replication, public-internet exposure, speculative security architecture, and broad
release-matrix expansion.

## Now — Revit Server Rapid Setup v0

**User outcome:** after the approved Alpha promotion closes the Private Boss Demo, the Owner uses
one exact Balls Development build inside a prepared Windows Server 2022 VM to set up Autodesk Revit
Server 2027 Host+Admin and receive a plain healthy result in under 30 minutes.

**Human boundary:** the Owner uses one Balls setup page, one Balls-owned Windows elevation, and
Autodesk's own signed installer consent plus graphical license/configuration step. The Owner does
not manually install IIS roles, edit ACLs, configure the Revit Server role variable, write
`RSN.ini`, inspect IIS, or diagnose provider services.

**Environment:** one isolated disposable Windows Server 2022 Desktop Experience VM hosted on the
current Linux laptop, with the OS prepared and official Autodesk media cached before timing. The VM
may show a setup console during installation and runs headlessly afterward. This unlisted QEMU/KVM
lab proves the Balls workflow, not final Autodesk-supported production operation.

**Evidence:** exact Balls and Autodesk artifact identities; computed and approved setup plan;
Host+Admin with no Accelerator; version-isolated storage; required IIS applications and app pool;
server-local `RSN.ini`; working Administrator page; no public exposure; elapsed wall time under 30
minutes; portable setup template and redacted receipt.

**Non-goals:** Revit client or model operations, synchronization, concurrency, Tailscale, remote
performance, Circle-wide authorization, Circle Files, backup, restore, production hardware,
Windows Service/Anchor work, or polished final UI.

**Executable specification:**
[`docs/specs/revit-server-rapid-setup-v0.md`](docs/specs/revit-server-rapid-setup-v0.md).

**Issue chain:**
[#114 — readiness and exact setup plan](https://github.com/scwlkr/balls/issues/114) ->
[#115 — install and verify Host+Admin](https://github.com/scwlkr/balls/issues/115) ->
[#116 — export handoff and prove setup under 30 minutes](https://github.com/scwlkr/balls/issues/116).
Only the unblocked frontier issue is active.

See [`ADR 0018`](docs/decisions/0018-prioritize-revit-server-rapid-setup.md). The Shared Ecosystem
Proof remains the next product gate after this bounded initiative.

## Then — Shared Ecosystem Proof

**User outcome:** after one installation and one Circle invitation, one Member can:

1. use an approved Circle Files folder;
2. send and receive Circle Messaging;
3. use Circle AI running on another approved Node.

The Member handles no provider address, credential, or machine configuration. One Member-removal
intent stops future authorization for all three and reports provider cleanup honestly.

**Product proof:** capabilities supplied by different computers and providers feel like one shared
Circle rather than three unrelated tools. If this journey is not materially easier than assembling
existing products, platform expansion pauses and the product is reconsidered.

**Non-goals:** arbitrary terminal access, RDP, general app orchestration, distributed inference,
automatic multi-Anchor failover, and broad public release.

Create executable issues only after the Private Boss Demo is observed and its friction is recorded.
The accepted cross-stage product contract is
[`docs/specs/private-shared-ecosystem-v1.md`](docs/specs/private-shared-ecosystem-v1.md); its later
stages are not implementation authority until they receive their own executable specifications.

## Optional product-guide lane — Balls Wizard

Balls Wizard is an optional on-device guide, not shared Circle AI and not a dependency of the boss
demo. The local browser offers a bottom-right download prompt; the user explicitly chooses whether
to install it. Balls then retrieves a pinned quantized instruction-tuned Gemma 4 E2B artifact and
integrates it without requiring model or runtime setup.

The character is a floating brand-violet ball wearing a wizard hat, derived from the canonical
[`balls-brand.png`](balls-brand.png) visual language. It retrieves version-matched user
documentation, cites the relevant guidance, and begins as read-only product help. Balls itself must
remain fully usable when Balls Wizard is absent or unsupported by the local hardware.

This lane starts only after the boss demo unless a tiny non-model visual placeholder directly helps
that demo.

## Later horizons

After the shared-ecosystem proof:

1. make the same experience operable across owner-managed Tailscale without Member setup;
2. add Circle Apps and typed approved actions;
3. add separately permissioned SSH/RDP integrations for technical users when evidence requires them;
4. improve resilient Circle Files, messaging, and multiple-Anchor behavior;
5. expand Circle AI providers and approved context;
6. add honest workload scheduling and Circle Compute.

## Roadmap rules

Every milestone must state:

1. the visible human outcome;
2. the time or intervention budget;
3. the smallest technical capability needed for that outcome;
4. explicit non-goals;
5. exact observed evidence;
6. the accepted artifact and distribution path, when software leaves the development machine.

Do not start a security, architecture, provider, or cross-platform expansion unless it protects or
unblocks the active outcome. Development publication follows the bounded authority in
[`ADR 0010`](docs/decisions/0010-public-development-download-channel.md); Alpha, Beta, and Stable
promotion remains separately Owner-gated.

## Historical roadmap

The former files-first v1 program and unpublished `0.4.0-alpha.1` milestone are historical. Their
implementation and verification remain valid evidence, but they no longer define current execution.
See [`docs/roadmap/files-first-v1.md`](docs/roadmap/files-first-v1.md).
