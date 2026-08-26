# Run-and-gun Office Server v1 Checklist

Use `PASS`, `BLOCKED`, or `NOT RUN`. Record exact package, machine, Revit, and network identities.

## Before hardware purchase — virtual proof

- [ ] Windows Server 2022 passes Balls host readiness.
- [ ] Exact Development package installs without changing Windows protection.
- [ ] `ballsd` runs as a restricted Windows Service and survives reboot.
- [ ] Office Circle remains usable while the Owner laptop is offline.
- [ ] Exported Circle recovery material restores into a disposable replacement Node.
- [ ] One `Office Circle Files` Explorer location opens from two approved clients.
- [ ] Every Member sees and edits the Shared area.
- [ ] Only an authorized test Member sees and edits the Restricted area.
- [ ] Directly addressing the Restricted path as another Member returns Access Denied.
- [ ] Stopping `ballsd` does not interrupt already provisioned ordinary SMB work.
- [ ] Removing the test Member stops future file and Tailscale authorization without deleting files.
- [ ] An unapproved Node receives no office Capability.
- [ ] Tailscale remote access uses the canonical MagicDNS Host identity.
- [ ] Revit Server 2027 Host+Admin health checks pass.
- [ ] All approved Office Circle Members can reach the Revit model service.
- [ ] Only the Server Administrator can reach the Revit administration interface.
- [ ] Two Revit clients complete representative local/remote create-local and synchronize behavior.
- [ ] The remote result records direct or relayed Tailscale behavior and observed usability.
- [ ] No Revit Server Accelerator is installed or configured.
- [ ] No `D:\RevitServer\2027` path is exposed by SMB or Circle Files.
- [ ] One lock, stage, unlock, backup, and restored-model test passes.
- [ ] Balls state and provider ownership restore without granting unintended access.
- [ ] External inspection finds no public SMB, Revit, RDP, PowerShell, or Balls listener.

## After hardware arrives — physical proof

- [ ] Windows Server 2022 drivers are present for chipset, Ethernet, storage, USB, and UPS.
- [ ] The RAID volume appears as the intended fixed NTFS data disk across reboot.
- [ ] RAID degradation is visible without data loss.
- [ ] DAS disconnect/reconnect behavior is documented and does not silently redirect paths.
- [ ] UPS loss produces a clean shutdown of the server and storage.
- [ ] The exact physical server and two employee-class clients repeat the file proof.
- [ ] The exact physical server and remote client repeat the Revit proof.
- [ ] Ordinary-file, Revit, and Balls-state restoration repeats from the selected backup media.

## Result

- Package/tag/commit:
- Server identity and Windows build:
- Revit Server identity/version:
- Client identities:
- Tailscale path result:
- File result:
- Revit result:
- Restore result:
- Public-exposure result:
- Overall: `PASS | BLOCKED | NOT RUN`
- Blocker or limitation:
