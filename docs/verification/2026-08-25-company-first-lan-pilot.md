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

The repository fast gate passed on Linux with the pinned Node version:

```text
mise exec -- dotnet run --project eng/Balls.Verify --configuration Release -- fast
```

The run completed locked dependency restore, C# formatting, generated-client and web-format
checks, a zero-warning Release solution build, web lint/typecheck, categorized .NET tests,
12 browser component tests, a production browser build, and one real Chromium Playwright journey.
The corresponding pull request also passed the Ubuntu, Windows, and macOS fast lanes, the
required-check aggregate, dependency review, and CodeQL.

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

Windows administrator consent for actual folder/share and grant-account provisioning is deliberately
preserved. Linux host firewall rules are likewise not changed implicitly: a running private-address
listener alone does not prove that another physical LAN computer can pass an active inbound
firewall. Physical-workstation access must be explicitly observed before being claimed.

The real working Windows guest initially reported `not-ready`: insecure SMB guest logons were
enabled, and existing Public/Any inbound firewall rules could admit SMB. A dedicated new-folder
hosting preview correctly refused that unsafe configuration without creating a folder or share.
The existing guest-logon configuration was independently shown to support an active guest-only
host share with open handles while Revit remained running. Changing that unrelated global client
setting would interrupt real existing work. No existing rule or guest-logon policy was changed
without explicit administrator approval.

## Not implied by this record

An automated in-process fake-mapper test is not evidence of two real Windows computers opening,
editing, and renaming the same SMB-hosted file. A loopback or Docker-network check is not proof of
physical LAN reachability. Nothing here claims signed public binaries, public release publication,
internet access, durable discovery after browser-origin changes, or user-approved administrator
consent that was not actually observed.
