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
you license it under the same terms without a CLA or copyright assignment.

## Verification

Run the fast repository gate before pushing:

```powershell
dotnet run --project eng/Balls.Verify --configuration Release -- fast
```

Use `focused` while editing and `full` for the complete Windows gate. The same commands work in
PowerShell, Bash, and other shells supported by the .NET CLI. See
[`docs/development.md`](docs/development.md) for examples and the commands each mode runs.

Update the relevant design, decision, protocol, security, or verification document when behavior
or a trust boundary changes. See [`docs/development-process.md`](docs/development-process.md) for
the delivery process.
