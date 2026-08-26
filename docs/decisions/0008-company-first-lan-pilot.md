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

The immediate company use is a proof-oriented pilot, not a production availability, backup, or
disaster-recovery commitment. The hosting laptop and Windows Node need only remain on and reachable
during the pilot. The owner may use administrator access for one-time installation and host setup,
but the guided coworker flow must not require networking or SMB administration knowledge.

Balls is responsible for providing the authorized local file-sharing capability and preserving
normal SMB and application-requested locking behavior. Revit may use that shared folder, including
for workflows that Revit itself supports over a LAN, but Balls does not define or guarantee Revit's
central-model, local-model, synchronization, corruption-recovery, or multi-user semantics. A pilot
may record observed Revit behavior without turning that observation into a general product promise.

After the implementation-level two-VM proof, the owner will rehearse the next pilot on two
available administrator-managed Windows laptops: one hosts the new Circle folder and the other
joins as the invited Member. The pilot passes when the invitation and guided mapping make the
folder appear in Explorer and both laptops can create, open, edit, rename, and delete ordinary
files. The owner may additionally open and save a disposable Revit project and try Revit's normal
worksharing flow, but those observations do not change the product boundary above. Supported
software delivery follows the owner-accepted Release and downloads-portal policy. The private
invitation is transferred separately; a universal install-and-join link is deferred.

The Windows provider secures the Circle-contributed share itself: the SMB server must require
signing, reject unencrypted access, support per-share encryption, and grant access only to the
explicitly authorized limited Member account. Those server-side controls preclude guest access to
the Balls share. An unrelated existing outbound SMB client connection, including a legacy
guest-only host-mounted drive, must not be disabled or treated as permission to access the Circle
share. The owner's other working files and applications remain outside Balls' mutation authority.
Likewise, one exact Public-network TCP 445 block may preserve unrelated existing firewall rules
when it is independently verified to cover all SMB traffic and no authenticated bypass exists.
