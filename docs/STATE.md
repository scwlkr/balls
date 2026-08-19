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

Published [`0.1.0-alpha.2`](https://github.com/scwlkr/balls/releases/tag/0.1.0-alpha.2) proves on Windows:

- Core, Protocol, SQLite, Windows adapter, daemon, and CLI boundaries;
- persistent local Node identity;
- atomic Circle creation with one Owner and enrolled local Node;
- Circle, Member, and Node listing;
- versioned HTTP/JSON over a same-user named pipe;
- protected, marked, fail-closed SQLite state;
- 72 automated tests and Windows process-boundary evidence.

Linux now composes protected XDG state and a same-user Unix-domain socket through the same daemon,
CLI, local-control v1, and SQLite v1 behavior as Windows.

## Active milestone

**`0.2.0-alpha.1` — Cross-platform Node and Web UI**

[Open the active GitHub milestone](https://github.com/scwlkr/balls/milestone/2).

Exit outcome: the same CLI and local browser experience controls a real Node on Windows and Linux;
platform composition, protected state, and local IPC are genuinely cross-platform; green `main`
publishes runnable Windows and Linux Canaries.

The active GitHub milestone owns executable tickets. Do not start product features from a future
milestone while a ready active-milestone ticket exists.

Completed Open and Fast Foundation:

- [#3 — Prepare and approve the public repository transition](https://github.com/scwlkr/balls/issues/3)
- [#4 — Create the sub-minute developer verification command](https://github.com/scwlkr/balls/issues/4)
- [#5 — Establish the protected pull-request workflow](https://github.com/scwlkr/balls/issues/5)
- [#6 — Publish green-main Canary artifacts](https://github.com/scwlkr/balls/issues/6)
- [#7 — Add the post-public security automation baseline](https://github.com/scwlkr/balls/issues/7)
- [#8 — Verify and accept the Open and Fast Foundation milestone](https://github.com/scwlkr/balls/issues/8)

The owner-approved clean lineage is public at `scwlkr/balls`. Private vulnerability reporting and
the active `main` rules are verified, anonymous web/API/Git reads pass, and both the original
private archive and rejected staging repository were deleted after verification. The complete
migration is recorded in the
[dated readiness evidence](verification/2026-08-19-public-readiness.md).

The repository-owned focused, fast, and full verifier enforces the six-category test taxonomy,
prevents empty focused selections, and exposes its standard tool commands. Final warm Windows
measurements are 6.03 seconds focused and 33.69 seconds fast; Ubuntu runs the same portable fast
path in CI.
Details are in the
[developer verification record](verification/2026-08-19-developer-verification.md).

Pull requests use fixed Windows 2025 and Ubuntu 24.04 fast lanes plus one fail-closed `Required`
decision. Squash-only auto-merge, automatic branch deletion, and the active `main` ruleset are
verified in the
[protected-workflow record](verification/2026-08-19-protected-pr-workflow.md).

Every green `main` publishes deterministic, checksummed, 14-day Windows and Linux workflow
artifacts from the accepted commit. Both platforms are smoked from fresh protected state. The
original build/test-only baseline is recorded in the
[Canary artifact record](verification/2026-08-19-canary-artifacts.md).

Dependency review, C# CodeQL, scheduled OpenSSF Scorecard, Dependabot security updates, action
SHA enforcement, and the structurally separate fork trust boundary are verified in the
[security automation record](verification/2026-08-19-security-automation.md).

The complete release-candidate matrix, exact downloaded-artifact observation, public-state
readback, and next executable milestone are reconciled in the
[Open and Fast Foundation record](verification/2026-08-19-open-fast-foundation.md).

Ready frontier:

- [#20 — Create the typed React workspace and generated local API client](https://github.com/scwlkr/balls/issues/20)

Completed in the active milestone:

- [#17 — Compose daemon and CLI through cross-platform host seams](https://github.com/scwlkr/balls/issues/17)
- [#18 — Run protected local state and control IPC natively on Linux](https://github.com/scwlkr/balls/issues/18)
- [#19 — Add stable structured CLI output and dual-platform process acceptance](https://github.com/scwlkr/balls/issues/19)

The executable layers consume neutral host contracts and one centralized selector. Windows keeps
its protected state and same-user named pipe; Linux adds owned `0700` state, `0600` known files,
and a `0600` Unix-domain socket without forking product, protocol, or storage behavior. Unregistered
hosts fail closed through one typed result.

The CLI now keeps human-readable text as its default and exposes a versioned typed JSON result or
error envelope for every current command. Global option placement, exit codes, identifiers,
timestamps, roles, and list ordering have contract coverage, and the same separate-process
create/list/restart acceptance runs on Windows and Ubuntu.

#21–#23 remain dependency-blocked behind the issue chain recorded in their acceptance contracts.

## Working rules

- One active milestone; at most two non-overlapping tickets in progress.
- One vertical outcome per issue and squash-merged pull request.
- Focused check target: under 15 seconds.
- Complete local fast-gate target: under 60 seconds.
- Windows/Linux pull-request target: under five minutes.
- Heavy VM, installer, recovery, upgrade, or full UI checks are release- or risk-triggered.
- Every green `main` produces a Canary; coherent outcomes may become Alphas.
- Alpha/tag publication requires a separate final owner confirmation after readiness evidence.

## Continue

1. Open the active milestone in GitHub.
2. Select the highest-priority unblocked issue labeled `ready-for-agent`.
3. Read only that issue and its linked documents.
4. Implement, verify, update evidence/state, open a pull request, and squash merge when green.
5. Move directly to the next ready issue unless a recorded stop condition applies.
