# ADR 0009 — Reset Around the Private Shared-ecosystem Proof

- **Status:** Accepted
- **Date:** 2026-08-26

Balls is a graphical shared computing environment, not a file-server product or general remote
shell. A Circle makes explicitly contributed capabilities from trusted computers discoverable and
usable without requiring Members to configure the underlying machines or providers. Finish one
urgent private boss demo first: official download, graphical join, and a usable shared Explorer
folder on a separate Windows computer. The next product gate is one joined Member using Circle
Files, Circle Messaging, and Circle AI hosted by another Node, followed by coherent access
revocation. Prefer integrating mature providers; Balls owns the Circle model, capability catalog,
authorization, reconciliation, and human experience.

The current audience is approximately two or three personally trusted people over a private LAN or
owner-managed Tailscale network. Preserve the narrow safety floor around OS protections, public
exposure, credentials, user data, and explicit access, but do not let speculative security work for
hypothetical scale displace the working pilot. Close the unpublished `0.4.0-alpha.1` acceptance
frontier as superseded, preserve its implementation and evidence as history, and replace it with
one urgent boss-demo issue before creating a broader issue queue.

Balls Wizard is a separate optional on-device guide: a floating brand-violet ball with a wizard hat
that the user may download from the local browser UI. Its intended local model is the quantized
instruction-tuned Gemma 4 E2B; it retrieves version-matched user documentation and begins as
read-only guidance. It is not downloaded automatically, is not required for core Balls operation,
and is not the shared Circle AI capability.

The accepted product contract is
[`../specs/private-shared-ecosystem-v1.md`](../specs/private-shared-ecosystem-v1.md). Only the
[`Private Boss Demo specification`](../specs/private-boss-demo-v1.md) currently authorizes
implementation; later product stages require their own executable specifications.
