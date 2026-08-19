# Phase 1 Slice 1 — Create a Local Circle

## Status

Implementation checkpoint recorded on 2026-08-19.

This is the first implementation slice, not completion of Phase 1.

## System summary

Balls is a Circle-first distributed platform for trusted groups. A Circle owns
the durable shared environment; Members are people in that Circle; Nodes are
machines that participate in one or more Circles. Each Node runs `ballsd`, and
human-facing tools such as `balls` use a versioned local control API instead of
containing product or persistence logic themselves.

## Architectural invariants

- Circle, Member, and Node identities are distinct typed concepts.
- One `ballsd` installation owns one persistent local Node identity.
- Creating a Circle atomically creates the founding owner Member and enrolls
  the local Node. The Member owns the role; the Node does not.
- A Node can participate in multiple Circles without leaking Circle-scoped
  state between them.
- Display names, hostnames, paths, and network addresses are labels or
  locations, never identity.
- The initial host is not encoded as a permanent master. Anchor selection and
  replication remain later responsibilities.
- The local control API is separate from the future authenticated Node-to-Node
  Circle protocol.
- Core code has no ASP.NET Core, filesystem, SQLite, shell, or Windows types.
- Storage and future capability adapters implement Core-owned ports and depend
  inward on Core. Outer host adapters such as local IPC and state-directory
  protection do not become Core dependencies. The daemon composes both kinds.
- `ballsd` and `balls` run unelevated.

The identifiers in this slice are persistent identifiers, not yet
cryptographic proofs of identity. Admission and authenticated membership must
not be inferred from possession of an identifier.

## User outcome

On one Windows account, a user can start `ballsd`, inspect the persistent local
Node, create a named Circle with a founding owner, and list that Circle's
Members and Nodes through `balls`. Restarting the daemon preserves every
identifier and relationship.

Illustrative flow:

```powershell
ballsd
balls status
balls circle create "Example Studio" --owner "Alice"
balls circle list
balls member list --circle <circle-id>
balls node list --circle <circle-id>
```

## Repository shape

```text
src/
  Balls.Core/               domain, use cases, and inward-facing ports
  Balls.Protocol/           versioned local-control DTOs and routes
  Balls.Storage.Sqlite/     transactional local-state adapter
  Balls.Platform.Windows/   Windows host IPC and state-directory policy
  Balls.Daemon/             ballsd host and composition root
  Balls.Cli/                balls command-line client
tests/
  Balls.Core.Tests/
  Balls.Storage.Sqlite.Tests/
  Balls.Daemon.Tests/
  Balls.Cli.Tests/
```

Linux, macOS, desktop UI, peer transport, and Anchor projects are not created
until they contain real behavior.

## Local control contract

- HTTP/1.1 and JSON with explicit `/control/v1` routes and DTOs.
- ASP.NET Core Minimal APIs hosted on a Kestrel named pipe on Windows.
- The pipe is restricted to the same unelevated user with
  `CurrentUserOnly = true`; `ballsd` opens no TCP listener.
- Protocol DTOs are not domain or persistence objects.
- Errors have stable machine-readable codes and safe human-readable messages.
- Request sizes, cancellation, and client timeouts are bounded.

See [ADR 0001](../decisions/0001-local-control-api.md) and the implemented
[local-control v1 contract](../protocol/local-control-v1.md).

## Persistence

SQLite is a local durable-state provider, not the future Circle replication
model. `ballsd` is the sole writer for one data directory.

- Schema changes are versioned and transactional.
- Circle creation, founding membership, and local Node enrollment commit in
  one transaction.
- Foreign keys are enabled.
- Durable writes use WAL mode with full synchronization.
- A data-directory lock prevents two daemons from writing the same state.
- Unknown future schema versions and corrupt data fail closed; startup never
  silently replaces state.

See [ADR 0002](../decisions/0002-protected-local-state.md) and the implemented
[SQLite local-state v1 contract](../storage/sqlite-local-state-v1.md).

## Acceptance checks

Automated checks must prove:

1. Circle creation stores a separate Circle, owner Member, and local Node
   enrollment atomically.
2. Invalid or blank names are rejected without partial state.
3. Retrying the same creation request is idempotent and does not duplicate a
   Circle; reusing its key for different input is rejected.
4. Reopening the same data directory preserves Node, Circle, Member, and
   enrollment identifiers.
5. Separate data directories produce distinct Node identities.
6. Multiple Circles on one Node remain Circle-scoped.
7. Concurrent creation requests produce no lost or partial state.
8. An unsupported schema version and corrupt database fail without reset.
9. Representative v1 JSON remains compatible with additive unknown fields.
10. Real HTTP requests over the named pipe exercise the daemon contract.
11. A separate `balls` process exercises the daemon and produces stable exit
   codes for success, usage errors, unavailable daemon, and rejected requests.
12. The daemon binds no TCP control endpoint, including when ambient Kestrel
    endpoint configuration is present.

Manual Windows evidence must start daemon and CLI as separate processes,
create and inspect a Circle, restart `ballsd`, repeat the queries, and confirm
the identifiers are unchanged. It must also confirm the daemon runs from a
standard user token and owns no listening TCP socket.

## Explicit non-goals

- Invitations, admission, revocation, or cryptographic membership proofs.
- Node-to-Node transport, discovery, relay, or synchronization.
- Two-machine participation or real-machine Phase 1 exit evidence.
- Messages, channels, direct messages, or Anchor replication.
- Files, AI, Apps, resource contribution, or distributed compute.
- Linux/macOS runtime support or a desktop UI.
- A hosted control plane.

## Deferred decisions

- Key and certificate model for Circle, Member, and Node authentication.
- Invite encoding and admission ceremony.
- Remote Circle protocol and transport providers.
- Durable event, replication, conflict, and multiple-Anchor models.
- Cross-platform local IPC adapter details.
- Desktop framework and installation/service lifecycle.

The next Phase 1 slices must add trusted create/join across two Windows
machines and a simple persistent Circle message path before Phase 1 can be
called complete.
