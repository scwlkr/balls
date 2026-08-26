# Balls downloads site

This isolated public site is the source for `balls.wlkrlabs.com`. It gives each delivery lane one
stable command while keeping the actual packages on the public GitHub Release.

The page is intentionally plain Vite: static HTML, CSS, and a few lines of TypeScript for the copy
buttons. The tiny Worker delegates requests to static assets and gives the Sites packager its
required `dist/server` entry point.

## Delivery contract

- `public/channels/alpha.json` is the only moving Alpha pointer. It names one owner-accepted tag,
  exact commit, GitHub Release asset URLs, SHA-256 values, and any package runtime contract.
- `public/install.ps1` supports the published Windows x64 package.
- `public/install.sh` supports the published Linux x64 package.
- `public/source.sh` downloads and verifies the accepted tag for the Apple-silicon macOS
  source-only developer lane. It is not a Mac installer.
- Bootstrap commands download completely before execution. Do not replace them with
  pipe-to-shell, execution-policy bypass, trust-policy bypass, or short-lived Actions-artifact
  commands.
- Windows and Linux bootstraps bind the package filename and internal `canary.json` identity to
  the manifest's exact commit before invoking the repository installer.
- The Windows bootstrap resolves the x64 apphost runtime location, not the first `dotnet` on
  `PATH`, and enforces the manifest's framework list before downloading the package. A future
  self-contained Windows package can remove that prerequisite through the manifest alone.
- The current packages are unsigned prereleases. Keep that warning visible until the release
  contract changes.

When publishing a new Alpha, update only the channel manifest after the release is owner-accepted.
Use the release asset API digests for the archives, checksum files, and installers; never point the
public channel at expiring GitHub Actions artifacts.

## Local verification

From the repository root:

```bash
pnpm install --frozen-lockfile
pnpm downloads:format:check
pnpm downloads:lint
pnpm downloads:typecheck
pnpm downloads:build
pnpm downloads:test
pnpm downloads:e2e
```

Use `pnpm downloads:dev` for the local site. The root Balls fast gate runs the same static-site
checks on Windows, Linux, and macOS.

Before moving the public channel to a newer packaged release, rerun the exact stable command in a
disposable Linux install root and the authorized Windows installer lab, then record only the
observed release identity and outcome on the issue.

Publishing and custom-domain attachment remain owner-gated operations. The Sites project ID is
recorded in `.openai/hosting.json` only when that gate is approved.
