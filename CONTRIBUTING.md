# Contributing

Balls is in early architectural development. Start with the product and engineering documents
linked from [`README.md`](README.md), especially `PRINCIPLES.md`, `ARCHITECTURE.md`, and
`ROADMAP.md`.

## Before proposing code

- Open an issue before a large product, protocol, trust, or storage change.
- Preserve the Circle-first model and the Core/Protocol/platform boundaries.
- Do not add secrets, private identifiers, real network details, or unsanitized diagnostics.
- Keep changes limited to the active roadmap slice and state explicit non-goals.

The source license has not yet been selected. Until it is recorded, use issues for discussion;
outside code contributions may not be merged.

## Verification

Run the repository gate before submitting a change:

```powershell
dotnet restore Balls.slnx --locked-mode
dotnet format Balls.slnx --verify-no-changes --no-restore
dotnet build Balls.slnx --configuration Release --no-restore
dotnet test Balls.slnx --configuration Release --no-build --no-restore
```

Update the relevant design, decision, protocol, security, or verification document when behavior
or a trust boundary changes. See [`docs/development.md`](docs/development.md) for the complete
workflow.
