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

The Linux platform adapter provides the equivalent native boundary:

- the default is `$XDG_STATE_HOME/balls`, or `$HOME/.local/state/balls` when the XDG variable is
  absent or relative;
- custom paths must be normalized absolute paths on a recognized local persistent filesystem;
- every existing path component is inspected without following its final link, symbolic links and
  group/other-writable ancestry are rejected, and the nearest parent used for creation must belong
  to the effective user (a root-owned sticky parent is accepted only when creating a private
  runtime directory, not state directly);
- the state directory must belong to the effective user and is set to `0700`; the marker, database,
  sidecars, lease, and automatic private-listener port record must be regular user-owned files and
  are set to `0600`;
- the same exact marker and allowlist used on Windows distinguish dedicated Balls state from an
  arbitrary directory. Unknown entries and ownership/type mismatches fail before modification.

Linux local-filesystem verification uses `/proc/self/mountinfo` and an explicit allowlist of local,
persistent filesystem types. Unknown, network, pseudo, removable compatibility, and memory-only
types fail closed rather than inheriting SQLite durability or permission assumptions.

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
- Linux ownership and Unix-mode details remain isolated from core, protocol, and SQLite projects.
- A future macOS adapter should provide equivalent ownership and path-safety semantics using native
  mechanisms, not copy Windows ACL or Linux `/proc` implementation details.
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
