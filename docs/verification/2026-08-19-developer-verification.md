# Developer Verification Evidence — 2026-08-19

## Scope

Issue [#4](https://github.com/scwlkr/balls/issues/4) adds one cross-platform .NET entry point with
focused, fast, and full modes. This record captures the local Windows observations; the pull
request checks capture the required clean Windows and Ubuntu observations.

## Windows host observations

Measured from a warm checkout with .NET SDK `10.0.400`:

| Mode | Command | Result | Elapsed | Budget |
| --- | --- | --- | ---: | ---: |
| Focused | `dotnet run --project eng/Balls.Verify --configuration Release -- focused --project tests/Balls.Core.Tests/Balls.Core.Tests.csproj --filter TestCategory=Unit` | 8 passed | 2.92s | `<15s` pass |
| Fast | `dotnet run --project eng/Balls.Verify --configuration Release -- fast` | 58 passed; category audit found zero unknown tests | 29.49s | `<60s` pass |

An intentional focused filter of `FullyQualifiedName=Does.Not.Exist` selected zero tests and the
verifier returned exit code 3. The verifier self-tests cover parsing, plan composition, aggregate
TRX counts, empty focused selections, category-audit failure, and subprocess failure propagation.

## Gate composition

`fast` and `full` run a locked restore, format verification without restore, and one Release build
without restore. The category audit and final test step reuse that build. Fast selects `Unit`,
`Contract`, and `ProcessIntegration`; full runs all six allowed categories. `OSIntegration`,
`Browser`, and `Lab` remain out of the portable fast path.

There is no JavaScript workspace at this checkpoint, so no `pnpm` process was observed. The
developer contract requires future browser commands to stay visible when that workspace lands.

## CI evidence

Pull request [#11](https://github.com/scwlkr/balls/pull/11) passed both clean hosted paths:

| Host | Mode | Result | Duration |
| --- | --- | --- | ---: |
| [`ubuntu-latest`](https://github.com/scwlkr/balls/actions/runs/32295032454/job/96204253136) | `fast` | Pass | 1m01s |
| [`windows-latest`](https://github.com/scwlkr/balls/actions/runs/32295032454/job/96204252763) | `full` | Pass | 2m03s |

The Ubuntu job executed the same portable fast path documented for Linux. The Windows job ran all
62 tests. Both completed inside the pull-request wall-time budget.

## Milestone exit remeasurement

Issue [#8](https://github.com/scwlkr/balls/issues/8) repeated the warm Windows measurements after
the complete Open and Fast Foundation change set. The same focused selection passed 8 tests in
6.03s. The complete fast gate passed against the 72-test repository in 33.69s. Both retained their
`<15s` and `<60s` budgets.

## Browser-workspace remeasurement

Issue [#20](https://github.com/scwlkr/balls/issues/20) added the pinned Node/pnpm workspace and made
its standard commands visible in the same verifier. On the warm Windows checkout:

- `focused --web generate:check` passed the committed OpenAPI generated-client comparison;
- `focused --web test` passed all 3 React component tests;
- `fast` passed 80 portable .NET tests, 3 component tests, generation drift, both formatters,
  ESLint, TypeScript typecheck, and the Vite production build in 35.92s.

The local browser rendered the accessible Circle shell at the desktop viewport and at 390 by 844
CSS pixels. The mobile document width remained within its viewport with one Circle, one Member
region, and one Node region. Hosted Windows and Ubuntu evidence is recorded on the delivery pull
request after both fixed lanes execute the same `fast` plan.
