---
name: onity-develop
description: Implement, fix, review, or benchmark the Onity Unity package itself. Use for package runtime, editor tooling, public APIs, tests, and performance work; use onity-use for game code that consumes Onity.
---

# Develop Onity

In an Onity source repository, read `AGENTS.md` and the root style guide when present. Locate the package root in the checkout: the published repository uses `Packages/com.onity.framework`, while some Unity project checkouts use `Assets/Onity-Packages/Onity`. Check Git status and preserve existing work. Use the installed Unity CLI to identify the exact project and choose focused verification under that checkout's instructions.

## Protect the package contract

- Keep `Onity.Core`, `Onity.DI`, `Onity.Messaging`, `Onity.Reactive`, and `Onity.Factory` engine-free. Unity-facing bridges belong in `Onity.Unity`.
- Implement Onity runtime behavior in Onity code. Third-party packages in this repository are comparison or tooling references, not runtime dependencies. Do not add `System.Linq` to Onity runtime code.
- Follow root style: Allman braces; `m_` instance fields, `s_` static fields, `k_` constants. Add XML documentation for changed public APIs.
- Preserve scope, lifetime, disposal, and event behavior unless the task explicitly changes their contract. Read existing tests and the relevant `docs/guide/` and `docs/reference/` pages before changing these surfaces.
- For DI resolve, publish, reactive delivery, and per-frame paths, check allocations and measure performance under the relevant benchmark scenario. A claim that Onity beats VContainer needs a current comparable baseline and recorded deltas.

## Finish a change

Add focused tests for new behavior and important failure paths. Run the smallest relevant Unity EditMode or PlayMode tests with the project pinned; build affected assemblies when generated project files are current. Update the relevant usage or API documentation when public behavior changes. Report the changed files, verification result, benchmark evidence when relevant, and any unverified Editor-dependent behavior.
