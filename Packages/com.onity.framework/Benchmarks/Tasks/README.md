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
  `-onityTaskBenchmarkSuite primary|threadswitch|threadpool|jobs|whenall|whenany|timing|eof|finite|channels|smoke|fullcycle` (default primary),
  `-onityTaskBenchmarkBuildPath <exe>`, and `-onityTaskBenchmarkOutput <json>`;
  it may be combined with `-quit`. The player itself accepts
  `-onityRunTaskBenchmark`, the same suite and output arguments, and writes
  the report with `isEditor` false and the backend it was built with.

### Worker-only thread-pool probes

Select `-onityTaskBenchmarkSuite threadpool` with the Player build entry point
above, or launch an already built Player:

```text
OnityTaskBenchmarkPlayer.exe -batchmode -nographics -onityRunTaskBenchmark -onityTaskBenchmarkSuite threadpool -onityTaskBenchmarkOutput <absolute-json-path> -logFile <absolute-log-path>
```

- This separate suite preserves the original seven `threadswitch` scenarios.
  It compares raw `SwitchToThreadPool().GetAwaiter().UnsafeOnCompleted(...)`,
  `RunOnThreadPool(Action, false)`, and `RunOnThreadPool(Func<int>, false)` with
  UniTask 2.5.11. Raw registration is context-free; Action/Func cases label Onity
  flow-off and flow-on separately. Only flow-off matches UniTask's nonflowing
  builder semantics; flow-on measures additional context preservation.
- Every cohort has 128 operations and preallocated callback/delegate slots.
  Two warmup cohorts per library precede eight samples, each containing two
  measured cohorts per library with alternating order. Every native task is
  consumed once on a worker, typed results must be 42, and callback faults or
  invalid completion counts reject the run. Main waits are bounded to 30 seconds
  per cohort and never pump Unity frames or the main-thread dispatcher.
- A shared harness gate keeps worker callbacks/delegates pending until all
  consumers register. Submission elapsed time ends before opening the gate;
  completion latency ends at the last worker consumption and includes the gate,
  thread-pool latency and harness overhead. Neither metric is a thread CPU-time
  counter or a compute workload. These probes never return work to the main
  thread and make no main-return/frame-latency comparison.
- Two real frames after every completed cohort are outside both measured slices
  and permit IL2CPP deferred returns to drain before the next cohort. This is an
  explicit reuse policy, not the primary suite's immediate-reuse workload.
- Submission bytes use only a calibrated main-thread per-thread allocation
  counter. Worker allocation is excluded. IL2CPP skips that counter; heap/frame
  fallbacks cannot isolate concurrent workers, so unavailable values are -1
  with the reason retained. Full cross-worker lifecycle allocation is always
  unmeasured, never inferred as zero from the submitting thread.
- The report captures build/runtime context evidence and per-case effective
  flow settings. Tracking and stack traces are off, runner retention is 128;
  original settings restore in finally before the completion callback. Run the
  correctness smoke suite separately and use repeated Release Player processes
  before interpreting scheduling differences.

### Jobs and Burst probes

Select `-onityTaskBenchmarkSuite jobs` with the same Player build entry point.
This suite keeps the default non-development Release build; no deep profiling
flags are needed. Use a host with the already installed Unity Burst and Collections
packages, and retain their package-lock versions with the results.

```text
Unity.exe -batchmode -nographics -quit -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine -onityTaskBenchmarkBackend IL2CPP -onityTaskBenchmarkSuite jobs -onityTaskBenchmarkBuildPath <release-exe> -onityTaskBenchmarkOutput <jobs-json> -logFile <build-log>
```

An existing Release Player accepts `-onityRunTaskBenchmark
-onityTaskBenchmarkSuite jobs -onityTaskBenchmarkOutput <jobs-json>`; pass
`-onityTaskBenchmarkBuildMetadata <release-exe>.build.json` to retain its sidecar.
The default report name is `onity-task-jobs-player-latest.json`. Errors fail the
Player exit code; an explicitly unavailable pending adapter panel is a successful
compute run with its reason retained.

- The compute panel applies one identical 32-round uint xorshift/add kernel to
  1,024 and 65,536 elements: a serial C# loop, plain `IJobParallelFor`, and Burst
  `IJobParallelFor` with batch size 64. Three warmups per variant precede eight
  samples of two runs each, rotating variant order. Serial timing covers the
  loop; job timing covers Schedule through Complete. Every execution checks all
  output elements and four fixed golden values outside timing.
- A `BurstDiscard` ref-bool sentinel proves the expected execution path on every
  run, including adapters. Wrong sentinels or output fail the run. The report
  records Burst enabled state and assembly identity, not a guessed package
  version. IL2CPP compiles serial/plain C# to native code too; these labels do
  not imply a managed-versus-native comparison.
- The adapter panel uses the same 65,536-element Burst job, one outstanding at
  a time, comparing `JobHandle.AsOnityTask()` with UniTask 2.5.11
  `ToUniTask(PlayerLoopTiming.Update)`. Both the handle before registration and
  returned awaiter must be incomplete. Two eligible warmups and sixteen eligible
  measured cohorts per library are required, with alternating library order.
  At most three extra attempts per library are allowed across the entire panel.
  A fourth exclusion exhausts replacements; the paired panel becomes unavailable
  and retains actual exclusions and all successful raw observations. Already
  completed work is safely completed/consumed, never forced to stay pending.
- Registration elapsed time and calibrated main-thread per-thread bytes are
  separate from Schedule-to-native-consumption wall time and frame indices.
  Cached native callbacks consume once on the main thread. Missing allocation
  evidence produces -1 and its reason; cross-worker lifecycle allocation is
  always unmeasured. Onity's MonoBehaviour Update and UniTask's injected Update
  occupy different PlayerLoop positions, so wall latency is not scheduler CPU
  superiority. Two real drain frames are outside measurements; pending waits
  fail after 30 seconds.
- Persistent NativeArrays are reused; outstanding jobs Complete before disposal
  even on failure. Context flow is enabled, tracking and stack traces are disabled,
  and runner retention is 128. The report captures effective environment/build
  evidence before measurement, and original flags restore in finally.

### Pending typed WhenAll construction

Select `-onityTaskBenchmarkSuite whenall` with the Player build entry point above.
It uses the default non-development Release build for either Mono or IL2CPP.
The default JSON is `onity-task-whenall-player-latest.json`.

```text
Unity.exe -batchmode -nographics -quit -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine -onityTaskBenchmarkBackend IL2CPP -onityTaskBenchmarkSuite whenall -onityTaskBenchmarkBuildPath <release-exe> -onityTaskBenchmarkOutput <whenall-json> -logFile <build-log>
```

- Compare 2, 8 and 16 unique pending built-in `OnityTaskCompletionSource<int>`
  inputs per aggregate, with 128 aggregates per cohort. Sources, input arrays and
  output holders are fresh and prepared before each measured slice. No input is
  reused between aggregates or paths. Three warmup cohorts per path precede eight
  samples of two runs, alternating path order. The cohort fits the native
  coordinator pool's capacity of 256.
- The new path calls `OnityTask.WhenAll(inputs)`. The exact old path allocates
  `Task<int>[]`, calls each input's `AsTask()`, then uses
  `OnityTask<int[]>.FromTask(OnityAsync.WhenAll<int>(taskArray))`, retaining the
  original tracker label. Tracking/stack traces are off for both; context flow
  is on, runner retention is 128, and original flags restore in finally.
- Stopwatch and allocation slices cover only the 128 aggregate factory calls.
  Counter readings end before any completion. Inputs complete in reverse order;
  every aggregate and every ordered result is checked outside timing. Original
  Task bridges can dispatch framework completion on workers, so all outputs have
  a bounded 30-second drain before the next cohort. Two additional real drain
  frames are outside measurement. Failure cleanup completes every prepared source,
  observes outputs, and attaches fault observers if the bounded drain times out.
- Allocation uses the shared calibrated per-thread, profiler-counter, then heap
  chain on a synchronous single-thread construction slice. Reports include the
  positive/empty controls, rejected candidates, eight raw sample values and valid
  sample counts. Collection-interrupted or negative-delta samples are invalid;
  no calibrated/valid evidence means unavailable bytes (-1), with the reason.
  Each raw sample sums two cohorts; mean/median milliseconds cover that sample,
  while nanoseconds/bytes per aggregate divide by 256 aggregate constructions.
- **Construction allocation only:** the new retained result array allocates
  during construction, while the old path can allocate its result array during
  completion. These values do not measure or imply full-lifecycle allocation.
  Private source fields are not reflected in Player timing; independent tests
  supply structural bridge evidence. Build sidecar/environment metadata records
  the backend and native compiler settings.

### Array WhenAny synthetic composition

Select `-onityTaskBenchmarkSuite whenany` with the same Player build entry point.
This suite requires a non-development Release Player, with either Mono or IL2CPP.
The default JSON is `onity-task-whenany-player-latest.json`.

```text
Unity.exe -batchmode -nographics -quit -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine -onityTaskBenchmarkBackend IL2CPP -onityTaskBenchmarkSuite whenany -onityTaskBenchmarkBuildPath <release-exe> -onityTaskBenchmarkOutput <whenany-json> -logFile <build-log>
```

- Twelve rows compare typed and untyped array calls at 2, 16 and 32 inputs with
  pinned UniTask 2.5.11. Each cohort prepares 128 independent groups of fresh
  public completion sources. Source objects, input arrays, output holders and
  winner/value storage allocate before the slice. Explicit array arguments select
  Onity's array overload rather than its existing two-input overload.
- **Synthetic composition slice:** construct all 128 outputs, complete every
  producer synchronously in reverse order, then consume each output exactly once
  and store its index/value. All three phases are inside timing and allocation
  readings. Every stored winner must be the last input index; every typed result
  must match that group's expected value. Assertions are outside the slice.
  The measurement includes public source completion and loser observation; it is
  not isolated scheduler CPU or the allocation of caller-prepared inputs.
- Three warmup cohorts per library precede eight samples of two cohorts each,
  alternating library order. Two real drain frames are outside measurements.
  Cleanup settles all prepared producers and consumes each constructed output at
  most once. Unexpected pending outputs retain one fault observer and fail the
  run. Original Onity flags restore in finally. Tracking/stack traces are off,
  context flow is on and runner retention is 128. UniTask's pinned tracker calls
  are conditional on UNITY_EDITOR and absent in Player builds.
- Reports retain the calibrated allocation-counter chain, positive/empty controls,
  rejected candidates, eight raw sample arrays and valid sample counts. Invalid
  collection/delta samples are excluded; missing evidence is -1 with a reason.
  Mean/median milliseconds cover two cohorts, while per-group values divide by
  256 compositions. Build/environment metadata includes native compiler settings.
- This success-only public shareable-source workload does not establish native
  single-consumer, cancellation or general performance superiority. Onity retains
  sources for up to 16 inputs; the 32-input path is unpooled. Run each Release
  Player twice and retain raw reports before interpreting differences.

### Injected Update allocation bracket

Select `-onityTaskBenchmarkSuite timing` in a non-development Release Player.
This checks the warmed, no-token `Yield(OnityPlayerLoopTiming.Update)` path;
it is not a UniTask speed comparison or coverage of every timing/cancellation
variant.

- Cached benchmark nodes bracket the actual Onity Update node. Before the
  drain, schedule 128 waits and register preallocated native callbacks. The
  callbacks consume each result once during the real drain. End readings in
  the adjacent After node, before validation or report construction.
- Three warmup cohorts precede eight samples of two cohorts each. Scheduling,
  registration, queue draining, consumption and pool return are inside the
  measured bracket. Holder/delegate construction and loop installation are
  outside it; there is no Task bridge or worker in the slice.
- Empty and retained 64 KiB positive controls use the same bracket layout,
  supplementing the shared counter's calibration. Failed controls or samples
  leave allocation unverified with an explicit reason. With HeapDelta, zero
  means **no measured heap growth**, not proof of zero GC allocation.
- Missing After callbacks and teardown close the measurement window. Cleanup
  removes only benchmark markers from the current loop, preserving foreign
  nodes. Run each backend twice and retain raw samples/environment metadata.

### Rendering end-of-frame verification

Select `-onityTaskBenchmarkSuite eof` with the Player build entry point. This
functional suite is verified on Mono/IL2CPP; it does not measure speed or
allocation. Build once for each backend. For example:

```text
unity run <benchmark-host> --editor-path <Unity.exe> --timeout 1800 --non-interactive --json -- -nographics -releaseCodeOptimization -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine -onityTaskBenchmarkBackend Mono -onityTaskBenchmarkSuite eof -onityTaskBenchmarkBuildPath <release-exe> -onityTaskBenchmarkOutput <eof-json>
```

- The build Editor may be headless. Only the `eof` Player launch omits both
  `-batchmode` and `-nographics` and uses a 320x240 window. It requires a real
  graphics device and a non-development Player. The launcher passes the
  expected backend and GUID from the successful BuildReport; the Player and
  report validator reject missing/mismatched identities.
- Six groups check alternating-color pixels after the owned camera renders,
  Update/LateUpdate/EOF ordering, callback-created later waits, stopped-pump
  worker cancellation, same-source reuse, manual host replacement and global
  close/failed-repair cleanup. Reflection or graphics failures fail the run.
  Each case has a 10-second wall deadline; the suite has 60 seconds. Ordinary
  Update drives these deadlines independently of the rendering coroutine.
- The default report is `onity-task-eof-player-latest.json`. After successful
  graphics verification, the launcher runs ordinary headless smoke from the
  **same binary**, writing a separate `.smoke.json` report, log and trace.
  Existing fresh-report, entry-handshake, exit-code and external watchdog
  checks apply. Direct graphics launches must also supply
  `-onityTaskExpectedBackend` and `-onityTaskExpectedBuildGuid` from that build.
- The suite restores its active render target, retires its private host and
  releases owned camera/texture resources. A controlled stopped coroutine
  proves cancellation during a stall, not actual Editor Scene-view behavior.
  Headless rejection is checked separately and never counted as render proof.

### Finite async-stream iteration

Select `-onityTaskBenchmarkSuite finite` with the same Release Player build
entry point. The default report is `onity-async-enumerable-player-latest.json`;
the launcher then runs the 35-case smoke suite from the same binary and writes
its separate `.smoke.json` report.

- Measure `Range` and a `Select/Where/Take` pipeline. Each fresh enumerator is
  primed with one successful move and Current read **before** measurement so
  lazy upstream construction is excluded. Priming values are -1 and -4.
- Each measured bracket includes 4,096 successful moves, GetResult and Current
  reads, one final false, and automatic Take cleanup. Explicit final disposal,
  description creation and priming remain outside. Checksums are 8,386,560 and
  33,546,240. Three warmups precede two runs of eight raw windows per scenario
  in one Player process; these are not independent process repetitions.
- Per-scenario empty and retained 64 KiB controls must pass. Frame counters are
  excluded from this synchronous slice. GC mode, context flow and tracker flags
  are restored. The build gate checks work/priming counts, controls, samples and
  the fresh Release backend/build GUID.
- HeapDelta zero means no measured retained-heap increase in this bracket.
  A zero allocated-byte claim requires a supported calibrated per-thread counter.
  Descriptions, linked tokens, pending moves and array consumers are outside this
  probe. Timings include traversal accounting and are not a UniTask comparison.

### Bounded channel buffered operations

Select `-onityTaskBenchmarkSuite channels` with the Release Player entry point.
The launcher also runs the 35-case smoke suite from the same fresh binary and
writes a separate `.smoke.json` report.

- Capacities 1 and 128 each run 4,096 fill/drain cycles per window: respectively
  4,096 and 524,288 accepted items, with the same number of writes and reads.
  Values follow acceptance order, and validation checks every value, operation
  count and the long checksum outside the allocation bracket.
- Construction, a full-ring priming fill/drain, sample storage and validation
  remain outside measurement. The bracket contains only immediate TryWrite /
  TryRead calls, loops and scalar accounting. There are no pending waiters.
- Three warmups precede two internal runs of eight raw windows per capacity
  in one Player process. These are not independent process repetitions.
- Empty and retained 64 KiB controls use the same calibrated counter. Frame
  counters are excluded. GC mode, context flow and tracking are restored;
  the build gate validates priming, counts, checksums, controls, samples,
  backend and build GUID before accepting both reports.
- HeapDelta zero does not prove zero allocated bytes. Pending waits,
  cancellation registrations, ReadAll descriptions and unbounded growth are
  outside this slice. Timings include bookkeeping and have no UniTask baseline.

### Player startup and smoke verification

- Benchmark-only runtime hooks persist early initialization, BeforeSceneLoad,
  AfterSceneLoad, argument detection, and benchmark entry to `<report>.startup.log`
  (extension replaced). The launcher clears that trace before each process.
  `-onityTaskBenchmarkStartupTrace <path>` selects a different trace for direct runs.
- Missing benchmark entry fails within 60 seconds. After entry, measurement has
  its own 15-minute limit. The launcher stops reading the trace during measurement
  and includes the final startup evidence on failure. Exit zero still requires a
  handshake and a report written after this process launched.
- Select `smoke` in a separate Player process before timing. Onity 0.4.0's
  35 cases cover
  synchronous completion, critical/safe typed and untyped suspension, native and
  bridge consumer exclusivity, stale tokens, bridge recycling, real-frame pool
  return, consume/re-rent during MoveNext, held-worker IL2CPP deferral, and
  execution-context flow, suppression, isolation, and synchronization context.
  They also cover thread-pool work, typed/untyped array WhenAny, external
  cancellation, cancellation-result suppression, injected phase/frame ordering
  and paused/reentrant cancellation, plus legacy runner retirement, replacement,
  token ownership and continued underlying Unity operation completion. Timeout
  checks add typed/untyped result forms, producer fault/cancellation identity,
  scaled/unscaled clocks and callback-created timer ordering. A separate EOF
  guard checks headless rejection before pre-cancellation. Finite-stream groups
  add real await-foreach cleanup, covariance, finite operators and pending native,
  preserved and Task-backed move/disposal ownership. Update/BCL adapter groups
  add idle/reentrant Update behavior, worker disposal, real compiler async
  iterators and reusable ValueTask consumption on both Player backends.
  Channel groups add bounded FIFO/backpressure, canceled-writer promotion,
  faulted-OCE closure, ReadAll token/lease ownership and actual await-foreach
  break/throw cleanup without closing the shared channel.
  Awaitable-operator groups cover sequential SelectAwait/WhereAwait/ForEachAsync,
  long filters, actual fault/cancellation status, suspended delegates and both
  delegate/upstream cleanup completion orders.
  Historical reports below
  retain the case count of their measured source revision.
  Failure identifies the case in JSON and the persistent trace. No reflective
  dispatcher drain or benchmark timing occurs in this suite.
- Primary runs save and restore tracking, stack traces, flow, and runner capacity.
  Effective baseline settings are tracker off, stack traces off, flow on, capacity
  128; flow-off cases remain separately labeled. Schema 6 records these settings,
  build GUID/development state, runtime internal-pair/public-fallback evidence,
  allocation selection and rejected candidates. The launcher writes a
  `<exe>.build.json` sidecar containing optimization, stripping, and registered
  package IDs and passes `-onityTaskBenchmarkBuildMetadata <path>` to the Player.
  Direct runs without a sidecar explicitly report unknown build fields.
- Optional `-onityTaskBenchmarkDrainBetweenBatches` adds two real frames after
  every consumed frame cohort, before another scheduling slice (including warmup).
  The launcher forwards it; JSON/Markdown report the control and drain frame count.
  It distinguishes immediate re-rent costs from fully recycled pools on IL2CPP.
  This is a separate experiment, never a replacement for the default alternating
  cohort baseline. Scheduling/GetResult timing slices themselves are unchanged.

## Measurement contract

### Full lifecycle diagnostic profile

`fullcycle` is a separate Development IL2CPP profile build with
`BuildOptions.Development | BuildOptions.EnableDeepProfilingSupport`. It runs
only bounded typed async helpers awaiting `NextFrame`, returning and checking `42` for
every operation. The launcher adds `-deepprofiling`. Do not enable deep profiling
on the primary million-iteration loops or compare diagnostic times with Release
baseline timings.
The diagnostic build appends `/optimize+` through Standalone additional compiler
arguments so managed state machines remain structs despite Development defaults.
Original arguments restore in `finally`; Release build arguments are untouched.
The build sidecar records `managedCompilerOptimization` explicitly.

Build and capture one configuration per process:

```text
Unity.exe -batchmode -nographics -quit -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine -onityTaskBenchmarkBackend IL2CPP -onityTaskBenchmarkSuite fullcycle -onityTaskProfileConcurrency 128 -onityTaskProfileFlow on -onityTaskProfileReuse immediate -onityTaskBenchmarkBuildPath <diagnostic-exe> -onityTaskBenchmarkOutput <case-json> -logFile <build-log>
```

The Player emits `<case>.raw` beside its JSON (extension replaced), plus the
existing startup and Player logs. A built diagnostic executable may be reused
for the other configurations:

```text
<diagnostic-exe> -batchmode -nographics -deepprofiling -onityRunTaskBenchmark -onityTaskBenchmarkSuite fullcycle -onityTaskProfileConcurrency 4096 -onityTaskProfileFlow off -onityTaskProfileReuse drain -onityTaskBenchmarkOutput <case-json> -onityTaskBenchmarkBuildMetadata <diagnostic-exe>.build.json -logFile <player-log>
```

Required configurations are the eight combinations of concurrency `128|4096`,
flow `on|off`, and reuse `immediate|drain`. Use unique JSON/raw paths for each
process. Every process captures Onity and UniTask in separate timestamp windows.
Each library runs two warmup batches with profiling disabled, then exactly two
captured batches using the same async helper type. Immediate reuse consumes and
re-rents in the same coroutine step; drain inserts two real frames between the
batches for both libraries. Capture remains enabled for two real terminal frames
after the last consumption to include deferred returns. The profiler buffer is
bounded to 512 MiB; missing capture markers/operations cause export failure.

Export in a headless Editor using the verified public Unity 2022 profiler APIs:

```text
Unity.exe -batchmode -nographics -quit -projectPath <benchmark-host> -executeMethod Onity.Editor.Benchmarks.OnityTaskFullCycleProfileExporter.ExportFromCommandLine -onityTaskProfileInput <case-json> -onityTaskProfileExport <attribution-json> -logFile <export-log>
```

To export several completed captures in one Editor session, replace the two
file flags with `-onityTaskProfileInputDirectory <capture-directory>` and
`-onityTaskProfileExportDirectory <output-directory>`. This selects only
`fullcycle-*.json` in that directory, excludes `*-attribution.json`, and validates
each selected Player report and raw capture. A focused subset is allowed; it
does not waive the eight-configuration evidence requirement. Single-file mode
is preserved and cannot be combined with directory mode.

The exporter loads the raw file with `ProfilerDriver.LoadProfile` and reads
`GetRawFrameDataView`. Begin/End marker timestamps define windows; Player frame
counts are evidence, never raw-frame index assumptions. Schedule and Consume
sample scopes never cross a yield; Resumed stamps validate all operations.
Exports retain full sample paths, thread and nearest Schedule/Consume phase,
with other work labeled Lifecycle. Per-path inclusive times are descriptive and
must not be added across parents/children. Exclusive self times subtract only
direct children inside the clipped window. `GC.Alloc` metadata is counted once
per event, never re-added to parent samples. Missing metadata or absent allocation
samples produces `gcAllocationBytes: -1` with explicit counts, not an inferred
zero. Preserve raw files for call-stack inspection and check all eight exports
before selecting a runtime optimization.
Sample durations retain instrumentation, waits and idle paths; they are not
aggregate active CPU time. Inspect the relevant runtime paths and phase boundaries.
The exporter also requires recognizable managed runner/source/pool detail for
Onity and managed builder/source detail for UniTask, beyond the custom phase
markers. It records `deepRuntimeSamplesPresent` and the matching sample count;
missing detail fails export instead of suggesting an absent cost is zero.
These values are instrumented sample durations, with wait/idle paths retained;
they are not aggregate active CPU time across all threads.

### Primary suite

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
  summarize the measurements. The primary report schema is version 6; the original
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
because that slice spans frames. The 2026-09-27 primary Player runs produced
calibrated HeapDelta values with eight valid samples per metric; this does not
verify thread-switch allocation values, which were not rerun in that task.

## Player verification - 2026-09-27

- Unity `2022.3.62f2` Release, Windows x64: Mono and IL2CPP each passed the
  13-case smoke suite with the internal execution-context pair active.
- Two primary processes per backend completed all 24 cases, tracking off and
  capacity 128. At concurrency 128, async scheduling Onity/UniTask ratios were
  1.41-1.53 with flow on and 1.23-1.26 with flow off for Mono, and 1.77-1.94
  and 1.70-1.74 for IL2CPP. The performance target remains unmet.
- HeapDelta calibrated at 69,632/0 bytes and retained eight valid samples per
  metric. The separate drain control removed the measured 128-operation IL2CPP
  heap delta; the burst remained above the pool caps. Measured zero is not
  proof of zero allocation.
- Full EditMode/PlayMode suites passed 668/668 and 41/41 in both optimizations
  after the startup fix. Final benchmark-only additions were rebuilt and smoke
  verified in both Players. Separate optimized Development IL2CPP profiling
  passed all eight configurations/16 windows, including every deferred runner
  return. It supplies attribution, not replacement Release timings.
- Raw samples, the separate drain control and detailed limits are in the
  [verification report](../../../../docs/assets/benchmarks/onitytask-player-verification-2026-09-27.md).

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
- Earlier validation used Unity 2022.3.62f3 Editor/Mono with a 65,568-byte
  positive and zero-byte empty control. The 2026-09-27 Player verification above
  records the current f2 backend results and their different counter limits.
- Added the thread-switch harness with per-thread calibrated allocation
  controls; historical Editor timing runs are in the comparison guide. Its
  current shared counter chain was not verified by the primary Player runs.
- Rooted benchmark initialization with `AlwaysLinkAssembly`, added startup
  deadlines and semantic smoke coverage, and recorded effective build/runtime
  metadata with allocation-counter rejection reasons in schema 6.
- Added bounded full-cycle capture and headless batch export. Managed helper
  value types were verified after the diagnostic `/optimize+` override; the
  final diagnostic Player also passed 13/13 smoke cases. After API restoration,
  Unity retained an empty Standalone compiler-argument map entry; verification
  removed that serialization-only entry and confirmed the original file hash.
