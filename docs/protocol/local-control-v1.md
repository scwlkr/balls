# Local Control API v1

**Status:** implemented for Phase 1 Slice 1.

This is the versioned, machine-local contract between `balls` or another local integration and
`ballsd`. It is not a Node-to-Node or Circle replication protocol.

## Transport and access

- HTTP/1.1 with JSON over a Windows named pipe.
- No TCP listener is configured. The client's `http://localhost` base address is only the logical
  HTTP authority used over the pipe connection.
- Server and client use `CurrentUserOnly`; the Windows account is the v1 local principal.
- The default pipe is `balls-control-<hash>`, where `<hash>` is the lowercase hexadecimal encoding
  of the first eight bytes of SHA-256 over the current Windows SID.
- `--pipe-name` selects an explicit pipe for development and testing.
- Maximum request body: 32 KiB. Default client timeout: 10 seconds.

The transport lives in an outer Windows host adapter and never becomes a Core dependency. Future
platforms can supply equivalent local IPC without changing the HTTP/JSON product contract.

## Encoding and compatibility

- Base path: `/control/v1`.
- `protocolVersion` is the integer `1`.
- JSON properties use camel case. Input property names are case-insensitive.
- Unknown input properties are ignored so additive changes remain compatible.
- UUIDs are JSON strings. Timestamps are ISO 8601 `DateTimeOffset` strings with round-trip
  precision.
- Additive optional response fields may be introduced in v1. A breaking semantic or shape change
  requires a new versioned path.

An inspectable OpenAPI document is available at `GET /control/v1/openapi.json`.

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
  "productVersion": "0.1.0-alpha.2",
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

### Read endpoints

| Request | Success response |
| --- | --- |
| `GET /control/v1/circles` | `200` with `{ "circles": [Circle summary] }` |
| `GET /control/v1/circles/{circleId}` | `200` with Circle details |
| `GET /control/v1/circles/{circleId}/members` | `200` with `{ "circleId": "...", "members": [Member] }` |
| `GET /control/v1/circles/{circleId}/nodes` | `200` with `{ "circleId": "...", "nodes": [Circle Node] }` |

Lists are returned in stable creation/identifier order as defined by the local store. An unknown
Circle returns `404 Not Found`.

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
