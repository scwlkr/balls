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

## Desktop UI

The initial Windows desktop application may remain WPF if that is the fastest path.

Do not allow WPF types, Windows commands, or desktop lifecycle assumptions into Circle/core logic.

A future cross-platform desktop UI can be selected independently.

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

## Remote Circle protocol

Node-to-Node communication is a separate protocol from the local control API.

Remote communication must authenticate:

- Circle;
- member;
- Node;
- requested capability/action.

Do not use IP address, hostname, or network location as identity.

## Identity model

Balls must distinguish:

### Circle identity

Persistent identity for the Circle.

Independent of any one member or Node.

### Member identity

Human identity inside a Circle.

A member can own/use multiple Nodes.

### Node identity

A particular Balls installation/device.

A Node can belong to more than one Circle.

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

Core logic talks to typed platform interfaces.

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

## Messaging architecture

Messaging needs:

- channels;
- direct messages;
- durable history;
- offline catch-up;
- identity-bound authorship;
- local/LAN operation where possible.

A sensible early design is durable storage on an Anchor plus synchronization to clients.

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
  Balls.Platform.Windows/
  Balls.Platform.Linux/
  Balls.Platform.MacOS/
  Balls.Desktop.Windows/

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

The arrows above describe runtime calls. Compile-time dependencies point
inward: Core owns the platform and persistence contracts it consumes;
adapters implement those contracts and depend on Core; the daemon references
and composes the selected adapters. Core never references an adapter.

Host-edge adapters are a separate category. Local IPC clients/servers and
state-directory OS policy support the CLI or daemon composition boundary and
need not implement or reference a Core port. `Balls.Platform.Windows` contains
that host-edge behavior in Slice 1; future Windows capability adapters that
implement Core-owned ports should be split by capability when they become real.

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
