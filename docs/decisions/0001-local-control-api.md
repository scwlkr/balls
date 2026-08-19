# ADR 0001 — Versioned HTTP/JSON over Local OS IPC

- Status: accepted for CLI/local integration control; refined for browser UI by ADR 0004
- Date: 2026-08-19

## Context

`balls` and future local interfaces need a typed, versioned API to `ballsd`.
The first calls are small unary requests. They do not yet require streaming,
cross-language code generation, or remote transport. A TCP listener would add
port discovery and local-authentication work and could accidentally blur the
local control API with the future Node-to-Node Circle protocol.

## Decision

Use HTTP/1.1 with JSON DTOs and explicit `/control/v1` routes, implemented with
ASP.NET Core Minimal APIs. On Windows, Kestrel listens on a named pipe limited
to the same Windows user. The CLI connects with `HttpClient` over that pipe.
No TCP listener exposes the full local-control API. ADR 0004 separately permits a narrow,
authenticated, loopback-only browser adapter.

The contract and transport remain separate. A future Linux/macOS adapter can
carry the same HTTP contract over a protected Unix-domain socket. The remote
Circle protocol is a separate decision.

Host composition is explicit: `Balls.Platform` defines typed client and server transport seams,
`Balls.Platform.Windows` implements them with the current same-user named pipe, and the centralized
`Balls.Host` selector supplies the same selected host to both executables. `ballsd` and `balls` do
not inspect the OS or construct Windows transport types. Until a real Linux adapter is registered,
selection returns one typed unsupported-host result and both executables fail closed.

## Why not gRPC now

gRPC remains viable when measured requirements justify Protobuf generation,
HTTP/2, streaming, or broader language interoperability. Adding it before
those requirements would not remove the need for named-pipe or Unix-socket
transport configuration and would make the first contract harder to inspect.

## Consequences

- Protocol DTOs and stable error codes require deliberate compatibility tests.
- OpenAPI documents the v1 HTTP surface.
- Windows named-pipe setup and client connection code live in the Windows
  adapter, outside Core.
- Adding Unix-domain sockets requires a real host adapter and selector registration; it does not
  require copying daemon routes, CLI behavior, or local-control v1 DTOs.
- Same-user processes are inside the local-control trust boundary for this
  slice. Sensitive future operations may require stronger per-operation
  authorization even over protected local IPC.
- Changing the transport must not change Core use cases or v1 message shapes.
