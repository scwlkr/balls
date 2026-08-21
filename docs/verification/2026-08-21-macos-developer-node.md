# macOS Developer Node Verification — 2026-08-21

## Outcome observed

An Apple M1 Max Mac composed the dedicated macOS host, protected fresh local state, communicated
over a private Unix-domain socket, ran separate `ballsd` and `balls` processes, opened the shared
browser workspace, created/listed a Circle, and retained identity and Circle state across restart.
It then joined a Circle anchored by a physical Windows laptop over the shared private LAN, authored
one persistent message, and retained the same Node identity, two-Node roster, and message after
both daemons restarted. No native GUI or remote browser-control surface was added.

## Environment

- Apple M1 Max, arm64, 64 GB
- macOS 27.0 prerelease
- .NET SDK 10.0.400 / runtime 10.0.11
- Node 24.18.0 and pnpm 11.19.0
- Physical Windows 11 ThinkPad at `192.168.4.115`
- Physical Mac at `192.168.4.112` on interface `en0`

macOS 27 is newer than Microsoft's currently documented .NET 10 support ceiling, so this is an
observed developer-machine result, not an official macOS 27 support claim. Required clean CI uses
the fixed Apple-Silicon `macos-26` image.

## Automated evidence

- `Balls.Platform.MacOS.Tests`: 8 passed, including actual ownership/mode/ACL, local-APFS path,
  protected identity, live/stale socket, and host-default checks.
- Architecture composition: 14 passed, 2 other-platform skips.
- Separate daemon/CLI create/list/restart: 2 passed.
- Browser security and daemon isolation focus: 6 passed, 4 pre-existing Windows-only skips.
- Repository `fast` and `full`: passed locked restore, format, generated-client drift, Release
  build with zero warnings/errors, portable and macOS OS-integration tests, web
  lint/typecheck/component/build, and Playwright launch/create/list/restart (1 passed).
- Pull request #50 merged as `f0a9530cec977c5fb2cc100cfb562170642c2450`; fixed Apple-Silicon
  `macos-26`, Windows, Ubuntu, dependency review, CodeQL, and fail-closed `Required` passed.

## Physical private-LAN evidence

Both machines ran exact protected-main source
`d2daa44b0367ab39a73298924b7b22dccc7bc8ab` and reported product version `0.3.0-alpha.1`.
Tailscale SSH carried only orchestration and the bounded invitation file. Balls admission and
message traffic used the physical private-LAN endpoint `192.168.4.115`, not a Tailscale address.

| Observation | Result |
| --- | --- |
| Windows Anchor | Node `01a02651-fb17-7e75-8ae5-0d7eac725b34`; admission `192.168.4.115:46481`; messages `192.168.4.115:46482` |
| macOS joiner | Node `01a02653-c094-78cb-8214-7989192695b0`; owned `0700` state and runtime directories; owned `0600` control socket |
| Circle admission | Circle `01a02653-22dd-798d-94da-b2b1119061d5` showed two Members and the same two Nodes on both machines |
| Persistent message | Mac-authored message `01a02654-1aaf-746a-805b-7124fc32a066`, text `Mac-to-Windows-private-LAN-proof`, appeared exactly once at Anchor sequence `1` in both histories |
| Restart | Both daemons stopped and restarted from the same state; both Node IDs, the Circle, two-Member/two-Node roster, message ID, text, timestamps, and sequence remained unchanged |
| Network boundary | The Windows firewall allowance was temporary and restricted to local `192.168.4.115`, remote `192.168.4.112`, and TCP `46481-46482`; it and the proof listeners were removed afterward |

The bounded invitation and Windows proof state were deleted after observation. The Mac proof state
was moved to Trash for recoverability. The physical risk gate for a macOS joining developer Node
is complete.

## Honest boundary

- .NET 10 supports TLS 1.3 on macOS clients, but not `SslStream` servers. The proven Mac role is a
  joining client; a Mac Anchor/listener remains unsupported, and the protocol was not downgraded.
- Canary packaging, launchd, signing/notarization, Keychain, Intel/universal binaries, and Stable
  support were not attempted.

See [ADR 0007](../decisions/0007-protected-macos-developer-node.md), the
[.NET 10 macOS TLS client documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#tls-13-for-macos-client),
and [Microsoft's macOS install support table](https://learn.microsoft.com/en-us/dotnet/core/install/macos).
