# ADR 0011 — Integrate Office Providers Behind Circle Capabilities

- **Status:** Accepted
- **Date:** 2026-08-26

The Office Circle will make mature office systems easy to set up and use without replacing their
data planes or application semantics. Autodesk Revit Server remains responsible for its service,
protocol, and workshared models; Balls supplies a guided Revit Server Capability for supported host
setup and authorized Member onboarding. Windows SMB remains responsible for ordinary file transfer
and locking; Balls owns the Circle authorization and provisions exact Member access, while separate
administrator break-glass access is recovery-only rather than a parallel employee path. Tailscale
remains the private-network authority; Balls integrates it through an Owner-approved, least-
privilege connection that simplifies Node enrollment, approved reachability, and diagnostics.

This boundary keeps the Circle experience coherent, lets mature providers continue working when the
Balls interface is unavailable, and avoids making Balls a replacement Revit server, file protocol,
or private network.

The initial office deployment supports Revit Server 2027 only. A future Revit release is a separate,
explicitly installed and verified Capability with its own provider state; Balls never silently
upgrades a Revit Server repository or project model.

Every approved Revit Node uses Tailscale even on the office LAN and reaches the Host through one
frozen MagicDNS name. Balls enrolls each Node only after Owner approval by using one-time material
created through a narrowly scoped, Owner-configured Tailscale trust credential; no employee handles
a reusable network key. All Office Circle Members may use the Revit model service, while only a
Server Administrator may reach or change the Revit Server administration surface.

The initial integration has no Revit Server Accelerator. A future Accelerator is outside this
version and may be reconsidered only through a new explicit Owner decision supported by measured
remote-performance failure; Balls never adds one automatically.
