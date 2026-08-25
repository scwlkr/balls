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
