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
| Full local gate | Passed on `35a7c974cd79e984b9ced60ea4a24f48a41a2c80` in 111 seconds: 203 .NET tests, 9 browser-component tests, and 1 installed-browser journey passed |

## Environment evidence

The owned `Balls.Lab.Ubuntu` Hyper-V guest was reset to checkpoint `Balls.Lab.Clean` and reported
machine ID `498d59af31b04f34bb19722beab972aa` with clean Balls identity. Exact branch commit
`35a7c974cd79e984b9ced60ea4a24f48a41a2c80` was exported with `git archive` and published
self-contained for `linux-x64` outside the worktree. Host and guest SHA-256 values matched:

- `ballsd`: `d33220fa70d62ca8b6e84f3784ea6888c0a15ccc5faf0e7074241888b56d87cd`
- `ballsd.dll`: `824a193a89ddeac46752b7f3e132c392386cde17f09622101526b07be671ba19`
- `balls`: `454b53cc129bb1e05a7e50d66ee31a7bba2179aa18dbc67a5321c1d9686b9e81`
- `balls.dll`: `91252eea6262f714d64fe2a30a568241eefe3bbbdff061b266aa38bd9358aa98`

The physical Windows host and virtual Ubuntu 24.04 guest created distinct fresh Node identities,
then observed this exact journey over the Hyper-V private network:

| Observation | Result |
| --- | --- |
| Windows Anchor Node | `01a025a5-e28c-7eab-ac7c-6d3a96c4512b` |
| Ubuntu joining Node | `01a025a6-cad5-74de-993d-dacd3cd4790f` |
| Circle | invitation-pinned TLS admission produced the same two Members and Nodes on both sides |
| Message | Ubuntu CLI authored `0198c2d8-b000-7000-8000-000000000352`; both Nodes stored text `Hello from final Ubuntu VM.` at sequence `1` |
| Browser | headless installed Google Chrome loaded the real Windows `ballsd` loopback workspace and its DOM contained the Circle, `Messages`, text, `Bob · Ubuntu-VM`, and `#1` |
| Restart/retry | both daemon processes restarted with their original Node identities; exact send retry remained sequence `1` and one row on each Node |
| Cleanup | host state moved under the owned lab root; guest restored again to `Balls.Lab.Clean` and reported clean identity |

No second physical machine was used. The Windows side was the physical development laptop; the
Ubuntu side was an owned Hyper-V virtual machine.

## Green-main Canary evidence

Pull request [#54](https://github.com/scwlkr/balls/pull/54) squash-merged as exact commit
`f2a2d72678a872f60cb816a62d67b40778c3566c`. Its
[main CI run](https://github.com/scwlkr/balls/actions/runs/32516147187) passed Windows, Ubuntu,
macOS, Required, Linux Canary, and Windows Canary. Each Canary job built once from that accepted
commit, smoked the package it built, and uploaded it.

The workflow artifacts were downloaded and smoked again from fresh local state:

| Platform | Artifact | Downloaded ZIP evidence | Independent smoke |
| --- | --- | --- | --- |
| Physical Windows | `9459023271`, `balls-0.2.0-alpha.1-canary-windows-x64-f2a2d72678a8` | 18,948,121 bytes; SHA-256 `e5460bc535ed054a155586ff15694eebb910fa37b27ea8adfa1c0d1a92f4a06d` | checksum install, structured CLI/Circle, real Chrome UI, and restart passed; fresh Node `01a025b7-f3e4-757c-8fd1-e449cc7275b6` |
| Virtual Ubuntu | `9458982984`, `balls-0.2.0-alpha.1-canary-linux-x64-f2a2d72678a8` | 18,866,987 bytes; SHA-256 `eb8e43f4deaf68bca5a5c7d59fe77aa6a96c4c1f04d57b927442473e065af1d3` | outer and internal checksums, fresh install, structured CLI/Circle, real Chrome UI, socket cleanup, and restart passed; fresh Node `01a025bc-04f5-7117-abc7-4f6ba1c73768` |

The Ubuntu guest was restored again to `Balls.Lab.Clean`; it reported the expected machine ID, no
Balls state, and no installed browser. Windows smoke state and processes were also cleaned. The
[Issue #39 acceptance ledger](https://github.com/scwlkr/balls/issues/39#issuecomment-5374169896)
contains the same post-merge record. No tag or GitHub Release was created.

## Security boundary and non-goals

The remote listener is explicit, numeric-private/loopback, and serves exactly one admitted Circle
where the local Node is the selected Anchor. The message is nonblank strict UTF-8 capped at 4,096
bytes; UUIDs and timestamps are canonical; network/provider metadata grants no authority.

No channels, direct messages, edits, deletes, attachments, reactions, typing state, offline
multi-peer synchronization, discovery, automatic failover, or multiple-Anchor ordering is claimed.
No physical second machine was used.
