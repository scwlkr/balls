# Local Control API v1

**Status:** implemented through provider-neutral Circle Files contribution and Access Grant
definition.

This is the versioned, machine-local contract between `balls` or another local integration and
`ballsd`. It is not a Node-to-Node or Circle replication protocol.

## Transport and access

- HTTP/1.1 with JSON over a Windows named pipe or Linux/macOS Unix-domain socket.
- The full control plane has no TCP route. The client's `http://localhost` base address is only
  the logical HTTP authority used over the local IPC connection. `ballsd` separately binds one
  ephemeral IPv4 loopback listener for the narrow browser adapter described below.
- Windows server and client use `CurrentUserOnly`; Linux and macOS require an owned `0600` socket
  in an owned `0700` runtime directory. The operating-system user is the v1 local principal.
- The Windows default pipe is `balls-control-<hash>`, where `<hash>` is the lowercase hexadecimal
  encoding of the first eight bytes of SHA-256 over the current Windows SID. Linux uses the
  protected XDG runtime location or an effective-user fallback. macOS uses a short directory below
  the current user's canonical private temporary location.
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
`circle create`, `circle join`, `circle list`, `member list`, `node list`, `invitation create`, and
`invitation redeem`, `message send/list`, `files contribution create/list`, and `files grant
create/list`. A successful JSON document is one line on standard output:

```json
{
  "outputVersion": 1,
  "result": {
    "productVersion": "0.3.0-alpha.1",
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
| Issued invitation | `circleId`, `invitationId`, `expiresAtUtc`, `package` (canonical JSON string) |
| Join request | `package`, `endpoint` (numeric private/loopback IP and port), `memberDisplayName` |
| Redemption | `circleId`, `invitationId`, `redemptionId`, `status` (`accepted`) |
| Circle Files provider | `id`, `nodeId`; provider implementation and credentials are absent |
| File Contribution | `id`, `circleId`, provider, `displayName`, lifecycle, generation, created time, authorizing Member/generation/time |
| Member Access Grant | `id`, `circleId`, `contributionId`, `memberId`, access, lifecycle, generation, created time, authorizing Member/generation/time |
| Error | `code`, `message` |

The v1 Member roles are `owner` and `member`. Contribution lifecycles are `defined`, `active`, and
`retired`; grant lifecycles are `defined`, `active`, and `revoked`; whole-folder access values are
`read-only` and `read-write`. Only `defined` creation is implemented in this slice.

## Endpoints

### `GET /control/v1/status`

Returns `200 OK` with the daemon version, protocol version, and persistent local Node:

```json
{
  "productVersion": "0.3.0-alpha.1",
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

### `POST /control/v1/circles/{circleId}/files/contributions`

Defines one provider-neutral whole-folder contribution on the local Node. The request contains a
canonical UUID `requestId` and `displayName`; the daemon creates stable contribution and provider
IDs, derives the provider's hosting Node, and sets lifecycle `defined`, generation `1`. Names are
trimmed, nonblank, and capped at 100 characters.

The local Circle Member must be an Owner, and the daemon must hold the matching current Circle
root. The exact normalized mutation is signed independently by the protected Member and root keys
before transactional persistence. Exact request-ID retry returns the original response; conflicting
reuse returns `409`. Success returns `201 Created`. The equivalent CLI is:

```text
balls files contribution create --circle <circle-id> --name <name> [--request-id <uuid>]
```

### `POST /control/v1/circles/{circleId}/files/contributions/{contributionId}/grants`

Defines one whole-folder Access Grant for a Circle Member. The body contains canonical UUID
`requestId`, canonical `memberId`, and exact access `read-only` or `read-write`. The contribution
and Member must belong to the named Circle. The daemon applies the same current Owner-Member and
Circle-root dual-signature rule, then stores lifecycle `defined`, generation `1` atomically.

Exact request-ID retry returns the original grant; conflicting reuse or a second grant for the
same Contribution/Member fails without partial state. Success returns `201 Created`. The CLI is:

```text
balls files grant create --circle <circle-id> --contribution <contribution-id> \
  --member <member-id> --access <read-only|read-write> [--request-id <uuid>]
```

Responses intentionally omit the canonical authorization transcript, both signatures, protected
private authority, and all future provider credentials.

### `POST /control/v1/circles/{circleId}/invitations`

Creates a canonical single-use Circle invitation. The request is
`{ "validForMinutes": 60 }`; validity is bounded from 1 through 10,080 minutes. Success returns
`201 Created` with the Circle/invitation IDs, expiry, and exact package string. The package is at
most 16 KiB and contains only public signed authority/context. The CLI prints that string for
direct copy or writes it with `--out <path>` using create-new semantics so existing files are not
overwritten.

### `POST /control/v1/circles/join`

Accepts the exact invitation package, an explicit numeric private/loopback `endpoint`, and the new
Member display name. `ballsd` connects through `lan-tcp-v1`, pins the invitation's Anchor transport
SPKI in TLS 1.3, presents the applicant's proposed transport proof, dual-signs the admission with
the retry-stable Member and local Node keys, validates the Anchor-signed response, and atomically
persists the returned Circle roster. Success returns `200 OK` with Circle details. The equivalent
CLI command is `balls circle join --file <path> --endpoint <ip:port> --member <name>`.

An exact completed retry returns the already persisted Circle without a network mutation.
Conflicting local reuse returns `409`; malformed, forged, unauthorized, revoked, stale,
wrong-Circle/Node, expired, replayed, and downgraded exchanges return bounded typed errors. Network
or authenticated-channel failure returns `502` without reflecting invitation or credential data.

### `POST /control/v1/circles/{circleId}/messages`

Authors one persistent text message through an admitted Anchor. The body contains canonical UUID
`requestId`, explicit numeric private/loopback `endpoint`, and nonblank `text` capped at 4,096
UTF-8 bytes. `ballsd` durably prepares the request, dual-signs it as the local Member and Node,
sends it over admitted-peer mTLS, validates the Anchor-signed receipt, and returns `200 OK` with
message/Circle/author IDs, text, authored and accepted UTC times, and positive Circle sequence.
The CLI is `balls message send --circle <id> --endpoint <ip:port> --text <text>
[--request-id <uuid>]`. Exact request-ID reuse with the same Circle and text is idempotent across
reconnect/restart; conflicting reuse returns `409`.

### `POST /control/v1/invitations/redeem`

Accepts `{ "package": "..." }`, verifies the exact canonical package against the issuing
Circle's current stored root/Anchor authority, and atomically commits one redemption result.
Success returns `200 OK` with stable IDs and `status: "accepted"`. Replay returns `409`; malformed,
forged, expired, future, revoked, stale, wrong-Circle, and unsupported inputs return bounded typed
errors without reflecting credentials or package contents. `balls invitation redeem --file
<path>` reads only an exact UTF-8 file of at most 16 KiB.

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
| `GET /control/v1/circles/{circleId}/messages` | `200` with `{ "circleId": "...", "messages": [Circle message] }` |
| `GET /control/v1/circles/{circleId}/files/contributions` | `200` with stable ordered Contribution projections |
| `GET /control/v1/circles/{circleId}/files/contributions/{contributionId}/grants` | `200` with stable ordered Access Grant projections |

Lists are returned in stable creation/identifier order as defined by the local store. An unknown
Circle returns `404 Not Found`.

## Browser adapter

The browser listener serves the bundled production application and only these `/browser/v1`
routes: session exchange, status, Circle list/create/details, and ordered Circle message history.
The browser control
plane is intentionally narrower than `/control/v1`; control routes return `404` on TCP and browser
routes return `404` over IPC. Invitation creation/redemption and every Circle Files route are
deliberately CLI/local-control only in this slice. The browser observes messages but does not
author them, and it has no Circle Files mutation path.

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
| 400 | `invalid_invitation_validity`, `invalid_admission_endpoint`, `member_display_name`, `malformed`, `forged`, `expired`, `not_yet_valid`, `revoked`, `wrong_circle`, `wrong_node`, `downgraded`, `unsupported_version`, `unsupported_suite`, `unauthorized_issuer`, `stale_authority_state` |
| 400 | `invalid_message_endpoint`, `invalid_message_text`, `unauthorized`, `oversized` |
| 400 | `contribution_name_required`, `contribution_name_too_long`, `invalid_member_access`, `circle_files_owner_required`, `circle_files_authority_unavailable`, `circle_files_authorization_failed` |
| 404 | `circle_not_found` |
| 404 | `invitation_not_found` |
| 404 | `circle_files_contribution_not_found`, `member_not_found` |
| 409 | `creation_request_conflict` |
| 409 | `admission_attempt_conflict` |
| 409 | `message_request_conflict`, `conflict` |
| 409 | `circle_files_contribution_request_conflict`, `circle_files_grant_request_conflict`, `circle_files_grant_exists` |
| 409 | `replayed` |
| 502 | `connection_failed`, authenticated remote-channel errors |

Framework-level rejection, such as malformed JSON or a request rejected before endpoint handling,
is not guaranteed to use the application error shape.

## Explicit non-goals

v1 does not expose invitation/join, message-authoring, or Circle Files mutation UX to the browser.
This slice defines Circle Files metadata and authorization only: it does not inspect or mutate SMB,
create folders/shares/accounts, store provider credentials, map drives, adopt existing folders,
delete files, revoke access, synchronize or replicate content, add version history/trash, discover
peers, or add automatic/multiple-Anchor behavior.
