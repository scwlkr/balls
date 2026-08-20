# Balls

[![CI](https://github.com/scwlkr/balls/actions/workflows/ci.yml/badge.svg)](https://github.com/scwlkr/balls/actions/workflows/ci.yml)

**Balls gives trusted Circles their own digital environment, where their people, computers, data, services, and intelligence can work together under their control.**

Balls is an open-source platform for small, trusted groups. A Circle can combine people, computers, servers, VPSs, storage, messaging, files, AI, apps, and eventually contributed compute into one coherent workspace.

The Circle is the product. The machines are the infrastructure that gives it power.

## Start here

Read these documents in order:

1. [`VISION.md`](VISION.md) — why Balls exists and what it is trying to become.
2. [`PRODUCT.md`](PRODUCT.md) — the concrete product model and user experience.
3. [`PRINCIPLES.md`](PRINCIPLES.md) — decisions and boundaries that should survive implementation changes.
4. [`ARCHITECTURE.md`](ARCHITECTURE.md) — recommended technical foundation.
5. [`GLOSSARY.md`](GLOSSARY.md) — canonical product language.
6. [`DECISIONS.md`](DECISIONS.md) — confirmed decisions, recommendations, and intentionally open questions.
7. [`ROADMAP.md`](ROADMAP.md) — staged path from a fresh repo to the larger vision.
8. [`LEGACY.md`](LEGACY.md) — relationship to the original `balls-server` prototype.
9. [`AGENTS.md`](AGENTS.md) — instructions for Codex and other coding agents.

## One-line product explanation

**Create a Circle. Invite people you trust. Their approved computers and servers become useful parts of a shared workspace for files, chat, AI, apps, services, and compute.**

## Initial target

Balls should first feel exceptional for **2–10 trusted, somewhat technical people**, with a **2–5 person small company** as the primary proving environment.

## Current status

**[`0.2.0-alpha.1`](https://github.com/scwlkr/balls/releases/tag/0.2.0-alpha.1)
Cross-platform Node and Web UI is published. Trusted Circle is the active milestone.**

On Windows and Linux, `ballsd` now owns persistent local Node and Circle state, and `balls` can
create a Circle and list its Circles, Members, and Nodes through a versioned HTTP/JSON API over
same-user local IPC. SQLite state is held in a dedicated marked directory with protected platform
permissions and fail-closed application-ID and schema validation.

The files-first path now establishes public-ready delivery and fast Windows/Linux development,
then a cross-platform daemon/CLI/browser foundation, trusted join, LAN Circle Files, operable
remote Files, a company Beta, and a focused v1.0. See the compact [`roadmap`](ROADMAP.md), detailed
[`files-first program`](docs/roadmap/files-first-v1.md), and [`current state`](docs/STATE.md).

The published Alpha has platform-neutral host composition, protected native Linux state/IPC, stable
machine-readable CLI output, and one typed React workspace generated from local-control v1.
`balls ui` opens that offline bundle through a hardened loopback-only adapter, where the user can
inspect the Node and create or revisit Circles, Members, and Nodes. Trusted Circle now has
protected production authority plus canonical, bounded, single-use invitation packages; encrypted
LAN Node transport is the next frontier.

## Quick start

On Windows with the .NET SDK selected by [`global.json`](global.json):

```powershell
dotnet restore Balls.slnx --locked-mode
dotnet build Balls.slnx --configuration Release --no-restore
$ballsDevState = Join-Path $env:LOCALAPPDATA "Balls-Dev"
dotnet run --project src/Balls.Daemon --configuration Release --no-build -- --data-directory $ballsDevState --pipe-name balls-dev --node-name $env:COMPUTERNAME
```

Leave the daemon running. In a second PowerShell window:

```powershell
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev status
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev circle create "My Circle" --owner $env:USERNAME
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev circle list
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev invitation create --circle <circle-id> --out .\invite.balls-invitation
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev invitation redeem --file .\invite.balls-invitation
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --pipe-name balls-dev ui
dotnet run --project src/Balls.Cli --configuration Release --no-build -- --output json --pipe-name balls-dev status
```

Use a new dedicated state directory, not a general-purpose folder. See the
[`developer workflow`](docs/development.md) for the full verification sequence and participant
listing commands. The [`docs index`](docs/README.md) links the local-control, storage, security,
and decision records. Native Linux uses the same daemon and CLI behavior over a protected
Unix-domain socket; see the developer workflow for its XDG defaults.

## Canary artifacts

Every successful `main` commit publishes two short-lived workflow artifacts from the exact commit
that passed CI:

- a runnable `windows-x64` development Canary containing `balls`, `ballsd`, identity metadata,
  and SHA-256 checksums;
- a runnable `linux-x64` development Canary using protected XDG state and a same-user Unix-domain
  socket.

Download the artifacts from the latest successful
[`CI` workflow](https://github.com/scwlkr/balls/actions/workflows/ci.yml). After extracting
the downloaded Windows workflow artifact, install and start it with one command:

```powershell
pwsh -File .\Install-BallsCanary.ps1 -PackagePath .\balls-*-canary-windows-x64-*.zip
```

On Ubuntu with the ASP.NET Core 10 runtime and `unzip`, the extracted Linux artifact uses the
same checksum-verifying one-command flow:

```bash
bash ./Install-BallsCanary.sh \
  ./balls-*-canary-linux-x64-*.zip \
  ./balls-*-canary-linux-x64-*.zip.sha256
```

The installer verifies both checksum layers and uses the dedicated
`%LOCALAPPDATA%\Balls-Canary\state` directory. Canary artifacts expire after 14 days. They are not
GitHub Releases, tags, stable installers, signed binaries, or support claims.

The accepted `0.2.0-alpha.1` archives, checksum files, installers, and SPDX SBOM are retained on
the [public prerelease](https://github.com/scwlkr/balls/releases/tag/0.2.0-alpha.1). These Alpha
binaries are unsigned; managed Windows Application Control may require an administrator-approved
signing or allow policy.

## Contributing and security

Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before proposing a change. Report suspected
vulnerabilities through [GitHub private vulnerability reporting](https://github.com/scwlkr/balls/security/advisories/new),
not a public issue.

## License

Balls source and documentation are licensed under the
[Apache License 2.0](LICENSE). See [`NOTICE`](NOTICE) for the current attribution status.
Contributions are accepted under the same terms without a CLA or copyright assignment.

The public repository uses a sanitized lineage and is open for issue reports and contributions
under the guidance above.

## Prior research

The [original Windows file-sharing prototype](https://github.com/scwlkr/balls-server) is archived
prior research, not the architecture of this project.
