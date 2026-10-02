# PAR-B6 Unity operation payloads (plan 16, Stage 5a)

## Changed files (all new)
- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityUnityOperationExtensions.cs` (+ .meta)
- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityProgress.cs` (+ .meta)
- `Packages/com.onity.framework/Tests/EditMode/Scripts/OnityUnityOperationExtensionsEditModeTests.cs` (+ .meta)
- `Packages/com.onity.framework/Tests/PlayMode/OnityUnityOperationExtensionsPlayModeTests.cs` (+ .meta)
- `docs/Plan/notes/par-b6-unity-payloads.md`

No existing file edited (OnityAsync.cs, asmdefs untouched).

## API added (namespace `Onity.Unity.Async`, extension class `OnityUnityOperationExtensions`)
- `ResourceRequest.AsAssetOnityTask(progress, ct)` -> `OnityTask<Object>`; `AsAssetOnityTask<T>(progress, ct)` -> `OnityTask<T>` (`T : Object`).
- `AssetBundleRequest.AsAssetOnityTask(...)` / `AsAssetOnityTask<T>(...)`; `AwaitForAllAssets(progress, ct)` -> `OnityTask<Object[]>`.
- `AssetBundleCreateRequest.AsAssetBundleOnityTask(progress, ct)` -> `OnityTask<AssetBundle>`.
- `UnityWebRequestAsyncOperation.AsWebRequestOnityTask(progress, ct)` -> `OnityTask<UnityWebRequest>`; failure faults with existing `OnityUnityWebRequestException`; cancelling a pending request aborts it.
- `AsyncGPUReadbackRequest.AsOnityTask(ct)` -> `OnityTask<AsyncGPUReadbackRequest>` (no progress parameter: UniTask has none, so it was removed as non-parity; faults with `InvalidOperationException` whenever `hasError` is observed, in the polling loop as UniTask `MoveNext` does, and also on the already-done fast path because a failed request reports done and error together).
- `OnityProgress.Create(Action<float>)` -> `IProgress<float>` (inline callback, no sync-context capture).

## What / why
Mirrors pinned UniTask 2.5.11 `UnityAsyncExtensions`: null argument throws synchronously; pre-canceled token returns a canceled task without touching the operation; already-done operation returns a completed task (progress reported 1 once, matching the existing Onity `AsOnityTask` fast path; UniTask reports nothing on its already-done path, a documented minor divergence). The pending path awaits the existing pooled `OnityAsyncOperationTaskSource` via `AsOnityTask<TAsyncOperation>` (Update phase, progress each tick then 1), then reads the payload. Main-thread/Play-session affinity is documented on the class. GPU readback polls with `OnityTask.Yield(Update, ct)` per frame, checking `hasError` before `done`.

## Deviations from UniTask
- Already-done fast path reports progress 1 (UniTask reports nothing).
- GPU readback already-done fast path checks `hasError` and faults; pinned UniTask returns the failed request unchecked.
- No `cancelImmediately` / `PlayerLoopTiming` parameters.

## Verification
- Roslyn: `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project <worktree> --files <4 new .cs>` -> COMPILE_OK (Onity.Unity, Onity.Tests.EditMode, Onity.Tests.PlayMode).
- EditMode: `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode -testFilter OnityUnityOperationExtensions ... -releaseCodeOptimization` -> 4/4 passed. `Logs/PAR-B6/EditMode.xml`, `EditMode.log`.
- PlayMode (same flags): 7 passed, 1 skipped (`AsyncGpuReadback_CompletesWhenSupported`, `Assert.Ignore`: no async readback under -nographics), 0 failed. `Logs/PAR-B6/PlayMode.xml`, `PlayMode.log`. First PlayMode run had 2 test-authoring failures (late asset-bundle error log; cancel test on a file request that completes synchronously); fixed in the tests and re-run green.
- Fix round (after last edit): Roslyn COMPILE_OK; EditMode 4/4; PlayMode run twice, 7 passed + 1 skipped (GPU, -nographics) each, 0 failed. Artifacts `Logs/PAR-B6-fix/{EditMode,PlayMode1,PlayMode2}.xml|log`. Asset-bundle invalid-data test now scopes `LogAssert.ignoreFailingMessages` and asserts `isDone`, null bundle and progress 1.
- Unity rewrote `ProjectSettings/EntitiesClientSettings.asset`, `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json`; restored with `git restore`.
- Slot-1 acquired and released; slot-2 belongs to another packet and was not touched.

## Integrator lines
- CHANGELOG: "Added OnityTask adapters for ResourceRequest, AssetBundleRequest, AssetBundleCreateRequest, UnityWebRequestAsyncOperation and AsyncGPUReadbackRequest, plus OnityProgress.Create."
- Parity matrix: UnityAsyncExtensions ToUniTask/WithCancellation payload overloads -> done; `cancelImmediately` and `PlayerLoopTiming` parameters not provided (Update only).
- Guide: show `await Resources.LoadAsync<T>(path).AsAssetOnityTask<T>(OnityProgress.Create(p => ...), ct)`.

## Open questions
- Packet row says "AssetBundleRequest.AsAssetBundleOnityTask()"; I put `AsAssetBundleOnityTask` on `AssetBundleCreateRequest` (payload `AssetBundle`, as in UniTask) and `AsAssetOnityTask` on `AssetBundleRequest`. Confirm naming.
- No positive typed-asset test: the repo has no `Resources` folder and new fixtures were out of scope; typed path is tested with a missing asset (null result, progress 1). A real-asset fixture would strengthen it.
- `cancelImmediately` (UniTask) is not mirrored; cancellation is observed at the next Update tick.

- Verification limits: the GPU readback pending path (including hasError) and the positive typed-asset path are verified only by Roslyn and code review; the GPU test is ignored under -nographics and no Resources fixture exists.
