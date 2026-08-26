# ADR 0016 — Coordinate Office Health and Recovery

- **Status:** Accepted
- **Date:** 2026-08-26

Balls provides one plain-language Office Health view across the Office Circle, Revit Server, Circle
Files, private-network reachability, backup freshness, storage, UPS, and the Balls service. It may
inspect, explain, alert, and offer an explicit supported repair plan, but it never silently mutates
an integrated provider to make a warning disappear.

Balls owns backup and tested restoration of Circle identity, authority, authorization, configuration,
provider-ownership records, and audit state. The selected backup product owns ordinary company files
and supported locked Revit Server repository copies. One recovery runbook coordinates both sets and
must prove that restored Circle intent still matches restored provider state.

The initial Revit backup operation runs after office hours, refuses to copy an unlocked live
repository, uses Autodesk's supported whole-Host lock and status operations, stages a consistent
copy, and unlocks before slow removable-disk or cloud transfer. Active work, incomplete lock, copy
failure, or failed unlock produces an alert rather than a forced or silently incomplete backup.
