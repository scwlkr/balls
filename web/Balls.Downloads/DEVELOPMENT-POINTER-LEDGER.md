# Development pointer ledger

This append-only ledger records every live `channels/development.json` and
`bootstrap/windows-x64.json` movement. Record the exact generator output and preserve both prior
pointer files before deployment so rollback does not depend on a mutable issue comment.

| Changed at (UTC)     | New tag                                   | New commit                               | Previous Development tag                  | Previous Development SHA-256                                     | Previous bootstrap tag                    | Previous bootstrap SHA-256                                       | Issue |
| -------------------- | ----------------------------------------- | ---------------------------------------- | ----------------------------------------- | ---------------------------------------------------------------- | ----------------------------------------- | ---------------------------------------------------------------- | ----- |
| Not published        | None                                      | None                                     | None                                      | None                                                             | None                                      | None                                                             | #95   |
| 2026-08-26T21:32:07Z | development-20260826T212044Z-1218b57d8d37 | 1218b57d8d3764b9a96b7fb9209dfbad759c335c | None                                      | None                                                             | None                                      | None                                                             | #101  |
| 2026-08-26T22:48:41Z | development-20260826T223620Z-72f6fa983b4c | 72f6fa983b4c2589d76ae10e263db7243ec2cc46 | development-20260826T212044Z-1218b57d8d37 | 3094a1e0ab017062a205a4fe926e181b31257102aff7cbe3024299583b3f862b | development-20260826T212044Z-1218b57d8d37 | 9973c0806e883f8250d28af0f7afd291c7d99b0a18800217881dfe9c71a53fe4 | #101  |
| 2026-08-27T05:17:32Z | development-20260827T045203Z-39cd15e5ffdf | 39cd15e5ffdf6faea3b5fd3430e3d4a1d67c2493 | development-20260826T223620Z-72f6fa983b4c | 0a54a6eb1d07556624cccea407a3e4d34ec70844bf742dbc986df3d0b4142f76 | development-20260826T223620Z-72f6fa983b4c | 975a83cd55a130c694a421691c0a12898f412626f607c1f964091adb1a8a33e2 | #101  |

Do not add invitations, credentials, Node state, private hostnames, or other pilot data. Alpha,
Beta, and Stable changes do not belong in this ledger because they require separate Owner approval.
