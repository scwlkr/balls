# ADR 0015 — Present Office Files Through One Circle Files Home

- **Status:** Accepted
- **Date:** 2026-08-26

Members use one Circle Files Home in Windows File Explorer rather than a drive letter, share path,
or setup action for every Office File Area. The Home contains only the areas authorized for that
Member and preserves each area's independent access and lifecycle. The provider implementation must
retain ordinary Windows SMB availability when the Balls interface is offline without creating a
parallel employee-access path that bypasses Circle authorization.
