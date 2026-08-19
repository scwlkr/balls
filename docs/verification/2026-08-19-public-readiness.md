# Public repository readiness — 2026-08-19

## Status and audited lineage

**Status:** the sanitized private successor is prepared; publication is not approved or complete.

- The owner selected a clean sanitized lineage and authorized deletion of the private archive only
  after the public successor is established and verified.
- Sanitized final candidate: `scwlkr/balls-public-candidate`, observed `PRIVATE`.
- Sanitized candidate base: `af3c03dd67663a1e2003827e948580f980b98090`.
- The private source archive remains unchanged and private.
- A separate final owner confirmation is still required before any repository rename, visibility
  change, or archive deletion.

No product release has been or will be published by this transition.

## Public tree

- `LICENSE` is equal to Apache's canonical `LICENSE-2.0.txt` after newline normalization.
- `NOTICE` states the current attribution status. No copied source or vendored dependency was found
  that requires an additional attribution notice in the current source distribution.
- `README.md`, `CONTRIBUTING.md`, ADR 0003, and package metadata consistently state Apache-2.0 and
  the same inbound contribution terms without a CLA or copyright assignment.
- Personal, real company-like, host, and operational examples were replaced with conventional
  synthetic people, Nodes, Circles, and the IANA-reserved `.example` domain.
- Necessary canonical repository-owner URLs, `CODEOWNERS`, and the public archived prior-research
  repository are explicitly retained; these identify repositories, not example users or hosts.
- The tracked brand PNG contains only the Balls brand presentation. ImageMagick reported no
  embedded author, comment, EXIF, or profile metadata.
- Gitleaks 8.30.1 found no leaks in the prepared tree.

## Sanitized history and repository migration

The final untouched mirror of the private source contained 19 unique commits across `main` and
four GitHub pull-request refs. The complete mirror produced zero Gitleaks findings. A targeted
privacy scan found 1,350 repeated identifier matches across 15 commits and one non-noreply author
address, so publishing the source repository in place would not satisfy strict sanitization.

`git-filter-repo` 2.47.0 rewrote the targeted examples and author metadata. Verification of the
final rewritten mirror observed:

- zero targeted identifier matches across every rewritten ref;
- only GitHub noreply author and committer addresses;
- zero Gitleaks findings;
- clean `git fsck --full --strict`;
- canonical license equality, successful YAML lint, and all relative Markdown links resolving;
- locked restore, format verification, zero-warning Release build, and 55 passing tests from a
  fresh rewritten checkout in 29.88 seconds.

A final metadata-only rewrite replaced the GitHub account's private squash-author address with its
public noreply address. The candidate base tree is byte-for-byte equal to the previously tested
sanitized base, every reachable candidate commit uses public noreply metadata, and the private
staging repository that exposed this edge case will not enter the public lineage.

Only rewritten `main` was pushed to the private staging repository. Legacy source pull refs were
not copied. Two closed empty-diff pull requests preserve the shared issue/PR numbering before the
six executable issues were recreated as #3–#8. All 23 labels, all seven milestone records, each
issue body, label set, state, and milestone assignment match the private source. There are no
tags, releases, or forks to migrate. The old source evidence comment was not copied because its
private Actions links will be deleted with the archive; replacement evidence will be recorded on
issue #3 after staging CI and the final audit complete.

This clean successor removes the immutable legacy pull refs from the publication lineage. The
private source will become a temporary archive during the approved cutover and will be deleted
only after the canonical public repository is verified ready.

## Contribution and security surface

- Bug and feature forms are present, blank issues are disabled, and the `needs-triage` label used
  by both forms exists. `yaml-lint` parses all issue-template YAML files successfully.
- The bug form requires synthetic reproduction data and redirects suspected vulnerabilities away
  from public issues.
- `CONTRIBUTING.md`, `SECURITY.md`, and the README agree on contribution scope and private security
  reporting.
- Private vulnerability reporting is unavailable while staging is private. It must be enabled and
  verified immediately after an approved public transition.
- Public repository rules are not applied while staging is private. After approval, protect `main`
  against deletion and force-push, require a pull request and linear history, require Windows/Linux
  CI, and verify the effective rules through GitHub's API.

## Verification performed

- `git diff --check`.
- Relative Markdown link validation: all links resolved.
- `yaml-lint` on bug, feature, and issue-template configuration YAML: pass.
- Apache canonical license text comparison: pass.
- Gitleaks 8.30.1 prepared-tree and complete mirrored-history scans: zero findings.
- Targeted tracked-tree, all-ref history, issue, and comment scans for identifiers, credentials,
  private network details, user paths, and diagnostics: zero findings in the successor inputs.
- Locked restore, format verification, Release build, and all 55 tests: pass on the prepared
  Windows checkout and from a fresh rewritten checkout.
- The private source preparation pull request passed Ubuntu and Windows CI before squash merge.
- The final candidate reconciliation is reviewed in
  [PR #9](https://github.com/scwlkr/balls-public-candidate/pull/9); its exact accepted commit and CI
  evidence will be recorded on issue #3 after squash merge.

No VM, installer, network, UI, multi-machine, or product-release gate is triggered by this
documentation/privacy migration.

## Approved cutover sequence

After candidate PR #9 passes Windows and Linux CI and the final private audit is clean, stop and show
the owner the exact readiness evidence. Only a separate final explicit confirmation authorizes:

1. rename the private source to a temporary archive name;
2. rename the sanitized staging repository to canonical `scwlkr/balls`;
3. change only the sanitized canonical repository to public;
4. enable and verify private vulnerability reporting and the available `main` rules;
5. verify unauthenticated public access, issue/milestone continuity, and Git/CI health;
6. delete the exact private archive after all preceding checks pass.

Publication must not create a product release.
