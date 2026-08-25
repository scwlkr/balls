# Company-first LAN Circle Files pilot verification

**Date:** 2026-08-25

**Issue:** [#73](https://github.com/scwlkr/balls/issues/73)

**Branch:** `codex/73-company-first-lan-pilot`

## User outcome

The first operational target is one Circle Owner and one invited coworker using a newly created
Windows-hosted project folder on their private LAN. The coworker opens the self-contained Windows
package, joins with one signed private invitation, and receives only their own authorized shared
folder in Windows Explorer. Existing-folder adoption, public internet access, new infrastructure
providers, AI, and Circle Apps remain outside this pilot.

## Automated security and product checks

- The existing signed one-use admission remains the membership boundary. A copyable outer
  invitation adds separate private IPv4 admission and authenticated file-synchronization endpoints
  without changing the signed package or treating an address as identity.
- The authenticated browser verifies the actual persisted local Member and role instead of
  inferring ownership from participant ordering. Invitation, join, and file synchronization remain
  behind the existing same-origin session and antiforgery protections.
- A direct two-node integration test establishes signed admission and mutual TLS, creates distinct
  Owner and ordinary-Member grants, transfers only the requesting Member's grant and provider
  credential, maps with an ordinary-Member platform fake, retries idempotently, and verifies that
  imported signed state and protected credential material survive SQLite restart. The joined Node
  never receives the Circle root's private authority.
- A separate two-daemon browser test binds an actual private IPv4 interface, admits the recipient,
  synchronizes through the authenticated browser route and mutual-TLS listener, excludes the
  Owner's grant, previews and maps `P:` using an ordinary-Member platform fake, and checks that
  the exact protected credential reaches the mapper without appearing in browser responses.
- Negative tests reject altered Owner-signed grant contents, importing another Member's grant,
  a forged Member signature from an otherwise authenticated peer, and an ordinary peer claiming
  the Owner's Member identity.
- Browser component tests cover delayed Owner grant creation, bounded synchronization retry,
  wrong-Member exclusion, and automatic selection of an available `P:` drive without recipient
  entry of an IP address, SMB credential, grant identifier, or mapping plan.
- Browser and remote contracts never return the provider password. Full Owner/authority
  signatures, the exact requesting Member and authenticated Node, current generation, and
  provider-credential binding are checked before local protected storage and mapping.
- Windows firewall readiness examines the exact application and service associated with a
  Public-capable TCP 445 rule. It ignores an explicit unrelated executable/service but continues
  to reject unrestricted, System, ambiguous, or SMB-service bindings; unrelated Windows app rules
  do not have to be disabled to satisfy a mistaken port-only classification.
- The exact read-only firewall applicability predicate passed all 12 safe/unsafe synthetic cases
  under real Windows PowerShell 5.1 on the isolated recipient guest.
- A Public-only inbound TCP 445 block is accepted only when its effective rule, port, application,
  service, address, interface, dynamic scope, and security filters cover all public SMB traffic.
  A Private/Any-profile block, incomplete or ambiguous scope, or authenticated allow-rule bypass
  remains unsafe; unrelated existing firewall rules are never modified by inspection. A dormant
  Public-only rule is accepted only when its sole inactive reason is the currently connected
  Private profile. The exact broad-block and bypass predicates passed 26 cases under real Windows
  PowerShell 5.1.
- The stable `guest-access` check applies to the Balls-hosted SMB server/share: required server
  signing, rejection of unencrypted access, and per-share encryption support exclude unauthenticated
  guest sessions. An unrelated outbound SMB client connection does not grant guest access to the
  Circle share and is never disabled or mutated by readiness inspection.
- A real working Windows VM exposed a read-only firewall inventory edge case: asking PowerShell
  for enabled inbound `Block` rules throws `ObjectNotFound` when all enabled inbound rules are
  `Allow`. The observer now retrieves the enabled inbound inventory once, separates exact Allow
  and Block actions in memory, and fails closed on unknown actions or real inventory failures.
  The exact selector and its five allow-only/zero-block/mixed/invalid-action cases passed under
  real Windows PowerShell 5.1; focused platform tests passed without mutating firewall policy.
- Deploying the complete firewall observer to the real Owner VM also exposed Windows' 32,767-
  character `CreateProcess` command-line limit: the unchanged fixed observer encoded to a
  33,708-character command and made every readiness check fail closed. Removing only leading
  indentation from each existing script line reduced the exact command to approximately 30,232
  characters without changing its content, execution mode, policy, timeout, or security checks.
  A platform-independent regression bounds the complete encoded command before Windows execution.
- Genuine cross-VM mapping exposed a contradiction hidden by same-user and fake-mapper tests:
  the encrypted SMB share correctly uses access-based enumeration, while its host and grant
  ownership markers correctly grant access only to the Owner and LocalSystem. An invited Member
  cannot see those private markers, so the previous filename-based identity check always rejected
  a legitimate remote mapping. A separate bounded grant-specific witness now carries only public
  identity fields plus a domain-separated HMAC-SHA256 proof bound to the protected provider
  credential. Its ACL is protected at file creation and grants only the exact Member read access,
  while Owner and LocalSystem retain full control. The original private markers and access-based
  enumeration remain unchanged. Missing, altered, oversized, cross-grant, wrong-generation, and
  wrong-secret witnesses fail closed; an existing otherwise-owned grant can repair only its
  missing witness without replacing its account or share permissions.

The repository fast gate passed on Linux with the pinned Node version:

```text
mise exec -- dotnet run --project eng/Balls.Verify --configuration Release -- fast
```

The run completed locked dependency restore, C# formatting, generated-client and web-format
checks, a zero-warning Release solution build, web lint/typecheck, categorized .NET tests,
12 browser component tests, a production browser build, and one real Chromium Playwright journey.
An earlier pull-request checkpoint also passed the Ubuntu, Windows, and macOS fast lanes, the
required-check aggregate, dependency review, and CodeQL. Check the current pull-request head
separately; an earlier green checkpoint is not evidence that a later head has passed.

## Windows lab observations

The existing working Windows 11 Pro guest and one isolated Windows Server 2025 Desktop Experience
guest were used. The working guest's existing interactive user and running Revit process remained
active; no VM restart, checkpoint restore, Defender change, or firewall-policy relaxation was
performed. Narrow reversible forwarding bound SMB, admission, and authenticated file
synchronization only to the current private LAN address.

The isolated recipient guest initially had no .NET runtime, PowerShell 7, Git, or development
checkout. The `win-x64` self-contained CLI and daemon started successfully on that guest and
returned a valid structured Node status without installing .NET or PowerShell. The Windows
archive includes the double-click `Open Balls.cmd` launcher and complete Windows helper files.

The working guest's self-contained daemon also started under its existing interactive, unelevated
Windows user without interrupting the active Revit process. The isolated Windows guest reached
that working guest's private container-network SMB, signed-admission, and authenticated
file-synchronization ports. The existing working user's CLI created one Circle and one signed
invitation; the runtime-free isolated Windows guest redeemed that invitation against the actual
working guest's private admission listener. Both devices then reported the same Circle with two
distinct Members and two distinct Nodes. The invitation moved only over protected local channels
and was never written to console output or public evidence. On the isolated guest, a genuine
same-user browser launch capability then established an authenticated session and antiforgery
context; the browser file-synchronization route reached the working guest over admitted mutual
TLS and returned success with zero authorized grants, as expected before host provisioning. This
direct container-network result does not establish that a separate physical LAN device can reach
the host.

The owner also made a freshly installed, separate physical Windows laptop available with no
personal or production data. Its available Windows account does not have administrator rights.
Administrative OpenSSH enrollment is neither available nor required: an ordinary recipient must
launch Balls, redeem its invitation, and map the approved folder without elevation. The physical
device's actual Balls connectivity and real Explorer/file operations have not been observed.

The owner subsequently reported that Microsoft Smart App Control blocked the unsigned application
on that physical Windows account, which does not have administrator rights. Repository policy
already records that local `balls.exe` and `ballsd.exe` are unsigned, and package inspection also
found unsigned Balls assemblies; signed Microsoft runtime dependencies do not make those separate
application binaries trusted. The exact device policy/event has not been independently inspected,
but the reported block prevents claiming physical Balls execution. No alternate executable,
removed download marker, weakened policy, or unsigned-code bypass was used. The disposable
authorized Windows guest remains available for genuine product testing; built-in browser, TCP,
or plain SMB checks on the physical laptop do not establish Balls acceptance. The owner explicitly
selected the existing disposable Windows guest as the immediate boss simulator and deferred
physical-device product testing until its administrator can legitimately address the block.

After returning to the owner-approved two-VM route, the existing isolated Circle still had its two
persisted Members and Nodes; the existing disposable guest again completed an authenticated
mutual-TLS browser synchronization against the owner's running Windows daemon. It correctly
received zero grants because the defined project contribution had not yet been hosted and no
limited Member account had been provisioned. Read-only inventory showed zero enabled inbound
Block rules and an active existing mapped-drive workload. Revit, that unrelated mapped drive,
the owner daemon, firewall policy, and VM configuration were not interrupted or changed.

With the owner's explicit authorization, one local inbound TCP 445 Block rule restricted exactly
to the Public profile was added to the working Windows VM. Its unrestricted scope and dormant
`ProfileInactive` status were verified while the connected network remained Private. The existing
SMB client guest setting and guest-only `Z:` share were not changed. All nine readiness checks
then passed, and the regular typed elevated helper created the dedicated new `C:\BallsProjects`
folder and encryption-required `balls-01a039487405` share. Its initial share ACL contained only
the Owner, and its protected folder ACL contained only the Owner and LocalSystem.

The existing Windows VM reported effective PowerShell execution policy `Restricted`, with every
policy scope `Undefined`. The grant helper's protected local `-File` script correctly refused to
run under that policy; the failed attempt created no account or ACL entry. After separate explicit
owner authorization, only that Owner's `CurrentUser` policy was temporarily set to `RemoteSigned`
for the exact pending grant operation and then restored to `Undefined`. Independent readback
confirmed all five scopes returned to `Undefined` and effective policy returned to `Restricted`.
No process bypass flag, alternate execution path, machine-wide policy change, or managed-policy
override was used.

The approved grant operation created exactly one existing-Member account. The encrypted share
then contained only Owner `Full` and that account's `Change` access; the protected folder ACL
contained only Owner/LocalSystem `Full` plus that account's `Modify` access. The disposable
Windows VM subsequently completed a real authenticated browser/mutual-TLS synchronization with
`importedGrantCount=1`, importing only that Member's protected provider credential. The Owner VM
already had `EnableLUA=0`; therefore its standard `runas` helper elevated automatically and no
visible administrator consent dialog or user click was observed or claimed. The disposable guest
also had `EnableLUA=0` and an administrator-capable account; running its daemon in the existing
interactive Explorer session proves session-correct mapping, not a genuine standard-user token.

After the private-marker/access-based-enumeration conflict was reproduced on the actual two
Windows guests, both daemons were updated in place at their existing approved executable paths.
The existing Boss grant was reconciled without replacing its limited account or share access.
Its new 511-byte authenticated witness was independently observed with a protected ACL granting
only Owner and LocalSystem `FullControl` and the exact Boss account `Read`; both original private
markers remained Owner/LocalSystem-only, and the share retained access-based enumeration.
The Owner's temporary execution-policy setting was again restored to `CurrentUser=Undefined` and
effective `Restricted` immediately after the authorized helper completed.

The actual disposable Windows guest then mapped `P:` to
`\\172.18.0.4\balls-01a039487405` through its existing interactive Explorer session and verified
the exact authenticated grant witness over the remote share. Windows negotiated SMB 3.1.1 with
`Encrypted=True`. Its data-share connection reported `Signed=False` while the separate IPC
connection reported `Signed=True`: this is expected because
[SMB encryption supersedes separate signing while providing the same tamper protection](https://learn.microsoft.com/en-us/windows-server/storage/file-server/configure-smb-client-require-encryption).
The Owner's signing-required policy and share-level encryption requirement remained enabled.
The Boss read and edited an Owner-created file, then created, edited, and renamed a second file
through `P:`; the Owner independently read both exact final contents from `C:\BallsProjects`.
The Boss daemon was then stopped and restarted through its existing same-user interactive task;
the restarted process remained in Explorer's Session 1, retained its protected grant and Circle
identity, and the product's mapping-inspection command independently confirmed the same persistent
`P:` drive and encrypted SMB connection. This is real encrypted cross-VM Windows file access and
restart persistence, not a fake mapper or loopback-only proof. The interactive-session account
remains administrator-capable, and the actual mapping request was issued through the CLI rather
than an observed click on the browser's guided mapping button. The user later explicitly authorized
interruption of work on the Owner VM; its unrelated guest `Z:` share remained connected.

The complete self-contained Windows pilot archive was rebuilt from the same corrected daemon and
helper already running on both guests, then replaced the previous stale local-transfer copy. The
99,887,103-byte archive contains the current authenticated-witness/compacted-observer platform
assembly, self-contained CLI and daemon, full adjacent Windows helper, browser assets, and root
`Open Balls.cmd` launcher. Its SHA-256 is
`ae1b3e4922977f6833ebb696bf999338b48c7c9f22de5681015dde322076759f`. No GitHub release or public
artifact was published; this is the local, manually transferable development package.

The Linux workstation's existing LocalSend installation and already approved LocalSend firewall
rule can offer the nonsecret self-contained Windows package through its normal browser-link
mode. The separate physical Windows laptop reached that page through its existing browser and
requested the exact Windows ZIP across the real private LAN without installing software,
requesting administrator access, creating a new firewall exception, or exposing invitations or
provider credentials. This proves only the existing LocalSend browser-transfer path; it does not
override Smart App Control or establish reachability to the separate Balls/SMB ports.

Windows administrator consent for actual folder/share and grant-account provisioning is deliberately
preserved. Linux host firewall rules are likewise not changed implicitly: a running private-address
listener alone does not prove that another physical LAN computer can pass an active inbound
firewall. Physical-workstation access must be explicitly observed before being claimed.

Before the approved server/client boundary correction, the real working Windows guest reported
`not-ready` for unrelated outbound guest access and for existing Public/Any inbound firewall rules
that could admit SMB. A dedicated new-folder hosting preview correctly refused to proceed without
creating a folder or share. The existing outbound guest setting was independently shown to support
an active guest-only host share with open handles while Revit was running. Disabling that unrelated
client setting would interrupt real existing work; the approved correction instead verifies that
the Balls-hosted share itself excludes guest access. No existing rule or guest-logon policy was
changed without explicit administrator approval.

After redeploying only the isolated Balls daemon with the approved provider correction, the real
working Windows guest returned `guest-access: ready` with code `guest_access_precluded` while its
existing guest-only host share and open handles remained available. The only remaining readiness
failure was the independently observed Public-capable SMB firewall scope. The existing isolated
Circle, both Members, and the protected cross-device file-synchronization route remained usable
after that daemon-only restart.

## Not implied by this record

An automated in-process fake-mapper test is not evidence of two real Windows computers opening,
editing, and renaming the same SMB-hosted file. A loopback or Docker-network check is not proof of
physical LAN reachability. Nothing here claims signed public binaries, public release publication,
internet access, durable discovery after browser-origin changes, or user-approved administrator
consent that was not actually observed.
