# Canary Artifact Evidence — 2026-08-19

## Authorization and boundary

The owner explicitly authorized issue [#6](https://github.com/scwlkr/balls/issues/6) public Canary
artifact publication. This scope creates bounded GitHub Actions artifacts only. It does not create
a GitHub Release, product tag, stable installer, signing claim, or Linux runtime-support claim.

## Executable package contract

Five package tests verify:

- deterministic names containing product version, platform, architecture, and 12 commit characters;
- full 40-character commit identity in `canary.json`;
- required `balls` and `ballsd` output trees;
- an external archive SHA-256 and internal per-file `SHA256SUMS`;
- Windows runnable metadata and installer inclusion;
- Linux `runtimeSupported: false` plus the `0.2.0-alpha.1` unsupported statement;
- rejection of malformed commit identities and command inputs.

## Local Windows observation

A locally built archive named
`balls-0.1.0-alpha.1-canary-windows-x64-2eb16113aa46.zip` passed the committed Windows smoke script.
The installer verified both checksum layers, extracted into a fresh dedicated temporary install
root, started `ballsd` with fresh state, and observed `balls status` returning Node identity and
control protocol v1. The script then stopped the daemon and removed its temporary state.

An ad-hoc repository-local install root was separately rejected by `ballsd` with startup exit code
4, preserving the existing fail-closed state-directory boundary.

## Hosted publication evidence

Implementation pull request [#14](https://github.com/scwlkr/balls/pull/14) passed required
[CI run 32298686664](https://github.com/scwlkr/balls/actions/runs/32298686664): Ubuntu fast in
1m09s, Windows fast in 1m58s, and the aggregate `Required` decision in 3s.

The first hosted [Canary run 32299340551](https://github.com/scwlkr/balls/actions/runs/32299340551)
passed for accepted commit `930e68a89538289e5e6a16af3dbb18e2d66dfa2f`. Windows completed in
1m19s and Linux in 29s. GitHub retained these public artifacts through September 2, 2026:

- `balls-0.1.0-alpha.1-canary-windows-x64-930e68a89538`, artifact `9382437085`,
  18,086,786 bytes;
- `balls-0.1.0-alpha.1-canary-linux-x64-930e68a89538`, artifact `9382409808`,
  17,997,661 bytes.

The downloaded Windows outer checksum passed and the packaged installer revalidated internal
checksums, installed into fresh temporary state, started `ballsd`, and observed CLI status. The
downloaded Linux outer checksum passed; its manifest named the exact commit and reported
`runtimeSupported: false`, and its README retained the unsupported-until-`0.2.0-alpha.1` statement.
Issue [#6](https://github.com/scwlkr/balls/issues/6#issuecomment-5347718965) records the hosted
artifact URLs and readback.
