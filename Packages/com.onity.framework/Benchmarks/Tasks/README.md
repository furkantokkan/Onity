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
`-onityTaskBenchmarkOutput <absolute-json-path>`. Run timing first, then add
allocation samples to the same JSON in a **separate Unity process** with
`-profiler-enable -onityTaskAllocationsOnly`. Do not pass `-quit`; the menu
controller exits after writing the report or hitting its 15-minute timeout.
Verify the exact Editor version and host with the Unity CLI first. The installed
`unity run` 1.0.0-beta.3 adds `-quit` automatically, which closes the Editor
before a Play Mode benchmark can start. Invoke the pinned Unity 2022 Editor
directly for this entry point, without `-quit`:

```text
Unity.exe -batchmode -nographics -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTaskBenchmarkOutput <absolute-json-path> -logFile <absolute-log-path>
Unity.exe -batchmode -nographics -profiler-enable -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTaskBenchmarkOutput <same-absolute-json-path> -onityTaskAllocationsOnly -logFile <separate-absolute-log-path>
```

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
- The Editor-only allocation pass measures the same synchronous slices with a
  dedicated Unity Profiler marker and `GC.Alloc` sample byte metadata. It runs
  after the same synchronous and completed frame-cohort warmups, using eight
  samples per scenario. The four synchronous cases use 1,024 operations per
  profiled sample; frame scheduling and consumption each use one cohort per
  sample. Profiling is off during the separate timing pass, so timing values
  do not include profiler overhead. Coroutine suspension, report construction,
  sample arrays and explicit full collections stay outside the marked slices.
  Burst source allocations remain included.
- The profiler must read at least 65,536 bytes for a known 64 KiB allocation
  and zero bytes for an empty control before publishing bytes/op. If either
  control or any of the 32 metrics fails, **all** allocation values remain `-1`
  and the report says unavailable. Unity 2022 IL2CPP allocation claims require
  separate player evidence.
- Timer frequency/resolution, allocation controls, all timing/allocation samples,
  mean, median, range and standard deviation are retained in JSON. CSV and Markdown
  summarize the measurements. The report schema is version 4; the original
  six primitive scenarios retain their names, indices and metric fields. Two
  synchronous async-method cases and eight frame-method cases follow them, and
  schema 4 appends the same eight frame-method cases measured with
  `OnityTask.FlowExecutionContext` off, suffixed ` (flow off)`, for 24 scenarios
  total. The unsuffixed frame-method cases force the setting on, so they stay
  comparable with reports from the earlier .NET-builder implementation, and
  the run restores the caller's setting afterwards. The report records the
  setting's default at run start (`flowExecutionContextDefault`) and whether
  the task tracker was enabled (`taskTrackerEnabled`). Consumers should
  identify cases by name and concurrency.

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
controls on both threads. When that counter fails its controls, both runners
now fall back to a `GC.GetTotalMemory` delta taken with
`GarbageCollector.GCMode` disabled inside each measured slice, calibrated with
the same 64 KiB and empty controls; the report's `allocationCounterKind` says
which counter produced the values (`PerThread`, `HeapDelta`, or `None`). The
heap delta is process-wide and has block granularity, so read it as an
average over many operations: the synchronous cases and the 4,096-operation
cohorts are meaningful, a 128-operation batch can be off by tens of bytes per
operation, and the worker-side value can include allocations from other
threads. The separate calibrated Profiler pass from the isolated host remains
the reference.

## Change note

- Split comparison assemblies and preserved the moved scripts' `.meta` GUIDs.
- Replaced the unlabelled 10,000-operation burst with matched, explicitly labelled
  steady-state and burst cohorts; added a separate calibrated profiler allocation pass.
- Added completion checks, bounded frame waits, alternating execution order,
  raw samples and failure propagation from frame measurements.
- Added matched typed/untyped async-method cases for synchronous completion and
  one-frame suspension; existing primitive case names and indices are retained.
- Validated the two-process harness in Unity 2022.3.62f3 Editor/Mono with a
  65,568-byte positive and zero-byte empty control. Player/IL2CPP results remain
  unmeasured.
- Added the thread-switch harness with per-thread calibrated allocation
  controls; it has not been run yet.
