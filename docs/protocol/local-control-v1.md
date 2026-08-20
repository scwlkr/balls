# Local Control API v1

**Status:** implemented for Phase 1 Slice 1.

This is the versioned, machine-local contract between `balls` or another local integration and
`ballsd`. It is not a Node-to-Node or Circle replication protocol.

## Transport and access

- HTTP/1.1 with JSON over a Windows named pipe or Linux Unix-domain socket.
- The full control plane has no TCP route. The client's `http://localhost` base address is only
  the logical HTTP authority used over the local IPC connection. `ballsd` separately binds one
  ephemeral IPv4 loopback listener for the narrow browser adapter described below.
- Windows server and client use `CurrentUserOnly`; Linux requires an owned `0600` socket in an
  owned `0700` runtime directory. The operating-system user is the v1 local principal.
- The Windows default pipe is `balls-control-<hash>`, where `<hash>` is the lowercase hexadecimal
  encoding of the first eight bytes of SHA-256 over the current Windows SID. Linux uses the
  protected XDG runtime location or an effective-user fallback.
- `--pipe-name` selects an explicit pipe for development and testing.
- Maximum request body: 32 KiB. Default client timeout: 10 seconds.

The transports live in outer host adapters and never become Core dependencies. Future platforms
can supply equivalent local IPC without changing the HTTP/JSON product contract.

## Encoding and compatibility

- Base path: `/control/v1`.
- `protocolVersion` is the integer `1`.
- JSON properties use camel case. Input property names are case-insensitive.
- Unknown input properties are ignored so additive changes remain compatible.
- UUIDs are JSON strings. Timestamps are ISO 8601 `DateTimeOffset` strings with round-trip
  precision.
- Additive optional response fields may be introduced in v1. A breaking semantic or shape change
  requires a new versioned path.

An inspectable OpenAPI document is available over protected IPC at
`GET /control/v1/openapi.json`. Its committed counterpart is
[`local-control-v1.openapi.json`](local-control-v1.openapi.json); a daemon contract test detects
drift, and the browser client is deterministically generated from that exact file. The document
includes both the protected control routes and narrow browser projection, but the daemon enforces
their distinct transports at runtime.

## CLI output compatibility

`balls` is a client of this API. It does not read the database or recreate Circle behavior. Its
global syntax is:

```text
balls [--pipe-name <endpoint>] [--output text|json] <command>
```

Global options may appear in either order, exactly once, and only before the command. Command
options remain after their command operands. Unsupported output modes, duplicate options, missing
values, and misplaced options return usage exit code `2` without contacting `ballsd`.

Text is the default and is intended for people. `--output json` is supported for `status`,
`circle create`, `circle list`, `member list`, and `node list`. A successful JSON document is one
line on standard output:

```json
{
  "outputVersion": 1,
  "result": {
    "productVersion": "0.2.0-alpha.1",
    "protocolVersion": 1,
    "node": {
      "id": "0198f2cc-6a50-7a08-aacb-298f4ebdf616",
      "displayName": "WORKSTATION",
      "createdAtUtc": "2026-08-19T12:00:00.0000000+00:00"
    }
  }
}
```

Failures selected with a valid `--output json` produce one line on standard error:

```json
{
  "outputVersion": 1,
  "error": {
    "code": "circle_not_found",
    "message": "The requested Circle is not known to this Node."
  }
}
```

`outputVersion` is the CLI envelope version. Version 1 may gain additive fields; consumers must
ignore unknown fields. `result` is the typed local-control response for that command, not parsed
presentation text. Identifiers remain lowercase UUID strings, timestamps retain the protocol's
round-trip ISO 8601 representation, roles remain protocol strings, and arrays preserve the
daemon's stable creation/identifier order. Envelope and typed-result property order is stable and
covered by golden tests.

Success writes only standard output. Usage, unavailable-daemon, rejected-request, and unsupported-
platform failures write only standard error and retain exit codes `2`, `3`, `4`, and `5`. A valid
JSON mode carries stable CLI codes such as `usage_error`, `daemon_unavailable`,
`invalid_daemon_response`, and `request_rejected`; application rejection preserves the daemon's
local-control error code. An invalid `--output` value cannot select a format, so that usage error
uses the default text form.

`balls ui` is intentionally text-only. It requests a launch capability through protected IPC,
validates the returned loopback URL, opens the system browser, and prints no capability or session
material. Selecting JSON output for this interactive command is a usage error.

## Shared representations

| Type | Fields |
| --- | --- |
| Node | `id` (UUID), `displayName`, `createdAtUtc` |
| Circle summary | `id` (UUID), `name`, `createdAtUtc`, `memberCount`, `nodeCount` |
| Member | `id` (UUID), `displayName`, `role`, `joinedAtUtc` |
| Circle Node | `id` (UUID), `displayName`, `joinedAtUtc` |
| Circle details | `circle` (Circle summary), `members` (Member array), `nodes` (Circle Node array) |
| Error | `code`, `message` |

The only v1 Member role is the string `owner`.

## Endpoints

### `GET /control/v1/status`

Returns `200 OK` with the daemon version, protocol version, and persistent local Node:

```json
{
  "productVersion": "0.2.0-alpha.1",
  "protocolVersion": 1,
  "node": {
    "id": "0198f2cc-6a50-7a08-aacb-298f4ebdf616",
    "displayName": "WORKSTATION",
    "createdAtUtc": "2026-08-19T12:00:00.0000000+00:00"
  }
}
```

### `POST /control/v1/circles`

Creates a Circle, its Owner Member, and enrollment of the daemon's local Node in one transaction.

```json
{
  "requestId": "0198f2cc-6a50-7a08-aacb-298f4ebdf617",
  "name": "My Circle",
  "ownerDisplayName": "Sam"
}
```

All fields are required. `requestId` must be a UUID. Names are trimmed, must not be blank, and may
not exceed 100 characters.

On success, returns `201 Created`, a `Location` header for
`/control/v1/circles/{circleId}`, and Circle details. Repeating the same request ID with the same
normalized name, owner name, and local Node returns the original Circle without duplicates.
Reusing that ID for different input returns `409 Conflict` with
`creation_request_conflict`.

### `POST /control/v1/ui/launch`

Returns the ephemeral loopback URL and expiry for a one-minute, single-use browser launch
capability. The capability is in the URL fragment, never its query. This endpoint exists only on
protected local IPC; it is not reachable from the browser listener.

### Read endpoints

| Request | Success response |
| --- | --- |
| `GET /control/v1/circles` | `200` with `{ "circles": [Circle summary] }` |
| `GET /control/v1/circles/{circleId}` | `200` with Circle details |
| `GET /control/v1/circles/{circleId}/members` | `200` with `{ "circleId": "...", "members": [Member] }` |
| `GET /control/v1/circles/{circleId}/nodes` | `200` with `{ "circleId": "...", "nodes": [Circle Node] }` |

Lists are returned in stable creation/identifier order as defined by the local store. An unknown
Circle returns `404 Not Found`.

## Browser adapter

The browser listener serves the bundled production application and only these `/browser/v1`
routes: session exchange, status, Circle list/create, and Circle details. The browser control
plane is intentionally narrower than `/control/v1`; control routes return `404` on TCP and browser
routes return `404` over IPC.

`POST /browser/v1/session` exchanges the launch capability once. Success sets the
`__Host-balls-session` cookie with `HttpOnly`, `Secure`, `SameSite=Strict`, and `Path=/`, and returns
an antiforgery token that remains in JavaScript memory. The session expires after 30 minutes.
Every other browser route requires that cookie. State-changing routes additionally require the
exact loopback `Origin` and `X-Balls-Antiforgery` header.

All browser requests require the exact selected Host authority. Duplicate or hostile Host/Origin
values fail closed, no permissive CORS headers are emitted, request bodies are capped at 32 KiB,
and security responses use bounded public messages without reflecting credential material.

## Application errors

Handled application errors use this shape:

```json
{
  "code": "circle_not_found",
  "message": "The requested Circle is not known to this Node."
}
```

| HTTP status | Code |
| ---: | --- |
| 400 | `invalid_request_id` |
| 400 | `circle_name_required` |
| 400 | `circle_name_too_long` |
| 400 | `owner_display_name_required` |
| 400 | `owner_display_name_too_long` |
| 400 | `invalid_circle_id` |
| 404 | `circle_not_found` |
| 409 | `creation_request_conflict` |

Framework-level rejection, such as malformed JSON or a request rejected before endpoint handling,
is not guaranteed to use the application error shape.

## Explicit non-goals

v1 does not define invitation, admission, remote Node authentication, discovery, synchronization,
messaging, files, AI, apps, or transport-provider behavior.
