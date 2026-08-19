# Public repository readiness — 2026-08-19

## Status and published lineage

**Status:** the clean sanitized lineage is public and the transition is complete.

- Canonical repository: `scwlkr/balls`, observed `PUBLIC`.
- Sanitized pre-cutover base: `af3c03dd67663a1e2003827e948580f980b98090`.
- Accepted transition PR head: `5b95ca7d6afcab16174d10b0a83faebc9147e5b5`.
- Initial public `main`: `a2b01aa56496eceeae8e931c25b7d46c0da854ab`.
- The owner selected the clean lineage, enabled GitHub commit-email privacy, and gave the separate
  final visibility and deletion confirmation.
- The original private source archive and rejected private staging repository were deleted only
  after the public repository, security settings, rules, tracker, Git access, and CI were verified.
  Both deleted repository API paths returned `404` afterward.

No product release was created.

## Public tree

- `LICENSE` equals Apache's canonical `LICENSE-2.0.txt` after newline normalization.
- `NOTICE` states the current attribution status. No copied source or vendored dependency requires
  an additional attribution notice in the current source distribution.
- `README.md`, `CONTRIBUTING.md`, ADR 0003, and package metadata consistently state Apache-2.0 and
  the same inbound contribution terms without a CLA or copyright assignment.
- Personal, real company-like, host, and operational examples use conventional synthetic people,
  Nodes, Circles, and the IANA-reserved `.example` domain.
- Necessary canonical repository-owner URLs, `CODEOWNERS`, and the public archived prior-research
  repository identify repositories, not example users or hosts.
- The tracked brand PNG contains only the Balls brand presentation. ImageMagick reported no
  embedded author, comment, EXIF, or profile metadata.

## Sanitized history and migration

The final untouched source mirror contained 19 unique commits across `main` and four GitHub pull
refs. It produced zero Gitleaks findings, but a targeted privacy scan found 1,350 repeated legacy
identifier matches across 15 commits and one private author address. Publishing that repository in
place would therefore have failed strict sanitization.

The clean successor rewrote examples and commit metadata without changing the accepted tree. A
private staging audit then exposed a GitHub web-squash author email that required account-level
commit-email privacy. The final candidate was rebuilt from the sanitized base, every commit used a
public GitHub noreply or system identity, and the final squash merge preserved that boundary.

The final pre-public mirror explicitly fetched every pull ref and observed:

- only `refs/heads/main` plus sanitized `refs/pull/1/head`, `refs/pull/2/head`, and
  `refs/pull/9/head`;
- 15 unique reachable commits;
- zero targeted identifier matches in tracked history or GitHub issue/PR text;
- zero Gitleaks findings;
- no private-domain author or committer addresses and no unexpected identity names;
- clean `git fsck --full --strict`;
- an exact tree match between accepted `main` and PR #9 head;
- only `main` as a writable branch.

Two closed empty-diff pull requests preserve the original shared issue/PR numbering. The six
executable issues remain #3–#8. All 23 labels, all seven milestones, and every migrated issue body,
label set, state, and milestone assignment matched the source before publication. There were no
tags, releases, or forks to migrate.

## Contribution, security, and rules

- Bug and feature forms are present, blank issues are disabled, and the `needs-triage` label used
  by both forms exists.
- The bug form requires synthetic reproduction data and redirects suspected vulnerabilities away
  from public issues.
- `CONTRIBUTING.md`, `SECURITY.md`, and the README agree on contribution scope and private security
  reporting.
- GitHub private vulnerability reporting is enabled and verified.
- Active ruleset `21056510` targets the default branch and blocks deletion and force-push, requires
  linear history and pull requests, permits squash only, requires resolved review threads, and
  requires both `Build and test (ubuntu-latest)` and `Build and test (windows-latest)`.
- Actions are restricted to GitHub-owned actions and the workflow token defaults to read-only.
- Issues are enabled; Projects, Discussions, and Wiki are disabled.

## Verification performed

- `git diff --check`.
- Relative Markdown link validation across 30 files: pass.
- Apache canonical license comparison: pass.
- Issue-form YAML validation: pass on the unchanged audited forms.
- Targeted tracked-tree, all-ref history, issue, PR, and comment privacy scans: zero findings.
- Gitleaks 8.30.1 prepared-tree and complete mirrored-history scans: zero findings.
- Locked restore and format verification: pass.
- Release build: zero warnings and zero errors.
- The final local candidate worktree passed 53 tests; Windows Application Control blocked only the
  two child-process tests at that new path. The byte-identical code tree had already passed all 55
  locally, and both clean GitHub-hosted platforms passed the full suite.
- [PR #9 CI](https://github.com/scwlkr/balls/actions/runs/32290659663): Ubuntu passed in 1 minute
  6 seconds and Windows passed in 2 minutes 11 seconds.
- [Initial public `main` CI](https://github.com/scwlkr/balls/actions/runs/32291243070): Ubuntu passed
  in 1 minute and Windows passed in 2 minutes 34 seconds.
- Anonymous GitHub API, repository web page, and credential-free HTTPS Git reads: pass.
- Anonymous issue continuity (#3–#8), seven milestones, Apache-2.0 detection, and exact public
  `main` SHA: pass.
- Effective rules API returned all five required rules; private vulnerability reporting returned
  enabled.
- Both authorized private deletion targets were rechecked by exact name, visibility, and `main`
  SHA immediately before deletion, then returned `404` after deletion.

No VM, installer, network, UI, multi-machine, or product-release gate was triggered by this
documentation/privacy migration. The exact accepted commit for this post-cutover evidence update
is recorded on issue #3 after its protected follow-up pull request lands.
