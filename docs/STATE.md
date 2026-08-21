# Current State

**Updated:** 2026-08-21

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

The dedicated `Balls.Dev.Windows11` Hyper-V guest is ready for unsigned source builds, real Chrome
UI journeys, installer checks, and Windows-specific acceptance. PowerShell Direct, GitHub access,
the repository toolchain, and the initial source/browser/installer smokes are proven. Read the
[Windows development lab runbook](windows-development-lab.md) before using or recovering it.

## Active milestone

**`0.3.0-alpha.1` — Trusted Circle**

[Open the active GitHub milestone](https://github.com/scwlkr/balls/milestone/3).

Status: trusted identity/admission design, protected authority, bounded invitations, authenticated
LAN transport, and restart-stable two-Node membership complete; persistent Circle messaging is
next.

Exit outcome: a second Node accepts a bounded invitation, joins one Circle over authenticated
encrypted transport, sees the same membership, restarts with stable identity, and exchanges one
persistent text message. Transport remains replaceable and separate from Circle authority.

The active GitHub milestone owns executable tickets. Do not start product features from a future
milestone while a ready active-milestone ticket exists.

## Parallel macOS developer lane

[#48](https://github.com/scwlkr/balls/issues/48) is the owner-approved second lane. It adds an
Apple-Silicon source-run host without a native GUI: `ballsd` owns local state and the existing
React workspace remains the interface. The adapter uses marked owned APFS state under
`~/Library/Application Support/Balls`, a private short Unix-domain socket, `/usr/bin/open`, and an
explicit owned-state private-material scheme. The Mac handles macOS/portable/browser work while
Windows keeps Windows-specific, Circle Files, and release work; GitHub Issues and pull requests
coordinate the two machines.

The exact TLS 1.3 remote contract is unchanged. .NET 10 supports its macOS `SslStream` path only
for clients, so the Mac can develop and prove local/browser behavior and is being prepared as a
joining client, but a macOS Anchor/listener is not yet claimed. The required `macos-26` fast lane
tests this honest boundary. [#49](https://github.com/scwlkr/balls/issues/49) now derives the shared
browser workspace from canonical `balls-brand.png`: the connected-node mark, focused palette and
type, responsive Circle interactions, explicit busy/error semantics, and reviewed state
screenshots are recorded in the
[browser brand workspace evidence](verification/2026-08-21-browser-brand-workspace.md).

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
launch/create/list/restart journey runs in focused/fast/full verification and the required CI lanes.
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
- [#35 — Persist and protect cryptographic Node and Circle authority](https://github.com/scwlkr/balls/issues/35)
- [#36 — Issue and redeem bounded single-use Circle invitations](https://github.com/scwlkr/balls/issues/36)
- [#37 — Authenticate and encrypt LAN Node transport](https://github.com/scwlkr/balls/issues/37)
- [#38 — Admit a second Node and persist shared Circle membership](https://github.com/scwlkr/balls/issues/38)

Remote v1 now has an accepted role-separated P-256 identity model, canonical dual-signed admission
transcript, deterministic rejection vocabulary, invitation-pinned TLS 1.3 bootstrap, admitted-peer
mTLS binding, transport-provider seam, and explicit authority recovery/revocation boundary. The
design and executable spike are recorded in
[`ADR 0006`](decisions/0006-trusted-circle-identity-and-admission.md), the
[`remote Circle v1 contract`](protocol/remote-circle-v1.md), and the
[`dated verification record`](verification/2026-08-20-trusted-circle-security-design.md).

Node, Circle-root, and delegated Anchor signing credentials now persist atomically with role-scoped
P-256 public IDs. Windows uses current-user DPAPI; Linux uses verified owned `0700`/`0600` state.
Schema v1 migrates transactionally without partial keys or silent regeneration, and an explicit
root-signed envelope exports separately encrypted root/Anchor PKCS#8 material. Native Windows and
Ubuntu WSL2 risk checks plus the complete storage/security matrix are recorded in the
[`protected cryptographic state record`](verification/2026-08-20-protected-cryptographic-state.md).

Circle invitations are now exact canonical JSON packages containing a root-signed bounded Anchor
delegation and Anchor-signed one-use invitation. A distinct protected transport key is pinned for
the next TLS slice; package digest, expiry, revocation, and one-winner redemption state persist in
schema v3. The CLI supports exact copy/file creation and bounded file redemption; browser routes
remain deliberately unchanged. Contract, concurrency, and local-control evidence is recorded in
the [`bounded invitation record`](verification/2026-08-20-bounded-circle-invitations.md).

Remote v1 now validates Circle-root-signed Node/transport bindings, establishes exact TLS 1.3
mutual authentication with encrypted Circle/peer confirmation, and exchanges replay-aware bounded
frames over a provider-neutral stream. The first `lan-tcp-v1` provider accepts only numeric
private/loopback endpoints and never treats network metadata as authority. Separate Windows and
Linux process tests pass, and the owned Windows-host/Ubuntu-VM private network proved the exact
cross-host encrypted channel while local-control/browser boundaries remained separate. Evidence
is in the
[`authenticated LAN transport record`](verification/2026-08-20-authenticated-lan-transport.md).

`ballsd` now composes an opt-in numeric private/loopback admission listener. `balls circle join`
uses the directly exchanged invitation to pin TLS 1.3, proves separate retry-stable Member and
local Node keys, and validates an Anchor-signed roster with root-signed transport bindings. Schema
v4 atomically couples invitation consumption to the Member/Node/credential/response commit on the
Anchor and stores the same public trust and signed receipt on the joiner without transferring
private Circle authority. Exact retry is stable, conflict/revocation/expiry are typed, security
audit retention is capped at 512 events per Circle, and CLI/local API/browser projections show the
same roster after restart. Evidence is in the
[`persisted Circle admission record`](verification/2026-08-20-persisted-circle-admission.md).

Ready frontier:

- [#39 — Exchange one persistent Circle message across two Nodes](https://github.com/scwlkr/balls/issues/39)

Blocked later in the active milestone:

- [#34](https://github.com/scwlkr/balls/issues/34) remains dependency-blocked.

## Working rules

- One active milestone; at most two non-overlapping tickets in progress.
- One vertical outcome per issue and squash-merged pull request.
- Focused check target: under 15 seconds.
- Complete local fast-gate target: under 60 seconds.
- Windows/Linux/macOS pull-request target: under five minutes.
- Heavy VM, installer, recovery, upgrade, or full UI checks are release- or risk-triggered.
- Every green `main` produces a Canary; coherent outcomes may become Alphas.
- Alpha/tag publication requires a separate final owner confirmation after readiness evidence.

## Continue

1. Open the active milestone in GitHub.
2. Select the highest-priority unblocked issue labeled `ready-for-agent`.
3. Read only that issue and its linked documents.
4. Implement, verify, update evidence/state, open a pull request, and squash merge when green.
5. Move directly to the next ready issue unless a recorded stop condition applies.
