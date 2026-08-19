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

The first hosted Canary can only run after this document is accepted onto `main`. Its workflow and
artifact URLs, downloaded Windows smoke observation, Linux unsupported-manifest readback, exact
names, sizes, and expiry are recorded on issue #6 after publication. This avoids creating a second
accepted commit solely to describe artifacts from the preceding accepted commit.
