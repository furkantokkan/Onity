# OnityTask comparison harness

## Setup

- Use a dedicated Unity `2022.3.62f2` benchmark host with the current Onity package.
- Install official UniTask `2.5.11`, tag commit
  `2e993ff18f28c931602a07292df0b0804eebef99`, using this pinned UPM URL:
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99`.
- Enable `ONITY_TASK_BENCHMARKS`. Leave `ONITY_BENCHMARKS` disabled unless the
  separate DI comparison dependencies are installed.
- New runtime/editor assemblies are `Onity.TaskBenchmarks` and
  `Onity.TaskBenchmarks.Editor`. No VContainer or Zenject assembly is referenced.
- Keep a clean benchmark scene and close profiling/diagnostic windows that add
  unrelated work. Record package lock revisions with published results.

Run `Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)`. The CLI
entry point is
`Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine` and accepts
`-onityTaskBenchmarkOutput <absolute-json-path>`. One process produces both
the timings and the allocation values; the counter that produced them is
recorded in the report (see the measurement contract). Do not pass `-quit`;
the menu controller exits after writing the report or hitting its 15-minute
timeout.
Verify the exact Editor version and host with the Unity CLI first. The installed
`unity run` 1.0.0-beta.3 adds `-quit` automatically, which closes the Editor
before a Play Mode benchmark can start. Invoke the pinned Unity 2022 Editor
directly for this entry point, without `-quit`:

```text
Unity.exe -batchmode -nographics -releaseCodeOptimization -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTaskBenchmarkOutput <absolute-json-path> -logFile <absolute-log-path>
```

`-releaseCodeOptimization` compiles the async state machines as structs, which
is what players run; without it the Editor's Debug code optimization compiles
them as classes and the builder measurements do not represent a player.

Editor timings still come from the Editor's own script runtime. A desktop
Mono 6.8 micro-benchmark of the builder wrapper alone (one suspended
`async` method on a manual awaitable, no PlayerLoop) measured about 50 to 120
ns per operation for Onity and about 40 ns for UniTask, while the Editor
attributes 800 ns or more to the same wrapper for both libraries; call-heavy
paths are inflated there, so ratios taken in the Editor are not the ratios a
player sees. Run the player build for any performance claim:

- `Onity/Benchmarks/Build and Run OnityTask Benchmarks (Mono Player)` and
  `... (IL2CPP Player)` build a non-development Standalone Windows player
  with that backend into `Temp/OnityBenchmarks`, run it headless, write
  `Results/onity-task-benchmark-player-latest.json` (plus `.csv`, `.md`, and
  a `.player.log`), and restore the project's build target, backend, and
  defines. `... Thread Switch Benchmarks (IL2CPP Player)` does the same for the
  thread-switch suite.
- The command-line entry point is
  `Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine`
  with `-onityTaskBenchmarkBackend Mono|IL2CPP` (default IL2CPP),
  `-onityTaskBenchmarkSuite primary|threadswitch` (default primary),
  `-onityTaskBenchmarkBuildPath <exe>`, and `-onityTaskBenchmarkOutput <json>`;
  it may be combined with `-quit`. The player itself accepts
  `-onityRunTaskBenchmark`, the same suite and output arguments, and writes
  the report with `isEditor` false and the backend it was built with.

## Measurement contract

- Two synchronous primitives: completed `GetResult`, and `FromResult<int>` plus
  `GetResult`. Each uses 4,096 warmup calls and eight samples of 1,000,000 calls.
- Two synchronous async-method cases call actual `async OnityTask`/`async UniTask`
  methods and consume their result. Each method awaits its library's completed
  task without suspending; the typed method returns `42`. These include the method
  builder path, unlike the direct primitives. They use the same warmup, sample
  count and synchronous iteration count as the primitive cases.
- The empty delegate loop is measured separately using the same invocation
  pattern. Raw times retain harness overhead; the baseline is not subtracted.
- Frame operations have two distinct cohorts: 128 concurrent operations below
  Onity's 256-source retention cap, and repeated bursts of 4,096 operations above
  that cap. A burst result must not be described as pooled steady state.
- Each library completes and consumes two warmup batches at the exact cohort
  size. Each measured sample sums 32 batches; there are eight samples per cohort.
  Library order alternates per sample and batch. All awaiter arrays are allocated
  before measurement, and all operations must complete before consumption.
- `NextFrame scheduling` times creation and storage of native awaiters.
  `NextFrame GetResult` times result consumption and clearing awaiter references.
  Scheduling and consumption measure synchronous main-thread slices separately.
  Frame waiting, runner ticking, continuation dispatch, async builders and the
  complete async lifecycle are **outside** these timings.
- `Async method NextFrame` and `Async method NextFrame<int>` each await exactly
  one native `NextFrame`; the typed method then returns `42`. Both use the same
  128/4,096 concurrency cohorts, two completed warmup batches, eight samples and
  32 batches/sample. `scheduling` includes calling the async method, its builder's
  initial suspension/continuation registration, and storing its returned awaiter.
  `GetResult` includes consuming the completed outer method result and clearing
  the awaiter; each typed result is checked against `42`, and the sum is stored
  in the same observable static sink for both libraries. Every outer operation
  is checked for completion before consumption, with the same 240-frame timeout
  as the primitive cases.
- Async-method frame timings and allocation deltas cover **only** the scheduling
  and consumption slices. Suspended-frame time, PlayerLoop work, resuming the
  method, and builder completion during that resumption remain outside them.
  These measurements are not full-lifecycle async-method costs. Builder pooling
  can differ from the primitive frame-source pool and is not assumed identical.
- Allocation values come from a calibrated counter selected at run start on
  the measuring thread. Candidates are tried in order and the first one that
  reads at least 65,536 bytes for a known 64 KiB allocation and zero bytes for
  an empty control is used: `GC.GetAllocatedBytesForCurrentThread` (reads zero
  on Unity 2022 Mono and is skipped on IL2CPP), the engine's
  `GC Allocated In Frame` counter through `Unity.Profiling.ProfilerRecorder`
  (process-wide, reset every frame, so it never measures a slice that spans
  frames), and a `GC.GetTotalMemory` heap delta (process-wide, block
  granularity). A player disables the collector inside each heap-delta slice;
  the Editor does not support changing `GarbageCollector.GCMode`, so there a
  slice in which `GC.CollectionCount` changed is discarded and the metric
  reports the valid samples it kept (`allocationSamplesValid`). The report's
  `allocationCounterKind` (`PerThread`, `ProfilerCounter`, `HeapDelta`, or
  `None`) and `allocationCounter` say which counter produced the values and
  why the others were rejected. Allocation readings are taken outside the
  timed region of each slice.
- A metric whose counter is unavailable, or whose every sample was discarded,
  reports `-1` bytes/op and never zero. Unity 2022 IL2CPP allocation claims
  require separate player evidence.
- Timer frequency/resolution, allocation controls, all timing/allocation samples,
  mean, median, range and standard deviation are retained in JSON. CSV and Markdown
  summarize the measurements. The report schema is version 5; the original
  six primitive scenarios retain their names, indices and metric fields. Two
  synchronous async-method cases and eight frame-method cases follow them, and
  schema 4 appended the same eight frame-method cases measured with
  `OnityTask.FlowExecutionContext` off, suffixed ` (flow off)`, for 24 scenarios
  total. The unsuffixed frame-method cases force the setting on, so they stay
  comparable with reports from the earlier .NET-builder implementation, and
  the run restores the caller's setting afterwards. The report records the
  setting's default at run start (`flowExecutionContextDefault`) and whether
  the task tracker was enabled (`taskTrackerEnabled`). Consumers should
  identify cases by name and concurrency. Schema 5 adds the per-metric
  `allocationSamplesValid` count and `sampleAllocationValid` flags of the
  calibrated counter chain; the thread-switch report is schema 2 for the same
  reason.

The expanded suite requires at least 5,160 rendered frames for its frame cases
(ten workload/cohort pairs, each with four warmup and 512 measured frame waits).
The command-line runner has a 15-minute timeout. Run the benchmark in a dedicated,
responsive host because a heavily throttled Editor may still hit that limit.

Results compare these specific primitives, async-method slices and workloads.
An Editor result is not an IL2CPP player result. Timing noise and pool retention
differences must be considered before making a comparison claim. No new benchmark
results are bundled inside the package; the [calibrated baseline and candidate
reports](https://furkantokkan.github.io/Onity/guide/onitytask-comparison.html)
are published with the comparison guide.

## Thread-switch harness

`Onity/Benchmarks/Run OnityTask Thread Switch Benchmarks (Play Mode)` runs
`OnityThreadSwitchBenchmarkRunner` and writes
`Packages/com.onity.framework/Benchmarks/Results/onity-thread-switch-benchmark-latest.*`.
The command-line entry point is
`Onity.Editor.Benchmarks.OnityThreadSwitchBenchmarkMenu.RunFromCommandLine`
with `-onityThreadSwitchBenchmarkOutput <absolute-json-path>`; do not pass
`-quit`. It compares `OnityTask.SwitchToMainThread` with
`UniTask.SwitchToMainThread` in seven scenarios, eight samples each, with
library order alternating:

- Two main-thread synchronous cases: a successful switch (1,000,000 calls per
  sample) and a switch with a pre-canceled token whose `GetResult` throws
  (20,000 calls per sample). Both include the delegate call, awaiter creation,
  `IsCompleted`, and `GetResult`. The main-thread allocation counter is
  calibrated with the same 64 KiB positive and empty controls as the primary
  harness.
- Worker scheduling at 128 (warm) and 4,096 (burst) continuations per batch,
  eight batches per sample. A dedicated worker thread calibrates its own
  allocation counter with the same controls, then times awaitable creation,
  `IsCompleted`, and `UnsafeOnCompleted` for the batch. Every scheduling job
  must pass both controls or the worker allocation values are reported as
  unavailable.
- Main-thread dispatch for the same batches, measured from the first to the
  last continuation executed inside one drain. The value therefore covers
  N-1 continuations, and the main-thread counter is read at both ends.
- A round trip of 128 concurrent `async Task` methods that hop to the thread
  pool and switch back, from starting the batch on the main thread until the
  last method resumes. Thread-pool latency and frame waits are included, so
  this is a wall-clock workflow, not a library slice.

The report records the Editor code optimization mode and the incremental GC
setting. Run the comparison with the Editor in Release code optimization, and
repeat it in Mono and IL2CPP players, because Debug mode disables JIT inlining
and register allocation and incremental GC changes the cost of reference
stores. Treat differences below about 5 ns/op as noise; identical code varied
by 25 percent between two Editor/Mono runs on 2026-09-25.

The first runs are recorded in the comparison guide with allocation values
unavailable: Unity 2022 Mono failed the `GC.GetAllocatedBytesForCurrentThread`
controls on both threads, and the first heap-delta fallback tried to disable
the collector, which the Editor rejects with `InvalidOperationException`.
Both runners now share the calibrated counter chain described in the
measurement contract. The heap delta is process-wide and has block
granularity, so read it as an average over many operations: the synchronous
cases and the 4,096-operation cohorts are meaningful, a 128-operation batch
can be off by tens of bytes per operation, and the worker-side value can
include allocations from other threads. The profiler counter is byte-exact
but also process-wide, and it is not used for the thread-switch round trip
because that slice spans frames. No run has produced allocation values with
this chain yet.

## Change note

- Split comparison assemblies and preserved the moved scripts' `.meta` GUIDs.
- Replaced the unlabelled 10,000-operation burst with matched, explicitly labelled
  steady-state and burst cohorts.
- Replaced the collector-disabling heap delta, which the Editor rejects, with a
  calibrated counter chain shared by both runners; the earlier separate
  Profiler pass never shipped in the package and its `-onityTaskAllocationsOnly`
  argument is gone from this README.
- Added completion checks, bounded frame waits, alternating execution order,
  raw samples and failure propagation from frame measurements.
- Added matched typed/untyped async-method cases for synchronous completion and
  one-frame suspension; existing primitive case names and indices are retained.
- Validated the two-process harness in Unity 2022.3.62f3 Editor/Mono with a
  65,568-byte positive and zero-byte empty control. Player/IL2CPP results remain
  unmeasured.
- Added the thread-switch harness with per-thread calibrated allocation
  controls; it has not been run yet.
