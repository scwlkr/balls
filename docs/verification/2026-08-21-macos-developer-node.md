# macOS Developer Node Verification — 2026-08-21

## Outcome observed

An Apple M1 Max Mac composed the dedicated macOS host, protected fresh local state, communicated
over a private Unix-domain socket, ran separate `ballsd` and `balls` processes, opened the shared
browser workspace, created/listed a Circle, and retained identity and Circle state across restart.
No native GUI or remote browser-control surface was added.

## Environment

- Apple M1 Max, arm64, 64 GB
- macOS 27.0 prerelease
- .NET SDK 10.0.400 / runtime 10.0.11
- Node 24.18.0 and pnpm 11.19.0

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

## Honest gaps

- The clean GitHub-hosted `macos-26` result is pending the pull request.
- .NET 10 supports TLS 1.3 on macOS clients, but not `SslStream` servers. Mac-local TLS server,
  two-Node admission, and remote-harness tests are explicitly skipped; the protocol was not
  downgraded.
- Physical Mac-to-Windows invitation/join/message proof depends on #39 landing and remains required
  before calling macOS a full Trusted Circle developer Node.
- Canary packaging, launchd, signing/notarization, Keychain, Intel/universal binaries, and Stable
  support were not attempted.

See [ADR 0007](../decisions/0007-protected-macos-developer-node.md), the
[.NET 10 macOS TLS client documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#tls-13-for-macos-client),
and [Microsoft's macOS install support table](https://learn.microsoft.com/en-us/dotnet/core/install/macos).
