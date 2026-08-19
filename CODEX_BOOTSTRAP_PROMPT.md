# Codex Bootstrap Prompt

Use this prompt after creating the fresh `scwlkr/balls` repository and placing the foundation documents in its root.

---

You are taking over the initial implementation of **Balls**.

The repository contains the product vision and architectural foundation. Treat the docs as source of truth.

First read, in order:

1. `VISION.md`
2. `PRODUCT.md`
3. `PRINCIPLES.md`
4. `ARCHITECTURE.md`
5. `GLOSSARY.md`
6. `DECISIONS.md`
7. `ROADMAP.md`
8. `LEGACY.md`
9. `AGENTS.md`

Your job is **not** to build the entire vision.

Your job is to establish the clean technical foundation for **Phase 1 — First Circle** without collapsing the product back into a Windows file-sharing utility.

Before implementation:

- summarize the system you believe you are building;
- identify the product invariants that affect architecture;
- propose the smallest Phase 1 vertical slice;
- propose the initial repository/project structure;
- identify decisions that can remain deferred;
- identify any recommendation in `ARCHITECTURE.md` that you believe should change, with a concrete reason.

Then implement the first logical vertical slice.

Priorities:

1. preserve the Circle abstraction;
2. create clean core/protocol boundaries;
3. establish `ballsd` and `balls`;
4. isolate Windows-specific integration;
5. use typed/versionable interfaces;
6. create persistent Circle and Node identity;
7. prove two real machines can participate in one Circle;
8. keep the code easy to test and reason about.

Do not implement speculative AI, app marketplace, distributed compute, or a universal filesystem during Phase 1.

Do not use WSL as the foundation.

Do not make Tailscale, SMB, WPF, or any one implementation provider synonymous with a Balls product concept.

Use `scwlkr/balls-server` only as prior art when a specific Windows/security/networking implementation is useful.

At every meaningful checkpoint update the relevant docs so the repository remains understandable to another engineer or coding agent with no conversation history.

The project should be capable of growing into the long-term vision without requiring the first milestone to pretend the future already exists.
