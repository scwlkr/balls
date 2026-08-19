# Glossary

Use these terms consistently in product, code, docs, UI, issues, and APIs.

## Balls

The overall open-source platform.

Prefer **Balls** over **Balls Server** for the new project.

## Circle

The top-level trusted group and shared digital environment.

Examples:

- Example Studio
- Example Lab
- Neighborhood Circle

A person joins a Circle.

## Member

A human identity inside a Circle.

A Member may use multiple Nodes.

## Owner / Admin

A Member with Circle-management permissions.

Governance can be centralized even when infrastructure is distributed.

## Node

A machine running Balls and participating in one or more Circles.

Examples:

- Windows laptop;
- Mac;
- Linux workstation;
- office server;
- VPS;
- cloud VM.

## Personal Node

A member-owned machine that contributes selected resources.

## Dedicated Node

A machine primarily intended to serve a Circle.

This is still a Node, not a different architecture.

## Anchor

A durable Node role.

Anchors are expected to remain online and may maintain replicated Circle state, coordination, history, or other persistent responsibilities.

Anchor is a role, not a single master server.

## Contribution

A capability or resource a Node explicitly offers to a Circle.

Examples:

- folder;
- storage;
- CPU;
- GPU;
- app hosting;
- AI inference;
- Anchor responsibility.

## Capability

Something a Member, Node, app, or service may be allowed to use or provide.

Prefer typed capabilities over broad implied trust.

## Circle Files

The Circle-level file experience.

SMB is a possible provider, not a synonym.

## Circle Files Provider

A platform implementation that exposes an approved File Contribution through Circle Files.

Examples may include SMB, synchronization, replicated storage, or a future Balls-native protocol.

_Avoid_: File system, the share

## Access Grant

A Circle authorization allowing one Member to use one Contribution at a defined access level.

Provider accounts or credentials may enforce an Access Grant, but they are not Member identity.

_Avoid_: User account, share login

## Circle AI

AI service belonging to a Circle and operating with explicitly permitted Circle context, tools, apps, and compute.

## Circle App

An application/service installed into a Circle.

Apps have declared resource requirements and permissions.

## Balls Cloud

Optional hosted infrastructure operated by the Balls project/company for convenience such as invitations, discovery, connectivity coordination, and updates.

Balls Cloud must not be synonymous with the Circle itself.

## `ballsd`

The long-running Balls Node service/daemon.

## `balls`

The canonical CLI.

## Transport

The network mechanism that lets Nodes reach one another.

Examples may include LAN and Tailscale.

Transport does not define identity.

## Control Plane

Coordination and metadata services.

May be hosted, self-hosted, or partly replicated.

## Data Plane

Actual Circle traffic/data such as files, messages, AI requests, and workloads.

## Workload

A bounded piece of execution intentionally scheduled onto a contributing Node.

## Share Compute / Circle Compute

Future capability for using explicitly contributed compute resources for workloads appropriate to distributed execution.
