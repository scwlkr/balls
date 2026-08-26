# SQLite Local State

**Status:** schema v9 implemented for local records, protected cryptographic authority,
invitations, persisted Circle admission, the first durable Circle message, and provider-neutral
Circle Files contribution/grant authorization, grant revocation, lifecycle audit, and protected
Windows provider credentials plus the joined Node's protected private Circle connection.

This database belongs to one `ballsd` instance. It preserves local Node identity and the Circles
known to that daemon. It is a storage adapter, not the eventual replicated Circle state model.

## Files and ownership

- Database: `balls.db` in the daemon's dedicated data directory.
- SQLite sidecars: `balls.db-wal` and `balls.db-shm` when present.
- Daemon lease: `ballsd.lock`, opened exclusively so only one daemon writes the directory.
- Directory marker: `.balls-state` with the exact v1 marker content.

On Windows and Linux, the selected platform adapter prepares the directory before SQLite opens it
and reapplies protection to known files after the store creates its sidecars. Windows uses
protected ACLs; Linux creates `balls.db` as `0600` before SQLite can persist a key and requires
effective-user ownership with a `0700` directory and `0600` regular files on a verified local
persistent filesystem. See
[`ADR 0002`](../decisions/0002-protected-local-state.md) and the
[`threat model`](../security/threat-model.md).

## Database identity and open sequence

- SQLite `application_id`: `0x42414C53` (`BALS`).
- SQLite `user_version`: `9`.
- Connection mode: read/write/create, private cache, pooling disabled.

The store reads identity and schema metadata before applying persistent configuration. A database
is fresh only when its application ID and user version are zero and it has no user tables, views,
or triggers. Any foreign, newer, incomplete, or incompatible state fails closed and is left in
place; it is not reset or replaced.

For a valid database, the store requires:

- the exact versioned tables, column types, primary keys, `NOT NULL` constraints, foreign keys, unique
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
| `local_node_credentials` | One role-scoped P-256 Node signing credential, stable public key ID, SPKI, protection scheme, protected PKCS#8 material, and creation time |
| `circle_authorities` | One generation-1 Circle root plus distinct delegated Anchor credential, their public IDs/SPKIs, protected PKCS#8 material, protection scheme, and creation time |
| `local_transport_credentials` | One protected P-256 TLS transport key distinct from the Node signing key |
| `circle_invitations`, `invitation_redemptions`, `revoked_invitations` | Exact issued package material plus durable single-use, expiry, and revocation state |
| `circle_trust` | Public root/Anchor credentials, authority generation/sequence, issuer Node, and the accepted signed admission receipt; present for every known Circle even when this Node has no private Circle authority |
| `circle_member_credentials` | Admitted Member signing credentials keyed by Circle and Member |
| `circle_node_credentials` | Node signing and transport credentials plus the canonical root-signed transport binding |
| `admission_attempts`, `admission_challenges` | Retry-stable protected applicant Member identity and both persistent admission challenges |
| `circle_admissions` | Exact accepted request digest, signed response, admitted Member/Node IDs, authority sequence, and time |
| `security_audit_events` | Bounded admission outcomes; at most 512 newest events per Circle and no keys, transcripts, or signatures |
| `local_circle_members` | This Node's protected Member signing identity for each joined or created Circle |
| `circle_member_nodes` | Authorized Member-to-Node authorship binding inside a Circle |
| `outgoing_circle_messages` | Retry-stable prepared message UUID, Circle/Member/Node attribution, text, and authored time before network I/O |
| `circle_messages` | Per-Circle ordered accepted message, canonical request digest, exact signed request, and Anchor-signed receipt; unique message UUID and Circle sequence |
| `circle_files_contributions` | Provider-neutral whole-folder Contribution ID, request ID, provider/hosting Node identity, lifecycle/generation, and exact Owner-Member/current-root authorization proof |
| `circle_files_access_grants` | One Member Access Grant per Contribution/Member with whole-folder access, lifecycle/generation, request identity, and exact dual-signed Owner authorization proof |
| `circle_files_provider_credentials` | One exact provider credential binding per Access Grant: Circle, Contribution, Member, provider, account/ownership IDs, access, generation, pending/active/removed lifecycle, protection scheme, protected secret, and creation time |
| `circle_files_access_grant_revocations` | One immutable exact-generation revocation per Access Grant with request identity, time, and dual-signed Owner/current-root proof |
| `circle_files_lifecycle_audit_events` | Append-only redacted lifecycle requests and outcomes with Circle/Contribution, typed subject ID, stable operation/outcome tokens, bounded session count, and time |
| `circle_connections` | One versioned provider/admission/synchronization connection per joined Circle; the provider and both private endpoints are serialized together under the local OS protection scheme rather than stored as browser state or plaintext columns |

`nodes` is deliberately broader than `local_node`: admitted remote Nodes share the catalog without
redefining the daemon's singleton identity. A joined Node stores public Circle trust and its signed
receipt but does not gain private root/Anchor authority or redefine itself as the Anchor.

## Transaction and concurrency rules

- First-time schema creation, application ID, and schema version are committed together.
- Initial local Node creation inserts the Node catalog record and singleton record together.
- Initial local Node creation inserts its distinct signing credential in that same transaction.
- Circle creation atomically inserts the Circle, Owner, local Node enrollment, and idempotency
  record plus distinct Circle-root and Anchor signing credentials and matching public trust state.
- Applicant preparation creates one protected Member key and challenge per invitation. Exact retry
  returns the same IDs/credential/challenge; conflicting reuse fails closed.
- Anchor admission atomically consumes the exact invitation and inserts the Member, Node, role,
  Member/Node/transport credentials, root-signed transport binding, monotonic authority sequence,
  exact signed response, and bounded audit outcome. Exact request retry returns the stored response;
  a conflicting transcript cannot create another membership.
- Browser joiner admission atomically inserts the signed Circle/Member/Node roster, public
  authority trust, local Member credential, all Node security bindings, exact receipt, and the
  protected unsigned outer invitation connection. Exact retry validates the same connection;
  completed legacy/recovery retry fills a missing connection without contacting the owner.
  Restart retains the same identifiers and connection with no duplicate rows. The diagnostic
  control/CLI join continues to accept its explicit endpoint and does not invent a durable Files
  connection.
- Outgoing message preparation atomically fixes the request UUID, local Member/Node attribution,
  text, and authored time. An exact retry returns that record; different Circle/text reuse fails.
- Anchor acceptance atomically authorizes the Member/Node binding and inserts the next unique
  Circle sequence, canonical-message digest, signed request, and signed receipt. Exact
  retransmission returns the stored receipt; conflicting UUID or sequence reuse changes nothing.
- The sender stores that validated receipt through the same Core-owned commit port. Ordered list
  reads survive daemon/database restart on both participating Nodes.
- Contribution creation atomically stores the stable provider identity, normalized definition,
  lifecycle/generation, authorizing Owner/generation/time, exact canonical transcript, and both
  signatures. Exact request retry returns the original IDs; conflicting reuse changes nothing.
- Grant creation first proves that its Contribution and Member belong to the same Circle, then
  atomically stores whole-folder access plus the same dual-signed authorization metadata. A bad
  Member/contribution, duplicate grant, or constraint failure leaves no partial grant.
- Grant revocation atomically changes only the expected generation to `revoked` and inserts its
  immutable dual-signed proof. Exact retry returns that record; changed generation or request reuse
  changes nothing.
- Credential preparation validates the complete grant/provider binding and transactionally inserts
  one pending DPAPI-protected random secret before elevation. Exact retry returns the same
  unprotected material only to the in-process helper path; a changed binding or duplicate identity
  fails closed. Successful helper completion atomically advances the row to active. Pending state
  survives restart so recovery reuses the same password rather than creating conflicting accounts.
  The daemon serializes the complete protected preparation, elevated helper, and completion sequence
  so concurrent exact apply requests cannot treat another request's resources as their rollback prefix.
- Cleanup advances the exact provider binding to `removed` only after the platform reports
  `removed` or `already-removed`. Busy/partial outcomes retain protected recovery material across
  restart, while active credential authorization returns nothing after removal. Cleanup and exact
  mapping unmap write a `requested` audit insert before mutation; bounded results, refusals,
  failures, and cancellations append terminal inserts, while an interrupted request remains visible
  for idempotent retry. Audit inserts contain no proof, secret, subprocess output, or free-form
  diagnostic text.
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
| `invalid_private_material` | A credential algorithm, key ID, public/private binding, protection scheme, or protected blob is invalid |
| `invalid_circle_connection` | The protected joined-Circle connection has an unsupported version, malformed value, mismatched protection scheme, or unreadable blob |
| `circle_connection_conflict` | An exact joined Circle already has different saved outer invitation connection details |

## Migration policy

Migrations run one boundary at a time and transactionally: v1 adds protected Node/Circle authority
(v2), v2 adds transport and invitation state (v3), v3 adds public Circle trust and admission
state (v4), v4 adds local Member authorship plus persistent message/replay state (v5), v5 adds
provider-neutral Circle Files contributions and Member Access Grants (v6), v6 adds protected
Circle Files provider credentials (v7), v7 adds grant revocations plus lifecycle audit (v8), and
v8 adds protected joined-Circle provider/admission/synchronization connection state (v9).
Each step records its
own target version, so interruption between steps resumes from the last complete schema. A
protection or database failure rolls back that schema version and every generated row; injected
failure after the v8 DDL leaves version 7 and both lifecycle tables absent; injected failure after
the v9 DDL leaves version 8 and the connection table absent. The next successful start performs
one complete migration. Protected credentials, joined-Circle connections, and public Circle trust
are validated on every open and are never silently regenerated when unreadable.

Future schema changes must retain explicit transactional migrations, forward-version refusal, and
failure/restart tests. SQLite remains local Node state, not the Circle's network replication
protocol.

## Private material and authority export

- Key roles use distinct P-256 signing keys. Public identifiers are the accepted role-prefixed
  SHA-256 digest of canonical DER SubjectPublicKeyInfo.
- Windows protects each PKCS#8 private key with DPAPI `CurrentUser` plus Balls-specific entropy.
- Linux stores PKCS#8 only in the already verified, current-user-owned `0700`/`0600` state boundary.
  This is access control, not encryption; full-disk protection remains an operator concern.
- The stored protection scheme must exactly match the selected adapter. Unknown schemes,
  malformed blobs, changed public material, and mismatched private keys fail startup with the same
  bounded `invalid_private_material` error.
- Private material is exposed to Core consumers only through signing operations. The browser,
  local-control protocol, and ordinary CLI have no key export route.
- Joined-Circle connection payloads use the same selected OS protection adapter and are exposed
  only through the admission-state port. Startup corruption and provider/version mismatch return
  bounded errors without reflecting provider or endpoint values. Failed protection rolls back the
  entire first-join transaction, and a later exact retry can complete it.
- Explicit Circle authority export writes a versioned envelope containing separately encrypted
  root and Anchor PKCS#8 values. PBES2 uses AES-256-CBC with PBKDF2-HMAC-SHA256 and 600,000
  iterations. A root P-256 signature authenticates the exact manifest, Circle ID, authority
  generation, public credentials, KDF profile, and encrypted-value SHA-256 digests.
- Import, rotation, custody UI, and secure deletion are not implemented. Backup loss is
  unrecoverable if no accepted live authority remains.
- Authority export is the v1 backup boundary before depending on one selected Anchor. Joined Nodes
  cannot export or promote themselves because they possess only public trust and a signed membership
  receipt. There is no automatic Anchor failover.
