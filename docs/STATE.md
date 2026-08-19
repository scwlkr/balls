# Current State

**Updated:** 2026-08-19

This is the compact entry point for a coding agent. GitHub Issues are the execution authority;
[`ROADMAP.md`](../ROADMAP.md) is the outcome index; detailed design and contract documents are
loaded only when linked by the active issue.

## Product direction

Balls is a Circle-first platform. The path to the first supported release is files-first: secure
Circle membership, a native Windows/Linux Node foundation, one TypeScript browser UI, and a
Windows Circle Files provider that gives two Members the same Explorer folder.

The files-first release does not redefine Balls as an SMB utility. AI, Apps, multiple Anchors,
replicated storage, richer messaging, macOS polish, and compute remain later product pillars.

## Proven checkpoint

`0.1.0-alpha.1` currently proves on Windows:

- Core, Protocol, SQLite, Windows adapter, daemon, and CLI boundaries;
- persistent local Node identity;
- atomic Circle creation with one Owner and enrolled local Node;
- Circle, Member, and Node listing;
- versioned HTTP/JSON over a same-user named pipe;
- protected, marked, fail-closed SQLite state;
- 55 automated tests and Windows process-boundary evidence.

Linux currently builds and runs portable tests, but `ballsd` and `balls` reject non-Windows hosts.

## Active milestone

**`0.1.0-alpha.2` — Open and Fast Foundation**

[Open the active GitHub milestone](https://github.com/scwlkr/balls/milestone/1).

Exit outcome: the repository passes an early owner-approved public transition, every green `main`
produces a runnable Windows Canary plus an explicitly unsupported Linux build/test artifact, and
an agent has a sub-minute local workflow plus a sub-five-minute Windows/Linux pull-request gate.
Linux becomes a runnable Canary in `0.2.0-alpha.1` when its native runtime lands.

The active GitHub milestone owns executable tickets. Do not start product features from a future
milestone while a ready active-milestone ticket exists.

Owner-gated transition:

- [#3 — Prepare and approve the public repository transition](https://github.com/scwlkr/balls/issues/3)

The source-tree preparation and disposable history-rewrite trial are recorded in the
[dated readiness evidence](verification/2026-08-19-public-readiness.md). Existing read-only GitHub
pull refs retain the legacy history, so the owner must choose the publication lineage before a
final visibility confirmation. The repository remains private.

Ready frontier:

- [#4 — Create the sub-minute developer verification command](https://github.com/scwlkr/balls/issues/4)

Issues #5–#8 are intentionally blocked by recorded dependencies in GitHub.

## Working rules

- One active milestone; at most two non-overlapping tickets in progress.
- One vertical outcome per issue and squash-merged pull request.
- Focused check target: under 15 seconds.
- Complete local fast-gate target: under 60 seconds.
- Windows/Linux pull-request target: under five minutes.
- Heavy VM, installer, recovery, upgrade, or full UI checks are release- or risk-triggered.
- Every green `main` produces a Canary; coherent outcomes may become Alphas.
- Repository publication requires a separate final owner confirmation after readiness evidence.

## Continue

1. Open the active milestone in GitHub.
2. Select the highest-priority unblocked issue labeled `ready-for-agent`.
3. Read only that issue and its linked documents.
4. Implement, verify, update evidence/state, open a pull request, and squash merge when green.
5. Move directly to the next ready issue unless a recorded stop condition applies.
