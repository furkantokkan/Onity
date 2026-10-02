# PAR-B1: OnityAutoResetTaskCompletionSource (note)

Branch `parity/autoreset-completion-source`, base 530c4dd. No git commits made.

## Changed files (all new)

- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAutoResetTaskCompletionSource.cs` (+ `.meta`)
- `Packages/com.onity.framework/Tests/EditMode/Scripts/OnityAutoResetTaskCompletionSourceEditModeTests.cs` (+ `.meta`)
- `docs/Plan/notes/par-b1-autoreset-source.md` (this note)

No existing file was edited (`OnityAsync.cs` untouched). Untracked `.vsconfig`, `Assets/NuGet.config.meta`
and `Assets/packages.config.meta` are Unity-generated leftovers from workspace setup, not part of this packet.

## API added (public, namespace `Onity.Unity.Async`)

`OnityAutoResetTaskCompletionSource` (untyped) and `OnityAutoResetTaskCompletionSource<T>`, both `sealed`:

- `static Create()`
- `static CreateFromCanceled(CancellationToken, out int token)`, `CreateFromException(Exception, out int token)`,
  `CreateCompleted(out int token)` (untyped) / `CreateFromResult(T, out int token)` (typed). These mirror
  UniTask's `out short token` overloads; Onity tokens are `int`.
- `OnityTask Task` / `OnityTask<T> Task`
- `TrySetResult()` / `TrySetResult(T)`, `TrySetException(Exception)`, `TrySetCanceled(CancellationToken = default)`

## What and why

- UniTask parity: pooled, single-consumption source; the awaiter's `GetResult` (or the `AsTask` bridge
  release) returns it to the pool with a new version. Stale `OnityTask` values throw
  `InvalidOperationException`. Same hazard as UniTask: the producer must not touch the source after the
  consumer took the result.
- Design: a public class cannot derive from the internal `OnityTaskSourceBase`, so each pooled instance is a
  public wrapper that owns one private nested `PooledSource : OnityTaskSourceBase[<T>]`. The `OnityTask`
  value binds to the nested source; the wrapper is the pool node (`IOnityPooledRunner<T>` +
  `OnityRunnerPool<T>`, capacity 256, same as `OnityFrameTaskSource`; one pool per `T`). The nested source's
  `ReleaseSource` pushes the wrapper back. Version-retiring CAS, registration pin and bridge reservation stay
  entirely inside `OnityTaskSourceBase`.
- UniTask semantics kept: `TrySet*` is guarded by the version recorded at `Create()` (`m_createdVersion ==
  Version`), so a completion after the consumer released the source returns false; `TrySetException` with an
  `OperationCanceledException` completes as canceled and rethrows that same exception; `TrySetException(null)`
  throws `ArgumentNullException`. `Task` binds the version recorded at `Create()` (not the live version), so
  a `Task` read after release yields a stale value that throws instead of binding a recycled cycle.
- Thread affinity (documented on the types): thread-safe, no main-thread requirement; the awaiting
  continuation runs inline on the completing thread.
- Not done on purpose: tracker registration (PAR-B8 owns tracker coverage of native sources), unobserved fault
  reporting (PAR-B8), `SourcePoolCapacity` (PERF-7), `IPromise` interfaces (not in plan scope).
- Residual race, identical to UniTask: a stale producer that passed the version check while another thread
  releases and re-creates the source can complete the new cycle. The base claim CAS keeps one completion
  per cycle.

## Verification

- Roslyn: `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project C:/Users/e-fur/.codex/worktrees/onity-autoreset-completion-source/Onity --files Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAutoResetTaskCompletionSource.cs Packages/com.onity.framework/Tests/EditMode/Scripts/OnityAutoResetTaskCompletionSourceEditModeTests.cs --dependents`
  -> COMPILE_OK (Onity.Unity, Onity.Editor, Onity.Tests.EditMode, Onity.Tests.PlayMode).
- EditMode (slot-2 acquired and removed; headless):
  `"C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe" -batchmode -nographics -runTests -projectPath C:/Users/e-fur/.codex/worktrees/onity-autoreset-completion-source/Onity -testPlatform EditMode -testFilter "Onity.Tests.EditMode.OnityAutoResetTaskCompletionSourceEditModeTests" -testResults .../Logs/PAR-B1/editmode-results.xml -logFile .../Logs/PAR-B1/editmode.log -releaseCodeOptimization`
  -> exit 0, 36 passed / 0 failed. Artifacts: `Logs/PAR-B1/editmode-results.xml`, `Logs/PAR-B1/editmode.log` (gitignored).
- Covered: typed + untyped result/exception/cancel, await before and after completion, late continuation inline,
  OCE-as-cancel, default token, double `TrySet*` false, `TrySetException(null)`, pool reuse (same instance,
  new token, 1000 cycles), distinct live instances, stale token (`GetResult`, `IsCompleted`, `OnCompleted`,
  `AsTask`) on a live successor cycle, second await throws, completion after consumption false,
  `AsTask` bridge (before/after completion, fault, cancel, releases to pool, native await afterwards throws),
  `Create*` factories with token, cancellation token identity, completion from another thread.
- After Unity: `ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json` was rewritten by Unity
  and restored with `git restore`. `packages-lock.json` unchanged.

## Lines for the integrator

- CHANGELOG: "Added `OnityAutoResetTaskCompletionSource` and `OnityAutoResetTaskCompletionSource<T>`: pooled,
  single-consumption, version-reset completion sources (UniTask `AutoResetUniTaskCompletionSource` parity)."
- Guide (From-UniTask): "`AutoResetUniTaskCompletionSource[<T>]` -> `OnityAutoResetTaskCompletionSource[<T>]`;
  `Create()`, `CreateFromCanceled/Exception/Completed/Result(out int token)`, `Task`, `TrySet*`. Do not use the
  source after the awaiter has consumed the result."
- Parity matrix: row "AutoResetUniTaskCompletionSource / <T>" -> `implemented` (EditMode tests
  `OnityAutoResetTaskCompletionSourceEditModeTests`).

## Open questions

- Pool capacity is the fixed 256 per type; should it follow the planned `SourcePoolCapacity` (PERF-7) once it exists?
- Should the `out int token` factory overloads stay public? They are UniTask parity, but `OnityTask(source, token)`
  is internal, so external callers can only use the token for comparison. Harmless to drop if undesired.
- Tracker integration (`TaskTracker.TrackActiveTask` in UniTask `Create`) is deferred to PAR-B8.
