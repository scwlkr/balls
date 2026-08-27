# Revit Server Rapid Setup v0

- **Status:** Accepted
- **Date:** 2026-08-27
- **Priority:** First bounded product issue after #103 closes #92
- **Decision:**
  [`ADR 0018`](../decisions/0018-prioritize-revit-server-rapid-setup.md)
- **Parent target:**
  [`Run-and-gun Office Server v1`](run-and-gun-office-server-v1.md)

## Outcome

On the Owner's Linux laptop, a separately prepared disposable Windows Server 2022 Desktop
Experience VM runs one exact Balls Development build. From a ready VM and locally cached official
Autodesk media, the Owner uses a graphical **Set up Revit Server 2027** workflow and receives a
truthful healthy result in less than 30 minutes of wall-clock time.

The workflow installs Revit Server 2027 with exactly the Host and Admin roles, no Accelerator,
version-isolated storage, a server-local Host identity, required Windows/IIS prerequisites, and no
public exposure. It verifies the Revit Server services and Administrator surface, then exports a
portable Setup Template and a redacted Setup Receipt that the Owner can give to the boss for the
eventual physical server.

This is an installation-health proof. It does not prove that a Revit workstation can open, save,
or synchronize a model.

## Priority and issue boundary

The already-approved Alpha promotion in issue #103 completes first and closes the Private Boss Demo
issue #92. Revit Server Rapid Setup is then one independently mergeable issue and the only active
feature outcome until its acceptance is recorded.

This bounded issue temporarily precedes, but does not cancel:

- the Shared Ecosystem Proof;
- Office Circle Files and access groups;
- Tailscale and remote Revit use;
- a production Office Server Node, Windows Service, and Anchor;
- full Revit Server Capability onboarding, backup, recovery, and model proof.

## Human experience

The complete timed path is:

```text
open Balls Development
        -> Set up Revit Server 2027
        -> select cached official Autodesk installer
        -> inspect Ready / Blocked preflight
        -> review Host+Admin / no-Accelerator plan
        -> approve one Balls-owned Windows elevation
        -> Balls prepares Windows and launches Autodesk setup
        -> accept Autodesk terms and confirm the displayed choices
        -> return to Balls and verify
        -> receive PASS and export boss handoff
```

The Owner does not manually:

- add Windows roles or IIS features;
- create or repair Default Web Site;
- create repository folders or edit their ACLs;
- set the Revit Server role variable;
- create server-local `RSN.ini`;
- configure firewall rules;
- inspect application pools, IIS applications, services, or logs;
- calculate installer, package, plan, or receipt identities.

Autodesk's graphical license acceptance and Revit Server configuration remain explicit human
steps. Balls does not accept third-party terms or guess undocumented silent-installer settings.
Autodesk's signed installer may display its own Windows publisher consent; that prompt is counted
and recorded separately from Balls' prerequisite elevation.

## Disposable laptop lab

The issue creates lab infrastructure, not a VM-management product feature.

The acceptance server is a new isolated Windows Server 2022 Standard Evaluation VM with Desktop
Experience, hosted by the Linux laptop's existing Docker/QEMU/KVM stack. It does not replace,
modify, or nest inside the existing Windows 11 or Neptune VMs.

Initial lab defaults are:

- separate VM/container, storage, virtual disks, network identity, and loopback console ports;
- 4 virtual CPUs;
- 8 GiB RAM;
- a 128–160 GiB sparse system disk;
- a separate fixed NTFS virtual data disk mounted as `D:`;
- a temporary hostname selected before Revit installation;
- a laptop-private network only;
- no Linux shared-folder mount beneath `D:\RevitServer`;
- no company data or production model.

The existing high-memory Windows VMs remain stopped while this server VM runs unless a separate
resource plan is explicitly accepted. Normal operation may be headless. A graphical web console or
RDP session is allowed for initial Windows preparation, the Autodesk installer, and troubleshooting.

Autodesk does not list this QEMU/KVM environment as a supported hypervisor. Evidence from this lab
proves the Balls workflow only. The boss's physical Windows Server requires its own later acceptance.

Before creating or operating the VM, the implementation issue must update and follow
[`windows-development-lab.md`](../windows-development-lab.md) with exact VM ownership, start/stop,
recovery, networking, and mutual-exclusion instructions.

## Ready gate and timer

VM creation is outside the 30-minute Revit setup timer. The timed run may start only after the
checklist records all of the following:

- the disposable VM is booted into updated Windows Server 2022 Desktop Experience;
- Windows reports no pending restart;
- the final temporary hostname and fixed NTFS `D:` volume are present;
- the exact Balls Development package is installed and launches normally;
- the official Revit Server 2027 installer is already downloaded, extracted locally, and selected;
- no Revit Server 2027 installation or foreign `D:\RevitServer\2027` state exists;
- the VM is on the bounded local lab network with no public-profile or Internet exposure;
- a monotonic wall-clock source and evidence location are ready.

IIS and Revit Server prerequisites may be absent at the ready gate. Installing them is part of the
timed Balls workflow. A required restart makes that timed run `BLOCKED`; the issue does not hide the
restart outside the clock and resume the same attempt as a pass.

The timer starts when the Owner selects **Begin setup** on a Ready result. It ends only when Balls
shows the final PASS receipt and has produced the portable handoff bundle. Passing elapsed time is
strictly less than `00:30:00`.

Record both wall-clock and human-intervention time. Wall-clock time is the acceptance gate; waiting
for Windows features and Autodesk installation still counts.

## Setup plan

Balls computes one approval-bound, immutable plan from fresh inspection. The preview contains no
internal object IDs or secrets, but clearly shows:

- detected Windows edition and build;
- verified Autodesk installer publisher, product, version, and SHA-256;
- enabled Revit roles: `Host,Admin`;
- forbidden role: `Accelerator`;
- repository root: `D:\RevitServer\2027`;
- Projects and any installer-required Cache paths beneath that version root;
- the portable ACL intent for `NETWORK SERVICE` and `CREATOR OWNER`;
- the exact Windows/IIS prerequisites from Autodesk's Server 2022 guidance;
- Default Web Site inspection or creation;
- the temporary canonical Host name;
- server-local 2027 `RSN.ini` content;
- exact local/private firewall effects;
- every verification action;
- Balls-owned state that can be repaired or removed;
- third-party Autodesk state that Balls will not silently uninstall or delete.

Changing the machine, installer, hostname, paths, network classification, roles, or inspected
prerequisites invalidates the plan and requires a new preview.

## Windows mutation boundary

Normal `ballsd` and the browser remain unelevated. One Revit-specific typed helper operation may:

- install the exact documented Windows Server 2022 IIS, ASP.NET, WCF, and compatibility features;
- ensure Default Web Site exists with the required local HTTP binding;
- create the empty version-specific data folders;
- apply the exact portable-principal ACLs required by Autodesk;
- add or narrow only the setup plan's local/private firewall rules;
- write Balls-owned setup state and redacted audit events.

The helper independently re-inspects the plan and refuses unsupported Windows, Server Core, a
pending restart, an untrusted installer, unexpected existing Revit roles, a non-fixed/non-NTFS data
volume, reparse traversal, foreign nonempty destinations, overlapping version paths, ambiguous IIS
state, or public network exposure.

The current Circle Files readiness inspector is not the Revit Server OS gate. Revit setup receives
its own platform contract and Windows adapter so Windows Server 2022 support cannot loosen the
separate SMB provider policy.

The helper does not remain attached to Autodesk's long-running graphical installer. Balls persists
the bounded setup stage, ends elevation, launches the verified official installer unelevated or
through its own required Windows consent, and resumes postflight afterward.

Minimum persisted stages are:

```text
not-started
  -> ready
  -> prerequisites-applied
  -> awaiting-autodesk
  -> verifying
  -> ready-for-handoff | incomplete | failed | blocked
```

Retry re-inspects reality and resumes or explains the next supported action. Balls never deletes a
Revit repository, uninstalls Autodesk software, or rewrites ambiguous third-party state to obtain a
green result.

## Autodesk handoff

Balls launches the verified official Revit Server 2027 installer and keeps a short exact instruction
card visible:

```text
Product: Revit Server 2027
Roles: Host + Admin
Accelerator: off
Projects: D:\RevitServer\2027\Projects
Cache: the displayed version-specific path, if requested
```

The Owner personally accepts Autodesk's terms, opens the installer configuration page, confirms the
displayed choices, and selects Install. Skipping the configuration page is a hard failure because
Autodesk otherwise installs Accelerator-only defaults.

Balls may later support a silent path only after Autodesk documents, or a separate controlled
prototype proves, a Revit Server 2027-specific unattended configuration contract. Generic Revit
silent flags are not sufficient authority.

## Health-only acceptance

Postflight reports `PASS` only when fresh inspection proves all of the following:

- the installed product is Revit Server 2027;
- enabled roles are exactly `Host,Admin`;
- no Accelerator role or `RSACCELERATOR2027` configuration is active;
- the configured Projects path is the exact approved version-specific path;
- required service principals have the approved access to the data paths;
- Default Web Site and expected versioned Host/Admin IIS applications exist;
- the Revit Server 2027 application pool is started in Integrated mode;
- expected local Revit/IIS endpoints respond without IIS error pages;
- server-local `RSN.ini` contains exactly the temporary canonical Host identity;
- the Revit Server Administrator page loads and displays that Host with an empty usable project tree;
- observed Revit Server and IIS logs contain no fatal setup/runtime error;
- `D:\RevitServer\2027` is not exposed through SMB, Circle Files, a Linux shared mount, or a
  reparse path;
- no required listener or firewall rule is exposed through a Public profile or public host bind;
- the final handoff bundle is complete and internally hash-consistent;
- measured wall-clock time is under 30 minutes.

The exact passing claim is:

> **PASS — Revit Server 2027 Host+Admin installation and Administrator surface are healthy in the
> disposable QEMU/KVM lab. Revit client/model use, synchronization, concurrency, performance,
> backup, recovery, remote access, and production hardware were not tested.**

`INCOMPLETE`, `FAILED`, or `BLOCKED` never renders as healthy. A plain result explains the observed
failure and next supported action without exposing raw command output, credentials, installer
arguments, or machine secrets.

## Portable boss handoff

Balls exports:

```text
revit-server-2027-setup-bundle.zip
├── setup-template.v1.json
├── setup-receipt.v1.json
├── README.md
└── bundle-manifest.json
```

The **Setup Template** contains portable intent only:

- Windows Server 2022 and Revit Server 2027 constraints;
- exact Host+Admin/no-Accelerator policy;
- version-relative storage layout;
- required Windows/IIS features and portable ACL principals;
- expected official installer publisher/product/version/hash metadata;
- private-LAN-only policy;
- health checks and explicit non-goals.

The **Setup Receipt** records the temporary proof:

- exact Balls tag, commit, package identity, and SHA-256;
- exact Autodesk installer signature, identity, and SHA-256;
- Windows edition/build and a bounded machine fingerprint;
- approved plan digest and Balls-owned changes;
- installed Revit roles/version and observed health states;
- resolved temporary paths and Host identity;
- start/end timestamps, wall-clock time, human interventions, and outcome;
- every untested scenario from the exact PASS claim.

`README.md` gives the boss the short repeatable flow. The manifest versions the schemas and hashes
the other bundle files. The bundle contains no credentials, private Circle material, Windows SIDs,
Autodesk installer, VM image, company data, model data, or executable script.

The first issue does not build a configuration-import system. The boss follows `README.md` and runs
the same Balls setup page on the future server, using the template as the approved choices and the
receipt as proof of what previously worked. Balls re-inspects that machine, resolves its real fixed
NTFS data volume and Host identity, verifies official media again, and computes a new target-specific
preview. It never replays the temporary machine's hostname, IP address, drive identity, paths, SIDs,
firewall bindings, or approved plan.

## Implementation slices inside the one issue

1. **Typed setup contract**
   - introduce Revit-specific inspect, preview, apply, stage, verify, and export results;
   - keep platform commands and Autodesk types outside Core;
   - define redacted stable error/outcome tokens.
2. **Windows setup adapter**
   - implement read-only inspection first;
   - implement the narrow prerequisite helper operation;
   - persist the Autodesk handoff boundary and idempotent postflight;
   - refuse ambiguous or foreign state.
3. **Development browser workflow**
   - add one Server Administrator-only setup page;
   - present readiness, exact preview, progress, handoff instructions, retry, result, and export;
   - keep provider jargon out of the default path while exposing inspectable details.
4. **Portable handoff**
   - generate and validate the template, receipt, README, and manifest;
   - exclude machine-specific replay material and explain the boss's fresh setup flow.
5. **Timed Windows lab proof**
   - create the separate disposable Server 2022 VM under the updated runbook;
   - install the exact Development package through the official distribution path;
   - run the complete checklist and record exact evidence.

## Automated verification

Focused tests must cover:

- supported and rejected Windows editions, builds, and installation types;
- missing prerequisites, pending restart, missing Default Web Site, and restart-required results;
- trusted and substituted Autodesk installer identities;
- exact role/path/ACL/firewall preview and plan invalidation;
- reparse, foreign destination, existing-version, and public-network refusal;
- helper request authentication, timeout, cancellation, and redaction;
- stage persistence across browser and daemon restart;
- Host+Admin verification and Accelerator detection;
- health result refusal on any missing or ambiguous observation;
- browser readiness, consent, Autodesk handoff, retry, receipt, and plain failure states;
- template portability and machine-specific replay exclusion;
- bundle schema bounds, internal hashes, and secret/private-material exclusion;
- wall-clock measurement and the strict `< 00:30:00` acceptance comparison.

The manual Windows checklist remains decisive for the provider installation and elapsed-time claim.
Linux mocks, PowerShell parsing, unit tests, or an IIS-only harness cannot replace it.

## Explicit non-goals

- Balls-created or Balls-managed Windows VMs;
- Windows Server Core;
- native Linux or Windows 11 Revit Server hosting;
- silent Autodesk license acceptance or undocumented installer automation;
- Revit workstation installation or `RSN.ini` deployment to clients;
- central model creation, save, open, synchronize, new-local, or multi-user proof;
- production certification on QEMU/KVM;
- Tailscale, MagicDNS, WAN access, remote performance, or Accelerator;
- Office Circle Membership, Revit Capability Grants, or Member removal;
- Circle Files, SMB company shares, or ordinary-file access groups;
- `ballsd` Windows Service, Office Anchor, or unattended Circle durability;
- backup lock/stage/unlock, restore, RAID, UPS, or Office Health;
- Revit upgrades, migration, uninstall, repository repair, or model manipulation;
- Alpha/Beta/Stable publication, Authenticode signing, or broad platform support;
- production company data or model data;
- automatic handoff-bundle import or machine-plan replay.

## Honest blockers

The issue is `BLOCKED` rather than broadened when:

- Windows Server 2022 Desktop Experience cannot run reliably in the isolated laptop lab;
- the exact Development build is rejected unless Windows protection is weakened;
- Autodesk media cannot be verified or requires unsupported automation;
- required prerequisites or Autodesk installation require a restart during the timed attempt;
- Host+Admin cannot be obtained without Accelerator;
- health cannot be established without a licensed Revit client or model operation;
- the Admin surface requires public exposure;
- ambiguous existing state cannot be reconciled without deleting third-party or user data;
- the complete warm-path run cannot finish in under 30 minutes.

Any blocker may justify a separately accepted scope or timing decision. It does not silently weaken
the passing claim.

## Primary references

- [Autodesk: Revit 2027 product system requirements](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/System-requirements-for-Revit-2027-products.html)
- [Autodesk: Install and Configure Revit Server](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-Installation/files/GUID-B4C2C529-26D8-461B-B06A-7E65744A3C72.htm)
- [Autodesk: Install Server System Prerequisites for Windows Server 2022](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-Installation/files/GUID-BF12B6F3-F69B-4ABC-8F37-52A83945DB68.htm)
- [Autodesk: About the RSN.ini File](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-Installation/files/GUID-00163A5A-1379-4743-87B7-DBBBBF00FC93.htm)
- [Autodesk: Check Revit Server basic functionality](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-check-Revit-Server-for-basic-functionality.html)
- [Microsoft: Windows Server installation options](https://learn.microsoft.com/en-us/windows-server/get-started/install-options-server-core-desktop-experience)
