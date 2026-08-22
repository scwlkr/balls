# Windows Explorer Circle Files mapping verification

**Date:** 2026-08-22  
**Issue:** [#60](https://github.com/scwlkr/balls/issues/60)  
**Scope:** explicit unelevated current-user Windows mapping for one authorized SMB Access Grant

## Implemented boundary

- The user discovers available D-Z letters and explicitly selects one; Balls never auto-selects,
  replaces, or adopts a drive, credential target, or Explorer label.
- The daemon derives the exact share/account/marker identity from signed Circle state and loads the
  active DPAPI-protected per-grant secret. Password bytes do not enter local-control, CLI, browser
  bodies, browser storage, process arguments, logs, or evidence.
- Windows saves one domain-password credential for the exact numeric private endpoint, maps the
  exact UNC persistently with `CONNECT_UPDATE_PROFILE`, validates host and grant markers through
  SMB, and writes a friendly Circle label with an exact ownership value.
- Inspect and unmap compare exact drive/UNC, account, credential ownership comment, label, and
  ownership ID. Unmap uses `force=false`; mismatched or open resources are preserved.

## Automated evidence

| Gate | Observation |
| --- | --- |
| Windows mapping contract | 5 focused tests passed for discovery, collision refusal, bounded offline preflight, exact map/retry, wrong-share rollback, redaction, repurposed-resource refusal, and exact unmap |
| Protected storage | 7 Circle Files state-store tests passed, including active credential readback through the configured current-user protector and restart stability |
| Daemon/local-control | 4 focused endpoint/OpenAPI tests passed; preview/map/inspect/unmap used the shared application and returned no secret/password fields |
| Browser | 10 component tests passed, including discovery before explicit selection and map through the browser API; TypeScript and ESLint passed |

## Windows VM evidence

Pending the risk-triggered client VM run. This record will be updated with the exact commit,
collision/offline/wrong-share/restart/unmap observations, and final cleanup state before merge.

## Boundaries

This slice does not share credentials between Members, discover peers, change network profiles,
enable SMB/firewall policy, choose a drive automatically, overwrite existing mappings, add shell
extensions/offline files/sync, or rotate/revoke credentials. Physical-host network policy is not
part of this verification.
