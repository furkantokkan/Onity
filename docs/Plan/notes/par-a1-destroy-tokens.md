# PAR-A1 - Destroy-bound cancellation tokens

## Changed files (all new)
- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsyncDestroyTrigger.cs` (+ .meta) - internal hidden component.
- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityTaskLifetimeExtensions.cs` (+ .meta) - public extensions, namespace `Onity.Unity.Async`.
- `Packages/com.onity.framework/Tests/EditMode/Scripts/OnityTaskLifetimeEditModeTests.cs` (+ .meta) - 7 tests.
- `Packages/com.onity.framework/Tests/PlayMode/OnityTaskLifetimePlayModeTests.cs` (+ .meta) - 9 tests.
- This note.

## API added (all main-thread only)
- `MonoBehaviour/GameObject/Component.GetCancellationTokenOnDestroy()`
- `CancellationTokenSource.RegisterRaiseCancelOnDestroy(Component | GameObject)` (void)
- No `CancellationTokenSource.AddTo` overloads (removed in the fix round: not in pinned UniTask 2.5.11, and with both `Onity.Unity.Async` and `Onity.Unity.Reactive` imported it would silently change `IDisposable.AddTo(owner)` call sites from dispose to cancel)

## What / why
- MonoBehaviour (and Component that is a MonoBehaviour) returns Unity's native `destroyCancellationToken`.
- GameObject / non-MonoBehaviour Component share one lazily added `OnityAsyncDestroyTrigger` per GameObject (`HideFlags.HideInInspector`, `DisallowMultipleComponent`). The trigger caches its token, so repeated calls do not allocate.
- Cancels exactly once in `OnDestroy`; after destruction the trigger returns an already-canceled token.
- Already-destroyed (Unity-null) owner returns an already-canceled token, and registering on it cancels the source inline. C# null throws `ArgumentNullException`.
- Never-awakened case (inactive object destroyed before Awake, Unity does not call OnDestroy): while `Awake` has not run, an `OnityUnityObservable.EveryUpdate()` monitor polls and cancels once the trigger is destroyed (equivalent to UniTask's AwakeMonitor). Monitor only starts in Play mode and stops after Awake or cancel.
- Registration suppresses ExecutionContext flow around `Token.Register` (UniTask `RegisterWithoutCaptureExecutionContext` parity; `UnsafeRegister` is not available in Unity 2022 profile).
- Deviation from pinned UniTask: `CancelSource` swallows `ObjectDisposedException` (a source disposed before destroy used to make OnDestroy throw an AggregateException).
- GameObject/Component overloads depend on the hidden trigger, which only exists in Play mode; in Edit mode registration on a live non-MonoBehaviour owner is not raised on destroy (XML-documented).

## Verification
- Roslyn: `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project C:/Users/e-fur/.codex/worktrees/onity-destroy-tokens/Onity --files <4 cs> --dependents` -> COMPILE_OK (Onity.Unity, Onity.Editor, Onity.Tests.EditMode, Onity.Tests.PlayMode).
- EditMode: `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode -testFilter OnityTaskLifetimeEditModeTests -releaseCodeOptimization` -> 7/7 passed (fix round). Artifacts `Logs/PAR-A1-fix/EditMode.xml`, `EditMode.log`.
- PlayMode: same with `PlayMode` / `OnityTaskLifetimePlayModeTests` -> 9/9 passed (fix round, incl. disposed-source-before-destroy test). Artifacts `Logs/PAR-A1-fix/PlayMode.xml`, `PlayMode.log`. (First run had 1 failure: disposed-source cancel threw in OnDestroy; fixed as above, re-run green.)
- Unity rewrote `ProjectSettings/EntitiesClientSettings.asset`, `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json`; restored with `git restore`. Slots released.
- The `Logs/` folder is gitignored; `Logs/PAR-A1/run.sh` is a helper only.

## Integrator lines
- CHANGELOG: "Added destroy-bound cancellation: `GetCancellationTokenOnDestroy`, `CancellationTokenSource.RegisterRaiseCancelOnDestroy` for GameObject and Component (UniTask parity)."
- Guide: destroy-token section; mention `cts.RegisterRaiseCancelOnDestroy(owner)` cancels (never disposes), swallows a source disposed earlier, and requires `using Onity.Unity.Async;`.
- Parity matrix: `GetCancellationTokenOnDestroy` (MonoBehaviour/GameObject/Component), `RegisterRaiseCancelOnDestroy` -> done; `AsyncDestroyTrigger.OnDestroyAsync` not in scope.

## Open questions
- HideFlags: pinned UniTask sets none; `HideInInspector` used per packet text.
