# Trusted Circle cryptography and authenticated-channel research

**Date:** 2026-08-20

**Scope:** Issue #33 research for the current `net10.0` Windows 11 and Ubuntu foundation. This is
not a protocol specification or an authorization to persist production keys.

## Finding

Use the platform-backed .NET 10 primitives that are already portable on the supported matrix:
ECDSA P-256 with SHA-256 for identity signatures, DER SubjectPublicKeyInfo (SPKI) for public keys,
encrypted PKCS#8 only for explicit private-key export, and TLS 1.3 through `SslStream` for the first
remote channel. Keep Circle, Anchor, Member, Node, and transport-certificate keys as distinct roles.

Do not select Ed25519 or application-level X25519 for the .NET 10 protocol. .NET 10 has no public
Ed25519 API, and the approved runtime API proposal remains assigned to .NET 11. The first public
`X25519DiffieHellman` API applies to .NET 11, not .NET 10. In contrast, .NET documents NIST P-256
ECDSA/ECDH support across Windows, Linux, macOS, Apple mobile, and Android, and exposes portable
`ECDsa.Create(ECCurve.NamedCurves.nistP256)` and `ECDiffieHellman.Create(...)` factories.
([Ed25519 proposal](https://github.com/dotnet/runtime/issues/63174),
[X25519 .NET 11 API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x25519diffiehellman?view=net-11.0),
[cross-platform cryptography](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography),
[P-256 API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.eccurve.namedcurves.nistp256?view=net-10.0))

This choice is replaceable: put algorithm identifiers in keys, signatures, credentials, and
protocol versions so a later version can add Ed25519/X25519 without reinterpreting version 1.

## Primitive choices

| Purpose                                     | .NET 10 choice                                                                   | Consequence                                                                                                                                                                            |
| ------------------------------------------- | -------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Identity and authority signatures           | ECDSA P-256 with SHA-256                                                         | Native Windows/Linux providers; explicitly request `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`, a fixed 64-byte `r \| s` representation, instead of relying on a provider default. |
| Public-key interchange                      | DER SPKI                                                                         | .NET imports/exports this standard representation directly; hash the complete DER SPKI, including algorithm parameters, for a typed key identifier.                                    |
| Private-key interchange                     | PKCS#8; encrypted PKCS#8 for backup                                              | Never put raw `ECParameters.D` or unencrypted PKCS#8 in protocol messages, logs, or routine state export. Password-based backup parameters and custody remain a separate decision.     |
| Key agreement, if later needed above TLS    | Ephemeral ECDH P-256, then HKDF-SHA-256                                          | .NET warns that the raw ECDH agreement is KDF input, not a traffic key. Include protocol, role, Circle, both peer keys, and transcript hash in HKDF salt/info.                         |
| Application AEAD, if later needed above TLS | AES-256-GCM with a 16-byte tag, or ChaCha20-Poly1305 after an availability check | Nonces must never repeat under one key. Keep independent send/receive keys and counters; a persisted counter requires crash-safe state. Do not add this layer merely to duplicate TLS. |

.NET's EC base APIs provide SPKI, PKCS#8, encrypted PKCS#8, and PEM import/export. Its ECDSA API
accepts an explicit signature format, and `IeeeP1363FixedFieldConcatenation` is fixed-size while the
RFC 3279 DER sequence is variable-size.
([EC key import/export](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecalgorithm?view=net-10.0),
[signature formats](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dsasignatureformat?view=net-10.0),
[ECDSA operations](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa?view=net-10.0),
[RFC 5480 EC SPKI](https://www.rfc-editor.org/rfc/rfc5480))

`ECDiffieHellman.DeriveRawSecretAgreement` explicitly warns callers to pass the result through a
KDF. .NET 10 has an RFC 5869 `HKDF` implementation. HKDF's `info` field is intended for application
and context binding, and its salt strengthens separation between uses. .NET also exposes AES-GCM
and ChaCha20-Poly1305; the latter's API requires a 96-bit nonce and 128-bit tag and warns that nonce
reuse with one key breaks its guarantees. AES-GCM has the same serious nonce-reuse boundary.
([raw ECDH agreement](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdiffiehellman.deriverawsecretagreement?view=net-10.0),
[HKDF API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hkdf?view=net-10.0),
[RFC 5869](https://www.rfc-editor.org/rfc/rfc5869),
[ChaCha20-Poly1305 API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.chacha20poly1305.encrypt?view=net-10.0),
[RFC 5116 AEAD nonce boundary](https://www.rfc-editor.org/rfc/rfc5116))

### Key roles and identifiers

- A Circle authority/root key defines Circle identity; an Anchor holds a separately identified
  delegated issuer key. The authority key must not exist only as a side effect of one Node.
- Each Member and Node has its own signing key. A Node compromise must not silently become a Member
  compromise, and neither key should be reused as a TLS session key.
- A replaceable transport certificate key is bound to a Node by an authority-signed credential.
  Rotating that certificate must not change the Node identifier.
- Define identifiers as a type and algorithm prefix plus a lowercase encoding of
  `SHA-256(DER-SPKI)`. Hashing only EC point bytes loses the algorithm/parameter binding.
- Never use signature bytes as an object identifier or replay key. Replay state keys on the
  invitation or operation identifier and signed content, not on one possible ECDSA encoding.

## Signed serialization and versioning

Do not sign ordinary `System.Text.Json` output. Property order can be influenced by declarations
and attributes, and ties in `JsonPropertyOrder` are explicitly undefined. Choose one byte-level
canonical transcript and test it with golden vectors on Windows and Linux.
([System.Text.Json ordering](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/customize-properties),
[`JsonPropertyOrder` tie behavior](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonpropertyorderattribute?view=net-10.0))

The smallest v1 format is a purpose-specific binary transcript: ASCII domain separator, unsigned
protocol major/minor integers, operation type, and fixed-order length-prefixed byte strings. Ban
floats, local-time strings, optional-field ambiguity, duplicate fields, and Unicode normalization
from the signed core. Encode timestamps as bounded UTC Unix seconds and durations as integers.

If interoperable JSON must itself be signed, implement the complete JSON Canonicalization Scheme
(JCS), not merely alphabetical property insertion. JCS recursively orders object properties and
defines number and string serialization. If CBOR is selected instead, name an exact RFC 8949
deterministic profile. The .NET `System.Formats.Cbor` package's mode named `Canonical` documents
the older RFC 7049 ordering, which is not automatically the RFC 8949 core deterministic profile.
([RFC 8785 JCS](https://www.rfc-editor.org/rfc/rfc8785),
[RFC 8949 deterministic CBOR](https://www.rfc-editor.org/rfc/rfc8949),
[.NET CBOR conformance modes](https://learn.microsoft.com/en-us/dotnet/api/system.formats.cbor.cborconformancemode?view=net-10.0-pp))

Every signature input should begin with a Balls-owned domain such as
`balls/trusted-circle/<operation>/v1\0`. Include the algorithm identifier in the envelope, but
derive verification behavior from the negotiated protocol version and an allow-list, never from an
attacker-controlled algorithm name alone. Major versions are incompatible; unknown majors fail
closed. A peer signs both offered-version lists and the selected version in the admission
transcript. Reject a selected version below either side's declared minimum, even if its syntax is
otherwise valid.

## Authenticated transport

### First channel: TCP plus TLS 1.3

Use `SslStream` rather than composing a record protocol from ECDH, HKDF, and AEAD. The .NET TLS
guidance recommends OS protocol/cipher selection by default so applications inherit platform
updates. If the Trusted Circle contract specifically requires a TLS 1.3 floor, set and verify that
floor deliberately and close the connection when the negotiated `SslProtocol` is not TLS 1.3.
Configure an exact ALPN value such as `balls-circle/1` and reject any mismatch.
([TLS/SSL best practices](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices),
[`SslStream` authenticated-channel behavior](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0))

Admission and ordinary member traffic have different trust states:

1. **Admission bootstrap:** the signed invitation carries the expected Circle/Anchor identity,
   endpoint constraints, and expected server transport SPKI digest. TLS authenticates the server
   by that pin. The not-yet-admitted client then proves possession of its proposed Node key in the
   application admission transcript. Do not pretend it is already a trusted mTLS client.
2. **Admitted traffic:** require a client certificate at the start of the connection. Validate its
   SPKI against the active, Circle-signed Node transport binding and validate Circle, Node,
   credential generation, validity, and revocation state before exposing application behavior.

.NET supports custom root trust and public-key pinning. A validation callback should ignore only
the intentionally replaced chain-trust decision; name, use, expiry, malformed certificate, and
binding errors still fail. SPKI is available as DER through `certificate.PublicKey`. Short-lived
self-signed transport certificates plus explicit Circle credentials keep authority visible;
a Circle private CA would simplify built-in mTLS chain validation but adds CA issuance, serial,
CRL/OCSP, clock, and recovery semantics that then become product authority.
([custom trust and pinning](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices),
[SPKI export from certificates](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.publickey?view=net-10.0),
[custom root trust](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.trustmode?view=net-10.0),
[ECDSA certificate creation](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterequest.-ctor?view=net-10.0))

Certificate authentication is negotiated for the connection, not per application message or HTTP
path. Request it at connection start; do not depend on post-handshake renegotiation. Configure
client-certificate chain behavior to avoid uncontrolled AIA, CRL, or OCSP network fetches becoming
an admission denial-of-service path.
([ASP.NET Core certificate-authentication boundary](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0),
[client-certificate validation considerations](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices))

TLS 1.3 provides a transcript-bound handshake, but its 0-RTT data has no cross-connection replay
guarantee. Do not permit admission, invitation redemption, authority changes, revocation, or message
creation in early data. .NET 10 does not document a public `SslStream` TLS-exporter API, so v1
should bind application authentication to the observed certificate SPKI and its own signed
nonces/transcript rather than assuming RFC 9266 channel-binding access. Revisit this only after an
executable platform spike.
([TLS 1.3 replay analysis](https://www.rfc-editor.org/rfc/rfc9846),
[TLS 1.3 exporter channel binding](https://www.rfc-editor.org/rfc/rfc9266),
[.NET 10 `SslStream` API surface](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0))

### QUIC

Keep QUIC as another transport provider, not the v1 authority protocol. `System.Net.Quic` is stable
from .NET 9 and uses the same SSL authentication option types, TLS 1.3, and multiplexed streams.
It also uses UDP, can be blocked by network equipment, and requires a separately installed
`libmsquic` on Linux. The same frames, ALPN, certificate binding, authorization, replay rules, and
rejection codes should work over TCP/TLS or QUIC; no Circle authority may depend on a QUIC
connection ID or network address.
([QUIC in .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview),
[`System.Net.Quic` API](https://learn.microsoft.com/en-us/dotnet/api/system.net.quic?view=net-10.0),
[RFC 9000](https://www.rfc-editor.org/rfc/rfc9000),
[RFC 9001](https://www.rfc-editor.org/rfc/rfc9001))

## Admission transcript and deterministic rejection

The signed admission request should bind at least:

- domain and protocol version; invitation identifier and authority generation;
- Circle, issuer, applicant Member, and proposed Node identifiers;
- invitation constraints, issued/expiry times, and one random invitation nonce;
- applicant Node public key and applicant transport SPKI;
- server challenge and applicant challenge;
- both offered-version lists, selected version, endpoint role, and ALPN;
- a digest of the invitation bytes and all prior canonical transcript messages.

The authority-signed response additionally binds the assigned role/capabilities, admitted Node
credential, transport binding, monotonically increasing Circle authority sequence, and resulting
transcript digest. Verify canonical structure and bounded lengths before any signature work.

Required deterministic outcomes follow from those bindings:

| Input                                                                                                                           | Required result                                                                            |
| ------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Bad structure, unknown major, unknown algorithm, or non-canonical bytes                                                         | Reject before state mutation.                                                              |
| Signature invalid or signer not authorized at the referenced authority generation                                               | `forged`/`unauthorized_issuer`; do not reveal finer cryptographic detail over the network. |
| Circle, Member, Node, transport SPKI, role, or ALPN mismatch                                                                    | Typed wrong-context rejection.                                                             |
| Expiry elapsed or `notBefore` too far in the future                                                                             | `expired`/`not_yet_valid`; local clock policy must be explicit.                            |
| Invitation already redeemed/revoked, nonce repeated, challenge missing, or transcript digest repeated under a different request | `replayed`; replay lookup uses invitation/operation identity, not signature bytes.         |
| Selected version below either authenticated minimum or absent from either authenticated offer                                   | `downgraded`.                                                                              |
| Authority sequence older than the local accepted sequence                                                                       | `stale_authority_state`.                                                                   |

Persist replay consumption and the resulting admission atomically. A retry after an uncertain
response may return the prior identical result, but must not create a second membership.

## Recovery and revocation implications

- **Authority recovery must be designed before keys ship.** If the only Circle authority private
  key is lost, cryptography cannot infer a replacement authority. Predeclare either an exportable
  offline Circle recovery key or a quorum/threshold rule. Do not silently promote an ordinary Node.
- **Backups are authority-bearing artifacts.** Export only through an explicit owner action, use
  encrypted PKCS#8, authenticate all metadata outside the encrypted blob, and test restoration on
  Windows and Linux. Password strength, KDF parameters, custody, and destruction need their own
  owner-visible policy.
- **Separate rotation scopes.** Transport-key compromise rotates the certificate binding; Node-key
  compromise revokes/replaces the Node credential; Member-key compromise revokes Member authority;
  Anchor-key compromise advances the Circle authority generation and revokes all credentials it
  could issue. These must not collapse into one key.
- **Invitation authority is bounded.** An invitation records issuer, Circle, one redemption count,
  expiry, roles/capabilities, and authority generation. Revoking an issuer or advancing authority
  invalidates its outstanding invitations according to an explicit rule.
- **Offline revocation is necessarily stale.** A disconnected peer can enforce only the newest
  signed authority state it possesses. Define a monotonic sequence/generation and a maximum stale
  window for privileged operations; do not claim instant revocation on an offline LAN.
- **X.509 revocation is not Circle revocation.** Platform CRL/OCSP behavior can require network
  access and differs by OS. Circle-signed membership and transport-binding revocations remain the
  application authority even when the certificate wrapper is otherwise valid.

The key APIs support encrypted PKCS#8 export/import, while .NET's X.509 guidance confirms that
chain building and revocation may perform external network work and vary by platform.
([encrypted PKCS#8 APIs](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecalgorithm?view=net-10.0),
[cross-platform X.509 behavior](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography),
[chain/revocation policy](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chain.chainpolicy?view=net-10.0))

## Uncertainties to retire with the issue #33 spike

1. Prove P-256 key generation, SPKI/PKCS#8 round trips, fixed-format signatures, malformed-signature
   rejection, raw ECDH plus HKDF vectors, and key disposal on exact Windows and Ubuntu runtimes.
2. Prove TLS 1.3 server authentication for an admission pin and post-admission mTLS with exact SPKI
   binding, ALPN, wrong-Circle/wrong-Node certificates, expiry, and restart behavior.
3. Confirm that certificate validation performs no unintended AIA/CRL/OCSP fetches in the chosen
   policy and fails closed when local authority state is unavailable or stale.
4. Produce cross-platform golden vectors for every signed transcript and all rejection cases.
5. Re-evaluate Ed25519/X25519 when Balls targets a stable .NET version that actually ships both
   public APIs. The current .NET 11 X25519 and Ed25519 work is pre-release and not a v1 dependency.
6. Treat OS-backed key storage, backup KDF parameters, recovery quorum, trusted-clock tolerance,
   authority rollback protection, and secure deletion as unresolved product/security decisions;
   this research does not make them disappear.

## Recommended ADR boundary

The smallest defensible ADR can select P-256/SHA-256, DER SPKI identifiers, P1363 signatures, the
canonical signed transcript, TCP/TLS 1.3 with invitation-pinned server bootstrap and later mTLS,
and monotonic Circle authority generations. It should explicitly defer production key persistence,
backup custody, quorum recovery, invitation redemption, a remote listener, QUIC, and application
AEAD to their own vertical issues.
