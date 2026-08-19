# Roadmap

## Purpose

The roadmap protects two things at once:

1. the enormous long-term vision;
2. the need to build small, real, testable vertical slices.

Do not implement the whole platform in parallel.

Do not reduce the mission to the current milestone.

## Phase 0 — Foundation

**Goal:** start the new `scwlkr/balls` repository without inheriting architectural baggage.

Deliver:

- foundational docs;
- project skeleton;
- core dependency rules;
- CI;
- test conventions;
- semantic versioning approach;
- threat-model starter;
- local developer workflow.

No need for a polished GUI yet.

## Phase 1 — First Circle

**Goal:** prove the core abstraction.

A user can:

- install/run Balls on two Windows machines;
- create a Circle;
- invite another trusted person/device;
- join the Circle;
- see Members;
- see Nodes;
- exchange a simple persistent message;
- establish trusted local connectivity.

This phase should prove Circle identity and Node identity before adding many features.

### Exit idea

Two real machines can join one Circle and still recognize that Circle after restart.

## Phase 2 — Useful Small-Team Workspace

**Goal:** make the Circle genuinely useful for the 2–5 person company.

Add:

- channels;
- DMs;
- durable history;
- initial Circle Files;
- simple roles/permissions;
- membership revocation;
- one durable Anchor;
- good Windows UX.

The original `balls-server` SMB work can be referenced/ported selectively here.

### Exit idea

A small company can use Balls daily for private messaging and shared files.

## Phase 3 — Cross-platform Nodes

**Goal:** prove Balls is a platform, not a Windows application.

Add supported Node implementations for:

- Linux;
- macOS;
- headless/server installation;
- VPS Node.

Windows can remain the strongest GUI platform.

### Exit idea

Windows + Mac + Linux/VPS can all participate in the same Circle.

## Phase 4 — Circle AI

**Goal:** give the Circle shared intelligence.

Start simple:

- register an AI provider/runtime;
- advertise capable GPU Nodes;
- permit Circle members to use it;
- permission approved files/context;
- select an approved execution Node;
- expose AI in Circle UX.

Do not require distributed inference.

### Exit idea

A coworker can be told: "this is our company AI," and it genuinely understands permitted Circle information and runs on Circle-controlled infrastructure.

## Phase 5 — Apps and Services

**Goal:** turn the Circle into an extensible environment.

Add:

- app manifest;
- permissions;
- service discovery;
- workload placement;
- app lifecycle;
- persistent storage bindings;
- networking rules.

Start with one compelling real app.

Possible examples:

- Minecraft;
- internal dashboard;
- Git service.

### Exit idea

Adding a service to a Circle is materially easier than manually deploying it onto a random computer.

## Phase 6 — Resilience and Multiple Anchors

**Goal:** remove single durable-node assumptions.

Add:

- multiple Anchors;
- replicated state;
- failure recovery;
- authority transfer;
- backup/restore;
- self-hosted control-plane path.

### Exit idea

Losing one Anchor does not destroy the Circle.

## Phase 7 — Circle Compute

**Goal:** make contributed resources broadly useful.

Add:

- compute advertisement;
- worker enrollment;
- quotas;
- scheduler;
- workload isolation;
- cancellation;
- auditing;
- failure handling.

Start with embarrassingly parallel/distributable workloads.

### Exit idea

A Circle can intentionally distribute appropriate jobs across approved machines.

## Long-term horizon

At scale, Balls may support Circles containing:

- thousands of Members;
- thousands of Nodes;
- substantial distributed storage;
- substantial aggregate CPU/GPU capacity;
- specialized Circle AI;
- rich app ecosystems.

This horizon should guide architecture.

It should not dictate premature implementation complexity.

## Roadmap rule

Every milestone must answer:

1. What useful user experience becomes possible?
2. What architectural capability is proven?
3. What is explicitly not being built yet?
4. What real-machine evidence proves completion?
