# Cross-platform Node and UI lab

This lab proves the same packaged Balls Node, structured CLI, and local browser workspace on the
Windows host and one dedicated Ubuntu 24.04 Hyper-V guest. The harness owns only:

- VM `Balls.Lab.Ubuntu`;
- checkpoint `Balls.Lab.Clean`;
- files below `C:\BallsLab`.

It uses the existing Hyper-V `Default Switch` without creating, changing, or deleting shared
switches. It refuses to adopt a same-named VM whose configuration or disks are outside its owned
root. It never touches other Hyper-V VMs or WSL distributions.

## Prerequisites

- Windows with WSL2, Hyper-V, PowerShell 7, OpenSSH, and the Hyper-V PowerShell module.
- The current account is an Administrator or a member of `Hyper-V Administrators`.
- The Hyper-V `Default Switch` exists.
- At least 50 GB free below the dedicated lab root.
- `qemu-img` is available inside WSL. The harness does not install or alter WSL packages. Pass
  its absolute WSL path with `-QemuImgWslPath`; use `-QemuLibraryWslPath` for a portable,
  lab-local build whose shared libraries are not on the distro's default path.

Inspect prerequisites and the exact ownership boundary without changing state:

```powershell
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action Inspect
```

## Prepare the verified image

Use the dated Canonical image and its published SHA-256. `PrepareImage` downloads only over HTTPS,
verifies the full digest, invokes WSL `qemu-img` to create a dynamic VHDX below `C:\BallsLab`,
and records the exact source identity in `lab-state.json`.

```powershell
$image = 'https://cloud-images.ubuntu.com/releases/noble/release-20260814/ubuntu-24.04-server-cloudimg-amd64.img'
$sha256 = '6e40c07ae715f744f84af0bec76415cc1987dd115b4b8de437818561f01a3733'

pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action PrepareImage `
  -ImageUri $image `
  -ImageSha256 $sha256 `
  -QemuImgWslPath /usr/bin/qemu-img
```

For a portable converter stored in the lab root, add both explicit paths:

```powershell
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action PrepareImage `
  -ImageUri $image `
  -ImageSha256 $sha256 `
  -QemuImgWslPath /mnt/c/BallsLab/Tools/qemu/root/usr/bin/qemu-img `
  -QemuLibraryWslPath /mnt/c/BallsLab/Tools/qemu/root/usr/lib/x86_64-linux-gnu
```

## Create, checkpoint, and inspect identity

```powershell
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action Create
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action Identity
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action Checkpoint
```

`Create` is idempotent. It creates a Generation 2 VM with Secure Boot disabled, fixed 4 GB
memory, one copied OS disk, and one generated `CIDATA` ISO. Cloud-init creates only the
`balls-lab` guest, authorizes only the dedicated lab SSH key, and writes the lab marker. The
action does not succeed until SSH, the marker, and all three clean-identity checks pass:

- no `$HOME/.local/state/balls`;
- no Balls control socket;
- no running `ballsd`.

`Checkpoint` repeats those checks before creating `Balls.Lab.Clean`. It is idempotent when that
checkpoint already exists.

## Identity rule and reset

Never clone or export the registered VM, its active disk, or a post-enrollment checkpoint. The
only reusable boundary is the verified pre-enrollment checkpoint. Immediately before an
enrollment proof, restore it and recheck identity:

```powershell
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action Reset -ConfirmReset
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action Identity
```

`Reset` is intentionally gated because it discards every post-checkpoint guest change. It restores
the clean OS identity, clears the dedicated SSH host record, waits for the guest, and fails unless
the Balls state/socket/process boundary is empty. The next packaged `ballsd` start therefore
generates a fresh Balls Node identity instead of cloning an enrolled Node.

## Package proof

Use only a checksummed Canary artifact for the exact commit under test. The Linux artifact includes
`Install-BallsCanary.sh`; its one-command installer verifies the outer checksum, rejects unsafe
archive paths, verifies every internal checksum, installs into protected version/state roots, and
waits for the packaged daemon.

```bash
bash ./Install-BallsCanary.sh \
  ./balls-*-canary-linux-x64-*.zip \
  ./balls-*-canary-linux-x64-*.zip.sha256
```

The committed Linux and Windows smoke scripts then prove fresh install, structured status and
Circle create/list, a real Chromium-rendered `balls ui`, loopback-only listeners, daemon restart,
stable Node/Circle identifiers, and socket cleanup. The Linux guest needs Chrome or Chromium for
that UI smoke; set `BALLS_CHROME` to an explicit executable when it is not on `PATH`.

## Cleanup

Cleanup is permanent and is never implicit:

```powershell
pwsh -File .\eng\lab\Invoke-BallsLab.ps1 -Action Cleanup -ConfirmCleanup
```

The action revalidates ownership, removes only `Balls.Lab.Ubuntu`, and then removes only the exact
resolved lab root. It does not remove or modify the Default Switch, other VMs, or any WSL distro.
