# ADR 0004 — Use One Local TypeScript Browser UI

- **Status:** Accepted for the files-first program
- **Date:** 2026-08-19

Balls will build one React/TypeScript browser UI instead of separate Windows, Linux, and macOS
interfaces. `ballsd` serves bundled assets and a narrow authenticated API on loopback; `balls ui`
bootstraps the session, while the CLI retains the full protected named-pipe/Unix-socket control
path. This adds a hardened loopback trust boundary but keeps one automation/accessibility model,
preserves offline use, and avoids making a public web page or native GUI framework part of Balls.

