# Protected Cryptographic State Verification

**Date:** 2026-08-20  
**Issue:** [#35](https://github.com/scwlkr/balls/issues/35)  
**Scope:** durable Node signing identity, Circle root/Anchor authority, platform protection,
transactional schema migration, and explicit encrypted authority export; no import, rotation,
invitation redemption, remote listener, or transport-credential issuance.

## Acceptance evidence

| Acceptance check | Observed evidence |
| --- | --- |
| Core-owned typed contracts | `Balls.Core` owns public role-scoped credentials, Node/Circle authority identities, signing/export operations, backup validation, and the private-material protection port. Core has no platform dependency; focused `Balls.Security.*` adapters depend inward on Core. |
| Strong distinct material and stable IDs | P-256 keys use canonical DER SPKI, SHA-256 role-scoped key IDs, SHA-256/P1363 signing, and fixed vectors from ADR 0006. Node, Circle root, and Anchor keys are generated independently and verified distinct. |
| Windows/Linux protection | Windows uses DPAPI `CurrentUser` with Balls-specific entropy. Linux creates `balls.db` as `0600` inside a verified current-user-owned `0700` directory on a supported local filesystem. The stored scheme must match the selected adapter. |
| Fail-closed state | Startup validates exact schema, credential completeness, algorithm/curve, key ID, public/private binding, authority generation, protection scheme, and protected encoding. Missing, substituted, malformed, unknown/newer, or mismatched material returns a bounded error and is left unchanged. |
| Atomic and restart-stable | SQLite schema v1→v2 migration creates existing Node/Circle credentials in one transaction. An injected failure after the first protected value leaves version 1 and no partial identity tables; the next successful open migrates once. Fresh Node and Circle creation commits records and credentials together. |
| Encrypted authority export | The write-only v1 envelope contains separate PBES2-encrypted root/Anchor PKCS#8 values using AES-256-CBC and PBKDF2-HMAC-SHA256 at 600,000 iterations. A root P1363 signature authenticates the exact manifest, Circle/generation, public credentials, cipher/KDF profile, and ciphertext digests. Wrong Circle, tampering, malformed input, and unknown versions reject. |
| No ordinary secret edge | Private PKCS#8 buffers are zeroed after protection/use where managed APIs permit. The envelope serializes only format/version and renders as sensitive/redacted. Repository search found no private-material contract in CLI, browser DTOs, local OpenAPI, or browser sources. |
| Documentation reconciled | ADR 0006, architecture, remote protocol boundary, storage v2, threat model, developer workflow, docs index, and current state record the implementation and explicit non-goals. |

## Focused and native observations

- Core identity/backup unit suite: 11 passed, 0 failed.
- SQLite contract suite: 23 passed, 0 failed, including atomic migration rollback, restart
  stability, role separation, signing, backup encryption/signature, missing material, malformed
  state, protection-scheme substitution, and forward-version refusal.
- Windows OS integration: 1 passed on the Windows development host. DPAPI ciphertext differed from
  plaintext, round-tripped for the current user, rejected tampering, signed from persisted state,
  and retained the same public key ID after restart under protected ACLs.
- Linux OS integration: all 8 platform tests passed in the Ubuntu WSL2 development executor. The
  new protected-state test observed `0700`/`0600`, valid signing state, and the same public key ID
  after restart. The minimal guest lacked ICU, so this focused run used .NET invariant
  globalization; the tested ownership, mode, SQLite, and cryptographic behavior is
  culture-independent. WSL is development evidence, not the product runtime definition.
- NuGet's current direct/transitive vulnerability audit reported no vulnerable package in any
  solution project.

## Repository full gate

The final repository `full` command passed after validation hardening, including:

- locked .NET/pnpm restore, .NET format/analyzers, generated-client drift, Prettier, ESLint,
  TypeScript, and Release build with 0 warnings and 0 errors;
- 120 .NET tests passed on Windows with 11 expected platform-only skips; Core passed 11 and
  SQLite passed 23;
- four Vitest component tests and the production web build;
- the real Playwright Chromium launch/create/list/restart journey.

The final hardening added typed missing-field rejection, credential-completeness and
authority-generation checks, more private-buffer zeroing, and stronger Windows end-to-end state
proof. No local failure or policy exception remained.

## Hosted pull-request evidence

Pending PR creation and the required fixed Windows 2025 / Ubuntu 24.04 lanes, dependency review,
and CodeQL. This section is replaced with exact run links and observations before merge.

## Non-goals observed

No CLI/browser key-export endpoint, authority import, automatic rotation, hardware-backed claim,
invitation redemption, remote listener, transport credential, membership admission, or
multi-Anchor replication is added. A lost authority remains unrecoverable without an accepted
live authority or separately custodied encrypted export.
