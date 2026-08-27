# Decisions and Open Questions

This file is the compact index of current product decisions, technical recommendations, and open
questions. Dated implementation state belongs in [`docs/STATE.md`](docs/STATE.md); hard-to-reverse
trade-offs belong in [`docs/decisions/`](docs/decisions/).

## Confirmed product decisions

### Core product

Balls is an open-source platform for trusted Circles. A Circle is a graphical shared computing
environment where people discover and use explicitly contributed capabilities from the group's
computers without configuring the underlying machines or providers.

The Circle is the product. People join Circles; Nodes support them. Joining establishes Membership,
not blanket machine access or automatic contribution. Circle policy produces explicit Capability
Grants that Balls realizes through integrated providers.

### Immediate private pilot

The urgent proving ground is approximately two or three personally trusted people on a private LAN
or owner-managed Tailscale network. The immediate boss demo is:

```text
balls.wlkrlabs.com
  → paste one Windows install command
  → join one Circle through the local browser
  → open the approved project folder in File Explorer
  → edit a real ordinary work file
```

Neither Owner nor invited Member should handle IP addresses, ports, SMB passwords, object IDs,
plan tokens, daemon arguments, PowerShell configuration, runtime setup, or manual drive-letter
selection. One website-provided command in the PowerShell included with Windows is the accepted
install/update entrypoint. Explicit Windows elevation remains acceptable for the exact host
mutation that requires it.

### Shared-ecosystem product gate

After the boss demo, the smallest complete proof is one joined Member using:

1. Circle Files;
2. Circle Messaging for human communication;
3. Circle AI contributed by another Node.

One Member-removal intent must then stop future authorization across all three and report provider
cleanup as complete, pending, or refused. Balls does not claim to erase copies outside its control
or revoke an unreachable offline device instantly.

Failure to make this whole journey materially easier than assembling existing products pauses
platform expansion and triggers a product rethink.

### Contribution and remote administration

Nodes contribute bounded capabilities, not whole computers. General shell, RDP, or remote-control
access is never implied by Membership. SSH was a usability analogy for seamless connection; any
real terminal or remote-administration capability remains separately defined and permissioned.

### Build versus integrate

Balls owns Circle Membership, contribution, capability discovery, authorization, lifecycle
reconciliation, auditing, and the shared human experience. Prefer integrating mature storage,
transport, remote-access, AI-runtime, and workload providers. Build replacements only when evidence
shows that integration cannot satisfy an accepted requirement for usability, security, LAN/offline
operation, ownership, or self-hostability.

### Private-pilot safety posture

Prioritize the working end-to-end product and rapid human feedback over security architecture for
hypothetical scale or public-internet threats. Never bypass operating-system protections, expose
private services publicly, mishandle credentials, delete user data, grant unapproved machine
access, or weaken existing provider security. Additional security work requires a concrete pilot
risk, observed failure, or accepted release requirement.

### Software distribution

[`balls.wlkrlabs.com`](https://balls.wlkrlabs.com) is the sole official human-facing software and
update entrypoint. Alpha is the primary recommended download. A lower Development section may point
to incomplete or broken immutable GitHub prereleases after package-integrity checks; agents may
publish that channel for an active issue without per-build approval. Previous versions remain
available through immutable version manifests. Alpha, Beta, and Stable promotion remains
Owner-gated and reuses the exact tested assets. The site does not host ad hoc binaries copied from
a development computer, and a private Circle invitation remains separate from the public software
download. See [`ADR 0010`](docs/decisions/0010-public-development-download-channel.md).

### Balls Wizard and Circle AI

**Balls Wizard** is an optional local product guide represented by a floating brand-violet ball
wearing a wizard hat. The local browser UI offers its download; Balls never downloads the model
automatically. The intended model is a pinned quantized instruction-tuned Gemma 4 E2B. Balls Wizard
uses a version-matched local Wizard Guide, exposes relevant guidance under optional Sources, and
begins as read-only help. Its playful local system prompt receives only bounded ephemeral system
facts and never receives tools or Circle content. Core Balls remains usable without it.

Windows 11 x64 v0 uses the exact text-only Google Gemma 4 E2B QAT Q4 GGUF behind a Balls-managed,
hash-pinned llama.cpp sidecar. Both are downloaded only after consent from immutable official
sources and remain replaceable behind typed platform/application boundaries. See
[`Balls Wizard v0`](docs/specs/balls-wizard-v0.md).

**Circle AI** is different: it is an explicitly contributed AI capability running on one approved
Node and made available to other authorized Circle Members without exposing runtime addresses or
credentials.

### Durable product truths

- Balls is open source under Apache License 2.0.
- Simple defaults and technical inspectability must coexist.
- One ordinary PC must not permanently define the Circle.
- LAN/offline capability matters; local first does not mean local only.
- Cross-platform support is architectural.
- Circle AI, Circle Apps, and honest distributed workloads remain long-term pillars.
- Balls may provide hosted convenience infrastructure but must not own the Circle.

## Recommended technical choices

- Keep the native cross-platform `ballsd` service, one typed application core, the local browser UI,
  and the first-class `balls` CLI.
- Keep platform-specific mutations behind narrow typed adapters; do not put raw OS commands in Core.
- Reuse the implemented trusted admission and Circle Files machinery where it helps the active
  human journey. Do not expand it for hypothetical future threats.
- Use SMB as the current Windows Circle Files provider, not as the Circle Files product definition.
- Use LAN and owner-managed Tailscale as early transports, not as Circle identity.
- Keep unrestricted remote execution outside ordinary Member flows.

## Historical reset boundary

The published `0.1.0-alpha.2`, `0.2.0-alpha.1`, and `0.3.0-alpha.1` releases and their evidence
remain historical checkpoints. The unpublished `0.4.0-alpha.1` acceptance issue and milestone were
closed as superseded on 2026-08-26. Their secure Circle Files implementation and verification remain
available to reuse; they no longer define the active roadmap. See
[`ADR 0009`](docs/decisions/0009-reset-around-private-shared-ecosystem-proof.md).

## Intentionally open questions

- Which measured runtime/artifact should follow the Windows 11 x64 llama.cpp and QAT Q4 Wizard v0
  on Linux, macOS, Windows Arm64, or accelerator-specific hardware?
- Which existing runtime first provides shared Circle AI?
- How far should the existing minimal Circle message protocol grow before an external messaging
  provider is preferable?
- What generic persistence and reconciliation model should represent Capability Grants beyond
  Circle Files?
- What remote-connectivity setup can remain invisible to an invited Member while preserving
  owner-controlled LAN/Tailscale boundaries?

Resolve these through the smallest prototype tied to an accepted user outcome, not through
speculative platform design.
