# ADR 0018 — Prioritize Revit Server Rapid Setup

- **Status:** Accepted
- **Date:** 2026-08-27

After the already-approved Alpha promotion closes the Private Boss Demo, one bounded Revit Server
Rapid Setup issue becomes Balls' next active product outcome. It temporarily precedes the Shared
Ecosystem Proof and the broader office-server sequence because the Owner needs immediate evidence
that Balls can make an existing specialist provider fast to install and easy to hand off. Those
later outcomes are deferred, not canceled.

The issue uses an exact Development build on one prepared disposable Windows Server 2022 Desktop
Experience VM hosted by the Owner's Linux laptop. From cached official Autodesk media and a ready
OS, Balls must guide Revit Server 2027 Host+Admin installation, verify the services and
Administrator surface, and export portable setup intent plus a redacted receipt in under 30 minutes
of wall-clock time. A graphical VM console is allowed for Autodesk's required license and
configuration step; the VM may run headlessly afterward. Balls does not create or manage the VM as
a product capability.

Passing this issue proves only installation health on an unlisted QEMU/KVM development environment.
It does not prove a Revit client, model operations, synchronization, concurrency, Tailscale,
Circle-wide authorization, backup, recovery, production hardware, or Autodesk-supported production
operation. Autodesk Revit Server remains the provider, and Balls never owns or manipulates its
model repository.
