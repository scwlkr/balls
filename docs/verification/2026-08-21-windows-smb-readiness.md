# Windows SMB readiness verification

**Date:** 2026-08-21  
**Issue:** [#57](https://github.com/scwlkr/balls/issues/57)  
**Scope:** typed read-only Windows SMB 3.1.1 readiness before any host mutation

## Implemented outcome

- `Balls.Platform` owns a provider-neutral readiness contract. The Windows adapter stays in
  `Balls.Platform.Windows`; Core has no Windows command, SMB, registry, network, or firewall type.
- The adapter executes one exact encoded PowerShell inspection with no caller-controlled input, a
  10-second timeout, one combined 65,536-character decoded-output budget for both streams, strict
  typed parsing, and deterministic redacted failures.
- Nine stable checks cover the supported Windows generation, SMB server/SMB 2+ availability, SMB
  3.1.1, SMB1, insecure guest access, signing, encryption, connected Private network scope, and
  Private/Public firewall enforceability including Public/Any inbound allow rules whose port scope
  can include TCP 445. Unsafe observations take precedence over unknown ones.
- `GET /control/v1/files/readiness` and `balls files readiness` expose the same ordered result;
  structured output uses the existing version-1 CLI envelope.
- Linux and macOS explicitly return `unknown` for this Windows provider. The browser has no
  readiness or mutation route.

## Automated evidence

Observed on the Windows development host from reviewed commit
`ce6d27b9b0657274cd0f14227fad92664d440bea`:

| Gate | Observation |
| --- | --- |
| Windows adapter contracts and OS integration | Passed the focused Contract/OSIntegration selection, including safe/unsafe/unknown matrices, strict malformed/forward-unknown handling, redacted command failure, allowlist and no-mutation assertions, timeout behavior, and the real host adapter |
| Structured CLI | One separate-process `files readiness` acceptance passed with the version-1 JSON envelope and nine ordered checks |
| Local-control/OpenAPI | The endpoint contract passed and the checked-in OpenAPI/TypeScript client generation had no drift |
| Host composition | Windows selected the real inspector; unsupported hosts selected the explicit unknown inspector |
| Development host observation | `not-ready`; the first eight checks were ready and `firewall-scope` returned `public_smb_inbound_allowed` because enabled Public/Any inbound allow rules can include TCP 445 |

The final repository fast/full gates and protected pull-request results are recorded on the issue
and pull request so their exact commit and platform checks remain linked to GitHub's durable run
evidence.

## Dedicated Windows VM evidence

The owned `Balls.Dev.Windows11` Hyper-V guest ran the focused adapter contracts, the real
OS-integration adapter, and the structured CLI from that exact detached reviewed commit.
The guest was Windows 11 Enterprise Evaluation `10.0.26200`; no checkpoint was restored and no
security policy was changed.

The durable guest result at `C:\BallsLab\smb-readiness\latest-result.json` recorded:

| Field | Observed value |
| --- | --- |
| UTC | `2026-08-22T00:28:10.0502189+00:00` |
| Focused Contract | 6 passed |
| Focused OSIntegration | 4 passed |
| Structured CLI | passed |
| Aggregate | `not-ready` |
| Unsafe checks | `private-network` / `private_network_unavailable`; `firewall-scope` / `public_smb_inbound_allowed` |
| No mutation | true |

The result is the intended fail-closed behavior. Independent readback showed the guest's connected
Ethernet profile was Public; the tightened inspection also observed enabled Public/Any inbound
allow-rule scope that can include TCP 445. The adapter therefore refused readiness while the first
seven checks were ready. Before/after snapshots of the inspected SMB server/client, services,
network profiles, firewall profiles/rule count, Windows feature state, and relevant registry values
were identical.

## Security boundary and non-goals

This slice never enables Windows features or SMB policy, starts services, changes a network or
firewall profile, creates firewall rules, folders, shares, accounts, ACLs, or provider credentials,
or maps Explorer. It does not prove physical two-machine access, actual share connectivity, or file
behavior. Those mutation and end-to-end outcomes belong to later milestone issues; this issue only
provides the typed prerequisite that must be ready before they run.
