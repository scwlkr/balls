# Project Documents

Use this directory for ADRs, threat models, protocol specifications, milestone design notes, and other detailed documents that would make the root foundation files harder to scan.

## Developer workflow

- [`development.md`](development.md) — locked build/test commands, local run instructions, state-directory safety, and exit codes.

## Design checkpoints

- [`design/phase-1-slice-1.md`](design/phase-1-slice-1.md) — scope and acceptance criteria for the first implementation slice.

## Architecture decisions

- [`decisions/0001-local-control-api.md`](decisions/0001-local-control-api.md) — local API contract and Windows IPC decision.
- [`decisions/0002-protected-local-state.md`](decisions/0002-protected-local-state.md) — dedicated state directory, platform protection, database identity, and fail-closed validation.

## Contracts and state

- [`protocol/local-control-v1.md`](protocol/local-control-v1.md) — implemented HTTP/JSON local-control contract over Windows named pipes.
- [`storage/sqlite-local-state-v1.md`](storage/sqlite-local-state-v1.md) — implemented local SQLite identity, schema, transactions, and migration policy.

## Security

- [`security/threat-model.md`](security/threat-model.md) — Slice 1 assets, trust boundaries, mitigations, limitations, and next-slice requirements.

## Verification

- [`verification/phase-1-slice-1.md`](verification/phase-1-slice-1.md) — automated and real Windows evidence for the local checkpoint.
