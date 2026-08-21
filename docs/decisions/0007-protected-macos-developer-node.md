# ADR 0007 — Add a Protected Apple-Silicon macOS Developer Node

- **Status:** Accepted for source-run development
- **Date:** 2026-08-21

## Context

Balls development now uses a Windows laptop and an Apple-Silicon Mac. The product already chose
one browser UI served by native `ballsd`, so macOS does not require a Swift/AppKit/SwiftUI app.
Reusing Linux host code would be unsafe: Linux ownership and local-filesystem checks depend on
Linux-only APIs and `/proc`, while macOS has different ACL, mount, temporary-path, and Unix-socket
constraints.

## Decision

Add dedicated `Balls.Platform.MacOS` and `Balls.Security.MacOS` adapters for Apple Silicon:

- default durable state is `~/Library/Application Support/Balls`;
- state must be current-user-owned, local APFS, normalized, unlinked, marked, `0700` for
  directories, `0600` for known files, and free of extended ACL grants;
- private material uses persisted scheme `macos-owned-state-v1`, relying on that verified
  directory/file boundary rather than implying Keychain encryption;
- local control is HTTP/JSON over an owned `0600` Unix-domain socket inside a private runtime
  directory, with a 103-byte path ceiling and safe live/stale cleanup;
- `balls ui` opens the existing loopback-only React workspace with `/usr/bin/open`;
- POSIX termination handling applies to macOS; and
- a fixed Apple-Silicon `macos-26` fast lane joins Windows and Ubuntu in `Required`.

Source-run local daemon, CLI, browser, persistence, and joining-client development are the first
claim. LaunchAgents, installers, signing, notarization, Canary packaging, Keychain migration,
Intel/universal binaries, and a native GUI are separate outcomes.

## Exact remote-TLS boundary

Remote v1 remains exact TLS 1.3. .NET 10 exposes TLS 1.3 through `SslStream` on macOS clients only,
using an opt-in Apple Network.framework implementation. Balls enables that client path. The .NET
10 macOS server path cannot satisfy remote v1, so this checkpoint does not claim a macOS Anchor or
incoming remote listener and does not relax the protocol to TLS 1.2.

Mac Anchor/listener support requires a proven TLS 1.3 server implementation or a future transport
provider that preserves the same signed Circle/Node/transport binding. The required physical
Mac-to-Windows private-LAN join/message observation completed on 2026-08-21 and is recorded in the
[macOS developer Node evidence](../verification/2026-08-21-macos-developer-node.md).

## Consequences

The Mac can immediately develop and verify portable code, macOS state/IPC, the shared browser UI,
interactions, and brand work without forking product behavior. GitHub required CI prevents those
paths from drifting. Support claims remain narrower than build claims: Apple Silicon is first,
macOS 26 is the CI target, and newer preview macOS observations are evidence rather than a Stable
support promise.
