# Glossary

Use these terms consistently in product, code, docs, UI, issues, and APIs.

## Balls

The overall open-source platform.

Prefer **Balls** over **Balls Server** for the new project.

## Circle

The top-level trusted group and shared digital environment.

It lets Members discover and use explicitly contributed capabilities from the group's Nodes
without configuring the underlying machines or providers.

Examples:

- Example Studio
- Example Lab
- Neighborhood Circle

A person joins a Circle.

## Member

A human identity inside a Circle.

A Member may use multiple Nodes.

## Membership

A person's admitted relationship to one Circle. Membership establishes Circle identity; it does
not by itself authorize every capability or contribute anything from the person's Nodes.

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

## Circle Authority

The signed root of trust for one Circle. It binds the Circle ID to current delegated Anchors,
Members, Nodes, transport credentials, generations, and revocations.

Circle Authority is a cryptographic role, not a synonym for the Anchor Node or Owner's computer.

## Credential

A role-scoped public key plus signed authority binding used to authenticate a Circle, Anchor,
Member, Node, or transport certificate.

A durable object UUID is an identifier, not a credential or proof of identity.

## Invitation

A bounded, signed authorization to attempt joining one Circle. Remote v1 invitations name their
issuer and authority generation, expire, permit one redemption, and pin the admission Anchor's
transport key.

An invitation is not membership; admission must still prove the applicant Member and Node keys.

## Admission

The authenticated, replay-protected operation that consumes one valid invitation and creates
Circle membership. Validation and durable mutation are separate boundaries.

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

An explicitly bounded resource or action that a Node, app, or service provides to a Circle or that
a Member may be permitted to use.

Prefer typed capabilities over broad implied trust.

## Capability Grant

A Circle authorization allowing one Member to use one Capability within an explicit scope.
Membership alone does not imply a Capability Grant.

## Circle Files

The Circle-level file experience.

SMB is a possible provider, not a synonym.

## Circle Files Provider

A platform implementation that exposes an approved File Contribution through Circle Files.

Examples may include SMB, synchronization, replicated storage, or a future Balls-native protocol.

_Avoid_: File system, the share

## Access Grant

The Circle Files form of a Capability Grant, allowing one Member to use one File Contribution at a
defined access level.

Provider accounts or credentials may enforce an Access Grant, but they are not Member identity.

_Avoid_: User account, share login

## Circle AI

An explicitly contributed AI capability that belongs to a Circle and operates with permitted
Circle context, tools, apps, and compute. It may run on one Node and serve other authorized Members.

Circle AI is not Balls Wizard.

## Balls Wizard

The optional on-device Balls product guide. It appears as a floating brand-violet ball wearing a
wizard hat, retrieves version-matched Balls user documentation, and explains how to use the product.

Balls Wizard runs locally on the requesting Node and is installed only after the user selects its
download prompt. It is not Circle AI and is not an authority for Circle administration.

## Circle Messaging

Human communication among Circle Members. Protocol traffic between Nodes, apps, and services is
not Circle Messaging.

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

## Capability Provider

An implementation integrated beneath a Circle Capability. A provider realizes the capability but
does not define Circle identity, Membership, or authorization.

## Coherent Access Revocation

One revocation intent reconciled across every reachable Balls-managed capability. It stops future
authorization and reports incomplete provider cleanup honestly; it cannot erase copies outside
Balls' control.

## Control Plane

Coordination and metadata services.

May be hosted, self-hosted, or partly replicated.

## Data Plane

Actual Circle traffic/data such as files, messages, AI requests, and workloads.

## Workload

A bounded piece of execution intentionally scheduled onto a contributing Node.

## Share Compute / Circle Compute

Future capability for using explicitly contributed compute resources for workloads appropriate to distributed execution.
