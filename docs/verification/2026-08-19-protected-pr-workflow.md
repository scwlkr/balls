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
- Third-party actions are full-SHA pinned, dependency caching remains enabled, and stale runs cancel.
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

The implementation pull request and controlled failing proof are recorded here after GitHub emits
the new stable check names. The failing proof changes one test assertion only, is never merged,
and is deleted after its failed lane and failed `Required` result are observed.
