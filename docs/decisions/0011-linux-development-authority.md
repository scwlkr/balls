# ADR 0011 — Linux Development Authority and Triggered Windows Conformance

- **Status:** Accepted
- **Date:** 2026-08-27

Linux is the Balls **Development Authority**: the environment that owns source editing, local
builds, focused tests, the fast gate, daemon and CLI execution, and development of the shared
browser application. Portable Core, protocol, persistence, LAN transport, daemon, CLI, and browser
behavior is designed and proved there by default. This matches the Owner's development workstation
and keeps the ordinary feedback loop fast without redefining the product as Linux-only.

Windows is a **Triggered Conformance Target**: a native platform that must provide evidence when a
change affects Windows contracts, adapters, privileged helpers, security behavior, Capability
Providers, packaging, launch behavior, Windows-specific error translation, or an accepted Windows
journey. Use the smallest native scenario that covers the changed risk. A required failed Windows
check blocks the affected change; successful Linux verification cannot override contradictory
native evidence. Automatic hosted Windows CI remains required clean-platform coverage and is
distinct from developer-operated Windows VM work.

**CLI-first verification** means that, before using a Windows VM or manipulating a Windows desktop,
a developer determines whether the claim can be established through the canonical CLI, a typed
local API, a headless browser, or a bounded repository-owned conformance script. If it can, that
headless path is mandatory. A conformance script invokes the compiled Balls product through those
public seams and independently inspects resulting Windows state; it does not reimplement Circle
rules, authorization, validation, rollback, persistence, or provider lifecycle behavior.

Interactive Windows evidence is reserved for claims that are inherently visual or consent-bound:
user-visible UAC consent, native folder-picker behavior, File Explorer presentation or location,
application-control prompts, and final graphical release acceptance. This is CLI-first
verification, not a CLI-first product. Balls remains graphical and provider-jargon-free, Windows
remains the first polished client, and the current Circle Files Capability Provider remains the
Windows SMB provider.

Every developer-operated Windows run starts by inspecting the live target, account and privilege
context, policy, network, source or package identity, authorization, and dirty state. Execution is
bounded and binds its evidence to the exact commit or immutable artifact. Results are structured
and redacted, preserve unrelated user state, and never emit credentials, invitations, private
keys, passwords, DPAPI material, or provider secrets.

This decision trades broader Linux test-harness responsibility and maintenance of focused native
entrypoints for shorter feedback loops and less desktop operation. It does not make a Linux
simulation evidence of Windows permissions, elevation, DPAPI, SMB, firewall, credentials,
mappings, packaging, or policy behavior. It introduces no production Linux Circle Files Provider,
does not weaken operating-system protections, and does not remove final graphical Windows release
acceptance.
