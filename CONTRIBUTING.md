# Contributing

Balls is in early architectural development. Start with the product and engineering documents
linked from [`README.md`](README.md), especially `PRINCIPLES.md`, `ARCHITECTURE.md`, and
`ROADMAP.md`.

## Before proposing code

- Use the repository issue forms. Every feature issue must state its user outcome, acceptance
  checks, non-goals, dependencies, risk/platform scope, verification, and documentation impact.
- Open an issue before a large product, protocol, trust, or storage change.
- Preserve the Circle-first model and the Core/Protocol/platform boundaries.
- Do not add secrets, private identifiers, real network details, or unsanitized diagnostics.
- Keep changes limited to the active roadmap slice and state explicit non-goals.

Balls source and documentation use [Apache License 2.0](LICENSE). By submitting a contribution,
you license it under the same terms without a CLA or copyright assignment. The repository remains
private, and outside contributions may not be merged until its reachable history is sanitized and
the owner explicitly approves public visibility.

## Verification

Run the repository gate before submitting a change:

```powershell
dotnet restore Balls.slnx --locked-mode
dotnet format Balls.slnx --verify-no-changes --no-restore
dotnet build Balls.slnx --configuration Release --no-restore
dotnet test Balls.slnx --configuration Release --no-build --no-restore
```

Update the relevant design, decision, protocol, security, or verification document when behavior
or a trust boundary changes. See [`docs/development.md`](docs/development.md) for the implemented
commands and [`docs/development-process.md`](docs/development-process.md) for the delivery process.
