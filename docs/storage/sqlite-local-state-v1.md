# SQLite Local State v1

**Status:** implemented local persistence for Phase 1 Slice 1.

This database belongs to one `ballsd` instance. It preserves local Node identity and the Circles
known to that daemon. It is a storage adapter, not the eventual replicated Circle state model.

## Files and ownership

- Database: `balls.db` in the daemon's dedicated data directory.
- SQLite sidecars: `balls.db-wal` and `balls.db-shm` when present.
- Daemon lease: `ballsd.lock`, opened exclusively so only one daemon writes the directory.
- Directory marker: `.balls-state` with the exact v1 marker content.

On Windows and Linux, the selected platform adapter prepares the directory before SQLite opens it
and reapplies protection to known files after the store creates its database/sidecars. Windows uses
protected ACLs; Linux requires effective-user ownership with a `0700` directory and `0600` regular
files on a verified local persistent filesystem. See
[`ADR 0002`](../decisions/0002-protected-local-state.md) and the
[`threat model`](../security/threat-model.md).

## Database identity and open sequence

- SQLite `application_id`: `0x42414C53` (`BALS`).
- SQLite `user_version`: `1`.
- Connection mode: read/write/create, private cache, pooling disabled.

The store reads identity and schema metadata before applying persistent configuration. A database
is fresh only when its application ID and user version are zero and it has no user tables, views,
or triggers. Any foreign, newer, incomplete, or incompatible state fails closed and is left in
place; it is not reset or replaced.

For a valid database, the store requires:

- the exact v1 tables, column types, primary keys, `NOT NULL` constraints, foreign keys, unique
  indexes, and singleton check;
- no unexpected views, triggers, or explicit indexes;
- `PRAGMA integrity_check` to return `ok`;
- `PRAGMA foreign_key_check` to return no rows.

It then configures `foreign_keys=ON`, `synchronous=FULL`, `busy_timeout=5000`, and
`journal_mode=WAL`. Startup fails if the filesystem cannot provide WAL mode.

## Schema

All identifiers and timestamps are stored as `TEXT`. UUIDs use their canonical string form;
timestamps use round-trip ISO 8601 format.

| Table | Purpose and key constraints |
| --- | --- |
| `nodes` | Catalog of Node IDs; `node_id` primary key |
| `local_node` | One local identity; singleton key constrained to `1`, unique `node_id` referencing `nodes`, display name, creation time |
| `circles` | Circle ID primary key, name, creation time |
| `members` | Member ID primary key, Circle foreign key with cascade delete, display name, numeric role, join time |
| `circle_nodes` | Composite Circle/Node primary key, Circle foreign key with cascade delete, Node foreign key, display name, join time |
| `circle_creations` | Request ID primary key, unique Circle foreign key with cascade delete, normalized Circle and Owner input, local Node foreign key |

`nodes` is deliberately broader than `local_node`: later slices can record other enrolled Nodes
without redefining the daemon's singleton identity. No external enrollment flow exists yet.

## Transaction and concurrency rules

- First-time schema creation, application ID, and schema version are committed together.
- Initial local Node creation inserts the Node catalog record and singleton record together.
- Circle creation atomically inserts the Circle, Owner, local Node enrollment, and idempotency
  record.
- A repeated creation request with equivalent normalized input returns the original Circle. A
  conflicting reuse fails with `creation_request_conflict`.
- Store operations are serialized within the process. Disposal waits for the active operation and
  prevents new operations.

## Fail-closed errors

| Code | Meaning |
| --- | --- |
| `foreign_local_state` | The file is not an empty database or a Balls database |
| `unsupported_state_schema` | The database schema version is newer than this daemon supports |
| `invalid_state_schema` | Required v1 structure is absent or incompatible |
| `invalid_local_state` | Integrity or relationship validation failed |
| `unsupported_state_filesystem` | WAL mode is unavailable |

## Migration policy

Version 1 is the first schema, so there is no earlier migration. Any future schema change must add
an explicit, transactional version migration with forward-version refusal and recovery tests. It
must not silently reinterpret SQLite as the Circle's network replication protocol.
