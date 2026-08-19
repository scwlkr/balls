# ADR 0004 — Use One Local TypeScript Browser UI

- **Status:** Accepted for the files-first program
- **Date:** 2026-08-19

Balls will build one React/TypeScript browser UI instead of separate Windows, Linux, and macOS
interfaces. `ballsd` serves bundled assets and a narrow authenticated API on loopback; `balls ui`
bootstraps the session, while the CLI retains the full protected named-pipe/Unix-socket control
path. This adds a hardened loopback trust boundary but keeps one automation/accessibility model,
preserves offline use, and avoids making a public web page or native GUI framework part of Balls.

## Implementation checkpoint

`web/Balls.Web` is the single React/TypeScript/Vite workspace. Its generated types and typed
`openapi-fetch` client come from the committed local-control OpenAPI contract through one
reproducible pnpm command. Protocol DTOs remain at the browser API edge; the accessible synthetic
Circle shell consumes a presentation snapshot. The repository verifier runs generation drift,
format, lint, typecheck, component tests, and production build on Windows and Ubuntu.

This checkpoint does not serve the bundle, open a browser, or establish the loopback listener.
Those operations land with the separately threat-modeled browser adapter and launch capability.
