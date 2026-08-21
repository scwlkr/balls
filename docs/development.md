# Developer Workflow

The delivery model, ticket flow, feedback-loop budgets, release channels, and one-laptop virtual
lab are defined in [`development-process.md`](development-process.md). This document records the
commands and safety rules implemented by the current checkpoint.

## Current platform boundary

The projects target .NET 10. The repository pins SDK `10.0.400` in
[`global.json`](../global.json). `ballsd` and `balls` run natively and unelevated on Windows,
Linux, and Apple-Silicon macOS through the same application, local-control v1, and SQLite behavior.
Windows composes protected ACLs, current-user DPAPI private-key protection, and same-user named
pipes; Linux composes effective-user Unix modes, mode-restricted key storage, and a protected
Unix-domain socket. macOS composes owned local-APFS state, strict modes with no extended ACL
grants, a short private Unix-domain socket, owned-state private material, and `/usr/bin/open` for
the shared browser workspace. This is source-run development support, not a signed/notarized Mac
distribution.

## Build and verify

Use the repository-owned verifier from the repository root. It prints each standard command before
running it and returns the failing command's exit code.

```powershell
# Direct feedback for one affected project and a filter that must select at least one test
dotnet run --project eng/Balls.Verify --configuration Release -- focused --project tests/Balls.Core.Tests/Balls.Core.Tests.csproj --filter TestCategory=Unit

# Focus a browser component or generated-client contract
dotnet run --project eng/Balls.Verify --configuration Release -- focused --web test
dotnet run --project eng/Balls.Verify --configuration Release -- focused --web generate:check

# Safe pre-push gate: portable unit, contract, and process-integration tests
dotnet run --project eng/Balls.Verify --configuration Release -- fast

# Release-grade gate: every test, including OS integration
dotnet run --project eng/Balls.Verify --configuration Release -- full
```

These commands are shell-neutral .NET CLI invocations; use them unchanged in PowerShell or Bash.
The required Ubuntu, Windows, and Apple-Silicon macOS pull-request lanes run `fast`; use `full`
locally and for
release/risk gates that require every OS-integration test. WSL may be used as a Linux development
executor, but it is not the Balls product runtime.

`fast` and `full` each run locked .NET and pnpm restore, format verification, and exactly one .NET
Release build. They reject uncategorized tests, check generated browser-client drift, and run web
lint, typecheck, component tests, and a production build. `fast` runs the portable-safe .NET
categories; `full` runs every .NET test. Both finish with one real Playwright Chromium journey.
Expanded, those standard commands include:

```powershell
dotnet restore Balls.slnx --locked-mode
pnpm install --frozen-lockfile
dotnet format Balls.slnx --verify-no-changes --no-restore
pnpm web:generate:check
pnpm web:format:check
dotnet build Balls.slnx --configuration Release --no-restore
pnpm web:lint
pnpm web:typecheck
dotnet test Balls.slnx --configuration Release --no-build --no-restore --filter "(TestCategory=Unit|TestCategory=Contract|TestCategory=ProcessIntegration)"
pnpm web:test
pnpm web:build
pnpm web:e2e
dotnet test Balls.slnx --configuration Release --no-build --no-restore
```

The repository pins Node through [`.node-version`](../.node-version), pnpm through the root
`packageManager`, and dependency resolution in `pnpm-lock.yaml`. CI enables Corepack and caches the
pnpm store independently on all fixed platform lanes. Run commands from the repository root so
the workspace and lockfile remain authoritative.

Package lock files are committed per project. Change dependencies deliberately, regenerate the
affected lock files, and then rerun the full sequence.

## Run the local slice on macOS

Use the pinned .NET, Node, and pnpm versions. After a locked restore/build, run the daemon with its
defaults in one terminal:

```bash
dotnet run --project src/Balls.Daemon --configuration Release --no-build
```

In another terminal, inspect it and open the same React workspace used on Windows and Linux:

```bash
dotnet run --project src/Balls.Cli --configuration Release --no-build -- status
dotnet run --project src/Balls.Cli --configuration Release --no-build -- ui
```

Default state is `~/Library/Application Support/Balls`; the control socket stays below the user's
canonical private macOS temporary directory. Stop with Ctrl+C and restart the same command to
confirm stable identity and Circle state. Do not run the daemon with `sudo`.

.NET 10 provides TLS 1.3 on macOS `SslStream` clients through Network.framework but not on servers.
Balls opts in for the joining-client path and keeps remote v1 at exact TLS 1.3. A Mac Anchor/
listener is therefore unverified and unsupported in this checkpoint; no TLS downgrade is allowed.

## Run the browser workspace

Start `ballsd` with a dedicated state directory and endpoint using the platform examples below.
Then open the production UI through the same protected endpoint:

```powershell
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev ui
```

With the Linux defaults, omit the endpoint override:

```bash
dotnet run --project src/Balls.Cli --configuration Release --no-build -- ui
```

`balls ui` requests a short-lived launch over protected IPC and opens the system browser; it does
not print the launch capability. The UI is served entirely by `ballsd`, works without an external
network connection, and uses the same durable Circle application behavior as the CLI.

For browser development and verification:

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm web:generate:check
pnpm web:test
pnpm web:e2e
```

The generated client is committed at `web/Balls.Web/src/api/generated`. Update it only with
`pnpm web:generate`; `pnpm web:generate:check` compares the committed file with fresh output from
`docs/protocol/local-control-v1.openapi.json` without rewriting it.

## Download and run a Windows Canary

The CI workflow starts its Canary jobs only after the required Windows, Ubuntu, and macOS lanes
succeed for a `main` push or an explicit branch `workflow_dispatch`. It checks out that exact commit, builds
each platform package once, and retains the results for 14 days. Ordinary pull-request runs skip
Canaries. Artifact names have this deterministic shape:

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

The Linux Canary is a runnable development artifact. Download and extract its workflow artifact,
then install it with the included checksum-verifying command:

```bash
bash ./Install-BallsCanary.sh \
  ./balls-*-canary-linux-x64-*.zip \
  ./balls-*-canary-linux-x64-*.zip.sha256
```

The Linux guest needs the ASP.NET Core 10 runtime and `unzip`. The default install, state, and
runtime stay below protected `$HOME/.balls-canary`; an explicit `$XDG_DATA_HOME` or
`$XDG_RUNTIME_DIR` is honored. The same-user control socket never falls back directly below the
system temporary directory.

Both platform jobs smoke the packaged daemon and CLI from fresh state, create and list a Circle,
render the live workspace in Chrome/Chromium, require loopback-only browser listeners, restart the
daemon, and verify stable Node/Circle identifiers. These 14-day artifacts are not stable installers
or GitHub Releases. Use the [cross-platform lab](cross-platform-lab.md) for the dedicated Ubuntu VM
identity/reset procedure.

Accepted Alpha assets are retained on the
[`0.2.0-alpha.1` public prerelease](https://github.com/scwlkr/balls/releases/tag/0.2.0-alpha.1).
They are unsigned development binaries. Managed Windows Application Control can reject them unless
an administrator approves the publisher or an appropriate allow policy; do not weaken a machine's
security policy merely to run a Canary.

## Versioning

Balls uses Semantic Versioning for product binaries. The shared version lives in
[`Directory.Build.props`](../Directory.Build.props); the current prerelease is `0.2.0-alpha.1`. Keep
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

The same order and envelope apply on Windows, Linux, and macOS. Global `--output` and `--pipe-name`
options must precede the command; command-specific `--owner`, `--request-id`, and `--circle`
options follow their command operands. JSON success is written to standard output and JSON errors
to standard error. See the
[CLI compatibility contract](protocol/local-control-v1.md#cli-output-compatibility) for the exact
envelope, error codes, ordering, and additive-field rules.

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
| `OSIntegration` | Windows ACL/DPAPI and Linux ownership/mode/IPC behavior | Full only |
| `Browser` | React components plus a real Chrome launch/create/list/restart journey | Yes |
| `Lab` | reserved for explicit VM/multi-node evidence | No |

The category audit fails if a test is added without a recognized category. `focused` also fails
with exit code 3 when its filter selects zero tests.

On the 2026-08-19 Windows development host, the browser-enabled warm fast gate took 37.90 seconds,
passing the 60-second budget. Focused generated-client and component commands also passed. See the
[dated verification record](verification/2026-08-19-developer-verification.md).

Keep host-edge APIs in `Balls.Platform.Windows`/`Balls.Platform.Linux`; Core-owned OS capability
implementations belong in focused `Balls.Security.*` adapters. A change to behavior, wire
contracts, storage, or trust boundaries should update the relevant document in [`docs/`](README.md)
in the same checkpoint.

## Process exit codes

| Process | Code | Meaning |
| --- | ---: | --- |
| `balls` and `ballsd` | 0 | Success |
| `balls` and `ballsd` | 2 | Command-line usage error |
| `balls` | 3 | Daemon unavailable |
| `balls` | 4 | Request rejected by the daemon |
| `ballsd` | 4 | Startup failure |
| `balls` and `ballsd` | 5 | Current platform unsupported |
