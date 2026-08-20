# Security policy

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Use
[GitHub private vulnerability reporting](https://github.com/scwlkr/balls/security/advisories/new).

Use synthetic data. Do not include credentials, private keys, tokens, private host or network
identifiers, real Circle state, or unsanitized diagnostics in a report.

## Supported versions

Balls is pre-release software and has no supported production version yet. Security fixes target
the latest commit on `main`. Canary, Alpha, Beta, and Stable support meanings are defined in the
[`development and release process`](docs/development-process.md); the supported-version table will
be expanded when the first supported Stable release exists.

The local control interface is not the future remote Node-to-Node security model. A separate
threat-model review is required before remote admission or transport ships; see
[`docs/security/threat-model.md`](docs/security/threat-model.md).

The local browser UI is served by `ballsd` on an authenticated loopback-only boundary. It must not
be proxied or rebound for LAN access. Launch capabilities and session material are credentials:
do not paste them into reports, logs, screenshots, or issue comments. Run `balls ui` again instead
of retaining a launch URL.

## Public repository automation

The public repository applies these automated checks:

- dependency review on pull requests, failing for newly introduced moderate-or-higher known
  vulnerabilities;
- C# CodeQL on pull requests, `main` pushes, and a weekly schedule;
- OpenSSF Scorecard on `main` pushes, branch-protection changes, and a weekly schedule;
- weekly Dependabot updates for NuGet and GitHub Actions plus enabled security updates;
- repository-level full-SHA enforcement for actions, backed by a pull-request policy test.

Security workflows use fixed GitHub-hosted runners and reference no secrets or persistent
self-hosted infrastructure. Fork contributions use `pull_request`, never `pull_request_target`;
no privileged `workflow_run` checkout follows untrusted code;
the repository default token permission is read-only, cannot approve pull requests, and requires
approval for first-time fork contributors. Write scopes exist only on the CodeQL and Scorecard jobs
that upload security results. Scorecard is advisory and is not a required release or merge check.

All controls required by issue #7 are available on the current public-repository plan. This
baseline does not claim binary signing, artifact attestation, or private self-hosted analysis.
Published Alpha binaries are unsigned and may be rejected by managed Windows Application Control;
do not weaken an organization or machine security policy to run them.
