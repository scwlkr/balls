# ADR 0010 — Public Development Download Channel

- **Status:** Accepted
- **Date:** 2026-08-26

Balls package testing must begin at [`balls.wlkrlabs.com`](https://balls.wlkrlabs.com), including
before a candidate is good enough for Alpha. Add a public **Development** channel beneath the
recommended Alpha and previous-version sections. Development may publish incomplete or broken
builds from an identified issue branch or `main` commit, but every build remains an immutable
GitHub prerelease with exact tag, commit, package identity, and SHA-256 metadata. Canary remains
short-lived CI evidence; Development is the durable public testing lane; Alpha remains the
Owner-accepted prerelease lane.

An agent working an active issue may create a Development tag and GitHub prerelease and move the
Development pointer after build and package-integrity checks pass, without separate approval for
each build. The agent records the previous pointer for rollback. Alpha, Beta, and Stable promotion
remain Owner-gated. Promotion reuses the exact green-`main` assets already rehearsed through
Development; it never rebuilds them.

This trades a noisier public release history and explicitly unreliable Development packages for a
single honest user distribution path. Functional failure is allowed in Development. Corrupt or
ambiguous identity, mutable assets, secret inclusion, and execution- or application-policy bypass
are not.
