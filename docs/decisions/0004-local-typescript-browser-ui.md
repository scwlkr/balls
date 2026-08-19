# ADR 0004 — Use One Local TypeScript Browser UI

- **Status:** Accepted for the files-first program
- **Date:** 2026-08-19

Balls will build one React/TypeScript browser UI instead of separate Windows, Linux, and macOS
interfaces. `ballsd` serves bundled assets and a narrow authenticated API on loopback; `balls ui`
bootstraps the session, while the CLI retains the full protected named-pipe/Unix-socket control
path. This adds a hardened loopback trust boundary but keeps one automation/accessibility model,
preserves offline use, and avoids making a public web page or native GUI framework part of Balls.

## Implementation checkpoint

`web/Balls.Web` is the single React/TypeScript/Vite workspace. Its generated types come from the
committed OpenAPI contract through one reproducible pnpm command. Protocol DTOs remain at the API
edge; the accessible Circle workspace consumes presentation snapshots and delegates all durable
behavior to `ballsd`.

`balls ui` now requests a one-minute, single-use launch capability through protected local IPC and
opens the system browser. `ballsd` serves the production bundle and only the status, Circle
list/create, and Circle-details projection on an ephemeral IPv4 loopback listener. The exchanged
session is a 30-minute HttpOnly, Secure, SameSite=Strict cookie; state changes also require an
in-memory antiforgery token. Exact Host and Origin checks, a restrictive CSP, no permissive CORS,
bounded request bodies, and safe errors defend the new boundary. Component tests and a real
Playwright Chromium launch/create/list/restart journey run in the repository verifier on Windows
and Ubuntu.
