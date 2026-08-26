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
