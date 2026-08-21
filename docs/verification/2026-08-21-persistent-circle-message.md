# Persistent Circle Message Verification

**Date:** 2026-08-21  
**Issue:** [#39](https://github.com/scwlkr/balls/issues/39)  
**Status:** implementation and local acceptance verified; Windows/Ubuntu risk gate and protected-branch evidence pending

## Outcome

One admitted Member/Node can author a bounded text message on its joined Node, send it to the
selected Anchor over the authenticated remote Circle channel, and observe the same durable result
on both Nodes. The message keeps its stable UUID, Member and Node authorship, authoritative Circle
sequence, text, and timestamps after both daemons restart.

This is deliberately the smallest messaging proof. The selected Anchor remains the single order
authority; there are no channels, direct messages, edits, deletion, attachments, reactions,
typing state, offline catch-up, or rich composer.

## Acceptance evidence

| Criterion | Observed evidence |
| --- | --- |
| Smallest versioned contract | Remote v1 defines a purpose-specific fixed-order binary transcript for Circle/message/Member/Node credentials, bounded UTF-8 text, authored UTC milliseconds, and dual fixed P1363 signatures; the Anchor receipt binds authoritative positive sequence, accepted time, and exact signed-request digest |
| Core-owned durable behavior | `IMessageStateStore` owns retry-stable draft, signing, authoritative idempotency/commit, replicated commit, and ordered read ports; the daemon, structured CLI, local API, and read-only browser projection consume that behavior |
| Authenticated and authorized remote operation | A separate opt-in `--circle-listen` uses admitted-peer TLS 1.3/mTLS; validation binds the live peer Node to its root-signed transport credential, requires the exact admitted Member/Node pair, and verifies independent Member/Node credentials and signatures |
| Fail-closed inputs | Protocol tests reject malformed/noncanonical, unsupported, unauthorized, revoked, forged/tampered, wrong-Circle, wrong-Node, stale, oversized, replayed, and conflicting inputs before state mutation; wire envelopes are capped at 16 KiB and text at 4,096 UTF-8 bytes |
| Idempotent authoritative order | Schema v5 stores retry-stable local drafts plus unique message UUID and Circle/sequence rows. The Anchor serializes sequence assignment and atomically stores request digest, message, and exact signed response; exact retry returns stored bytes and conflicting reuse rejects |
| Restart-stable common projection | A real two-daemon CLI test performs invitation/admission/message, verifies both JSON lists have the same identity/authorship/order/content/timestamps, restarts both daemons, and rechecks the same durable result |
| Minimal product UI | The browser reads the existing application projection only and renders accessible durable history without a composer. Styling uses the indigo, charcoal, pale-gray palette and connected-node visual language from `balls-brand.png` |

## Local verification observed

- Release build succeeded with zero warnings.
- Circle message protocol/security tests passed.
- SQLite schema/migration/message tests passed.
- Real Windows two-daemon CLI admission/message/restart acceptance passed.
- Browser unit, type, lint, formatting, and generated-OpenAPI checks passed.
- Complete repository fast/full results will be appended from the final documented gate.

## Windows-host/Ubuntu-VM risk gate

Pending on the exact final branch commit. The required journey will use clean namespaced state on
the owned Ubuntu VM and Windows host, then prove invitation, admission, message exchange, browser
and CLI observation, cold restart, exact identity/authorship/order/content retention, cleanup, and
an identity-clean lab report. Virtual coverage will be recorded explicitly; no physical second
machine is claimed.

## Canary evidence

Pending the exact merged commit. Windows and Linux runnable Canaries must be downloaded from the
same successful protected-main run, checksum-verified, and smoked without rebuilding.

## Protected-branch evidence

To be appended for the exact review head: Windows fast, Ubuntu fast, Required, CodeQL, dependency
review, squash commit, issue evidence URL, and exact main Canary run.

## Explicit non-goals retained

No channels, direct messages, edits, deletion, attachments, reactions, typing indicators, offline
multi-peer sync, rich chat UI, public discovery, Tailscale provider, automatic Anchor failover,
multiple-Anchor consensus, credential rotation/import UX, Circle Files, AI, or Apps behavior was
added. The message listener is opt-in, numeric private/loopback only, and v1 serves exactly one
locally authoritative Circle.
