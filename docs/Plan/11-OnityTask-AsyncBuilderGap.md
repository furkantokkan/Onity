# 11 - OnityTask Async Builder Gap

This memo traces the measured `async OnityTask` overhead against pinned UniTask
`2.5.11` (`2e993ff`) to its code-level causes, states the constraint that
prevents an equal-cost builder that keeps today's semantics, and lays out the
options the product owner has to choose between before the gap can close. It
is a decision document; no builder change is implemented yet.

## Measured gap

Editor/Mono rerun of the 16-scenario primary harness on 2026-09-25, mean
ns/op (see the [comparison guide](../guide/onitytask-comparison.md#primary-suite-rerun)):

| Slice | N | Onity | UniTask | Ratio |
| --- | ---: | ---: | ---: | ---: |
| Async method completed `GetResult` | 1 | 775.42 | 354.33 | 2.19x |
| Async method completed `<int>` `GetResult` | 1 | 702.54 | 379.77 | 1.85x |
| Async method `NextFrame` scheduling | 128 | 2734.70 | 1440.73 | 1.90x |
| Async method `NextFrame<int>` scheduling | 128 | 2571.04 | 1350.38 | 1.90x |
| Async method `NextFrame` scheduling | 4096 | 2414.53 | 1337.64 | 1.81x |
| Async method `NextFrame<int>` scheduling | 4096 | 2107.73 | 1175.43 | 1.79x |
| Async method `NextFrame` `GetResult` | 128 | 75.70 | 153.91 | 0.49x |

The calibrated allocation run of 2026-09-23 (comparison guide, "Calibrated
allocation follow-up") measured the same scheduling slices at 304 B/op untyped
and 312 B/op typed for Onity against 64 and 72 B/op for UniTask, with 160 B/op
attributed to `AsyncMethodBuilderCore.GetCompletionAction()` and 80 B/op to
`AsyncTaskMethodBuilder<T>.get_Task()`. The 2026-09-25 rerun could not
measure allocations.

## Where the time goes

`OnityTaskMethodBuilder` and `OnityTaskMethodBuilder<T>` wrap
`AsyncTaskMethodBuilder`. Unity 2022's Mono class library uses the .NET
Framework reference-source implementation of the async builder core, so each
`async OnityTask` method pays:

1. **Start.** `AsyncMethodBuilderCore.Start` establishes an execution-context
   copy-on-write scope around the synchronous part of the method and undoes it
   afterwards. This is what keeps `AsyncLocal` writes made before the first
   await from leaking to the caller. UniTask's `AsyncUniTaskMethodBuilder.Start`
   is a bare `stateMachine.MoveNext()`.
2. **First suspension.** `AsyncMethodBuilderCore.GetCompletionAction` boxes the
   state machine, allocates a `MoveNextRunner` and an `Action`, and captures the
   execution context with the internal sync-context-free `FastCapture`. The
   untyped builder then materializes a `Task`, and the typed builder a
   `Task<T>`, which `OnityTask.FromTask` wraps. UniTask rents a pooled
   `AsyncUniTask<TStateMachine>` runner that stores the state machine by value,
   reuses one cached `MoveNext` delegate, captures nothing, and returns
   `new UniTask(runner, version)`.
3. **Resumption.** `MoveNextRunner.Run` resumes through
   `ExecutionContext.Run(context, callback, state, preserveSyncCtx: true)`,
   which restores the captured `AsyncLocal` values and keeps the main thread's
   Unity synchronization context. UniTask calls `stateMachine.MoveNext()`.
4. **Completion and consumption.** Onity completes the .NET task and the
   consumer goes through `OnityTaskAwaiter.GetResultSlow`, a type test, and
   `TaskAwaiter.GetResult` validation. UniTask flips a status in
   `UniTaskCompletionSourceCore`, and `GetResult` returns the runner to its
   pool. Onity's faster suspended `GetResult` slice is a side effect of the
   completed-task check being cheap once the task exists.

Items 1 and 2 explain the synchronous 2.2x and the scheduling 1.8x to 2.2x
respectively; item 4 explains why the Onity `GetResult` slice is faster.

## The execution-context constraint

UniTask's speed and allocation profile come from not flowing
`ExecutionContext` at all. Its README states the consequence: "`AsyncLocal`
also does not work because it ignores ExecutionContext."

An Onity-owned pooled builder that preserved `AsyncLocal` flow would have to
capture the context itself. The only public entry point,
`ExecutionContext.Capture()`, copies the current `SynchronizationContext` on
the reference-source implementation; on Unity's main thread that is a
`UnitySynchronizationContext.CreateCopy()`. The rejected value-continuation
candidate of 2026-09-23 measured exactly this: 552 B/op in the
`UnitySynchronizationContext` constructor, 48 B/op in `CreateCopy`, and
72 B/op in `ExecutionContext.Capture`. The sync-context-free `FastCapture`
that the .NET builder uses is internal and unavailable to package code, and
reflection into it is not acceptable for IL2CPP or performance.

Therefore, on Unity 2022:

- keeping today's `AsyncLocal` semantics keeps a context capture per
  suspension, so the per-suspension floor stays near the current 300 B/op
  through the .NET builder, or rises above 650 B/op through a custom builder
  that uses the public capture;
- matching UniTask's allocation and time requires matching UniTask's
  semantics: no `AsyncLocal` flow across awaits in `async OnityTask` methods,
  and no copy-on-write isolation of `AsyncLocal` writes before the first
  await.

`SynchronizationContext` is unaffected either way. Onity's native awaiters
resume on the main thread by construction, the thread switch resumes there
through its dispatcher, and a `Task` awaited inside an `async OnityTask`
method still captures the context through its own `TaskAwaiter`.

## Options

| Option | Allocation and time outlook | Semantics | Scope |
| --- | --- | --- | --- |
| A. Keep the wrapped `AsyncTaskMethodBuilder` | Unchanged: 304 to 312 B/op and 1.8x to 2.2x slower scheduling; synchronous typed results already stored inline. | `AsyncLocal` flows; synchronous-part writes isolated. | None. |
| B. Pooled native builder without context flow | Expected to land in UniTask's class: one pooled runner per suspended method, cached `MoveNext`, no `Task`; synchronous completion without any `Task` or context scope. | Same as UniTask: `AsyncLocal` does not flow across `async OnityTask` awaits, and writes before the first await leak to the caller. Existing PlayMode test `SwitchToMainThread_AsyncLocal_FlowsThroughAsyncTaskAndAsyncOnityTask` must change to assert the new contract. | New runner types implementing `IOnityTaskSource` and `IOnityTaskSource<T>`, version tokens, single-consumer rules, `AsTask` bridging, tracker registration, `Forget`, and IL2CPP validation; roughly 600 to 900 runtime lines plus tests. |
| C. Option B with an explicit opt-in for context flow | Default matches B. Enabling flow uses the public capture and pays its copy cost only in projects that need `AsyncLocal`. | Documented switch; default is UniTask semantics. | Option B plus one static flag, a captured-context field on the runner, and a `ExecutionContext.Run` resumption path with its own tests. |

Option A cannot close the measured gap. Option B closes it at the cost of a
documented semantic change. Option C keeps an escape hatch for projects that
depend on `AsyncLocal`, at the price of one more code path to test.

**Recommendation:** choose C, with the flag defaulting to UniTask semantics
and named for what it costs, such as `OnityTask.FlowExecutionContext`. If a
simpler product is preferred, choose B and document `AsyncLocal` as
unsupported inside `async OnityTask` methods, as UniTask does. Either choice
is a public behavior change that belongs in the changelog and the migration
guide.

## Implementation plan for B or C

1. Add `OnityAsyncStateMachineRunner<TStateMachine>` and
   `OnityAsyncStateMachineRunner<TStateMachine, T>` beside the existing
   sources: pooled per closed generic type, state machine stored by value,
   one cached `MoveNext` delegate, version token, status, exception and
   cancellation storage mirroring `OnityTaskSourceBase`, and a bounded pool.
2. Implement `IOnityTaskSource`, `IOnityTaskSource<T>`, and
   `IOnityPreservedTaskSource` on the runners so `Preserve()`, `AsTask()`,
   `WhenAll`, `WhenAny`, and the tracker keep working; return the runner to
   the pool from `GetResult` after the single consumer reads it, never before.
3. Replace the builders' internals: `Start` runs `MoveNext` directly;
   `AwaitOnCompleted` and `AwaitUnsafeOnCompleted` rent the runner on first
   suspension and register its cached delegate; `SetResult` and
   `SetException` complete the runner, mapping `OperationCanceledException` to
   the canceled status as the .NET builder does; `Task` returns the runner's
   native task, or the inline result or a faulted Task-backed task when the
   method never suspended.
4. For option C, add the flag, capture the context in `Await*OnCompleted`
   when enabled, and resume through `ExecutionContext.Run` in the runner's
   `MoveNext` path.
5. Check the IL2CPP struct-call workaround that UniTask carries for its
   runner (`StateMachineRunner.cs`) against Unity 2022.3 IL2CPP before
   depending on pooled struct-held state machines in players.

## Acceptance gates

- Calibrated Editor/Mono allocation for the four async-method scheduling
  cases within 16 B/op of UniTask, with the 64 KiB positive and 0 B empty
  controls passing on every participating thread, in two Profiler processes.
- Scheduling and synchronous-completion timing not slower than the current
  builder in two independent timing processes, with raw samples retained.
- Full EditMode and PlayMode suites green, including the updated
  `AsyncLocal` tests and new tests for pool reuse, stale-token rejection,
  exception and cancellation mapping, `Preserve`, `AsTask`, `Forget`, and
  tracker registration.
- Focused Windows IL2CPP Player run covering suspension, resumption, and pool
  return.
- Changelog, migration guide, and the comparison guide updated with the
  chosen semantics and the measured result before any release.
