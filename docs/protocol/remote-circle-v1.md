# Remote Circle Security v1

**Status:** accepted security core and executable spike; no listener, admission mutation, or remote
application frame ships yet.

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
discovery, retries, framing, and a listener are later issues.

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

The current spike bounds individual byte strings to 16 KiB and text to 1,024 ASCII characters.
Production framing will impose a tighter total message limit before allocation and signature work.

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
returns `replayed`; digest substitution fails closed. This local application slice prepares the
operation for the remote transport/admission transaction in #37 and #38.

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

The later admission response must be Anchor-signed and bind the granted role/capabilities,
resulting credentials and transport binding, authority generation/sequence, both challenges, and
the full request/response transcript digest.

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
4. The applicant is not yet a trusted Node and presents no trusted client certificate. It proves
   Member and Node key possession in the application admission transcript.
5. The Anchor validates without mutation, then atomically consumes the invitation and commits the
   membership/response in the later admission implementation.

After admission, connections require a client certificate at handshake start. Each peer validates
certificate time/use and binds the exact SPKI to an active Circle-signed Node transport credential.
TLS early data is not used for admission or any durable or authority-bearing mutation.

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
dual-signed admission, highest-common-version selection, and deterministic forged, expired,
replayed, downgraded, wrong-Circle, and wrong-Node rejection.

`AuthenticatedChannelSpikeTests` exercise the configured policy through a real loopback TLS 1.3
mutual-auth handshake with exact SPKI and ALPN binding, and prove that admission bootstrap rejects
a server key not pinned by the invitation. The Windows test loads generated test certificates
through PKCS#12 because SChannel rejects ephemeral server keys. Production Node/Circle signing
keys are now persisted through the protected Core-owned storage boundary from #35; remote
transport-credential issuance and rotation remain part of the admission/transport slices.

## Explicit non-goals

No remote listener is opened, hosted control plane selected, authority response committed, remote
application frame exchanged, membership created, or message stored. Invitation redemption is a
local durable request/result until the transport and admission slices make the remote protocol live.
