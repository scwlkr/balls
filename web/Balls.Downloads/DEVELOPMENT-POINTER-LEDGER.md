# Development pointer ledger

This append-only ledger records every live `channels/development.json` and
`bootstrap/windows-x64.json` movement. Record the exact generator output and preserve both prior
pointer files before deployment so rollback does not depend on a mutable issue comment.

| Changed at (UTC)     | New tag                                   | New commit                               | Previous Development tag | Previous Development SHA-256 | Previous bootstrap tag | Previous bootstrap SHA-256 | Issue |
| -------------------- | ----------------------------------------- | ---------------------------------------- | ------------------------ | ---------------------------- | ---------------------- | -------------------------- | ----- |
| Not published        | None                                      | None                                     | None                     | None                         | None                   | None                       | #95   |
| 2026-08-26T21:32:07Z | development-20260826T212044Z-1218b57d8d37 | 1218b57d8d3764b9a96b7fb9209dfbad759c335c | None                     | None                         | None                   | None                       | #101  |

Do not add invitations, credentials, Node state, private hostnames, or other pilot data. Alpha,
Beta, and Stable changes do not belong in this ledger because they require separate Owner approval.
