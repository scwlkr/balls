# Balls Wizard v0

- **Status:** Accepted
- **Date:** 2026-08-27
- **Issue:** [#118](https://github.com/scwlkr/balls/issues/118)
- **Owner authorization:** optional parallel lane in an isolated worktree; do not merge without the
  ordinary pull-request gate

## Outcome

After installing Balls normally on Windows 11 x64, any local user may explicitly install a small
on-device product-help chat. Balls manages its runtime and model, the character answers from
version-matched Balls guidance with optional Sources, and removing or breaking the Wizard never
prevents ordinary Balls use.

The visible journey is:

```text
open Balls
  -> Download Wizard
  -> inspect local-only context, support, source, size, and storage
  -> approve download
  -> watch download and verification
  -> click the floating violet Wizard
  -> ask a Balls question
  -> receive playful, grounded help and optional Sources
  -> clear the in-memory conversation or remove Wizard
```

## Product boundary

Balls Wizard is a local product guide. It is not Circle AI, a Circle Capability, a Circle App, or
an administrator. It receives no tools and cannot run a command, open a file, call a mutation API,
or change Circle or operating-system state.

The character may remain tongue-in-cheek throughout. Personality never obscures download,
privacy, error, removal, or actionable instructions.

## Installation and artifact contract

The normal Balls package contains the UI, typed behavior, version-matched Wizard Guide, and an
immutable artifact manifest. It does not contain or automatically download the model or runtime.

The Windows 11 x64 v0 manifest pins:

- Google `gemma-4-E2B-it-qat-q4_0-gguf`, revision
  `675cff42a74c774d6cb76f76d8eacb49b48c9b93`;
- text-only `gemma-4-E2B_q4_0-it.gguf`, 3,349,516,256 bytes, SHA-256
  `fa401b55b07ee70a54c6dae3903c783a6e65064312529ea57175cb5f8dec6634`;
- llama.cpp `b10516` Windows CPU x64 package, 18,506,923 bytes, SHA-256
  `fbbbc55e0eb2e1b07f9dcb9488616c98ed47d9003b90e15e7c8c7812c4307cd3`.

The multimodal projector is not downloaded. Exact revision URLs, byte sizes, hashes, licenses,
and attribution ship in the manifest. A partial or hash-mismatched artifact is never extracted or
executed.

Installation is current-user, unelevated, resumable, cancellable, retryable, and removable. Balls
stores Wizard-owned data beneath the protected local Balls data directory. Removal stops the
runtime and deletes only the Wizard model, runtime, partials, cache, and installation record.

First installation needs internet access. A verified installation answers offline. A Balls update
never silently uses a mismatched Wizard Guide; model or runtime changes require new consent.

## Support gate

Windows 11 x64 is the only v0 support claim. Preflight reports exact architecture, memory, and
free-storage observations. It blocks download when the platform, measured memory floor, or storage
floor is not met. A supported CPU-only computer may receive an honest performance warning.

Linux, macOS, Windows Arm64, GPU-specific acceleration, and other configurations return an
explicit unsupported result while using the same platform-neutral contract.

## Wizard System Context

Each request receives a fresh ephemeral snapshot containing only:

- installed Balls and Wizard versions;
- Windows name/version and process/OS architecture;
- CPU/GPU names and bounded capacity facts;
- total/available memory;
- free space in the Wizard storage location;
- local Circle role as `owner`, `member`, or `none`.

It excludes usernames, hostname, serial numbers, network addresses, arbitrary paths, Circle names,
Member identities, messages, contributed resources, files, and prior sessions. The UI exposes this
boundary under **What Wizard can see**.

## Knowledge and chat

The system prompt contains the Wizard identity, playful voice, durable Balls concepts, strict
read-only boundary, and uncertainty rule. For each question, deterministic local selection adds
the most relevant sections of the version-matched Wizard Guide. v0 uses no fine-tuning, embeddings,
vector database, cloud inference, telemetry, or persistent conversation store.

Actionable instructions require supporting Wizard Guide content. If no guidance supports an
answer, Wizard says it does not know rather than inventing a command, procedure, or feature.
Responses list the selected guide sections in a collapsed **Sources** disclosure. Harmless social
conversation may answer directly in character.

Conversation history exists only in the current browser component. **Clear conversation**, page
reload, or browser closure removes it.

## Runtime boundary

`ballsd` owns Wizard application behavior. A Windows platform adapter owns artifact inspection,
download, extraction, bounded system inventory, and the llama.cpp process. The runtime listens on
a random loopback port, uses a per-process secret, receives a bounded context, and stops with
`ballsd` or Wizard removal. The browser never receives a runtime address, model path, API key, or
raw process output.

Core Balls workflows do not depend on Wizard state. The absent, unsupported, downloading,
cancelled, corrupt, outdated, stopped, failed, and removed states all leave the normal workspace
available.

## Acceptance evidence

Automated tests cover contracts, platform gating, integrity refusal, resumable lifecycle, bounded
prompt/context construction, guide selection, browser states, chat clearing, and core independence.

One isolated Windows 11 x64 run must observe the real pinned download, verification, local answer,
Sources disclosure, clearing, removal, and continued Balls operation. Evidence records exact
artifact identities, hashes, environment, elapsed download/inference behavior, and limitations.
There is no numerical model-accuracy claim.

## Non-goals

- Circle AI, shared inference, or remote prompts;
- tools, actions, shell access, file access, or administration;
- model fine-tuning, embeddings, vector search, or multimodal input;
- persistent chat history or telemetry;
- a broad platform, accelerator, or performance matrix;
- automatic download or forced update;
- Alpha/Beta/Stable promotion or merge.
