# Two-machine development

The Mac and Windows laptops are peers, not a shared disk. GitHub remains the source of truth for
source, branches, pull requests, reviews, and CI. Tailscale supplies the private device network;
SSH supplies interactive terminals, file transfer for short-lived test artifacts, and editor
connections.

No passwords, private keys, or repository files cross through GitHub Issues. Do not place a Git
working tree in iCloud, OneDrive, SMB, or another synchronized/network filesystem. Each laptop
keeps its own clone and worktrees, then exchanges commits through GitHub.

## Security boundary

- The sandboxed Tailscale macOS app cannot host Tailscale SSH. The Mac therefore runs one dedicated
  native `sshd` bound only to its Tailscale IPv4 address. Native macOS Remote Login remains off, so
  port 22 is not opened generally on the LAN.
- Windows uses the native OpenSSH Server because Tailscale SSH does not provide a Windows server.
  Its broad default firewall rule is disabled and replaced by one rule limited to the Tailscale
  interface and `100.64.0.0/10`.
- Windows initially permits password authentication only for the first connection check. After a
  successful key-authenticated Mac-to-Windows connection, `Finalize` disables password and
  keyboard-interactive authentication.
- The shared client key lives in the 1Password SSH agent. Only its public key is written to the
  Windows OpenSSH authorization file. Private key material is never exported to these scripts.
- The bootstrap defaults to read-only `Inspect`. Every persistent system change requires an
  explicit confirmation flag. `Disable` stops the SSH entry point but deliberately preserves the
  authorized public key and Tailscale installation for recovery.

## One-time bootstrap

1. Install Tailscale and 1Password on both laptops. Sign both Tailscale apps into the same tailnet.
2. In 1Password, create one Ed25519 SSH Key item for the development link. Enable the 1Password SSH
   agent on both laptops and expose that key on each. Give it the same unique comment on both.
3. Review the scripts before changing either machine:

   ```bash
   ./eng/remote/Initialize-BallsDevLink.sh inspect
   ```

   ```powershell
   .\eng\remote\Initialize-BallsDevLink.ps1 -Action Inspect
   ```

4. On the Mac, install the key-only SSH daemon bound to its current Tailscale address:

   ```bash
   ./eng/remote/Initialize-BallsDevLink.sh configure --confirm-system-change
   ```

5. In an elevated Windows PowerShell from its local Balls clone, install/configure OpenSSH. The
   default key item is `Balls Dev Link`; pass `-KeyItem` only if its title differs:

   ```powershell
   .\eng\remote\Initialize-BallsDevLink.ps1 -Action Configure -ConfirmSystemChange
   ```

6. From the Mac, connect to the Windows Tailscale DNS name and prove that 1Password authorizes the
   key. Then, from that proven Windows session, make Windows key-only:

   ```powershell
   .\eng\remote\Initialize-BallsDevLink.ps1 -Action Finalize -ConfirmSystemChange
   ```

7. Add stable SSH aliases locally. Keep machine-specific usernames and hostnames outside the public
   repository. VS Code Remote SSH and ordinary `ssh`, `scp`, and `rsync` can use those aliases.

Keep the tailnet device-access rule scoped to the owner's devices; do not add a public or wildcard
source solely for convenience.

## Daily loop

Start work by fetching GitHub and creating an issue worktree on the owning laptop. Push useful
commits early. Use SSH only to operate the other laptop, inspect platform behavior, or move bounded
ephemeral proof artifacts. The remote command should create its own issue worktree instead of
switching the other laptop's primary checkout.

For the physical Mac-to-Windows Trusted Circle risk gate, Windows owns the Anchor/listener and the
Mac owns the joining Node. Exchange only the bounded invitation and sanitized evidence over the
private SSH link; keep each Node's protected state on its native local disk. Record exact commit,
Tailscale addresses, distinct Node identities, join/message/restart observations, and cleanup in a
dated verification document.

## Disable access

Run the matching confirmed `Disable` action locally on either laptop. The Mac action persistently
unloads the dedicated Tailscale-bound daemon. The Windows action stops/disables `sshd` and disables
the Balls-specific firewall rule. Neither action signs out of Tailscale or removes 1Password data.
