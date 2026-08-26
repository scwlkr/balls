# ADR 0014 — Use Access Groups and Bounded Office File Areas

- **Status:** Accepted
- **Date:** 2026-08-26

Office authorization is expressed through human-readable Capability Access Groups and separately
bounded Office File Areas rather than raw Windows groups or arbitrary nested ACL editing. Each true
permission boundary becomes an Office File Area whose ordinary descendants inherit that access.
Provider accounts and permissions enforce the resulting grants without becoming Circle identity.

The initial Office Circle grants its Revit Server Capabilities to all current Members by default;
this is an explicit default Capability policy, not a rule that Membership authorizes every Circle
Capability. Every new Node still requires separate Owner approval. Because the company data layout
is new, the initial office-server milestone creates fresh Office File Areas and does not require a
legacy-file or legacy-permission migration feature.

Accounting Office File Areas are granted only to an explicit Accounting access group. A Member's
Capability Grants become usable from each of that Member's separately Owner-approved Nodes; Node
approval does not create new Member authorization, and an unapproved Node receives nothing.

The ordinary policy needs only Shared Office File Areas for everyone and Restricted Office File
Areas for selected groups or people. Removing a Member stops their future Balls-managed provider
authorization and approved-device reachability, preserves company data, and reports unreachable
cleanup as pending without requiring an enterprise identity or offboarding system.
