# PERF-6: intrusive pools outside `OnityAsync.cs` (note)

Branch `perf/intrusive-source-pools` (base 530c4dd). Implementation and functional tests only.
Timing, IL2CPP smoke and the adoption gate (G3 Yield, G5 WhenAny, `eof` report) run later on the
benchmark host against the PERF-1 baseline. No speed claim is made here.

## Changed files

Runtime (all `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/`):

- `OnityPlayerLoopTaskSource.cs`
- `OnityEndOfFrameTaskSource.cs`
- `OnityJobHandleExtensions.cs` (`OnityJobHandleTaskSource` only)
- `OnityWhenAllTypedCoordinator.cs`
- `OnityWhenAnyArrayTaskSource.cs` (untyped and typed sources, both size buckets)

Tests (new): `Packages/com.onity.framework/Tests/EditMode/Scripts/OnityTaskSourcePoolEditModeTests.cs` (+ `.meta`).

Not touched: `OnityAsync.cs`, `OnityAsyncStateMachineRunner.cs`, `OnityTaskThreadSwitch.cs`, benchmarks,
existing tests.

## API added

None public. Internal only: each pooled type now implements the existing internal
`IOnityPooledRunner<T>` (explicit `ref T NextPooled` over a new `m_nextPooled` field) and holds
`OnityRunnerPool<T>` static struct field(s) instead of `Stack<T>` + `lock`.

## What and why

`lock (s_pool)` around a `Stack<T>` on every rent and release is replaced by the CAS-gate
`OnityRunnerPool<T>` that `OnityFrameTaskSource` already uses (`TryPop` / `TryPush(this, cap)`).

- Caps unchanged: PlayerLoop 128, EndOfFrame 128, JobHandle 128, WhenAll coordinator 256,
  WhenAny 256 (inputs <= 16) and 128 (17..32 inputs, medium bucket). More than 32 inputs is still
  never pooled.
- Contended rent allocates a new instance; contended or full return lets the instance be
  collected (the gate never waits). Same contract as `OnityFrameTaskSource`.
- PlayerLoop `Rent` order is untouched: pop, `Reset`, assign fields, register the cancellation
  token with flow suppressed, return. Only the pop changed. EndOfFrame likewise. The
  `m_canceled` flag handling and `Publish` are unchanged.
- WhenAll coordinator: `lock(this)` callback pinning is kept. `ReturnIfReady` runs under
  `lock(this)` and now calls the non-blocking `TryPush`, so no lock order exists between the
  coordinator and the pool.
- Main-thread affinity: unchanged. These are internal sources; the public entry points keep
  their own thread checks (`Rent` of PlayerLoop/EndOfFrame is main-thread by caller contract,
  JobHandle `Rent` runs on the registering thread, WhenAny/WhenAll rent on the caller thread and
  are safe to rent concurrently, which is what the gate allows).
- Cold cost: +8 B per source or coordinator (one `m_nextPooled` reference on x64) at first
  allocation. WhenAny adds it once per instance (the field lives on the concrete type, an
  instance is in exactly one bucket). The static pool state is two ints and a reference per
  pool instead of a `Stack<T>` object (32-slot backing array) per pool, so the cold static
  footprint shrinks.
- `System.Collections.Generic` using removed where it became unused (PlayerLoop, EndOfFrame,
  WhenAll). `OnityJobHandleExtensions.cs` and `OnityWhenAnyArrayTaskSource.cs` still use it.

## Per-site `InvalidateVersion` removal proof

`InvalidateVersion()` bumped the token version once more inside each `ReleaseSource` override. It
is redundant at every site because `ReleaseSource` has exactly three callers in the two bases
(`OnityTaskSourceBase` and `OnityTaskSourceBase<T>`, `OnityAsync.cs`), and every one runs
after a successful compare-and-swap that already wrote the next version with
`OnityTaskSourceState.WithReleased` (`WithNextVersion(state | released)`):

1. `GetResultCore` (native, preserved and typed consumption): `ClaimResult` returns only from the
   CAS that sets consumed + released and bumps the version (`OnityAsync.cs` 2907 untyped, 3514
   typed). The token a caller holds is dead from that instant.
2. `AsTask` with the completion already claimed: `TryClaimBridgeRelease` returns true only from
   the CAS that sets released and bumps the version (2974 untyped, 3581 typed), then
   `ApplyStatusToTask`, `ClearCompletionReferences`, `ReleaseSource`.
3. `TrySetStatus` bridge path (completer after a materialized bridge): same `TryClaimBridgeRelease`
   claim before `ReleaseSource`.

Mutual exclusion between 1 and 2/3: `ClaimResult` throws when the mode is task or the released
bit is set, and `TryClaimBridgeRelease` fails once released is set, so a cycle releases at most
once. The source is reachable from a pool only after `ReleaseSource`, and `Reset` publishes
`NextCycle` (version + 1, all bits cleared) as the last write of the next rental, so a stale value
fails its versioned CAS both before and after reuse. There is no path where a pushed source still
carries the version of its last live token.

Site by site:

| Site | Callers of its `ReleaseSource` | Version retired by the caller? |
| --- | --- | --- |
| `OnityPlayerLoopTaskSource` | base paths 1-3, plus its own `Rent` catch | base paths yes. The `Rent` catch runs after `Reset` and before any task value or registration exists, so no token exists to retire |
| `OnityEndOfFrameTaskSource` | same as PlayerLoop | same |
| `OnityJobHandleTaskSource` | base paths 1-3 only (no direct call) | yes |
| `OnityWhenAnyArrayTaskSourceBase<T>` (`OnityWhenAnyArrayTaskSource`, `<T>`) | base paths 1-3 only; the old guard `if (!m_outputReleased)` only made `InvalidateVersion` run once per cycle | yes. The WhenAny-specific check: the winner calls `TrySetResult/Exception/Canceled` without an expected version, but only before any release (release needs a published status), and losers never complete the output. `ReturnIfReady` additionally waits for `m_remaining == 0` and no active callbacks, so the source cannot be re-rented while a late loser callback is in flight, and by then the version has long been retired by the claim. `m_outputReleased = true` is kept as the pool-eligibility flag |
| `OnityWhenAllTypedCoordinator<T>` | not a task source: no version, no `InvalidateVersion` to remove | n/a |

All five sites were provable, so every `InvalidateVersion` call in these files is removed. No site
keeps one. The WhenAny `Rent` catch calls `ReturnToPool` directly (also before any token exists).

Double-push hazard (new with an intrusive list, a duplicate would link a node to itself): every
return is single-shot. Sources by the released bit, WhenAny and WhenAll by `m_returned` (set
inside `lock(this)` before the push), the two `Rent` catch paths by being unreachable from any
other release path.

## Verification

All Unity runs headless, Unity 2022.3.62f2, `-releaseCodeOptimization`, under the shared slot
semaphore (slot-2, acquired immediately, removed after exit). The worktree has no live Editor.

1. Roslyn, runtime files with dependents:
   `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project C:/Users/e-fur/.codex/worktrees/onity-intrusive-source-pools/Onity --dependents --files <5 runtime files>`
   Result: COMPILE_OK (Onity.Unity, Onity.Editor, Onity.Tests.EditMode, Onity.Tests.PlayMode).
2. Roslyn, new test file: COMPILE_OK (Onity.Unity, Onity.Tests.EditMode).
3. EditMode (one run, 179 tests, 179 passed, 0 failed/skipped):
   `Unity.exe -batchmode -nographics -runTests -projectPath <wt> -testPlatform EditMode -testFilter "Onity.Tests.EditMode.OnityTaskPlayerLoopEditModeTests;...OnityTaskEndOfFrameEditModeTests;...OnityJobHandleEditModeTests;...OnityTaskWhenAnyArrayEditModeTests;...OnityWhenAnyEditModeTests;...OnityTaskWhenAllTypedCoordinatorTests;...OnityTaskSourceStateEditModeTests;...OnityTaskSourcePoolEditModeTests" -testResults Logs/PERF-6/EditMode-results.xml -logFile Logs/PERF-6/EditMode.log -releaseCodeOptimization`
   Per class: PlayerLoop 3 (covers Yield/NextFrame/DelayFrames argument and lifecycle cases; the
   timing cases live in PlayMode), EndOfFrame 2, JobHandle 8, WhenAny array 49, WhenAny 14, WhenAll
   typed coordinator 25, source state 40, new pool class 38.
4. PlayMode (one run, 18 tests, 18 passed):
   same command with `-testPlatform PlayMode` and filter
   `Onity.Tests.PlayMode.OnityTaskPlayerLoopPlayModeTests;...OnityTaskEndOfFramePlayModeTests;...OnityJobHandlePlayModeTests;...OnityTaskWhenAnyArrayPlayModeTests`,
   results `Logs/PERF-6/PlayMode-results.xml`, log `Logs/PERF-6/PlayMode.log`.
   PlayerLoop 10, EndOfFrame 2, JobHandle 4, WhenAny array 2.
5. Unity rewrote `ProjectSettings/EntitiesClientSettings.asset`,
   `ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json` and
   `ProjectSettings/ProjectSettings.asset`; restored with `git restore -- <path>` for those three.
   `Packages/packages-lock.json` was not changed.

Artifacts (gitignored): `Logs/PERF-6/{EditMode,PlayMode}-results.xml`, `{EditMode,PlayMode}.log`,
`editmode-run.out`, `playmode-run.out`.

### New test class `OnityTaskSourcePoolEditModeTests` (38 cases)

Reflection-driven because the sources are internal. For PlayerLoop, EndOfFrame and JobHandle:
rent, release, rent returns the same instance with a new version and a cleared pool link; stale
token rejected by `GetStatus`/`GetResult`/`AsTask`/`OnCompleted` before reuse (version retired by
the release itself, which is the proof above) and after reuse, leaving the new cycle pending;
same through the `AsTask` bridge in both orders (bridge before and after completion); fault and
cancel outcomes; cancellation registration flagged while rented, precanceled token flagged before
`Rent` returns, and no flag leak into the next rental; retention cap exactly 128; concurrent
rent/release from 4 threads never hands one instance to two owners. WhenAny (untyped/typed, small
and medium bucket): reuse with stale value rejection, cap per bucket (256 / 128). WhenAll
coordinator: reuse without retained state, retained output survives reuse, cap 256.

## Lines for the integrator

- CHANGELOG (Unreleased, Changed): "OnityTask: the PlayerLoop, EndOfFrame, JobHandle, array
  WhenAny and typed WhenAll pooled sources now use the lock-free intrusive pool already used by
  the frame source; pool caps are unchanged. A contended rent allocates instead of waiting."
  Do not state a speed figure until the host gate passes.
- Guide: nothing user-visible. If a pooling section exists, one sentence: "Contended rent or
  return never blocks; it allocates or lets the instance be collected."
- Parity matrix: no row (internal performance packet).

## Open questions

- Host gate after this lands: G3 Yield and WhenAny-32 (G5) IL2CPP and the `eof` report. The plan
  expectation (-40..-90 ns per IL2CPP Yield/timed cycle, -20..-35 ns Mono) is not claimed.
- `OnityTask<T>.AsTask`/`GetResult` stale checks in the WhenAny tests assume the existing
  `InvalidOperationException` contract; unchanged by this packet.
- PERF-7 can reuse the same `IOnityPooledRunner<T>` shape for the sources inside `OnityAsync.cs`.
