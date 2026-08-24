# Circle Files revocation and cleanup verification

**Date:** 2026-08-24

**Issue:** [#61](https://github.com/scwlkr/balls/issues/61)

**Branch:** `codex/issue-61-revoke-circle-files`

## Executable coverage

- Core and SQLite tests commit one exact grant generation to `revoked`, reject future active
  authorization, preserve the signed revocation across restart, and reject generation drift.
- SQLite schema v8 migration rolls back atomically on injected failure. Removed credential state,
  protected recovery material, and redacted append-only outcomes survive restart without plaintext
  secret bytes in the database.
- Windows operation contracts return `busy` before mutation, require separate termination
  confirmation, bound counts to 1,000, recover injected partial cleanup on retry, and refuse hostile
  substitution before session termination or rollback.
- A Windows-gated ACL/marker integration test writes 4,096 deterministic user bytes, removes exact
  Balls metadata, and verifies the contributed folder and bytes remain unchanged. It was skipped on
  this Linux run and remains unobserved here. A second Windows-gated case covers preservation when
  the contributed folder is empty.
- Local-control and CLI contracts expose revoke, cleanup preview/apply, explicit session
  confirmation, final host removal, and no secret/proof fields. The committed OpenAPI document and
  generated web client were refreshed from the running daemon.

## Commands

```text
dotnet test tests/Balls.Storage.Sqlite.Tests/Balls.Storage.Sqlite.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~CircleFilesStateStoreTests
dotnet test tests/Balls.Platform.Windows.Tests/Balls.Platform.Windows.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~CircleFiles
dotnet test tests/Balls.Cli.Tests/Balls.Cli.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~CliApplicationTests
```

`dotnet run --project eng/Balls.Verify --configuration Release -- full` passed on Linux: locked
restore, format and generated-client checks, zero-warning Release build, 252 passed .NET tests
(50 platform-gated skips), web lint/typecheck, 10 component tests, production build, and one
Playwright daemon-restart journey. Windows-only execution remains scoped below.

## Windows lab status

The canonical two-Windows-VM gate from `docs/windows-development-lab.md` has not been observed from
this Linux workspace. Therefore this record does **not** claim live future-auth denial, open-session
termination, injected partial recovery, hostile on-machine substitution, or two-VM before/after
hash proof. Run machine-local `Test-BallsCircleFilesRevocation.Guest.ps1` against the exact landed
commit and append its structured evidence before #62 acceptance.
