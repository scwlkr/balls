# Decisions and Open Questions

This file separates **confirmed product decisions** from **recommended technical choices** and **intentionally open questions**.

Codex should not treat every current technical recommendation as sacred.

It should treat the confirmed product decisions as strong constraints unless explicitly changed by the owner.

## Confirmed product decisions

### Audience

The immediate proving ground is two trusted coworkers in the owner's small company.

The first outcome is a new Windows-hosted project folder on a private LAN, one shareable Circle
invitation, and usable Windows Explorer access for the invited coworker. That coworker must not
need networking or SMB administration knowledge.

The broader 2–10-person audience remains a later product direction. Open-source licensing and the
long-term Circle vision are unchanged. See
[`ADR 0008`](docs/decisions/0008-company-first-lan-pilot.md).

### Circle

The top-level object is a **Circle**.

People join Circles rather than connecting primarily to one server.

A Circle has a durable identity.

A Circle has owner/admin governance.

Infrastructure should be decentralized enough that one ordinary PC does not define the Circle.

### Messaging

Support both:

- Circle/group/channel messaging;
- direct user-to-user messaging.

History should persist.

### Files

Sharing files should become extremely easy after joining a Circle.

Initial physical-source visibility is acceptable.

Long-term unified Circle Files is desirable.

### AI

Circle AI is central and should eventually understand and act across explicitly permitted Circle context and tools.

Running AI on one best available approved Node is a valid and useful first implementation.

### Nodes and resources

Members explicitly decide what their machines contribute.

Nodes may belong to multiple Circles.

Dedicated servers and VPSs are valid Nodes.

### Anchors

A Circle can have durable Anchor Nodes.

Anchors are roles, not one permanent master server.

### Apps

Circle Apps are part of the long-term product.

Different Circles can enable very different apps and capabilities.

### Offline behavior

LAN-hosted Circle functionality should continue where possible when internet connectivity is unavailable.

### Open source

Balls is open source.

Apache License 2.0 is the accepted source license. Incoming contributions use the same terms
without a copyright assignment or CLA unless a later recorded business need changes that policy.

### Hosted service

An official Balls service is acceptable if basic operation can remain effectively free/very inexpensive and the Circle does not become owned by that service.

Expected domain may be something like `balls.example`.

## Recommended technical choices

These are current best recommendations and may change with evidence.

### Fresh repository

Create `scwlkr/balls` as the canonical project.

Do not fork/copy Git history from `balls-server`.

### C#/.NET core

Keep .NET initially to preserve useful experience from `balls-server` while gaining cross-platform service/CLI/API support.

Re-evaluate only if a concrete engineering reason appears.

### Native host daemon

Run `ballsd` natively on Windows, Linux, and macOS.

Do not make WSL the foundational host runtime.

The initial macOS claim is Apple-Silicon source-run development through the same local browser UI,
with dedicated protected state/IPC adapters and no native GUI. See
[`ADR 0007`](docs/decisions/0007-protected-macos-developer-node.md).

### WSL

Use WSL as an optional Windows workload runtime where useful.

### API

Strongly typed, versioned APIs.

Phase 1 starts with versioned HTTP/JSON over protected local OS IPC because its
initial calls are unary and benefit from direct inspectability. gRPC/Protocol
Buffers remains a candidate when streaming, code generation, or
interoperability requirements justify it. See
[`ADR 0001`](docs/decisions/0001-local-control-api.md) and the implemented
[`local-control v1 contract`](docs/protocol/local-control-v1.md).

### Phase 1 Slice 1 implementation checkpoint

As of 2026-08-19, the first local vertical slice is implemented on Windows:

- `ballsd` and `balls` communicate through versioned HTTP/JSON over a same-user named pipe;
- Node and Circle identities persist in local SQLite state;
- Circle creation atomically records one Owner and enrolls the local Node;
- the CLI can inspect daemon status and list Circles, Members, and Nodes;
- a dedicated marked state directory, Windows ACL protection, database application ID, exact
  schema validation, and an exclusive daemon lease fail closed on unsafe state.

This is a checkpoint, not Phase 1 completion. Invitation/admission, join, authenticated
Node-to-Node transport, two-machine membership, and persistent messaging remain next-slice work.
See the [`Slice 1 design`](docs/design/phase-1-slice-1.md),
[`ADR 0002`](docs/decisions/0002-protected-local-state.md), and
[`SQLite local-state v1`](docs/storage/sqlite-local-state-v1.md).

### Local state

SQLite is the first local durable-state provider, not the definition of Circle-wide replicated
state. Each daemon owns a dedicated platform-protected directory, and the store identifies and
validates its schema before use. Unknown, future, incomplete, or corrupt state is left unchanged
and startup fails. See [`ADR 0002`](docs/decisions/0002-protected-local-state.md).

### Versioning

Product binaries follow Semantic Versioning from one repository-wide version; Slice 1 is
`0.1.0-alpha.1`. Wire/API path versions and storage schema versions remain independent
compatibility axes. A product version bump must not silently redefine either contract.

### Transport

Use a transport abstraction.

LAN + Tailscale are sensible early providers.

Do not make Tailscale a permanent definition of Balls.

### Trusted Circle identity and admission

Remote v1 keeps UUID object identifiers separate from cryptographic proof. The Circle root,
delegated Anchor issuer, Member, Node, and transport certificate use distinct ECDSA P-256/SHA-256
credentials identified by role-scoped hashes of canonical DER SPKI. Signed purpose-specific
binary transcripts and exact Circle/Node/TLS context produce deterministic admission rejection.

TCP plus `SslStream` TLS 1.3 is the first authenticated channel. Admission pins the server
transport key from the signed invitation; admitted peers use mTLS bound to active Circle-signed
Node transport credentials. LAN, Tailscale, and future providers remain untrusted stream
providers. Circle authority requires explicit encrypted export and never silently transfers to an
ordinary Node. See [`ADR 0006`](docs/decisions/0006-trusted-circle-identity-and-admission.md) and
the [`remote Circle v1 contract`](docs/protocol/remote-circle-v1.md).

### Minimal persistent Circle messaging

Use the selected Anchor as the v1 ordering authority for a bounded text-only Circle history.
Every message binds its stable UUID, Circle, author Member, author Node, protocol-second UTC time,
and UTF-8 text in one canonical transcript signed independently by the Member and Node. The
Anchor authenticates the admitted Node with remote-v1 mTLS, authorizes the Member-to-Node binding,
assigns a monotonic Circle sequence, signs a receipt, and atomically stores replay state and the
message. Sender preparation is durable so an exact retry after interruption reuses the authored
identity and content; conflicting UUID reuse fails closed. This is a vertical proof, not rich chat:
channels, direct messages, edits, deletes, attachments, reactions, typing state, multi-Anchor
replication, and offline multi-peer synchronization remain deferred.

### Cross-platform UI

Use one React/TypeScript browser application as the primary GUI and retain `balls` as a first-class
automation interface. `ballsd` serves the bundled UI through an authenticated loopback-only
adapter; native shells are deferred until a proven OS-specific need appears. See
[`ADR 0004`](docs/decisions/0004-local-typescript-browser-ui.md).

### Files-first v1

The first supported release focuses on Circle creation/join, Member and Node visibility, the local
browser UI and CLI, and one secure Windows Explorer Circle Files provider. The immediate company
pilot requires the invited non-Owner Member to receive actual authorized access on a separate
Windows computer without manually handling provider credentials. One Anchor may be authoritative
without automatic failover. Rich chat, replication, macOS polish, AI, Apps, and compute remain
later milestones. See [`ADR 0005`](docs/decisions/0005-files-first-v1.md) and
[`ADR 0008`](docs/decisions/0008-company-first-lan-pilot.md).

### Development and release model

Use GitHub Issues for executable tickets, one active milestone, no more than two non-overlapping
tickets in progress, short-lived pull requests, green-main Canary artifacts, outcome-based Alphas,
risk-triggered heavy tests, and explicit owner acceptance for public publication and Stable
releases. See [`docs/development-process.md`](docs/development-process.md).

## Intentionally open technical questions

These should be resolved by prototypes, threat modeling, and performance/testing rather than ideology.

### Durable state model

Possible approaches include:

- Anchor-hosted database;
- append-only event log;
- replicated database;
- CRDTs;
- consensus for selected state;
- hybrid architecture.

Do not choose full distributed consensus merely because the word "decentralized" sounds attractive.

For v1, one selected Anchor may hold authoritative Circle state while other Nodes retain their own
identities and membership records. Authority backup/export is required; automatic failover and
multiple-Anchor replication remain open.

### Hosted control plane scope

No Balls Cloud dependency is planned for v1. Invitations are exchanged directly; LAN and an
already configured Tailscale network provide initial reachability. The minimum future official
service needed for exceptional onboarding remains open.

### Circle Files implementation

The v1 Windows provider uses authenticated SMB 3.1.1 with separate limited Access Grants. Normal
application/SMB locking is preserved; universal single-writer enforcement is not promised. The
provider initially exposes one live contributed folder without replication, sync, version history,
or managed trash.

The long-term unified filesystem/sync/storage architecture remains open.

### App runtime

Could include:

- native processes;
- containers;
- VM-backed apps;
- WSL;
- platform-specific runners.

### Large-scale distributed compute

This is research territory and should be approached workload by workload.

### Public repository transition

The repository remains private until the active public-readiness milestone adds the canonical
Apache 2.0 license, replaces identifying examples with fictional data (including history where
needed), audits for private material, passes the full gate, and receives a final owner confirmation.

## Decision rule

When deciding between two technical designs, prefer the design that:

1. preserves the Circle abstraction;
2. keeps trust explicit;
3. keeps APIs stable;
4. keeps platform-specific behavior isolated;
5. can be tested on real machines;
6. solves the current milestone without making the future impossible.
