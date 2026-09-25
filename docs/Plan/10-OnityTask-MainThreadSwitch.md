# 10 - OnityTask Main-Thread Switch Contract

This document settles the Editor lifecycle, hook ownership, and queue
invalidation rules for `OnityTask.SwitchToMainThread`, and records the
verification state of the implementation. It replaces the unpushed
`plan/onitytask-main-thread-switch` contract; the earlier feature worktree was
never compilable, committed, or pushed, so the implementation restarted from the
released `0.3.13` source (`ad0bfd5`).

## Scope

- `OnityTask.SwitchToMainThread(CancellationToken)` returns the
  `OnityTaskThreadSwitch` awaitable. Awaiting it on Unity's main thread completes
  synchronously without a frame delay; the 0 B/op target for this path is
  listed under "Allocation and performance targets" and is not yet measured.
  Awaiting it on any other thread queues the continuation and resumes it on
  Unity's main thread.
- The destination phase in Play Mode and players is the script `Update` phase,
  because the queue is drained at the end of `OnityTaskRunner.Update()`, after
  the frame, delay, and predicate sources tick. A native wait scheduled from a
  switch continuation therefore keeps its existing next-tick semantics. Outside
  Play Mode the Editor drains the queue from `EditorApplication.update`.
- Cancellation is observed when the await completes. `GetResult()` throws
  `OperationCanceledException` carrying the token on the destination thread,
  including a token that was already canceled when the switch was requested.
  A queued continuation still runs on the main thread when its token is
  canceled while queued; the throw happens there, never on the canceling thread.
- Out of scope for this increment: `SwitchToThreadPool`, selectable player-loop
  phases, `ReturnToMainThread`, and async-enumerable support. Worker work
  continues to use `Task.Run` or the reactive thread-pool operators.

## Editor lifecycle decisions

| Situation | Behavior |
| --- | --- |
| Player start | `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` captures the main thread and Unity synchronization context. The first initialization of a domain keeps session 1, so a switch requested before the hook (a static constructor or an earlier `SubsystemRegistration` method) still resumes; a later initialization in the same domain starts a new session. No runner object is created until the first main-thread OnityTask call or the first worker-thread switch, except that continuations already queued at initialization request one immediately. |
| Worker switch before any runner exists | The dispatcher posts one runner-creation request through the captured Unity synchronization context. The request creates `OnityTaskRunner` on the main thread; its next `Update` drains the queue. Without a synchronization context, `BeforeSceneLoad` creates the runner eagerly instead. |
| Runner destroyed during a session | `OnDestroy` marks the pump missing and, when continuations are already queued, posts the creation request itself. The next worker enqueue also posts one, so the switch still resumes. Main-thread OnityTask calls recreate the runner directly. |
| Editor domain load | `InitializeOnLoadMethod` captures the main thread, subscribes `EditorApplication.update`, and subscribes `playModeStateChanged`. Session numbering restarts at 1 with fresh statics, and the following Play Mode initialization starts session 2, so worker switches requested by `[InitializeOnLoad]` code between the two hooks are discarded. |
| Edit Mode | `EditorApplication.update` drains the queue while the Editor is not playing, not about to change Play Mode, not compiling, and not importing assets. The `[ExecuteAlways]` runner may also drain from its sporadic Edit Mode `Update`; both run on the main thread and share the reentrancy guard. |
| Compilation or asset import | Draining pauses. Continuations queued during compilation are lost with the domain reload that follows, exactly like every other managed continuation. |
| Entering Play Mode | Continuations queued in Edit Mode are discarded when `SubsystemRegistration` starts the Play Mode session. With domain reload enabled the reload already dropped them; with domain reload disabled the session bump discards them explicitly. |
| Exiting Play Mode | `ExitingPlayMode` ends the session, discards queued continuations, and stops runner requests. `EnteredEditMode` bumps the session again so anything created during the teardown window is also stale. Continuations from the Play Mode session never run in Edit Mode. |
| Player quitting | `Application.quitting` ends the session, discards the queue, and rejects later enqueues. Continuations requested after quitting begins never run. |
| Editor paused Play Mode | The player loop does not run `Update`, so queued continuations wait until Play Mode resumes. |

## Hook ownership

- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityTaskThreadSwitch.cs`
  owns the public awaitable, the awaiter, and the internal
  `OnityTaskMainThreadDispatcher`, including the Editor hooks under
  `#if UNITY_EDITOR`. The `Onity.Unity` runtime assembly compiles those hooks
  only for the Editor, so no `InternalsVisibleTo` or Editor-assembly dependency
  is introduced and the queue stays private to one file.
- `OnityAsync.cs` owns the `OnityTask.SwitchToMainThread` facade and the three
  runner touch points: the `Update` drain, the creation notification in
  `GetOrCreate`, and the destruction notification in `OnDestroy`.
- `OnityTaskRunner.Schedule()` remains a main-thread API. It creates Unity
  objects and mutates plain lists, so the dispatcher never calls it from a
  worker thread; worker threads only touch the locked queue and the captured
  synchronization context.

## Queue invalidation rules

1. Every `OnityTaskThreadSwitch` value carries the session number read when
   `SwitchToMainThread` was called.
2. `Enqueue` appends `(continuation, session)` under a lock. It never runs the
   continuation inline and never touches Unity objects.
3. `Drain` swaps the pending list with an empty list under the lock, then runs
   only the swapped batch. Continuations enqueued during a drain, including
   reentrant main-thread registrations made from a running continuation, wait
   for the next drain. A batch is therefore bounded by the count at swap time.
4. A batch entry whose session differs from the current session is dropped
   without running. Dropped continuations are not logged. Session zero, which
   only a `default` awaitable value carries, always runs.
5. A continuation exception is caught and reported through `Debug.LogException`
   without stopping the rest of the batch, matching the frame-source behavior.
6. After the player starts quitting, `Enqueue` discards new continuations.

## Allocation and performance targets

- Main-thread synchronous path: 0 B/op. The awaitable and awaiter are structs,
  `IsCompleted` compares thread ids, and `GetResult` checks the token.
- Worker enqueue: 0 B/op steady state. The queue stores structs in a retained
  list; growth beyond the retained capacity allocates once per growth step.
- Main-thread drain: 0 B/op in the dispatcher itself. The continuation delegate
  is owned by the caller's async builder or manual registration.
- Execution context is not captured by `OnCompleted`, matching the existing
  native `OnityTaskAwaiter`. Compiler-generated `async Task` and
  `async OnityTask` methods flow `AsyncLocal` values through their builders and
  keep Unity's synchronization context during resumption; `SuppressFlow`
  before the await prevents the flow as it does for other awaiters.

## Verification matrix

| Behavior | Coverage | Status |
| --- | --- | --- |
| Main-thread fast path completes synchronously; canceled token throws at `GetResult`; null continuation rejected | EditMode `OnityTaskThreadSwitchEditModeTests`, PlayMode `OnityTaskThreadSwitchPlayModeTests` | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| Worker switch resumes on the main thread during `Update`, before `LateUpdate`, outside fixed steps | PlayMode `OnityTaskThreadSwitchPlayModeTests` probe test | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| Cancellation while queued throws on the main thread with the original token | PlayMode and EditMode | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| Concurrent producers, reentrant registration during a drain, runner destruction and recreation | PlayMode `OnityTaskThreadSwitchPlayModeTests` | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| `AsyncLocal` flow and suppressed flow through `async Task` and `async OnityTask` | PlayMode `OnityTaskThreadSwitchPlayModeTests` | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| Edit Mode dispatch through `EditorApplication.update`, including manual main-thread registration | EditMode `OnityTaskThreadSwitchEditModeTests` | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| Session isolation across Play Mode entry and exit, domain reload enabled and disabled | EditMode `OnityTaskThreadSwitchEditorLifecycleTests` with `EnterPlayMode`/`ExitPlayMode` | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| A throwing continuation does not stop the rest of a batch, in the same drain, exactly once; registration order survives buffer growth past the initial 64 entries from the main thread, from a worker, and from a reentrant registration | EditMode `OnityTaskThreadSwitchEditModeTests` (including a synchronous drain through reflection), PlayMode `OnityTaskThreadSwitchPlayModeTests` | Passed at `6c978a6` (focused filter, Unity 2022.3.62f2) |
| Compile check of the changed runtime, tests, and benchmark files | Roslyn 4.10 against .NET Standard 2.1 references with Unity, Editor, Test Framework, and UniTask stubs | Passed for every revision on this branch |

The 2026-09-25 rerun report ran the focused `OnityTaskThreadSwitch` filters on
`6c978a6` through the Unity CLI: 15 of 15 EditMode and 12 of 12 PlayMode tests
passed with no skips, which covers every row above. The earlier report's 614
EditMode, 37 PlayMode, and 26 analyzer passes match the full suites at
`3260c40`. Two limits remain: the focused filters are not the complete
repository suite, so the full EditMode and PlayMode runs still have to be
repeated on the current head before a release, and both reports used the
installed Unity 2022.3.62f2 Editor rather than the repository's pinned
2022.3.62f3. Do not publish a release from this state.

## Benchmark protocol

`Onity/Benchmarks/Run OnityTask Thread Switch Benchmarks (Play Mode)` runs
`OnityThreadSwitchBenchmarkRunner` in the isolated comparison host with the
pinned UniTask dependency and the `ONITY_TASK_BENCHMARKS` define. It measures:

- the main-thread synchronous switch, success and pre-canceled, for both
  libraries, with the main-thread allocation counter calibrated by a 64 KiB
  positive control and an empty control;
- worker-thread scheduling of 128 (warm) and 4,096 (burst) continuations,
  with the worker thread's allocation counter calibrated the same way;
- main-thread dispatch of the same batches, measured from the first to the last
  continuation executed inside one drain, with the main-thread counter read at
  both ends;
- the full `Task.Run` plus switch round trip through matched `async` methods.

Timing uses eight samples per case, alternating library order. Report values
are machine-specific Editor/Mono evidence; player and IL2CPP results need a
separate run.

### Measured state

Two Editor/Mono runs of `3260c40` on 2026-09-25 are recorded in the
[comparison guide](../guide/onitytask-comparison.md#thread-switch-comparison).
The synchronous main-thread switch was within 1.5 percent of UniTask, worker
enqueue of 4,096 continuations favored Onity by 10.5 to 22 percent, the
128-continuation enqueue had no consistent winner, and main-thread dispatch
favored UniTask by about 25 to 36 percent (24.7 to 36.1) in both runs. Both
per-thread allocation counters failed the 64 KiB positive control, so no
allocation value is available and the 0 B/op targets above are unverified.
The runs did not record the Editor code optimization mode or the incremental
GC setting; both change per-item dispatch cost (Debug mode disables JIT
inlining, and incremental GC turns UniTask's per-item null store into a write
barrier call), so the harness now records them and later runs should compare
Release mode and Mono and IL2CPP players.

The drain was then rewritten to array-backed double buffers with one session
read per batch and an inline exception guard, matching the shape of UniTask's
`ContinuationQueue`. A source-level review found the new loop at or below
UniTask's per-item cost on Mono x64 and IL2CPP, with the old gap explained by
the non-inlined `List<T>` indexer and invoke helper. The rerun on `6c978a6`
(same host, Debug code optimization, incremental GC enabled, two processes)
confirmed it: main-thread dispatch moved from 24.7 to 36.1 percent slower to
9.0 to 36.1 percent faster than UniTask across both cohorts and both runs,
worker enqueue of 4,096 continuations stayed faster, and the synchronous
switch stayed within noise. Allocation counters failed their controls again,
so the 0 B/op targets are still unverified, and no Release-mode, player, or
IL2CPP run exists yet. An optional IL2CPP-only follow-up is to disable null
and bounds checks on `Drain` through `Il2CppSetOption`, which needs the
attribute source added to the runtime assembly.

## Remaining scope from the continuation plan

Completing this feature does not close the comparison matrix. The suspended
async builder allocation gap (Onity 312 B/op versus UniTask 72 B/op),
Task-backed composition paths, selectable player-loop phases, immediate
cancellation, and async-enumerable support remain open items with measured
acceptance gates.
