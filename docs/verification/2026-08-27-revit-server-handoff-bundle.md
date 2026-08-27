# Revit Server 2027 handoff bundle verification

**Date:** 2026-08-27  
**Issue:** #116  
**Scope:** deterministic bundle/export behavior only; disposable-VM timed acceptance remains `NOT RUN`

## Implemented contract

- `revit-server-2027-setup-bundle.zip` contains exactly `setup-template.v1.json`,
  `setup-receipt.v1.json`, `README.md`, and `bundle-manifest.json`.
- The template contains portable version, role, relative-storage, prerequisite, symbolic-principal,
  official-media, private-LAN, health, and non-goal intent. It contains no lab Host or absolute path.
- The receipt records exact Development package, Autodesk, bounded Windows, plan, observed health,
  temporary evidence, monotonic wall-clock, human-intervention, outcome, and untested-scenario data.
- The manifest hashes every other member. Export validates exact member/schema bounds, hashes,
  receipt completeness, strict `< 00:30:00` outcome, and private/machine-replay exclusions before
  returning any ZIP.
- The end sample is taken only after package verification and one complete ZIP generation plus
  strict validation pass. A final receipt-bound ZIP is then serialized from that locked evidence.
- Export requires the official Windows bootstrap's exact Development `installation.json` identity.
  It blocks on missing/substituted identity or a lost in-process monotonic timer.
- Begin setup verifies that same installation record before starting the monotonic timer, so a
  direct development build or unidentified package cannot enter a passing timed attempt.
- Export repeats the full health inspection immediately before bundle creation and refuses any
  role, service, endpoint, path, ACL, log, or network drift after the healthy screen.

## Automated evidence

`RevitServerHandoffBundleTests` covers exact members and hashes, template portability, receipt
evidence, sensitive/payload exclusions, tampering, and the 29:59.999 PASS / 30:00.000 FAILED
boundary. `RevitServerSetupApplicationTests` covers uninterrupted export, persisted timer and human
time, the exact PASS claim, timeout failure, and restart fail-closed behavior.
`RevitServerPackageIdentitySourceTests` covers accepted and substituted installation records.

## Still required for issue acceptance

- exact immutable Development artifact publication and prior-pointer record;
- installation through the official distribution path in the disposable Windows Server VM;
- one fresh complete graphical timed run ending only after ZIP export;
- the exact artifact, Windows, Autodesk, plan, prompt, elapsed, bundle, failure, and residual-state
  evidence in the manual checklist;
- relevant full gate, required PR CI, and distribution-risk verification.

No provider installation or under-30-minute real-world claim is made by this record.
