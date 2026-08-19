# Browser UI Evidence — 2026-08-19

## Scope

Issue [#21](https://github.com/scwlkr/balls/issues/21) replaces the synthetic browser checkpoint
with the production bundle served by `ballsd`, a narrow authenticated loopback adapter, and the
`balls ui` launch path on Windows and Linux.

## Boundary observations

- Protected named-pipe or Unix-socket IPC is the only place that can request a browser launch.
- Launch authority is a 256-bit random capability in the URL fragment, expires after one minute,
  is consumed once, and is removed from browser history after exchange.
- The browser listener is an ephemeral IPv4 loopback socket. Contract inspection found exactly one
  listener at its port and a loopback address; `/control/v1` is unavailable on that listener.
- The browser projection contains only session exchange, status, Circle list/create, and Circle
  details. It calls the same `CircleApplication` as the IPC control plane.
- Host and Origin must exactly match the selected authority. Hostile and duplicate values, missing
  antiforgery, replayed or expired authority, and bodies over 32 KiB fail closed.
- The 30-minute session cookie is HttpOnly, Secure, SameSite=Strict, and scoped to `/`. State
  changes also require the per-session antiforgery token held only in memory.
- Static and API responses carry the restrictive CSP and related security headers, no permissive
  CORS header, and no-store caching. Rejection bodies do not reflect the tested capability.

These observations are automated contract evidence, not a claim that loopback HTTP is safe to
proxy or expose remotely. The updated [threat model](../security/threat-model.md) records the
residual same-user, extension, and compromised-session risks.

## Local Windows evidence

On the Windows development host:

- 10 focused broker, browser-adapter, and OpenAPI contract tests passed;
- 4 React component journeys passed;
- Playwright using installed Chrome passed launch, first-Circle creation, Circle/Member/Node
  rendering, Circle listing, fragment removal, daemon restart, and persistent identifiers;
- the complete `full` gate passed in 47.31 seconds: 96 .NET tests passed, 10 platform-inapplicable
  tests were skipped, 4 component journeys passed, the Chromium journey passed, the category audit
  found no unclassified tests, and the Release build produced zero warnings;
- a separate Release publish contained `wwwroot/index.html` and 12 bundled asset files.

The exact clean Windows/Ubuntu pull-request runs are recorded on the delivery issue after the
documentation checkpoint. No LAN, remote-browser, physical Linux, account-login, or
Circle-admission scenario is claimed by this issue.
