# ADR 0012 — Run the Office Node as an Unattended Windows Service

- **Status:** Accepted
- **Date:** 2026-08-26

The Office Server Node runs `ballsd` as a restricted Windows Service rather than inside an Owner's
interactive login. The service starts with the server, recovers from bounded failures, and stops
cleanly with Windows shutdown while the graphical interface remains a client used for consent and
administration. This requires a deliberate service identity, protected-state custody, and narrow
privileged-operation design, but it prevents ordinary logout or reboot from silently removing the
Office Circle's capabilities.
