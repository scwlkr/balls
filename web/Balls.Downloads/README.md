# Balls downloads site

This isolated public site is the source for `balls.wlkrlabs.com`. It gives each delivery lane a
copyable command while keeping the actual packages on public GitHub Releases.

The page is intentionally plain Vite: static HTML, CSS, and a few lines of TypeScript for the copy
buttons. The tiny Worker delegates requests to static assets and gives the Sites packager its
required `dist/server` entry point.

## Delivery contract

- `public/channels/alpha.json` is the moving Owner-accepted pointer. A Development publication adds
  or replaces `public/channels/development.json` only after package-integrity checks pass.
- `public/versions/<tag>.json` is immutable after publication. `public/releases.json` lists every
  accepted release and at most the newest ten Development manifests, newest first, plus the complete
  GitHub Releases archive.
- Every package manifest names the release tag and full commit; exact GitHub Release asset names,
  URLs, and SHA-256 values; the internal package product/version/commit/platform/architecture; and
  the runtime contract. Release tags and internal package versions may differ, so both identities
  are explicit and must agree with the actual archive.
- `public/bootstrap/windows-x64.json` binds the current native Windows bootstrap executable to an
  immutable release asset and SHA-256. The one-line website command validates that pointer,
  downloads the executable completely, verifies its hash, and runs it with the selected channel or
  version manifest. This works from the Windows PowerShell prompt even when its effective execution
  policy is `Restricted`; it never runs a `.ps1`, changes policy, or requires Git, a repository,
  PowerShell 7, or .NET.
- The native bootstrap installs inside the current user's Local AppData, records
  `installation.json`, creates a normal Start Menu shortcut, and opens the local workspace after a
  successful verified install.
- `public/install.sh` supports the published Linux x64 package.
- `public/source.sh` downloads and verifies the accepted tag for the Apple-silicon macOS
  source-only developer lane. It is not a Mac installer.
- Bootstrap commands download completely before execution. Do not replace them with
  pipe-to-shell, execution-policy bypass, trust-policy bypass, or short-lived Actions-artifact
  commands.
- Windows and Linux bootstraps bind the package filename and internal `canary.json` identity to
  the manifest's exact commit. The Windows bootstrap also verifies every internal checksum before
  writing a version under the user-owned install root.
- For historical framework-dependent packages, the Windows bootstrap resolves the x64 apphost
  runtime location rather than the first `dotnet` on `PATH`. New Windows Development packages are
  self-contained and declare that fact in the manifest, so .NET is not a user prerequisite.
- The current packages are unsigned prereleases. Keep that warning visible until the release
  contract changes.

Use the release asset API digests for archives, checksum files, and installers; never point a
durable public channel at expiring GitHub Actions artifacts.

## Development publication

An agent working an active issue may publish Development after exact-commit build and integrity
checks. This does not authorize Alpha, Beta, or Stable movement.

1. Record the current `channels/development.json` and `bootstrap/windows-x64.json` contents, tags,
   and hashes, or record that a pointer does not exist. These are the two rollback pointers.
2. Build the Windows package once from the identified commit with `--runtime win-x64
--self-contained true`; run the package-integrity and focused package tests against those bytes.
3. Create the canonical tag `development-<yyyyMMddTHHmmssZ>-<commit12>` using the UTC package-build
   preparation time and package commit, then create an immutable GitHub prerelease. Upload the
   archive, archive checksum, legacy repository installer, native x64 bootstrap, and the normal
   release integrity assets. Do not upload invitations, credentials, Node state, logs, or
   screenshots containing private data.
4. Read asset digests back from the GitHub Release API. Create `versions/<tag>.json` with the exact
   tag, full commit, URLs, SHA-256 values, internal `canary.json` identity, and `self-contained`
   Windows runtime. Generate and validate the immutable manifest, moving pointer, and catalog with:

   ```bash
   dotnet run --project eng/Balls.Canary --configuration Release -- development-manifest \
     --public-root web/Balls.Downloads/public \
     --package-path <release-directory>/<windows-archive>.zip \
     --checksum-path <release-directory>/<windows-archive>.zip.sha256 \
     --installer-path <release-directory>/Install-BallsCanary.ps1 \
     --bootstrap-path <release-directory>/balls-bootstrap-windows-x64-<commit12>.exe \
     --tag <development-tag> \
     --commit <full-commit> \
     --published-at <yyyy-MM-ddTHH:mm:ssZ>
   ```

   The command refuses a framework-dependent archive, a forbidden sensitive file or common
   credential pattern, an identity/hash/filename mismatch, or a changed existing version manifest.
   It also creates an immutable `bootstrap/versions/<tag>.json` descriptor and prints the prior
   Development tag, channel-manifest SHA-256, and native-bootstrap-pointer SHA-256 for the issue
   rollback record.

5. Publish the immutable prerelease assets, `versions/<tag>.json`, and
   `bootstrap/versions/<tag>.json` first while leaving every moving pointer, the live site command,
   and the release catalog unchanged. Read that exact manifest back from
   `https://balls.wlkrlabs.com/versions/<tag>.json`, then run the exact-release smoke below against
   that immutable version URL under an authorized ordinary Windows profile. The smoke installs the
   package and checks the daemon path, Start Menu shortcut, execution policy, and owned cleanup. It
   validates both automatic first launch and a real Start Menu shortcut relaunch, requires each
   installed daemon to own exactly two private IPv4 TCP listeners, rejects any wildcard/public
   listener, and reports `privateListenersVerified: true` without recording the selected private
   address or ports:

   ```powershell
   # Release-engineering seam from a repository checkout; this is not an Owner command.
   .\eng\canary\Test-WindowsDownload.ps1 `
     -ManifestUri https://balls.wlkrlabs.com/versions/<development-tag>.json `
     -BootstrapManifestUri https://balls.wlkrlabs.com/bootstrap/versions/<development-tag>.json `
     -ExpectedTag <development-tag> `
     -ExpectedCommit <full-commit>
   ```

6. Only after that immutable-version smoke passes, deploy the generated native-bootstrap pointer
   and site command, prepend the build to `releases.json`, retain
   every accepted release and only the newest ten Development rows, and preserve the
   complete-history link. Append the generator's prior-pointer output and the new exact identity to
   [`DEVELOPMENT-POINTER-LEDGER.md`](DEVELOPMENT-POINTER-LEDGER.md), then deploy the generated
   Development pointer and catalog. The Owner never needs this repository script; the Owner copies
   only the one native-bootstrap command displayed by the website. Run the local gates below before
   each deployment. Read back the
   live warned Development entry and repeat the copied Development command once to prove it resolves
   to the same installed `installation.json` identity. Record only the observed identities and
   outcomes on the issue.

If publication or live installation fails, restore the recorded prior Development pointer. Never
edit an already-published version manifest or overwrite a GitHub Release asset.

GitHub Release immutability must be enabled before publishing. If the repository setting is off,
publication is blocked; do not substitute a mutable prerelease or a short-lived Actions artifact.

Alpha promotion is a separate Owner gate. It moves only `channels/alpha.json` to the identical
green-`main` assets already rehearsed through Development; it never rebuilds them.

## Issue #92 target contract

The [Private Boss Demo specification](../../docs/specs/private-boss-demo-v1.md) adds:

- accepted Alpha as the primary section;
- a warned Development section below it;
- a previous-versions list containing every accepted release and the newest ten Development builds;
- immutable version manifests for historical commands and a complete GitHub Releases link;
- built-in Windows PowerShell compatibility, a self-contained current-user package, automatic first
  launch, a normal shortcut, and persisted channel/package identity;
- normal first launch and shortcut launch automatically bind the two required remote services to
  one unambiguous private IPv4 interface without user-supplied addresses, ports, or daemon setup.

Development is a durable public testing lane, not Canary. It may point to an incomplete or broken
immutable GitHub prerelease from an identified issue branch or `main` commit. An active issue may
publish Development after build and package-integrity checks and must record the prior pointer.
Alpha promotion remains Owner-gated and points to the exact green-`main` assets already rehearsed
through Development. See
[`ADR 0010`](../../docs/decisions/0010-public-development-download-channel.md).

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

Before moving a public channel to a newer packaged release, rerun the exact copied command in the
authorized Windows installer lab, then record only the observed release identity and outcome on
the issue.

Development publication is authorized only by the bounded active-issue process above. Alpha, Beta,
and Stable publication plus custom-domain attachment remain Owner-gated operations. The Sites
project ID is recorded in `.openai/hosting.json` only when that gate is approved.
