# Trusted Circle milestone evidence

**Date:** 2026-08-21

**Issue:** [#34](https://github.com/scwlkr/balls/issues/34)

**Target:** `0.3.0-alpha.1`

## Status

Trusted Circle is a release candidate. Product code, contracts, migrations, and the milestone's
cross-host outcome are implemented. The shared .NET/web product version is `0.3.0-alpha.1`.

The candidate landed through protected `main` as exact commit
`8dc39455ac432c6f295a86fad3a765d4f70a1fe9`. Its once-built Windows and Linux Canaries passed
hosted and independent exact-artifact verification. Explicit owner acceptance is still required;
no tag or GitHub Release has been created.

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
- the final pre-push `full` verifier passed in 117.92 seconds;
- formatting, generated-client drift, web lint/typecheck, zero-warning Release build, all current
  .NET/web tests, and the Playwright Chromium journey passed;
- NuGet reported no vulnerable direct or transitive packages, and pnpm reported no known
  vulnerabilities;
- 54 tracked Markdown files had zero broken relative links;
- repository state validation confirmed product version `0.3.0-alpha.1`, only #34 open in the
  active milestone, exactly #56 ready in the seven-issue prepared milestone, and no existing
  `0.3.0-alpha.1` tag or release.

[Pull request #63](https://github.com/scwlkr/balls/pull/63) passed dependency review, CodeQL, and
fixed Windows 2025, Ubuntu 24.04, and Apple-Silicon macOS 26 lanes before squash merge. Its first
Windows attempt exposed an old fixed test instant whose real 24-hour TLS certificate had expired
25 seconds before the run. The fixture now samples wall-clock UTC once and freezes application
time without bypassing certificate validity. The focused test then passed 11 consecutive runs,
the local fast gate passed, both independent reviews found no violation, and the hosted Windows
rerun passed in 4 minutes 25 seconds.

The historical gitleaks match for the literal metadata pair
`privateKeyEncoding` / `encrypted-pkcs8` is not a credential. Its exact fingerprint is ignored;
the final tracked-history scan found no leaks.

## Proven headline outcome

The [persistent-message record](2026-08-21-persistent-circle-message.md) first proved the full
outcome from exact source. Protected-main
[CI run 32522245443](https://github.com/scwlkr/balls/actions/runs/32522245443) then passed Windows,
Ubuntu, macOS, Required, and both once-built Canary jobs for `8dc39455ac43`.

| Platform | GitHub artifact | Downloaded package | Independent smoke |
| --- | --- | --- | --- |
| Physical Windows | ID `9461108213`; 18,955,912 bytes; artifact digest `9fefd3cc4eb059f31d6222a1884ebc07ae07540db72d82084d3f1fdbaf3b208f` | 18,948,090-byte ZIP; SHA-256 `8d7f4c5d02a4ae17787d7617ff6a75394b35c898efa7d55be8c63d51fb049ae1` | outer/internal checksums, fresh install, structured CLI/Circle, real Chrome UI, and restart passed; fresh Node `01a025f8-82ff-7f31-91de-838b4007169a` |
| Ubuntu 24.04 Hyper-V guest | ID `9461099183`; 18,872,208 bytes; artifact digest `15e9bb345c6f2e6eb1f28915393b3ad5a8a3681ad44986a2b5cbbc8a3ce4f5b2` | 18,866,957-byte ZIP; SHA-256 `ab09f8795f4ef9f6ef31b7fcd606b889a62aab0daca68a488a8f0a710ac2f0fe` | outer/internal checksums, fresh install, structured CLI/Circle, Chrome-for-Testing UI, socket cleanup, and restart passed; fresh Node `01a025fb-34d2-7679-875a-caae185c5caa` |

The two downloaded packages then completed one shared private-network journey. Ubuntu Anchor Node
`01a025fc-4d1d-77d7-989e-29665f77c39d` created Circle
`01a025fe-dfc8-7841-bb14-df1511a4e2a6`; invitation-pinned TLS 1.3 admitted physical-Windows Node
`01a025fd-e5f9-72a7-a8f3-5c8bcb657c88`. Both sides showed Alice/Bob and the same two Nodes.
Windows authored message `0198c2d8-b000-7000-8000-000000000635`; both histories stored exactly
one identical sequence-`1` row. The packaged Windows browser rendered the Circle, attribution,
message, and `#1` before and after both daemons restarted. Exact retry after restart retained both
Node identities, the original authored/accepted times, sequence `1`, and one row per Node.

The guest retained machine ID `498d59af31b04f34bb19722beab972aa`. Its dated checkpoint lacked
the intended runtime/browser, so .NET 10.0.11, Chrome for Testing 152.0.7977.54, and only the
required extracted user-local libraries were staged without system package installation. Spent
invitation files and all generated private identity/state/tool directories were removed; the lab
again reported a clean Balls identity. No second physical machine was used.

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
  workflow/commit provenance. Artifact attestations are not currently claimed. GitHub produced a
  valid SPDX 2.3 dependency-graph SBOM with 333 packages and 487 relationships; the staged
  313,580-byte JSON has SHA-256
  `60901d2bb3378a0b53660a025bee1fd70491c017f6f3b7f3edd5a63991c79daf`.

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

1. Obtain explicit owner acceptance.
2. Tag `8dc39455ac432c6f295a86fad3a765d4f70a1fe9`, promote only the verified exact artifacts, and
   attach their checksum files, installers, and the staged SPDX SBOM.
3. Verify anonymous public readback, then close #34 and the milestone.
