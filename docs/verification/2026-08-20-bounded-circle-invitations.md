# Bounded Circle invitation verification — 2026-08-20

## Outcome

Issue #36 delivers a direct, inspectable invitation package that an Owner can copy or save to a
file and redeem exactly once against the issuing Circle. This slice records a bounded admission
request result; it does not open a remote listener or create membership, which remain #37 and #38.

## Acceptance evidence

| Acceptance | Observed evidence |
| --- | --- |
| Inspectable bounded package | `InvitationPackageCodec` emits canonical UTF-8 JSON with format/version, Circle and issuer context, expiry, nonce, protocol range, one-redemption authorization, public credentials, and signatures. Exact re-encoding is capped at 16 KiB; tests confirm no private material. |
| Accepted authority and validation | A current Circle root signs a time-bounded Anchor delegation; that Anchor signs the invitation. Pure validation checks canonical shape, both signatures, trusted root, issuer authorization, authority generation, Circle binding, time, suite, protocol, revocation, and replay state. |
| Durable one-use result | SQLite schema v3 stores the exact package/digest, expiry, revocation, a protected distinct bootstrap transport identity, and one redemption row. Sixteen concurrent store attempts produced one `Accepted` and fifteen `Replayed`; twelve concurrent API requests produced one HTTP 200 and eleven bounded 409 responses. Restart preserved package and transport key identity. |
| Direct copy/file exchange | `balls invitation create --circle <id>` prints the exact package; `--out <path>` creates a new file without overwriting; `balls invitation redeem --file <path>` accepts only strict UTF-8 input at most 16 KiB. No Cloud, discovery, provider credential, or IP identity participates. |
| Typed API/CLI boundary | Local-control v1 exposes typed creation/redemption requests and results. JSON CLI output retains the versioned envelope. Rejections use stable bounded codes/messages and never reflect the package, signature, or public credential. The browser adapter exposes no invitation route or storage. |
| Adversarial cases | Protocol tests deterministically reject forged, altered, expired, future, revoked-issuer, wrong-Circle, malformed, noncanonical, oversized, and unsupported-version packages. Storage/API tests cover digest substitution, expiry, revocation, missing state, replay, invalid validity, and malformed input. |

## Local verification

- Invitation protocol contract: 9 passed in 176 ms.
- Invitation storage contract: 3 passed in 188 ms.
- Invitation local-control contract: 2 passed in 706 ms.
- Direct file CLI contract: 1 passed in 565 ms.
- Schema v2→v3 rollback/retry migration contract: 1 passed in 157 ms.
- Complete storage suite: 27 passed in 1 second.
- Complete Windows full gate: 136 .NET passed, 11 expected platform skips, 4 Vitest passed, and
  1 Playwright Chromium journey passed; Release build reported 0 warnings and 0 errors.
- NuGet transitive vulnerability audit: every project reported no vulnerable packages.
- pnpm audit: no known vulnerabilities.

## Protected pull-request evidence

Pull request [#44](https://github.com/scwlkr/balls/pull/44) validated implementation head
`0868daf549c8c6255587f92d7f7437647ee33242`:

- [Windows fast](https://github.com/scwlkr/balls/actions/runs/32408900954/job/96554452625):
  passed in 2 minutes 48 seconds;
- [Ubuntu fast](https://github.com/scwlkr/balls/actions/runs/32408900954/job/96554452368):
  passed in 2 minutes 6 seconds;
- [Required](https://github.com/scwlkr/balls/actions/runs/32408900954/job/96555284336):
  passed in 3 seconds after both platform lanes;
- [CodeQL C#](https://github.com/scwlkr/balls/actions/runs/32408901116/job/96554452803):
  passed in 2 minutes 27 seconds;
- [dependency review](https://github.com/scwlkr/balls/actions/runs/32408900972/job/96554452141):
  passed in 6 seconds.

There were no review comments, change requests, merge conflicts, dependency findings, or CodeQL
findings. The final evidence-only head and squash-merged commit are recorded after the second
protected check run.

## Boundaries

- The transport public key is persisted and pinned, but #36 does not bind it into a live TLS
  listener.
- Redemption creates one durable request/result, not a Member or Circle Node. #38 must atomically
  couple the consume result with admission persistence.
- Invitation revocation exists as a fail-closed storage/validation seam; owner-facing revocation
  and general credential rotation remain later work.
- Email, QR, hosted delivery, account login, rich roles, and browser invitation UX are out of scope.
