# Cross-platform Node and Web UI milestone evidence — 2026-08-20

## Status

[`0.2.0-alpha.1`](https://github.com/scwlkr/balls/releases/tag/0.2.0-alpha.1) is published as a
public prerelease. Its annotated tag peels to exact protected-main squash commit
`3935b6ac275b24c8ed2389862b012da747099f34`; publication promoted only the artifacts produced by
that commit's CI run plus its matching installers and dependency-graph SPDX SBOM. Anonymous
readback verified every public asset byte-for-byte.

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
| PR #40 Ubuntu fast | 1m58s | `<5m` | pass |
| PR #40 Windows fast | 3m32s | `<5m` | pass |
| Accepted-main Ubuntu fast | 1m47s | `<5m` | pass |
| Accepted-main Windows fast | 4m26s | `<5m` | pass |

The first post-version-change run is retained rather than hidden: it crossed the warm target by
1.54 seconds while restoring regenerated project locks and rebuilding. The immediate warm rerun
passed with 14.48 seconds of margin. PR #40's fail-closed `Required` job passed in 3 seconds;
dependency review and CodeQL also passed. The accepted-main `Required` job passed in 4 seconds.

The final local `full` gate completed in 44.75 seconds. Locked .NET and pnpm restore, both
formatters, generated-client drift, lint, typecheck, and the Release build passed with zero
warnings or errors; 97 .NET tests passed with 10 platform-appropriate skips, 4 React tests passed,
and the Playwright Chromium journey passed. Relative-link validation found zero broken links in 43
Markdown files; repository/GitHub state validation confirmed version `0.2.0-alpha.1`, six closed
implementation issues, only #23 open in the active milestone, and only #33 ready in the prepared
milestone. Gitleaks found no leaks, and `pnpm audit --audit-level high` found no known
vulnerabilities.

## Pre-candidate current-main artifact observation

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

## Exact accepted release observation

PR [#40](https://github.com/scwlkr/balls/pull/40) squash-merged the candidate as
`3935b6ac275b24c8ed2389862b012da747099f34`. Accepted-main
[CI run 32395509618](https://github.com/scwlkr/balls/actions/runs/32395509618) passed both fast lanes,
`Required`, Linux Canary, and the exact-commit Windows Canary rerun. The first Windows Canary
attempt reported only pipe unavailability through its 15-second readiness deadline; the unchanged
rerun passed packaging, fresh install, structured CLI, real Chrome, restart, and upload in 2m58s.
[CodeQL](https://github.com/scwlkr/balls/actions/runs/32395509631) and
[Scorecard](https://github.com/scwlkr/balls/actions/runs/32395509705) passed.

| Platform | Workflow artifact | Artifact ID | Downloaded archive bytes | Downloaded archive SHA-256 | Expires |
| --- | --- | ---: | ---: | --- | --- |
| Windows x64 | `balls-0.2.0-alpha.1-canary-windows-x64-3935b6ac275b` | `9416836654` | 18,450,758 | `FC3B01351168F1092220C71073507338A7C3DCAD68B900DEB6FE14477122BEDB` | 2026-09-03 17:14 UTC |
| Linux x64 | `balls-0.2.0-alpha.1-canary-linux-x64-3935b6ac275b` | `9416652944` | 18,370,307 | `291567B0C5690A1B1676BE002EE7BA20E211C208F3A2858DB880F2DA4FF71ABD` | 2026-09-03 17:09 UTC |

Both archives passed their external SHA-256 file and every internal checksum. Both manifests name
version `0.2.0-alpha.1`, the full accepted commit, their exact platform, x64 architecture, and
`runtimeSupported: true`. The exact Linux archive independently passed fresh installation,
structured Circle work, real Chrome, loopback-only exposure, and restart-stable identity in the
owned Ubuntu 24.04 VM; the explicit reset then restored its clean identity checkpoint without
changing unrelated VMs or switches.

The anonymously downloaded Windows archive was checksum-identical to the CI upload and internally
intact. The owner's managed Windows PC rejected its unsigned `Balls.Core.dll` under Enterprise
Application Control (`0x800711C7`; Code Integrity events 3033/3077), while a local build of the same
commit and GitHub's exact CI package flow passed the full smoke. No security policy was weakened.
This is an explicit unsigned-Alpha distribution limitation: a managed machine may require an
administrator-approved signer or allow policy. It is not claimed as physical-machine proof for the
exact accepted Windows asset.

Annotated tag object `1e0ffc8facfec38b7569521a61c7958b334f38f5` peels to the accepted commit. The
public prerelease is non-draft and contains seven assets:

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| Windows archive | 18,450,758 | `FC3B01351168F1092220C71073507338A7C3DCAD68B900DEB6FE14477122BEDB` |
| Windows checksum | 123 | `43E665B6116187B13AC8E3830A653F98E3E262A462C79FB4A2C9608C54ED2B45` |
| `Install-BallsCanary.ps1` | 7,121 | `BEF520F4D8CDD71E4AF50707F209059740C1D962E84BE12572C65BA4DD8B88D7` |
| Linux archive | 18,370,307 | `291567B0C5690A1B1676BE002EE7BA20E211C208F3A2858DB880F2DA4FF71ABD` |
| Linux checksum | 120 | `E40492814CF6211D368A0B344CEDD95E836E5EEBB09ADAA29332D460F6C8EA7D` |
| `Install-BallsCanary.sh` | 4,563 | `CEB75E206B0366172BFDCE976D16A87D7328D6DDF62F6194CD1521D373277226` |
| SPDX 2.3 SBOM | 231,968 | `C4A4008E1B8A51969C8788364F4B81E61F34C74DE268F23C9B953529FBAD395E` |

Unauthenticated downloads matched every source byte and GitHub-reported digest. Both installers
match the accepted commit. The SBOM parses as SPDX 2.3 with 332 packages and creation time
2026-08-20 17:20:55 UTC.

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

- Physical Windows host: pre-candidate and local-build outcome observed; exact accepted download
  blocked by the host's Enterprise Application Control because it is unsigned.
- Dedicated Ubuntu Hyper-V VM: observed.
- GitHub-hosted Windows 2025 and Ubuntu 24.04 runners: observed.
- Physical Linux hardware, macOS, service installation, binary signing, artifact attestation,
  remote Circle transport, invitation/admission, and multi-machine messaging: unverified or not
  implemented and not claimed by this Alpha.
- No known credential/private-data exposure, destructive data loss, unsafe system mutation, or
  corrupt migration is open. Unsigned managed-Windows distribution remains explicitly unsupported.

## Owner decision and next milestone

The owner explicitly authorized completing #23, publishing `0.2.0-alpha.1`, and continuing to the
next ready issue. Publication and anonymous readback are complete. The
`0.3.0-alpha.1 — Trusted Circle` milestone now has seven executable issues:

1. [#33 identity/admission/remote-protocol decision](https://github.com/scwlkr/balls/issues/33) — ready;
2. [#35 protected cryptographic authority](https://github.com/scwlkr/balls/issues/35);
3. [#36 bounded single-use invitations](https://github.com/scwlkr/balls/issues/36);
4. [#37 authenticated encrypted LAN transport](https://github.com/scwlkr/balls/issues/37);
5. [#38 two-Node admission and shared membership](https://github.com/scwlkr/balls/issues/38);
6. [#39 one persistent Circle message](https://github.com/scwlkr/balls/issues/39);
7. [#34 milestone verification and acceptance](https://github.com/scwlkr/balls/issues/34).

#33 is the single ready frontier. Every later issue is dependency-blocked.
