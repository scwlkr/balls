# Open and Fast Foundation Milestone Evidence — 2026-08-19

## Status

`0.1.0-alpha.2` is a verified release candidate awaiting explicit owner acceptance. No tag or
GitHub Release has been created. The accepted publication target will be the exact protected
`main` commit and Canary artifacts recorded on issue
[#8](https://github.com/scwlkr/balls/issues/8) after this record lands.

## Completed outcomes

| Issue | Outcome | Evidence |
| --- | --- | --- |
| [#3](https://github.com/scwlkr/balls/issues/3) | sanitized public cutover | [readiness record](2026-08-19-public-readiness.md) |
| [#4](https://github.com/scwlkr/balls/issues/4) | focused, fast, and full verifier | [developer record](2026-08-19-developer-verification.md) |
| [#5](https://github.com/scwlkr/balls/issues/5) | protected dual-platform pull requests | [workflow record](2026-08-19-protected-pr-workflow.md) |
| [#6](https://github.com/scwlkr/balls/issues/6) | green-main Canary artifacts | [Canary record](2026-08-19-canary-artifacts.md) |
| [#7](https://github.com/scwlkr/balls/issues/7) | public security automation | [security record](2026-08-19-security-automation.md) |

All five implementation issues are closed on protected `main` with linked hosted evidence.

## Feedback budgets

Measured from a warm Windows checkout with .NET SDK `10.0.400`:

| Path | Observation | Budget | Result |
| --- | ---: | ---: | --- |
| Focused Core unit selection | 8 passed in 6.03s | `<15s` | pass |
| Complete local fast gate | 72-test repository, selected fast categories in 33.69s | `<60s` | pass |
| PR #16 Ubuntu fast | 1m01s | `<5m` | pass |
| PR #16 Windows fast | 2m08s | `<5m` | pass |

The pull-request aggregate passed in 4s. Dependency review passed in 4s and C# CodeQL in 2m02s;
both remain visible outside the required aggregate.

## Public repository state

- Canonical `scwlkr/balls` is public, unarchived, and uses `main` over the SSH remote.
- The owner-selected sanitized lineage, anonymous access, issue/milestone continuity, private
  vulnerability reporting, and active ruleset are recorded by #3.
- The authorized original private archive and rejected `scwlkr/balls-public-staging` repository
  were deleted after cutover verification and returned `404`; the current owner-repository list
  contains no replacement private Balls source or staging repository.
- The separately archived public `scwlkr/balls-server` prior-research repository is intentionally
  retained. The unrelated private-history repository for that retired project is not this
  repository's deleted source archive.
- Squash-only auto-merge and merged-branch deletion remain enabled. The default workflow token is
  read-only, action SHA enforcement is enabled, and the `Required` ruleset decision is active.
- There are zero product tags and zero GitHub Releases before owner acceptance.

## Exact artifact observation

[Main CI run 32301283888](https://github.com/scwlkr/balls/actions/runs/32301283888) proved the
structural publication order for commit `bdec8d8007901f7e0955202c3882484f11f9ab9d`: Ubuntu passed
in 52s, Windows in 2m05s, `Required` in 3s, Linux Canary in 35s, and Windows Canary in 1m12s.

The exact downloaded Windows artifact `9383194487` passed its outer SHA-256, every internal file
checksum, a fresh temporary install/state, daemon startup, and `balls status`; the smoke process
and temporary state were removed afterward. The exact downloaded Linux artifact `9383177334`
passed SHA-256, named the full accepted commit, reported `runtimeSupported: false`, and retained
`Runtime unsupported until 0.2.0-alpha.1` in its README. Both artifacts expire September 2, 2026.

This pre-version-bump observation proves the current packaging path. After the milestone version
record lands, issue #8 records and independently smokes the exact `0.1.0-alpha.2` artifacts that
are eligible for owner acceptance; no locally rebuilt substitute may be promoted.

## Security and quality

The first Scorecard analysis exposed two critical `workflow_run` checkout findings. Issue #7 was
reopened, Canary publication moved behind successful required CI in the same read-only main-push
workflow, and the second Scorecard run marked both findings fixed. C# CodeQL has no open CodeQL
alert. Remaining Scorecard recommendations are advisory maturity signals rather than observed
credential, data-loss, or product-startup blockers.

The candidate gate includes locked restore, format verification, a zero-warning Release build,
the six-category audit, and all 72 automated tests. Unsupported physical-machine, Linux runtime,
installer, remote-network, and browser scenarios are not claimed by this milestone.

## Next executable milestone

The `0.2.0-alpha.1 — Cross-platform Node and Web UI` milestone has seven executable issues:

1. [#17 — Compose daemon and CLI through cross-platform host seams](https://github.com/scwlkr/balls/issues/17)
2. [#18 — Run protected local state and control IPC natively on Linux](https://github.com/scwlkr/balls/issues/18)
3. [#19 — Add stable structured CLI output and dual-platform process acceptance](https://github.com/scwlkr/balls/issues/19)
4. [#20 — Create the typed React workspace and generated local API client](https://github.com/scwlkr/balls/issues/20)
5. [#21 — Serve a hardened local browser UI from ballsd](https://github.com/scwlkr/balls/issues/21)
6. [#22 — Prove the Windows and Ubuntu Node/UI outcome and publish runnable Canaries](https://github.com/scwlkr/balls/issues/22)
7. [#23 — Verify and accept the Cross-platform Node and Web UI milestone](https://github.com/scwlkr/balls/issues/23)

#17 is the first frontier after #8 is accepted. All seven remain blocked while the current
milestone and owner publication decision are open.

## Owner gate

Owner acceptance must explicitly authorize tagging and publishing `0.1.0-alpha.2`. Until then,
the milestone remains open, #8 remains open, #17 remains blocked, and no tag or Release is created.
