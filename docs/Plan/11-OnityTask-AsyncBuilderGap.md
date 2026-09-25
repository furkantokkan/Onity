# 11 - OnityTask Async Builder Gap

This memo traces the measured `async OnityTask` overhead against pinned UniTask
`2.5.11` (`2e993ff`) to its code-level causes, corrects an earlier assumption
about execution-context capture, and lays out the options the product owner
has to choose between before the gap can close. It is a decision document; no
builder change is implemented yet.

The class-library facts below were checked against the sources Unity 2022.3
compiles: the Unity-Technologies/mono `unity-2022.3-mbe` branch, whose
`AsyncMethodBuilder.cs`, `executioncontext.cs`, `synchronizationcontext.cs`,
and `thread.cs` come from the .NET Framework reference source in its legacy
desktop configuration (`FEATURE_CORECLR` is not defined for the `unityjit` or
`unityaot` profiles), while `Task`, `TaskAwaiter`, and `CancellationToken` come
from the CoreRT sources. Onity's own 2026-09-23 profiler callstack, which shows
`ExecutionContext.Capture()` calling `UnitySynchronizationContext.CreateCopy()`,
confirms that configuration on the Editor. Nothing in this memo was measured
on a Unity runtime; every estimate needs the acceptance gates at the end.

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
and 312 B/op typed for Onity against 64 and 72 B/op for UniTask. Its callstack
attribution reads as follows once the class-library code is taken into
account: the 160 B/op inside `AsyncMethodBuilderCore.GetCompletionAction()` is
the `MoveNextRunner` plus the Mono delegate object; the 80 B/op inside
`AsyncTaskMethodBuilder<T>.get_Task()` is the `Task<VoidTaskResult>` or
`Task<T>` forced on the first suspension; the unattributed remainder of about
64 B/op is the boxed state machine, which `AwaitUnsafeOnCompleted` allocates.
The 2026-09-25 rerun could not measure allocations.

## Where the time goes

`OnityTaskMethodBuilder` and `OnityTaskMethodBuilder<T>` wrap
`AsyncTaskMethodBuilder`. Each `async OnityTask` method therefore pays:

1. **Start.** `AsyncTaskMethodBuilder.Start` and
   `AsyncTaskMethodBuilder<TResult>.Start` inline an execution-context
   copy-on-write scope (`EstablishCopyOnWriteScope` and `Undo`) around the
   synchronous part of the method; `AsyncMethodBuilderCore.Start` is an
   uncalled template in this reference source. The scope allocates and
   captures nothing: it records the thread's current context and scope flag,
   and any `AsyncLocal` write inside the method copies the context before
   mutating it, so writes made before the first await do not leak to the
   caller. UniTask's `AsyncUniTaskMethodBuilder.Start` is a bare
   `stateMachine.MoveNext()`. The scope contributes to the synchronous 1.85x to
   2.19x, but a handful of field operations cannot account for 350 to 420 ns
   on their own; the synchronous path needs a profile before its cost is
   attributed.
2. **First suspension.** `AsyncTaskMethodBuilder<TResult>.AwaitUnsafeOnCompleted`
   materializes the `Task<TResult>` (`VoidTaskResult` for the untyped builder),
   calls `GetCompletionAction`, which allocates a `MoveNextRunner` and an
   `Action` and captures the execution context through the internal
   sync-context-free `FastCapture`, then boxes the state machine through
   `PostBoxInitialization`. When the thread's context holds no `AsyncLocal`
   values, change notifications, or logical call context data and flow is not
   suppressed, `FastCapture` returns the shared default context and the runner
   and delegate are cached for later suspensions of the same invocation; when
   an `AsyncLocal` value is present, every suspension allocates a fresh
   context, runner, and delegate. UniTask rents a pooled
   `AsyncUniTask<TStateMachine>` runner that stores the state machine by
   value, reuses one cached `MoveNext` delegate, captures nothing, and returns
   `new UniTask(runner, version)`.
3. **Resumption.** `MoveNextRunner.Run` resumes through the internal
   `ExecutionContext.Run(context, callback, state, preserveSyncCtx: true)`,
   which on the default path only establishes another copy-on-write scope and
   otherwise installs the captured `AsyncLocal` values while keeping the main
   thread's Unity synchronization context. That is two delegate invocations
   plus an interface `MoveNext`; the direct `MoveNext` branch runs only when
   flow was suppressed. UniTask calls `stateMachine.MoveNext()`.
4. **Completion and consumption.** A successful synchronous completion reuses
   the cached completed task, so `OnityTask.FromTask` allocates nothing there;
   a synchronous exception or cancellation forces a `Task` through
   `SetException` in both Onity builders. Consumers go through
   `OnityTaskAwaiter.GetResultSlow`, a type test, and `TaskAwaiter.GetResult`
   validation. UniTask flips a status in `UniTaskCompletionSourceCore`, and
   `GetResult` returns the runner to its pool. Onity's faster suspended
   `GetResult` slice is a side effect of the completed-task check being cheap
   once the task exists.

Items 1 and 2 are where the synchronous 1.85x to 2.19x and the scheduling
1.8x to 1.9x come from; item 4 is why the Onity `GetResult` slice is faster.

## Execution context: what is and is not possible

UniTask's speed and allocation profile come from not flowing
`ExecutionContext` at all. Its README says so in its .NET Core section:
"`AsyncLocal` also does not work because it ignores ExecutionContext."

An earlier draft of this memo claimed that no public API could capture the
execution context without copying Unity's synchronization context. That is
wrong. `ExecutionContext.Capture()` copies the synchronization context only
when the thread's context currently holds one, and
`SynchronizationContext.SetSynchronizationContext` is a plain field write on
the thread's mutable context (`CreateMutableCopy` does not deep-copy the sync
context). A public, reflection-free capture that keeps `AsyncLocal` values and
never calls `UnitySynchronizationContext.CreateCopy` is therefore:

```csharp
SynchronizationContext outer = SynchronizationContext.Current;
SynchronizationContext.SetSynchronizationContext(null);
ExecutionContext captured;
try
{
    captured = ExecutionContext.Capture(); // null when flow is suppressed
}
finally
{
    SynchronizationContext.SetSynchronizationContext(outer);
}
```

The rejected value-continuation candidate of 2026-09-23 measured 552 B/op in
the `UnitySynchronizationContext` constructor and 48 B/op in `CreateCopy`
because it called `Capture()` while the Unity context was installed, not
because the public API cannot avoid the copy.

Resuming through the public `ExecutionContext.Run(context, callback, state)`
does not preserve the resuming thread's synchronization context, so inside the
callback `SynchronizationContext.Current` is null until the callback's first
statement re-installs the context read on the resuming thread just before
`Run`; that is two field writes with no allocation and reproduces what the
internal `preserveSyncCtx: true` path does. A captured context is single-use,
so the runner must capture at every suspension, dispose after `Run`, clear
the field when returning to the pool, and fall back to a bare `MoveNext` when
`Capture()` returns null under suppressed flow.

What this costs per suspension, from the source rather than a measurement:
one `ExecutionContext` of 72 bytes, paid even when no `AsyncLocal` is set
because the public `Capture()` has no default-case shortcut; about 56 bytes of
`LogicalCallContext` on the Mono JIT profile only (`FEATURE_REMOTING` is
defined there and not on IL2CPP); and one 72-byte mutable copy when the first
suspension happens inside an enclosing copy-on-write scope. A pooled runner
that flows the context would therefore land near 136 to 144 B/op on IL2CPP
and 190 to 200 B/op on Mono, against 304 to 312 B/op today and 64 to 72 B/op
for UniTask. Captures made on worker threads, such as before
`SwitchToMainThread`, never paid the copy because no synchronization context
is installed there.

Two things remain out of reach through public APIs. The Start-phase
copy-on-write isolation has no public equivalent, so any custom builder lets
`AsyncLocal` writes made before the first await leak to the caller unless it
pays a capture per method call. And the internal default-context shortcut is
unavailable, so the 72-byte context is paid on every suspension.

Synchronization-context behavior needs precise statements:

- A bare `MoveNext` never touches `SynchronizationContext.Current`, which is
  why a no-flow builder leaves it unchanged. Today's builder scope and
  switcher `Undo` revert a `SetSynchronizationContext` call made inside an
  `async OnityTask` method when the method yields or returns; a bare
  `MoveNext` lets such a call persist on the thread.
- Native completion-source and `WhenAny` continuations run on the completing
  thread, including workers; only frame sources and the thread switch resume
  on the main thread. A `Task` awaited inside an `async OnityTask` method
  still posts back through its own `TaskAwaiter`, which reads the non-flowed
  context.
- With flow enabled through the public path, the resumed callback must
  re-install the outer context before user code runs, otherwise
  `SynchronizationContext.Current` is a fresh `UnitySynchronizationContext`
  copy when the method suspended on the main thread, or null when it suspended
  on a worker, and `OnityTaskCompletionSource<T>` instances created there would
  record that value for unobserved-fault reporting.

A no-flow builder has a hazard beyond "`AsyncLocal` does not flow": writes
made after an await that resumed on the main thread mutate the main thread's
ambient context and persist for all later main-thread code, including capture
into later `async Task` methods and `Task` continuations. Today's switcher
`Undo` discards them. UniTask has the same behavior.

A corollary for the wider comparison: Unity's class library uses the public
`Capture()` in the CoreRT-derived `Task` constructor and in
`CancellationToken.Register`, so on the main thread every `Task` created with
a delegate and every `Register` call pays the roughly 600-byte
synchronization-context copy. Onity's native sources register cancellation
callbacks on the main thread when a token can be canceled, in
`OnityTaskSourceBase.Reset` and the Unity frame providers; the same null
window, or UniTask's `SuppressFlow` pattern, could remove that copy. That is a
separate measurable change, not part of this decision.

## Options

| Option | Allocation and time outlook | Semantics | Scope |
| --- | --- | --- | --- |
| A. Keep the wrapped `AsyncTaskMethodBuilder` | Unchanged: 304 to 312 B/op and 1.8x to 1.9x slower scheduling; synchronous typed results already stored inline. | `AsyncLocal` flows; synchronous-part writes isolated; `SetSynchronizationContext` inside the method reverted. | None. |
| B. Pooled native builder without context flow | Expected near UniTask's class for the scheduling and synchronous slices: one pooled runner per suspended method, cached `MoveNext`, no `Task`; synchronous completion without any `Task` or scope. Runners derived from the existing source bases take three uncontended locks per suspended method where UniTask's core is lock-free. | UniTask semantics: `AsyncLocal` does not flow across `async OnityTask` awaits; writes before the first await leak to the caller; writes after an await resumed on the main thread persist in the main thread's ambient context; `SetSynchronizationContext` inside the method persists. Results of suspended methods become single-consumer pooled values that throw on reuse; share them with `Preserve()`. | New runner types deriving from the native source bases, a native `Forget`, an `AsTask` bridge fix, IL2CPP deferred pool return, plus the test and documentation changes listed below. Roughly 650 to 800 runtime lines and more than 20 new test cases. |
| C. Pooled native builder with context flow through the null-window capture, and a static switch to turn flow off | Flow on: about 136 to 144 B/op on IL2CPP and 190 to 200 B/op on Mono with no `AsyncLocal` set, unmeasured. Flow off: option B. | Flow on keeps `AsyncLocal` flow across awaits and the Unity context at resumption, but not the Start-phase isolation or the `SetSynchronizationContext` revert. Results are single-consumer as in B. Flow off is option B. | Option B plus the capture, the re-install, a per-suspension captured field, and tests for both settings. Roughly 800 to 950 runtime lines. |

Option A cannot close the measured gap. Option B closes it at the cost of a
documented semantic change. Option C keeps `AsyncLocal` working by default and
still removes the `Task`, the boxed state machine, and the runner and delegate
allocations; its flow-off setting reaches option B for projects that never use
`AsyncLocal`.

**Recommendation:** choose C with flow enabled by default, expose the switch
as a static property such as `OnityTask.FlowExecutionContext`, and measure
both settings in the comparison matrix. If a simpler product is preferred,
choose B and document `AsyncLocal` as unsupported inside `async OnityTask`
methods, as UniTask does. Either choice changes public behavior and belongs in
the changelog and the migration guide.

Rules for the switch under option C: the flow decision is taken per
suspension from the runner's captured field, never by re-reading the switch at
resumption, so a flip while methods are suspended cannot resume with a null
context or leave a captured context undisposed; the switch is process-wide and
also changes third-party code compiled against Onity, so it should be set
before any `async OnityTask` method starts, for example from a static
initializer or a `SubsystemRegistration` hook; and both transitions need
tests.

## Changes that either option forces

- `OnityTaskBuilderEditModeTests` assert a Task-backed representation
  (`SynchronousException_RemainsTaskBacked`,
  `SynchronousCancellation_RemainsTaskBacked`,
  `SuspensionAfterCompletedAwait_RemainsTaskBacked`,
  `SafeAwaiterSuspension_RemainsTaskBacked`) and must change with the
  representation.
- `SwitchToMainThread_AsyncLocal_FlowsThroughAsyncTaskAndAsyncOnityTask` fails
  under option B and under option C with flow off; the
  `async OnityTask` half of `SwitchToMainThread_SuppressedFlow_DoesNotFlowAsyncLocal`
  becomes vacuous without flow and should assert the chosen contract.
- `docs/guide/onitytask.md`, the verification row in
  `10-OnityTask-MainThreadSwitch.md`, and the XML documentation of
  `OnityTaskThreadSwitchAwaiter.OnCompleted` currently promise that
  `async OnityTask` methods flow `AsyncLocal` values through their builders.
- `async OnityTask` methods are not tracked by `OnityTaskTracker` today, since
  the builder returns `OnityTask.FromTask(m_builder.Task)` without a `Track`
  call. Tracking runner-backed methods would be new per-method cost, and the
  tracker's own `ContinueWith` capture is independent of any builder switch,
  so allocation gates must state whether tracking is off.
- Results of suspended `async OnityTask` methods are Task-backed and
  multi-consumer today. A pooled runner makes them single-consumer values whose
  status reads throw on a stale token, and `AsTask()` after a native await
  throws as it does for frame sources. This is the largest user-visible change
  of either option and belongs in the changelog and migration guide. The
  existing tests `SuspensionAfterCompletedAwait_RemainsTaskBacked` and
  `SafeAwaiterSuspension_RemainsTaskBacked` call `AsTask()` and then await the
  same value, which becomes an invalid scenario, not merely a wrong assertion.
  The plan must also choose whether the runner bumps its version when it
  returns to the pool (deterministic throw after the first read, UniTask's
  behavior) or when it is rented again (the current base behavior, which makes
  the throw depend on later pool traffic).
- `Preserve()` casts a native source to the concrete `OnityTaskSourceBase` or
  `OnityTaskSourceBase<T>` to read the preserved result, so a runner must derive
  from those exact bases, or `IOnityPreservedTaskSource` must gain a
  `GetPreservedResult` member that `OnityPreservedTaskSource` calls instead.
- `Forget()` is `AsTask().Forget(...)`, `WhenAll` bridges every input that is
  not a completion source, and `AsTask()` on a runner-backed task would create
  a `TaskCompletionSource` through the base class without the suppress-flow
  window that `OnityTaskCompletionSource.CreateTaskBridge` uses. Today's
  builder tasks are already `Task` objects, so these paths would regress from
  zero extra allocation to one bridge plus, on the main thread, the
  synchronization-context copy described above. A native `Forget` for source
  inputs, the suppress-flow window in the base `AsTask`, and a pair coordinator
  that accepts runner inputs are therefore in scope, and these slices need a
  "not worse than today" gate. Whether forgotten builder tasks stay visible to
  the tracker, which only the bridge provides today, must be decided.
- Cancellation mapping: the .NET builder stores the thrown
  `OperationCanceledException` instance and `TaskAwaiter` rethrows that
  instance with its stack trace; `OnityTask.FromCanceled` calls
  `Task.FromCanceled`, which throws `ArgumentOutOfRangeException` for a token
  that is not canceled, the untyped source base has no
  `TrySetCanceled(OperationCanceledException)` overload, and both bases rethrow
  with `throw exception;`. A synchronous `throw new OperationCanceledException()`
  before the first await would fail inside the builder unless the exceptional
  synchronous path keeps a throwaway `AsyncTaskMethodBuilder` or a non-pooled
  completed source that stores the instance, and the bases capture
  `ExceptionDispatchInfo` for faults and cancellations.
- Unobserved faults of never-consumed runner-backed methods would be dropped
  silently. That matches today's Task-backed builder, whose faults are also
  silent unless `Forget` observes them, but UniTask publishes them through
  `UniTaskScheduler.UnobservedTaskException`; decide whether to add a
  fault-only holder with a finalizer like `OnityTaskCompletionSource`'s or to
  document the gap beside the `Forget` guidance.

## Implementation plan for B or C

1. Add `OnityAsyncStateMachineRunner<TStateMachine>` deriving from
   `OnityTaskSourceBase` and `OnityAsyncStateMachineRunner<TStateMachine, T>`
   deriving from `OnityTaskSourceBase<T>`, pooled per closed generic type with
   the `Stack` plus lock pattern of `OnityFrameTaskSource`, the state machine
   stored by value, one cached `MoveNext` delegate, and the bases' version
   token, status, and exception storage. Add
   `TrySetCanceled(OperationCanceledException)` to the untyped base and make
   both bases rethrow through `ExceptionDispatchInfo`.
2. Keep the bases' single-consumer and stale-token rules, so `Preserve()`,
   `AsTask()`, `WhenAll`, `WhenAny`, and `Forget` keep working through the
   existing contracts. Return the runner to the pool from `GetResult` after
   the single consumer reads it, or from the completion path when a `Task`
   bridge already holds the outcome, as the bases do today. Add a native
   `Forget` for source inputs, give the base `AsTask` bridge the suppress-flow
   window, and let the two-input `WhenAll` coordinator read runner outcomes.
3. Replace the builders' internals: `Start` runs `MoveNext` directly;
   `AwaitOnCompleted` and `AwaitUnsafeOnCompleted` rent the runner on first
   suspension and register its cached delegate; `SetResult` and
   `SetException` complete the runner, mapping `OperationCanceledException` to
   the canceled status with the thrown instance preserved; `Task` returns the
   runner's native task, the inline result when the method completed
   synchronously, or a throwaway `AsyncTaskMethodBuilder` task when it failed
   or canceled synchronously so the exact .NET mapping is kept on that path.
4. For option C, capture through the null window at each suspension, store
   the context on the runner, resume through the public `ExecutionContext.Run`
   with the outer synchronization context re-installed as the first statement,
   dispose the context after the run, and fall back to a bare `MoveNext` when
   the capture returned null.
5. Design in the IL2CPP deferred pool return from the start: UniTask 2.5.11
   still ships it unconditionally under `ENABLE_IL2CPP` for a Unity issue
   marked Won't Fix, and Onity has two return sites (the consumer's
   `GetResult` and the completion path with a materialized bridge). Add a
   cached return delegate and defer both sites through the dispatcher or the
   runner under `ENABLE_IL2CPP`. Note that the repository has no IL2CPP build
   job, so the player gate needs tooling.

## Acceptance gates

- Calibrated Editor/Mono allocation in Release code optimization for the four
  async-method scheduling cases, with the 64 KiB positive and 0 B empty
  controls passing on every participating thread in two Profiler processes and
  the task tracker off: within 16 B/op of UniTask with flow off; at or below
  200 B/op on Mono with flow on and no `AsyncLocal` set, with the one-`AsyncLocal`
  case measured and recorded without a bound until it is; and the IL2CPP figure
  recorded separately. `Forget`, `AsTask`, and `WhenAll` of builder tasks must
  not allocate more than today.
- Scheduling and synchronous-completion timing within 1.2x of UniTask in the
  same run for the four scheduling and two synchronous slices, and never slower
  than the current builder, in two independent timing processes in Release code
  optimization, with raw samples retained.
- Full EditMode and PlayMode suites green, including the rewritten builder and
  `AsyncLocal` tests and new tests for pool reuse, stale-token rejection and
  status reads after the first await, re-renting the same closed type inside a
  producer's `MoveNext`, `OperationCanceledException` with an uncanceled token
  and with a subclass thrown synchronously and after suspension with instance
  identity asserted, `Preserve`, `AsTask`, `Forget`, the synchronization
  context observed after resumption, and both switch settings including a flip
  while methods are suspended.
- Focused Windows IL2CPP Player run in Release code optimization and a
  non-development build, asserting at runtime that the state machines are
  value types, covering suspension, resumption, both pool-return sites, the
  materialized-bridge completion, re-rent inside `MoveNext`, and the flow-on
  capture path.
- Changelog, migration guide, usage guide, and the comparison guide updated
  with the chosen semantics and the measured result before any release.
