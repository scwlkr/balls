# Threat Model Starter

**Status:** Windows/Linux local Node baseline, protected production Node/Circle/transport identity
storage, bounded invitations, authenticated LAN transport, persisted two-Node admission, and one
durable signed Circle message operation, 2026-08-21.

## Scope

This baseline covers one unelevated Windows or Linux account running `ballsd`, the same-account
`balls` CLI, the local OS-IPC control API, the authenticated loopback browser adapter, the bundled
browser UI, and the daemon's SQLite state directory. It also covers the remote v1 identity,
authenticated-channel, admission, and persistent message boundaries. Executable loopback,
separate-process, and Windows-host/Ubuntu-VM tests cover transport, admission, message, and restart.

## Assets

- persistent local Node identity;
- Circle, Member, and Node records known to this daemon;
- integrity and availability of the local database;
- authority to issue local control requests as the current OS account;
- short-lived browser launch and session authority;
- Circle authority, delegated Anchor, Member, Node, and transport private keys;
- signed authority state, membership credentials, revocations, invitations, and admission
  transcripts;
- durable message identity, authorship, order, content, signed requests, and Anchor receipts;
- explicit encrypted Circle authority exports and their custody metadata.

## Trust boundaries

1. CLI or another same-account client to the daemon over a Windows named pipe or Linux Unix-domain socket.
2. Browser JavaScript to the daemon's separate IPv4 loopback HTTP adapter.
3. Daemon process to the dedicated local state directory and SQLite database.
4. OS account boundary to other local users and administrator/root authority.
5. An untrusted LAN, Tailscale, or future-provider stream to the TLS 1.3 remote Circle protocol.
6. Admission bootstrap from a signed invitation and pinned Anchor transport key to not-yet-trusted
   Member/Node credentials.
7. The selected Anchor's live authority state to explicit offline authority export/restore.
8. An admitted Member/Node pair to the selected Anchor's authoritative Circle-message commit.

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
| Malformed identifiers or names reach storage | Boundary validation, typed core identifiers, length limits, and parameterized SQL | Authorization beyond the Windows account is not implemented |

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
| A valid Member signs from a different admitted Node | Message requests require independent Member and Node signatures; the live mTLS Node must match the signed Node, and the Anchor checks the persisted Member/Node admission pair | Malware controlling both keys or the same user session defeats the separation until revocation |
| A message is altered, moved to another Circle, replayed, or reordered | Purpose-specific canonical transcripts bind Circle/message/Member/Node/text/time; the Anchor validates both signatures, assigns one monotonic sequence, signs the exact result, and persists request digest plus response atomically | The selected Anchor remains the ordering authority and can deny service; multiple-Anchor consensus is deferred |
| Oversized or hostile message content reaches rendering/storage | Request JSON is capped at 16 KiB; signed text is nonblank, control-filtered, and capped at 4 KiB UTF-8; React renders text without HTML interpretation | Rich content, links, attachments, and third-party rendering require a new review |
| Retry or crash creates duplicate/different messages | Sender persists a retry-stable draft before I/O; Anchor message UUID plus signed digest returns exact stored bytes or conflicts; sender commits the signed receipt atomically | Offline catch-up and reconciliation after a sender permanently loses its draft are deferred |
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
  Linux private keys rely on owned mode-restricted storage. Operating-system disk encryption is a
  separate control.
- Production Node/Circle/Anchor/Member/transport keys, invitation replay, public trust, signed
  membership receipts, bounded security audit outcomes, and minimal message history are durable.
  `ballsd` opens separate admission/message listeners only for explicit numeric private/loopback
  endpoints. There is no discovery, owner revocation UX, credential rotation/import, remote audit
  replication, offline catch-up, or multi-Anchor message replication.
- The state marker and ACL are safety boundaries, not proof against an administrator, LocalSystem,
  physical access, or a compromised user session.
- Use the default LocalAppData or XDG state root, or another dedicated current-user-controlled
  parent on a supported local filesystem. Cross-user-writable and network paths are unsupported.
- `CurrentUserOnly` authenticates the Windows account and elevation level, but the current pipe
  provider does not yet request Windows' remote-client rejection flag. A remote SMB session with
  the same Windows SID is inside the present account-level trust boundary.
- Circle and Node UUIDs remain non-secret object references. Durable role-scoped public
  credentials and the authenticated channel bind them to signing authority; only the persisted
  Anchor-signed receipt grants the joined relationship.

## Required implementation for the next trust boundary

Before Circle Files mutates Windows sharing state, define signed contribution/access grants,
least-privilege helper authorization, revocation, recovery, and provider-specific rollback. The
existing recovery/rotation boundary remains explicit for lost or compromised devices and keys.

The Node-to-Node protocol must remain secure whether connectivity comes from LAN, Tailscale, or a
future provider.

See [`ADR 0006`](../decisions/0006-trusted-circle-identity-and-admission.md), the
[`remote Circle v1 contract`](../protocol/remote-circle-v1.md), and the
[`primary-source research`](../research/2026-08-20-trusted-circle-cryptography.md).
