# Threat Model Starter

**Status:** Phase 1 Slice 1 baseline, 2026-08-19. Review before any remote listener, invitation,
Node-to-Node transport, or credential storage is added.

## Scope

This baseline covers one unelevated Windows account running `ballsd`, the same-account `balls` CLI,
the local named-pipe control API, and the daemon's SQLite state directory. It does not claim to
secure Circle membership or traffic between machines; those paths do not exist in Slice 1.

## Assets

- persistent local Node identity;
- Circle, Member, and Node records known to this daemon;
- integrity and availability of the local database;
- authority to issue local control requests as the current Windows account;
- future Circle credentials and invitation material, once introduced.

## Trust boundaries

1. CLI or another same-account client to the daemon over a named pipe.
2. Daemon process to the dedicated local state directory and SQLite database.
3. Windows account boundary to other local users, LocalSystem, and administrators.
4. Future Node-to-Node traffic across LAN or overlay transports. This boundary is deferred.

The same Windows account is the Slice 1 local-control principal. The pipe does not distinguish
between processes running as that account.

## Current threats and mitigations

| Threat | Current mitigation | Residual risk |
| --- | --- | --- |
| Another ordinary local user sends control requests | Kestrel and the client use named pipes with `CurrentUserOnly`; the default pipe name is derived from the current user's SID | A malicious process already running as the same user can connect |
| Accidental TCP exposure | Kestrel listens only on the configured named pipe; `http://localhost` is a client URI placeholder | A future transport change could widen the boundary and requires review |
| State placed on an unsafe or substituted path | The Windows adapter rejects UNC and mapped-network paths, reparse-point paths/files, unmarked nonempty directories, and unexpected entries | A custom directory must already be under a current-user-controlled parent; retained handles and cross-user-writable parents are not a supported trust boundary |
| Another ordinary user reads or changes state | The dedicated directory and known files receive protected ACLs for the current user and LocalSystem only | Administrators, LocalSystem, offline disk access, and inherited account compromise remain powerful |
| Wrong, newer, incomplete, or corrupt SQLite state is opened | Application ID, schema version, exact schema shape, integrity, and foreign-key checks fail closed | Recovery and backup tooling do not yet exist |
| Two daemons write the same state | An exclusive `ballsd.lock` lease permits one daemon owner | A crash can leave the file, but the OS releases the lease |
| Oversized local request consumes resources | Kestrel limits request bodies to 32 KiB; the client limits buffered responses to 256 KiB | Same-user denial of service is not comprehensively addressed |
| Duplicate Circle creation after retry | A caller-provided request UUID makes creation idempotent; conflicting reuse is rejected | The UUID is not authentication or a general replay defense |
| Malformed identifiers or names reach storage | Boundary validation, typed core identifiers, length limits, and parameterized SQL | Authorization beyond the Windows account is not implemented |

## Known limitations

- Local control has no bearer credential or per-operation authorization beyond same-account pipe
  access.
- Local state is not encrypted by Balls. Operating-system disk encryption is a separate control.
- There is no remote protocol, mutual Node authentication, invitation flow, revocation, audit log,
  or message security yet.
- The state marker and ACL are safety boundaries, not proof against an administrator, LocalSystem,
  physical access, or a compromised user session.
- Use the default LocalAppData state root or another dedicated current-user-controlled parent. A
  custom path beneath a cross-user-writable parent is unsupported.
- `CurrentUserOnly` authenticates the Windows account and elevation level, but the current pipe
  provider does not yet request Windows' remote-client rejection flag. A remote SMB session with
  the same Windows SID is inside the present account-level trust boundary.
- Circle and Node UUIDs in Slice 1 are persistent identifiers, not cryptographic identities.

## Required work for the next trust boundary

Before two machines exchange Circle state, define and test:

- cryptographic Node and Circle identity and protected key storage;
- invitation issuance, expiry, single-use behavior, admission, and revocation;
- mutual peer authentication and Circle authorization independent of transport provider;
- replay, downgrade, tampering, and confused-deputy defenses;
- encrypted transport, peer identity binding, and auditable security events;
- recovery behavior for lost devices, keys, and durable Nodes.

The Node-to-Node protocol must remain secure whether connectivity comes from LAN, Tailscale, or a
future provider.
