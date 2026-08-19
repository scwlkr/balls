# ADR 0002 — Protect and Identify Local Daemon State

- **Status:** Accepted
- **Date:** 2026-08-19
- **Scope:** Phase 1 Slice 1 local persistence

## Context

`ballsd` needs durable Node and Circle identity before remote membership exists. A user-supplied
path and a generic SQLite file are easy to misuse: the daemon could inherit broad ACLs, traverse a
redirected path, modify unrelated contents, open another application's database, or accept a
partially compatible schema.

The safety rule belongs at the platform and storage boundaries, not in Circle domain logic.

## Decision

Each daemon owns one dedicated local state directory.

The Windows platform adapter:

- permits only a new/empty directory or a directory with the exact Balls v1 marker;
- permits only the marker, SQLite database/sidecars, and daemon lock file;
- rejects UNC and mapped-network paths, reparse-point paths/files, invalid markers, and unexpected
  entries;
- replaces inherited access rules with protected ACLs granting full control only to the current
  Windows user and LocalSystem, and reapplies them to known existing files.

The daemon takes an exclusive `ballsd.lock` lease before opening state.

The SQLite adapter identifies its file with application ID `0x42414C53` and schema version `1`. It
validates the exact schema, integrity, and foreign-key relationships before enabling WAL and fails
closed for foreign, future, incomplete, corrupt, or unsupported state.

SQLite remains a local persistence provider. This decision does not select the future replicated
Circle state model.

## Consequences

- A custom data directory must be dedicated to Balls; a nonempty arbitrary folder is rejected.
- Startup stops with an actionable error instead of resetting unknown or incompatible state.
- One daemon process owns a state directory at a time.
- Windows ACL details remain isolated from core, protocol, and SQLite projects.
- Future Linux and macOS adapters should provide equivalent ownership and path-safety semantics
  using their native mechanisms, not copy Windows ACL implementation details.
- The marker and ACL do not defend against LocalSystem, an administrator, offline disk access, or a
  process already running as the same user. Encryption at rest and backup/recovery remain separate
  decisions.
- Every future schema version requires an explicit migration and rollback/recovery test strategy.

## Alternatives not selected

- **Use any user-provided directory:** too easy to damage unrelated data or inherit unsafe access.
- **Trust the SQLite filename alone:** cannot distinguish a foreign or structurally incompatible
  database.
- **Silently recreate invalid state:** would destroy durable identity and hide corruption.
- **Put SQLite rules in Core:** would make a provider synonymous with the product model and reverse
  the intended dependency direction.
