# Office Server Integration Context

This context names the office-specific concepts needed to integrate the company server into Balls
without redefining the Circle or replacing the systems that provide its capabilities.

## Language

**Office Circle**:
The single Circle representing the currently trusted office group. Separately permissioned company
resources remain distinct Capabilities inside this Circle rather than becoming separate Circles.
_Avoid_: Company server, H and H Circle, Bubbas Circle

**Office Server Node**:
The Dedicated Node that supplies durable office capabilities to the Office Circle. It supports the
Circle but does not define or own the Circle.
_Avoid_: Balls Server, master server

**Office File Area**:
A separately permissioned tree of ordinary company files within the Office Circle. H and H and
Bubbas are separate Office File Areas even though they belong to the same Office Circle.
_Avoid_: Company Circle, Revit repository

**Revit Server Integration**:
The Circle-facing connection to Autodesk Revit Server that makes authorized use easier while Revit
Server remains the system responsible for its service, protocol, and workshared models.
_Avoid_: Balls Revit Server, Revit File Contribution

**Revit Server Capability**:
The bounded Office Circle service through which an authorized Member is prepared to use an existing
Revit Server. It grants use of the service, not ownership of or direct file access to its models.
_Avoid_: Revit share, Revit folder

**Office File Access**:
An Office Circle authorization to use one Office File Area through a mature file provider. Balls
owns the human grant while the provider remains responsible for transferring and locking files.
_Avoid_: SMB membership, Windows login

**Break-glass Access**:
Exceptional administrator access used only for recovery when the ordinary Circle workflow is
unavailable. It is not a parallel employee-access path.
_Avoid_: Fallback employee share, second login

**Capability Access Group**:
A named set of Office Circle Members who receive the same bounded Capabilities. It expresses human
policy without exposing provider accounts or operating-system groups.
_Avoid_: Windows group, shared login

**Office Anchor**:
The Office Server Node's role in keeping the Office Circle's live membership, authorization, and
coordination state available. A separate recovery copy prevents that Node from owning the Circle.
_Avoid_: Master server, only copy

**Circle Files Home**:
The single member-facing location containing every Office File Area that Member is currently
authorized to use. Unauthorized areas are absent rather than shown as inaccessible infrastructure.
_Avoid_: Drive collection, share list

**Server Administrator**:
A person explicitly authorized to change the Office Server Node and its provider installations.
Circle ownership does not automatically grant this separate operational authority.
_Avoid_: Circle Owner, every administrator

**Office Health**:
The combined plain-language view of whether the Office Circle and its integrated providers are
available, protected, and ready for their intended use.
_Avoid_: Provider console, automatic repair

**Managed Node Enrollment**:
The Owner-approved process that joins one Member's device to both the Office Circle and its private
network without making the Member handle a separate network credential.
_Avoid_: Shared network key, automatic Member device
