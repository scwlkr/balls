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
