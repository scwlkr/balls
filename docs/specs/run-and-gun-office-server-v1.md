# Run-and-gun Office Server v1

- **Status:** Draft for Owner confirmation
- **Date:** 2026-08-26
- **Source:** Office File + Revit Server — Authoritative Architecture Specification
- **Execution boundary:** Design work may proceed in this worktree. Product implementation starts
  only after the Private Boss Demo issue #92 is complete or this work is separately authorized as a
  concrete blocker.

## Outcome

Set up one new Windows Server 2022 office machine for two or three trusted people. Balls makes the
company's ordinary files and Autodesk Revit Server 2027 easy to set up and use without replacing
Windows SMB, Tailscale, Autodesk Revit Server, the backup product, RAID, or the UPS.

The shortest successful employee experience is:

```text
install exact Balls Development build
        -> join Office Circle
        -> Owner approves this computer
        -> open Office Circle Files in Explorer
        -> use shared or restricted company folders
        -> open Revit 2027 against the approved Revit Server Host
```

Employees do not handle SMB credentials, share names, drive letters, Tailscale keys, IP addresses,
ports, `RSN.ini`, or Revit Server administration.

## Simple system model

- There is one **Office Circle**.
- The Windows Server machine is the **Office Server Node** and first **Office Anchor**.
- All Members may use Revit Server 2027.
- Every Member uses one **Circle Files Home** in Explorer.
- A **Shared Office File Area** is available to everyone.
- A **Restricted Office File Area** is available only to selected groups or people.
- One explicitly authorized **Server Administrator** initially manages Windows Server and Revit
  Server administration.
- Every employee computer requires separate Owner approval before it receives that Member's
  Capabilities.

## Provider boundaries

### Ordinary files

Windows SMB remains the file server. Balls provides the Circle-facing setup, grants, mapping,
revocation, and repair workflow.

The initial shape is one encrypted, access-based-enumerated SMB share:

```text
D:\CompanyData
  -> \\<canonical-office-server>\OfficeCircleFiles

Office Circle Files
├── H and H
│   ├── Projects
│   ├── Accounting
│   ├── General
│   └── Revit Content
├── Bubbas
│   ├── Marketing
│   ├── Operations
│   ├── Accounting
│   └── General
└── Shared
```

Access policy remains simple:

- every Member may use Shared;
- only selected Members or access groups may use restricted areas such as Accounting;
- unauthorized areas are absent from Explorer and remain denied when addressed directly;
- one Member uses one provider identity and one persistent Explorer mapping;
- Windows SMB continues serving already provisioned access while `ballsd` is temporarily stopped;
- administrator break-glass access is recovery-only, not a second employee-access system.

This is a new company layout. No legacy folder, share, group, or ACL migration feature is required.

### Revit Server 2027

Autodesk Revit Server remains responsible for workshared models. Balls never reads, shares, moves,
synchronizes, repairs, or interprets its model repository.

The initial Revit shape is:

- Windows Server 2022;
- Revit Server 2027 Host and Admin roles;
- one version-specific repository such as `D:\RevitServer\2027\Projects`;
- no Revit Server Accelerator in v1;
- all approved Office Circle Members may reach the Revit model service;
- only the Server Administrator may reach the Revit administration interface;
- one frozen Tailscale MagicDNS Host name is written to every approved Revit 2027 client's
  version-specific `RSN.ini`;
- every Revit client uses Tailscale, including on the office LAN, so the Host identity never changes;
- central models are opened only through Revit's Revit Server workflow.

Balls may inspect prerequisites, create an exact setup plan, configure supported Windows roles and
firewall rules with consent, guide the official Autodesk installer, deploy the canonical `RSN.ini`,
check service health, and invoke Autodesk's documented lock/status/unlock backup commands.

Balls does not automate Autodesk licensing, accept Autodesk terms, create central models, perform
licensed user synchronization, or make restore decisions.

### Tailscale

Tailscale remains the private network. Balls uses an Owner-created, narrowly scoped trust credential
to create one-time enrollment material after the Owner approves a Node. Employees do not receive or
reuse network secrets.

Balls must:

- accept Tailscale addressing as a private transport;
- enroll and remove approved Nodes without redefining Circle identity;
- apply only the access needed for SMB and Revit use;
- keep the Revit administration surface limited to the Server Administrator;
- show whether a path is direct or relayed;
- never publicly expose SMB, Revit Server, RDP, PowerShell, or the Balls control surface.

### Backup, storage, and UPS

The selected office backup product owns company-file and Revit-repository copies. Balls owns backup
and restoration of its Circle identity, authority, authorization, provider-ownership, configuration,
and audit state.

For Revit, a configurable after-hours job uses Autodesk's whole-Host lock and status operation,
stages a consistent copy, unlocks promptly, and then lets the backup product copy the staged data.
An active session, failed lock, failed copy, or failed unlock produces a visible warning instead of
a forced or silently incomplete backup.

One coordinated restore of Balls state, an ordinary file, and a representative Revit model must pass
before real company data becomes authoritative. Recurring schedules and reminders are Owner
configuration, not separate product features.

Balls may display storage, UPS, backup, SMB, Tailscale, Revit, and daemon health. Email, SMS,
automatic provider repair, RAID management, and UPS management are deferred.

## Deployment posture

This is a **Run-and-gun Office Pilot**, not an enterprise release:

- exact immutable Development builds are allowed;
- Authenticode signing is not a pilot gate;
- an ordinary Windows administrator approval or warning is acceptable;
- Windows protections are never disabled or bypassed;
- a machine that rejects the Development build is honestly `BLOCKED`;
- one office server is sufficient;
- no automatic failover or second live server is required;
- an office-server outage temporarily removes the files and Revit capabilities hosted there;
- updates are deliberate and must preserve or restore the existing Circle and provider state.

## Capability gap matrix

| Area | What Balls has now | What this office needs |
| --- | --- | --- |
| Windows host | Windows 11/Server 2025 SMB readiness; current-user package | Windows Server 2022 host support and native service installation |
| Server lifecycle | Hidden process started from an interactive user package | Restricted Windows Service, boot start, bounded recovery, clean shutdown and repair |
| Circle durability | Local Node state and authority export foundation | First-Anchor operation plus tested state and authority restoration |
| File topology | One Balls-owned share and account per Access Grant | One provider root, one Member identity, multiple independently authorized File Areas |
| File UX | One grant mapped to one drive; multiple grants are ambiguous | One persistent Office Circle Files location containing all authorized areas |
| Access policy | Direct per-Member whole-folder grants | Shared and restricted Capability Access Groups materialized into exact area access |
| Member removal | File-grant revocation and exact cleanup foundation | One simple Member removal intent across file access, approved Nodes, and Revit reachability |
| Remote transport | RFC1918 LAN endpoints and LocalSubnet SMB firewall scope | Tailscale transport, `100.64.0.0/10`, one-time Node enrollment, and canonical MagicDNS identity |
| Revit | No Autodesk integration or repository exclusion | Revit Server 2027 setup, onboarding, health, backup coordination, and a hard repository boundary |
| Backup/recovery | Local SQLite integrity and protected material; no complete import/restore path | Coordinated Balls-state export/restore and proof against restored provider state |
| Health | Basic daemon status, audit records, and launcher logs | One local Office Health view with plain warnings and no silent repair |
| Delivery | Exact Development packages; unsigned | Continue exact Development delivery for the pilot without weakening Windows policy |

## Small implementation slices

Implementation follows the Private Boss Demo rather than expanding that issue.

1. **Server foundation**
   - recognize and test Windows Server 2022;
   - install `ballsd` as a restricted Windows Service;
   - separate interactive administration from the service identity;
   - prove reboot, update, rollback, state export, and restoration.
2. **Office Circle Files**
   - create the new `D:\CompanyData` root and one encrypted SMB share;
   - add Shared and Restricted File Areas and Capability Access Groups;
   - use one Member provider identity and one Explorer mapping;
   - prove area grant, denial, removal, daemon-stop continuity, and reboot recovery.
3. **Private remote Nodes**
   - add the Tailscale provider and Owner-approved one-time Node enrollment;
   - use one canonical MagicDNS server identity;
   - prove LAN and remote SMB with no public exposure.
4. **Revit Server 2027**
   - add repository exclusion before any other Revit work;
   - inspect prerequisites and guide Host+Admin installation;
   - configure exact model/admin reachability and canonical `RSN.ini`;
   - expose bounded health and backup operations;
   - prove real local/remote Revit behavior without an Accelerator.
5. **Run-and-gun acceptance**
   - install exact Development packages from the official download path;
   - run the complete Server 2022 and two-client journey;
   - perform one coordinated restore;
   - repeat the hardware-specific gate after the actual server arrives.

## Decisive acceptance

The software design is ready before hardware purchase when a Windows Server 2022 VM and two Windows
clients pass the companion checklist.

The physical server is ready for real company data only after:

- its Server 2022 drivers, fixed NTFS data volume, RAID behavior, UPS shutdown, and storage recovery
  are observed;
- the exact Development build runs without weakening Windows protection;
- Office Circle Files proves one shared area, one restricted area, and bidirectional ordinary-file
  use from two approved Nodes;
- an unauthorized Member and unapproved Node cannot reach the restricted area;
- Revit Server 2027 proves representative local/remote two-user open, create-local, and synchronize
  behavior through one canonical Host identity;
- no Revit repository path is reachable through SMB or Circle Files;
- no required service is publicly exposed;
- a coordinated Balls, ordinary-file, and Revit restore succeeds.

## Honest blockers

The deployment is `BLOCKED` when:

- Windows Server 2022 cannot safely host the required Balls provider;
- Windows refuses the unsigned Development build unless Defender or another Windows protection is
  weakened;
- a restricted File Area is reachable without its Circle authorization;
- the Revit repository is exposed or modified through Circle Files;
- remote Revit performance cannot support real work without an Accelerator;
- Revit lock/stage/unlock or restoration cannot produce a usable model;
- the selected hardware cannot reliably run Windows Server 2022, the DAS, or UPS shutdown.

## Explicit non-goals

- enterprise identity, Active Directory, SCIM, HR onboarding, or elaborate offboarding;
- Authenticode signing as a pilot prerequisite;
- email/SMS alert delivery;
- arbitrary nested NTFS permission editing;
- legacy share or file migration;
- DFS or a second file server;
- automatic failover or multiple live Anchors;
- Revit Server 2026 or multiple simultaneous Revit releases;
- a Revit Server Accelerator in v1;
- automatic Revit upgrades or project-model migration;
- Balls-managed Revit model data, RAID, UPS, or general backup storage;
- Circle Messaging, Circle AI, Apps, remote shell, RDP, or general server administration.

## Corrections to the source office architecture

The source architecture remains useful, but its Balls-facing version should change as follows:

- replace Revit Server 2026 with Revit Server 2027 and use version-specific repository paths;
- describe `RSN.ini` as discovery configuration, never an authorization control;
- replace several normal employee SMB shares with one Office Circle Files root and area permissions;
- replace raw Windows employee groups with Circle Capability Access Groups realized by the provider;
- use one frozen Tailscale MagicDNS Host identity on LAN and remote clients;
- describe no Accelerator as a v1 constraint whose failed remote performance blocks acceptance;
- permit exact Development builds for the pilot without weakening Windows protection;
- remove legacy-data migration requirements because the company data tree is new;
- keep email alerts and recurring restore cadence as later configuration;
- keep `D:\RevitServer\2027` permanently outside every SMB and Circle Files boundary.
