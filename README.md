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

**Phase 1 Slice 1 is implemented as a local checkpoint; Phase 1 is not complete.**

On Windows, `ballsd` now owns persistent local Node and Circle state, and `balls` can create a
Circle and list its Circles, Members, and Nodes through a versioned HTTP/JSON API over a same-user
named pipe. SQLite state is held in a dedicated marked directory with protected Windows ACLs and
fail-closed application-ID and schema validation.

The next slice must add invitation/admission, join, authenticated Node-to-Node communication,
two-real-machine membership evidence, and a persistent Circle message path. AI, apps, distributed
compute, and a universal filesystem remain outside Phase 1.

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
```

Use a new dedicated state directory, not a general-purpose folder. See the
[`developer workflow`](docs/development.md) for the full verification sequence and participant
listing commands. The [`docs index`](docs/README.md) links the local-control, storage, security,
and decision records.

## Contributing and security

Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before proposing a change. Report suspected
vulnerabilities through [GitHub private vulnerability reporting](https://github.com/scwlkr/balls/security/advisories/new),
not a public issue.

## License

Balls is intended to remain open source, but the owner has not selected the source license yet.
No external release will be made until that decision is recorded.

## Prior research

The [original Windows file-sharing prototype](https://github.com/scwlkr/balls-server) is archived
prior research, not the architecture of this project.
