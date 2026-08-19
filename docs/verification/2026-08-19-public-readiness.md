# Public repository readiness — 2026-08-19

## Status and audited commits

**Status:** source-tree preparation is complete; publication is not approved or complete.

- Audited remote `main`: `4a6b1b39b1f5794cb886a179211663f2ceda2379`.
- Audited implementation commit before this evidence record:
  `2bf0a00ef1b89a9db2fcb13194bc3b84689cdd9f`.
- Initial pull-request head: `f71d7f9657b547794da97455044067f6920605b3` in
  [PR #10](https://github.com/scwlkr/balls/pull/10).
- Repository visibility observed through GitHub: `PRIVATE`.
- No visibility, remote-history, rules, release, or product-publication mutation was performed.

The initial pull-request head passed Windows and Linux CI. The final accepted commit will be
recorded on issue #3 after the evidence-only follow-up is green and squash-merged. A fresh mirror
audit remains required immediately before any history mutation.

## Public tree

- `LICENSE` is equal to Apache's canonical `LICENSE-2.0.txt` after newline normalization.
- `NOTICE` states the current attribution status. No copied source or vendored dependency was found
  that requires an additional attribution notice in the current source distribution.
- `README.md`, `CONTRIBUTING.md`, ADR 0003, and package metadata consistently state Apache-2.0 and
  the same inbound contribution terms without a CLA or copyright assignment.
- Personal, real company-like, host, and operational examples were replaced with conventional
  synthetic people, Nodes, Circles, and the IANA-reserved `.example` domain.
- Necessary canonical repository-owner URLs, `CODEOWNERS`, and the public archived prior-research
  repository are explicitly retained; these identify the repositories, not example users or hosts.
- The tracked brand PNG was visually inspected. It contains only the Balls brand presentation and
  no person, host, company, network, or operational data. ImageMagick reported no embedded author,
  comment, EXIF, or other profile metadata.
- Gitleaks 8.30.1 found no leaks in the prepared working tree.

## Git history and rewrite trial

A fresh mirror of every GitHub-advertised ref contained 14 unique commits:

| Ref | Audited old commit | Disposable rewritten commit |
| --- | --- | --- |
| `refs/heads/main` | `4a6b1b39b1f5794cb886a179211663f2ceda2379` | `6d7aeed2f19d223b11a77f4b4ef0f38f073e7112` |
| `refs/pull/1/head` | `fa21ea94ae82ec8e3369657c336df3aed7662823` | `fe17df6a25febf07576e1956752c7e45a85ba411` |
| `refs/pull/2/head` | `eab250362f1af410e8b3c4e48b4fccb0fc82266b` | `dcdfef1f659a97e2b7768347edddc4a58c749e1e` |
| `refs/pull/9/head` | `7d1bb777840a02a6457a6da69ed8882d16c08b3a` | `4d58f448fd68a2015718e95aa22374c782f389fc` |

The untouched mirror produced zero Gitleaks findings across all 14 commits. A targeted identifier
scan found 1,255 repeated matching lines across 14 commits and one non-noreply author address. No
credentials, tokens, private keys, private network addresses, state databases, diagnostic dumps,
or other secret material were found. There are no tags or forks; GitHub reported zero forks.

A disposable `git-filter-repo` 2.47.0 trial rewrote all targeted examples and the author metadata.
Its first changed commit was `6b85f03b34a3215b536fa0ad776738a0359d7ad4`. Verification of the
rewritten mirror observed:

- zero targeted identifier matches across every rewritten ref;
- only GitHub noreply author and committer addresses;
- zero Gitleaks findings across 16 commits, including the local readiness trial;
- clean `git fsck --full`;
- canonical license equality, successful YAML lint, and all relative Markdown links resolving;
- locked restore, format verification, zero-warning Release build, and 55 passing tests from a
  fresh rewritten checkout in 32.55 seconds.

Only `refs/heads/main` is writable on GitHub. The three `refs/pull/*/head` refs are read-only and
would continue to expose the old commits after a force-push. GitHub's
[history-removal guidance](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)
says pull-request references require GitHub Support, while Support generally does not remove
non-sensitive data. Opening this readiness pull request will add another read-only pull ref, so the
final ref map must be regenerated after merge.

No force-push was attempted. A force-push of `main` alone is insufficient for strict history
sanitization.

## Contribution and security surface

- Bug and feature forms are present, blank issues are disabled, and the `needs-triage` label used
  by both forms exists. `yaml-lint` parsed all three issue-template YAML files successfully.
- The bug form requires synthetic reproduction data and explicitly redirects suspected
  vulnerabilities away from public issues.
- `CONTRIBUTING.md`, `SECURITY.md`, and the README agree on contribution scope and private security
  reporting.
- GitHub private vulnerability reporting returned `404` while the repository was private. GitHub
  documents the feature for public repositories, so it must be enabled and verified immediately
  after an approved public transition.
- Branch protection and repository rulesets returned `403` with the current private GitHub Free
  repository. GitHub reports that the available protections become usable when the repository is
  public. They have not been preconfigured.

After an approved transition, protect `main` against deletion and force-push, require a pull
request, linear history, and the Windows/Linux CI checks, then verify the rules through GitHub's
API and an unauthenticated public read.

## Verification performed

- `git diff --check`.
- Relative Markdown link validation: all links resolved.
- `yaml-lint` on bug, feature, and issue-template configuration YAML: pass.
- Apache canonical license text comparison: pass.
- Gitleaks 8.30.1 working-tree and complete mirrored-history scans: zero findings.
- Targeted tracked-tree and all-ref history scans for identifiers, credentials, private network
  details, user paths, and diagnostics.
- Locked restore, format verification, Release build, and all 55 tests: pass on the prepared
  Windows checkout in 25.94 seconds.
- Disposable rewritten-mirror verification and the same full gate: pass in 32.55 seconds.
- [GitHub Actions run 32286314169](https://github.com/scwlkr/balls/actions/runs/32286314169)
  passed on the initial PR head: Ubuntu in 1 minute 9 seconds and Windows in 2 minutes 28 seconds.

The evidence-only follow-up commit must pass both jobs again before merge. No VM, installer,
network, UI, multi-machine, or product-release gate is triggered by this documentation/privacy
change.

## Remaining limitations and owner decision

The owner must choose the strict history-publication path before visibility changes:

1. **Publish a clean sanitized repository lineage (recommended):** retain this repository as a
   private archive and publish only rewritten/sanitized refs in the canonical public repository.
   This avoids exposing GitHub's immutable legacy pull refs but requires an owner-approved
   repository migration or recreation plan.
2. **Request GitHub Support cleanup:** rewrite writable refs, then ask Support to remove the legacy
   pull refs and cached views. GitHub warns that non-sensitive data may not qualify, so approval is
   uncertain.
3. **Accept residual legacy pull refs:** force-push sanitized `main` but explicitly waive strict
   history sanitization. This leaves identifying examples fetchable and does not satisfy the
   current acceptance wording without a recorded owner exception.

After that choice is executed and verified, a separate final explicit owner confirmation is still
required before changing visibility. Publication must not create a product release.
