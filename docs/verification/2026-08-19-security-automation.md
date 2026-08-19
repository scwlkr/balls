# Public Security Automation Evidence — 2026-08-19

## Scope

Issue [#7](https://github.com/scwlkr/balls/issues/7) adds the no-spend public-repository baseline
for vulnerable dependency changes, unsafe C# changes, and insecure workflow changes. It adds no
persistent runner, repository secret, signing, attestation, or release requirement.

## Checked-in controls

- `Dependency review` runs on pull requests and fails at moderate severity.
- `CodeQL C#` uses advanced C# analysis on pull requests, `main`, and weekly.
- `OpenSSF Scorecard` runs on `main`, branch-rule changes, and weekly; it is not required by the
  `main` ruleset.
- Dependabot retains weekly NuGet and GitHub Actions updates.
- All actions use full 40-character commit SHAs and fixed Ubuntu 24.04 runners.
- One repository test rejects missing security workflows, floating runner labels, non-SHA actions,
  `pull_request_target`, `workflow_run`, `secrets.*`, and `self-hosted` workflow paths.

## Repository readback before merge

| Control | Observed value |
| --- | --- |
| Visibility | `public` |
| Dependency graph | 18 manifests through GraphQL |
| Dependabot security updates | `enabled` |
| Private vulnerability reporting | enabled endpoint returned HTTP 200 |
| Default workflow token | `read` |
| Workflow PR approvals | disabled |
| Fork approval | first-time contributors |
| Allowed actions | GitHub-owned plus exact pinned OpenSSF Scorecard commit |
| Repository SHA-pin enforcement | enabled |
| CodeQL default setup | `not-configured`; checked-in advanced setup is authoritative |

All required issue #7 controls are available for this public repository. Scorecard results and its
OIDC publication are public by design. CodeQL and Scorecard receive job-local security-result write
permissions; no fork-triggered workflow references secrets or persistent infrastructure.

## Hosted evidence

Implementation pull request [#15](https://github.com/scwlkr/balls/pull/15) passed dependency review,
C# CodeQL, and the Windows/Ubuntu required CI gate. Accepted commit
`1d23741d786ab1332ef085de1d09b6aada634241` then passed
[CodeQL run 32300400729](https://github.com/scwlkr/balls/actions/runs/32300400729) in 2m07s and
[Scorecard run 32300400835](https://github.com/scwlkr/balls/actions/runs/32300400835) in 50s.
CodeQL uploaded one C# analysis with no open CodeQL alerts.

The first Scorecard result correctly exposed the Canary workflow's `workflow_run` checkout as a
critical pattern that static analysis could not prove safe, despite its accepted-main condition and
read-only credentials. Canary publication therefore moved behind the successful `Required` job in
the same trusted main-push workflow. Pull requests now contain no privileged follow-on checkout,
and the repository test rejects reintroducing `workflow_run` anywhere under `.github/workflows`.
