# 12 - OnityTask Player Verification

## Objective

Find why the IL2CPP benchmark does not start, fix the demonstrated cause, and
establish a reproducible Player baseline before changing the native task core.
Choose the next bounded core optimization from the measured lifecycle evidence.

## Baseline before this task (2026-09-26)

- Candidate: `528d52c3a2db2f07abb0612d2784413623c6c45d`.
- Unity: `2022.3.62f2` (`7670c08855a9`); pinned UniTask `2.5.11`.
- Full suites passed with normal and Release code optimization: 659 EditMode
  tests and 41 PlayMode tests each, including 48 EditMode builder tests.
- Mono Player completed all 24 primary scenarios. Onity was slower in all 24.
  At 128 concurrent async NextFrame operations, scheduling ratios were
  1.39-1.46x with flow on and 1.21-1.25x with flow off.
- Two Editor Release runs also had Onity slower in all 24 scenarios. Allocation
  selection differed between HeapDelta and ProfilerCounter; do not compare
  their byte readings as equivalent measurements.
- The IL2CPP build succeeded, but no benchmark-start marker or report appeared.
  The launcher timed out after 15 minutes. A short graphics-enabled retry also
  produced no start marker. The cause is unknown; this does not establish an
  async-core deadlock or prove a stripping failure.
- Temporary scenes were removed and the checked build-settings files returned
  to their original hashes. The main dirty workspace was not changed.

## Scope and ownership

- Work in an isolated checkout pinned to the candidate. Preserve the main
  workspace's existing changes.
- Implementation scope: task benchmark Editor/runtime tooling, focused task
  tests, and the task/comparison documentation.
- Change async runtime code only when evidence identifies it as the cause.
- Own only the existing benchmark tool's temporary scene during builds. Record
  settings before each run and verify restoration on success and failure.
- Keep FlowExecutionContext true by default and runner capacity 128 for the
  baseline. Preserve cancellation, stale-token, bridge, and single-consumer
  contracts. Do not publish a release during this task.

## Step 1 - Make Player startup observable

1. Add benchmark-only persistent markers around runtime initialization,
   scene loading, argument detection, and benchmark entry. Write them outside
   measured slices; do not log per operation.
2. Record backend, build/optimization settings, tracker state, flow switch,
   runner capacity, fast-path availability, and allocation-counter selection
   with the rejected candidates' reasons.
3. Add a startup watchdog to distinguish "benchmark never started" from
   "measurement timed out". A missing start marker should fail within 60
   seconds instead of consuming the whole 15-minute measurement limit.
4. Check the runtime-initialization registration and linked output. If the
   process remains busy without progress, capture its call stack before
   selecting a fix. Do not infer the cause from CPU usage alone.

## Step 2 - Diagnose and fix the demonstrated cause

1. Run a small Player smoke suite before the full benchmark: synchronous
   completion, typed/untyped suspension and resumption, native consumption,
   bridge completion, deferred IL2CPP pool return, and re-rent during MoveNext.
2. Check execution-context behavior in the Player: AsyncLocal flow/isolation,
   synchronization-context restoration, suppressed flow, and both flow modes.
   Record whether the internal pair or the public fallback actually ran.
3. Use the markers and call stack to isolate initialization, argument handling,
   stripped code, dispatcher, or task execution. Apply one surgical fix and
   repeat the failing smoke case first.
4. Add focused regression coverage for the identified failure and startup
   watchdog. Run the full EditMode/PlayMode suites after the fix, including
   Release code optimization.

## Step 3 - Establish a trustworthy Player baseline

1. Run Mono and IL2CPP primary suites in two independent processes per backend,
   using non-development Release builds, tracker off, identical workloads,
   and no concurrent tests/builds. Record other active workloads.
2. Compare each Onity/UniTask pair within its block. Keep flow-on and flow-off
   results separate; retain raw samples and variability.
3. Keep default-capacity 128 and burst 4096 cohorts. Any raised-capacity run is
   a separate experiment with retained-memory costs recorded, not a substitute
   for the default result.
4. Calibrate allocation controls in every process. Report valid sample counts
   and counter kind. HeapDelta zero is not proof of zero allocation. If timing
   and allocation need separate passes, implement and document real supported
   modes before using them; do not invent command-line flags.
5. Label the primary suite as scheduling/GetResult slices. Profile a separate
   full-cycle case to locate the remaining work in completion, resumption, and
   pool return before proposing a core optimization.

## Acceptance and next decision

- Both Player backends reach startup markers and the smoke suite exits zero.
- IL2CPP context semantics and both pool-return paths pass, with fast-path or
  fallback status explicitly recorded.
- Missing startup fails promptly with useful evidence and clean restoration.
- All current EditMode and PlayMode tests plus new regression tests pass under
  normal and Release optimization.
- Two completed primary reports per backend include calibrated allocation
  metadata and raw timing samples. Build settings and temporary assets restore.
- Update the comparison guide and this plan with measured results and limits.
- The existing 1.2x timing gate is an intermediate milestone, not proof of being
  faster than UniTask. Keep the superiority/release claim unfulfilled until the
  agreed performance and behavior gates pass.

After this task, use the Player profile to decide whether to remove the three
remaining monitor acquisitions from the native-consumption path. Keep bridge
and preserved-task behavior protected by dedicated regression tests; widening
the pool alone does not resolve the default burst-allocation result.

## Planning status

Written on 2026-09-26 from completed verification evidence. No implementation,
new build, commit, push, or release was performed in this planning turn.

## Implementation checkpoint - 2026-09-27

- Work resumed on candidate `528d52c` in an isolated checkout.
- The previous IL2CPP executable omitted `Onity.TaskBenchmarks`, `Onity.Unity`,
  and `UniTask` from both native code registration and shipped metadata. The
  empty benchmark scene did not root the package initialization assembly.
- Added assembly-level `AlwaysLinkAssembly` to the benchmark entry assembly,
  following the [Unity 2022.3 assembly-linking contract](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Scripting.AlwaysLinkAssemblyAttribute.html).
  The runtime task implementation is unchanged.
- Added persistent startup stages, a 60-second startup deadline and a separate
  15-minute measurement deadline. The launcher rejects missing handshakes and
  stale reports. Trace polling stops after startup to avoid competing I/O
  during measurement.
- All nine focused watchdog tests passed in Unity `2022.3.62f2` (CLI exit 0).
  Result: `TestResults/player-watchdog-20260927.xml` in the benchmark host.
- The rebuilt IL2CPP Player retained all three assemblies and its entry method,
  reached every startup stage and completed all 24 primary scenarios in about
  14 seconds; CLI exit 0. Report: `BenchmarkResults/player-startup-fixed-il2cpp.json`.
  This diagnostic run retained tracking enabled; it is not the final baseline.
- ProjectSettings and EditorBuildSettings SHA-256 hashes matched their pre-run
  values and `Assets/OnityBenchmarkTemp` was absent after the run.
- Complete suites passed after the startup fix: 668/668 EditMode and 41/41
  PlayMode with both default and Release code optimization. These include
  48 builder tests and nine new watchdog cases. The later changes add
  benchmark-only verification/reporting; runtime library sources are unchanged.
- The final Mono and IL2CPP verification Players each passed all 13 smoke cases and reported
  `InternalPair`, flow enabled, tracking disabled and runner capacity 128.
  Two independent primary processes per backend completed with all 24 scenarios
  and 8/8 valid allocation samples per metric. All selected calibrated HeapDelta
  with a 69,632-byte positive control and zero-byte empty control; measured zero
  does not prove zero allocation.
- The diagnostic IL2CPP report exposed immediate consume/re-rent churn before
  deferred returns drain. Preserve this default workload; an optional,
  separately labelled two-frame drain control now measures replenished pools.
- At concurrency 128, async scheduling Onity/UniTask ratios were 1.41-1.53 with
  flow on and 1.23-1.26 with flow off in Mono; IL2CPP read 1.77-1.94 and
  1.70-1.74 respectively. The separate IL2CPP drain control reduced measured
  scheduling allocation from 242-245 B/op to zero at 128; the 4096 burst still
  read 607-611 B/op. The performance target remains unmet.
- Step 3.5 is complete. Windows Performance Recorder failed with `0xc5585011`,
  so a separate Development IL2CPP build used Unity's binary profiler. All
  eight configurations and 16 library windows passed capture/export checks,
  including every resumption and deferred return. `/optimize+` and generated
  value-type helpers were verified; initial class-state-machine diagnostics
  were excluded from final attribution. Instrumented times remain distinct
  from non-development Release timing.
- The 128 drained case shows nine operation-related monitor acquisitions per
  operation; source-base `OnCompleted`, `TrySetStatus` and `GetResultCore`
  account for five. Burst allocation sites match runner/delegate/frame-source
  retention limits. Flow on/off did not change observed allocation sites.
- Next bounded task: design and verify native-only synchronization reduction
  in those three typed/untyped source methods. Preserve bridge/preserved
  behavior, concurrency/stale-token guarantees and deferred IL2CPP return.
  Keep flow enabled and capacity 128; do not bundle a new pool policy or early
  return. Runtime sources were not changed by this verification task.
- Build settings match their original hashes. Unity's empty Standalone compiler
  argument entry required serialization-only cleanup after API restoration;
  the temporary scene/folder is absent. Final diagnostic smoke passed 13/13.
- **Verification objective complete.** Superiority and the 1.2x performance
  milestone remain unmet. No commit, push or release was performed.
- [Verification report and raw evidence](../assets/benchmarks/onitytask-player-verification-2026-09-27.md).
