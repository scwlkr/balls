# ADR 0006 — Authenticate Trusted Circle Identity and Admission

- **Status:** Accepted
- **Date:** 2026-08-20
- **Issue:** [#33](https://github.com/scwlkr/balls/issues/33)

## Context

The local-control v1 UUIDs identify stored objects but do not authenticate a Circle, Member, or
Node. Trusted Circle must establish remote identity before invitation redemption, a listener,
shared membership, or messaging ships. The design must work on the supported .NET 10 Windows and
Linux runtimes, remain independent of LAN or Tailscale, and avoid making one ordinary Node the
Circle's irreplaceable definition.

The supporting [primary-source research](../research/2026-08-20-trusted-circle-cryptography.md)
found that .NET 10 exposes portable platform-backed P-256, SHA-256, SPKI, PKCS#8, and `SslStream`
TLS 1.3 APIs. It does not expose stable public Ed25519 or X25519 APIs. The
[executable spike](../../tests/Balls.Protocol.Tests/AdmissionSecurityTests.cs) also found that
Windows SChannel requires an ECDSA certificate loaded through a supported key-store path rather
than an ephemeral in-memory private key.

## Decision

### Identity and key roles

Keep durable object identifiers and cryptographic credentials separate:

- Circle, Member, Node, invitation, and operation IDs are canonical lowercase UUID strings. They
  are stable references, never proof of identity.
- A Circle identity is its Circle ID plus the current signed Circle authority state. The authority
  state names the root credential, authority generation, delegated Anchors, Members, Nodes,
  transport bindings, and revocations.
- The Circle authority/root, each Anchor issuer, each Member, each Node, and each transport
  certificate have distinct keys. A compromise or rotation in one role does not silently grant
  another role.
- A Node installation may retain one Node signing identity while each Circle issues its own
  membership and transport binding. Every signed operation includes the Circle ID, preventing a
  valid credential from becoming authority in another Circle.

Remote v1 uses ECDSA P-256 with SHA-256. Public keys are DER SubjectPublicKeyInfo. Signatures use
`DSASignatureFormat.IeeeP1363FixedFieldConcatenation`, producing a fixed 64-byte `r | s` value.
Key identifiers are:

```text
<role>:p256-sha256:<base64url-without-padding(SHA-256(DER-SPKI))>
```

The allowed roles are `circle-authority`, `anchor`, `member`, `node`, and `transport`. Algorithms
are selected by the authenticated protocol version and an allow-list, not by an untrusted name.
Ed25519, X25519, certificate-chain PKI, and application encryption above TLS are deferred.

### Authority and invitation issuance

The Circle root signs authority state and can delegate bounded invitation authority to an Anchor.
An invitation names the Circle, issuer and issuer key, authority generation, one Anchor transport
key pin, validity window, supported remote-protocol range, one redemption, and a random nonce.
Only an issuer authorized in the named current authority generation may sign it.

One selected Anchor is authoritative in v1, but the Circle root is not the Anchor's Node key and
must be explicitly exportable. An Anchor compromise advances authority generation and invalidates
the affected delegation and credentials according to signed revocation state. An ordinary Node is
never silently promoted to Circle authority.

### Canonical signed bytes

Do not sign ordinary JSON. Each remote v1 signature covers a purpose-specific byte transcript:

1. an ASCII domain separator ending in NUL;
2. the protocol version and operation discriminator;
3. fixed-order, big-endian signed integers;
4. bounded ASCII text and byte strings prefixed by a big-endian 32-bit length;
5. UTC timestamps represented as Unix seconds.

Remote v1 rejects empty, oversized, non-ASCII, structurally ambiguous, or unknown-major signed
inputs before mutation. Golden-vector tests pin the byte format on Windows and Linux. An outer
inspectable envelope may use another encoding later, but it cannot redefine the signed core.

### Version negotiation and admission transcript

The invitation, applicant, and Anchor each authenticate their minimum and maximum compatible
remote-protocol versions. The selected version is the highest common version. A missing common
version, a selected version outside any authenticated range, or a lower selected version is a
`downgraded` rejection.

The applicant's admission request is signed independently by the Member and Node keys and binds:

- protocol version, signature suite, endpoint role, and exact `balls-circle/1` ALPN;
- Circle, invitation, issuer, authority-generation, Member, and Node identities;
- Member, Node, and proposed transport credentials;
- a digest of the complete signed invitation;
- invitation, Anchor, and applicant challenges/nonces;
- all offered version ranges and the selected version.

The authoritative response will additionally bind the granted role/capabilities, resulting Member
and Node credentials, transport binding, monotonic authority sequence, and transcript digest.
Issue #36 owns atomic invitation consumption and issue #38 owns persisted admission; this ADR does
not implement either mutation.

### Peer authentication and transport

Remote protocol and transport stay separate from local-control v1:

```text
remote Circle behavior
        ↓
signed remote v1 frames
        ↓
TLS 1.3 identity binding
        ↓
untrusted byte stream
        ↓
LAN / Tailscale / future provider
```

The transport connector/listener returns only an untrusted duplex stream and provider metadata.
It cannot assert Circle, Member, or Node identity. The protocol layer applies TLS and application
credentials.

Admission uses TCP plus `SslStream` TLS 1.3. The client pins the server transport key named by the
signed invitation, checks certificate validity, server-auth use, DNS name, and ALPN, and presents
no already-trusted client certificate. The applicant then proves its proposed Member and Node keys
in the signed admission transcript.

After admission, both peers present transport certificates. The receiver binds the exact
certificate SPKI to an active Circle-signed Node transport credential before exposing Circle
behavior. Certificate chain or IP/hostname identity never substitutes for that binding. Early
data is forbidden for admission, invitation redemption, authority/revocation changes, and durable
message creation.

### Deterministic rejection and mutation boundary

Remote v1 validation is pure and fail-closed. It returns one typed result before persistence or
authorization. Evaluation order is: structure, suite/ALPN, issuer authority, all signatures and
credential/digest bindings, revocation, authority freshness, Circle context, transport/Node
context, challenges, time window, replay state, then version negotiation.

Required public codes are `forged`, `expired`, `replayed`, `downgraded`, `wrong_circle`, and
`wrong_node`. The protocol also defines `malformed`, `unsupported_suite`, `unauthorized_issuer`,
`revoked`, `stale_authority_state`, and `not_yet_valid`. Network responses may collapse detailed
cryptographic failures to a bounded public message while retaining the typed local audit result.

Replay state keys on the invitation/operation identifier and transcript digest, never ECDSA
signature bytes. Consuming an invitation and persisting the admission must be one atomic durable
operation. A retry of the same transcript may return the prior result but cannot create a second
membership.

### Recovery, backup, and revocation

The Circle authority private key requires an explicit encrypted offline export. The implemented v1
envelope stores separate PBES2-encrypted root and Anchor PKCS#8 values (AES-256-CBC,
PBKDF2-HMAC-SHA256, 600,000 iterations) and a root-signed exact manifest covering Circle,
generation, public credentials, profile, and ciphertext digests. Windows live state uses
current-user DPAPI; Linux uses verified owned `0700`/`0600` state. Import, rotation, custody UX,
and secure deletion remain separately gated. Raw private-key bytes never appear in protocol
messages, logs, or ordinary state exports.

If both live authority and its accepted export are lost, the Circle is cryptographically
unrecoverable. Recovery cannot infer a new owner or silently promote a Node. Root rotation and
multi-party recovery remain future versioned authority operations.

Revocation is signed, monotonic authority state. Transport, Node, Member, Anchor, and Circle-root
compromise have distinct rotation scopes. Offline peers can enforce only their newest verified
authority state, so privileged operations must later define a maximum stale window; v1 does not
claim instant offline revocation.

## Consequences

- Supported Windows and Linux use only platform-backed .NET 10 primitives.
- Key rotation does not change durable object IDs, and a network address never becomes identity.
- A stable signed transcript and rejection vocabulary can be implemented before persistence or a
  listener.
- TLS removes the need to design a custom encrypted record layer while preserving transport
  replacement below it.
- Invitation redemption, remote framing/listening, recovery UX/import, key rotation, and messaging
  remain independently reviewable slices.

## Rejected alternatives

- **Ed25519/X25519 in remote v1:** not public stable .NET 10 APIs on the supported matrix.
- **Signing ordinary JSON:** property, string, and numeric representations are not a complete
  canonical signature format.
- **Using one key for Circle, Anchor, Member, Node, and TLS:** collapses compromise and rotation
  boundaries.
- **IP, DNS, Tailscale identity, or a public CA as Circle identity:** confuses reachability or
  certificate issuance with Circle authority.
- **Custom ECDH/HKDF/AEAD transport:** duplicates TLS state machines and increases nonce, replay,
  downgrade, and interoperability risk without a v1 benefit.
- **Automatic Anchor promotion after key loss:** lets availability silently redefine authority.

## Non-goals

This decision does not redeem invitations, open a listener, persist production private keys,
select a hosted control plane, implement Circle-wide authorization, or exchange messages.
