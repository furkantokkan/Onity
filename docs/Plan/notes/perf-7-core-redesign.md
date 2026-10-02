# PERF-7: OnityTask core redesign and Phase T parity (note)

Branch `perf/onitytask-core-redesign` (from `perf/surpass-unitask`, last merged at `61ff67b`).
Implementation, Roslyn compile checks and written (not run) tests only. No Unity test, smoke,
self-test or benchmark was run on this branch; the attempt operator runs them.

## Commits

| Commit | Content |
| --- | --- |
| `fae679c` | S1: cctor-free `OnityTask`/`OnityTask<T>` (statics moved to `OnityTaskSettings`), awaiter fast paths, no write barrier on completed awaits |
| `c01affb` | S2: class-rooted sources (`OnityTaskCore`, `OnityTaskSourceCore`, `OnityTaskSourceBase(<T>)`) with the single-word completion protocol |
| `76eff12` | S3: array-slot `OnityRunnerPool<T>`, shared `SourcePoolCapacity` (default 256) |
| `26393fb` | S3: composition sources use `OnityTaskSettings.s_sourcePoolCapacity` |
| `2f40f81` | S4: immediate runner return (D5), class-typed builder runners; `ONITY_RUNNER_RETURN_GATE` fallback kept |
| `2dfbaa7` | S5: stateless default frame waits, reference-free `OnityYieldAwaitable` |
| `a4cb2fc` | S6: protocol, stateless-wait and immediate-return tests and smoke cases |
| `e59504c` | H3: Forget and completion-source unobserved faults route through `OnityTaskScheduler` |
| `0e2abc9` | Phase T (A2): 16 appended timings, lazy nodes, items, `cancelImmediately`, timed switches |
| `b7f8d14` | A4: timed waits, `OnityPlayerLoopTimer`, timed timeouts, UOP-06, MSC-01, `JobHandle.WaitAsync` |
| `96fbed4` | A5: implicit `OnityTask<T>` to `OnityTask` view, native Forget tracking |
| `5fa5b07` | A6: PlayerLoop timing streams |

## Public API added (CHANGELOG lines)

- `OnityPlayerLoopTiming` gains 16 members appended after `LateUpdate` (values 3-18):
  `Initialization`, `LastInitialization`, `EarlyUpdate`, `LastEarlyUpdate`, `FixedUpdateBegin`,
  `LastFixedUpdate`, `PreUpdate`, `LastPreUpdate`, `UpdateBegin`, `LastUpdate`, `PreLateUpdate`,
  `LastPreLateUpdate`, `PostLateUpdate`, `LastPostLateUpdate`, `TimeUpdate`, `LastTimeUpdate`.
  Their nodes install on first use; the three existing timings keep their values and eager nodes.
- `OnityTaskPlayerLoop`: `Initialize(params OnityPlayerLoopTiming[])`, `InitializeAll()`,
  `IsInjected(timing)`, `DumpCurrentPlayerLoop()`, `AddAction(timing, IOnityPlayerLoopItem)`,
  `AddContinuation(timing, Action)`, `IsMainThread`, `MainThreadId`, `UnitySynchronizationContext`;
  new `IOnityPlayerLoopItem`.
- `OnityTask`: `Yield(timing)` (returns `OnityYieldAwaitable`), `Yield(CancellationToken)`,
  `Yield(CancellationToken, bool cancelImmediately)`, `Yield(timing, ct, cancelImmediately)`,
  `NextFrame(timing, ct = default, cancelImmediately = false)`, `NextFrame(ct, cancelImmediately)`,
  `DelayFrame(n, timing, ct, cancelImmediately)`, `WaitForFixedUpdate()`, `WaitForFixedUpdate(ct, ci)`,
  `WaitForEndOfFrame(MonoBehaviour)`, `WaitForEndOfFrame(MonoBehaviour, ct, ci)`, `Post(Action, timing)`,
  `SwitchToMainThread(timing, ct)`, `ReturnToMainThread(ct)`, `ReturnToMainThread(timing, ct)`.
- Timed waits: `OnityDelayType { DeltaTime, UnscaledDeltaTime, Realtime }`;
  `OnityTask.Delay(TimeSpan, bool ignoreTimeScale | OnityDelayType, timing, ct, ci)`,
  `WaitForSeconds(float | int, ignoreTimeScale, timing, ct, ci)`, `WaitUntil`/`WaitWhile(Func<bool>, timing, ct, ci)`,
  `WaitUntil`/`WaitWhile<TState>(state, Func<TState, bool>, timing, ct, ci)`,
  `WaitUntilCanceled(ct, timing, completeImmediately)`, `WaitUntilValueChanged<T, U>(...)`.
- `OnityPlayerLoopTimer` (`Create`, `StartNew`, `Restart`, `Restart(TimeSpan)`, `Stop`, `Dispose`, `IsRunning`).
- `CancellationTokenSource.CancelAfterSlim(TimeSpan, OnityDelayType, timing)` and `(int ms, OnityDelayType, timing)`.
- `OnityTimeoutController(OnityDelayType, timing)`, `OnityTimeoutController(CancellationTokenSource, OnityDelayType, timing)`,
  `Timeout(int millisecondsTimeout)`.
- `Timeout` / `TimeoutWithoutException` (untyped and typed) `(TimeSpan, OnityDelayType, timing, taskCancellationTokenSource)`.
- `OnityUnityWebRequestException`: `UnityWebRequest`, `IsNetworkError`, `IsHttpError`, `Error`, `Text`, `ResponseHeaders`.
- `OnityTaskAwaiter` / `OnityTaskAwaiter<T>`: `SourceOnCompleted(Action<object>, object)`.
- `JobHandle.WaitAsync(OnityPlayerLoopTiming, CancellationToken)`.
- Implicit conversion `OnityTask<T>` to `OnityTask` (a non-consuming, allocation-free view).
- `OnityTrackedTaskInfo.IsNative` and a constructor overload with `isNative`.
- `OnityAsyncEnumerable`: `EveryUpdate(timing, ci)`, `Timer(due[, period], timing, ignoreTimeScale, ci)`,
  `Interval`, `TimerFrame(due[, period], timing, ci)`, `IntervalFrame`, `EveryValueChanged`.
- `OnityTask.FlowExecutionContext`, `RunnerPoolCapacity` (default 128) and `SourcePoolCapacity` (default 256)
  are properties backed by `OnityTaskSettings`.

## Behaviour changes (CHANGELOG lines)

- Token-less default frame waits (`NextFrame()`, `DelayFrames(n)`, `NextFixedFrame()`, `NextLateFrame()`) and,
  per D2b, token-less explicit-timing waits (`Yield(timing, default)`, `NextFrame(timing, default)`,
  `DelayFrames(n, timing, default)`) are stateless in Play: they rent nothing, any number of consumers may
  await them, `Preserve()` returns them unchanged, repeated `GetResult()` does not throw, and `Forget()` is a
  no-op. Cancelable waits stay pooled single-consumer sources.
- `Yield(timing)` without a token returns `OnityYieldAwaitable` (implicitly convertible to `OnityTask`).
  `Yield(default)` is now ambiguous (`Yield(timing)` and `Yield(CancellationToken)`); pass a named argument.
- Async-method runners return to their pool at consumption on every backend (D5).
- `Forget()` always observes single-consumer native tasks directly; with tracking on it adds a native tracker
  row (negative synthetic id) instead of bridging to a .NET task.
- A retirement during a drain restores the markers' pre-drain stamps, so waits that drain had not resumed
  report the retirement instead of success.
- Posted continuations (`Post`, `AddContinuation`) are dropped, not run, when the session ends; awaited
  yields still observe the retirement.
- `WaitForFixedUpdate()` resumes at `LastFixedUpdate` (after the physics step, like UniTask and Unity's
  coroutine instruction); `NextFixedFrame()` keeps resuming right after the fixed scripts.
- `DelayFrame(0)` waits for the next drain (UniTask); `DelayFrames(0)` still completes immediately.

## Migration doc lines (`docs/Migration/From-UniTask.md`)

- `PlayerLoopTiming.Update` maps to `OnityPlayerLoopTiming.UpdateBegin` and `FixedUpdate` to
  `FixedUpdateBegin`; Onity's `Update`/`FixedUpdate`/`LateUpdate` run after the script callbacks.
- `UniTask.Delay(int ms, ...)` maps to `OnityTask.Delay(TimeSpan.FromMilliseconds(ms), ...)`: integer
  millisecond overloads are absent because `OnityTask.Delay(2)` already means two seconds.
- `UniTask.DelayFrame(n, cancellationToken: ct)` can now stay `OnityTask.DelayFrame(n, cancellationToken: ct)`.
- `await using (UniTask.ReturnToMainThread())` maps to `OnityTask.ReturnToMainThread()`.

## Internal members that changed (harness and reflection)

- Removed: `OnityTaskSourceState`, `IOnityAsyncStateMachineRunner(<T>)`, `m_moveNextDepth`,
  `OnityTaskComposition.k_sourcePoolCapacity`; `OnityRunnerReturnGate` compiles only under
  `ONITY_RUNNER_RETURN_GATE`.
- `OnityTaskPlayerLoop.Phase.Pending` is a `WaitQueue` that implements `ICollection` (Count) for the
  self-test reflection; `s_phases` has 19 phases; `s_timers` and the three eager marker type names are kept.
- `CloseSession()` keeps its parameterless reflection signature; the legacy-closing variant is `EndSession(bool)`.
- The end-of-frame runner keeps `m_pending` (now `List<PendingWait>`), null after detach.
- `OnityPlayerLoopTaskSource.Rent` takes `cancelImmediately`; `Publish` takes the cycle version.

## Deferred

- P-UOP timing and `cancelImmediately` overloads of the AsyncOperation, ResourceRequest, AssetBundleRequest
  and AsyncGPUReadback adapters (UOP-01/02/03/07): they need per-operation polled adapters; the existing
  adapters keep polling at Update. UOP-06, MSC-01 and UOP-09 landed.
- P-C9 native shareable WhenAll output.
- THR-11 `OnityTaskSynchronizationContext`.
- Tracking of `async OnityTaskVoid` starts (only Forget is tracked natively).
- `OnityTask<T>.AsOnityTask()` (L1) still converts through an async method; it could return the new view.

## Known issue outside this lane

`Tests/PlayMode/UGUI/Onity.Tests.UGUI.PlayMode.asmdef` lacks an `Onity.Reactive` reference: with
`com.unity.ugui` installed, `OnityUguiAsyncPlayModeTests.cs` lines 244/245/272/288 fail with CS0012 because
`BindTo` overload resolution sees `OnityReactivePropertyAsyncExtensions.BindTo<T>(ReactiveProperty...)`.
