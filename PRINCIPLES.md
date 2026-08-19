# Product and Engineering Principles

These principles are more important than any current framework, protocol, UI toolkit, or implementation detail.

## 1. The Circle is the product

Do not make the product model revolve around one "server computer."

Users join Circles.

Nodes support Circles.

## 2. People first, machines underneath

Normal users should primarily see:

- people;
- chat;
- files;
- AI;
- apps;
- services.

Technical users and admins can inspect:

- nodes;
- hardware;
- networking;
- runtime placement;
- logs;
- resource use.

## 3. Simple by default, inspectable by design

Do not choose between "magic black box" and "sysadmin console."

Default UX should be approachable.

The system should still expose enough detail for a slightly technical user to understand and customize it.

## 4. Explicit contribution

Joining a Circle never means giving that Circle unrestricted access to a machine.

Every meaningful capability is contributed intentionally.

## 5. No accidental remote administration

Balls may execute approved workloads and manage approved Circle resources.

It is not automatically a general-purpose unrestricted remote shell, spyware platform, or hidden remote-control mechanism.

Any remote administration capability must be separately defined and permissioned.

## 6. No single ordinary machine owns the Circle

Turning off the creator's laptop must not conceptually destroy the Circle.

Durable state belongs to the Circle and can be replicated through Anchor Nodes.

## 7. Local capability matters

When the internet is unavailable, reachable local Circle capabilities should continue working wherever technically possible.

## 8. Local first does not mean local only

A Circle may include:

- LAN devices;
- remote personal devices;
- dedicated servers;
- VPSs;
- cloud VMs.

## 9. Balls Cloud is optional infrastructure, not ownership

An official hosted service can make setup dramatically easier.

Core design must avoid making the Circle synonymous with a record in Balls-the-company's database.

## 10. Open source is part of trust

A Circle should be able to inspect, understand, and eventually self-host the important infrastructure on which it depends.

## 11. Cross-platform is architectural, not cosmetic

Windows is the first polished client, but core concepts cannot depend on Windows.

Linux, macOS, headless servers, and VPSs must fit the same model.

## 12. Native node foundation

Do not define Balls as a WSL application.

WSL may be a workload runtime on Windows.

The Node foundation should run natively on the host OS.

## 13. One core, multiple interfaces

GUI, CLI, web UI, integrations, and apps should call stable APIs around the same underlying system.

Do not reimplement product logic separately in each interface.

## 14. OS integration belongs behind adapters

Core product rules should not contain raw Windows, Linux, or macOS commands.

Platform-specific behavior belongs behind typed interfaces/adapters.

## 15. Privilege is narrow

Do not run the entire system as Administrator/root just because some operations need privilege.

Privileged actions should be narrow, explicit, auditable, and recoverable.

## 16. Protocol over transport

Circle identity, authorization, and application behavior must not be defined by a specific network product.

Tailscale may be an excellent initial transport.

It is not the definition of Balls.

## 17. Product abstraction over implementation

Examples:

- Circle Files is the feature; SMB is one provider.
- Circle AI is the feature; Ollama or another runtime is one provider.
- Circle networking is the feature; Tailscale is one provider.
- Circle Apps is the feature; containers are one possible runtime.

## 18. Do not fake distributed computing

Aggregate resources do not magically create one computer.

Balls should expose real capabilities that appropriate distributed workloads can actually use.

## 19. Build vertical slices

Do not attempt the entire vision at once.

Each milestone should produce a coherent useful experience while preserving the architecture needed by the larger system.

## 20. Preserve the dream without overbuilding the present

A milestone can be small.

Its architecture should not redefine the project into something smaller than the mission.
