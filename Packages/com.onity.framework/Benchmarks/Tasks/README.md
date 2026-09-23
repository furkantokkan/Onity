# OnityTask comparison harness

## Setup

- Use a dedicated Unity `2022.3.62f3` benchmark host with the current Onity package.
- Install official UniTask `2.5.11`, tag commit
  `2e993ff18f28c931602a07292df0b0804eebef99`, using this pinned UPM URL:
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99`.
- Enable `ONITY_TASK_BENCHMARKS`. Leave `ONITY_BENCHMARKS` disabled unless the
  separate DI comparison dependencies are installed.
- New runtime/editor assemblies are `Onity.TaskBenchmarks` and
  `Onity.TaskBenchmarks.Editor`. No VContainer or Zenject assembly is referenced.
- Keep a clean benchmark scene and close profiling/diagnostic windows that add
  unrelated work. Record package lock revisions with published results.

Run `Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)`. The existing CLI
entry point remains
`Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine` and accepts
`-onityTaskBenchmarkOutput <absolute-json-path>`. Do not pass `-quit`; the menu
controller exits after writing the report or hitting its 15-minute timeout.
Verify the exact Editor version and host with the Unity CLI first. The installed
`unity run` 1.0.0-beta.3 adds `-quit` automatically, which closes the Editor
before a Play Mode benchmark can start. Invoke the pinned Unity 2022 Editor
directly for this entry point, without `-quit`:

```text
Unity.exe -batchmode -nographics -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTaskBenchmarkOutput <absolute-json-path> -logFile <absolute-log-path>
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
- Allocation deltas surround those same synchronous slices. Coroutine suspension,
  report construction, sample-array allocation and explicit full collections are
  outside them. Collections occur once before each measured sample, not inside a
  measured slice. Burst source allocations remain included.
- Mono's `GC.GetAllocatedBytesForCurrentThread` must detect a known 64 KiB
  allocation and report zero for an empty counter pair before allocation results
  are enabled. If calibration fails, bytes/op is `-1` and the report says
  unavailable. Unity 2022 IL2CPP skips this counter because the repository's DI
  harness documented crashes; IL2CPP allocation claims require separate evidence.
- Timer frequency/resolution, allocation controls, all timing/allocation samples,
  mean, median, range and standard deviation are retained in JSON. CSV and Markdown
  summarize the measurements. The report schema remains version 2; the original
  six primitive scenarios retain their names, indices and metric fields. Two
  synchronous async-method cases and eight frame-method cases are appended, for
  16 scenarios total. Consumers should identify cases by name and concurrency.

The expanded suite requires at least 3,096 rendered frames for its frame cases
(six workload/cohort pairs, each with four warmup and 512 measured frame waits).
The command-line runner has a 15-minute timeout. Run the benchmark in a dedicated,
responsive host because a heavily throttled Editor may still hit that limit.

Results compare these specific primitives, async-method slices and workloads.
An Editor result is not an IL2CPP player result. Timing noise and pool retention
differences must be considered before making a comparison claim. No new benchmark
results are bundled with this harness change.

## Experimental builder allocation attribution

The `benchmark/onitytask-value-attribution` branch adds a diagnostic mode for
the pooled typed builder candidate at source commit `8292166`. In a separate
Unity 2022.3.62f3 Editor process, run:

```text
Unity.exe -batchmode -nographics -profiler-enable -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTaskBuilderAttribution -onityTaskBenchmarkOutput <absolute-json-path> -logFile <absolute-log-path>
```

This mode measures one warm scheduling batch of 128 operations per case after
two completed warmup batches. It captures Profiler `GC.Alloc` callstacks for
native `NextFrame`, completed async methods, and suspended async methods, typed
and untyped where applicable, for OnityTask and pinned UniTask. It checks that
native and suspended awaiters are pending before a frame yield. A 64 KiB
positive control and an empty control must pass, and every case's callstack
bytes must equal its marker bytes. Waiting, resumption, and result consumption
occur outside each marker. Treat the callstack names as measured allocation
sites; they do not establish allocated object types or player/IL2CPP behavior.

The calibrated report at
`docs/assets/benchmarks/onitytask-builder-value-attribution-8292166-2026-09-23.json`
records 752 B/op for the candidate typed suspended method versus 72 B/op for
UniTask in the same marker. The candidate's 752 B/op consists of 552 B/op at
`UnitySynchronizationContext..ctor`, 72 B/op at `ExecutionContext.Capture`,
48 B/op at `UnitySynchronizationContext.CreateCopy`, and 80 B/op at the async
method call site. The first three sites are under the candidate's
`CreateNativeContinuation` path. This is one diagnostic allocation sample;
the separate eight-sample report carries the numerical benchmark result.

## Change note

- Split comparison assemblies and preserved the moved scripts' `.meta` GUIDs.
- Replaced the unlabelled 10,000-operation burst with matched, explicitly labelled
  steady-state and burst cohorts; added calibrated allocation measurements.
- Added completion checks, bounded frame waits, alternating execution order,
  raw samples and failure propagation from frame measurements.
- Added matched typed/untyped async-method cases for synchronous completion and
  one-frame suspension; existing primitive cases and the version 2 metric schema
  are retained. New results still require compilation and execution in Unity.
- Unity compilation and execution must be performed in the dedicated host after
  installing the pinned comparison package. Static review is not runtime proof.
