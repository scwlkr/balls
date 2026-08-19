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

The `0.1.0-alpha.2` release candidate currently proves on Windows:

- Core, Protocol, SQLite, Windows adapter, daemon, and CLI boundaries;
- persistent local Node identity;
- atomic Circle creation with one Owner and enrolled local Node;
- Circle, Member, and Node listing;
- versioned HTTP/JSON over a same-user named pipe;
- protected, marked, fail-closed SQLite state;
- 72 automated tests and Windows process-boundary evidence.

Linux currently builds and runs portable tests, but `ballsd` and `balls` reject non-Windows hosts.

## Active milestone

**`0.1.0-alpha.2` — Open and Fast Foundation**

[Open the active GitHub milestone](https://github.com/scwlkr/balls/milestone/1).

Exit outcome: the repository passes an early owner-approved public transition, every green `main`
produces a runnable Windows Canary plus an explicitly unsupported Linux build/test artifact, and
an agent has a sub-minute local workflow plus a sub-five-minute Windows/Linux pull-request gate.
Linux becomes a runnable Canary in `0.2.0-alpha.1` when its native runtime lands.

The exit outcome is implemented and verified. No `0.1.0-alpha.2` tag or GitHub Release exists;
issue #8 and the milestone remain open until the owner explicitly accepts publication.

The active GitHub milestone owns executable tickets. Do not start product features from a future
milestone while a ready active-milestone ticket exists.

Completed transition:

- [#3 — Prepare and approve the public repository transition](https://github.com/scwlkr/balls/issues/3)
- [#4 — Create the sub-minute developer verification command](https://github.com/scwlkr/balls/issues/4)
- [#5 — Establish the protected pull-request workflow](https://github.com/scwlkr/balls/issues/5)
- [#6 — Publish green-main Canary artifacts](https://github.com/scwlkr/balls/issues/6)
- [#7 — Add the post-public security automation baseline](https://github.com/scwlkr/balls/issues/7)

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

Every green `main` now publishes deterministic, checksummed, 14-day Windows and Linux workflow
artifacts from the accepted commit. Windows is smoked from fresh state; Linux is explicitly
runtime-unsupported. Evidence is in the
[Canary artifact record](verification/2026-08-19-canary-artifacts.md).

Dependency review, C# CodeQL, scheduled OpenSSF Scorecard, Dependabot security updates, action
SHA enforcement, and the structurally separate fork trust boundary are verified in the
[security automation record](verification/2026-08-19-security-automation.md).

The complete release-candidate matrix, exact downloaded-artifact observation, public-state
readback, and next executable milestone are reconciled in the
[Open and Fast Foundation record](verification/2026-08-19-open-fast-foundation.md).

Ready frontier:

- [#8 — Verify and accept the Open and Fast Foundation milestone](https://github.com/scwlkr/balls/issues/8)

Issue #8 is the final milestone verification and explicit owner-acceptance gate.

Prepared next frontier after acceptance:

- [#17 — Compose daemon and CLI through cross-platform host seams](https://github.com/scwlkr/balls/issues/17)

#17–#23 are executable under milestone `0.2.0-alpha.1` but remain labeled `blocked` until #8 closes.

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
