# Threat Model Starter

**Status:** Windows/Linux local Node baseline, protected production Node/Circle/transport identity
storage, bounded invitations, authenticated LAN transport, persisted two-Node membership,
minimal durable Circle messaging, provider-neutral Circle Files authorization, read-only Windows
SMB readiness, dedicated Windows folder hosting, and limited per-grant Windows SMB credentials,
2026-08-21.

## Scope

This baseline covers one unelevated Windows or Linux account running `ballsd`, the same-account
`balls` CLI, the local OS-IPC control API, the authenticated loopback browser adapter, the bundled
browser UI, and the daemon's SQLite state directory. It also covers the remote v1 identity,
authenticated-channel, admission, and minimal persistent-message boundaries. Executable loopback,
separate-process, and Windows-host/Ubuntu-VM tests prove the transport, admission, and
message/restart outcomes. It also covers local definition of File Contributions and Member Access
Grants, bounded inspection of Windows SMB/network/firewall readiness, and one narrow elevated
operation that creates a dedicated folder ACL, encrypted share, and Private/LocalSubnet firewall
rule. It also covers one protected random local-account credential and exact Member-specific
folder/share ACL per Access Grant and exact current-user Explorer mapping. Credential sharing
between Members, rotation/revocation, Contribution activation/revocation, and existing-content
adoption remain out of scope.

## Assets

- persistent local Node identity;
- Circle, Member, and Node records known to this daemon;
- integrity and availability of the local database;
- authority to issue local control requests as the current OS account;
- short-lived browser launch and session authority;
- Circle authority, delegated Anchor, Member, Node, and transport private keys;
- signed authority state, membership credentials, revocations, invitations, and admission
  transcripts;
- explicit encrypted Circle authority exports and their custody metadata.
- Circle Files Contribution/provider identities, Member Access Grants, lifecycle/generation, and
  their canonical Owner-Member/current-root authorization proofs.
- dedicated-folder ownership marker and recovery journal, deterministic host plan, protected ACL,
  encrypted SMB share, and narrowly scoped firewall rule.
- exact grant credential binding and lifecycle, DPAPI-protected provider secret, grant-owned local
  account and deny-logon rights, protected grant marker, and exact folder/share ACL entries.
- exact current-user drive/UNC mapping, Credential Manager target, Explorer friendly-label marker,
  and mapping ownership ID.

## Trust boundaries

1. CLI or another same-account client to the daemon over a Windows named pipe or Linux Unix-domain socket.
2. Browser JavaScript to the daemon's separate IPv4 loopback HTTP adapter.
3. Daemon process to the dedicated local state directory and SQLite database.
4. OS account boundary to other local users and administrator/root authority.
5. An untrusted LAN, Tailscale, or future-provider stream to the TLS 1.3 remote Circle protocol.
6. Admission bootstrap from a signed invitation and pinned Anchor transport key to not-yet-trusted
   Member/Node credentials.
7. The selected Anchor's live authority state to explicit offline authority export/restore.
8. The Windows adapter to a bounded child PowerShell process reading fixed SMB, registry, network,
   service, command-metadata, and firewall observations.
9. The unelevated daemon to the UAC-elevated Windows helper over a random one-time named pipe, and
   that helper to fixed Windows account, logon-right, ACL, SMB-share, and firewall command adapters.

The same OS account is the current local-control principal. Local IPC does not distinguish between
processes running as that account.

## Current threats and mitigations

| Threat | Current mitigation | Residual risk |
| --- | --- | --- |
| Another ordinary local user sends control requests | Windows uses `CurrentUserOnly` named pipes; Linux uses a user-owned `0700` runtime directory and `0600` Unix socket | A malicious process already running as the same user can connect |
| Browser listener reaches the LAN | Kestrel binds the browser adapter to ephemeral IPv4 loopback only; socket inspection on Windows and Linux contract runners rejects any non-loopback listener | A future transport change could widen the boundary and requires review |
| A hostile site or DNS rebinding drives browser requests | Exact Host and Origin validation, no permissive CORS, an authenticated session, and an antiforgery token on state changes | A malicious process already running as the same user can use protected IPC to request its own launch capability |
| Browser credentials leak through history, logs, or storage | The one-minute single-use launch capability is placed only in the URL fragment and removed after exchange; the 30-minute session is an HttpOnly, Secure, SameSite=Strict cookie; antiforgery authority stays in memory | Browser extensions, debugging tools, or compromise of the current user session remain powerful |
| Injected browser content gains broad authority | Only the bundled production assets are served; CSP restricts content to the same origin, framing and objects are disabled, responses are not cached, and the browser API is a narrow application projection | A future feature that renders rich or third-party content requires a new review |
| State placed on an unsafe or substituted path | Windows rejects network/reparse paths; Linux requires absolute local paths, rejects symbolic links and writable ancestry, and verifies ownership; both require an exact marker and entry allowlist | Retained-directory-handle defenses are not yet implemented; administrator/root and same-user races remain powerful |
| Another ordinary user reads or changes state | Windows applies protected current-user/LocalSystem ACLs; Linux applies user-owned `0700` directories and `0600` known files | Administrators/root, offline disk access, and inherited account compromise remain powerful |
| Node or Circle private signing material is read from local state | Windows wraps each PKCS#8 key with current-user DPAPI and domain entropy; Linux creates the database as `0600` inside a verified owned `0700` directory | Same-user compromise, administrator/root, memory inspection, and offline attacks against an unencrypted Linux disk remain powerful |
| Protected key state is substituted, malformed, or moved between protection schemes | Startup validates the exact scheme, P-256 encoding, role-scoped public key ID, and public/private binding before serving; failure returns one bounded error and never regenerates | Same-user code execution can replace the whole database and its public identifiers; trusted recovery/import is not implemented |
| Stale or substituted Linux socket | The adapter removes only a disconnected socket owned by the effective user, refuses live/non-socket/foreign-owned entries, applies `0600` after bind, and removes the socket during orderly shutdown | A killed daemon leaves a stale socket until the next validated startup; same-user races remain possible |
| Wrong, newer, incomplete, or corrupt SQLite state is opened | Application ID, schema version, exact schema shape, integrity, and foreign-key checks fail closed | Recovery and backup tooling do not yet exist |
| Two daemons write the same state | An exclusive `ballsd.lock` lease permits one daemon owner | A crash can leave the file, but the OS releases the lease |
| Oversized local or browser request consumes resources | Kestrel limits request bodies to 32 KiB; the IPC client limits buffered responses to 256 KiB | Same-user denial of service is not comprehensively addressed |
| Duplicate Circle creation after retry | A caller-provided request UUID makes creation idempotent; conflicting reuse is rejected | The UUID is not authentication or a general replay defense |
| Malformed identifiers or names reach storage | Boundary validation, typed core identifiers, length limits, and parameterized SQL | Most same-account local-control operations do not add capability-specific authorization |
| A local non-Owner defines Circle Files state | Each mutation resolves the protected local Member identity, requires its persisted Circle role to be Owner, and verifies a Member signature over the exact mutation | Malware in the same unlocked Owner account/session can ask the daemon to sign an authorized mutation |
| A stale or substituted Circle authority authorizes a contribution/grant | The persisted current generation/root credential must exactly match the protected live Circle authority; the same transcript is independently root-signed and verified before commit | Root rotation/recovery and replicated authorization convergence remain separate work |
| Retry creates duplicate or conflicting contribution/grant state | Caller request UUID plus normalized input is transactional and idempotent; conflicting reuse and duplicate Contribution/Member grants fail closed | Request IDs are replay identities, not bearer authentication |
| Circle Files metadata leaks provider secrets or private authority | Core records contain no provider credentials; local-control, CLI, and browser projections omit transcripts, signatures, passwords, and private material; browser mapping bodies contain only endpoint/letter/plan ID and retain antiforgery state in memory | Object IDs, endpoint, drive, lifecycle, Member/Node relationships, and authorization times are intentionally inspectable to the same OS account |
| Provider password leaks through API, CLI, browser, logs, or restart state | The daemon creates 32 random complex ASCII characters, protects them immediately with DPAPI CurrentUser plus domain entropy, persists only the protected blob, sends plaintext only through the authenticated bounded helper pipe, zeroes candidate/material/protocol byte arrays, and returns only the public plan; `ToString` and all browser/list/history projections are redacted | The bounded child PowerShell process necessarily materializes immutable JSON/password strings for its short lifetime; same-user compromise, administrator/LocalSystem, debugger or memory inspection, and a compromised helper remain powerful |
| A credential is substituted across Circle, grant, Member, access, or generation | SQLite and the helper bind the exact Circle/Contribution/grant/Member/provider/account/ownership/access/generation tuple; Core revalidates current local Contribution and Access Grant authorization before every preview/apply; changed or duplicate bindings fail closed | Rotation and revocation need new lifecycle authorization rather than mutation of this v1 binding |
| Revocation races provider cleanup or later authorization | The Owner revokes one expected grant generation atomically with an immutable dual-signed proof; cleanup accepts only that proof, while every active grant/credential/mapping authorization rejects `revoked` immediately | A remote SMB session authorized before convergence can remain open until separately confirmed termination |
| Cleanup removes foreign or user-owned state | Every rollback step re-inspects recorded account/ownership IDs, protected markers, ACL entries, share/rule descriptions, and exact properties; ambiguous, blocked, or substituted state refuses mutation. A protected exact recovery witness permits restoration of Windows' automatic share-creation firewall change only while creation remains incomplete; a later administrator enablement without that witness refuses cleanup. Host cleanup never calls folder/content deletion, and Windows integration preserves exact user-file bytes | An administrator can forge all local evidence; secure deletion and recovery/import UX are out of scope |
| Cleanup kills unrelated SMB sessions | The first apply returns bounded `busy` without mutation. A second explicit confirmation is accepted only after that durable audit outcome and closes only the exact grant account's sessions. Final host cleanup closes only exact open-file handles under the contribution and does not call session termination, so unrelated handles and idle tree connections on the same session survive | Windows open-file reporting can fail or change concurrently, producing conservative `partial` or conflict results |
| Interrupted cleanup loses recovery state or audit leaks secrets | Revocation, protected binding, removed lifecycle, and append-only cleanup/unmap audit survive restart; busy/partial never complete removal. Requests are recorded before mutation and terminal results/refusals/failures/cancellations afterward, so an interrupted request remains visible and idempotent retry records its eventual outcome. Audit contains only IDs, stable tokens, bounded counts, and times | Protected recovery material remains until a future secure-erasure design; same-user inspection sees object relationships and timing |
| A network endpoint or wrong share is treated as Circle authority | Mapping accepts only canonical numeric private/loopback IPv4, derives the share/account/marker names from signed local state, authenticates with the exact random grant credential, and requires both protected marker names through SMB before success; marker contents remain unreadable and endpoint metadata grants no authority | A host administrator can forge local marker names and Windows resources; authenticated remote replication remains separate work |
| Mapping overwrites or later removes another resource | The user explicitly selects D-Z; occupied drive, credential target, or Explorer label collisions fail before mutation, including a foreign mapping to the planned UNC. Retry and unmap compare exact UNC, account, ownership comment/marker, and friendly name; unmap is non-forced and preserves changed/open mappings and unrelated label-key values | Windows does not return the stored domain-password blob on every supported configuration; ownership therefore relies on the random derived comment plus target/account, while same-user races and administrator manipulation remain possible |
| Provider password leaks while mapping | The local API/browser/CLI never receive the password. Preview, inspect, and unmap load binding metadata without decryption; map/reconnect alone unprotect into disposable, exception-zeroed buffers, writes the current-user Credential Manager value, and passes an explicitly NUL-terminated pointer directly to Win32 rather than process arguments or shell commands | The Windows networking API consumes plaintext in daemon memory; same-user compromise, administrator/LocalSystem, debugger, crash dump, or Credential Manager compromise remain powerful |
| Limited account gains local execution or unrelated file access | The account has no group membership and exactly denies interactive, remote-interactive, batch, and service logon; it receives one inherited whole-folder `ReadAndExecute` or `Modify` ACL and one encrypted-share `Read` or `Change` entry, never `FullControl`. The target folder/share must equal the exact Owner/System host baseline plus all protected marker-backed grants: deny entries, reduced Owner rights, wrong grant rights, orphan SIDs, and unmarked principals fail closed. Known broad token principals are rejected before mutation, and the created account's actual network token groups are checked against every other non-special share | Windows/network logon is intentionally allowed for SMB; access through special administrative shares remains governed by Windows privileges; Windows account-policy or ACL bugs remain high impact |
| Failed, terminated, or concurrent grant provisioning leaves an account, rights, or ACL | Daemon apply serializes the complete prepare/helper/complete sequence. Each helper step requires exact owned state; failure and partial retry remove only that grant's exact share/folder entry, protected marker, expected LSA-rights subset, and local account in reverse order, preserving other marker-backed grants. An account with the exact random password/description, no groups, and only an expected-rights subset is recoverable after the fixed PowerShell child is killed; changed target state still stops cleanup | Power loss or administrator interference can require exact retry; forged ownership evidence is possible to an administrator |
| Caller input changes the Windows readiness command | The platform boundary exposes no command or argument input; one exact enum allowlist selects a static encoded script and an exact system PowerShell executable, without a shell | Same-user compromise can replace the running process or machine tools and is outside this inspection boundary |
| Windows inspection hangs, floods output, or returns malformed/forward-unknown data | The child process has a 10-second timeout, one combined 65,536-character decoded-output budget for both streams, strict JSON parsing, exact recognized values, and deterministic redacted `unknown` results | Inspection can be unavailable or conservatively unknown; this endpoint is not a general diagnostic console |
| SMB would be exposed on an unsafe network scope | Readiness is `not-ready` unless a connected Private profile exists, Private/Public firewall profiles are enabled with default inbound Block, and no enabled Public/Any-profile inbound allow rule has a protocol/port scope that can include TCP 445 | Readiness proves enforceable preconditions; the separate exact helper rule and ownership checks enforce mutation scope, while ambiguous broad pre-existing rules are rejected conservatively |
| Readiness inspection mutates the host | The static readiness allowlist uses only read operations and command-metadata inspection; contract tests reject mutation verbs and VM before/after snapshots proved no observed setting change | Hosting is a separate explicit preview/apply contract and never occurs through the readiness endpoint |
| Caller input becomes an arbitrary elevated command | The daemon launches one exact adjacent helper and passes only a random pipe name and daemon PID; the helper accepts one versioned hosting plan, verifies both persisted Contribution signatures and exact signed fields, recomputes it under elevation, and selects only typed folder/share/firewall operations plus a fixed PowerShell command enum. Hosting uses an encoded constant; grant credentials use a direct fixed constant under an asserted command-line budget; caller data remains JSON on stdin | Replacement of the daemon/helper binary by the same user or an administrator remains outside this source-run boundary; installed artifacts require protected locations and signing |
| Another process impersonates the daemon or elevated helper | A cryptographically random pipe name is never persisted; the daemon verifies the connected client PID equals the exact launched helper PID; the helper verifies the pipe server PID, adjacent `ballsd.exe` path, and process-token Owner SID before accepting the signed plan. The pipe ACL admits only that Owner and administrators, and messages are length-prefixed and capped at 64 KiB | Source-run binaries live in a same-user-writable tree; administrator/LocalSystem and compromise or replacement of either authenticated process remain powerful |
| A hostile or substituted folder path reaches privileged mutation | Both privilege levels canonicalize the same absolute fixed-local path and reject drive roots, Windows/profile roots, network locations, files, existing reparse traversal, nonempty directories, and foreign markers; the elevated helper reruns preflight immediately before mutation | Same-user and administrator races remain possible without retained directory handles; Balls therefore never adopts existing content |
| A retry or foreign resource is mistaken for Balls-owned state | The plan and ownership IDs are deterministic from authorized Circle/Contribution/provider/Node/path inputs; exact marker, journal, ACL semantics, share name/path/description/encryption/access, and firewall name/description/profile/address/service properties must all match | A same-user administrator can forge or replace all local ownership evidence |
| Partial failure leaves unsafe access or rollback deletes user state | An operation journal records folder provenance before restriction; it can identify only the exact target as newly created, never a parent/ancestor. All resource collisions are preflighted; failure and partial-state recovery inspect exact ownership and roll back in reverse order; only proven-owned rules/shares/markers are removed, the target is removed only while empty, and an originally empty folder's observed prior ACL is restored | Power loss or hostile interference can require a later retry; a recovery-incomplete result fails closed instead of broad cleanup |
| SMB is exposed beyond the intended private LAN | Apply requires the complete readiness report to be `ready`; the owned share requires encryption, the host Owner principal, and only exact grant-owned additions; the only created inbound rule is enabled for `Private`, TCP 445, `LocalSubnet`, and `LanmanServer`; the helper never changes a network profile or global SMB policy | The host Owner principal remains infrastructure until later lifecycle work removes it; other pre-existing firewall rules remain the operator's responsibility and cause readiness failure when broad |
| Elevated subprocess hangs, floods output, or leaks diagnostics | Helper IPC is capped at 64 KiB; forward work has an independent two-minute lifetime, after which reverse recovery runs with individually bounded commands; each fixed PowerShell child has a 20-second timeout and one combined 16,384-character streaming budget, suppresses inherited module paths, and returns only typed states and bounded public errors | Recovery can extend elevation beyond the forward deadline but remains bounded by the fixed step set and child-command limits; local Windows tooling can still be unavailable or compromised; raw subprocess output is intentionally discarded |

## Trusted Circle remote design threats

These controls are implemented for one selected Anchor. Pure validation, protected
identity/invitation state, authenticated transport, atomic membership, and bounded audit retention
are executable.

| Threat | Required control | Residual risk / failure boundary |
| --- | --- | --- |
| One UUID, address, hostname, or Tailscale identity is mistaken for Circle identity | Circle identity is a durable ID plus current signed authority state; exact Circle context is in every signed admission | Object IDs remain non-secret references; transport metadata can reveal network relationships |
| Circle authority exists only as one ordinary Node key | Root, delegated Anchor, Member, Node, and transport roles use distinct keys; root authority requires explicit encrypted offline export | Loss of both accepted live authority and export is unrecoverable; multi-party recovery is deferred |
| Compromised transport certificate becomes Node, Member, or Circle authority | Transport SPKI has only a Circle-signed Node binding; other roles require separate keys/signatures | A live compromised transport key can impersonate that Node until revocation/expiry reaches the peer |
| Compromised Node becomes its Member | Admission requires independent Member and Node signatures over the same Circle-bound transcript | Malware controlling both keys inside one user session defeats this separation |
| Compromised Member becomes Circle/Anchor authority | Member authority is limited by signed roles/capabilities; invitations require an authorized current-generation issuer | A compromised authorized issuer can create invitations within its delegation until revoked |
| Compromised Anchor preserves old authority | Authority generations/sequences are monotonic; stale credentials and invitations reject after the accepted floor advances | Offline peers know only their newest verified authority state and cannot receive instant revocation |
| Forged or substituted invitation/admission fields | P-256/SHA-256 signatures cover fixed canonical bytes, role-scoped key IDs, the signed invitation digest, and all identity/context fields | Implementation bugs in canonicalization remain high impact; cross-platform golden vectors are mandatory |
| Captured invitation or admission is replayed | One-redemption invitation ID/nonce, exact package digest, persistent challenges, and one atomic invitation/member/node/response commit | Exact retry receives the stored response; conflicting request reuse rejects and survives restart |
| Invitation file is oversized, noncanonical, or leaks authority | Decode is capped at 16 KiB and requires exact canonical UTF-8 JSON; only public root/Anchor credentials, signed context, and a transport key pin are present | The bearer package can be copied by anyone who can read it until it expires or is consumed |
| Active peer forces an older protocol | Invitation, applicant, and Anchor authenticate version ranges and must select the highest common version | A vulnerability shared by the highest mutually supported version remains possible |
| Valid request is used for another Circle | Circle ID, invitation digest, authority generation, identities, ALPN, and endpoint role are signed and checked against receiver context | A compromised issuer authorized in both Circles remains separately accountable in each Circle |
| Valid Node transcript is presented over another TLS client | Exact presented certificate SPKI must match the signed proposed/active transport credential | Certificate wrapper validation still depends on correct time, key-use, name, and no-network policy |
| TLS chain validation silently fetches attacker-controlled AIA/CRL/OCSP | Admission uses an invitation SPKI pin; admitted peers use Circle-signed transport binding; revocation checking is local and no-fetch | X.509 is only a TLS key wrapper, so Circle revocation must remain application state |
| Unknown, revoked, wrong-Circle, or substituted peer reaches application code | Circle-root-signed Node/transport bindings are validated before exact mTLS SPKI matching; an encrypted mutual confirmation binds Circle, sender Node, and expected peer Node before channel exposure | Offline peers can enforce only their newest accepted revocation/generation state |
| TLS replay/early data repeats a state change | TLS 1.3 only, exact `balls-circle/1` ALPN, no early data, encrypted mutual confirmation, and duplicate operation-ID rejection within each bounded channel | Durable operations must retain operation IDs and atomic application replay checks across reconnects |
| A Node forges a Member's message or a Member attributes it to another Node | Member and Node independently sign the same canonical Circle/message/author/time/text transcript; the mTLS peer must equal the author Node and the persisted Member-to-Node binding must authorize it | Malware controlling both local private keys or the authoring user session can create messages as that local author |
| A valid message crosses Circles or is tampered in transit | Circle, Member, Node, timestamp, and bounded text are covered by both signatures; Circle context and TLS peer are checked before storage | Canonicalization defects remain high impact and require cross-platform contract tests |
| Retransmit, reconnect, or restart duplicates/reorders a message | Sender durably prepares one request identity; Anchor atomically stores canonical digest, unique per-Circle sequence, and signed receipt; exact retry returns stored receipt and conflicting reuse rejects | There is one selected ordering Anchor; multi-Anchor consistency and offline catch-up are deferred |
| A malicious peer sends malformed or oversized message data | Remote frame and `BMSG` envelopes are bounded before allocation, strict UTF-8 text is capped at 4,096 bytes, timestamps/UUIDs are canonical, and rejection is typed | The initial sequential listener has no per-source rate limit |
| A compromised Anchor rewrites accepted history | Sender validates an Anchor-signed receipt and both Nodes retain the same ordered local record | v1 has no independent transparency log, replicated consensus, or owner-facing audit comparison |
| Malformed, oversized, silent, or interrupted peer consumes unbounded resources | Fixed 28-byte frame headers, 64 KiB default payload cap before allocation, bounded operation count, 10-second handshake/I/O defaults, cancellation, and fail-closed interruption | The opt-in production listener is sequential; per-source rate limits and discovery policy remain later work |
| LAN address or provider metadata becomes authority | The LAN provider accepts only numeric private/loopback unicast endpoints and returns an untrusted stream; the remote layer authorizes signed Circle/Node credentials independently | Diagnostic endpoints still reveal network relationships and private addresses |
| Authority backup leaks or is restored ambiguously | Separate root/Anchor encrypted PKCS#8 values use the fixed PBES2 profile; the root signs exact Circle/generation/public-key/KDF/ciphertext-digest metadata; raw private keys are never logged or transmitted | Passphrase quality and custody remain operator responsibilities; import, rotation, restore UX, and secure deletion are separately gated |
| Recovery silently changes ownership | No ordinary Node self-promotion; recovery must prove possession of accepted authority material and advance signed generation | Without accepted recovery material, availability is lost rather than authority guessed |
| Revocation is claimed while peer is offline | Signed monotonic authority state and later maximum-staleness rules for privileged operations | Offline LAN behavior necessarily trades availability against revocation freshness |
| Validation differences leak cryptographic detail or mutate partial state | Pure validation returns one deterministic typed rejection before persistence; network errors may collapse detail | Local audit data must stay bounded and omit keys, transcripts, and signatures |

## Known limitations

- Local control has no bearer credential or per-operation authorization beyond same-account pipe
  access.
- The browser adapter is local HTTP rather than TLS. It relies on an IP-loopback-only listener,
  strict authority/origin checks, and Chromium's trustworthy-loopback handling of the Secure
  session cookie; it must not be exposed through a proxy or remote bind.
- Ordinary local records are not encrypted by Balls. Windows private keys are DPAPI-protected;
  Linux and initial macOS developer private keys rely on owned mode-restricted storage, with macOS
  also rejecting extended ACL grants. Operating-system disk encryption is a separate control.
- .NET 10 has macOS TLS 1.3 client support but no `SslStream` TLS 1.3 server support. The Mac
  joining-client path opts into Network.framework; a Mac Anchor/listener is unsupported rather
  than silently downgraded to TLS 1.2.
- Production Node/Circle/Anchor/Member/transport keys, invitation replay, public trust, signed
  membership receipts, and bounded admission audit outcomes are durable. `ballsd` opens the
  admission and message listeners only for explicit numeric private/loopback endpoints. The first
  message operation has dual-signature authorization, Anchor order, replay persistence, and bounded
  text; there is no discovery, owner revocation UX, credential rotation/import, remote audit
  replication, rich chat, multi-Anchor replication, or offline catch-up.
- The state marker and ACL are safety boundaries, not proof against an administrator, LocalSystem,
  physical access, or a compromised user session.
- Use the default LocalAppData, XDG, or macOS Application Support state root, or another dedicated
  current-user-controlled parent on a supported local filesystem. Cross-user-writable and network
  paths are unsupported.
- `CurrentUserOnly` authenticates the Windows account and elevation level, but the current pipe
  provider does not yet request Windows' remote-client rejection flag. A remote SMB session with
  the same Windows SID is inside the present account-level trust boundary.
- Circle and Node UUIDs remain non-secret object references. Durable role-scoped public
  credentials and the authenticated channel bind them to signing authority; only the persisted
  Anchor-signed receipt grants the joined relationship.
- Circle Files Contributions currently remain `defined`. A Windows host can create the dedicated owned
  folder/share/firewall resources, one limited account plus exact ACL per Access Grant, and an
  explicit current-user Explorer mapping after readiness and preview. It does not enable features
  or policy, change network profiles, expose or share the password between Members, activate/revoke a Contribution, or rotate credentials,
  adopt existing content, or prove remote/physical-machine access. The local Owner remains the host
  share principal until later lifecycle work. Recovery/rotation and remote Owner administration
  remain later trust boundaries.

## Required implementation for the next trust boundary

Before expanding Member access, keep the implemented capability-specific authorization IDs/proofs,
protected per-grant secret, exact helper/mapping ownership boundaries, and fail-closed rollback.
Add authenticated per-Member credential delivery, then ownership-proven rotation/revocation.
Recovery for lost or compromised devices and keys remains
required before claiming resilient authority.
Rich message content requires a new rendering/content review; multiple writers or Anchors require
an explicit synchronization and history-integrity design.

The Node-to-Node protocol must remain secure whether connectivity comes from LAN, Tailscale, or a
future provider.

See [`ADR 0006`](../decisions/0006-trusted-circle-identity-and-admission.md), the
[`remote Circle v1 contract`](../protocol/remote-circle-v1.md), and the
[`primary-source research`](../research/2026-08-20-trusted-circle-cryptography.md).
