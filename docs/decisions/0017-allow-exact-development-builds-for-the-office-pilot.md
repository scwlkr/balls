# ADR 0017 — Allow Exact Development Builds for the Office Pilot

- **Status:** Accepted
- **Date:** 2026-08-26

The Run-and-gun Office Pilot may use an exact immutable Development build without Authenticode
signing. Code signing is not a launch gate for this two- or three-person startup pilot. Balls still
verifies package identity and integrity, never weakens Defender, Smart App Control, execution policy,
or application control, and reports a machine that rejects the build as blocked rather than teaching
an application-trust bypass. This is an honest Development deployment, not an accepted production
release claim.
