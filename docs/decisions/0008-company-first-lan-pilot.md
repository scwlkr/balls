# ADR 0008 — Prove the Owner's Two-person Company LAN Workflow First

- **Status:** Accepted
- **Date:** 2026-08-25

Balls remains open source and Circle-first, but its immediate proving ground is the owner's own
company rather than every possible organization. The Owner creates a new Windows-hosted project
folder on the private LAN, invites one trusted coworker, and that ordinary Member receives usable
Explorer access on a separate Windows computer without manually discovering IP addresses, provider
credentials, grants, or mapping plans. Verify the complete journey privately before inviting the
real coworker, using the owner's existing working Windows VM as the folder host and its existing
small disposable Windows desktop VM as the immediate ordinary-coworker simulator. A separate
physical Windows laptop is deferred because its standard-user account reports Smart App Control
blocking the unsigned development build. Preserve that boundary: do not bypass policy; treat
trusted code signing or an authorized administrator-managed application policy as a later
owner-approved prerequisite for physical deployment. The invited Member must not need local
administrator privileges, and two-VM evidence must never be mislabeled as physical-LAN proof.
Existing-folder adoption, remote connectivity, broader infrastructure providers,
AI, Apps, and other platform ambitions follow that first useful outcome instead of delaying it.

The Windows provider secures the Circle-contributed share itself: the SMB server must require
signing, reject unencrypted access, support per-share encryption, and grant access only to the
explicitly authorized limited Member account. Those server-side controls preclude guest access to
the Balls share. An unrelated existing outbound SMB client connection, including a legacy
guest-only host-mounted drive, must not be disabled or treated as permission to access the Circle
share. The owner's other working files and applications remain outside Balls' mutation authority.
Likewise, one exact Public-network TCP 445 block may preserve unrelated existing firewall rules
when it is independently verified to cover all SMB traffic and no authenticated bypass exists.
