# Vision

## Mission

**Help trusted groups become more powerful together by giving them ownership and control of their shared digital environment.**

Balls exists because small groups should be able to have serious digital capability without surrendering their whole workspace to a handful of large SaaS and cloud companies.

A family, team, small company, research group, gaming group, or community already has people, computers, storage, CPUs, GPUs, networks, servers, knowledge, and data.

Balls turns those separate resources into a coherent environment that belongs to the group.

## The belief behind Balls

Modern computing made individuals powerful, but shared digital infrastructure is still strangely centralized.

Messaging is usually rented from one company.
Files are usually rented from another.
AI comes from another.
Servers, databases, storage, and collaboration tools each come from somewhere else.

The group itself rarely feels like it owns a digital place.

Balls is built around a different belief:

> **Communities can be strong.**

Trusted groups should be able to build a digital environment around themselves and become more capable as they add people, knowledge, machines, and infrastructure.

## The core abstraction

The fundamental object in Balls is a **Circle**.

A Circle is a trusted group of people with a persistent shared digital environment.

A Circle can have:

- members;
- identity and permissions;
- channels and direct messages;
- files and storage;
- Circle AI;
- apps and services;
- personal computers;
- dedicated servers;
- VPSs and cloud machines;
- contributed CPU, GPU, storage, and other resources;
- durable Anchor nodes.

People join a Circle.

Machines join a Circle as Nodes.

Nodes explicitly contribute capabilities.

The Circle survives individual machines going offline.

## North-star experience

Someone should eventually be able to receive a link or command, install Balls, enter or accept an invite, and immediately become part of the Circle.

A simple version of the experience:

```text
install Balls
      ↓
join Example Studio
      ↓
Example Studio appears
      ↓
people • chat • files • AI • apps • nodes
```

The magic is not that the user learned how to configure networking.

The magic is that **joining the Circle made their computer useful to the group.**

## Immediate proving ground

The first Circle does not need to serve every organization. It needs to help two real coworkers
share a newly created project folder over their private local network and open it in the Windows
applications they already use.

One coworker creates the Circle and folder. The other receives an invitation, joins without
configuring networking or SMB credentials, and sees the approved folder in File Explorer. The
owner verifies this experience privately before inviting the actual coworker.

Balls remains open source, and its larger Circle vision remains intact. General-purpose
integrations, remote connectivity, and additional product pillars follow the first working
company experience instead of delaying it.

## The workspace idea

For work, Balls should feel like a rethinking of what a workspace platform can be.

Google Workspace is a useful comparison, but Balls is not trying to clone Google Docs.

Google Workspace gives a group access to Google's infrastructure.

Balls gives a group a workspace built from infrastructure the group controls.

A Circle may still use external SaaS products. Balls is not an isolationist project.

The difference is that the Circle can increasingly own its core environment.

## What a Circle could become

A small company Circle might contain:

```text
Example Studio

People
  Alice
  Bob
  Casey

Chat
  #general
  #projects
  direct messages

Files
  Projects
  Standards
  Shared

Circle AI
  understands permitted company context
  searches permitted files
  understands permitted conversations
  can use approved Circle tools

Apps
  project tools
  Git
  databases
  dashboards
  internal services

Nodes
  Alice-PC
  Bob-Laptop
  Office-Server
  VPS

Compute
  contributed CPUs
  contributed GPUs
  contributed storage
```

A gaming Circle may look completely different.

A family Circle may look completely different.

The Circle should be customizable around the group rather than forcing every group into one application model.

## Circle AI

Circle AI is one of the most important long-term ideas.

It should not merely be "ChatGPT running locally."

Circle AI belongs to the Circle and can understand the information and capabilities that the Circle explicitly permits it to access.

Examples:

- find the latest Lot 14 drawings;
- summarize a project discussion;
- search company standards;
- answer questions from internal knowledge;
- interact with a Circle app;
- start an approved service;
- select an available approved GPU for inference.

The AI must obey Circle permissions. "Circle AI" never means unrestricted access.

The earliest implementation can be simple: choose one approved capable node and run inference there.

True distributed inference is a later research problem.

## Collective compute

Balls should eventually make contributed computing resources useful at the Circle level.

This does **not** mean pretending that RAM from unrelated computers becomes one giant shared address space.

It means making aggregate resources available to workloads that can actually be distributed.

Examples include:

- AI inference on the most appropriate available GPU;
- batch work distributed across workers;
- rendering;
- builds;
- background processing;
- game/service hosting;
- storage and replication;
- distributed applications designed for multiple nodes.

At very large scale, a Circle could represent enormous collective computing capacity.

That long-term possibility is part of the dream, but Balls must never fake technical capabilities that do not exist.

## Open source and ownership

Balls is open source.

Open source is part of the trust model.

A Circle should be able to understand what runs on its machines, operate its infrastructure without hidden dependencies, and eventually self-host the services needed to keep the Circle alive.

Balls may offer convenient hosted infrastructure, but Balls-the-company should not own the Circle.

## Long-term statement

In the long run:

> **Balls lets trusted groups create digital environments that become more powerful as their people, knowledge, computers, and infrastructure work together.**

The project succeeds when a small group can do things together that would normally require a pile of SaaS subscriptions, a dedicated IT team, or infrastructure owned by somebody else.
