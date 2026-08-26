# Private Boss Demo v1 Manual Acceptance Checklist

Use this checklist for the decisive issue #92 rehearsal. The executable product contract is
[`../specs/private-boss-demo-v1.md`](../specs/private-boss-demo-v1.md), and the lab safety boundary
is [`../windows-development-lab.md`](../windows-development-lab.md).

The Owner performs both roles. A pass is **same-host two-VM** evidence. It is not physical-device,
physical-LAN, or actual-boss usability evidence.

## Result header

- Date and start time:
- Date and finish time:
- Result: `PASS`, `WARNING`, or `BLOCKED`
- Repository commit:
- Release tag:
- Package filename and internal identity:
- Package SHA-256:
- Development manifest identity:
- Owner Windows version and profile privilege:
- Member Windows version and profile privilege:
- Network: `windows_default` private bridge
- Every prompt, elevation, retry, explanation, or manual intervention:
- Known limitations:

Never record a private invitation, password, key, provider credential, signed-in application
state, or other secret.

## 1. Prove the lab boundary

- [ ] The working Windows 11 Owner environment is the selected Owner Node.
- [ ] `balls-issue61-provider-desktop` is the only running disposable Windows guest.
- [ ] Every other historical Windows and GPU VM is stopped.
- [ ] Host memory is recorded and monitored while both selected guests run.
- [ ] Neither selected VM's CPU, memory, disk, GPU, checkpoint, or network configuration changed.
- [ ] Both selected guests are attached to `windows_default`.
- [ ] Circle traffic has no Tailscale dependency, public exposure, or host-forwarded SMB path.
- [ ] Host access to each interactive desktop remains loopback-only.

## 2. Prove clean and unrelated state

- [ ] The Owner uses a dedicated test profile and starts Balls unelevated.
- [ ] The Member uses a dedicated profile that is not in local Administrators.
- [ ] The Member profile has no prior Balls state, Node identity, Balls-managed credential, or
      Balls-created drive mapping.
- [ ] Existing unrelated Owner and Member mappings are recorded without exposing credentials.
- [ ] `C:\BallsDemo\Projects` exists locally on the Owner before Balls starts.
- [ ] The demo folder contains `before-balls.txt`; its bytes and SHA-256 are recorded.
- [ ] The demo folder is not host-mounted, mapped, or otherwise network-backed.

## 3. Prove website installation

- [ ] The live website shows the accepted Alpha first.
- [ ] The Development section appears beneath it with a clear may-not-work warning.
- [ ] The candidate appears in Development or its immutable previous-version row.
- [ ] Both profiles copy the Windows command from the website; no package is copied between VMs.
- [ ] The command runs in the PowerShell included with Windows without PowerShell 7, .NET, or
      development tools.
- [ ] The bootstrap downloads before execution and requests no policy bypass.
- [ ] Both installations report the same tag, commit, package identity, and SHA-256.
- [ ] Both installations remain current-user scoped, create a normal Balls shortcut, and open
      Balls automatically.
- [ ] Normal shortcut launch starts the loopback UI and required private-network listeners without
      flags, addresses, ports, or separate service setup.

## 4. Prove the Owner contribution and invitation

- [ ] The Owner creates a new Circle through the browser.
- [ ] The Owner selects `C:\BallsDemo\Projects` through the real Windows folder picker.
- [ ] Balls summarizes the exact folder and access before mutation.
- [ ] Only the narrow host-side operation crosses the Windows consent boundary.
- [ ] The ordinary Balls process remains unelevated.
- [ ] The Owner contributes the exact existing folder without changing `before-balls.txt`.
- [ ] The Owner obtains one private invitation without revealing it in captured evidence.

## 5. Prove the join, grant, and Member journey

- [ ] The Member pastes only the private invitation and human display name.
- [ ] The browser shows clear Circle and Member join success.
- [ ] The Owner clicks `Refresh members`, selects the joined human Member and `Read/write`, reviews
      the access summary, and shares the Capability without entering an internal ID, SMB account,
      password, Access Grant, plan, address, or port.
- [ ] The Member clicks `Check again` and the approved folder appears as a Circle Capability without
      provider terminology.
- [ ] Closing and reopening Balls preserves the joined Circle and guided Capability.
- [ ] The Member sees one `Open shared folder in Explorer` action and no drive selector or mapping
      plan.
- [ ] Balls uses `P:` when available or another free supported letter without touching unrelated
      mappings.
- [ ] File Explorer opens visibly at the approved mapped root in the Member's interactive session.
- [ ] The Member sees no administrator prompt.

## 6. Prove one shared folder

- [ ] The Member opens and reads `before-balls.txt` through Explorer.
- [ ] The Member creates a disposable ordinary file; the Owner observes the same file and bytes.
- [ ] The Owner edits that file; the Member observes the same edit.
- [ ] The Member renames the file; the Owner observes the new name.
- [ ] The Member deletes the disposable file; the Owner observes its removal.
- [ ] `before-balls.txt` retains its original name, bytes, and SHA-256.
- [ ] No unrelated mapping, share, account, firewall rule, credential, or file changed.

## 7. Decide the result

`PASS` requires every product step above through the graphical journey. Product CLI setup, copied
binaries, manual IP or port entry, manual provider credentials, manual drive selection, an
administrator-capable Member, or Explorer opening the wrong location fails the journey.

Record elapsed wall time, download time, and every intervention. Five minutes is a product target,
not an automated or hard pass threshold. A Windows application-control block is `BLOCKED`; never
bypass it.

After a green-`main` Development package passes, pause for Owner approval. Alpha promotion moves
only to the identical tested assets. Then repeat live Alpha website identity, Windows installation,
shortcut, and startup readback; the complete Circle journey does not repeat when the bytes are
identical.

Preserve the evidence and lab-owned state until the result is recorded. Cleanup is a separate,
ownership-proven operation and must preserve the contributed folder and unrelated Windows state.
