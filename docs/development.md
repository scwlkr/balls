# Developer Workflow

The delivery model, ticket flow, feedback-loop budgets, release channels, and one-laptop virtual
lab are defined in [`development-process.md`](development-process.md). This document records the
commands and safety rules implemented by the current checkpoint.

## Current platform boundary

The projects target .NET 10. The repository pins SDK `10.0.400` in
[`global.json`](../global.json). Restore, build, and portable tests run on Windows and Linux, but
the current `ballsd` runtime and `balls` CLI require Windows because Slice 1 uses same-user named
pipes and Windows ACLs.

## Build and verify

Run the same sequence used by CI from the repository root:

```powershell
dotnet restore Balls.slnx --locked-mode
dotnet format Balls.slnx --verify-no-changes --no-restore
dotnet build Balls.slnx --configuration Release --no-restore
dotnet test Balls.slnx --configuration Release --no-build --no-restore
```

This is the current full gate. The active `0.1.0-alpha.2` milestone will add a measured sub-minute
fast gate without weakening this release-grade sequence.

Package lock files are committed per project. Change dependencies deliberately, regenerate the
affected lock files, and then rerun the full sequence.

## Versioning

Balls uses Semantic Versioning for product binaries. The shared version lives in
[`Directory.Build.props`](../Directory.Build.props); this checkpoint is `0.1.0-alpha.1`. Keep
`balls` and `ballsd` on the same product version. Local-control path versions, protocol versions,
and SQLite `user_version` are separate compatibility axes and change only when their own contract
requires it. Do not tag a release until its milestone evidence is accepted.

## Run the local slice on Windows

Use a dedicated development directory. The daemon will reject a nonempty directory that was not
previously initialized as Balls state.

In one PowerShell window:

```powershell
$ballsDevState = Join-Path $env:LOCALAPPDATA "Balls-Dev"
dotnet run --project src/Balls.Daemon --configuration Release --no-build -- --data-directory $ballsDevState --pipe-name balls-dev --node-name $env:COMPUTERNAME
```

In another PowerShell window:

```powershell
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev status
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev circle create "My Circle" --owner $env:USERNAME
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev circle list
```

Copy the returned Circle ID to inspect its participants:

```powershell
$circleId = "replace-with-circle-id"
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev member list --circle $circleId
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev node list --circle $circleId
```

Stop the daemon with Ctrl+C. Restart it with the same data directory to verify that Node and Circle
identities persist.

## State-directory safety

The default directory is `%LOCALAPPDATA%\Balls`. For development, use a new dedicated directory or
one already marked by Balls. Do not point `--data-directory` at a general-purpose folder. The
Windows adapter rejects network paths, reparse-point paths, unmarked nonempty directories, and
unexpected entries before opening the database. The parent of a custom directory must already be
controlled by the current user; prefer LocalAppData.

Only one daemon may own a data directory at a time. Use a different directory and pipe name for
parallel manual runs.

## Tests and boundaries

- Core tests cover Circle behavior without platform dependencies.
- Protocol tests cover wire serialization.
- SQLite tests cover persistence, transactions, idempotency, and fail-closed schema validation.
- Daemon and CLI tests cover the local API and process boundary.
- Windows-only ACL and named-pipe tests are skipped or inconclusive on non-Windows hosts.
- Architecture tests protect the dependency direction documented in
  [`ARCHITECTURE.md`](../ARCHITECTURE.md).

Keep Windows-specific APIs in `Balls.Platform.Windows`. A change to behavior, wire contracts,
storage, or trust boundaries should update the relevant document in [`docs/`](README.md) in the
same checkpoint.

## Process exit codes

| Process | Code | Meaning |
| --- | ---: | --- |
| `balls` and `ballsd` | 0 | Success |
| `balls` and `ballsd` | 2 | Command-line usage error |
| `balls` | 3 | Daemon unavailable |
| `balls` | 4 | Request rejected by the daemon |
| `ballsd` | 4 | Startup failure |
| `balls` and `ballsd` | 5 | Current platform unsupported |
