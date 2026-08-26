# Private Shared Ecosystem v1 Product Specification

- **Status:** Accepted product contract
- **Date:** 2026-08-26
- **Implementation boundary:** Only the Private Boss Demo slice is ready for implementation. Later
  slices require their own executable specifications after the demo is observed.

## Problem Statement

A small trusted group can already connect computers with a private LAN or Tailscale, share folders
with SMB, host an AI model, and run messaging software. The failure is the human experience: each
capability still requires separate machine settings, addresses, credentials, permissions, provider
knowledge, and instructions. Joining the group does not make the group's approved resources feel
like one usable environment.

Balls has also accumulated more security and architecture work than proven product value. If it
only wraps existing tools in a dashboard, it does not justify its complexity. It must prove that a
Circle makes several approved capabilities from several computers dramatically easier for ordinary
Members to use.

## Solution

Balls provides one graphical Circle experience above existing operating-system, network, storage,
messaging, and AI providers. A Member joins a Circle once, discovers the capabilities approved for
them, and uses those capabilities without configuring the underlying computers or providers.

Membership establishes Circle identity. A Node contributes a bounded Capability explicitly. A
Capability Grant authorizes a Member to use that Capability. Balls owns this Circle model, the
capability catalog, authorization intent, provider reconciliation, and the human workflow. It
integrates mature providers wherever they already solve the underlying technical problem well.

The product is proved in stages:

1. **Private Boss Demo:** official download, graphical join, and a usable shared folder in Windows
   File Explorer on a separate computer.
2. **Shared Ecosystem Proof:** the joined Member uses Circle Files, Circle Messaging, and Circle AI
   hosted by another Node, then one removal intent revokes future access coherently.
3. **Optional Balls Wizard:** a local, opt-in, docs-aware product guide that is separate from shared
   Circle AI and never becomes a dependency of the core product.

The thesis passes only if these journeys are materially easier than installing the underlying tools
and configuring each one separately. If they are not, platform expansion pauses and the product is
reconsidered.

## User Stories

1. As an Owner, I want to create a Circle graphically, so that I can establish a shared environment
   without designing infrastructure.
2. As an Owner, I want to invite a person once, so that I do not create separate identities for
   every capability.
3. As an invited person, I want to join from one invitation, so that I do not configure addresses,
   ports, credentials, or providers.
4. As a Member, I want the Circle to show people before machines, so that the environment reflects
   the group I joined.
5. As a Member, I want to see only capabilities approved for me, so that membership does not imply
   access to every machine or resource.
6. As a Node owner, I want contribution to be explicit, so that joining a Circle never exposes my
   whole computer.
7. As a Node owner, I want to contribute a bounded folder graphically, so that I do not configure
   SMB accounts, shares, or firewall rules myself.
8. As an Owner, I want to grant a Member read-only or read/write file access in human terms, so that
   I do not handle provider credentials or internal identifiers.
9. As a Member, I want an approved folder to appear through the normal file experience on my
   computer, so that I can use existing work applications.
10. As a Member, I want to open and edit a real shared file, so that Circle Files proves useful work
    rather than technical connectivity.
11. As a Member, I want approved capabilities from different Nodes presented together, so that the
    Circle feels like one ecosystem.
12. As a Member, I want to know which Node supplies a Capability when I inspect details, so that
    simple UX does not make the system mysterious.
13. As a nontechnical Member, I want provider terminology hidden from the normal path, so that I do
    not need to understand SMB, model runtimes, reverse proxies, or transport addresses.
14. As a technical Member, I want optional diagnostic details, so that I can understand failures
    without those details burdening everyone else.
15. As a Member, I want Circle Messaging to be available after joining, so that I can communicate
    with the people in the environment without installing a separate group tool.
16. As a Member, I want messages attached to Circle identities rather than machines, so that
    changing computers does not change who is speaking.
17. As a Member on a reachable private LAN, I want local messaging and local capabilities to work
    without public internet, so that the Circle is not merely a cloud account.
18. As a Node owner, I want to contribute an approved AI model as Circle AI, so that other Members
    can use it without learning its runtime address or credentials.
19. As an authorized Member, I want to use Circle AI from the Circle interface, so that a model
    hosted on another computer feels like a Circle capability.
20. As an Owner, I want to control which Circle context and tools Circle AI may use, so that model
    access does not imply access to every file, message, or action.
21. As an Owner, I want one Member-removal intent to stop future authorization across Circle Files,
    Circle Messaging, and Circle AI, so that I do not revoke each provider manually.
22. As an Owner, I want incomplete provider cleanup reported honestly, so that coherent revocation
    is not confused with erasing copies outside Balls' control.
23. As a removed Member, I want unavailable capabilities removed from the normal Circle experience,
    so that stale UI does not imply access I no longer have.
24. As a Node owner, I want a contribution to be removable without deleting the contributed user
    data, so that access lifecycle and data ownership remain separate.
25. As a small private company, I want Balls to use my existing LAN or owner-managed Tailscale
    network, so that I do not deploy a new public network service for the pilot.
26. As an Owner, I want Balls to integrate mature providers, so that the project spends effort on
    the shared human workflow rather than rebuilding storage, networking, or inference engines.
27. As an Owner, I want remote shell or desktop access treated as separately approved
    Capabilities, so that the seamless-connection analogy does not grant arbitrary machine control.
28. As a Circle, I want no ordinary personal computer to permanently define my identity, so that
    future resilience remains possible without blocking the pilot.
29. As a user, I want Balls to remain open source and eventually self-hostable, so that an optional
    hosted service never owns my Circle.
30. As a user obtaining Balls, I want one official human download entrypoint, so that I do not have
    to identify a trustworthy artifact myself.
31. As a user obtaining Balls, I want that entrypoint to resolve to an exact immutable GitHub
    Release asset with an honest channel status, so that the website does not become a second
    package store or misrepresent a Development build as accepted.
32. As an Owner, I want Alpha, Beta, and Stable promotion separately approved, so that a passing
    Development build cannot silently become a recommended release.
33. As a user, I want core Balls to work without an AI model, so that product use does not require a
    large optional download or suitable inference hardware.
34. As a user, I want a small bottom-right offer to download Balls Wizard, so that local product
    help is discoverable but never forced on me.
35. As a user who accepts that offer, I want Balls to retrieve and integrate the supported model and
    runtime, so that I do not configure local AI software myself.
36. As a user asking Balls Wizard a product question, I want an answer grounded in documentation
    matching my installed Balls version, so that the guidance fits the interface I am using.
37. As a user, I want Balls Wizard to cite the relevant guidance, so that I can verify its answer.
38. As an Owner, I want Balls Wizard to begin as read-only help, so that asking a question cannot
    silently change Circle administration.
39. As a user, I want Balls Wizard to run on the computer asking the question, so that product-help
    prompts are not sent to another Circle Member's model.
40. As an Owner, I want observed product friction recorded before another feature frontier is
    created, so that the roadmap follows evidence instead of architectural momentum.

## Implementation Decisions

- **Product boundary:** A Circle is the top-level product object. Balls is a graphical capability
  integration layer for a group of people; it is not a server manager, file-sharing utility,
  remote-shell product, or dashboard over arbitrary services.
- **Authorization boundary:** Membership establishes identity only. Every usable Capability comes
  from an explicit Contribution and an explicit Capability Grant. Provider accounts and tokens
  enforce grants but never become Circle identity.
- **Ownership boundary:** Balls owns Circle membership, Contributions, Capability Grants, catalog
  and discovery, reconciliation intent, and the provider-free user experience. It integrates the
  data plane through typed Capability Providers.
- **Provider strategy:** Use mature providers for storage, private connectivity, messaging
  transport, model inference, and operating-system integration unless a measured product gap
  requires Balls-owned machinery.
- **First implementation slice:** Circle Files on two Windows Nodes is the only ready implementation
  slice. Its accepted behavior is defined by the Private Boss Demo specification.
- **Second product gate:** Circle Files, Circle Messaging, and Circle AI must be usable by one joined
  Member before adding more provider categories. Circle Messaging means human communication, not
  internal Node or service protocol traffic.
- **Revocation semantics:** One removal intent stops future Balls authorization across reachable
  providers and reports reconciliation state. Balls does not claim to erase prior downloads,
  screenshots, exports, or provider state it cannot prove it owns.
- **Remote administration:** SSH, remote terminal execution, RDP, and similar access are separate,
  typed, explicitly granted future Capabilities. They are not implied by Membership or Node
  participation.
- **Network posture:** The current audience is approximately two or three personally trusted people
  over a private LAN or owner-managed Tailscale network. Public-internet exposure is not part of
  this version.
- **Safety floor:** Preserve operating-system protections, private-service boundaries, credential
  confidentiality, contributed user data, and explicit access approval. Additional security work
  must answer a concrete pilot risk, observed failure, or accepted release requirement.
- **Distribution:** `balls.wlkrlabs.com` is the only official human entrypoint for software and
  updates. It presents accepted Alpha first, warned Development builds below it, and exact previous
  versions. Every lane links to immutable GitHub Release assets and the site does not host a second
  copy. Invitations are private Circle data and never pass through the public download channel.
- **Publication:** An agent working an active issue may publish an identity-verified Development
  prerelease and move its pointer after package-integrity checks. Alpha, Beta, and Stable promotion
  requires separate Owner approval and reuses the exact tested assets.
- **Balls Wizard identity:** Balls Wizard is the optional on-device product guide represented by a
  floating brand-violet ball wearing a wizard hat. It is not Circle AI.
- **Balls Wizard installation:** The base Balls package does not contain or automatically download
  the model. A bottom-right prompt lets the user opt in; after consent, Balls manages model and
  runtime setup and exposes clear progress, size, storage, hardware-support, failure, retry, and
  removal states.
- **Balls Wizard model:** The accepted model family is the instruction-tuned Gemma 4 E2B. The exact
  pinned quantized artifact, runtime, hardware floor, and package channel require measured
  compatibility evidence in a later executable specification.
- **Balls Wizard knowledge:** Guidance retrieves documentation matching the installed Balls
  version, identifies the source used, and starts with read-only explanatory behavior. Core product
  workflows cannot depend on Balls Wizard being installed, running, or supported by the hardware.
- **Architecture boundary:** User interfaces and CLI clients call the same local application
  behavior through the local API and daemon. Platform mutation stays behind typed OS adapters;
  provider details stay out of the Circle domain.
- **Roadmap boundary:** Product canon may describe later pillars, but only a separately accepted,
  testable issue specification authorizes implementation.

## Testing Decisions

- Tests assert external human and provider behavior, not private class structure, cryptographic
  internals, or incidental UI markup.
- The preferred product seam is one installed Member Node using its local Balls interface against
  live Capabilities contributed by a separate installed Node. This seam crosses the UI, local API,
  daemon, persisted Circle state, transport, provider adapter, and real user-facing result.
- Each implementation slice should have one dominant end-to-end journey at that seam. Lower tests
  exist only to make failures precise or cover destructive/error cases that the high seam cannot
  exercise safely.
- The Private Boss Demo uses the existing packaged-application browser journey, Windows provider
  lab, and prior two-computer Circle Files pilot as prior art.
- The Shared Ecosystem Proof must exercise Files, Messaging, and Circle AI from the same joined
  Member state, then remove that Member once and observe authorization and reconciliation results
  for all three.
- Balls Wizard testing begins only with its later executable specification. Its highest seam is the
  local browser offering an explicit download, completing managed setup, answering from
  version-matched docs through the real local model, citing guidance, and leaving core Balls usable
  after Wizard removal or failure.
- Release evidence labels the exact environment observed. Same-host two-VM, separate physical
  device, and unobserved claims never substitute for one another.
- Public distribution is verified by exact tag, commit, package identity, SHA-256, and website
  channel readback. Development uses the bounded publication authority above; accepted-channel
  readback follows Owner approval.

## Out of Scope

- Public-internet service exposure or enterprise-scale adversarial security.
- Rebuilding Tailscale, SMB, model runtimes, reverse proxies, or other mature providers without a
  measured product gap.
- Arbitrary terminal execution, SSH, RDP, or whole-computer control in the initial product proof.
- Distributed inference, distributed model training, or generalized compute scheduling.
- Multi-Anchor replication, automatic failover, offline file synchronization, conflict merging,
  file version history, or Balls-managed trash in the initial proof.
- Rich messaging features beyond the minimum Shared Ecosystem Proof.
- Circle Apps or broad internal-service orchestration before the Shared Ecosystem Proof passes.
- Automatic Balls Wizard installation, silent model downloads, or Wizard authority to perform
  administrative mutations.
- A claim that Balls is uniquely able to provide service discovery or access revocation; the claim
  under test is that the Circle experience is materially simpler and more coherent for its users.

## Further Notes

The existing Circle identity, LAN admission, Circle Files, protected-state, Windows provider, and
browser work is reusable implementation evidence. This specification resets the product sequence;
it does not invalidate functioning code or historical verification.

Architecture language such as Anchor, provider, generation, or reconciliation remains available to
technical users and internal design documents. It must not become the vocabulary required to join
and use an ordinary Circle.
