# Assisted two-laptop LAN Circle Files pilot evidence

**Observed:** 2026-08-25

**Artifact commit:** `67974f2de6502d99a55378e9da5aabf5e4293cc7`

**Runtime artifact:** `balls-0.3.0-alpha.1-canary-windows-x64-67974f2de650.zip`

**Runtime SHA-256:** `96e742abcf1a35efb5722d54dc88dc26471cafdeb501672997de49e5749613b5`

This file records a completed assisted pilot. It is not a current installation or distribution
runbook. Balls software and updates now come only from an owner-accepted GitHub Release through
`balls.wlkrlabs.com`; a private Circle invitation is transferred separately.

## Result

`PASS — two-laptop Circle Files core workflow`, with the following exact boundary:

- The host was the owner Windows Node running in a VM on the Linux laptop. The joining Node was a
  separate physical Windows boss laptop on the same private LAN.
- Both machines ran the checksum-verified artifact identified above, joined the same Circle, and
  authenticated one read/write grant over the protected synchronization endpoint.
- Balls mapped `P:` on the boss laptop to the dedicated `C:\BallsPilotData\Shared` host folder.
- The host share required SMB encryption, the server required signing, SMB1 was disabled, and the
  observed session negotiated SMB 3.1.1.
- Both Nodes observed create, read, edit, rename, and delete operations in both directions. All
  disposable smoke files were removed.
- The repository launcher fix cold-started an isolated boss Node through the real Explorer-shell
  path. The isolated daemon and state were then removed.
- The consumed invitation was removed. The host's temporary `CurrentUser` execution-policy change
  was independently verified restored to `Undefined`, with effective policy `Restricted`.

No invitation, provider credential, password, or private key is retained in this record.

## Limits

This run did not exercise Revit, revocation, backup/recovery, a host reboot, or the browser's
guided mapping-button click. The real mapping was applied through the Balls CLI, which calls the
same local application behavior. The joining account used a limited interactive token but may
still have been administrator-capable.

These omissions did not satisfy the former `0.4.0-alpha.1` release-acceptance matrix. Issue
[#62](https://github.com/scwlkr/balls/issues/62) was closed as superseded without a release on
2026-08-26. The current Private Boss Demo specification separately requires exact accepted
Release-candidate artifacts, a genuinely nonadministrator browser-guided mapping, honest evidence,
and explicit Owner approval before publication. This record remains historical evidence only.
