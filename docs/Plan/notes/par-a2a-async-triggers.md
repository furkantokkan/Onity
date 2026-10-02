# PAR-A2a - Async trigger base and lifecycle triggers

Branch `parity/async-triggers-lifecycle` (Codex worktree), base dc50c97. The Codex work was uncommitted; it was landed on
`perf/surpass-unitask` through the salvage branch `parity/salvage-triggers-arp-linq` by byte-for-byte file copy, plus one
documentation sentence on `OnityAsyncTriggerBase<T>` (see Notes below).

## Changed files

New (namespace `Onity.Unity.Async.Triggers`, folder `Runtime/Unity/Scripts/Async/Triggers/`, each with `.meta`, plus `Triggers.meta`):
- `OnityAsyncTriggerBase.cs` - `OnityAsyncTriggerBase<T>`, private enumerator, internal `IOnityTriggerHandler<T>` and `OnityTriggerEvent<T>` (waiter list).
- `OnityAsyncTriggerHandler.cs` - `IOnityAsyncOneShotTrigger`, `OnityAsyncTriggerHandler<T>` (partial), internal `OnityTriggerCancellation`.
- `OnityAsyncLifecycleTriggers.cs` - `IOnityAsyncOnEnableHandler`, `IOnityAsyncOnDisableHandler`, Awake/Start/Enable/Disable triggers.
- `OnityAsyncTriggerExtensions.cs` - `OnityAsyncTriggerExtensions` (partial).
- Tests: `Tests/EditMode/Scripts/OnityAsyncTriggerEditModeTests.cs`, `Tests/PlayMode/OnityAsyncTriggerPlayModeTests.cs`.

Edited (the one owned existing file):
- `Async/OnityAsyncDestroyTrigger.cs` - class `internal` -> `public`, `CancellationToken` -> `public`, new `OnDestroyAsync()`. `GetOrAdd`, token, monitor and cancel behaviour unchanged (namespace stays `Onity.Unity.Async`).

## API added (all main-thread only)

- `OnityAsyncTriggerBase<T> : MonoBehaviour, IOnityAsyncEnumerable<T>`: `GetAsyncEnumerator(ct)`, `protected RaiseEvent(T)`. The existing `AsAsyncEnumerable()` extension bridges it to `IAsyncEnumerable<T>`.
- `OnityAsyncTriggerHandler<T> : IDisposable, IOnityAsyncOneShotTrigger, IOnityAsyncOnEnableHandler, IOnityAsyncOnDisableHandler` (internal constructors, as UniTask).
- `IOnityAsyncOneShotTrigger.OneShotAsync()`, `IOnityAsyncOnEnableHandler.OnEnableAsync()`, `IOnityAsyncOnDisableHandler.OnDisableAsync()`.
- `OnityAsyncAwakeTrigger.AwakeAsync()`, `OnityAsyncStartTrigger.StartAsync()`.
- `OnityAsyncEnableTrigger`: `GetOnEnableAsyncHandler()`, `GetOnEnableAsyncHandler(ct)`, `OnEnableAsync()`, `OnEnableAsync(ct)`; `OnityAsyncDisableTrigger` same for Disable.
- `OnityAsyncDestroyTrigger` (now public): `CancellationToken`, `OnDestroyAsync()`.
- Extensions on `GameObject` and `Component`: `GetAsyncAwakeTrigger`, `GetAsyncStartTrigger`, `GetAsyncEnableTrigger`, `GetAsyncDisableTrigger`, `GetAsyncDestroyTrigger`, `AwakeAsync`, `StartAsync`, `OnDestroyAsync`.

## Semantics (UniTask 2.5.11 parity unless listed)

- Multi-waiter: every handler/enumerator waiting when a message fires is resumed once; values raised while no wait is pending are skipped; a waiter added from a continuation does not get the current value.
- One-shot `OnXAsync()` unregisters after its first value (callOnce); reusable `GetOnXAsyncHandler()` waits again on each call until disposed/canceled/destroyed.
- Destroy: handler waits complete as canceled, enumerators end with `false`. `OnDisable` waiters resume before destroy completion.
- Cancellation: token cancel unregisters and completes the wait canceled with that token.
- Re-raising the same trigger from its own continuation throws `InvalidOperationException` (UniTask parity).
- `OnDestroyAsync` and the destroy token share one hidden `OnityAsyncDestroyTrigger` per GameObject.
- Pending/waiting is built on `OnityAutoResetTaskCompletionSource(<T>)` (pooled). Allocation: one handler per one-shot call (UniTask: one handler); reusable handler and enumerator steady state: no allocations observed in an EditMode test (not a zero-allocation claim; plan 3.6 requires a calibrated Release Player measurement). Enumerator: one object per `GetAsyncEnumerator` (UniTask same).

Deviations (UniTask leaves these pending forever or throws):
- Pre-canceled token, wait after dispose/cancel/destroy -> canceled task. `Dispose()` cancels a pending wait.
- Enumerator idle at destroy ends `false` on its next move; `DisposeAsync` ends a pending move `false` (Onity enumerator contract); second outstanding move throws.
- `AwakeAsync` on an inactive object completes on activation (UniTask only cancels it on destroy).
- Waiter list is a fresh implementation (UniTask's `TriggerEvent` merge loses nodes when two waiters are added during one delivery); destroy from a continuation completes everyone after the delivery unwinds instead of throwing.
- `OnDestroyAsync` on a destroyed owner returns a completed task; C# null owners throw `ArgumentNullException`.
- New triggers keep default hideFlags (UniTask); only the destroy trigger stays `HideInInspector` (A1).
- Edit mode: Unity sends no messages to these triggers, and the never-awakened monitor only runs in Play mode.

## Verification

Results below were recorded in the Codex worktree `C:/Users/e-fur/.codex/worktrees/onity-async-triggers-lifecycle/Onity`
at base dc50c97 (Unity 2022.3.62f2). They were NOT re-run on the integration branch; the combined
final test stage re-runs them there. Log directory: `<that worktree>/Logs/PAR-A2a/` (not committed).

- Roslyn (Codex worktree): `unity_compile_check.py --project <wt> --files <5 runtime + 2 tests> --dependents` -> COMPILE_OK (Onity.Unity, Onity.Editor, Onity.Tests.EditMode, Onity.Tests.PlayMode).
- EditMode `OnityAsyncTriggerEditModeTests;OnityTaskLifetimeEditModeTests` (`-releaseCodeOptimization`): first run 30/31
  (`Logs/PAR-A2a/EditMode.xml`, `EditMode.log`): `ContinuationDisposingLaterHandler_SkipsItsResult` failed on a wrong test
  expectation, fixed in the test only (the runtime files were not touched after that run). Final run (after the test fix)
  31/31 = 24 trigger + 7 lifetime (`Logs/PAR-A2a/EditMode-fix.xml`, `EditMode-fix.log`, runner `run-fix.sh`).
- PlayMode `OnityAsyncTriggerPlayModeTests;OnityTaskLifetimePlayModeTests`: 19/19 = 10 trigger + 9 lifetime
  (`Logs/PAR-A2a/PlayMode.xml`, `PlayMode.log`).
- Runner `Logs/PAR-A2a/run.sh` (slot semaphore), output `Logs/PAR-A2a/run.out`.

## Integrator lines

- CHANGELOG: "Added async lifecycle triggers (UniTask parity): `AwakeAsync`, `StartAsync`, `OnEnableAsync`/`OnDisableAsync` handlers, `OnDestroyAsync`, `GetAsyncXTrigger()`, and `OnityAsyncTriggerBase<T>` as an `IOnityAsyncEnumerable<T>` (`using Onity.Unity.Async.Triggers;`)."
- Parity matrix: AsyncTriggerBase, AsyncTriggerHandler, IAsyncOneShotTrigger, Awake/Start/Enable/Disable/Destroy triggers and `AsyncTriggerExtensions` lifecycle entries -> implemented.

## Notes for PAR-A2b / open questions

- `OnityAsyncTriggerHandler<T>` only has an untyped wait (`internal WaitAsync()`). Message triggers returning `OnityTask<TPayload>` need a typed wait (pooled `OnityAutoResetTaskCompletionSource<T>` stored in the same pending slot, completed in `OnNext`); A2b must own `OnityAsyncTriggerHandler.cs` for that.
- The plan row names `OnNextAsync`; pinned UniTask has no such member, so none was added (per-message methods only).
- `ITriggerHandler`/`TriggerEvent` are public in UniTask; kept internal here.
- Derived triggers: the base's `Awake` and `OnDestroy` are private Unity messages. A derived trigger that declares its own
  `Awake` or `OnDestroy` hides them (Unity calls only the most derived method), so the awake/destroy bookkeeping stops
  silently (UniTask has the same design). Now stated in the `OnityAsyncTriggerBase<T>` remarks; A2b generated triggers must
  not declare these two messages.
- Known limits carried from the review: the waiter list is main-thread only (a token canceled off the main thread mutates it
  while a delivery may be running; UniTask has the same limitation); each `OnityAsyncDestroyTrigger.OnDestroyAsync()` call adds
  a token registration that lives until destroy, and an un-awaited call leaves its pooled source for the garbage collector.
