# Dedicated Circle folder helper verification

**Date:** 2026-08-21  
**Issue:** [#58](https://github.com/scwlkr/balls/issues/58)  
**Scope:** one previewable, idempotent, ownership-proven Windows hosting operation

## Implemented outcome

- Protected local control and the structured CLI preview one deterministic version-1 plan, then
  require its exact ID before apply. The daemon revalidates the current local Owner, persisted
  Member/root authorization proofs, readiness, and path on both calls.
- `ballsd` remains unelevated. Apply launches the adjacent `balls-windows-helper.exe` through UAC
  with only a random one-time pipe name and daemon PID. The daemon and helper verify each other's
  exact pipe process IDs before a bounded plan crosses the elevation boundary. The helper also
  requires the adjacent `ballsd.exe`, binds the Owner SID to its process token, and independently
  verifies the two P-256 signatures and exact signed Contribution fields. A separate administrator
  credential can elevate the helper without replacing the original Owner SID.
- The helper recomputes the whole plan under elevation and can perform only the typed folder ACL,
  marker/journal, SMB-share, and firewall operations. Its fixed PowerShell adapter accepts JSON on
  standard input, inherits no `PSModulePath`, has a 20-second timeout, and enforces a combined
  16,384-character streaming output budget.
- Paths must be absolute fixed-local locations that are new or empty. Roots, Windows/profile
  roots, network locations, files, existing reparse traversal, user content, and foreign
  markers/resources are refused at both privilege levels.
- Exact ownership is recorded in the folder marker and journal plus the share and firewall
  descriptions. The protected ACL grants full control only to the local Owner and LocalSystem;
  the share requires encryption and initially grants only the Owner; the inbound rule is limited
  to `Private`, TCP 445, `LocalSubnet`, and `LanmanServer`.
- Retry succeeds only for the complete exact state. Failure and partial recovery inspect every
  resource, roll back in reverse, remove only proven-owned state, restore an originally empty
  folder's prior ACL, and delete only the exact newly created target while it remains empty. A
  journal cannot claim a parent or ancestor directory.

The Contribution deliberately stays `defined`. Member accounts/credentials and grant ACLs are
#59; Explorer mapping is #60; lifecycle cleanup and revocation are #61.

## Automated evidence

Focused Windows helper contracts passed on the development host, including deterministic preview,
changed-plan refusal, hostile and nonempty/reparse/file paths, readiness gating, fixed command and
64 KiB helper protocol bounds, real Windows ACL ownership inspection, clean apply/retry, injected
reverse rollback, partial journal recovery, and pre-existing-resource collision. Core
authorization, daemon endpoint, CLI text/JSON, OpenAPI drift, generated client, architecture, and
cross-platform unsupported-adapter coverage are part of the repository fast/full gates recorded
on the pull request and issue.

## Dedicated Windows VM evidence

The owned `Balls.Dev.Windows11` Hyper-V guest ran the source-built helper from exact implementation
commit `7bf86c506591cfd18681c71ab988eb2e268657b6`. No checkpoint was restored. The guest repository
was clean at that commit, and the VM was stopped after post-run inspection.

The lab started with a Public connected profile and 17 pre-existing enabled Public/Any inbound
allow rules whose scope can include TCP 445. The machine-local test fixture snapshotted those exact
states, temporarily selected Private and disabled those exact rules, and restored all of them in a
`finally` path. This was confined to the disposable guest; the physical host was not changed.

Observed structured result:

| Check | Observation |
| --- | --- |
| Readiness after isolated guest fixture | `ready` |
| Debug injected failure | `hosting_apply_failed`; exact rollback clean |
| Hostile Windows path | `hosting_path_invalid` |
| Pre-existing share collision | `hosting_resource_collision` |
| Release clean apply | `applied` |
| Exact release retry | `already-applied` |
| Created folder | protected ACL with exactly Owner and LocalSystem SIDs |
| Created share | encryption required; exactly one Owner SID share grant |
| Created firewall rule | `Private`, `LocalSubnet`, TCP 445, `LanmanServer` |
| Guest fixture restoration | original Public profile and all 17 rule enabled states restored |
| Final cleanup | no `balls-*` share, no firewall rule in group `Balls`, and all six exact lab run directories absent |

The Debug fault hook is compiled only in Debug builds and accepts only the fixed
`PrivateFirewallRule` step. Release builds ignore the environment variable.

## Physical-host boundary

The owner identified this PC as the intended host: Windows 11 Pro 25H2, build 26200.9168. The
read-only #57 inspection observed its first eight checks ready but the firewall-scope check
`not-ready` because pre-existing enabled Public/Any rules can include TCP 445. Issue #58 did not
change that network profile, firewall state, SMB policy, or any physical-host folder/share/ACL.
Operational host apply remains fail-closed until the owner separately approves how those
pre-existing host rules are handled.

## Non-goals retained

No existing nonempty folder is adopted; no user file is deleted; no Public-profile TCP 445 rule,
Member credential, drive mapping, long-running elevation, arbitrary command execution, provider
activation, or remote/physical-client access is claimed by this slice.
