# Trusted Circle Security Design Verification

**Date:** 2026-08-20  
**Issue:** [#33](https://github.com/scwlkr/balls/issues/33)  
**Scope:** identity/admission decision and executable security spike; no production keys, remote
listener, invitation redemption, or durable admission.

## Acceptance evidence

| Acceptance check | Observed evidence |
| --- | --- |
| Circle/Member/Node/Anchor/invitation threat model | The [threat model](../security/threat-model.md) now covers separated key roles, issuer delegation, compromise scopes, canonical signatures, replay, downgrade, wrong-context/confused-deputy defense, recovery, revocation, offline staleness, and authority export/loss. |
| Mainstream .NET crypto/channel evaluation | The [primary-source research](../research/2026-08-20-trusted-circle-cryptography.md) selects supported .NET 10 P-256/SHA-256/SPKI/PKCS#8 and `SslStream` TLS 1.3; it rejects unavailable Ed25519/X25519 and a custom encrypted transport. |
| Recorded security decision | [ADR 0006](../decisions/0006-trusted-circle-identity-and-admission.md) selects object/key identifiers, key roles/types, P1363 signatures, canonical serialization, version negotiation, admission transcript, invitation-pinned bootstrap, admitted-peer mTLS, rejection order, recovery, and revocation boundaries. |
| Typed protocol/provider seams | `Balls.Protocol.Remote.V1` contains typed credentials, invitation/admission records, pure validation, TLS policy, deterministic results, and connector/listener seams that return untrusted streams. The [remote v1 contract](../protocol/remote-circle-v1.md) keeps them separate from local-control v1 and provider identity. |
| Deterministic required rejection | One parameterized contract test validates two identical results for forged, expired, replayed, downgraded, wrong-Circle, and wrong-Node inputs. |
| Canon reconciled | Architecture, glossary, decisions, threat model, protocol/docs index, roadmap, detailed files-first program, and current state link the accepted design and the #35 frontier. |

## Executable observations

### Focused Windows contract run

Command:

```text
dotnet run --project eng/Balls.Verify --configuration Release -- focused --project tests/Balls.Protocol.Tests/Balls.Protocol.Tests.csproj --filter "FullyQualifiedName~AdmissionSecurityTests|FullyQualifiedName~AuthenticatedChannelSpikeTests"
```

Observed on .NET 10 Windows:

- 11 passed, 0 failed, 0 skipped;
- P-256 DER SPKI credentials and role-scoped SHA-256 key IDs round-tripped;
- ECDSA P1363 signatures were exactly 64 bytes and verified;
- one fixed public key produced the committed canonical invitation digest golden vector;
- valid dual-signed admission selected the highest common protocol version;
- forged, expired, replayed, downgraded, wrong-Circle, and wrong-Node inputs returned their exact
  deterministic typed rejection twice;
- a wrong invitation server pin was rejected;
- a real loopback `SslStream` handshake negotiated TLS 1.3, exact `balls-circle/1` ALPN, and
  mutually bound ECDSA transport credentials.

The first Windows TLS attempt also produced useful negative evidence: SChannel rejected an
in-memory ephemeral ECDSA server certificate with `AuthenticationException` / "the platform does
not support ephemeral keys." Loading the generated test certificate through PKCS#12 with a user
key-store-backed private key passed. This changes no system policy and reinforces that issue #35
must own an explicit platform key-storage boundary.

### Local fast gate

The repository `fast` command observed:

- locked restore, .NET format verification, generated-client drift check, web format, Release
  build, lint, and typecheck passed;
- Release build completed with 0 warnings and 0 errors;
- category audit found no unclassified tests;
- all selected .NET unit/contract/process tests passed (including 15 protocol tests), with only
  the repository's expected platform skips;
- four Vitest component tests and the production web build passed;
- the final existing Playwright browser journey could not start because the owner's managed
  Application Control policy blocked the newly built unsigned `Balls.BrowserHarness.dll` with
  `0x800711C7`.

No policy was weakened. The browser harness is unrelated to the protocol-only product change; the
required clean hosted Windows and Ubuntu `fast` lanes remain the complete pull-request gate.

## Hosted pull-request evidence

PR [#42](https://github.com/scwlkr/balls/pull/42) first evaluated implementation/documentation
commit `882c0b00ea76bfbf51dfa062974eba8519496c92`:

- [Windows fast](https://github.com/scwlkr/balls/actions/runs/32400945171/job/96528731404):
  passed in 3m23s, including the TLS 1.3 contract and existing Playwright journey;
- [Ubuntu fast](https://github.com/scwlkr/balls/actions/runs/32400945171/job/96528731057):
  passed in 1m56s with the same protocol contract;
- [Required](https://github.com/scwlkr/balls/actions/runs/32400945171/job/96529786509):
  passed its fail-closed platform decision in 5s;
- [dependency review](https://github.com/scwlkr/balls/actions/runs/32400945093/job/96528730741):
  passed in 7s;
- [CodeQL C#](https://github.com/scwlkr/balls/actions/runs/32400944955/job/96528729909):
  passed in 2m10s.

The final documentation-only evidence commit is subject to the same required pull-request checks
before merge.

## Non-goals observed

The diff does not open a socket in product code, redeem or persist an invitation, mutate Circle
membership, persist a production private key, select a hosted control plane, or implement remote
messaging. The only listener is an ephemeral loopback socket inside the TLS contract test.
