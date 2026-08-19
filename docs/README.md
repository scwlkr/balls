# Project Documents

Use this directory for ADRs, threat models, protocol specifications, milestone design notes, and other detailed documents that would make the root foundation files harder to scan.

## Developer workflow

- [`STATE.md`](STATE.md) — compact current checkpoint, active milestone, and continuation route.
- [`development.md`](development.md) — locked build/test commands, local run instructions, state-directory safety, and exit codes.
- [`development-process.md`](development-process.md) — ticket flow, feedback loops, virtual lab, release channels, and public-source gate.

## Roadmap

- [`roadmap/files-first-v1.md`](roadmap/files-first-v1.md) — detailed outcome path and candidate ticket maps through v1.0.

## Design checkpoints

- [`design/phase-1-slice-1.md`](design/phase-1-slice-1.md) — scope and acceptance criteria for the first implementation slice.

## Architecture decisions

- [`decisions/0001-local-control-api.md`](decisions/0001-local-control-api.md) — local API contract and Windows IPC decision.
- [`decisions/0002-protected-local-state.md`](decisions/0002-protected-local-state.md) — dedicated state directory, platform protection, database identity, and fail-closed validation.
- [`decisions/0003-apache-2.0-license.md`](decisions/0003-apache-2.0-license.md) — permissive public-source and inbound contribution decision.
- [`decisions/0004-local-typescript-browser-ui.md`](decisions/0004-local-typescript-browser-ui.md) — one local cross-platform browser UI and loopback boundary.
- [`decisions/0005-files-first-v1.md`](decisions/0005-files-first-v1.md) — focused v1 Circle Files outcome and provider boundary.

## Contracts and state

- [`protocol/local-control-v1.md`](protocol/local-control-v1.md) — implemented HTTP/JSON local-control contract over Windows named pipes.
- [`storage/sqlite-local-state-v1.md`](storage/sqlite-local-state-v1.md) — implemented local SQLite identity, schema, transactions, and migration policy.

## Security

- [`security/threat-model.md`](security/threat-model.md) — Slice 1 assets, trust boundaries, mitigations, limitations, and next-slice requirements.

## Verification

- [`verification/phase-1-slice-1.md`](verification/phase-1-slice-1.md) — automated and real Windows evidence for the local checkpoint.
- [`verification/2026-08-19-public-readiness.md`](verification/2026-08-19-public-readiness.md) — licensing, privacy/history audit, rewrite trial, and owner-gated public-transition evidence.
- [`verification/2026-08-19-developer-verification.md`](verification/2026-08-19-developer-verification.md) — focused/fast/full verifier behavior and measured budgets.
- [`verification/2026-08-19-protected-pr-workflow.md`](verification/2026-08-19-protected-pr-workflow.md) — fixed Windows/Linux lanes and the fail-closed merge decision.
- [`verification/2026-08-19-canary-artifacts.md`](verification/2026-08-19-canary-artifacts.md) — deterministic Windows and explicitly unsupported Linux artifact evidence.
- [`verification/2026-08-19-security-automation.md`](verification/2026-08-19-security-automation.md) — dependency review, CodeQL, Scorecard, action policy, and fork boundary.
- [`verification/2026-08-19-open-fast-foundation.md`](verification/2026-08-19-open-fast-foundation.md) — final feedback budgets, exact-artifact checks, repository state, and owner acceptance gate.
