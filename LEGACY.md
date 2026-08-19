# Relationship to archived `balls-server`

## Repository status

[`scwlkr/balls-server`](https://github.com/scwlkr/balls-server) was retired and publicly archived
on August 19, 2026. Its sanitized public history, three pilot tags, prerelease assets, and one
unmerged WIP preservation branch remain available as historical provenance. The default branch
and preservation branch both refuse installation, and every pilot release is explicitly marked
retired and unsupported.

The archive was not deleted, renamed, made private, merged into this repository, or presented as a
completed v0.4.0 release.

## Original prototype

`scwlkr/balls-server` is the original Windows file-sharing project that led to Balls.

It explored:

- safe Windows SMB sharing;
- LAN/Tailscale connectivity;
- Windows readiness checks;
- Windows UI;
- limited identities;
- privileged helper boundaries;
- installation/update workflows;
- safety and testing.

That work remains useful.

## Why the new repo exists

Balls is now a broader product:

- Circle-first;
- people-first;
- cross-platform;
- API/CLI foundational;
- messaging;
- files;
- AI;
- apps;
- services;
- resource contribution;
- distributed compute horizons.

Trying to evolve every old architectural assumption into this platform would create unnecessary baggage.

## Policy

The new `balls` repository should:

- start with clean Git history;
- not fork `balls-server`;
- not blindly copy its source tree;
- reference it as prior art;
- deliberately port useful implementations when the new architecture needs them.

## What is worth preserving

Especially valuable ideas from `balls-server` include:

- least privilege;
- explicit user consent for system mutations;
- a narrow privileged helper;
- isolating OS observations behind interfaces;
- clear ownership of system changes;
- robust repair/removal behavior;
- real-machine verification;
- avoiding unsafe public SMB exposure.

## What should not be inherited automatically

Do not inherit as foundational assumptions:

- Windows-only product identity;
- WPF as the core application;
- SMB as the definition of files;
- Tailscale as the definition of networking;
- one server as the top-level product object;
- version roadmap built around the old file-sharing utility.

## Historical description

When appropriate, describe `balls-server` as:

> **The original Windows file-sharing prototype that led to the broader Balls platform.**
