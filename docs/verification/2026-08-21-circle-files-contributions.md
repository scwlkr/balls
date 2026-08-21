# Circle Files contributions and Access Grants verification

**Date:** 2026-08-21  
**Issue:** [#56](https://github.com/scwlkr/balls/issues/56)  
**Scope:** provider-neutral contribution definitions and whole-folder Member Access Grants

## Implemented outcome

- Core owns typed contribution, provider identity, lifecycle, and Access Grant concepts. A grant
  applies to the whole contribution and is exactly `read-only` or `read-write`.
- Each mutation resolves the protected local Member, requires its persisted Circle role to be
  Owner, binds the current Circle authority generation/root credential, and stores independent
  Member and Circle-root signatures over one canonical mutation transcript.
- SQLite schema v6 transactionally adds contribution and grant state. Caller request IDs provide
  exact-retry idempotency, conflicts fail closed, and list order remains stable after reopen.
- Local-control v1 and `balls files contribution|grant create|list` expose deterministic safe
  projections. Transcripts, signatures, provider credentials, and private authority are absent.
- Authenticated browser GETs reuse the same application queries for read-only contribution/grant
  lists. Circle Files mutation methods remain unmapped and receive `405` on those read routes.

## Automated evidence

Observed on the Windows development host from the issue branch:

| Gate | Observation |
| --- | --- |
| Core/application | 4 focused tests passed: dual authorization, Read-only/Read-write behavior, non-Owner rejection before signing/mutation, and stale/substituted authority rejection |
| SQLite contract | 5 focused tests passed: idempotent restart-stable state, v3-step interruption resume, v5-to-v6 migration with preserved Circle state, injected v6 migration rollback/retry, and failed-grant rollback with restart absence |
| Local API/browser boundary | 3 focused tests passed: safe control create/list projections, OpenAPI contract, and authenticated read-only browser lists with mutation absence |
| Structured CLI | 1 focused separate-process test passed for contribution/grant create/list text and JSON output |
| Local fast gate | Passed after the final review fixes in 59.8 seconds; Release build had 0 warnings/errors, 210 .NET tests passed, 9 browser-component tests passed, and the installed-browser journey passed |
| Full local gate | Passed in 51.6 seconds: 215 .NET tests, 9 browser-component tests, and the installed-browser journey passed; 20 host-inapplicable tests skipped honestly |

Existing storage contract coverage also refuses a database whose `user_version` is newer than the
supported schema. No VM,
installer, network, privileged helper, or provider mutation was run because this slice introduces
no OS/provider behavior.

## Security boundary and non-goals

The local daemon still trusts its protected same-user IPC boundary. Within that boundary, only the
protected local Owner Member holding the current Circle root can authorize these Circle Files
mutations. Same-session malware could ask that Owner daemon to sign a mutation; remote Owner
administration, recovery/rotation, and replicated authorization convergence remain later work.

The stored provider identity is an opaque product identifier plus the selected authority-holding
Node. This slice does not probe or mutate SMB, create a folder/share/account, store a provider
credential, map Explorer, adopt existing folders, delete user files, replicate/synchronize files,
or implement version history or trash. Contributions and grants stop at `defined`; later issues
own readiness, activation, provider credentials, mapping, and revocation.
