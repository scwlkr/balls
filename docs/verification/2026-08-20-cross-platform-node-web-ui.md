# Cross-platform Node and Web UI milestone evidence — 2026-08-20

## Status

`0.2.0-alpha.1` is an owner-authorized release candidate. The release tag will target the exact
protected `main` squash commit produced by the candidate pull request, and publication will promote
only the Windows and Linux artifacts built once by that commit's successful CI run. Final tag,
asset, checksum, SBOM, and anonymous-download readback will be appended after publication.

## Completed outcomes

| Issue | Outcome | Hosted evidence |
| --- | --- | --- |
| [#17](https://github.com/scwlkr/balls/issues/17) | platform-neutral daemon/CLI host composition | [acceptance comment](https://github.com/scwlkr/balls/issues/17#issuecomment-5348322990) |
| [#18](https://github.com/scwlkr/balls/issues/18) | protected native Linux state and Unix-domain-socket control | [acceptance comment](https://github.com/scwlkr/balls/issues/18#issuecomment-5348631059) |
| [#19](https://github.com/scwlkr/balls/issues/19) | stable structured CLI and dual-platform process behavior | [acceptance comment](https://github.com/scwlkr/balls/issues/19#issuecomment-5348745139) |
| [#20](https://github.com/scwlkr/balls/issues/20) | pinned React workspace and generated local API edge | [acceptance comment](https://github.com/scwlkr/balls/issues/20#issuecomment-5349059032) |
| [#21](https://github.com/scwlkr/balls/issues/21) | hardened loopback browser UI served by `ballsd` | [acceptance comment](https://github.com/scwlkr/balls/issues/21#issuecomment-5349419025) |
| [#22](https://github.com/scwlkr/balls/issues/22) | exact Windows/Ubuntu Node/UI outcome and runnable Canaries | [acceptance comment](https://github.com/scwlkr/balls/issues/22#issuecomment-5357880488) |

All six implementation issues are closed on `main`. Their pull requests were squash-merged and
their acceptance evidence states the observed tests, platform boundary, and exact landed commit.

## Feedback budgets

Measured on the Windows development host after the `0.2.0-alpha.1` version change:

| Path | Observation | Budget | Result |
| --- | ---: | ---: | --- |
| Focused Core unit selection | 8 passed in 7.52s | `<15s` | pass |
| First local fast gate after lock regeneration | all checks passed in 61.54s | `<60s` warm target | cold observation |
| Warm local fast gate | all checks passed in 45.52s | `<60s` | pass |
| Warm Playwright browser journey | 1 passed in 2.53s wall time | measured | pass |
| PR #32 Ubuntu fast | 2m30s | `<5m` | pass |
| PR #32 Windows fast | 3m38s | `<5m` | pass |

The first post-version-change run is retained rather than hidden: it crossed the warm target by
1.54 seconds while restoring regenerated project locks and rebuilding. The immediate warm rerun
passed with 14.48 seconds of margin. PR #32's fail-closed `Required` job passed in 2 seconds;
dependency review and CodeQL also passed.

The final local `full` gate completed in 44.75 seconds. Locked .NET and pnpm restore, both
formatters, generated-client drift, lint, typecheck, and the Release build passed with zero
warnings or errors; 97 .NET tests passed with 10 platform-appropriate skips, 4 React tests passed,
and the Playwright Chromium journey passed. Relative-link validation found zero broken links in 43
Markdown files; repository/GitHub state validation confirmed version `0.2.0-alpha.1`, six closed
implementation issues, only #23 open in the active milestone, and only #33 ready in the prepared
milestone. Gitleaks found no leaks, and `pnpm audit --audit-level high` found no known
vulnerabilities.

## Exact current-main artifact observation

Before changing the product version, protected [main CI run 32385414549](https://github.com/scwlkr/balls/actions/runs/32385414549)
passed Windows fast, Ubuntu fast, `Required`, Windows Canary, and Linux Canary for exact commit
`1aac67ed07ebbdfdd5dd4f67099754feea8b56d5`. CodeQL and Scorecard also passed.

| Platform | Workflow artifact | Artifact ID | Downloaded archive bytes | Downloaded archive SHA-256 | Expires |
| --- | --- | ---: | ---: | --- | --- |
| Windows x64 | `balls-0.1.0-alpha.2-canary-windows-x64-1aac67ed07eb` | `9412891766` | 18,450,495 | `A6BEB560B89BD2DDBD4D145D8CB7B3B790FBDAF0C106688AEF0CC3B044087C87` | 2026-09-03 15:24 UTC |
| Linux x64 | `balls-0.1.0-alpha.2-canary-linux-x64-1aac67ed07eb` | `9412842054` | 18,370,087 | `6EB1D93FF612933A7A35EE50B15E514B5F36960C93F3BCB06534CAE01441D3A3` | 2026-09-03 15:23 UTC |

The artifact IDs are read from the workflow API. Both downloaded archives passed their external
SHA-256 files and every internal packaged-file checksum. Their `canary.json` files name the full
`1aac67ed07ebbdfdd5dd4f67099754feea8b56d5` source commit.

The Windows package independently passed fresh installation, structured status and Circle work,
real browser rendering, loopback-only exposure, daemon restart, and stable identifiers as Node
`01a0200c-03cf-7376-b432-e047bc7298c4`.

The Linux package independently passed the same committed smoke in dedicated Ubuntu 24.04 VM
`Balls.Lab.Ubuntu` with ASP.NET Core 10.0.11 and Google Chrome 151.0.7922.169. The fresh smoke Node
was `01a0200e-7584-704c-a90c-e3e756c4a4de`. Restoring the explicit `Balls.Lab.Clean` checkpoint
returned machine ID `498d59af31b04f34bb19722beab972aa` with Balls identity clean. Unrelated
`BallsServer.Test.Client`, `BallsServer.Test.Host`, `BallsServer.Test.Private`, Default Switch, and
WSL switch state was observed unchanged.

The final release artifacts will be the same package flow under version `0.2.0-alpha.1` from the
candidate's protected-main merge commit, not a rebuild of the artifacts above.

## Repository and security state

- `scwlkr/balls` is public and unarchived; `main` is the default branch.
- Active ruleset `21056510` blocks deletion and force-push, requires linear history, permits only
  squash pull requests, requires resolved review threads, and requires strict `Required` status.
- Repository settings allow squash only, delete merged branches, and keep Actions read-only by
  default; workflows cannot approve pull requests.
- Actions are limited to GitHub-owned actions plus the exact pinned OpenSSF Scorecard action, and
  repository-level full-SHA enforcement is enabled.
- Private vulnerability reporting is enabled. Dependabot has zero open alerts. The code-scanning
  API has zero open CodeQL-tool alerts and six open Scorecard maturity signals for branch
  protection, review, fuzzing, SAST recognition, maintenance, and CII Best Practices. They do not
  report an observed credential, code vulnerability, data-loss path, or product launch failure.
- The final candidate gate includes a gitleaks scan, license/readme/repository hygiene, locked
  dependency restore, formatting, generated-client drift, lint, typecheck, zero-warning Release
  build, full .NET/web tests, Playwright, and relative-link/state validation.

## Documentation reconciliation

README, roadmap/state, architecture, local-control protocol, development, storage, and security
records were reviewed against the executable result. Product version examples now identify
`0.2.0-alpha.1`; historical `0.1.0-alpha.2` evidence remains unchanged. The browser remains a
narrow client of existing application behavior, the remote Circle trust boundary remains absent,
and SQLite/local-control schema versions did not change with the product version.

## Evidence boundary and blockers

- Physical Windows host: observed.
- Dedicated Ubuntu Hyper-V VM: observed.
- GitHub-hosted Windows 2025 and Ubuntu 24.04 runners: observed.
- Physical Linux hardware, macOS, service installation, binary signing, artifact attestation,
  remote Circle transport, invitation/admission, and multi-machine messaging: unverified or not
  implemented and not claimed by this Alpha.
- No known credential/private-data exposure, destructive data loss, unsafe system mutation,
  corrupt migration, or inability to install/start/exercise the release outcome is open.

## Owner decision and next milestone

The owner explicitly authorized completing #23, publishing `0.2.0-alpha.1`, and continuing to the
next ready issue. The `0.3.0-alpha.1 — Trusted Circle` milestone now has seven executable issues:

1. [#33 identity/admission/remote-protocol decision](https://github.com/scwlkr/balls/issues/33) — ready;
2. [#35 protected cryptographic authority](https://github.com/scwlkr/balls/issues/35);
3. [#36 bounded single-use invitations](https://github.com/scwlkr/balls/issues/36);
4. [#37 authenticated encrypted LAN transport](https://github.com/scwlkr/balls/issues/37);
5. [#38 two-Node admission and shared membership](https://github.com/scwlkr/balls/issues/38);
6. [#39 one persistent Circle message](https://github.com/scwlkr/balls/issues/39);
7. [#34 milestone verification and acceptance](https://github.com/scwlkr/balls/issues/34).

#33 is the single ready frontier. Every later issue is dependency-blocked.
