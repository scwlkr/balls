# Development and Release Process

## Objective

Optimize Balls for safe, extremely fast AI-assisted development from one Windows laptop and one
Apple-Silicon Mac while
keeping every green `main` promotable. Speed comes from short feedback loops, small vertical
outcomes, deterministic automation, and rare high-cost evidence—not from skipping known
security, data-integrity, or system-mutation risks.

## Technology selection

Prefer mainstream, stable, strongly typed, well-documented tools with abundant examples. The
current default set is .NET/C#, HTTP/JSON/OpenAPI, SQLite, React, TypeScript, Vite, pnpm,
Playwright, PowerShell, Git, and GitHub Actions. Adopt an unusual tool only after a small prototype
demonstrates a material advantage.

## Sources of truth

- [`docs/STATE.md`](STATE.md): compact current state and active milestone.
- [`ROADMAP.md`](../ROADMAP.md): outcome and release index.
- GitHub milestone: committed scope for the active release target.
- GitHub Issue: executable acceptance contract for one vertical outcome.
- Linked design/protocol/security documents: deeper constraints for that issue.
- Pull request: implementation and evidence review.

An issue executes the roadmap; it does not silently redefine it. If implementation exposes a
product or trust-boundary decision, record the decision first.

## Ticket contract

Every feature issue states:

1. user outcome;
2. acceptance checks;
3. explicit non-goals;
4. dependencies;
5. platform and risk labels;
6. fast verification commands;
7. environment-specific evidence, if genuinely required;
8. documentation that must change.

A ticket should usually fit one focused agent session and produce one independently mergeable
vertical outcome. Prefer three to seven substantial tickets per milestone. Do not split a coherent
outcome merely to create activity.

## Execution flow

```text
active milestone
      ↓
highest ready issue
      ↓
short-lived codex/ branch
      ↓
fast local feedback
      ↓
one pull request + automated review
      ↓
required Windows/Linux/macOS gate
      ↓
squash merge to main
      ↓
immutable canary artifact
      ↓
next ready issue
```

- Keep one active milestone and at most two non-overlapping issues in progress.
- Open a pull request early when useful; keep branches short-lived.
- Auto-merge only after acceptance criteria, documentation, and required checks pass.
- Delete merged branches.
- Never rebuild artifacts for promotion. Promote the exact files produced from the accepted commit.

## Feedback loops

| Loop | Target | Runs | Contents |
| --- | ---: | --- | --- |
| Focused | `<15s` | During edits | directly affected unit, contract, or component tests |
| Local fast gate | `<60s` | Before push | format/analyzers, incremental build, unit/contract/process smoke |
| Pull request | `<5m` wall time | Every PR | parallel fixed Windows/Linux/macOS build and fast suites; Chromium UI smoke when present |
| Risk gate | No calendar target | Only when triggered | relevant VM, OS mutation, migration, installer, recovery, networking, or full UI scenario |
| Release candidate | Outcome-driven | Before promotion | exact packaged bits and the minimum environment evidence for the release claim |

Do not schedule a nightly or weekly heavyweight suite until actual failures demonstrate that its
signal is worth its cost. A risky change cannot avoid its specific risk gate merely because the
suite is normally rare.

## Pull-request merge decision

The required CI path uses fixed `windows-2025`, `ubuntu-24.04`, and Apple-Silicon `macos-26`
hosted images. Each platform runs the repository `fast` command in parallel under stable platform
names. A final `Required` job runs with `always()`, inspects all three dependency results, and
fails if any lane failed, was cancelled, or was skipped. Only `Required` is a branch-ruleset status,
giving every pull request one fail-closed merge decision while retaining platform-specific logs.

The workflow keeps read-only repository permission, SHA-pinned third-party actions, dependency
caches, ten-minute lane timeouts, and stale-run cancellation. The repository allows squash merges
only, deletes merged branches automatically, and enables auto-merge; `main` blocks deletion,
force-pushes, and direct changes through its active ruleset.

The implemented repository entry point is:

```text
dotnet run --project eng/Balls.Verify --configuration Release -- focused --project <test-project> --filter <expression>
dotnet run --project eng/Balls.Verify --configuration Release -- fast
dotnet run --project eng/Balls.Verify --configuration Release -- full
```

It prints its underlying `dotnet` and `pnpm` commands, builds .NET only once in `fast` and `full`,
rejects unknown categories, and makes an empty focused selection fail. The browser workspace uses
the same visible orchestration for locked install, generated-client drift, format, lint, typecheck,
component tests, and production build. Focused web verification accepts only named repository
scripts rather than arbitrary shell input.

## Two-workstation GitHub loop

GitHub is the shared state; the computers never share a working directory. Keep one issue and one
branch per outcome, and do not edit the same ticket from both machines. Windows owns Windows/Files/
release tickets. The Mac owns macOS, portable code, browser UI, interaction, and brand tickets.

At the start of work on either computer:

```bash
git fetch origin
git switch main
git pull --ff-only origin main
git switch -c codex/<issue>-<short-slug>
```

Commit useful checkpoints and push the branch early with `git push -u origin HEAD`. Open one pull
request linked to the issue. Use issue/PR comments only for handoff facts, not as a second design
document. Let required CI and auto-merge serialize accepted work; after merge, fast-forward `main`
before starting the next branch. If two changes overlap, finish the smaller PR first and rebase or
merge current `main` into the other branch once—do not develop competing versions.

## Test taxonomy

- **Unit:** millisecond-fast isolated domain/application behavior.
- **Contract:** serialization, API, storage schema, and provider contracts.
- **Process integration:** multiple real `ballsd`/`balls` child processes with isolated temporary state.
- **OS integration:** one real adapter or privileged boundary.
- **Browser:** TypeScript/component tests plus a minimal Playwright Chromium journey.
- **Lab:** Windows host and virtual Nodes for install, restart, networking, or provider behavior.
- **Pilot:** observed use of an accepted candidate; never a substitute for deterministic tests.

New tests should use Microsoft.Testing.Platform when the selected framework/extensions fully
support it. Every test class must declare one of the six category names above; the repository
verifier rejects tests without a recognized category. Build once and avoid redundant restore/build work.
A focused filter must fail rather than silently run zero expected tests.

## Development lab

The initial lab is intentionally one-laptop:

- Windows host: primary development machine and Windows Node;
- WSL2 Ubuntu: optional sub-minute Linux development executor, never the product runtime definition;
- dedicated Ubuntu Hyper-V VM: separate Linux Node on a dedicated internal/NAT lab network;
- optional small Windows VM: started serially for installer, upgrade, and Windows-to-Windows checks;
- GitHub-hosted Windows, Ubuntu, and Apple-Silicon macOS runners: clean pull-request evidence.

Create clean checkpoints before Balls persists Node identity. Never clone a VM after enrollment
without regenerating its Node identity. Existing unrelated VMs and switches remain untouched.
Virtual evidence is sufficient for releases when the release notes state the limitation; physical
machines are opportunistic, not a general gate.

## Evidence and release blockers

An Alpha is blocked by a known:

- credential or private-data exposure;
- destructive data loss or corruption;
- unsafe privileged/system mutation;
- corrupt upgrade or unrecoverable migration;
- inability to install, start, or exercise the release's headline outcome.

Ordinary prerelease defects should be documented and shipped. Record untested environments as
unverified; never convert them into implied support.

## Release channels

- **Canary:** automatically built once from the exact green `main` commit; public workflow
  artifacts retained for 14 days. Windows and Linux are runnable development evidence smoked from
  fresh protected local state on their native hosted runners.
- **Development:** durable public testing snapshot from an identified issue branch or `main`
  commit. It may be incomplete or broken, but it is an immutable GitHub prerelease with exact
  identity and package-integrity metadata.
- **Alpha:** public immutable prerelease for one coherent product outcome.
- **Beta:** accepted for the initial company pilot and broader real use.
- **Stable:** explicitly owner-accepted and supported, with no known critical security or data-loss defect.

An agent working an active issue may create a Development tag and GitHub prerelease and move the
Development pointer after build and package-integrity checks pass. Record the previous pointer for
rollback. Development may fail functionally; it may not contain ambiguous identity, corrupt
packaging, secrets, mutable assets, or policy bypasses. Alpha, Beta, and Stable publication uses an
owner-gated GitHub environment.

The release pipeline builds once, generates checksums and an SBOM, records provenance/attestation
where available, and promotes the same artifacts. A green-`main` package rehearsed through
Development becomes Alpha by moving the Alpha pointer to those identical assets, never by
rebuilding. Windows public binaries must be signed before Stable.

Canary publication is downstream of the successful `Required` job in the same CI workflow for a
`main` push. It checks out the accepted SHA with read-only permissions, packages version/OS/
architecture/commit identity and checksums, smokes the Windows archive from fresh state, and
smokes the Linux archive from fresh state with the same CLI/browser/restart outcome, then uploads
both with a bounded retention period. Pull-request runs skip both publication jobs. It does not
create a product tag or GitHub Release.

The download page presents Alpha first, then the warned latest Development build, then immutable
previous versions. Keep all accepted releases and the newest ten Development builds on the page;
link the complete GitHub Releases archive. See
[`ADR 0010`](decisions/0010-public-development-download-channel.md).

## Public security automation

Dependency review and C# CodeQL provide pull-request feedback but are not hidden inside the
platform aggregate. CodeQL also runs weekly and on `main`. OpenSSF Scorecard runs after `main`, on
branch-protection changes, and weekly; its first result is evidence, not a release blocker.
Dependabot tracks NuGet and GitHub Actions weekly and security updates are enabled.

Repository workflow tokens default to read-only and cannot approve pull requests. Only the CodeQL
and Scorecard analysis jobs receive narrowly scoped security-result writes; only Scorecard receives
OIDC. All third-party actions require full commit SHAs, and the sole non-GitHub action allowlist
entry is the exact pinned OpenSSF Scorecard commit. Fork-triggered workflows contain no secrets,
`pull_request_target`, `workflow_run`, or self-hosted runner path.

## Public-source boundary

Apache 2.0 is the accepted source-license choice. Before changing repository visibility:

1. add and verify the canonical license and notices;
2. replace identifying examples with fictional data, including Git history where necessary;
3. audit tracked files and history for credentials and private operational details;
4. enable issue forms and contribution/security guidance;
5. pass the full current gate;
6. show the owner the exact readiness evidence;
7. obtain a final explicit confirmation before publication.

Dependency review, code scanning, Scorecard, Canary publication, and other supply-chain automation
should follow immediately after publication but do not delay the initial visibility transition.

External contributions use the same Apache 2.0 terms without a CLA or copyright assignment unless
a later recorded business need changes that policy.

## Automation-first interfaces

Every important use case must be reachable through the CLI or a typed API with deterministic exit
codes and structured output. The browser UI is one React/TypeScript application served locally by
`ballsd`; it does not own product logic. Native shells are added only for a proven OS-specific UX
need. This keeps AI verification fast and avoids separate GUI implementations per operating system.
