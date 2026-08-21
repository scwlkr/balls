# Architecture Foundation

## Status

This document defines the **recommended starting architecture**, not an immutable technology constitution.

The product principles are more durable than specific language/framework choices.

Current recommendation: keep C#/.NET as the core implementation because the existing prototype already uses it and .NET is capable of cross-platform services, networking, APIs, CLI tooling, and OS integration.

Windows remains the first polished desktop platform.

## Architectural objective

Balls should be a cross-platform distributed system that can run on:

- Windows;
- Linux;
- macOS;
- headless servers;
- VPSs;
- dedicated Circle machines.

The architecture should not require those systems to pretend they are the same OS.

## Core rule

> **Balls is a protocol and platform first, a Node service second, and a GUI third.**

## Recommended system shape

```text
                         Circle
                           │
                    Circle Protocol
                           │
              ┌────────────┼────────────┐
              │            │            │
            Node         Node        Anchor Node
              │            │            │
            ballsd       ballsd       ballsd
              │            │            │
        ┌─────┼────┐  ┌────┼────┐  ┌────┼────┐
        │     │    │  │    │    │  │    │    │
       GUI   CLI Apps GUI  CLI Apps API  Apps ...
```

## `ballsd`

Every Node runs a long-lived local Balls service/daemon called `ballsd`.

`ballsd` owns local system responsibilities such as:

- Node identity;
- Circle membership;
- networking;
- local authorization;
- Circle state synchronization;
- capability advertisement;
- resource contribution;
- peer communication;
- app/service coordination;
- health and event reporting.

The GUI should not be the system.

The CLI should not be the system.

They should control/query `ballsd`.

## `balls`

`balls` is the canonical CLI.

Illustrative future commands:

```bash
balls status
balls circle list
balls circle create
balls circle join <invite>
balls members
balls nodes
balls chat
balls files
balls ai
balls apps
```

CLI and GUI should use the same application APIs.

## Browser UI

The primary GUI is one React/TypeScript browser application shared by Windows, Linux, and future
macOS Nodes. It is a client of `ballsd`; it does not own Circle behavior, persistence, networking,
or OS integration.

`ballsd` serves the bundled offline-capable UI and a narrow browser-facing API on an authenticated
loopback-only origin. `balls ui` creates a short-lived launch capability and opens the browser.
Production uses same-origin requests, strict Host/Origin validation, antiforgery protection, a
restrictive content-security policy, and no permissive CORS. The browser listener never binds to
the LAN.

The CLI continues to use the protected named-pipe or Unix-domain-socket local-control API. A future
remote browser experience uses a separate authenticated Circle endpoint; the loopback trust model
must never be exposed remotely. Native shells may wrap the same UI only when a proven OS-specific
UX need justifies them.

The implemented browser workspace lives in `web/Balls.Web`. It pins Node and pnpm, generates its
typed API edge from the committed OpenAPI document, and keeps generated DTOs and fetch behavior at
`src/api`. React components receive presentation snapshots rather than owning Circle behavior or
persistence. The accessible status, Circle list/create, Member, and Node views now consume the
same `CircleApplication` behavior as the CLI through the narrow browser adapter. A real Chromium
journey covers launch, creation, listing, and daemon restart on every required CI platform.

## Local API

`ballsd` exposes a versioned local control API.

Recommended contract qualities:

- strongly typed schemas;
- an explicit, versioned RPC contract;
- Windows local transport via named pipes where appropriate;
- Linux/macOS local transport via Unix-domain sockets where appropriate.

Phase 1 begins with versioned HTTP/JSON over local OS IPC. gRPC/Protocol
Buffers remains an option when streaming, code generation, or interoperability
requirements justify it. See `docs/decisions/0001-local-control-api.md`.

The local API supports:

- GUI;
- CLI;
- local integrations;
- testing;
- future web/desktop shells.

The local browser adapter is intentionally separate from the full local-control transport. It may
project only the capabilities needed by the UI even though it runs in the `ballsd` host.

## Remote Circle protocol

Node-to-Node communication is a separate protocol from the local control API.

Remote communication must authenticate:

- Circle;
- member;
- Node;
- requested capability/action.

Do not use IP address, hostname, or network location as identity.

Remote v1 uses signed Circle authority state plus distinct Circle-root, delegated Anchor, Member,
Node, and transport credentials. Purpose-specific canonical transcripts bind the Circle and peer
context; TLS 1.3 binds the exact transport certificate SPKI to that signed state. Transport
providers return untrusted byte streams and cannot assert Circle identity. See
[`ADR 0006`](docs/decisions/0006-trusted-circle-identity-and-admission.md) and the
[`remote Circle v1 contract`](docs/protocol/remote-circle-v1.md).

.NET 10 exposes TLS 1.3 through `SslStream` on macOS clients only. The macOS developer adapter
opts its client path into Network.framework and keeps the exact TLS 1.3 contract. It does not
downgrade remote v1 or claim a macOS Anchor/listener until a TLS 1.3 server implementation is
available. See [`ADR 0007`](docs/decisions/0007-protected-macos-developer-node.md).

## Identity model

Balls must distinguish:

### Circle identity

Persistent identity for the Circle.

Independent of any one member or Node.

The durable Circle ID is authenticated together with current signed authority state. A UUID alone
is not proof of identity, and the root credential is distinct from an Anchor's Node identity.

### Member identity

Human identity inside a Circle.

A member can own/use multiple Nodes.

A Member signing credential is distinct from all of those Nodes' credentials.

### Node identity

A particular Balls installation/device.

A Node can belong to more than one Circle.

Its Node signing credential is distinct from its replaceable, Circle-bound TLS transport key.

### App/service identity

Apps and services should eventually receive their own identities/capabilities rather than inheriting unrestricted permissions from the Node hosting them.

## Permissions

Authorization should be explicit and typed.

Example member permissions:

```text
Bob
  chat                 allow
  project-files        read/write
  accounting           deny
  circle-ai            allow
  manage-members       deny
```

Example Node contribution:

```text
Alice-PC → Example Studio
  project-folder       allow
  GPU                  allow when idle
  CPU                  max 25%
  personal-files       deny
```

Identity and resource contribution are related but separate.

## Circle state

Some information must outlive ordinary Nodes:

- Circle metadata;
- membership;
- roles;
- permissions;
- chat events;
- service/app metadata;
- replicated configuration.

Do not begin by solving global distributed consensus for every piece of state.

Use a practical replication model with Anchor Nodes.

A future design may use event logs, replicated databases, CRDTs, consensus, or a hybrid where appropriate.

Choose complexity only when the product requires it.

## Anchors

Anchor is a role that a Node can perform.

Possible Anchor responsibilities:

- persistent state;
- chat history;
- coordination;
- service discovery;
- replication;
- optional relay behavior.

A Circle may have one Anchor early and multiple Anchors later.

The model should not make the first Anchor the permanent irreplaceable "master server."

In v1, one selected Anchor may hold authoritative state and a delegated invitation key. Circle
authority remains a separate role with explicit encrypted export; losing both live authority and
its accepted export is unrecoverable rather than permission to promote an ordinary Node.

## Networking

Separate three concerns:

### Identity

Who is this Member/Node/Circle?

### Authorization

What may this identity do?

### Transport

How can packets reach the peer?

Do not mix them.

## Transport providers

Recommended initial provider strategy:

```text
Circle Protocol
      │
Transport abstraction
      │
  ┌───┼────────────┐
  │   │            │
 LAN  Tailscale   future provider
```

Tailscale can solve difficult connectivity problems now.

Balls should be able to add or replace transport mechanisms later without redefining Circle identity or application APIs.

The typed remote provider seam returns only an untrusted duplex stream plus diagnostic metadata.
The remote Circle layer applies TLS, certificate-SPKI binding, signed credentials, versioning,
replay policy, and authorization above that stream.

The first provider, `Balls.Transport.Lan`, accepts only explicit numeric private/loopback
unicast TCP endpoints and returns `lan-tcp-v1` diagnostic metadata. It performs no DNS discovery
and grants no authority from an IP address, port, interface, hostname, or provider label.

Remote v1 validates a Circle-root-signed Node-to-transport-key binding before exact TLS 1.3/mTLS
and `balls-circle/1` negotiation. Both sides then exchange a fixed encrypted Circle/sender/
expected-peer confirmation before the channel becomes usable. Versioned frames have bounded
payloads, operation IDs, per-channel duplicate rejection, timeouts, and interruption handling.
Durable application operations retain their own transactional replay defense.

The first durable application operation is a text-only Circle message. A joining Node prepares a
stable outgoing record, dual-signs the Circle/Member/Node-bound message, and sends it to the
selected Anchor over the admitted-peer channel. Core owns the persistence port. The SQLite
adapter atomically records the canonical-message digest, signed bytes, Anchor-signed receipt, and
monotonic sequence; exact retransmission returns the stored receipt and conflicting reuse rejects.
The CLI and browser read the same ordered local history through their existing local application
boundary. The initial opt-in message listener serves exactly one admitted Anchor Circle and does
not add discovery, arbitrary peer synchronization, or a remote browser endpoint.

This remote listener contract remains distinct from `ballsd` local-control named pipes/Unix
sockets and the loopback browser adapter. Issue #37 ships the provider, authenticated channel,
and explicit process/lab harness without routing local API behavior through a remote port.

Possible future work:

- Balls-hosted relay;
- direct NAT traversal;
- self-hosted relay;
- other overlay-network providers.

## Official control/coordination plane

An optional service may run at infrastructure such as:

`balls.example`

Possible responsibilities:

- invitations;
- account convenience;
- initial discovery;
- public keys;
- connectivity metadata;
- relay coordination;
- update metadata.

The control plane should not silently become the mandatory data plane for:

- Circle files;
- chat;
- AI;
- app data;
- compute workloads.

A future self-hosted implementation should be possible.

## Platform integration

Core logic talks to typed capability interfaces. Executable host composition uses a separate set
of typed host-edge contracts for protected local state and local control IPC.

```text
Balls Core
    │
Platform contracts
    │
 ┌──┼───────────┐
 │  │           │
Win Linux      macOS
```

Adapters may implement:

- filesystem integrations;
- mounts;
- credentials/keychain;
- services/daemons;
- firewall;
- hardware inventory;
- GPU inventory;
- notifications;
- process isolation;
- container/workload runtimes;
- network inspection;
- OS-specific privilege.

The implemented host edge is composed once for both `ballsd` and `balls`:

```text
ballsd / balls
      │
Balls.Host selector
      │
HostPlatform aggregate
      │
Balls.Platform contracts
      ├──────────────┬──────────────┐
      │              │              │
Windows adapter   Linux adapter   macOS adapter
```

`ballsd` also consumes the Core-owned `IPrivateMaterialProtector` capability selected by
`Balls.Host`. Separate Windows, Linux, and macOS security adapters implement that inward-facing
port. Windows uses current-user DPAPI. Linux and the initial macOS developer adapter rely on a
verified owned `0700` directory and `0600` database boundary; macOS additionally rejects extended
ACL grants. The SQLite adapter sees only the typed protection contract and stored scheme
identifier; it contains no platform commands.

`HostPlatform` supplies platform defaults plus independent seams for local-state preparation,
local-control server transport, and local-control client transport. `Balls.Host` is the only
project that selects an OS adapter. Windows, Linux, and macOS are registered; other hosts return
one typed, fail-closed selection result. Executable entry points do not perform their own OS
checks or construct adapter types.

## Privilege model

Normal `ballsd` operation should not require full-time Administrator/root privileges.

Privileged operations should be isolated and narrowly scoped.

A privileged operation should define:

- exact operation type;
- authorization;
- consent where user-visible;
- least privilege;
- validation;
- idempotency;
- logs without secrets;
- recovery/rollback;
- ownership of the changes Balls makes.

The original Windows prototype's separate privileged-helper concept is worth preserving.

## Files architecture

The product feature is **Circle Files**.

Providers may include:

- SMB;
- platform-native network mounts;
- synchronization;
- replicated storage;
- object storage;
- future Balls-native protocols.

Initial Windows implementation may use SMB.

Do not let SMB become the permanent Circle Files API.

The implemented provider-neutral foundation keeps three Core concepts separate:

- a File Contribution is one explicitly offered whole-folder capability with its own lifecycle
  (`defined`, `active`, or `retired`) and generation;
- a Circle Files Provider has a stable provider ID and hosting Node identity without embedding an
  operating-system path, share name, account, password, or provider implementation in Core;
- a Member Access Grant binds one Circle Member to one Contribution at whole-folder `Read-only`
  or `Read/write` access, with its own lifecycle and generation.

Contribution and grant creation are explicit Owner mutations. `ballsd` binds the local Member
identity and current Circle authority generation into a canonical transcript, signs it separately
with the protected Member and Circle-root keys, verifies both public credentials, and commits the
state and exact proof atomically. A stale root, substituted key, joined non-Owner, wrong-Circle
Member, or conflicting retry fails before partial state. Local-control and CLI projections expose
only public object, lifecycle, and authorizing-Member metadata; signatures, transcripts, private
authority, and future provider credentials stay behind the Core-owned persistence seam.

The browser has no Circle Files mutation route in this slice. Windows readiness, folder/share
creation, provider credentials, drive mapping, provider lifecycle transitions, and revocation are
separate adapters and tickets built on these IDs and grants.

For the files-first v1, the Windows provider uses SMB 3.1.1 with SMB1 and guest access disabled,
signing and encryption required, and one limited provider credential per Member Access Grant. A
narrow privileged helper owns exact share, account, ACL, and firewall mutations; normal daemon,
browser, CLI, and Explorer work remains unelevated.

The provider preserves application-requested SMB share modes and locks. It does not claim that an
arbitrary application becomes single-writer. The first certified matrix covers Windows 11 File
Explorer and current Word, Excel, and PowerPoint desktop behavior.

## Messaging architecture

Messaging needs:

- channels;
- direct messages;
- durable history;
- offline catch-up;
- identity-bound authorship;
- local/LAN operation where possible.

A sensible early design is durable storage on an Anchor plus synchronization to clients.

The implemented first slice deliberately proves only that foundation: one bounded plain-text
message, identity-bound Member and Node authorship, selected-Anchor order, durable idempotency,
and restart-stable local observation on both participating Nodes. It does not yet define channels,
direct messages, edits/deletes, attachments, reactions, typing, catch-up, or multiple-Anchor
replication.

Do not prematurely require a fully peer-to-peer messaging consensus algorithm.

## Apps architecture

Circle Apps should eventually declare a manifest such as:

```text
identity
version
required runtime
CPU/RAM/GPU needs
storage needs
network needs
requested Circle permissions
supported OS/architecture
```

Apps should interact with Circle capabilities through stable APIs.

The app model should allow multiple execution providers.

## Workload runtime

Balls is not itself a container platform, VM platform, or WSL platform.

It can orchestrate workloads across runtimes such as:

- native process;
- container;
- Linux container;
- VM;
- WSL-backed workload on Windows;
- AI runtime.

This is where WSL can be useful.

**WSL may run workloads for Balls. Balls should not be defined as running inside WSL.**

## AI architecture

Circle AI is a service abstraction.

It should not assume one:

- model;
- vendor;
- inference runtime;
- GPU;
- Node.

Basic flow:

```text
user / app request
       ↓
Circle AI
       ↓
permission + context resolution
       ↓
resource selection
       ↓
approved Node/runtime
       ↓
inference
```

An early scheduler can simply select one capable approved machine.

Later systems may add:

- model placement;
- GPU scheduling;
- batching;
- failover;
- multiple providers;
- distributed inference where technically appropriate.

## Compute architecture

Compute contribution requires explicit opt-in.

The eventual system needs:

- resource advertisement;
- scheduling;
- workload identity;
- isolation;
- quotas;
- cancellation;
- failure handling;
- audit trail;
- revocation.

Do not make file sharing silently enroll a computer as a worker.

## Recommended project shape

A likely starting structure:

```text
src/
  Balls.Core/
  Balls.Protocol/
  Balls.Daemon/
  Balls.Cli/
  Balls.Host/
  Balls.Platform/
  Balls.Platform.Linux/
  Balls.Platform.Windows/
  Balls.Platform.MacOS/
  Balls.Security.Linux/
  Balls.Security.Windows/

web/
  Balls.Web/                 React/TypeScript browser client

tests/
  Balls.Core.Tests/
  Balls.Protocol.Tests/
  Balls.Daemon.Tests/
  Balls.Platform.Windows.Tests/
```

The exact names can evolve.

The important part is dependency direction.

## Runtime call flow

Prefer:

```text
GUI / CLI / integrations
          │
      local API
          │
        ballsd
          │
      Balls Core
          │
  platform contracts
          │
  OS-specific adapters
```

The arrows above describe runtime calls. Compile-time dependencies point inward for product
capabilities: Core owns the capability and persistence contracts it consumes; adapters implement
those contracts and depend on Core. Core never references an adapter.

Host-edge adapters are a separate category. Local IPC clients/servers and
state-directory OS policy support the CLI or daemon composition boundary and
need not implement or reference a Core port. Their neutral contracts live in `Balls.Platform`,
the OS implementations live in `Balls.Platform.Windows` and `Balls.Platform.Linux`, and
`Balls.Host` centralizes selection. The executable projects reference the selector and contracts,
not either OS adapter.
Future Windows capability adapters that implement Core-owned ports should be split by capability
when they become real.

Avoid:

```text
GUI
 ↓
PowerShell/shell scripts
 ↓
OS-specific feature
 ↓
product logic everywhere
```

## North-star architecture test

The architecture is on the right path when:

> A Windows laptop, a Mac, a Linux VPS, and a dedicated office server can join the same Circle, advertise different capabilities, communicate through the same Circle model, and be managed through the same Balls APIs without hiding their platform differences.
