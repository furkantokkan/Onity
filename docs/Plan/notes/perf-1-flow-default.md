# PERF-1 note: FlowExecutionContext default off, partial types

Packet: PERF-1 (plan 16 section 2 and W1-A). Branch `perf/flow-default-off`, from 530c4dd.
The host baseline-freeze step is deferred and was not touched.

## Changed files

- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsync.cs`
  - `s_flowExecutionContext` now defaults to `false`.
  - `FlowExecutionContext` XML doc rewritten: default off is UniTask semantics, set `true` before any
    async Onity method starts to opt in, and a `<remarks>` block on the synchronous-prefix limitation
    (`Start` runs the first `MoveNext` without a copy-on-write scope until PERF-9).
  - `OnityTask` and `OnityTask<T>` are `readonly partial struct`.
- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityTaskExtensions.cs`: `static partial class`.
- `Packages/com.onity.framework/Tests/EditMode/Scripts/OnityTaskAsyncBuilderEditModeTests.cs`
  - New `FlowExecutionContext_DefaultsToFalse`.
  - New `ResumedOnWorker_AtTheDefault_DoesNotFlowAsyncLocal` (flag deliberately not set: an `AsyncLocal`
    written before the await reads null after it on the resuming worker).
  - `SynchronousPartAsyncLocalWrite_ReachesTheCaller` now sets `FlowExecutionContext = true` explicitly
    (it documents the flow-on synchronous-prefix limitation and previously relied on the old default).

## What and why

No flow-on behaviour changed; only the default and the doc moved. Audit of every test file that references
`FlowExecutionContext` or `AsyncLocal` (EditMode and PlayMode, 20 files):

- All flow-dependent tests already set the flag explicitly inside the test (the 12
  `TwoSuspensions_UseEachSuspensionsContextAndFlowDecision` cases set `firstFlow`/`secondFlow` in the test body
  and the `*_FlowsAsyncLocal_WhenFlowIsEnabled` cases set `true`), and the fixtures that touch the flag
  (AsyncBuilder, ThreadPool Edit/Play, WhenAllTypedCoordinator, Cancellation, Timeout, ThreadSwitch) already
  save the previous value in SetUp (or a local) and restore it in TearDown/finally. I did not add a blanket
  `= true` to those SetUps: that would make the whole fixture run at the opt-in value and hide the new default
  for the many flow-agnostic tests (pooling, representation, cancellation mapping).
- Files that only use `AsyncLocal` through raw awaiters, `AsTask` bridges, trackers or enumerable adapters
  (`OnityAsyncTests`, `OnityTaskCompletionSource*`, `OnityAsyncEnumerable*`, tracker tests) do not depend on the
  builder flow switch and were left unchanged.

Benchmark runners are untouched. They all set `FlowExecutionContext = true` explicitly at start and restore it
afterwards, so their "flow on primary" results are unchanged by the new default; only the recorded
`flowExecutionContext` environment field and the `flowExecutionContextDefault` report field read `false` now
(the report text "flow on" labels are driven by the runner's explicit set, not by the default).

## Verification

- Roslyn (with dependents):
  `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project C:/Users/e-fur/.codex/worktrees/onity-flow-default-off/Onity --dependents --files <OnityAsync.cs> <OnityTaskExtensions.cs> <OnityTaskAsyncBuilderEditModeTests.cs>`
  -> `COMPILE_OK` (Onity.Unity, Onity.Editor, Onity.Tests.EditMode, Onity.Tests.PlayMode). Benchmark assemblies
  have no csproj (ONITY_TASK_BENCHMARKS not defined); the change adds no public API beyond `partial`, and
  `FlowExecutionContext` keeps its signature, so they are not affected at compile time.
- Headless Unity 2022.3.62f2, `-releaseCodeOptimization`, one slot held for both runs (script
  `perf1-run.sh`, slot-1 released):
  - EditMode `-testFilter "Onity.Tests.EditMode.OnityTaskAsyncBuilderEditModeTests;...OnityTaskBuilderEditModeTests;...OnityTaskSourceStateEditModeTests;...OnityTaskPreserveEditModeTests;...OnityTaskCancellationEditModeTests;...OnityAsyncTests"`:
    272/272 passed, 0 failed, 0 skipped. Includes both new tests and all 12 two-suspension cases.
    Artifacts: `Logs/PERF-1/editmode.xml`, `Logs/PERF-1/editmode.log`.
  - PlayMode `-testFilter "Onity.Tests.PlayMode.OnityTaskAsyncBuilderPlayModeTests"`: 1/1 passed.
    Artifacts: `Logs/PERF-1/playmode.xml`, `Logs/PERF-1/playmode.log`.
  - Run output: `Logs/PERF-1/run.out` (EDITMODE_RC=0, PLAYMODE_RC=0).
- Unity rewrote `ProjectSettings/EntitiesClientSettings.asset`, `ProjectSettings/ProjectSettings.asset` and
  `ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json`; all three were restored with
  `git restore`. Untracked `.vsconfig`, `Assets/NuGet.config.meta`, `Assets/packages.config.meta` (workspace
  setup leftovers) must not be committed.

Not run: the other flow-touching fixtures (ThreadPool, WhenAllTyped, Timeout, CompletionSource, enumerable
adapters, PlayMode ThreadSwitch/ThreadPool/Timeout). They were audited, not executed, because the packet's
acceptance list was limited to the classes above. A wider run would confirm no implicit reliance on the old default.

## Integrator lines

CHANGELOG `[Unreleased]`, Changed:
- `OnityTask.FlowExecutionContext` now defaults to `false`, matching UniTask: `AsyncLocal<T>` values no longer
  flow across awaits of `async OnityTask` and `async OnityTask<T>` methods unless you opt in, and no execution
  context is captured per suspension. `OnityTask`, `OnityTask<T>` and `OnityTaskExtensions` are now `partial`.

CHANGELOG `[Unreleased]`, Migration:
- If your code relies on `AsyncLocal<T>` (logging scopes, correlation IDs, ambient services) surviving an await
  inside an async Onity method, set `OnityTask.FlowExecutionContext = true` once at startup, before any async
  Onity method runs. Flow-on behaviour is unchanged. Known limitation while flow is on: code before an async
  method's first await runs without a copy-on-write scope, so an `AsyncLocal<T>` write there can leak to the
  caller (UniTask behaves the same; a later release will scope it).

`docs/Migration/From-UniTask.md` (replace the flow sentence near line 23):
- "Like UniTask, async `OnityTask` methods do not flow `AsyncLocal<T>` values across awaits by default. Set
  `OnityTask.FlowExecutionContext = true` before any async Onity method starts to opt in; on Unity's Mono class
  library that costs an execution-context allocation per suspension only once a thread has stored an
  `AsyncLocal<T>` value."

`docs/guide/onitytask.md` (lines ~344, 836-877, 932):
- Rewrite "By default the builder flows the execution context" to "By default the builder does not flow the
  execution context, as in UniTask: `AsyncLocal<T>` values set before an await are not visible after it on a
  resumed worker, writes after an await stay on the resuming thread, and a `SetSynchronizationContext` call after
  an await persists. Set `OnityTask.FlowExecutionContext = true` before any async Onity method starts to flow the
  context, isolate post-await writes and restore the resuming thread's synchronization context."
- Line ~871 "Set `OnityTask.FlowExecutionContext = false` ... to skip the capture" becomes the opt-in sentence above.
- Line ~424 "Onity remains slower at default flow" must say "with flow on"; speed claims are made at the default (off).
- Line ~932 "`OnityTask.FlowExecutionContext` is on" stays accurate as written (conditional on the opt-in).

`docs/guide/onitytask-comparison.md` (lines ~23, 48, 86, 726, 732, 781):
- Replace "Keep context flow enabled by default" and "Keep `FlowExecutionContext = true` by default" with
  "Flow is off by default (UniTask semantics); flow-on is an opt-in that preserves ambient `AsyncLocal<T>` and is
  reported separately, never gated." Reword "With flow on, the default" and "Flow-on remains the default" to
  "flow on, the opt-in" and "Flow-off is the default". Historical measurement tables keep their labels
  (measured before this default change).

`docs/guide/performance-and-il2cpp.md` (no flow text today; add one line to the OnityTask performance section):
- "Performance claims for `async OnityTask` are measured at the default `FlowExecutionContext = false`, which is
  UniTask-equivalent; enabling flow adds one execution-context capture and restore per suspension."

README (`Packages/com.onity.framework/README.md` lines ~85-88):
- "...By default they do not flow the execution context across awaits, matching UniTask; set
  `OnityTask.FlowExecutionContext = true` before any async Onity method starts to flow `AsyncLocal<T>` values,
  which allocates a context per suspension only once a thread has stored an `AsyncLocal<T>` value."

`Packages/com.onity.framework/Benchmarks/Tasks/README.md` (not edited here; wording for the "default flow" in the
`primary` suite, lines ~66, 89, 158, 198-199, 221, 273, 296, 346, 431, 455, 499-501, 651):
- State that the library default is flow off, that `primary` runs set the flag explicitly (flow on is the labeled
  primary arm and flow off the diagnostic arm until PERF-0/baseline work flips the primary), and replace
  "Effective baseline settings are tracker off, stack traces off, flow on, capacity 128" with the explicit
  per-arm flag. The post-PERF-1 baseline reports the default (off) as the claim arm, per plan 16 section 0.

## Open questions

1. The benchmark runners (all `Benchmarks/Tasks/Runtime/*Runner.cs`) force `FlowExecutionContext = true` at start
   and label flow on as the primary arm. Plan 16 says speed claims are measured at the default (off). A runner
   change (PERF-0 or the baseline step) must decide which arm is primary before the baseline is frozen; I did not
   touch runners per the packet rules.
2. Fixtures not in the acceptance list (see "Not run") were audited only; worth including in the integrator's
   wider EditMode/PlayMode pass.
