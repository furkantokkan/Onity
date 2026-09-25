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
  synchronously without allocation or a frame delay. Awaiting it on any other
  thread queues the continuation and resumes it on Unity's main thread.
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
| Player start | `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` captures the main thread and Unity synchronization context and starts session 2. No runner object is created until the first main-thread OnityTask call or the first worker-thread switch. |
| Worker switch before any runner exists | The dispatcher posts one runner-creation request through the captured Unity synchronization context. The request creates `OnityTaskRunner` on the main thread; its next `Update` drains the queue. Without a synchronization context, `BeforeSceneLoad` creates the runner eagerly instead. |
| Runner destroyed during a session | `OnDestroy` marks the pump missing. The next worker enqueue posts a new creation request, so the switch still resumes. Main-thread OnityTask calls recreate the runner directly. |
| Editor domain load | `InitializeOnLoadMethod` captures the main thread, subscribes `EditorApplication.update`, and subscribes `playModeStateChanged`. Session numbering restarts at 1 with fresh statics. |
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
| Main-thread fast path completes synchronously; canceled token throws at `GetResult`; null continuation rejected | EditMode `OnityTaskThreadSwitchEditModeTests`, PlayMode `OnityTaskThreadSwitchPlayModeTests` | Written, not yet run in Unity |
| Worker switch resumes on the main thread during `Update`, before `LateUpdate`, outside fixed steps | PlayMode `OnityTaskThreadSwitchPlayModeTests` probe test | Written, not yet run in Unity |
| Cancellation while queued throws on the main thread with the original token | PlayMode and EditMode | Written, not yet run in Unity |
| Concurrent producers, reentrant registration during a drain, runner destruction and recreation | PlayMode `OnityTaskThreadSwitchPlayModeTests` | Written, not yet run in Unity |
| `AsyncLocal` flow and suppressed flow through `async Task` and `async OnityTask` | PlayMode `OnityTaskThreadSwitchPlayModeTests` | Written, not yet run in Unity |
| Edit Mode dispatch through `EditorApplication.update`, including manual main-thread registration | EditMode `OnityTaskThreadSwitchEditModeTests` | Written, not yet run in Unity |
| Session isolation across Play Mode entry and exit, domain reload enabled and disabled | EditMode `OnityTaskThreadSwitchEditorLifecycleTests` with `EnterPlayMode`/`ExitPlayMode` | Written, not yet run in Unity |
| Compile check of the changed runtime, tests, and benchmark files | Roslyn 4.10 against .NET Standard 2.1 references with Unity, Editor, Test Framework, and UniTask stubs | See the change record in the pull request or commit message |

All Unity-dependent rows require the pinned Unity 2022.3.62f3 Editor. The
Linux container used for this increment has no Unity installation, so every
row marked "not yet run in Unity" is unverified behavior until the EditMode and
PlayMode suites run there. Do not publish a release or a comparison claim from
this state.

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
separate run. No thread-switch measurement has been taken yet.

## Remaining scope from the continuation plan

Completing this feature does not close the comparison matrix. The suspended
async builder allocation gap (Onity 312 B/op versus UniTask 72 B/op),
Task-backed composition paths, selectable player-loop phases, immediate
cancellation, and async-enumerable support remain open items with measured
acceptance gates.
