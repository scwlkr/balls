# Cross-platform Node and UI evidence

**Observed:** 2026-08-20  
**Issue:** [#22](https://github.com/scwlkr/balls/issues/22)  
**Pull request:** [#32](https://github.com/scwlkr/balls/pull/32)  
**Exact Canary source:** `2261eb7a4fb582e261a591383dd9d76acc13d01a`

## Outcome

The same packaged daemon, structured CLI, Circle behavior, and local React workspace passed on a
physical Windows host and a dedicated Ubuntu 24.04 Hyper-V VM. Both platforms retained Node and
Circle identifiers across daemon restart, rendered the workspace in real Chrome, and exposed only
same-user local control plus loopback browser listeners.

## Automated gates

- Local `dotnet run --project eng/Balls.Verify --configuration Release -- full` passed: locked
  restore, format/generated-client checks, zero-warning Release build, 97 .NET tests passed with
  10 platform-appropriate skips, 4 React component tests, and 1 Playwright Chromium journey.
- Exact branch dispatch [run 32383803019](https://github.com/scwlkr/balls/actions/runs/32383803019)
  passed: Ubuntu fast `96473211911`, Windows fast `96473212259`, Required `96474394713`, Windows
  Canary `96474433664`, and Linux Canary `96474433723`.
- Each Canary job built its package once, installed from fresh state, verified both checksum
  layers, exercised structured status and Circle create/list, rendered the real browser UI,
  restarted `ballsd`, rechecked stable identifiers, and uploaded only after the smoke passed.

## Exact retained artifacts

| Platform | Workflow artifact | Artifact ID | Downloaded archive bytes | Downloaded archive SHA-256 | Expires |
| --- | --- | ---: | ---: | --- | --- |
| Windows x64 | `balls-0.1.0-alpha.2-canary-windows-x64-2261eb7a4fb5` | `9412292233` | 18,450,549 | `BAEE71511891497EF3E0B36CF8E6658D6F83C0848E793272CD426833A4516AC2` | 2026-09-03 15:09 UTC |
| Linux x64 | `balls-0.1.0-alpha.2-canary-linux-x64-2261eb7a4fb5` | `9412247099` | 18,370,166 | `28A5352C6AE00F9AB7E8666F51A93787CED9E7AB6BC52EA63BC09BB3B4CE127F` | 2026-09-03 15:07 UTC |

Artifact names include the exact 12-character commit prefix, retention is 14 days, and this
Canary flow created neither a Git tag nor a GitHub Release.

## Independent downloaded-artifact observations

On the Windows host, the downloaded Windows artifact passed
`Test-WindowsCanary.ps1` from fresh temporary state. The checksum-verifying installer started Node
`01a01fb8-9bdc-771b-b8d9-66937522480e`; the smoke observed the expected Circle, Owner, and Node in
Chrome before and after restart and rejected any non-loopback listener.

The namespaced lab used only VM `Balls.Lab.Ubuntu`, checkpoint `Balls.Lab.Clean`, files below
`C:\BallsLab`, and the existing Hyper-V `Default Switch`. The verified Canonical source image SHA-256
was `6e40c07ae715f744f84af0bec76415cc1987dd115b4b8de437818561f01a3733`. The dynamic OS disk was
16 GB, automatic checkpoints were disabled, and unrelated VMs and switches were untouched.

Inside that Ubuntu VM, the downloaded Linux artifact passed `Test-LinuxCanary.sh` with Google
Chrome 151.0.7922.169. Fresh install, CLI/Circle, browser, loopback, cleanup, and restart assertions
passed for smoke Node `01a01fb9-bc42-72d9-b317-b8b3a5912f16`. The packaged default one-command
installer then created persistent Node `01a01fba-018d-7638-8489-111a1871930a` and Circle
`01a01fba-041e-798a-9ecf-a02c304ad3bf`; the lab reported the Balls identity as enrolled.

Restoring the explicitly gated clean checkpoint removed the post-checkpoint install and identity.
The VM returned with unchanged OS machine ID `498d59af31b04f34bb19722beab972aa`, Balls identity clean,
automatic checkpoints disabled, and only `Balls.Lab.Clean` available as the identity boundary.

## Evidence boundary

- Physical Windows host: observed.
- Dedicated Ubuntu Hyper-V VM: observed.
- Physical Linux hardware, macOS, service installation, signing, remote Circle transport, and VM
  publication: unverified and outside this issue.
- These are development Canaries, not stable installers or support claims.

All #22 acceptance checks are satisfied. Milestone acceptance and any Alpha publication remain
the separate owner-gated scope of #23.
