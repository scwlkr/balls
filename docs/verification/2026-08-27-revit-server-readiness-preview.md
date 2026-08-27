# Revit Server 2027 readiness and preview verification

**Date:** 2026-08-27  
**Issue:** [#114](https://github.com/scwlkr/balls/issues/114)  
**Scope:** read-only inspection, official-media identity, and exact Host+Admin/no-Accelerator plan

## Proved in source and automated tests

- `Balls.Platform` owns a Revit-specific media-selection, readiness, redacted outcome, and immutable
  plan contract separate from Circle Files/SMB readiness.
- The Windows adapter uses one fixed, bounded, cancellable PowerShell inspection and no mutation
  verb. It fails closed for unsupported OS/build/install type, pending restart, unsuitable `D:`,
  foreign/nonempty/reparse/shared/mounted repository state, ambiguous IIS, existing Revit roles or
  products, Public network/exposure, and untrusted or substituted media.
- A Ready report creates one digest-bound plan with Host+Admin, Accelerator forbidden,
  `D:\RevitServer\2027` version-isolated paths, Autodesk's documented Windows/IIS prerequisites,
  portable-principal ACL intent, Default Web Site effect, server-local `RSN.ini`, Private/LocalSubnet
  TCP 80/808 plus ICMP intent, verification actions, and the Balls/Autodesk ownership boundary.
- Browser selection is native and short-lived/session-bound. The browser never uploads the roughly
  870 MB installer and never receives the selected local path. The local-control and browser routes
  use the same daemon application service.
- The top-level Development panel is available before a Circle exists and plainly renders Ready or
  Blocked without an apply/install action.

## Environment evidence

| Check | Result |
| --- | --- |
| Focused Windows-adapter contract tests | `PASS` on Linux against deterministic observations; not a Windows OS claim |
| Daemon application contract tests | `PASS` |
| Browser component tests/typecheck/build | `PASS` |
| Pinned compose overlays and bounded manager syntax/configuration | `PASS`; configuration only, VM not started |
| Disposable Windows Server 2022 graphical Ready path | `NOT RUN` — reserved VM has not been created or operated |
| Representative graphical Blocked paths on Windows | `NOT RUN` — reserved VM has not been created or operated |
| Before/after Windows no-mutation snapshot | `NOT RUN` |
| Revit Server installation or health | `NOT RUN` — outside #114 |

The live Linux host inventory found `omarchy-windows` and `balls-issue61-provider-desktop` running
with insufficient free memory for the reserved 8 GiB server. No guest was stopped or changed. The
company-content Revit ZIP in Linux Downloads is not Autodesk installer media and was not used.

## Exact limitation

This record proves implementation and deterministic contract behavior only. It does not claim the
official 2027 media's real PE/manifest identity, a Windows graphical Ready/Blocked observation,
Windows no-mutation evidence, Revit Server installation, Autodesk health, model operations, remote
access, or production support. Those required Windows observations remain `NOT RUN`, not PASS.
