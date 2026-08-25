# Files-first v1 Program

## Goal

First make Balls useful to the owner's own company: two trusted coworkers must be able to use a
new Windows-hosted project folder over their private LAN after the second person accepts a Circle
invitation. Reach a public, supported Circle Files product afterward while preserving the larger
open-source Circle platform.

The actual coworker is invited only after the same ordinary-member journey has passed privately on
two separate Windows Nodes. Existing-folder adoption, remote networking, and broader integrations
are not allowed to delay that first company outcome.

The milestone names below describe user outcomes. The active milestone and its immediate successor
are committed in GitHub Issues; later ticket maps remain planning hypotheses until they are refined
immediately before work.

## `0.1.0-alpha.2` — Open and Fast Foundation

**User outcome:** contributors and agents can understand, change, test, and obtain a canary build
of Balls with minimal delay.

**Architectural proof:** delivery and release mechanics are deterministic before the distributed
system grows.

**Executed issues:** [#3 public transition](https://github.com/scwlkr/balls/issues/3),
[#4 developer verifier](https://github.com/scwlkr/balls/issues/4),
[#5 protected pull requests](https://github.com/scwlkr/balls/issues/5),
[#6 Canary artifacts](https://github.com/scwlkr/balls/issues/6),
[#7 security automation](https://github.com/scwlkr/balls/issues/7), and
[#8 milestone verification/acceptance](https://github.com/scwlkr/balls/issues/8).

#3–#8 are closed with hosted evidence. The owner accepted exact commit
`66b94a3fce9485ce448736f3dc996f6611f1e742`; its annotated tag and public prerelease were verified
through unauthenticated downloads.

**Exit evidence:** fast-loop budgets measured; fixed Windows/Linux checks green; the Windows
Canary starts; the initial Linux artifact is honestly build/test-only until native runtime support
lands in `0.2.0-alpha.1`; the public transition decision and resulting state are recorded.

## `0.2.0-alpha.1` — Cross-platform Node and Web UI

**User outcome:** the same CLI and local browser experience can control a real Node on Windows or
Linux.

**Architectural proof:** platform composition, state protection, and local IPC are genuinely
cross-platform; the UI does not create a second product implementation.

**Executable issues:**

1. [#17 compose daemon/CLI through cross-platform host seams](https://github.com/scwlkr/balls/issues/17);
2. [#18 add protected Linux state and Unix-domain-socket control](https://github.com/scwlkr/balls/issues/18);
3. [#19 add structured CLI output and dual-platform process acceptance](https://github.com/scwlkr/balls/issues/19);
4. [#20 create the typed React workspace and generated OpenAPI client](https://github.com/scwlkr/balls/issues/20);
5. [#21 serve the hardened loopback browser UI from `ballsd`](https://github.com/scwlkr/balls/issues/21);
6. [#22 prove the Windows/Ubuntu outcome and runnable Canaries](https://github.com/scwlkr/balls/issues/22);
7. [#23 verify and accept the milestone](https://github.com/scwlkr/balls/issues/23).

#17–#23 are closed with hosted evidence. The owner accepted exact protected-main commit
`3935b6ac275b24c8ed2389862b012da747099f34`; its annotated tag, seven public prerelease assets,
checksums, installers, and SPDX SBOM were verified through unauthenticated downloads.

**Exit evidence:** Windows host and Ubuntu VM independently persist Node identity and pass the same
CLI/API acceptance flow; the browser opens through `balls ui`; no non-loopback browser listener;
green `main` publishes runnable Windows and Linux Canary archives.

## `0.3.0-alpha.1` — Trusted Circle

**User outcome:** a second Node can accept a directly exchanged invitation, join one Circle, see
Members/Nodes, restart, and exchange one persistent text message.

**Architectural proof:** cryptographic Circle/Member/Node identity, authenticated admission,
authorization, and the remote Circle protocol are separate from transport.

**Executable issues:**

1. [#33 decide Circle identity, admission, and remote protocol security](https://github.com/scwlkr/balls/issues/33);
2. [#35 persist and protect cryptographic Node and Circle authority](https://github.com/scwlkr/balls/issues/35);
3. [#36 issue and redeem bounded single-use Circle invitations](https://github.com/scwlkr/balls/issues/36);
4. [#37 authenticate and encrypt LAN Node transport](https://github.com/scwlkr/balls/issues/37);
5. [#38 admit a second Node and persist shared Circle membership](https://github.com/scwlkr/balls/issues/38);
6. [#39 exchange one persistent Circle message across two Nodes](https://github.com/scwlkr/balls/issues/39);
7. [#34 verify and accept the Trusted Circle milestone](https://github.com/scwlkr/balls/issues/34).

#33 selected the remote v1 identity, signed-admission, TLS, provider, recovery, and revocation
boundaries with executable Windows/Linux-ready contracts. #35 protected production authority,
#36 added direct canonical single-use invitations, #37 authenticated LAN transport, #38 persisted
one signed two-Node roster through the daemon/API/CLI, and #39 added one persistent dual-signed
message. #34 accepted and published the exact verified `0.3.0-alpha.1` artifacts.

**Exit evidence:** Windows host and Ubuntu VM join the same Circle, reject forged/expired/replayed
admission, restart with stable identities, and exchange the same persisted message.

**Boundary:** one selected Anchor is authoritative; Nodes retain their identities and membership
records, but automatic Anchor failover and replicated authority are deferred. Authority state must
be exportable for backup.

## `0.4.0-alpha.1` — LAN Circle Files

**User outcome:** an Owner creates a new Windows-hosted project folder and sends one Circle
invitation; a second ordinary Windows Member joins and can add, remove, rename, and edit files in
the same persistent Explorer-mapped folder over a private LAN without handling its SMB password.

**Architectural proof:** Circle contribution and authorization drive a provider without making SMB
the Circle Files product model.

**Executable issues:**

1. [#56 define Circle Files contributions and Member access grants](https://github.com/scwlkr/balls/issues/56);
2. [#57 add typed fail-closed Windows SMB 3.1.1 readiness](https://github.com/scwlkr/balls/issues/57);
3. [#58 create a dedicated Circle folder through a narrow Windows helper](https://github.com/scwlkr/balls/issues/58);
4. [#59 issue one limited SMB credential per Member access grant](https://github.com/scwlkr/balls/issues/59);
5. [#60 map the exact Circle folder unelevated in Windows Explorer](https://github.com/scwlkr/balls/issues/60);
6. [#61 revoke grants and remove only Balls-owned infrastructure](https://github.com/scwlkr/balls/issues/61);
7. [#73 deliver the first two-person LAN Circle Files pilot](https://github.com/scwlkr/balls/issues/73);
8. [#62 verify and accept the LAN Circle Files milestone](https://github.com/scwlkr/balls/issues/62).

#56–#61 are closed. #73 supplies the missing ordinary-member cross-Node access and invitation
journey; #62 remains the final milestone/release acceptance gate after that product outcome is
honestly verified.

**Provider contract:** SMB1 is disabled; the Balls-hosted share excludes guest access and requires
signing, encryption, and an authorized Member account; TCP 445 is never public. Existing unrelated
outbound SMB client sessions remain untouched. Access begins with `Read/write` or `Read-only` at
the whole-folder level. SMB preserves application-requested locks; Balls does not promise
universal single-writer behavior for arbitrary applications.

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

**User outcome:** the owner and one coworker use an accepted candidate for real shared files with
the additional support and operational readiness expected of a formal Beta. The first useful
private-LAN company workflow is required during `0.4.0-alpha.1`; it must not wait for this later
Beta milestone.

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
