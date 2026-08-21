# Persistent Circle message verification

**Date:** 2026-08-21  
**Issue:** [#39](https://github.com/scwlkr/balls/issues/39)  
**Scope:** one bounded authored text message across two admitted Nodes

## Implemented outcome

- A joining Node durably prepares and independently Member/Node-signs one Circle-bound message.
- The selected Anchor authenticates the admitted Node with TLS 1.3 mTLS, authorizes the persisted
  Member-to-Node binding, assigns sequence `1`, signs the receipt, and commits it atomically.
- Exact retransmission returns the stored receipt without a second row or sequence. Conflicting
  request-ID reuse rejects, and both local histories remain identical after database reopen.
- Structured `balls message send/list` and the authenticated local browser message history use the
  same Core-owned persistence behavior.

## Automated evidence

Observed on the Windows development host from the issue branch:

| Gate | Observation |
| --- | --- |
| Protocol contract | 3 message security/codec tests passed; malformed, tampered, wrong-Circle, wrong-Node, unauthorized, unsupported-suite, and oversized inputs reject |
| SQLite contract | 2 message state tests passed; stable preparation, conflicting reuse, order, exact retry, and reopen persistence passed |
| Two-Node application | 1 real loopback TLS admission/message journey passed; retransmit remained one row on each Node and reopen histories matched |
| CLI contract | 2 focused tests passed, including two daemons, invitation/admission, `message send`, and identical `message list` output |
| Browser component | 9 tests passed; authenticated Circle history renders text, Member/Node attribution, and Anchor sequence |
| Compile/type gate | Release solution build and TypeScript typecheck passed with no build warnings |
| Warm local fast gate | Passed in 94.73 seconds; functionally green but above the 60-second feedback target on this host |

## Environment evidence

The Windows-host/Ubuntu-VM message journey and exact post-merge Windows/Linux Canary identities
will be appended before issue acceptance. Until then, this record does not claim cross-host or
packaged-product completion.

## Security boundary and non-goals

The remote listener is explicit, numeric-private/loopback, and serves exactly one admitted Circle
where the local Node is the selected Anchor. The message is nonblank strict UTF-8 capped at 4,096
bytes; UUIDs and timestamps are canonical; network/provider metadata grants no authority.

No channels, direct messages, edits, deletes, attachments, reactions, typing state, offline
multi-peer synchronization, discovery, automatic failover, or multiple-Anchor ordering is claimed.
No physical second machine was used by the automated evidence above.
