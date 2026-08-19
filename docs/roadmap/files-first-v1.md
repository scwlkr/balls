# Files-first v1 Program

## Goal

Reach a public, supported Circle Files product quickly while preserving the larger Circle platform.
The first serious proving ground is the owner and one coworker editing the same contributed folder
through Windows File Explorer.

The milestone names below describe user outcomes. Only the active milestone is fully committed in
GitHub Issues; later ticket maps are planning hypotheses and are refined immediately before work.

## `0.1.0-alpha.2` — Open and Fast Foundation

**User outcome:** contributors and agents can understand, change, test, and obtain a canary build
of Balls with minimal delay.

**Architectural proof:** delivery and release mechanics are deterministic before the distributed
system grows.

**Candidate tickets:**

1. adopt Apache 2.0, sanitize/audit the public surface and history, present readiness evidence,
   and obtain the owner decision to change visibility;
2. create one fast cross-platform developer command and test taxonomy;
3. establish GitHub issue, pull-request, and required-check workflow;
4. publish a runnable Windows Canary and explicit Linux build/test artifact from green `main`, plus
   a one-command development install;
5. add the post-public security and supply-chain automation baseline;
6. measure the feedback budgets and reconcile the milestone evidence.

Ticket 1 is the first ready ticket. Public visibility depends only on the license/notices,
privacy/history audit, the current full gate, readiness evidence, and a final explicit owner
confirmation; it does not wait for unrelated Canary or supply-chain automation.

**Exit evidence:** fast-loop budgets measured; fixed Windows/Linux checks green; the Windows
Canary starts; the Linux artifact builds/tests and is labeled runtime-unsupported until
`0.2.0-alpha.1`; the public transition decision and resulting state are recorded.

## `0.2.0-alpha.1` — Cross-platform Node and Web UI

**User outcome:** the same CLI and local browser experience can control a real Node on Windows or
Linux.

**Architectural proof:** platform composition, state protection, and local IPC are genuinely
cross-platform; the UI does not create a second product implementation.

**Candidate tickets:**

1. refactor Windows-only daemon/CLI composition behind host adapters;
2. add protected Unix state and HTTP/JSON over a Unix-domain socket;
3. add structured CLI output and Windows/Linux process acceptance;
4. create the React/TypeScript/Vite workspace and generated OpenAPI client;
5. serve an authenticated, antiforgery-protected loopback UI from `ballsd`;
6. automate the WSL/Hyper-V lab and Playwright Chromium smoke.

**Exit evidence:** Windows host and Ubuntu VM independently persist Node identity and pass the same
CLI/API acceptance flow; the browser opens through `balls ui`; no non-loopback browser listener;
green `main` publishes runnable Windows and Linux Canary archives.

## `0.3.0-alpha.1` — Trusted Circle

**User outcome:** a second Node can accept a directly exchanged invitation, join one Circle, see
Members/Nodes, restart, and exchange one persistent text message.

**Architectural proof:** cryptographic Circle/Member/Node identity, authenticated admission,
authorization, and the remote Circle protocol are separate from transport.

**Candidate tickets:**

1. prototype and record the identity/admission decision against the threat model;
2. protect persistent cryptographic Node and Circle authority material;
3. implement bounded, expiring, single-use invitation and admission;
4. implement authenticated/encrypted LAN transport behind a provider seam;
5. synchronize membership and preserve it across restart;
6. add one minimal persistent Circle message path and virtual two-Node evidence.

**Exit evidence:** Windows host and Ubuntu VM join the same Circle, reject forged/expired/replayed
admission, restart with stable identities, and exchange the same persisted message.

**Boundary:** one selected Anchor is authoritative; Nodes retain their identities and membership
records, but automatic Anchor failover and replicated authority are deferred. Authority state must
be exportable for backup.

## `0.4.0-alpha.1` — LAN Circle Files

**User outcome:** two Windows Members can add, remove, rename, and edit files in the same persistent
Explorer-mapped folder over a private LAN.

**Architectural proof:** Circle contribution and authorization drive a provider without making SMB
the Circle Files product model.

**Candidate tickets:**

1. define Circle Files contribution, provider, and access-grant contracts;
2. implement typed fail-closed Windows SMB 3.1.1 readiness;
3. create a new dedicated contributed folder through one narrowly scoped UAC-approved, recoverable
   helper operation;
4. issue one limited provider credential per Member access grant;
5. save the credential and map the exact Circle drive unelevated using a user-selected available
   drive letter and friendly Circle name;
6. revoke grants and remove only Balls-owned infrastructure;
7. certify Explorer, ordinary PDF/image viewing, current Word/Excel/PowerPoint locking, and the
   full LAN outcome.

**Provider contract:** SMB1 and guest access are disabled; signing and encryption are required;
TCP 445 is never public. Access begins with `Read/write` or `Read-only` at the whole-folder level.
SMB preserves application-requested locks; Balls does not promise universal single-writer behavior
for arbitrary applications.

**Boundary:** one live folder on one Node; no sync, replication, conflict merge, version history,
or Balls-managed trash. Independent backup is required. Cleanup never deletes user files or the
contributed folder.

## `0.5.0-alpha.1` — Operable Remote Files

**User outcome:** an Owner can safely adopt an existing folder, grant remote access through an
already configured Tailscale network, install/update Balls, diagnose drift, repair it, revoke a
Member, and remove Balls-owned infrastructure.

**Candidate tickets:**

1. adopt an existing folder with preview, exact ownership records, and no content ownership;
2. add an explicit Tailscale endpoint/provider path without installing or signing into Tailscale;
3. add drift detection and bounded repair;
4. make revoke/open-session handling explicit and recoverable;
5. package normal Windows installation for the company pilot plus native Windows/Linux service
   installation and one-command removal;
6. add Canary update/rollback and release-candidate verification.

**Exit evidence:** exact packaged bits pass clean VM install, restart, mapping, remote access,
repair, update/rollback, revoke, and removal scenarios. Open-session termination requires a second
explicit confirmation after future authentication is disabled.

## `0.6.0-beta.1` — Company Pilot

**User outcome:** the owner and one coworker use an accepted candidate for real shared files.

**Candidate tickets:**

1. complete pilot data/backup and support readiness;
2. install the exact candidate on the coworker's Windows computer;
3. observe normal use and fix only release-blocking failures;
4. reconcile documentation, known limitations, and Beta evidence.

Physical-machine evidence is useful but not a general release gate. The pilot is the first planned
external physical Node because it serves a real user outcome, not because the roadmap waits for a
perfect lab.

## `1.0.0` — Public Files Release

**User outcome:** a small trusted group can install a supported release and maintain one secure
Circle Files workspace without being systems administrators.

**Candidate tickets:**

1. close critical security/data-loss findings and compatibility gaps;
2. produce signed immutable artifacts, checksums, SBOM, and provenance;
3. finish install, backup, recovery, privacy, contribution, and support documentation;
4. verify the exact release candidate against its supported matrix;
5. obtain explicit owner acceptance and promote the exact artifacts.

## Post-v1 direction

Rich messaging, multiple Anchors, macOS polish, replicated and synchronized Files, Circle AI,
Circle Apps, and Circle Compute grow from the same identities, protocols, contribution model, and
provider seams. They are not removed from the product; they are deliberately kept off the critical
path to the first useful release.
