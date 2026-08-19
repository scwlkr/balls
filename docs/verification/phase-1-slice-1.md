# Phase 1 Slice 1 Verification

**Checkpoint:** 2026-08-19. This evidence covers the local first slice only;
it is not the two-machine Phase 1 exit proof.

## Automated evidence

- [GitHub Actions run 32269579896](https://github.com/scwlkr/balls/actions/runs/32269579896)
  passed the locked restore, formatting, Release build, and full test gate on both
  `windows-latest` and `ubuntu-latest` for commit `1b918c2`.

The verified Release gate is:

```powershell
dotnet restore Balls.slnx --locked-mode
dotnet format Balls.slnx --verify-no-changes --no-restore
dotnet build Balls.slnx --configuration Release --no-restore
dotnet test Balls.slnx --configuration Release --no-build --no-restore
```

Result on Windows: build succeeded with zero warnings and zero errors; 55 tests
passed across Core, Protocol, SQLite storage, architecture, daemon, CLI, and
separate-process acceptance suites.

The daemon regression suite proves that injected `Kestrel__Endpoints__*`
configuration cannot add a TCP control endpoint. Windows-specific named-pipe,
ACL, and process tests are skipped on non-Windows CI; the cross-platform Core,
Protocol, storage, and architecture suites still run there.

## Real Windows process evidence

Using the Release `ballsd.exe` and `balls.exe` as separate processes under a
fresh dedicated LocalAppData state directory:

1. Started `ballsd` from a standard, non-administrator Windows token.
2. Queried status, created one Circle, and listed its Owner Member and enrolled
   local Node through `balls`.
3. Confirmed `Get-NetTCPConnection -OwningProcess <ballsd-pid> -State Listen`
   returned zero listeners.
4. Terminated and restarted `ballsd` against the same state directory while
   supplying a different Node display name.
5. Confirmed the original Node ID, original Node display name, Circle ID,
   Owner, and Node enrollment all remained unchanged.

The temporary verification state and logs contained no keys or credentials.

## Remaining Phase 1 evidence

Phase 1 still requires two real Windows machines to join one Circle through an
authenticated admission and Node-to-Node protocol, exchange a persistent
Circle message, restart, and recognize the same Circle. That is intentionally
next-slice work.
