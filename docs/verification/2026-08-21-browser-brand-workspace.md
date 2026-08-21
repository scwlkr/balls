# Browser brand workspace verification

**Date:** 2026-08-21  
**Issue:** [#49](https://github.com/scwlkr/balls/issues/49)

## Outcome

The React workspace now uses the connected-node mark, carbon/graphite/indigo/mist palette,
Manrope-led typography, compact status/icon language, and one dark Circle trust-topology signature
derived from the canonical `balls-brand.png`. The generic `B` placeholder is gone. Product copy
and navigation stay Circle-first, while all application behavior remains behind the existing typed
`BrowserApi` seam.

The small browser foundation is documented in `web/Balls.Web/README.md`. It consists of the
existing workspace components, one reusable `BrandMark`, and tokens/rules in `src/styles.css`; no
general component package or new product capability was added.

## Interaction and accessibility evidence

- Eight focused component tests passed. They cover launch exchange, empty/populated structure,
  production brandmark use, loading status, create busy state, switching busy/current state,
  Circle roster presentation, and fail-closed session errors.
- The loading and switching states expose live status roles. Create and switch surfaces expose
  `aria-busy`; switch controls remain disabled while the previous Circle stays visible.
- Browser review found and fixed a masthead defect that exposed People/Nodes links during loading
  and error states. The regression check now requires those links to appear only with a selected
  Circle.
- Keyboard inspection reached the skip link with a solid `3px` signal-indigo focus outline.
- Chromium reduced-motion emulation computed the brandmark connection animation at `1e-05s`.
- At `390 × 844`, the document had no horizontal overflow; navigation remains reachable and the
  topology stacks into Circle, Members, and Nodes.
- Checked foreground/background pairings range from `5.69:1` to `18.59:1`: success on white,
  muted on white, signal-deep on white, danger on danger-pale, and white on carbon/signal-deep.

## Reviewed screenshots

The transient visual fixture rendered the production `App` through its existing injectable API
test seam, held asynchronous states for capture, and was removed afterward. The real production
bundle was then exercised separately through `ballsd`'s protected loopback browser harness.

| State | Screenshot |
| --- | --- |
| Loading, desktop | [loading-desktop.png](screenshots/issue-49/loading-desktop.png) |
| Empty, desktop | [empty-desktop.png](screenshots/issue-49/empty-desktop.png) |
| Creating busy, desktop | [busy-desktop.png](screenshots/issue-49/busy-desktop.png) |
| Populated, desktop | [populated-desktop.png](screenshots/issue-49/populated-desktop.png) |
| Switching busy, desktop | [switching-desktop.png](screenshots/issue-49/switching-desktop.png) |
| Session error, desktop | [error-desktop.png](screenshots/issue-49/error-desktop.png) |
| Populated, narrow | [populated-narrow.png](screenshots/issue-49/populated-narrow.png) |

## Observed verification

- `dotnet build eng/Balls.BrowserHarness/Balls.BrowserHarness.csproj --configuration Release`
  passed with zero warnings and zero errors; it rebuilt the production Vite bundle.
- `pnpm web:e2e` passed its real Chromium launch, create, list, and daemon-restart journey (`1/1`).
- `pnpm web:format:check`, `pnpm web:lint`, `pnpm web:typecheck`, and `pnpm web:build` passed.
- The protected browser/API boundary, protocol, durable state, remote access, and native GUI scope
  were unchanged.
