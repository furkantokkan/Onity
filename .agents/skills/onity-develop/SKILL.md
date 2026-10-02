---
name: onity-develop
description: Implement, fix, review, benchmark, document, or release the Onity Unity package itself. Use for package runtime, editor tooling, public APIs, tests, docs, performance work, and releases; use onity-use for game code that consumes Onity.
---

# Develop Onity

In an Onity source repository, read `AGENTS.md` and the root style guide when present. Locate the package root in the checkout: the published repository uses `Packages/com.onity.framework`, while some Unity project checkouts use `Assets/Onity-Packages/Onity`. Check Git status and preserve existing work. Use the installed Unity CLI to identify the exact project and choose focused verification under that checkout's instructions.

## Protect the package contract

- Keep `Onity.Core`, `Onity.DI`, `Onity.Messaging`, `Onity.Reactive`, `Onity.Factory`, and `Onity.Composition` engine-free: no `UnityEngine` and no `OnityTask`. Unity-facing bridges belong in `Onity.Unity`.
- Implement Onity runtime behavior in Onity code. Third-party packages in this repository are comparison or tooling references, not runtime dependencies. Do not add `System.Linq` to Onity runtime code.
- Follow root style: Allman braces; `m_` instance fields, `s_` static fields, `k_` constants. Add XML documentation for changed public APIs.
- Preserve scope, lifetime, disposal, and event behavior unless the task explicitly changes their contract. Read existing tests and the relevant `docs/guide/` and `docs/reference/` pages before changing these surfaces.
- Do not add an overload to an existing extension-method name when its receiver or a parameter type lives in another Onity assembly: C# reports CS0012 for every caller of that name that does not reference that assembly. Use a distinct name instead, as `ReceiveAllAsync` does.

## Keep the OnityTask core intact

- `OnityTask` and `OnityTask<T>` have no static constructor: add no static field initializer in any partial (settings live in `OnityTaskSettings`). A test asserts `TypeInitializer == null`.
- Pooled task sources derive from the class root (`OnityTaskCore`, `OnityTaskSourceCore`, `OnityTaskSourceBase`) so awaiters dispatch by class test and virtual call, and they use its single state-word protocol. Keep the registration pin and the version-retiring compare-and-swap, and add no extra `Volatile` or `Interlocked` on a hot path without a measured reason.
- An async-method runner returns to its pool when its result is consumed, on every backend. `ONITY_RUNNER_RETURN_GATE` restores the IL2CPP return-on-unwind gate; do not enable it by default.
- Apply `[Il2CppSetOption(Option.NullChecks, false)]` only to types whose receivers are non-null by construction, and validate arguments explicitly at their public entry points.

## Measure performance

- Check allocations and measure DI resolve, publish, reactive delivery, and per-frame paths under the relevant benchmark scenario. A claim that Onity beats VContainer or UniTask needs a current comparable baseline and recorded deltas.
- Claim OnityTask speed only from the Release Player gate, never from Editor timings. On the benchmark host run `tools/benchmark-host/run-attempt.ps1 -SelfTest`, then the attempt with `-PlayersFrom <selftest dir>`, and take the verdict from `surpass-gate.py` (IL2CPP rows gate; Mono and flow-on rows are reported only). Follow `tools/benchmark-host/README.md` and `Packages/com.onity.framework/Benchmarks/Tasks/README.md`, and state the evidence path with the claim.

## Test and finish a change

- Add focused tests for new behavior and important failure paths. Before finishing, run the full EditMode and PlayMode suites in both default and `-releaseCodeOptimization` code optimization (Unity 2022.3.62f2 in the public repository).
- After every Unity run in the public project, restore `ProjectSettings/EditorSettings.asset` (62f2 rewrites `serializedVersion` 13 to 12) and delete the generated `.vsconfig`, `Assets/NuGet.config.meta`, and `Assets/packages.config.meta`.
- In a test, observe (await) every faulted `OnityTaskCompletionSource`: an unobserved fault is published from the finalizer into whichever test runs next. In batch mode, bound frame-count waits by real time (`Time.realtimeSinceStartupAsDouble`).
- Docs: `docs/` and `.agents/skills/onity-use` are the source of truth. After editing them run `python tools/docs/sync-package-docs.py` (CI runs `--check`, which also requires `Packages/com.onity.framework/llms.txt` to list every shipped page). Name installed paths in `onity-use` in backticks, not links, because an unresolvable link fails the sync. Record the change under `[Unreleased]` in `CHANGELOG.md` (root and package copy), and keep `AGENTS.md` and `CLAUDE.md` at the package root as entry points.
- Report the changed files, verification result, benchmark evidence when relevant, and any unverified Editor-dependent behavior.

## Release

- Fetch before pushing and never force-push. Release one squashed `Release Onity X.Y.Z` commit on `main` plus an annotated `vX.Y.Z` tag, and attach the test XMLs and benchmark evidence to the GitHub release. CI mirrors `Packages/com.onity.framework` to the `upm` branch; verify it holds `Documentation~`, `AGENTS.md`, and `llms.txt`. Add no AI attribution to commits.
