# Threat Model Starter

**Status:** Windows/Linux local Node baseline, 2026-08-19. Review before any remote listener, invitation,
Node-to-Node transport, or credential storage is added.

## Scope

This baseline covers one unelevated Windows or Linux account running `ballsd`, the same-account
`balls` CLI, the local OS-IPC control API, the authenticated loopback browser adapter, the bundled
browser UI, and the daemon's SQLite state directory. It does not claim to secure Circle membership
or traffic between machines; those paths do not exist yet.

## Assets

- persistent local Node identity;
- Circle, Member, and Node records known to this daemon;
- integrity and availability of the local database;
- authority to issue local control requests as the current OS account;
- short-lived browser launch and session authority;
- future Circle credentials and invitation material, once introduced.

## Trust boundaries

1. CLI or another same-account client to the daemon over a Windows named pipe or Linux Unix-domain socket.
2. Browser JavaScript to the daemon's separate IPv4 loopback HTTP adapter.
3. Daemon process to the dedicated local state directory and SQLite database.
4. OS account boundary to other local users and administrator/root authority.
5. Future Node-to-Node traffic across LAN or overlay transports. This boundary is deferred.

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
| Stale or substituted Linux socket | The adapter removes only a disconnected socket owned by the effective user, refuses live/non-socket/foreign-owned entries, applies `0600` after bind, and removes the socket during orderly shutdown | A killed daemon leaves a stale socket until the next validated startup; same-user races remain possible |
| Wrong, newer, incomplete, or corrupt SQLite state is opened | Application ID, schema version, exact schema shape, integrity, and foreign-key checks fail closed | Recovery and backup tooling do not yet exist |
| Two daemons write the same state | An exclusive `ballsd.lock` lease permits one daemon owner | A crash can leave the file, but the OS releases the lease |
| Oversized local or browser request consumes resources | Kestrel limits request bodies to 32 KiB; the IPC client limits buffered responses to 256 KiB | Same-user denial of service is not comprehensively addressed |
| Duplicate Circle creation after retry | A caller-provided request UUID makes creation idempotent; conflicting reuse is rejected | The UUID is not authentication or a general replay defense |
| Malformed identifiers or names reach storage | Boundary validation, typed core identifiers, length limits, and parameterized SQL | Authorization beyond the Windows account is not implemented |

## Known limitations

- Local control has no bearer credential or per-operation authorization beyond same-account pipe
  access.
- The browser adapter is local HTTP rather than TLS. It relies on an IP-loopback-only listener,
  strict authority/origin checks, and Chromium's trustworthy-loopback handling of the Secure
  session cookie; it must not be exposed through a proxy or remote bind.
- Local state is not encrypted by Balls. Operating-system disk encryption is a separate control.
- There is no remote protocol, mutual Node authentication, invitation flow, revocation, audit log,
  or message security yet.
- The state marker and ACL are safety boundaries, not proof against an administrator, LocalSystem,
  physical access, or a compromised user session.
- Use the default LocalAppData or XDG state root, or another dedicated current-user-controlled
  parent on a supported local filesystem. Cross-user-writable and network paths are unsupported.
- `CurrentUserOnly` authenticates the Windows account and elevation level, but the current pipe
  provider does not yet request Windows' remote-client rejection flag. A remote SMB session with
  the same Windows SID is inside the present account-level trust boundary.
- Circle and Node UUIDs in Slice 1 are persistent identifiers, not cryptographic identities.

## Required work for the next trust boundary

Before two machines exchange Circle state, define and test:

- cryptographic Node and Circle identity and protected key storage;
- invitation issuance, expiry, single-use behavior, admission, and revocation;
- mutual peer authentication and Circle authorization independent of transport provider;
- replay, downgrade, tampering, and confused-deputy defenses;
- encrypted transport, peer identity binding, and auditable security events;
- recovery behavior for lost devices, keys, and durable Nodes.

The Node-to-Node protocol must remain secure whether connectivity comes from LAN, Tailscale, or a
future provider.
