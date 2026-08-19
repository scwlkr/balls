# Decisions and Open Questions

This file separates **confirmed product decisions** from **recommended technical choices** and **intentionally open questions**.

Codex should not treat every current technical recommendation as sacred.

It should treat the confirmed product decisions as strong constraints unless explicitly changed by the owner.

## Confirmed product decisions

### Audience

Initial sweet spot: 2–10 trusted people.

Primary first environment: a 2–5 person small company.

Users are expected to be at least somewhat technical.

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

### WSL

Use WSL as an optional Windows workload runtime where useful.

### API

Strongly typed, versioned APIs.

gRPC/Protocol Buffers is a recommended starting point.

### Transport

Use a transport abstraction.

LAN + Tailscale are sensible early providers.

Do not make Tailscale a permanent definition of Balls.

### Windows UI

WPF can remain the initial Windows desktop technology if it accelerates shipping.

Do not let WPF define the core.

## Intentionally open technical questions

These should be resolved by prototypes, threat modeling, and performance/testing rather than ideology.

### Circle identity implementation

Possible approaches include:

- keypairs;
- signed membership records;
- certificate-based identity;
- external identity plus Circle keys;
- hybrids.

### Durable state model

Possible approaches include:

- Anchor-hosted database;
- append-only event log;
- replicated database;
- CRDTs;
- consensus for selected state;
- hybrid architecture.

Do not choose full distributed consensus merely because the word "decentralized" sounds attractive.

### Hosted control plane scope

The minimum official service needed for exceptional onboarding is not yet fixed.

### Desktop cross-platform UI

No permanent UI framework has been chosen for macOS/Linux.

### Circle Files implementation

SMB is useful on Windows.

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

## Decision rule

When deciding between two technical designs, prefer the design that:

1. preserves the Circle abstraction;
2. keeps trust explicit;
3. keeps APIs stable;
4. keeps platform-specific behavior isolated;
5. can be tested on real machines;
6. solves the current milestone without making the future impossible.
