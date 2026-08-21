# Remote Circle Security v1

**Status:** authenticated LAN transport and persisted single-Anchor admission implemented; durable
Circle messaging remains a later slice.

This contract is the authenticated security core for Node-to-Node Circle behavior. It is separate
from [`local-control v1`](local-control-v1.md), and it does not trust a LAN, Tailscale, DNS name,
IP address, or transport provider as identity.

## Layer boundary

```text
Circle operation
      ↓
remote v1 signed transcript
      ↓
TLS 1.3 + Circle credential binding
      ↓
IRemoteTransportConnector / IRemoteTransportListener
      ↓
LAN, Tailscale, or future provider byte stream
```

`IRemoteTransportConnector` and `IRemoteTransportListener` return an
`UntrustedRemoteConnection`: a duplex `Stream`, provider label, and diagnostic peer address. The
provider must not manufacture an authenticated Circle/Member/Node claim. TLS and remote v1 turn
the stream into an authenticated channel.

The interfaces live under `Balls.Protocol.Remote.V1` so local-control transports and remote
Circle providers cannot be substituted for each other accidentally. Provider implementations,
discovery, and retries remain replaceable; v1 now includes bounded framing and one explicit LAN
provider/listener composed into `ballsd` only when `--admission-listen <private-ip:port>` is selected.

## Constants

| Name | v1 value |
| --- | --- |
| Protocol version | `1` |
| Signature suite | `ecdsa-p256-sha256-p1363` |
| TLS version | exactly TLS 1.3 |
| ALPN | `balls-circle/1` |
| Public key | DER SubjectPublicKeyInfo for NIST P-256 |
| Key digest | SHA-256 |
| Signature | ECDSA/SHA-256, IEEE P1363 fixed 64 bytes |

An unknown suite, protocol major, or ALPN fails closed. Algorithm fields are descriptive and
authenticated; implementation selection comes from the protocol version's allow-list.

This contract is platform-independent, but implementation claims are not. .NET 10 supports the
exact TLS 1.3 `SslStream` path on macOS clients through opt-in Network.framework and does not
support a macOS TLS 1.3 server. The initial macOS developer Node keeps the constants above and is
not an Anchor/listener; TLS 1.2 fallback is not permitted. See
[`ADR 0007`](../decisions/0007-protected-macos-developer-node.md).

## Identifiers and credentials

Circle, Member, Node, invitation, and operation IDs are lowercase UUID strings and are not
authenticators. A `PublicKeyCredential` contains a role, algorithm, key ID, and DER SPKI. Its key
ID is:

```text
<role>:p256-sha256:<base64url-without-padding(SHA-256(DER-SPKI))>
```

Remote v1 recognizes `circle-authority`, `anchor`, `member`, `node`, and `transport`. The role is
included both in the credential and key ID. A credential is malformed if its algorithm, role,
key ID, curve, or canonical DER SPKI disagree.

Circle identity is the Circle ID plus its newest accepted signed authority state. That state
binds credential roles and monotonic authority generation. A UUID alone never authenticates an
entity.

## Canonical signed format

Every signed core starts with an operation-specific ASCII domain ending in NUL. Integers are
big-endian. Text and bytes are prefixed by an unsigned-shape 32-bit big-endian length. Signed text
is non-empty bounded ASCII; timestamps are signed 64-bit UTC Unix seconds. Arrays, maps, floats,
local times, duplicate fields, and implicit optional values do not occur.

Signed structures bound individual byte strings to 16 KiB and text to 1,024 ASCII characters.
Authenticated-channel framing applies its own limit before payload allocation.

### Invitation transcript

Domain: `balls/trusted-circle/invitation/v1\0`

Fields in exact order:

1. remote protocol version;
2. Circle ID;
3. invitation ID;
4. issuer ID;
5. issuer key ID;
6. Anchor transport key ID used for TLS bootstrap pinning;
7. authority generation;
8. not-before and expiry Unix seconds;
9. maximum redemptions, which is exactly one in v1;
10. minimum and maximum remote-protocol versions;
11. 32-byte invitation nonce.

The envelope adds the fixed signature suite and 64-byte issuer signature. The invitation digest
is SHA-256 over the domain `balls/trusted-circle/signed-invitation/v1\0`, length-prefixed canonical
invitation bytes, signature suite, and issuer signature. Admission signs that digest so neither
the invitation nor its signer can be substituted.

The implemented direct-exchange package is canonical UTF-8 JSON with format
`balls-circle-invitation`, version `1`, and a 16 KiB total limit. It carries the Circle root public
credential, a root-signed time-bounded Anchor delegation authorizing exactly
`issue-single-use-invitations`, and the Anchor-signed invitation. Property order, timestamp form,
base64 encoding, and every field are exact: decoding re-encodes and byte-compares the package, so
whitespace, reordered/extra/duplicate properties, or alternate encodings reject as `malformed`.
The package contains public credentials and signatures only—never private-key material, provider
credentials, discovery data, or an IP-address identity.

Issuance stores the exact package digest and expiry. Redemption first performs pure signature,
Circle, issuer authorization, generation, time, protocol, revocation, and canonical checks, then
atomically inserts one durable redemption result keyed by invitation ID. Concurrent or later use
returns `replayed`; digest substitution fails closed. Admission verifies this exact stored package
before any membership mutation.

### Node-transport binding transcript

Domain: `balls/trusted-circle/node-transport-binding/v1\0`

Fields in exact order:

1. remote protocol version;
2. Circle ID and Node ID;
3. authority generation;
4. complete transport credential;
5. not-before and expiry Unix seconds;
6. minimum and maximum remote-protocol versions.

The Circle root signs the binding with the fixed v1 suite. Validation checks structure, suite,
authority credential, signature, revocation, authority-generation floor, expected Circle and
Node, validity, and highest-common-version negotiation in deterministic order. A transport
certificate is only an X.509 wrapper for the bound SPKI.

### Admission-request transcript

Domain: `balls/trusted-circle/admission-request/v1\0`

Fields in exact order:

1. remote protocol version;
2. Circle and invitation IDs;
3. Member ID and complete Member credential;
4. Node ID and complete Node credential;
5. proposed transport credential;
6. applicant minimum, maximum, and selected versions;
7. signature suite and ALPN;
8. 32-byte signed-invitation digest;
9. 32-byte Anchor challenge;
10. 32-byte applicant challenge.

The Member key and Node key independently sign the identical transcript. The invitation digest
transitively binds Circle, issuer, authority generation, invitation constraints, server transport
pin, and invitation nonce. The Anchor challenge prevents a captured applicant request from being
used in another handshake; persistent invitation state prevents replay across restarts.

### Admission-response transcript

Domain: `balls/trusted-circle/admission-response/v1\0`

The Anchor-signed response binds the protocol/Circle/invitation, monotonic authority
generation/sequence, Circle name and creation time, admitted Member/Node credentials, granted
`member` role and sorted capabilities, the admitted root-signed transport binding, exact signed
request digest, both challenges, and sorted Member/Node snapshots. Every Node snapshot carries its
Node signing credential and canonical root-signed transport binding. The joiner validates the
Anchor signature, request binding, roster shape, admitted identities, version, revocation floor,
and every transport binding before one local commit.

## Version negotiation

An invitation, applicant, and Anchor each have authenticated inclusive `[minimum, maximum]`
ranges. The selected version is:

```text
min(invitation.maximum, applicant.maximum, anchor.maximum)
```

and must be greater than or equal to all three minima. Any other selection is `downgraded`, even
if both peers still implement that older version. Breaking signed-format or semantic changes use
a new major protocol version rather than an additive guess.

## Admission channel

1. A directly exchanged signed invitation supplies the Circle/Anchor context and exact server
   transport key pin.
2. The applicant connects through any provider and creates an `SslStream` client fixed to TLS
   1.3 and `balls-circle/1`.
3. The client validates certificate time, server-auth use, expected DNS name, and SPKI-derived key
   ID against the invitation. Ordinary public-chain trust does not replace the pin.
4. The applicant is not yet a trusted Node. It presents a self-wrapped proposed transport
   certificate only as TLS proof of possession; it receives no Circle authority from X.509.
5. The applicant sends the exact invitation package and names, receives the persistent Anchor
   challenge, and independently signs one transcript with its retry-stable Member key and local
   Node key. The transcript's transport SPKI must equal the presented TLS certificate.
6. The Anchor validates without mutation, root-signs the Node transport bindings, Anchor-signs the
   complete roster response, then atomically consumes the invitation and commits membership plus
   the exact response. An identical request receives those stored bytes; conflicting reuse rejects.
7. The joiner validates and atomically stores the roster, public root/Anchor trust, local Member
   credential, Node credentials/bindings, and exact signed receipt. It stores no private Circle
   authority and cannot redefine the Anchor.

After admission, connections require a client certificate at handshake start. Each peer validates
certificate time/use and binds the exact SPKI to an active Circle-signed Node transport credential.
TLS early data is not used for admission or any durable or authority-bearing mutation.

## Admitted-peer channel

An admitted peer channel is established in this order:

1. the selected provider returns an untrusted duplex stream and diagnostic endpoint metadata;
2. each side validates its expected peer's Circle-root-signed Node-transport binding;
3. `SslStream` negotiates exactly TLS 1.3 and `balls-circle/1`, with mutual certificates whose
   SPKIs exactly match those bindings;
4. before exposing the channel, both peers exchange a fixed 56-byte encrypted confirmation:
   `BCH1`, protocol version, Circle UUID, sender Node UUID, and expected peer Node UUID;
5. any mismatch closes the stream as `authentication_failed`.

The confirmation makes both peers finish explicit Circle/Node authorization and binds their
opposite expectations inside the live TLS session. Certificate DNS validation remains a wrapper
integrity check; DNS, IP address, port, interface, and provider label grant no authority.

Application frames use a fixed 28-byte header followed by a bounded payload:

| Offset | Size | Value |
| --- | ---: | --- |
| 0 | 4 | ASCII `BRF1` |
| 4 | 4 | big-endian protocol version |
| 8 | 16 | big-endian operation UUID |
| 24 | 4 | big-endian payload length |

Defaults are a 64 KiB payload maximum, 4,096 received operation IDs per channel, a 10-second
handshake timeout, and a 10-second I/O timeout. The receiver rejects a duplicate operation ID
within the channel, unsupported version, malformed header, oversized length, operation-count
exhaustion, timeout, or interrupted frame before returning application data. Durable operations
must also enforce replay/idempotency at their state transaction; a new TLS session does not erase
that application responsibility.

## LAN TCP provider

`Balls.Transport.Lan` implements the provider seam as `lan-tcp-v1`. It accepts only numeric
IPv4/IPv6 loopback, RFC 1918, IPv4 link-local, IPv6 unique-local, or IPv6 link-local unicast
endpoints. DNS names, wildcard, multicast, public, missing, and out-of-range endpoints reject.
Connect attempts are bounded and external cancellation remains distinguishable from timeout.
Listener disposal is idempotent and cancels pending accepts.

The provider never parses, creates, or authorizes a Circle or Node identity. It does not expose
the local-control named pipe/Unix socket or the loopback browser adapter. The only listener in
this slice is the explicit remote transport listener used by the process/lab harness; `ballsd`
does not route local-control or browser operations through it.

## Validation and rejection

Validation returns one deterministic typed result and performs no state mutation. Checks run in
this order so identical inputs and context produce the same result:

1. bounded canonical structure and credential self-consistency;
2. supported signature suite and ALPN;
3. issuer authorization for the authority generation;
4. issuer, Member, and Node signatures plus invitation digest;
5. credential and invitation revocation;
6. authority-generation freshness;
7. expected Circle;
8. presented TLS transport credential/Node binding;
9. Anchor challenge;
10. validity window;
11. invitation consumption/replay state;
12. highest-common-version negotiation.

| Code | Meaning |
| --- | --- |
| `malformed` | Structure, size, key shape, or required field is invalid |
| `unsupported_suite` | Signature suite or ALPN is not remote v1 |
| `unauthorized_issuer` | Invitation signer is not authorized in the referenced generation |
| `forged` | A signature, digest, or authenticated challenge does not verify |
| `revoked` | A credential or invitation is revoked |
| `stale_authority_state` | Invitation generation precedes the accepted authority floor |
| `wrong_circle` | Signed request, invitation, and receiving Circle context do not agree |
| `wrong_node` | Presented TLS transport SPKI is not the proposed/active Node binding |
| `not_yet_valid` | Invitation validity has not begun |
| `expired` | Invitation expiry is reached |
| `replayed` | Invitation is already consumed or its operation/transcript was seen |
| `downgraded` | No common version exists or the highest common version was not selected |

The public network error may intentionally reveal less detail, but local code and security audit
events retain the typed reason without credential bytes or signature material. A failed result
must occur before invitation, authority, membership, or message state changes.

## Recovery and revocation boundary

- Circle authority export is explicit, encrypted PKCS#8 plus authenticated metadata. Raw private
  keys never cross this protocol.
- Losing both live authority and its accepted export is unrecoverable; no Node self-promotes.
- Transport, Node, Member, Anchor, and root revocations rotate distinct credentials and advance
  signed monotonic authority state as appropriate.
- Offline peers enforce only the newest authority state they possess. Privileged operations will
  require an explicit maximum-staleness policy; remote v1 does not promise instant offline
  revocation.
- Invitation consumption and resulting admission commit atomically. An identical retry may return
  the prior response; it cannot create another membership.

## Executable evidence

`AdmissionSecurityTests` pin credential shape, fixed P1363 signatures, canonical invitation bytes,
dual-signed admission, highest-common-version selection, and deterministic unauthorized, revoked,
stale, forged, expired, replayed, downgraded, wrong-Circle, and wrong-Node rejection.

`AuthenticatedChannelSpikeTests` preserve the admission-bootstrap pin and baseline mutual-TLS
policy. `NodeTransportSecurityTests`, `RemoteAuthenticatedChannelTests`, and
`RemoteFrameTests` cover signed transport bindings, mutual peer confirmation, replay,
downgrade, revocation, wrong-Circle/Node, tamper, bounds, timeout, and interruption.
`Balls.Transport.Lan.Tests` exercises the numeric private-endpoint policy and real loopback TCP
I/O. `Balls.RemoteHarness.Tests` starts independent server/client processes on Windows and Linux;
the owned Hyper-V lab additionally runs the Windows client against the Ubuntu server on its
private switch.

`TrustedCircleAdmissionApplicationTests`, `AdmissionEndpointsTests`, CLI contract tests, and
SQLite admission tests prove the real TLS exchange, exact retry, atomic invitation/membership
commit, shared API/CLI/browser projection, restart-stable roster, no private authority on the
joiner, schema migrations, conflict/revocation/expiry outcomes, and 512-event audit retention.

## Explicit non-goals

No discovery, public bind, automatic failover, multiple-Anchor behavior, rich roles, message, file,
AI, or app operation is defined here. Admission listening is opt-in on a numeric private/loopback
endpoint; the invitation remains directly exchanged. Credential rotation/import and owner-facing
revocation UX remain separate work.
