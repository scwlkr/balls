# Development pointer ledger

This append-only ledger records every live `channels/development.json` movement. Record the exact
generator output before deployment so rollback does not depend on a mutable issue comment.

| Changed at (UTC) | New tag | New commit | Previous tag | Previous manifest SHA-256 | Issue |
| ---------------- | ------- | ---------- | ------------ | ------------------------- | ----- |
| Not published    | None    | None       | None         | None                      | #95   |

Do not add invitations, credentials, Node state, private hostnames, or other pilot data. Alpha,
Beta, and Stable changes do not belong in this ledger because they require separate Owner approval.
