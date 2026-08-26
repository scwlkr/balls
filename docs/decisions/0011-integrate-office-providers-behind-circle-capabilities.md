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
