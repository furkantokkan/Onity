# PAR-A3: OnityTaskVoid

Branch `parity/onitytask-void`, base 76f0190. Pinned reference: UniTask 2.5.11 `UniTaskVoid`,
`AsyncUniTaskVoidMethodBuilder`, `UniTask.Factory.cs` (`Void`, `Action`, `UnityAction`).

## What changed

- `Async/OnityTaskVoid.cs`: `OnityTaskVoid` (readonly struct, `[AsyncMethodBuilder]`, no-op `Forget()`)
  and `OnityTaskVoidMethodBuilder`.
- `Async/OnityTask.Void.cs`: `partial OnityTask` statics `Void` (3), `Action` (3), `UnityAction` (13),
  the exact overload set of UniTask 2.5.11 (plain, `CancellationToken`, state, generic 1-4 arguments,
  generic 1-4 arguments with token).
- `Tests/EditMode/Scripts/OnityTaskVoidEditModeTests.cs`.
- No existing file edited; no new runner type.

## Design

- `OnityTaskVoidMethodBuilder` holds an `OnityTaskMethodBuilder` by value and forwards `Start`,
  `AwaitOnCompleted`, `AwaitUnsafeOnCompleted`, `SetStateMachine`. The inner builder binds the pooled
  `OnityAsyncStateMachineRunner<TStateMachine>` on first suspension (the runner field lives inside the
  state machine through the wrapper field, so the by-value copy sees it).
- `SetResult` / `SetException` forward to the inner builder, then consume the inner task in place:
  `m_builder.Task.GetAwaiter().GetResult()`. Consuming releases the runner to its pool (no leaked
  single-consumer source) and surfaces a fault. A method that never suspends has a null runner, so
  the sync-success path allocates nothing.
- Faults go through `OnityTaskForgetObserver.Report(exception, null)`, the exact function `.Forget()`
  uses (log with `Debug.LogException`). This avoids a per-completion observer allocation that
  `Task.Forget()` would add on the steady-state success path.
- Cancellation: `OperationCanceledException` (including `TaskCanceledException`) is ignored, matching
  UniTask's default `UniTaskScheduler.PropagateOperationCanceledException = false`. This deliberately
  differs from `OnityTask.Forget()`, which logs cancellation.
- `OnityTaskVoid.Forget()` is a no-op (UniTask parity).
- Execution-context flow follows `OnityTask.FlowExecutionContext` through the inner builder.
- Task tracking: void methods are not registered with `OnityTaskTracker` (UniTaskVoid is not tracked
  either); only the `OnityTask.Forget()` bridge tracks.

## Main-thread affinity

The method starts on the calling thread and resumes on the thread that completes its awaited
operation (PlayerLoop-driven Onity awaits resume on the Unity main thread). `OnityTask.Void/Action/
UnityAction` do not marshal; call them from the main thread when the method touches Unity objects.

## Allocation statements

- Sync completion: no allocation from the builder.
- Suspended method: pooled runner (rent allocates only on pool miss), same as `async OnityTask`.
  The `OnityTask.Action/UnityAction` factories allocate the closure and delegate once at creation.
- No zero-allocation claim is made without a Release Player measurement.

## Verification

See the packet report for results and artifact paths (`Logs/PAR-A3/`).
