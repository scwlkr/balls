# Protected Pull-Request Workflow Evidence — 2026-08-19

## Scope

Issue [#5](https://github.com/scwlkr/balls/issues/5) gives every pull request one unambiguous,
fail-closed Windows/Linux merge decision without adding a paid control or heavyweight schedule.

## Workflow contract

- `Windows fast` runs on the fixed `windows-2025` image.
- `Ubuntu fast` runs on the fixed `ubuntu-24.04` image.
- Both lanes run the repository-owned `fast` verifier in parallel.
- `Required` uses `always()` and succeeds only when both dependency results equal `success`.
- Workflow permissions remain `contents: read`; checkout credentials are not persisted.
- Third-party actions are full-SHA pinned, dependency caching remains enabled, and stale pull-request
  runs cancel; accepted `main` runs finish their bounded Canary publication.
- Three verifier self-tests enforce these properties from the checked-in workflow text.

## Repository settings

GitHub API readback after configuration reported:

| Setting | Verified value |
| --- | --- |
| Repository visibility | `public` |
| Allowed merge method | squash only |
| Auto-merge | enabled |
| Delete branch on merge | enabled |
| Ruleset enforcement | active |
| Ruleset required status | `Required` |
| Strict status policy | enabled |
| Block branch deletion | enabled |
| Block force-push | enabled |

## Pull-request proof

Implementation pull request [#12](https://github.com/scwlkr/balls/pull/12) produced one successful
decision in [run 32296149564](https://github.com/scwlkr/balls/actions/runs/32296149564):

| Check | Result | Duration |
| --- | --- | ---: |
| [`Ubuntu fast`](https://github.com/scwlkr/balls/actions/runs/32296149564/job/96207791446) | Pass | 57s |
| [`Windows fast`](https://github.com/scwlkr/balls/actions/runs/32296149564/job/96207791261) | Pass | 2m43s |
| [`Required`](https://github.com/scwlkr/balls/actions/runs/32296149564/job/96208602637) | Pass | 2s |

Controlled draft pull request [#13](https://github.com/scwlkr/balls/pull/13) added one intentional
failing assertion only. [Run 32296543221](https://github.com/scwlkr/balls/actions/runs/32296543221)
proved the aggregate fails rather than skips when its dependencies fail:

| Check | Result | Duration |
| --- | --- | ---: |
| [`Ubuntu fast`](https://github.com/scwlkr/balls/actions/runs/32296543221/job/96209036175) | Intentional fail | 1m22s |
| [`Windows fast`](https://github.com/scwlkr/balls/actions/runs/32296543221/job/96209036487) | Intentional fail | 1m58s |
| [`Required`](https://github.com/scwlkr/balls/actions/runs/32296543221/job/96209633133) | Expected fail | 3s |

The draft was closed without merge, and its local and remote branch were deleted after observation.
The active ruleset was then read back with `Required` as its sole strict required status; deletion,
non-fast-forward changes, linear history, squash-only pull requests, and resolved threads remain
enforced.
