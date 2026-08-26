# ADR 0015 — Present Office Files Through One Circle Files Home

- **Status:** Accepted
- **Date:** 2026-08-26

Members use one Circle Files Home in Windows File Explorer rather than a drive letter, share path,
or setup action for every Office File Area. The Home contains only the areas authorized for that
Member and preserves each area's independent access and lifecycle. The provider implementation must
retain ordinary Windows SMB availability when the Balls interface is offline without creating a
parallel employee-access path that bypasses Circle authorization.

The initial provider realizes that experience as one encrypted, access-based-enumerated Windows SMB
share rooted at the new company-data tree. Each Member uses one provider identity and one persistent
mapping; exact Office File Area permissions determine both visibility and effective access. Hiding
an area is never treated as authorization, and direct access to an unauthorized child path must be
denied independently.
