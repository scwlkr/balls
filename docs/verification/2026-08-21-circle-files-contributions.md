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
- Browser Files routes remain absent; an authenticated request to that route receives `404`.

## Automated evidence

Observed on the Windows development host from the issue branch:

| Gate | Observation |
| --- | --- |
| Core/application | 4 focused tests passed: dual authorization, Read-only/Read-write behavior, non-Owner rejection before signing/mutation, and stale/substituted authority rejection |
| SQLite contract | 3 focused tests passed: idempotent restart-stable state, v5-to-v6 forward migration with preserved Circle state, and failed-grant rollback with restart absence |
| Local API/browser boundary | 3 focused tests passed: safe create/list projections, OpenAPI contract, and authenticated browser mutation-route absence |
| Structured CLI | 1 focused separate-process test passed for contribution/grant create/list text and JSON output |
| Local fast gate | Passed in 48.5 seconds; Release build had 0 warnings/errors, 208 .NET tests passed, 9 browser-component tests passed, and the installed-browser journey passed |
| Full local gate | Not yet run; scheduled once after independent code review |

Existing storage contract coverage also refuses a database whose `user_version` is newer than the
supported schema and exercises transactional rollback for prior forward migrations. No VM,
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
