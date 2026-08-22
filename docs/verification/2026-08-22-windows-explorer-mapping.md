# Windows Explorer Circle Files mapping verification

**Date:** 2026-08-22  
**Issue:** [#60](https://github.com/scwlkr/balls/issues/60)  
**Scope:** explicit unelevated current-user Windows mapping for one authorized SMB Access Grant

## Implemented boundary

- The user discovers available D-Z letters and explicitly selects one; Balls never auto-selects,
  replaces, or adopts a drive, credential target, or Explorer label.
- The daemon derives the exact share/account/marker identity from signed Circle state and loads the
  active credential binding without decryption for preview/inspect/unmap. Only map/reconnect loads
  the DPAPI-protected per-grant secret. Password bytes do not enter local-control, CLI, browser
  bodies, browser storage, process arguments, logs, or evidence.
- Windows saves one domain-password credential for the exact numeric private endpoint, maps the
  exact UNC persistently with `CONNECT_UPDATE_PROFILE`, verifies authenticated directory access
  and both exact protected marker names through SMB, and writes a friendly Circle label with an
  exact ownership value. Marker contents remain hidden from the limited account.
- Inspect and unmap compare exact drive/UNC, account, credential ownership comment, label, and
  ownership ID. A persisted but disconnected drive reports `partial`; an idempotent map retry
  reconnects it with the protected grant secret. Unmap uses `force=false`; mismatched or open
  resources and unrelated Explorer registry values are preserved. Incomplete exact rollback is a
  typed conflict that retains an ownership witness for safe retry.

## Automated evidence

| Gate | Observation |
| --- | --- |
| Windows mapping contract | 10 focused tests passed for discovery, different-UNC and same-UNC foreign collision refusal, bounded offline preflight, exact map/retry/reconnect, restart-partial label recovery, NUL-terminated native password handling, typed incomplete rollback, wrong-share rollback, redaction, repurposed-resource refusal, and exact unmap |
| Protected storage | 7 Circle Files state-store tests passed, including active credential readback through the configured current-user protector and restart stability |
| Daemon/local-control | 4 focused endpoint/OpenAPI tests passed; preview/map/inspect/unmap used the shared application and returned no secret/password fields |
| Browser | 10 component tests passed, including discovery before explicit selection and map through the browser API; TypeScript and ESLint passed |
| Full repository verifier | Passed formatting, generated-client drift, lint, typecheck, zero-warning Release build, 286 automated tests, 10 browser component tests, and the real Playwright restart journey; 20 host-inapplicable tests skipped |

## Windows VM evidence

The `Balls.Dev.Windows11` Windows 11 guest ran the exact clean commit
`ebf8b03a97f7adec2d31dba04a45ebbc046b7b0b`. No checkpoint was restored. The test temporarily set
the guest fixture network to Private and disabled 17 pre-existing broad Public/Any inbound SMB
allow rules; final cleanup restored the original Public profile and rules. The physical host's
network profile and firewall were not changed.

| Scenario | Observed result |
| --- | --- |
| Explicit mapping | Selected `M:` mapped `\\127.0.0.1\balls-01a02a23d0c3` as `Issue 60 Circle`; plan `8c85b9bdac19e828a93d8d37a431d7ed4c87a75f19575f6094ca9e5dafad43dd` and ownership `fd0acaebc2474290bf390e48386bd4b1fceeef3a8751f52d6f78e8df402fec37` remained stable |
| Collision | A temporary foreign `HKCU\Network\M` fixture pointing to the exact planned UNC returned `mapping_drive_collision` and was preserved until the test removed that exact fixture |
| Offline endpoint | `192.168.254.254` returned bounded `mapping_endpoint_unreachable` before mapping or credential mutation |
| Wrong share | Temporarily hiding the exact protected grant marker returned `mapping_share_identity_mismatch`; restoring the same marker allowed mapping |
| Authenticated I/O | The limited grant credential wrote, read, and deleted a probe through `M:`; the Explorer label and exact mapping ownership value were present |
| Restart | After a real guest reboot, inspect conservatively returned `partial`; map retry returned `already-mapped`, restored `M:`, and write/read/delete succeeded |
| Exact unmap | Returned `unmapped`; `M:` SMB mapping, `HKCU\Network\M`, the endpoint credential, and Balls label values were absent, while an unrelated pre-existing Explorer value remained exact before fixture cleanup |
| Redaction | CLI/public results for inspect, retry, and unmap contained no password or secret fields or values |
| Final cleanup | The dedicated lab root was absent; Balls shares, grant accounts, firewall rules, and SMB mappings were all zero; the guest profile was restored to Public |

This is a single-guest virtual client proof using loopback to exercise real Credential Manager,
MPR/SMB, Explorer registry, reboot, and cleanup behavior. It is not a physical two-PC LAN proof;
peer discovery and the two-Member operational journey remain #61 and #62 work.

## Boundaries

The product slice does not share credentials between Members, discover peers, change network profiles,
enable SMB/firewall policy, choose a drive automatically, overwrite existing mappings, add shell
extensions/offline files/sync, or rotate/revoke credentials. Physical-host network policy is not
part of this verification.
