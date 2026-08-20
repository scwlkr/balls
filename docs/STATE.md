# Current State

**Updated:** 2026-08-20

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

Published [`0.2.0-alpha.1`](https://github.com/scwlkr/balls/releases/tag/0.2.0-alpha.1) proves the
same native daemon, structured CLI, durable Circle/Node state, local-control v1 behavior, and
hardened local React workspace on Windows and Linux. The annotated tag targets exact accepted
commit `3935b6ac275b24c8ed2389862b012da747099f34`; seven public assets, checksums, installers, and
the SPDX 2.3 SBOM passed unauthenticated readback.

## Active milestone

**`0.3.0-alpha.1` — Trusted Circle**

[Open the active GitHub milestone](https://github.com/scwlkr/balls/milestone/3).

Status: trusted identity/admission design complete; protected authority persistence is next.

Exit outcome: a second Node accepts a bounded invitation, joins one Circle over authenticated
encrypted transport, sees the same membership, restarts with stable identity, and exchanges one
persistent text message. Transport remains replaceable and separate from Circle authority.

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
prevents empty focused selections, and exposes its standard .NET and pnpm commands. The browser-
enabled warm Windows fast path passed in 37.90 seconds, within its 60-second budget; Ubuntu runs the
same portable path in CI.
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

Completed Cross-platform Node and Web UI milestone:

- [#17 — Compose daemon and CLI through cross-platform host seams](https://github.com/scwlkr/balls/issues/17)
- [#18 — Run protected local state and control IPC natively on Linux](https://github.com/scwlkr/balls/issues/18)
- [#19 — Add stable structured CLI output and dual-platform process acceptance](https://github.com/scwlkr/balls/issues/19)
- [#20 — Create the typed React workspace and generated local API client](https://github.com/scwlkr/balls/issues/20)
- [#21 — Serve a hardened local browser UI from ballsd](https://github.com/scwlkr/balls/issues/21)
- [#22 — Prove the Windows and Ubuntu Node/UI outcome and publish runnable Canaries](https://github.com/scwlkr/balls/issues/22)
- [#23 — Verify and accept the Cross-platform Node and Web UI milestone](https://github.com/scwlkr/balls/issues/23)

The executable layers consume neutral host contracts and one centralized selector. Windows keeps
its protected state and same-user named pipe; Linux adds owned `0700` state, `0600` known files,
and a `0600` Unix-domain socket without forking product, protocol, or storage behavior. Unregistered
hosts fail closed through one typed result.

The CLI now keeps human-readable text as its default and exposes a versioned typed JSON result or
error envelope for every current command. Global option placement, exit codes, identifiers,
timestamps, roles, and list ordering have contract coverage, and the same separate-process
create/list/restart acceptance runs on Windows and Ubuntu.

The repository now has one pinned React/TypeScript/Vite workspace served offline by `ballsd`.
Protected IPC issues a short-lived one-time launch capability; the separate loopback adapter uses
an HttpOnly session, antiforgery, exact Host/Origin validation, CSP, bounded requests, and no
permissive CORS. The accessible Node, Circle list/create, Member, and Node views use the same
application behavior as the CLI. Component tests and a real Playwright Chromium
launch/create/list/restart journey run in focused/fast/full verification and both fixed CI lanes.
The implementation and observed security evidence are recorded in the
[browser UI record](verification/2026-08-19-browser-ui.md).

The exact Windows and Linux Canary flow passes checksum installation, structured CLI/Circle work,
real Chrome rendering, restart-stable identifiers, and loopback-only exposure in CI. A namespaced
Ubuntu Hyper-V lab independently proved the exact Linux upload and gated clean reset without
touching unrelated resources. The exact unsigned Windows download was checksum-intact but blocked
by the owner's managed Application Control policy; no security policy was weakened. Exact assets
and the honest physical/virtual boundary are recorded in the
[cross-platform Node/UI record](verification/2026-08-20-cross-platform-node-ui.md).

#23 remeasured feedback budgets, validated the exact protected-main artifacts, and published only
those accepted bytes under the owner's explicit authorization. The final evidence is recorded in the
[milestone record](verification/2026-08-20-cross-platform-node-web-ui.md).

Completed Trusted Circle foundation:

- [#33 — Decide Circle identity, admission, and remote protocol security](https://github.com/scwlkr/balls/issues/33)

Remote v1 now has an accepted role-separated P-256 identity model, canonical dual-signed admission
transcript, deterministic rejection vocabulary, invitation-pinned TLS 1.3 bootstrap, admitted-peer
mTLS binding, transport-provider seam, and explicit authority recovery/revocation boundary. The
design and executable spike are recorded in
[`ADR 0006`](decisions/0006-trusted-circle-identity-and-admission.md), the
[`remote Circle v1 contract`](protocol/remote-circle-v1.md), and the
[`dated verification record`](verification/2026-08-20-trusted-circle-security-design.md).

Ready frontier:

- [#35 — Persist and protect cryptographic Node and Circle authority](https://github.com/scwlkr/balls/issues/35)

Blocked later in the active milestone:

- [#36](https://github.com/scwlkr/balls/issues/36),
  [#37](https://github.com/scwlkr/balls/issues/37),
  [#38](https://github.com/scwlkr/balls/issues/38),
  [#39](https://github.com/scwlkr/balls/issues/39), and
  [#34](https://github.com/scwlkr/balls/issues/34) remain dependency-blocked.

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
