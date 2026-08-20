# Authenticated LAN transport verification — 2026-08-20

## Outcome

Issue #37 delivers a provider-neutral remote v1 channel that mutually authenticates two
Circle-authorized Nodes, encrypts their stream with exact TLS 1.3, confirms both peer identities
inside that session, and exchanges replay-aware bounded frames over one private-LAN TCP provider.
Network location is diagnostic only.

The slice does not open a production `ballsd` listener, expose local-control/browser behavior
remotely, create membership, or define a message payload. Admission persistence remains #38.

## Acceptance evidence

| Acceptance | Observed evidence |
| --- | --- |
| Provider-neutral seam | `IRemoteTransportConnector` and `IRemoteTransportListener` remain in remote protocol v1 and return only an `UntrustedRemoteConnection`. `Balls.Transport.Lan` depends only on `Balls.Protocol`; Core has no transport dependency. |
| Mutual Node authentication and encryption | Circle-root-signed, time-bounded Node/transport bindings are validated before exact TLS 1.3, `balls-circle/1` ALPN, and mutual certificate-SPKI matching. Both sides exchange an encrypted 56-byte Circle/sender/expected-peer confirmation before a channel is returned. |
| Location-independent authorization | The LAN provider accepts only explicit numeric private/loopback unicast endpoints. DNS, wildcard, multicast, and public endpoints reject. IP address, hostname, port, interface, and provider label never enter the signed binding or authorization decision. |
| Bounded reliable channel | A fixed 28-byte versioned header carries one operation UUID and a payload capped at 64 KiB by default before allocation. Handshake/I/O defaults are 10 seconds; receive history defaults to 4,096 operations; external cancellation is preserved; interrupted reads fail closed. |
| Fail-closed peers | Tests cover unknown credentials, revoked credentials, wrong Circle, wrong Node, forged/tampered bindings, future/expired bindings, stale authority, downgrade, unsupported versions, duplicate operation IDs, malformed/oversized frames, silent peers, interruption, and operation-count exhaustion. |
| Local trust boundary preserved | The new listener is a separate transport assembly and explicit engineering harness. It has no local-control route, named-pipe/Unix-socket adapter, browser route, or daemon composition. Existing architecture tests continue to enforce the local seams. |
| Cross-platform process proof | Independent Windows and Ubuntu processes each generated/loaded their own test certificate and signed binding, established the real channel, exchanged ping/pong frames, and returned the same Circle with opposite peer Node IDs. |

## Focused local verification

- Protocol suite on Ubuntu: 40 passed, 0 failed.
- LAN provider suite on Ubuntu and Windows: 20 passed, 0 failed on each platform.
- Architecture suite on Windows: 13 passed, 1 expected Linux-only skip, 0 failed.
- Separate-process harness on Windows and Ubuntu: 1 passed, 0 failed on each platform.
- Windows Release builds reported 0 warnings and 0 errors for the focused projects.

An initial Windows attempt to launch a freshly rebuilt protocol test assembly was blocked by the
owner's managed Application Control policy with
`An Application Control policy has blocked this file. (0x800711C7)`. No policy was weakened.
After the normal locked restore/build path, the relevant Windows architecture, LAN, and process
suites executed successfully.

## Owned Windows/Ubuntu lab gate

The owned lab inspection reported Hyper-V available, VM `Balls.Lab.Ubuntu` present under
`C:\BallsLab`, and machine identity `498d59af31b04f34bb19722beab972aa` with Balls identity
state `clean`. No reset was needed.

The exact harness source commit was
`e5c4f356c3dc0e51470e4b0fe19c6b83b1117038`. The Windows framework-dependent harness DLL SHA-256
was `75EC32702EF0D110F210CC614B5540A227C61E1E7B29519B2DE99B4CFC40B5ED`; the Linux
self-contained apphost SHA-256 was
`398E114E541CC1371173BA8C698F59984FC18BC6FBC593FC40052A311967D90D`.

The clean checkpoint did not contain a discoverable `.NET` runtime despite the seed's package
request. The gate therefore published the exact archived commit as a self-contained Linux
artifact outside tracked files rather than installing or changing the guest. The guest firewall
was observed inactive. A truly detached Ubuntu server then listened on the owned Default Switch;
the Windows client connected and both processes returned:

- provider `lan-tcp-v1`;
- the same Circle ID;
- opposite peer Node IDs;
- protocol version `1`;
- `encrypted: true`;
- client status `acknowledged` and server status `received`;
- empty server error output.

Only four exact guest paths below `/tmp/balls-remote-e5c4f35*` were removed after the run. The
two local generated PKCS#12-bearing configuration files were deleted and were not retained as
evidence. A final Identity check reported the same machine ID and Balls identity state `clean`.

## Repository full gate

The complete Windows full verifier passed after the SChannel fixture correction:

- locked restore, `dotnet format --verify-no-changes`, generated-client drift, Prettier, ESLint,
  TypeScript, and Release build all passed;
- Release build reported 0 warnings and 0 errors;
- 174 .NET tests passed with 11 expected platform skips;
- 4 Vitest component tests passed;
- 1 Playwright Chromium journey passed.
- NuGet transitive vulnerability audit reported no vulnerable package in every project; pnpm
  reported no known vulnerabilities.

The corrected TLS happy path then passed 10 consecutive focused Windows process runs.

The WSL verifier restored, formatted, and built the solution and passed all new Linux protocol,
LAN, and remote-harness suites. Its complete test phase was not counted as a full-gate pass:
two existing daemon/browser tests correctly rejected data directories on the Windows-mounted
DrvFS worktree as an unverified local filesystem. The protected Ubuntu runner's native filesystem
remains the authoritative complete Linux gate.

## Protected pull-request evidence

Pull request [#45](https://github.com/scwlkr/balls/pull/45) validated implementation head
`3fb673676238bec1b4507ea8177f005f64bf8390`:

- [Windows fast](https://github.com/scwlkr/balls/actions/runs/32413926013/job/96570581535)
  passed in 3 minutes 1 second;
- [Ubuntu fast](https://github.com/scwlkr/balls/actions/runs/32413926013/job/96570581247)
  passed in 2 minutes 6 seconds;
- [Required](https://github.com/scwlkr/balls/actions/runs/32413926013/job/96571475512)
  passed in 3 seconds after both platform lanes;
- [CodeQL C#](https://github.com/scwlkr/balls/actions/runs/32413926014/job/96570580692)
  passed in 2 minutes 10 seconds;
- [dependency review](https://github.com/scwlkr/balls/actions/runs/32413926040/job/96570580729)
  passed in 7 seconds.

There were no review comments, change requests, merge conflicts, dependency findings, or CodeQL
findings. The evidence-only final head receives the same protected checks before squash merge.

## Boundaries

- Duplicate operation IDs reject within one channel. Durable admission and message operations must
  also persist idempotency/replay state across reconnects.
- Revocation and authority-generation checks use the newest state the offline peer possesses;
  instant offline revocation is impossible.
- Test harness configuration contains temporary PKCS#12 material and is intentionally generated
  outside tracked files, permission-restricted on Linux, never logged, and deleted after use.
- Discovery, NAT traversal, Tailscale, public listening, relay, admission mutation, messaging
  semantics, and general remote administration remain out of scope.
