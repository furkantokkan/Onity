# PERF-4 note: C1 return-on-unwind (IL2CPP runners)

Packet: PERF-4 (plan 16, section 2 "PERF-4 C1 return-on-unwind", rulings R1, 1.3 and 7). Branch
`perf/return-on-unwind`, base `be13218`. Runtime part only; the benchmark/smoke edits are listed under
"Bench follow-up" because those files are owned by another packet right now.

## Changed files

- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityRunnerReturnGate.cs` (new, + `.meta`)
  - `internal static class OnityRunnerReturnGate`, compiled on every backend so Mono EditMode tests can
    drive it. One `int` word: `MoveNext` frame count in the low bits, `k_returnPending = 0x40000000`.
  - `EnterFrame(ref int)`: `Interlocked.Increment`.
  - `ExitFrame(ref int)`: `Interlocked.Decrement(...) == k_returnPending && CompareExchange(ref w, 0,
    k_returnPending) == k_returnPending`; true means "the caller pools now".
  - `RequestReturn(ref int)`: loop `v = Volatile.Read(w)`; `v == 0` -> true (pool now); else
    `CompareExchange(v | k_returnPending, v)`; success -> false (the last frame pools); failure -> re-read.
  - `EnterFrame`/`ExitFrame` are `AggressiveInlining`; nothing allocates.
- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsyncStateMachineRunner.cs`, both runner
  classes, IL2CPP blocks only:
  - `MoveNext`: `EnterFrame`; `try { MoveNextCore(); } finally { if (ExitFrame) ReturnToPool(); }`.
  - `ReleaseSource`: `if (RequestReturn(ref m_moveNextDepth)) ReturnToPool();` instead of
    `OnityTaskMainThreadDispatcher.Enqueue(m_returnToPool, 0)`.
  - `ReturnToPool`: no depth re-check / re-enqueue any more; `m_stateMachine = default`, then
    `m_executionContext` and `m_resumeContext` are stored null only when non-null (skips two IL2CPP write
    barriers in the common flow-off case), then `s_pool.TryPush(this, OnityTask.RunnerPoolCapacity)`.
  - Removed: the `m_returnToPool` cached delegate field and its constructor assignment.
  - The field keeps its name `m_moveNextDepth` (the smoke runner reads it by reflection).
  - Mono (`#else`) path is byte-for-byte the old behaviour: `ReleaseSource -> ReturnToPool()` with the
    unconditional clears.
- `Packages/com.onity.framework/Tests/EditMode/Scripts/OnityRunnerReturnGateEditModeTests.cs` (new, + `.meta`)
  - Binds the gate by reflection (no `InternalsVisibleTo` in this package) and drives it through a runner
    double with the exact IL2CPP runner shape.
  - Cases: release at depth 0; release inside the producer frame (inline continuation, re-rent sees no
    pooled runner, word = `1 | pending`); nested depth 2 (pooled only at the outer unwind); frame throws
    after the release; frame throws before the release; successive rentals alternate who pools; race of
    producer unwind vs consumer release on two threads (10,000 rounds); race of two frames unwinding on two
    threads vs a consumer release on a third (10,000 rounds). Every round asserts exactly one pooling
    decision and a zero word.
  - The race rounds are gated with `Thread.Yield` spins and a `Stopwatch` timeout. The first attempt used
    `SpinWait.SpinOnce` / `SpinWait.SpinUntil`, whose `Thread.Sleep(1)` fallback costs a ~15 ms Windows
    timer tick per round: the 2-thread race took 21.8 s and the 3-thread race hit Unity's 180 s test
    timeout (no assertion failure; a harness speed issue, not a protocol defect).

Not edited (as required): `OnityAsync.cs`, `OnityTaskThreadSwitch.cs` (dispatcher), every benchmark file.

## Protocol proof

Notation: `W` = `m_moveNextDepth`, `d` = its low 30 bits, `P` = `k_returnPending`. "Pool" = `ReturnToPool`.

Two facts of a rental, both already enforced before this packet:

- F1 (single release). `ReleaseSource` runs once per rental: only the party that wins the version-retiring
  claim reaches it (`GetResultCore` after `ClaimResult`, or `TryClaimBridgeRelease` in `AsTask` /
  `TrySetStatus`). So `RequestReturn` runs once per rental, and the version is retired before it runs.
- F2 (no frame after completion). A frame enters only when an awaiter invokes `MoveNextAction` for an await
  the state machine registered. Release needs a published outcome, the outcome is published by the
  completing frame (or, for a first-suspension fault, outside any frame), and after completion no await is
  registered. So every `EnterFrame` of a rental happens before its `RequestReturn` (an awaiter that invokes
  a continuation twice is out of contract for every builder, unchanged by this packet).

Exactly one party pools:

- Exits before the release see `after = d - 1 < P`, never `== P`, so they never pool.
- Release reads `W == 0`: by F2 no frame can enter later, and every frame has exited without pooling. The
  release pools; `W` stays 0. Exactly one.
- Release reads `v != 0` and its CAS `v -> v | P` succeeds: `d >= 1` frames are on stacks and `P` is set. Only
  exits change `W` from now on (F1, F2). `Interlocked.Decrement` is linearizable, so exactly one exit takes
  `d` from 1 to 0 and returns exactly `P`; its CAS `P -> 0` cannot fail (nobody else writes `W` at `d == 0`),
  so that exit pools once and leaves `W == 0`. The release returned false. Exactly one.
- Release CAS fails: an exit changed `W` between the read and the CAS; the loop re-reads. `W` only decreases
  while it loops, so it ends in one of the two cases above.
- `d` never reaches bit 30: that would need 2^30 nested frames of one runner.

No pooling under a running frame: the pooling exit runs after its own `MoveNextCore` returned, and it is the
last frame of the rental; the pooling release saw `d == 0`. The memory order holds because every
`Interlocked` operation is a full fence and `Volatile.Read` is an acquire: all frame writes to
`m_stateMachine` happen before the last decrement, which happens before the `m_stateMachine = default` and
the pool push of whichever party pools. The pool gate (`CompareExchange` acquire / `Volatile.Write` release)
then publishes the cleared runner to the next renter on any thread. `W` is 0 at every push, so the next
rental counts from 0 without a reset.

Interleavings:

1. Producer unwind on a worker vs consumer release on the main thread. Worker completes the method inside
   `MoveNext` (`d == 1`); main thread sees `IsCompleted` and calls `GetResult`. If the worker decremented
   first, the release reads 0 and pools on the main thread. If the release won, it sets `P` and the worker
   pools in its `finally`, on the worker thread. If they interleave between the read and the CAS, the CAS
   fails, the release re-reads 0 and pools. Covered by `ReleaseRacingTheProducerUnwindOnAnotherThread`.
   Cross-thread depth 2 (the thread that registered the last await is still unwinding while the worker
   resumes, completes and unwinds) follows the same argument with two exits; covered by
   `ReleaseRacingTwoFramesUnwindingOnTwoThreads`.
2. Release inside the producer's own `MoveNext` through an inline continuation (`SetResult` ->
   `OnityTaskContinuation.Invoke` -> consumer `GetResult` -> `ReleaseSource` at `d == 1`): sets `P`; the
   consumer continues; the producer frame unwinds and pools. Same for the `AsTask` bridge release that
   `TrySetStatus` performs inside `SetResult`: it only reads locals afterwards. Covered by
   `ReleaseInsideTheProducerFrame`.
3. Nested depth on one thread (an awaiter that runs the continuation inline from `UnsafeOnCompleted`): the
   release in the inner frame sets `P` at `d == 2`; the inner exit leaves `1 | P`; the outer exit pools.
   Covered by `ReleaseInsideANestedFrame`.
4. Exceptions from `MoveNextCore` (an inline continuation that throws out of `SetResult`, which the compiler
   calls outside its try block; `ExecutionContext.Run` failures): the `finally` still runs `ExitFrame`, so
   accounting is unchanged and the runner pools while the exception propagates. `ReturnToPool` does not
   throw and nothing on the unwinding path touches the runner afterwards. Covered by both `FrameThatThrows`
   tests.
5. Re-rent inside a producer's `MoveNext`: while `P` is pending the runner is not in the pool, so a rental of
   the same state machine type inside the continuation pops another runner or allocates one, exactly as with
   the old deferred return; the producer's `m_stateMachine` is untouched until its frame unwinds.
6. Worker completion with no consumer yet: the frame exits at `W == 1 -> 0` without pooling; the later
   release on any thread reads 0 and pools at once.
7. First-suspension fault (the awaiter's registration threw in the caller's stack copy): `SetException` runs
   outside any runner frame; the caller reads `builder.Task` (the runner's `Version`) before any consumer can
   exist; the release reads 0 and pools. The stack copy never touches the runner after that.

## Preserved contracts (plan list)

- No pooling while any `MoveNext` of the rental is on any stack (proof above).
- Version retired by the release CAS before the return: unchanged; `ReleaseSource` is still called only
  after the base's claim.
- Retention 128: unchanged; every return goes through `s_pool.TryPush(this, OnityTask.RunnerPoolCapacity)`.
- Zero steady-state allocation: the return path now allocates nothing at all (no dispatcher enqueue, no
  queue growth, no cached return delegate).
- Worker completion safety: interleaving 1 and 6.
- Re-rent inside a producer's `MoveNext`: interleaving 5.
- Cached delegates: `m_moveNext` stays cached; `m_returnToPool` is gone because nothing schedules it.
- Mono: unchanged code path.

## Shutdown and session behaviour

- IL2CPP runner returns no longer go through `OnityTaskMainThreadDispatcher`. Before, a return was queued with
  session 0 and drained on the main thread; a return queued after shutdown (`s_isShutDown`) or never drained
  (no PlayerLoop runner, application quitting) was dropped and the runner was left to the GC.
- Now the return happens synchronously on the thread that finishes last (the consumer, or the last unwinding
  frame, which can be a worker thread). Returns during or after shutdown pool instead of being dropped, and
  pooled runners stay in the static pool across play sessions in one domain, as on Mono already. Both are
  harmless: a pooled runner holds no state machine, context or continuation.
- The dispatcher's `PendingCount` no longer contains runner returns on IL2CPP; thread-switch continuations
  keep their FIFO, session and shutdown semantics unchanged.
- A push that finds the pool gate taken by another thread still drops the runner to the GC (existing
  behaviour; now reachable from worker threads too).

## Verification

- Roslyn, Editor (Mono defines), with dependents:
  `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project <worktree> --files
  <runner> <gate> <gate tests> --dependents` -> `COMPILE_OK` (Onity.Unity, Onity.Editor, Onity.Tests.EditMode,
  Onity.Tests.PlayMode).
- Roslyn, IL2CPP proxy: `Logs/PERF-4/make_il2cpp_rsp.py` builds `.rsp` files from the generated
  `Onity.Unity.csproj` (its 209 references with project references mapped to `Library/ScriptAssemblies`, its
  78 sources plus the new gate file, its defines, `LangVersion 9.0`) and compiles with the Editor's
  `DotNetSdkRoslyn/csc.dll`:
  - `Onity.Unity.il2cpp-editor-defines.rsp`: csproj defines + `ENABLE_IL2CPP` -> exit 0, 0 errors.
  - `Onity.Unity.il2cpp-player-defines.rsp`: also without `ENABLE_MONO` / `UNITY_EDITOR*` -> exit 0, 0 errors.
  - Both output DLLs contain `m_moveNextDepth` and `OnityRunnerReturnGate` and no `m_returnToPool`, so the
    IL2CPP branch was the one compiled. Logs: `Logs/PERF-4/csc-*.log`.
- EditMode, headless Unity 2022.3.62f2, `-releaseCodeOptimization`, one run under a Unity slot:
  filter = `OnityRunnerReturnGateEditModeTests`, `OnityTaskAsyncBuilderEditModeTests`,
  `OnityTaskBuilderEditModeTests`, `OnityTaskSourceStateEditModeTests`, `OnityTaskThreadSwitchEditModeTests`
  -> 121/121 passed, 0 failed, Unity exit 0 (gate 8/8 in 5.0 s, races 3.0 s and 1.9 s; async builder 56/56,
  builder 6/6, source state 40/40, thread switch 11/11). Artifacts: `Logs/PERF-4/editmode-results.xml`,
  `Logs/PERF-4/editmode.log`, `Logs/PERF-4/run-editmode.out`. The Unity-rewritten
  `ProjectSettings/Packages/com.unity.testtools.codecoverage/Settings.json` was restored afterwards.
- Roslyn checks above were re-run after the race-harness fix (Editor `COMPILE_OK`; both IL2CPP proxies exit 0,
  0 errors).

Not run here (host, later): IL2CPP Player smoke, candidate Players, paired timing rounds.

## Bench follow-up (apply when the benchmark files are released)

All in `Packages/com.onity.framework/Benchmarks/Tasks/Runtime/`. Two cases beyond the plan's list break on
IL2CPP without these edits: `TypedPoolReturn` and `UntypedPoolReturn` assert the old deferral.

### `OnityTaskPlayerSmokeRunner.cs`

1. Next to `MoveNextDepth` (inside `#if ENABLE_IL2CPP`) add
   `private const int k_returnPending = 0x40000000;`.
2. `TypedPoolReturn` / `UntypedPoolReturn`: consumption happens after the producer unwound, so the runner
   pools at once on both backends. Replace each `#if ENABLE_IL2CPP Require(!ReferenceEquals(State(early),
   runner), "IL2CPP ... returned before dispatcher drain.") #else ... #endif` with the former Mono branch
   unconditionally (`Require(ReferenceEquals(State(early), runner), "Typed runner did not return immediately
   after consumption.")`, complete and consume `early`), and delete the trailing `#if ENABLE_IL2CPP
   earlyGate.Complete(); early.GetAwaiter().GetResult(); #endif`. Labels: "typed immediate pool return",
   "untyped immediate pool return".
3. `TypedReentrantConsumption` (and the same in `UntypedReentrantConsumption`): inside the continuation,
   after `GetResult`, on IL2CPP record `pendingInside = MoveNextDepth(runner);`. After `firstGate.Complete();`:
   ```csharp
   #if ENABLE_IL2CPP
               Require(!ReferenceEquals(State(next), runner), "Typed runner was re-rented inside its IL2CPP MoveNext.");
               Require(pendingInside == (1 | k_returnPending), "Typed consumption inside MoveNext did not set the pending return.");
               Require(MoveNextDepth(runner) == 0, "Typed producer unwind did not clear the return word.");
               CriticalGate probeGate = new CriticalGate();
               OnityTask<int> probe = PoolTypedAsync(probeGate, 57);
               Require(ReferenceEquals(State(probe), runner), "Typed runner was not pooled at the producer's unwind.");
               probeGate.Complete();
               Require(probe.GetAwaiter().GetResult() == 57, "Typed unwind-pooled runner result mismatch.");
   #endif
   ```
   (`int pendingInside = 0;` declared before `OnCompleted`; untyped uses `PoolUntypedAsync` and no value.)
4. `TypedHeldWorker` / `UntypedHeldWorker`: the held frame must show a pending return, and the runner must
   pool at the worker's unwind without any frame:
   - `Require(MoveNextDepth(runner) > 0, ...)` -> `Require(MoveNextDepth(runner) == (1 | k_returnPending),
     "Typed held worker did not leave a pending return.")`.
   - Delete the two `yield return null;` before `probe = ...` (pooling no longer waits for a drain); keep the
     probe `!ReferenceEquals` check.
   - After `worker.Wait(...)` in `finally` nothing changes; then delete the two `yield return null;` before
     `late` and change the message to "Held typed runner was not pooled at the worker's unwind."; on IL2CPP
     add `Require(MoveNextDepth(runner) == 0, "Worker unwind did not clear the return word.");` before
     renting `late`. Labels: "typed held-worker return on unwind", "untyped held-worker return on unwind".
5. New case `new SmokeCase("worker unwind racing main-thread consumption returns once", WorkerUnwindRace)`
   after the held-worker cases:
   ```csharp
   private static IEnumerator WorkerUnwindRace()
   {
       for (int i = 0; i < 1000; i++)
       {
           CriticalGate gate = new CriticalGate();
           OnityTask<int> first = PoolTypedAsync(gate, i);
           object runner = State(first);
           Task worker = Task.Run(() => gate.Complete());
           SpinWait wait = default;
           while (!first.IsCompleted)
           {
               wait.SpinOnce();
           }

           Require(first.GetAwaiter().GetResult() == i, "Raced typed result mismatch.");
           Require(worker.Wait(k_workerTimeoutMilliseconds), "Raced worker did not unwind.");
   #if ENABLE_IL2CPP
           Require(MoveNextDepth(runner) == 0, "Raced return left a non-zero word.");
   #endif
           CriticalGate firstProbeGate = new CriticalGate();
           CriticalGate secondProbeGate = new CriticalGate();
           OnityTask<int> firstProbe = PoolTypedAsync(firstProbeGate, 1);
           OnityTask<int> secondProbe = PoolTypedAsync(secondProbeGate, 2);
           Require(ReferenceEquals(State(firstProbe), runner), "Raced runner was not returned.");
           Require(!ReferenceEquals(State(secondProbe), runner), "Raced runner was returned twice.");
           firstProbeGate.Complete();
           secondProbeGate.Complete();
           Require(firstProbe.GetAwaiter().GetResult() == 1 && secondProbe.GetAwaiter().GetResult() == 2,
               "Raced probe results mismatch.");
           if (i % 100 == 99)
           {
               yield return null;
           }
       }
   }
   ```
6. New case `new SmokeCase("nested depth-2 consumption pools at the outer unwind", NestedDepthReturn)`:
   ```csharp
   private static IEnumerator NestedDepthReturn()
   {
       CriticalGate gate = new CriticalGate();
       InlineGate inline = new InlineGate();
       OnityTask<int> first = NestedTypedAsync(gate, inline, 73);
       object runner = State(first);
       int observed = 0;
   #if ENABLE_IL2CPP
       int pendingInside = 0;
   #endif
       first.GetAwaiter().OnCompleted(() =>
       {
           observed = first.GetAwaiter().GetResult();
   #if ENABLE_IL2CPP
           pendingInside = MoveNextDepth(runner);
   #endif
       });
       gate.Complete();
       Require(observed == 73, "Nested consumer did not run inline.");
   #if ENABLE_IL2CPP
       Require(pendingInside == (2 | k_returnPending), "Nested consumption did not run at depth 2 with a pending return.");
       Require(MoveNextDepth(runner) == 0, "Outer unwind did not clear the return word.");
   #endif
       CriticalGate probeGate = new CriticalGate();
       OnityTask<int> probe = PoolTypedAsync(probeGate, 79);
       Require(ReferenceEquals(State(probe), runner), "Nested runner was not pooled at the outer unwind.");
       probeGate.Complete();
       Require(probe.GetAwaiter().GetResult() == 79, "Nested probe result mismatch.");
       yield return null;
   }

   private static async OnityTask<int> NestedTypedAsync(CriticalGate gate, InlineGate inline, int value)
   {
       await gate;
       await inline;
       return value;
   }

   // Reports not completed, then runs the continuation inline from UnsafeOnCompleted, so the
   // resumption is a second MoveNext frame of the same runner on the same stack.
   private sealed class InlineGate : ICriticalNotifyCompletion
   {
       public bool IsCompleted => false;
       public InlineGate GetAwaiter() => this;
       public void GetResult() { }
       public void OnCompleted(Action continuation) => continuation();
       public void UnsafeOnCompleted(Action continuation) => continuation();
   }
   ```
   (Format the gate class in Allman style like `CriticalGate` when applying.) Smoke total becomes 35 + 2.

### `OnityTaskBuilderLifecycleBenchmarkRunner.cs` (assertion only)

- Line ~381: `sample.expectedOnityBeforeFirstPassPerInvocation = IsIl2Cpp && onity ? count : 0;` -> `= 0;`.
- `CheckBeforeReturnPass` (~805): `int expectedOnity = IsIl2Cpp && m_onityLibrary ? m_count : 0;` ->
  `int expectedOnity = 0;` (consumption happens after the producing frames unwound, so every Onity runner
  pools inside the consume phase on both backends). The UniTask expectation is unchanged.
- `Report.returnBoundary` text: "IL2CPP requires N queued returns in the active library and zero in the
  inactive library before first pass; Mono requires both zero." -> "IL2CPP requires N queued UniTask returns
  when UniTask is active; Onity requires zero on both backends (runners pool on unwind). Mono requires both
  zero." The Onity `Drain` pass stays in the protocol and now measures the empty drain (~0).
- If the host validator checks `expectedOnityBeforeFirstPassPerInvocation` or `onityBeforeFirstPassSum`
  against N on IL2CPP, change that expectation to 0 as well.

## CHANGELOG lines (integrator)

`[Unreleased]`, Performance:
- IL2CPP: a suspended `async OnityTask` / `async OnityTask<T>` method's runner returns to its pool as soon as
  its result is consumed, or, when the result is consumed inside the method's own resumption (an inline
  continuation) or while a worker thread is still unwinding it, when that last resumption unwinds. The
  main-thread deferred-return queue is no longer used for runners, so the consume and return phases get
  cheaper and a runner can be reused immediately in the same frame. Mono is unchanged.

`[Unreleased]`, Changed:
- IL2CPP: runner returns during application shutdown now go back to the pool instead of being dropped, and
  `OnityTaskMainThreadDispatcher.PendingCount` no longer counts runner returns.
