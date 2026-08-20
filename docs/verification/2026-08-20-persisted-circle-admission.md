# Persisted Circle Admission Verification

**Date:** 2026-08-20  
**Issue:** [#38](https://github.com/scwlkr/balls/issues/38)  
**Status:** implementation verified locally; exact PR/CI and cross-host evidence appended before merge

## Outcome

A second native Node can use a directly exchanged invitation to join one Circle through the
Anchor's explicit private-LAN listener. The exchange pins the invitation's transport SPKI in TLS
1.3, proves distinct Member and Node keys over one transcript, and returns an Anchor-signed roster
whose Node transports are root-signed. Both Nodes persist the same Circle, two Members, and two
Nodes. Restart does not regenerate identities or duplicate membership.

The selected Anchor remains authoritative in v1. The joiner stores public root/Anchor trust and the
exact signed receipt; it receives no private Circle authority and cannot export or promote itself.
Explicit encrypted authority export remains the backup boundary before relying on that Anchor.

## Acceptance evidence

| Criterion | Observed evidence |
| --- | --- |
| Explicit retryable admission with bounded failure states | Persistent applicant/Anchor challenges and Member identity; exact request lookup returns stored response bytes; protocol tests reject unauthorized, revoked, stale, forged, expired, replayed, downgraded, wrong-Circle, and wrong-Node inputs deterministically |
| Atomic Anchor membership | SQLite schema v4 commits invitation consumption, Member, Node, role, Member/Node/transport credentials, root-signed transport binding, authority sequence, signed response, and audit outcome in one transaction |
| Signed joiner membership without master redefinition | Joiner validates the Anchor signature and every root-signed transport binding, then atomically stores public trust, roster, local Member credential, all Node security state, and exact receipt; `GetCircleAuthorityAsync` remains null |
| One behavior through CLI/API/browser | `balls circle join`, `POST /control/v1/circles/join`, Circle reads, and the existing browser Circle-details projection consume the same `CircleApplication`/SQLite state; Member role mapping includes `member` |
| Restart stability | Direct two-store application test closes and reopens both databases with the same two Member IDs, two Node IDs, and no duplicate rows; Windows local-control and CLI tests expose the same roster on both daemons |
| Replay/conflict/revocation/expiry | Exact local retry returns the persisted Circle; conflicting applicant/request digests reject; storage commits return typed replay/revoked/expired outcomes; security audit retains only the newest 512 outcomes per Circle |
| Authority backup defined first | The pre-existing root-signed encrypted authority envelope is documented as the v1 recovery boundary; no automatic Anchor failover, private-authority transfer, or ordinary-Node promotion was added |

## Local verification observed

- Windows Release protocol contract: 53 passed.
- Windows Release SQLite contract: 32 passed.
- Windows Release daemon contract: 30 passed, 1 unsupported-host test skipped.
- Windows Release CLI contract: 14 passed, 1 unsupported-host test skipped.
- Real loopback TLS admission passed through the application, daemon local API, and CLI.
- OpenAPI was regenerated from the running daemon and its drift test passed.

## Cross-host and protected-branch evidence

To be appended for the exact review head:

- Windows host to owned Ubuntu VM admission and clean restart;
- Ubuntu portable verification for the exact commit;
- Windows and Ubuntu pull-request CI, Required, CodeQL, and dependency review;
- squash commit and issue evidence URL.

## Explicit non-goals retained

No automatic failover, multiple-Anchor consensus, rich roles, public discovery, Tailscale provider,
credential import/rotation UX, persistent message, Circle Files, AI, or Apps behavior was added.
The admission listener is opt-in and rejects public/hostname endpoints.
