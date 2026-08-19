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
  `pull_request_target`, `secrets.*`, and `self-hosted` workflow paths.

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

Pull-request and first `main` security-run links are recorded on issue #7 after GitHub executes the
new workflow files.
