# Limited SMB Member credential verification

**Date:** 2026-08-21  
**Issue:** [#59](https://github.com/scwlkr/balls/issues/59)  
**Scope:** one protected, limited Windows SMB credential per Member Access Grant

## Implemented outcome

- Protected local control and the CLI expose deterministic credential preview/apply for one exact
  Circle, Contribution, Access Grant, Member, access mode, generation, host plan, local account,
  and ownership ID. Core revalidates the current local Contribution and signed grant before both.
- Apply creates one random 32-character complex credential, persists only its DPAPI CurrentUser-
  protected form in SQLite schema v7, and sends plaintext only through the authenticated 64 KiB
  helper pipe. Candidate/material/protocol byte arrays are zeroed. API, CLI, OpenAPI/browser,
  history/list, error, and object-string projections never return the secret.
- The helper creates one deterministic `BallsG-*` local account with no group membership, no
  expiry, and exactly four deny rights: interactive, remote interactive, batch, and service logon.
  It grants only whole-folder `ReadAndExecute`/share `Read` or folder `Modify`/share `Change`; it
  never grants `FullControl` or access to another share. Target folder/share principals are exact-
  allowlisted; known broad principals fail before mutation, and actual network token groups catch
  custom/nested group access on another non-special share before share access is granted.
- A protected grant marker records the full binding and the folder's pre-mutation SDDL. Account,
  marker, folder ACL, and share ACL must all match exact owned state. Foreign or changed state fails
  closed. Pending protected state survives daemon restart, so an exact retry uses the same password.
- Failure rolls the proven-owned prefix back in reverse, restores the prior folder ACL, removes the
  marker/share entry, removes the four LSA rights, and then deletes the exact owned account.
- Daemon apply serializes the complete prepare/helper/complete sequence. A concurrent exact retry
  cannot race forward work or roll back another request's resources.

Credential delivery and Explorer mapping are #60. Rotation/revocation and lifecycle cleanup are
#61. This slice does not activate Contributions/grants, adopt existing content, or alter network,
firewall, SMB, or global account policy.

## Automated evidence

Focused contracts cover secret generation, redacted public output, signed-grant tamper rejection,
deterministic plans after JSON round trip, helper command closure, apply/retry/collision/reverse
rollback, SQLite protected-secret restart/conflict/corruption and v6-to-v7 migration rollback,
daemon endpoints, CLI output, OpenAPI drift, concurrent exact apply, and unsupported non-Windows
adapters. The full local repository gate is rerun after the final evidence commit; required GitHub
checks are recorded on the pull request and issue after they complete.

## Dedicated Windows VM evidence

The owned `Balls.Dev.Windows11` Hyper-V guest ran Debug and Release source builds from exact clean
commit `8946e9b7e404c34eec3d396298763bb63527cb14`. No checkpoint was restored. The test temporarily
changed only the guest's connected profile and the exact 17 event-identified Public/Any inbound
allow rules used by the readiness fixture; the physical host was not changed.

Observed structured result:

| Check | Observation |
| --- | --- |
| Broad-access fixture | unrelated non-special Builtin Users/Network-full share rejected as `grant_resource_collision` before account creation |
| Nested group fixture | custom local group containing Authenticated Users detected from the created account's actual network token; `grant_apply_failed`; exact owned prefix rolled back |
| Child termination after account creation | `grant_apply_failed`; exact account with zero rights recovered and removed |
| Child termination after LSA rights | `grant_apply_failed`; exact account and four rights recovered and removed |
| Debug account failure | failure immediately after LSA rights returned `grant_apply_failed`; `AccountFailureRollbackClean: true` |
| Debug injected failure | later share-access failure returned `grant_apply_failed`; `FailureRollbackClean: true` |
| Release clean apply | `applied` |
| Release restart retry | `already-applied` with the unchanged plan/protected credential |
| Public projections | redacted; no protected secret, signatures, transcript, or authorization proof |
| Local account | deterministic `BallsG-*`; enabled; no local-group membership |
| Deny-logon rights | exactly 4: interactive, remote interactive, batch, service |
| Correct issued credential | network logon allowed; SMB write, append/edit, read, and delete all succeeded |
| Non-SMB logons | interactive, remote interactive, batch, and service all denied with the correct issued credential |
| Wrong-password network logon | denied |
| Share authorization | exactly one matching `Change` allow entry; zero foreign-share entries |
| Folder authorization | `Modify, Synchronize`; protected ACL; no `FullControl` |
| Readiness fixture | 17 exact pre-existing rules temporarily disabled and restored |
| Final cleanup | Public profile restored; all 17 rules enabled; zero Balls grant accounts, shares, or firewall rules; zero Issue59 lab roots; guest repository clean |

The VM campaign found and fixed Windows-specific defects before final acceptance: local-user
descriptions are capped at 48 characters, so the account uses a deterministic 47-character short
ownership marker while full ownership remains in SQLite and the protected folder marker; LSA deny
rights require explicit removal before deleting an owned account; an externally terminated child
requires recoverable partial-account state; nested Windows principals can otherwise give the limited
account inherited access; and the fixed encoded command exceeded Windows' command-line limit after
hardening, so it now passes the same fixed 13 KiB script directly under an asserted 32,767-character
budget while caller data remains structured JSON on stdin. The issued-credential diagnostic read the
DPAPI-protected row only inside the disposable guest process and emitted booleans, never the generated
password. Its managed password string existed only for that short process lifetime.

## Physical-host boundary

The intended physical host remains Windows 11 Pro 25H2, build 26200.9168. This issue did not change
its network profile, firewall rules, SMB policy, accounts, folders, shares, or ACLs. Operational
host work remains fail-closed behind #57 readiness and explicit owner approval for any change to
pre-existing physical-host network/firewall state.

## Non-goals retained

No credential is delivered on a command line or browser response; no drive is mapped; no existing
user content is adopted or deleted; no arbitrary elevated command is accepted; no account receives
interactive/local execution rights, unrelated share access, or `FullControl`; and no rotation,
revocation, physical-client proof, or production host mutation is claimed.
