# Trusted Circle milestone evidence

**Date:** 2026-08-21

**Issue:** [#34](https://github.com/scwlkr/balls/issues/34)

**Target:** `0.3.0-alpha.1`

## Status

Trusted Circle is a release candidate. Product code, contracts, migrations, and the milestone's
cross-host outcome are implemented. The shared .NET/web product version is `0.3.0-alpha.1`.

The final accepted commit is not defined until this candidate lands through protected `main`.
Only the Windows and Linux Canaries built once by that commit may be promoted. Exact downloaded
artifact verification and explicit owner acceptance are still required; no tag or GitHub Release
has been created.

## Completed implementation outcomes

| Issue | Outcome | Hosted acceptance evidence |
| --- | --- | --- |
| [#33](https://github.com/scwlkr/balls/issues/33) | Circle identity, admission, and remote security design | [acceptance ledger](https://github.com/scwlkr/balls/issues/33#issuecomment-5359867004) |
| [#35](https://github.com/scwlkr/balls/issues/35) | protected Node and Circle cryptographic authority | [acceptance ledger](https://github.com/scwlkr/balls/issues/35#issuecomment-5360368958) |
| [#36](https://github.com/scwlkr/balls/issues/36) | bounded canonical single-use invitations | [acceptance ledger](https://github.com/scwlkr/balls/issues/36#issuecomment-5360776295) |
| [#37](https://github.com/scwlkr/balls/issues/37) | authenticated encrypted LAN transport | [acceptance ledger](https://github.com/scwlkr/balls/issues/37#issuecomment-5361426656) |
| [#38](https://github.com/scwlkr/balls/issues/38) | persisted two-Node Circle admission | [acceptance ledger](https://github.com/scwlkr/balls/issues/38#issuecomment-5362445083) |
| [#39](https://github.com/scwlkr/balls/issues/39) | one persistent Member-and-Node-signed Circle message | [acceptance ledger](https://github.com/scwlkr/balls/issues/39#issuecomment-5374169896) |

All six implementation issues are closed on `main`. Their evidence identifies exact landed
commits, focused/full tests, required hosted checks, security boundaries, and the observed
physical-Windows/virtual-Ubuntu limit.

## Candidate verification

The release-version change was developed against the public daemon status boundary:

- red: the focused named-pipe status contract expected `0.3.0-alpha.1` and observed
  `0.2.0-alpha.1`;
- green: after the shared version change and lock regeneration, the same contract passed;
- locked .NET restore and frozen pnpm resolution passed;
- the complete local `full` verifier passed in 132.21 seconds;
- formatting, generated-client drift, web lint/typecheck, zero-warning Release build, all current
  .NET/web tests, and the Playwright Chromium journey passed;
- NuGet reported no vulnerable direct or transitive packages, and pnpm reported no known
  vulnerabilities;
- 53 tracked Markdown files had zero broken relative links.

The historical gitleaks match for the literal metadata pair
`privateKeyEncoding` / `encrypted-pkcs8` is not a credential. Its exact fingerprint is ignored;
the tracked-history scan must be rerun on the final candidate.

## Proven headline outcome

The [persistent-message record](2026-08-21-persistent-circle-message.md) proves the full outcome
on one physical Windows host and the owned Ubuntu 24.04 Hyper-V guest: invitation-pinned TLS 1.3
admission, the same two Members and Nodes, one Ubuntu-authored message with identical CLI/browser
history, both-daemon restart, and retry-stable Anchor sequence `1`.

That source proof and the subsequent individual green-main Canary smokes establish the product
behavior but are not substituted for this release's exact artifact gate. After the candidate merge,
the exact `0.3.0-alpha.1` Windows and Linux Canaries must repeat checksum installation, invitation,
admission, shared membership, persistent message, real browser observation, restart, and retry.

## Repository and security state

- `scwlkr/balls` is public, unarchived, and uses `main` as its default branch.
- Active ruleset `21056510` blocks deletion and force-push, requires linear squash-only pull
  requests, resolved review threads, strict up-to-date `Required` status, and has no bypass actor.
- `Required` aggregates fixed Windows 2025, Ubuntu 24.04, and Apple-Silicon macOS 26 fast lanes.
- Actions use read-only defaults, cannot approve pull requests, require full-SHA pins, and permit
  only GitHub-owned actions plus the exact pinned OpenSSF Scorecard action.
- Private vulnerability reporting is enabled. Dependabot has zero open alerts. CodeQL reports no
  open code alerts. Six open Scorecard findings are repository-maturity signals rather than an
  observed credential, code vulnerability, corruption path, or launch failure.
- Canary packages carry external and internal SHA-256 checksums plus GitHub artifact digests and
  workflow/commit provenance. Artifact attestations are not currently claimed. The Alpha release
  will attach the exact installers, checksums, and GitHub dependency-graph SPDX 2.3 SBOM.

## Documentation reconciliation

README, roadmap/state, architecture, local and remote protocols, storage, development, security,
and the detailed files-first program were reviewed against the implementation. Product-version
examples now identify `0.3.0-alpha.1`; historical evidence remains immutable. No protocol or
SQLite version changed merely because the product version changed.

Trusted Circle remains one selected Anchor with explicit authority export and no automatic
failover. The loopback browser boundary remains separate from remote Circle transport. Channels,
direct messages, edits/deletes, attachments, reactions, offline catch-up, Tailscale, Circle Files,
and multiple-Anchor replication remain non-goals for this Alpha.

## Prepared next milestone

The `0.4.0-alpha.1 — LAN Circle Files` milestone now has seven executable issues:

1. [#56 provider-independent contributions and grants](https://github.com/scwlkr/balls/issues/56) — ready;
2. [#57 fail-closed Windows SMB readiness](https://github.com/scwlkr/balls/issues/57);
3. [#58 narrow privileged folder/share helper](https://github.com/scwlkr/balls/issues/58);
4. [#59 one limited SMB credential per grant](https://github.com/scwlkr/balls/issues/59);
5. [#60 unelevated Explorer mapping](https://github.com/scwlkr/balls/issues/60);
6. [#61 revocation and owned-only cleanup](https://github.com/scwlkr/balls/issues/61);
7. [#62 milestone verification and acceptance](https://github.com/scwlkr/balls/issues/62).

#56 is the only `ready-for-agent` frontier. #57–#62 are dependency-blocked. Circle Files
implementation does not start before Trusted Circle is accepted.

## Remaining acceptance gates

1. Merge this candidate through protected `main` with all required hosted checks green.
2. Download the resulting exact Windows/Linux Canaries and complete the cross-artifact outcome.
3. Append artifact IDs, sizes, SHA-256 values, commit identity, environment evidence, and any
   honest limitations to this record.
4. Obtain explicit owner acceptance.
5. Tag the accepted commit, promote only those exact artifacts, attach the SPDX SBOM, verify
   anonymous public readback, and then close #34 and the milestone.
