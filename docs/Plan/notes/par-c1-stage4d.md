# PAR-C1: Stage 4d reactive adapters integration

Branch `parity/stage4d-reactive-adapters` (base `530c4dd`). No commits made; the orchestrator commits.

## Changed files (git status --porcelain)

- `M Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsyncEnumerableExtensions.cs`
  (only the two adapter methods inserted; CRLF kept, 48 lines added, nothing else touched)
- `?? Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsyncEnumerableReactiveAdapters.cs` (+ `.meta`, GUID `b86ecf2359964aaf933bc8d6b7d859b9`)
- `?? Packages/com.onity.framework/Tests/EditMode/Scripts/OnityAsyncEnumerableReactiveEditModeTests.cs` (+ `.meta`, `9adcc46d9d0844899a30a5c8fca49390`)
- `?? Packages/com.onity.framework/Tests/PlayMode/OnityAsyncEnumerableReactivePlayModeTests.cs` (+ `.meta`, `28c5de4f7a0a4b7b9dcb26a283dd34a2`)
- `?? Packages/com.onity.framework/Benchmarks/Tasks/Runtime/OnityReactiveAdapterBenchmarkRunner.cs` (+ `.meta`, `30acd923b2a74bc9b9b2f358f1cf57be`); file only, not wired
- `?? docs/assets/benchmarks/onitytask-stage4d-reactive-adapters-2026-09-27.md` (historical 4d report, copied as is; its "in progress" wording is the 2026-09-27 state)
- `?? docs/Plan/notes/par-c1-stage4d.md` (this note)

All four .meta GUIDs were grepped across all `*.meta` in the worktree: each occurs once (its own file).
Bytes were copied from `onity-fast-capture-f682b7c` (LF; git index is LF, working tree autocrlf). Adapter SHA256
prefix `5465373F...` matches the frozen 4d candidate hash. Extension methods inserted were exactly lines 11-58 of
the 4d copy; verified that the 4d copy minus those lines equals the current file (modulo CRLF).

## API added (UniTask-parity packet, no extras)

- `OnityAsyncEnumerableExtensions.AsOnityAsyncEnumerable<T>(this IOnityObservable<T>, int capacity)`
- `OnityAsyncEnumerableExtensions.AsObservable<T>(this IOnityAsyncEnumerable<T>)`
- Internal: `OnityPushAsyncEnumerable<T>`, `OnityPullObservable<T>` (in the new adapter file).
- Affinity: no implicit thread hop; callers respect source subscribe/dispose affinity (documented in XML).

## Adaptation to the 0.4/0.5 runtime

None needed. Everything compiled unchanged against the current runtime.

## Test-quality corrections (judgement call, see open questions)

No readable record enumerates the "two test-quality corrections" (plan 13 and the 4d report only name them).
I reviewed the 31 EditMode cases and applied two strengthening edits to the copied EditMode file
(the 4d copy is otherwise byte-identical):

1. `CancellationBeforeSubscribeOrBeforeHandleAssignment_PreservesTokenAndDetachesOnce`: removed a tautological
   block (`caller.Token.Register(...)` then asserting `caller.IsCancellationRequested`, which the test itself set).
   Replaced with: a move after disposal returns false, and `Subscriptions` stays at the pre-dispose count
   (a disposed enumeration never subscribes late).
2. `CancellationDiscardsPrivateBuffer_ButDoesNotRewriteCommittedDelivery`: added `iterator.Current == 1` right
   after the committed first move, so "committed delivery is not rewritten" is asserted on the value, not only on
   the returned bool.

## Verification

All Unity runs: headless 2022.3.62f2, `-batchmode -nographics -releaseCodeOptimization`, under semaphore slot-1
(released afterwards), after the Roslyn checks. Artifacts under `Logs/PAR-C1/` (gitignored).

| Check | Command / result | Artifact |
| --- | --- | --- |
| Roslyn runtime + tests | `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project <wt> --files <adapters, extensions, EditMode test, PlayMode test> --dependents` -> COMPILE_OK: Onity.Unity, Onity.Editor, Onity.Tests.EditMode, Onity.Tests.PlayMode | console |
| Roslyn Onity.TaskBenchmarks runner | The assembly has no generated csproj (asmdef needs `ONITY_TASK_BENCHMARKS`; UniTask is not in this worktree's manifest), so the checker returned COMPILE_UNVERIFIED. Used a throwaway csproj (derived from the generated `Onity.Unity.csproj`, define `ONITY_TASK_BENCHMARKS`, outside the repo) compiling `OnityReactiveAdapterBenchmarkRunner.cs`, `OnityTaskBenchmarkEnvironment.cs`, `OnityBenchmarkAllocationCounter.cs` against the fresh `Onity.Unity.dll`: `dotnet build` succeeded, 0 errors | scratchpad `Onity.TaskBenchmarks.csproj`, `mkbench.py` (not committed) |
| EditMode (Release) | `Unity.exe -batchmode -nographics -runTests -projectPath <wt> -testPlatform EditMode -testFilter "OnityAsyncEnumerableReactiveEditModeTests;OnityAsyncEnumerableAdapterEditModeTests;OnityAsyncEnumerableAwaitEditModeTests;OnityAsyncEnumerableEditModeTests;OnityChannelEditModeTests" -releaseCodeOptimization` -> exit 0, 164/164 passed (Reactive 31, Adapter 28, Await 34, AsyncEnumerable 37, Channel 34) | `Logs/PAR-C1/editmode-release.xml`, `.log` |
| PlayMode (Release) | same flags, `-testPlatform PlayMode -testFilter "OnityAsyncEnumerableReactivePlayModeTests;OnityAsyncEnumerableAdapterPlayModeTests;OnityAsyncEnumerableAwaitPlayModeTests;OnityAsyncEnumerablePlayModeTests"` -> exit 0, 10/10 passed (Reactive 2, Adapter 4, Await 2, AsyncEnumerable 2) | `Logs/PAR-C1/playmode-release.xml`, `.log` |

Unity rewrote `ProjectSettings/EntitiesClientSettings.asset`, `ProjectSettings/ProjectSettings.asset` and
`ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json` during the runs; all three were restored
with `git restore -- <path>`. Final `git status --porcelain` lists only the files above.

Tests set no flow-dependent expectations; none depend on the `FlowExecutionContext` default.

## Remaining host items (not done here)

- Benchmark wiring of `OnityReactiveAdapterBenchmarkRunner` into `OnityTaskBenchmarkPlayerRunner` / build runner
  and Tasks README (after PERF-3, through the benchmark writer); the runner file is unreferenced until then.
- Mono and IL2CPP Release Player smoke (runtime hot-file packet rules; paired with the 35-case smoke) on the host.
- Allocation workload in a Release Player (two pending-delivery rows per the 4d report). Claim limit: pending state
  allocates; no zero-allocation claim.
- Roslyn of `Onity.TaskBenchmarks` through the real generated csproj once the host has UniTask and the define.

## Lines for the integrator

- CHANGELOG (Unreleased/Added): "Reactive stream adapters: `IOnityObservable<T>.AsOnityAsyncEnumerable(capacity)`
  (lazy, bounded FIFO, overflow faults after draining accepted values) and `IOnityAsyncEnumerable<T>.AsObservable()`
  (sequential pump per subscription, terminal after native cleanup). Cancellation round-trips as a faulted OCE
  because `OnityResult` has no canceled status. Pending state allocates; no thread hop is added."
- Guide (`docs/guide/onitytask.md`, streams section): same two APIs, the overflow-is-Fail rule, the value-only
  `Subscribe` policy (successful completion ignored, faults routed to `OnityObservableExceptionHandler`), and the
  subscribe/dispose thread-affinity statement.
- Parity matrix: UniTask `ToUniTaskAsyncEnumerable(IObservable)` / `ToObservable` rows -> implemented
  (EditMode 31 + PlayMode 2), with the stated differences (explicit capacity, fixed Fail overflow, OCE-as-fault).
  Do not mark area C complete; C2a/b/C3 remain.

## Open questions

- The two "test-quality corrections" could not be identified from any readable record; the two applied above are my
  choice. If the 4d author had specific ones in mind (the original development worktree is off limits to me), they
  should be compared against these.
- The extension file keeps both methods at the top of the class (as in the 4d copy); PAR-C2a/b file order is
  unaffected, since they land in new files.
