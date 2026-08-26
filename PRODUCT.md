# Product Definition

## Product

**Balls is an open-source graphical integration platform for trusted Circles.**

A Circle is a private shared computing environment whose capabilities are supplied by the people
and infrastructure inside it. Members use those approved capabilities without configuring the
underlying computers or providers.

Balls is people-first. Nodes matter because of what they make possible for the Circle.

## Initial user

Design the immediate private pilot obsessively for:

- approximately two or three personally trusted people in one small company;
- software obtained only through [`balls.wlkrlabs.com`](https://balls.wlkrlabs.com), pointing to an
  exact owner-accepted GitHub Release;
- a newly created project folder hosted by one Windows Node on the private LAN;
- one invitation that makes the approved folder usable from the boss's separate Windows PC;
- ordinary Windows File Explorer and the company's existing project applications.

Neither person should handle PowerShell, daemon arguments, IP addresses, ports, SMB accounts,
provider grants, object IDs, plan tokens, or drive-mapping plans. Exact Windows elevation remains
acceptable for an Owner-approved host mutation, but contribution and use are graphical and
provider-jargon-free by default.

After that boss demo, the first complete ecosystem proof is one joined Member using Circle Files,
Circle Messaging, and Circle AI hosted by another Node, followed by coherent access revocation.
The broader audience and future integrations remain long-term goals.

Balls should not require every user to be a systems administrator.

At the same time, it should not hide useful technical information from users who want to understand or customize the system.

**Simple by default. Inspectable by design.**

## Top-level UX

Balls opens into Circles, not into a server dashboard.

Example:

```text
Your Circles

Example Studio
3 people • 5 nodes

Example Lab
2 people • 3 nodes

Neighborhood Circle
6 people • 9 nodes
```

Opening a Circle should eventually expose a stable set of product areas:

```text
Home
Chat
Files
AI
Apps
People
Nodes
```

Not every Circle must enable every area.

## Membership

A Circle has a durable identity independent of any one computer.

A Circle has owners/admins.

Admins can:

- invite and remove members;
- approve or revoke nodes;
- control roles and permissions;
- manage Circle services;
- manage apps;
- manage resource policy.

Removing a member should revoke their Circle access coherently rather than requiring separate cleanup in every subsystem.

## Messaging

Circle communication includes:

- channels;
- group conversations;
- direct member-to-member messages;
- durable history;
- offline synchronization.

Messages belong to Circle identities, not to physical computers.

LAN messaging should continue when the internet is unavailable if reachable Circle infrastructure remains online.

## Files

Circle Files should make file access easy.

The earliest implementation may expose physical sources clearly:

```text
Example Studio Files
  Alice-PC
  Office-Server
  VPS
```

The long-term goal is a more unified shared-files experience where users do not need to care which node physically stores every file.

The product abstraction is **Circle Files**.

SMB, sync, mounts, object storage, replication, and future protocols are implementation providers underneath that abstraction.

### Circle Files provider foundation

The implemented Windows provider foundation lets two trusted Members join one Circle and work in
the same newly contributed folder through Windows File Explorer. The Private Boss Demo must prove
that a second, ordinary Member can receive and use approved access graphically on a separate Windows
computer; an Owner mapping a share on its own hosting Node is not sufficient evidence.

The initial provider is authenticated SMB 3.1.1 on a Windows hosting Node. Each Member receives a
separate limited Access Grant with whole-folder `Read/write` or `Read-only` access. LAN access
ships before an optional Tailscale path. The normal applications opening a file determine its SMB
sharing/locking behavior; Balls certifies named application scenarios rather than promising a
universal single-writer lock.

This provider is one live folder on one Node. Replication, offline synchronization, conflict
merging, version history, and Balls-managed trash are later capabilities. The product must expose
that limitation honestly and must never delete user files while removing Balls-owned access.

## Nodes

A Node is a computer or server participating in a Circle.

Possible Nodes include:

- Windows desktop;
- Windows laptop;
- Mac;
- Linux workstation;
- Linux server;
- home server;
- NAS;
- Raspberry Pi-class device;
- VPS;
- cloud VM.

A Node may belong to multiple Circles.

A machine's participation in one Circle does not automatically expose it to another.

## Resource contribution

Joining a Circle does not give that Circle ownership of the computer.

Contribution is explicit.

Example:

```text
Alice-PC → Example Studio

Project folder        allowed
GPU                   allowed when idle
CPU                   max 25%
Storage               500 GB
Personal files        denied
Remote desktop        denied
```

Different Circles may receive different contributions from the same Node.

## Dedicated Nodes

A Circle may have machines primarily dedicated to the Circle.

Examples:

- office server;
- home server;
- VPS;
- dedicated GPU machine.

Dedicated Nodes still use the same Node model. They simply advertise more capabilities and are expected to remain available.

## Anchors

An **Anchor** is a durable Node role.

Anchors are expected to stay online and may hold replicated Circle responsibilities such as:

- membership state;
- Circle metadata;
- chat history;
- service discovery;
- app state;
- coordination;
- relaying.

An Anchor is not "the central Balls Server."

A Circle can have multiple Anchors.

The goal is to avoid one ordinary user PC becoming the single point whose shutdown kills the Circle.

## AI

Circle AI belongs to the Circle.

It can use only explicitly permitted:

- files;
- conversations;
- app APIs;
- services;
- tools;
- compute resources.

The first useful implementation does not require distributed inference.

Balls can select the best available approved Node and make that model available to the Circle.

Later versions may add sophisticated schedulers and distributed workloads.

Circle AI is not Balls Wizard. Circle AI is contributed by an approved Node and made available to
other authorized Members without exposing runtime addresses or credentials.

## Balls Wizard

Balls Wizard is an optional local product guide inside the Balls browser interface. Its character
is a floating brand-violet ball wearing a wizard hat, derived from the canonical
[`balls-brand.png`](balls-brand.png) visual language.

When Balls Wizard is absent, a small bottom-right prompt offers to download it. Balls never
downloads its model automatically. After the user opts in, Balls retrieves a pinned quantized
instruction-tuned Gemma 4 E2B model and integrates it without requiring model or runtime setup.

Balls Wizard runs on the computer asking the question. It retrieves user documentation matching
the installed Balls version and can answer questions such as “How do I remove a Member from this
Circle?” with links to the relevant guidance. It begins as read-only assistance, not an authority
that silently performs administrative changes. Core Balls remains fully usable without it.

## Apps

A Circle may install applications and services.

Examples:

- Minecraft;
- local AI;
- Git hosting;
- media server;
- project dashboard;
- database;
- internal company tool;
- custom app.

An app should declare:

- required compute;
- storage;
- network needs;
- platform/runtime requirements;
- requested Circle permissions.

The Circle approves those permissions.

Apps should be able to integrate with other Circle capabilities through stable APIs rather than custom one-off glue.

## Balls Cloud

Balls may provide an official hosted coordination service, likely under infrastructure such as:

`balls.example`

The hosted service may provide:

- invitations;
- identity convenience;
- initial discovery;
- NAT/relay coordination;
- update metadata;
- optional public-key directory;
- convenience account management.

The hosted service should be inexpensive enough to operate that basic use can remain effectively free.

The architectural principle is:

> **Balls may provide infrastructure, but Balls must not own the Circle.**

A self-hosted coordination path should be possible as the system matures.

## Offline and LAN behavior

Local capability is important.

If the internet goes down but members and Anchor infrastructure remain reachable over the LAN, locally hosted parts of the Circle should continue operating wherever possible.

Examples:

- LAN chat;
- local files;
- local AI;
- locally hosted apps;
- local service discovery.

"Local first" does not mean "local only."

Remote nodes, VPSs, and cloud machines are valid Circle infrastructure.

## Success

Balls is succeeding when a user can invite a trusted coworker and, with very little setup, gain a private shared environment where:

- both people appear;
- both machines appear;
- they can message each other;
- shared files are immediately useful;
- approved services can run;
- technical users can inspect what is happening;
- future capabilities can be added without rebuilding the foundation.

The private product gate is stricter: from the official download, a fresh Member must join, open an
approved folder, send a Circle message, and use Circle AI hosted by another Node without handling
provider configuration. If this is not materially easier than assembling existing tools, platform
expansion pauses and the product direction is reconsidered.
