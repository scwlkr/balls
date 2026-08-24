# Circle Files revocation and cleanup verification

**Date:** 2026-08-24

**Issue:** [#61](https://github.com/scwlkr/balls/issues/61)

**Branch:** `codex/issue-61-revoke-circle-files`

## Executable coverage

- Core and SQLite tests commit one exact grant generation to `revoked`, reject future active
  authorization, preserve the signed revocation across restart, and reject generation drift.
- SQLite schema v8 migration rolls back atomically on injected failure. Removed credential state,
  protected recovery material, and redacted append-only outcomes survive restart without plaintext
  secret bytes in the database. Exact mapping unmap records `requested` plus terminal outcomes,
  survives restart, and records an idempotent retry.
- Windows operation contracts return `busy` before mutation, require separate termination
  confirmation, bound counts to 1,000, recover injected partial cleanup on retry, and refuse hostile
  substitution before session termination or rollback. Final host cleanup closes only exact
  contributed-file handles and never closes their containing SMB sessions.
- A Windows-gated ACL/marker integration test writes 4,096 deterministic user bytes, removes exact
  Balls metadata, and verifies the contributed folder and bytes remain unchanged. A second
  Windows-gated case covers preservation when the contributed folder is empty and verifies exact
  pre-mutation owner, group, DACL, and inheritance-control restoration.
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
restore, format and generated-client checks, zero-warning Release build, 255 passed .NET tests
(53 platform-gated skips), web lint/typecheck, 10 component tests, production build, and one
Playwright daemon-restart journey.

## Windows lab status

The required live gate used two disposable Windows Nodes on an isolated virtual LAN: a Windows
Server 2025 Desktop Experience provider and a Windows Server 2022 Core client. Each Node had 2 GiB
RAM. The user's working Windows/Revit VM was not started, stopped, reconfigured, or otherwise used.

The complete Windows platform test assembly passed 57 tests with zero skips and zero failures. The
live product path then observed this matrix:

| Scenario | Observed result |
| --- | --- |
| Host and grant | The provider hosted a Circle Files share and created one read-write grant; the client authenticated with that exact relayed credential. |
| User bytes | The client wrote a deterministic 4,096-byte file. Its SHA-256 remained `4e441a3533bb2c10cd5649981d395744213e09a336746b5a3458fee4057205ec` through revoke, cleanup, daemon restart, and host removal. |
| Exact revoke and retry | Revoke returned `revoked`; an exact retry returned the same durable result. Future authorization was rejected. |
| Confirmation boundary | Forced cleanup before an observed busy result was refused with `circle_files_open_session_confirmation_required`; ordinary cleanup then returned `busy` with one exact session. |
| Hostile substitution | Replacing the exact grant marker caused `grant_resource_collision`; the open session was not terminated and the original marker hash was unchanged after restoration. |
| Open-session termination | Confirmed cleanup terminated the exact SMB session and returned `partial` because the grant marker was deliberately held without delete sharing. The client observed its established session fail. |
| Restart recovery | After daemon restart and marker-lock release, cleanup returned `removed`; its exact retry returned `already-removed`. The local grant account and marker were absent. |
| Future authentication | The second Node could no longer authenticate with the revoked relayed credential. |
| Final host removal | Host removal returned `removed`; its exact retry returned `already-removed`. The share and Balls firewall rule were absent while the contributed folder and user file survived. |
| Exact ACL restoration | The surviving folder's owner, group, DACL, inheritance flags, and complete SDDL matched a fresh sibling created under the same parent. |
| Durable audit | The redacted export contained bounded `requested` plus `revoked`, `refused`, `busy`, `partial`, `removed`, and `already-removed` outcomes, including restart recovery, with no credential, proof, password, or secret fields. |

The live run and final review also exposed and drove fixes for the Windows helper publish bundle,
early helper-exit reporting, fixed-script parsing, protected PowerShell script transport, Windows
PowerShell assembly resolution, restart-safe restoration of automatically enabled built-in SMB
firewall rules, empty `Get-SmbSession` query semantics, durable unmap audit, and final-host open-file
termination scope. The final two-Node observations above and complete Windows test run were made
after the applicable fixes.
