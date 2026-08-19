# Developer Workflow

The delivery model, ticket flow, feedback-loop budgets, release channels, and one-laptop virtual
lab are defined in [`development-process.md`](development-process.md). This document records the
commands and safety rules implemented by the current checkpoint.

## Current platform boundary

The projects target .NET 10. The repository pins SDK `10.0.400` in
[`global.json`](../global.json). `ballsd` and `balls` run natively and unelevated on Windows and
Linux through the same application, local-control v1, and SQLite v1 behavior. Windows composes
protected ACLs and same-user named pipes; Linux composes effective-user Unix modes and a protected
Unix-domain socket.

## Build and verify

Use the repository-owned verifier from the repository root. It prints each standard command before
running it and returns the failing command's exit code.

```powershell
# Direct feedback for one affected project and a filter that must select at least one test
dotnet run --project eng/Balls.Verify --configuration Release -- focused --project tests/Balls.Core.Tests/Balls.Core.Tests.csproj --filter TestCategory=Unit

# Safe pre-push gate: portable unit, contract, and process-integration tests
dotnet run --project eng/Balls.Verify --configuration Release -- fast

# Release-grade gate: every test, including OS integration
dotnet run --project eng/Balls.Verify --configuration Release -- full
```

These commands are shell-neutral .NET CLI invocations; use them unchanged in PowerShell or Bash.
The required Ubuntu and Windows pull-request lanes both run `fast`; use `full` locally and for
release/risk gates that require every OS-integration test. WSL may be used as a Linux development
executor, but it is not the Balls product runtime.

`fast` and `full` each run locked restore, format verification, and exactly one Release build.
They then reject uncategorized tests. `fast` runs the portable-safe categories; `full` runs every
test. Expanded, those standard commands are:

```powershell
dotnet restore Balls.slnx --locked-mode
dotnet format Balls.slnx --verify-no-changes --no-restore
dotnet build Balls.slnx --configuration Release --no-restore
dotnet test Balls.slnx --configuration Release --no-build --no-restore --filter "(TestCategory=Unit|TestCategory=Contract|TestCategory=ProcessIntegration)"
dotnet test Balls.slnx --configuration Release --no-build --no-restore
```

The verifier currently has no `pnpm` step because this checkpoint has no JavaScript workspace.
When the browser workspace lands, its standard `pnpm install --frozen-lockfile`, lint, typecheck,
test, and browser commands must remain explicit in the verifier output.

Package lock files are committed per project. Change dependencies deliberately, regenerate the
affected lock files, and then rerun the full sequence.

## Download and run a Windows Canary

The CI workflow starts its Canary jobs only after the required Windows and Ubuntu lanes succeed for
a `main` push. It checks out that exact accepted commit, builds each platform package once, and
retains the results for 14 days. Pull-request runs skip publication. Artifact names have this
deterministic shape:

```text
balls-<version>-canary-<windows|linux>-x64-<12-character-commit>
```

Download and extract the Windows workflow artifact. From that directory, this one command verifies
the archive checksum and every packaged file, installs the version, starts `ballsd`, and confirms
readiness through `balls status`:

```powershell
pwsh -File .\Install-BallsCanary.ps1 -PackagePath .\balls-*-canary-windows-x64-*.zip
```

The default install root is `%LOCALAPPDATA%\Balls-Canary`; persistent development state is isolated
under its `state` directory. The installer records the background daemon PID in `ballsd.pid`.
Stop that process before installing another Canary:

```powershell
$canaryRoot = Join-Path $env:LOCALAPPDATA 'Balls-Canary'
Stop-Process -Id ([int](Get-Content (Join-Path $canaryRoot 'ballsd.pid') -Raw))
Remove-Item -LiteralPath (Join-Path $canaryRoot 'ballsd.pid')
```

The Linux Canary is a runnable development artifact. Its manifest records runtime support, and CI
smokes the packaged daemon and CLI over a fresh Unix-domain socket before upload. It is not a
stable installer or release.

## Versioning

Balls uses Semantic Versioning for product binaries. The shared version lives in
[`Directory.Build.props`](../Directory.Build.props); this release candidate is `0.1.0-alpha.2`. Keep
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

## Use stable CLI output

Human-readable text is the default. Automation selects the versioned JSON envelope with the global
option before the command:

```powershell
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --output json --pipe-name balls-dev status
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --output json --pipe-name balls-dev circle list
```

The same order and envelope apply on Windows and Linux. Global `--output` and `--pipe-name` options
must precede the command; command-specific `--owner`, `--request-id`, and `--circle` options follow
their command operands. JSON success is written to standard output and JSON errors to standard
error. See the [CLI compatibility contract](protocol/local-control-v1.md#cli-output-compatibility)
for the exact envelope, error codes, ordering, and additive-field rules.

## Run the local slice on Linux

The default state directory is `$XDG_STATE_HOME/balls` or `$HOME/.local/state/balls`. The default
socket is `$XDG_RUNTIME_DIR/balls/control.sock`, `/run/user/<uid>/balls/control.sock`, or a private
effective-user fallback below the system temporary directory. Start the daemon and use the CLI in
separate shells:

```bash
dotnet run --project src/Balls.Daemon --configuration Release --no-build
dotnet run --project src/Balls.Cli --configuration Release --no-build -- status
dotnet run --project src/Balls.Cli --configuration Release --no-build -- circle create "My Circle" --owner "$USER"
dotnet run --project src/Balls.Cli --configuration Release --no-build -- circle list
```

Stop with Ctrl+C and restart with the same environment to verify that Node and Circle identifiers
persist. A custom `--data-directory` must be a normalized absolute path beneath an owned safe
parent. The existing `--pipe-name` compatibility option accepts the normalized absolute Unix-socket
path on Linux.

## State-directory safety

The defaults are `%LOCALAPPDATA%\Balls` on Windows and the XDG state location above on Linux. For
development, use a new dedicated directory or one already marked by Balls. Do not point
`--data-directory` at a general-purpose folder. Windows rejects network/reparse paths; Linux rejects
relative, symlinked, cross-user-writable, foreign-owned, and unverified filesystem paths. Both
reject unmarked nonempty directories and unexpected entries before opening the database.

Only one daemon may own a data directory at a time. Use a different directory and pipe name for
parallel manual runs.

## Tests and boundaries

Every current test class declares one of these `TestCategory` values:

| Category | Current coverage | Fast |
| --- | --- | --- |
| `Unit` | isolated domain and verifier behavior | Yes |
| `Contract` | architecture, protocol, storage, daemon, and CLI contracts | Yes |
| `ProcessIntegration` | real `ballsd`/`balls` process acceptance | Yes |
| `OSIntegration` | Windows ACL and named-pipe defaults | Full only |
| `Browser` | reserved; no browser workspace yet | No |
| `Lab` | reserved for explicit VM/multi-node evidence | No |

The category audit fails if a test is added without a recognized category. `focused` also fails
with exit code 3 when its filter selects zero tests.

On the 2026-08-19 Windows development host, a warm focused Core run took 2.92 seconds and a warm
fast gate took 29.49 seconds, passing the 15-second and 60-second budgets. See the
[dated verification record](verification/2026-08-19-developer-verification.md).

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
