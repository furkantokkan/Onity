# Onity Benchmarks

This folder contains benchmark tooling for comparing:
- `Onity`
- `VContainer`
- `Zenject`
- `OnityTask` vs `UniTask` async hot-path primitives
- `Onity.Reactive` vs `R3` and `UniRx` (`Reactive/`, define `ONITY_REACTIVE_BENCHMARKS`; see its README)
- `Onity.Messaging` vs `MessagePipe` (`Messaging/`, define `ONITY_MESSAGING_BENCHMARKS`; see its README)

## 1. Run DI Benchmarks in Unity

Benchmark comparison assemblies reference VContainer and Zenject. To compile and
run them in a package-only project, install those comparison packages and add
`ONITY_BENCHMARKS` to **Project Settings > Player > Scripting Define Symbols**.
The OnityTask comparison is independently enabled with `ONITY_TASK_BENCHMARKS`.
It requires only UniTask as a comparison dependency; VContainer and Zenject are
not required. DI benchmarks no longer reference UniTask. Both comparison suites
remain optional and outside Onity runtime dependencies.

From the Unity menu:
- `Onity/Benchmarks/Run DI Benchmarks (Editor)`
- `Onity/Benchmarks/Build and Run DI Benchmarks (IL2CPP Player)`
- `Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)`

Command line (batchmode) entry point:
- `Onity.Editor.Benchmarks.OnityDiBenchmarkRunner.RunBenchmarksFromCommandLine`
- `Onity.Editor.Benchmarks.OnityDiBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine`
- `Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine`

Example:

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "<development checkout>" `
  -executeMethod Onity.Editor.Benchmarks.OnityDiBenchmarkRunner.RunBenchmarksFromCommandLine `
  -onityBenchmarkOutputDirectory "<development checkout>\Temp\di-benchmark-results" `
  -logFile "<development checkout>\Temp\onity-di-benchmark.log"
```

Precondition:
- Close all other Unity instances that currently have this same project open.

The Editor DI runner accepts `-onityBenchmarkOutputDirectory <absolute-directory>`
to write its reports outside the package. Without the argument, it writes to
`Packages/com.onity.framework/Benchmarks/Results`. It validates the gross managed
allocation counter with a positive control. If neither the process-wide nor
current-thread counter passes, it publishes timing results with allocation values
marked unavailable.
In JSON, `allocationMeasured: false` and `allocationCounter: "Unavailable"`
mean the allocation fields' `-1` values are sentinels, not byte counts. CSV
leaves those cells blank and Markdown prints `N/A`.

For a focused Editor timing pass, add `-onityBenchmarkHotPathsOnly`. It runs the
existing baked Onity singleton, transient, combined, and complex-graph scenarios
with one warmed container per scenario, 16 interleaved samples, and per-sample
GC collection counts. Its reports use separate `di-benchmark-hot-paths-latest.*`
names and state their limited scope. Do not
combine this timing option with the allocation-only options. Run allocation
checks separately; the focused timing report marks allocation unavailable.

Pass `-onityBenchmarkEventsOnly` to the same command-line entry point for a
separate `Unity.Profiling` `GC.Alloc` event-count run. It uses the current-thread
recorder, checks an empty control and 128 known array allocations, then measures
3 samples of 64 operations per case after 512 warmup operations. Add
`-onityBenchmarkOutputDirectory <absolute-directory>` to keep its
`di-allocation-events-latest.json` report outside `Assets`. Event counts
measure how many managed allocation events occur, not their byte sizes. This
pass is separate so profiler recording cannot affect the timing measurements.

Pass `-onityBenchmarkBytesOnly` for a separate raw Profiler byte run. Omit
`-quit` from this batchmode command: the runner reads completed Editor frames
and exits Unity itself. It validates an empty marker plus 1 MiB and 2 MiB
known-allocation controls before recording 3 samples of 64 operations per
case. `RawFrameDataView` reads the `GC.Alloc` sample's byte metadata only
inside each measured marker. The output directory option also applies to its
`di-allocation-bytes-latest.json` report. This profiler pass is not used
for timing.

Output files are generated at:
- `Benchmarks/Results/di-benchmark-latest.json`
- `Benchmarks/Results/di-benchmark-latest.csv`
- `Benchmarks/Results/di-benchmark-latest.md`
- `Benchmarks/Results/di-allocation-events-latest.json`
- `Benchmarks/Results/di-allocation-bytes-latest.json`
- `Benchmarks/Results/di-benchmark-summary.md`
- `Benchmarks/Results/di-benchmark-player-latest.json`
- `Benchmarks/Results/di-benchmark-player-latest.csv`
- `Benchmarks/Results/di-benchmark-player-latest.md`
- `Benchmarks/Results/onity-task-benchmark-latest.json`
- `Benchmarks/Results/onity-task-benchmark-latest.csv`
- `Benchmarks/Results/onity-task-benchmark-latest.md`

On GitHub `main`, the package copy is under
`Packages/com.onity.framework/Benchmarks`. On the `upm` branch, these paths are
at the package root.

The player build runner accepts `-onityBenchmarkOutput <absolute-path>`, which is
useful when writing IL2CPP output outside `Packages` to avoid importing benchmark
artifacts during the build session.
The built Release player also accepts `-onityRunDiBenchmark
-onityBenchmarkHotPathsOnly -onityBenchmarkOutput <absolute-json-path>` for a
separate Onity Baked singleton/transient/combined/complex timing report. It
uses one warmed container per scenario, 16 interleaved samples, and records
GC collection counts. Run the same player executable repeatedly for baseline
checks. This focused report does not measure allocation bytes or competitors.
Pass `-onityCaptureDiAllocationBytes` to the same player build entry point for a
separate IL2CPP Development build. It records a `.raw` player profile next to
the output JSON, then reads `GC.Alloc` bytes within each measured marker in the
Editor. Empty, 1 MiB, and 2 MiB controls and all expected case markers must
pass before the JSON is written. This capture does not provide release timing.
The build entry point forwards `-onityBenchmarkIterations`, `-onityBenchmarkSamples`,
`-onityBenchmarkWarmup`, `-onityBenchmarkScenario <BenchmarkScenario>` and
`-onityBenchmarkHotPathsOnly` to the player; without them the full seven-scenario
protocol runs with its defaults.

Use `-onityBenchmarkScenario <name>` to run one player scenario for a focused
high-sample gate. Valid names are `ResolveSingleton`, `ResolveTransient`,
`ResolveCombined`, `ResolveComplex`, and `PrepareAndRegisterComplex`. To measure
only the singleton scenario with 1000 samples, append
`-onityBenchmarkScenario ResolveSingleton -onityBenchmarkSamples 1000`. Omit
the scenario argument to run the full suite.

Scenarios:
- Resolve (Singleton)
- Resolve (Transient)
- Resolve (Combined)
- Resolve (Complex)
- Prepare & Register (Complex)
- Resolve (Keyed Singleton): each container resolves the same string-identified singleton.
- Resolve (Scoped Singleton): each container resolves a warmed singleton from a child scope. Onity and VContainer use their native scoped lifetimes; Zenject registers an equivalent child-local singleton because it has no native scoped lifetime. Scope creation, registration, first activation, and disposal are excluded from the measured loop.

Profiler note:
- For Play Mode profiler evidence, enter Play Mode, keep the Profiler recording,
  then run `Onity/Benchmarks/Run DI Benchmarks (Play Mode Profiler)`.
- The menu queues the benchmark for the next Play Mode frame. Select the large
  benchmark spike frame before searching `Raw Hierarchy` for
  `Onity.DI.Benchmark`; the search only filters the selected frame.
- The Play Mode profiler path uses a reduced one-sample, 10,000-iteration pass so
  it is suitable for marker evidence, not for publishing final timing claims.

## 2. Current Advanced DI Evidence

The 2026-09-23 post-change run measured all seven scenarios in Editor/Mono and
Windows IL2CPP. Onity baked was faster than VContainer and Zenject in all seven
timing cases; keyed and scoped singleton resolves recorded 0 B/op in both raw
Profiler allocation runs. See the [current summary and raw reports](../../../docs/benchmarks/advanced-di-summary-2026-09-23.md).

The figures below are earlier five-scenario snapshots retained for historical
comparison.

### Earlier five-scenario results

The checked-in `Benchmarks/Results/di-benchmark-latest.*` files and
`di-benchmark-summary.md` are earlier May 2026 snapshots. The September
Editor/Mono raw report below lives under `docs/benchmarks`, outside Unity
`Assets`; it has not replaced those historical files.

Earlier Editor/Mono run: `2026-09-23T14:23:06Z`, Unity 2022.3.62f3, Windows
Editor/Mono, 512 warmup iterations, 8 measured samples of 10,000 operations,
arithmetic mean. [Raw JSON](../../../docs/benchmarks/2026-09-23-editor-mono.json)
records the per-case spread. The Editor's process-wide allocation API was
unavailable, and its current-thread counter reported 0 B for a 1 MiB positive
control. Inline allocation columns in the timing report are therefore N/A;
the separate raw Profiler report below measures bytes.

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~76 ns | ~216 ns | ~269 ns | ~2,641 ns | ~+72% |
| Resolve Transient | ~503 ns | ~751 ns | ~1,723 ns | ~13,179 ns | ~+71% |
| Resolve Combined | ~545 ns | ~817 ns | ~1,681 ns | ~13,784 ns | ~+68% |
| Resolve Complex (6-level) | ~12,663 ns | ~12,294 ns | ~39,548 ns | ~267,936 ns | ~+68% |
| Prepare & Register Complex | ~37,759 ns | ~27,296 ns | ~139,177 ns | ~178,444 ns | ~+73% |

The separate raw Profiler allocation run at `2026-09-23T14:21:25Z`
recorded 0 B for an empty marker, 1,053,728 B for 128 arrays totaling 1 MiB
of payload, and 2,102,304 B when the payload doubled. It measured 3 samples
of 64 operations per case after 512 warmups; every sample in each case had
the same byte and event count. [Raw byte JSON](../../../docs/benchmarks/2026-09-23-editor-mono-allocation-bytes.json).

| Scenario | Onity Baked (B / events per op) | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Resolve Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Transient | 16 / 1 | 16 / 1 | 272 / 4 |
| Resolve Combined | 16 / 1 | 16 / 1 | 272 / 4 |
| Resolve Complex (6-level) | 384 / 24 | 384 / 24 | 5,780 / 96 |
| Prepare & Register Complex | 9,852 / 109 | 15,488 / 238 | 23,166 / 419 |

Onity matched VContainer's allocation bytes and event counts in every resolve
case and used fewer in prepare/register. It used fewer bytes and events than
Zenject in every case that allocated. The
[before-tuning byte report](../../../docs/benchmarks/2026-09-23-editor-mono-allocation-bytes-baseline.json)
records the 25,212 B/operation baked build baseline. The earlier
[event-only report](../../../docs/benchmarks/2026-09-23-editor-mono-allocation-events.json)
and [timing baseline](../../../docs/benchmarks/2026-09-23-editor-mono-timing-baseline.json)
also predate container capacity tuning.

September 23 DI pass: baked resolve became the default; post-build and
open-generic rebindings now refresh cached providers; argument-array pooling
and initial container storage were tightened. The validated Editor/Mono
reports above show faster timing than both comparison containers in all five
cases, equal resolve allocation to VContainer, and lower prepare/register
allocation. The full Unity EditMode suite passed 441/441 and PlayMode passed
3/3. That earlier Windows IL2CPP release player run also put Onity baked ahead
of both comparison containers in all five timing scenarios.

Earlier Windows IL2CPP release Player run: `2026-09-23T15:15:01Z`, Unity 2022.3.62f3,
512 warmup iterations, 8 measured samples, arithmetic mean, `19` generated
activators registered. [Raw report](../../../docs/benchmarks/2026-09-23-windows-il2cpp-release.json).

| Scenario | Onity Baked | VContainer | Zenject | Onity Baked vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~21 ns | ~91 ns | ~457 ns | ~+77% |
| Resolve Transient | ~120 ns | ~528 ns | ~2,608 ns | ~+77% |
| Resolve Combined | ~135 ns | ~697 ns | ~3,124 ns | ~+81% |
| Resolve Complex (6-level) | ~4,031 ns | ~13,025 ns | ~61,533 ns | ~+69% |
| Prepare & Register Complex | ~19,425 ns | ~40,575 ns | ~64,032 ns | ~+52% |

Separate Windows IL2CPP Development allocation capture:
[raw byte report](../../../docs/benchmarks/2026-09-23-windows-il2cpp-development-allocation-bytes.json).
The empty control recorded 0 B; the 1 MiB and 2 MiB controls recorded
1,053,728 B and 2,102,304 B. Each case had three identical samples of 64
operations on its warmed container.

| Scenario | Onity Baked (B / events per op) | VContainer | Zenject |
| --- | ---: | ---: | ---: |
| Resolve Singleton | 0 / 0 | 0 / 0 | 0 / 0 |
| Resolve Transient | 16 / 1 | 16 / 1 | 200 / 3 |
| Resolve Combined | 16 / 1 | 16 / 1 | 200 / 3 |
| Resolve Complex (6-level) | 384 / 24 | 384 / 24 | 4,800 / 72 |
| Prepare & Register Complex | 9,912 / 109 | 16,136 / 238 | 23,410 / 420 |

The release player timing report marks allocation unavailable. Its zero byte
fields are placeholders; use the separate Development capture for byte claims.

Latest Windows IL2CPP Player run: `2026-07-12T13:34:55Z`, Unity 2022.3.62f3,
512 warmup iterations, 8 measured samples, 10,000 measured iterations per
sample, arithmetic mean.

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~18 ns | ~18 ns | ~95 ns | ~449 ns | ~82% |
| Resolve Transient | ~159 ns | ~191 ns | ~541 ns | ~2,448 ns | ~71% |
| Resolve Combined | ~176 ns | ~196 ns | ~612 ns | ~3,080 ns | ~71% |
| Resolve Complex (6-level) | ~5,107 ns | ~5,071 ns | ~12,475 ns | ~59,327 ns | ~59% |
| Prepare & Register Complex | ~21,128 ns | ~24,490 ns | ~34,888 ns | ~59,567 ns | ~39% |

The raw reports retain the historical `Onity (Reflection)` label for the
standard lane. Generic resolves in that lane now use dense type-id provider
slots; reflection is only the activation fallback when no generated or compiled
activator exists.

Earlier focused Windows IL2CPP singleton release gate: `2026-07-12T13:30:25Z`, 512
warmup iterations, 1000 measured samples, 10,000 resolves per sample.

| Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| ---: | ---: | ---: | ---: | ---: |
| 18.80 ns | 17.40 ns | 94.39 ns | 435.24 ns | 80.1% |

See `Results/di-benchmark-player-singleton-1000.md` for the focused report.

IL2CPP note: both player runs use source-generated activators for the
benchmark graph, so transient and complex resolves avoid `ConstructorInfo.Invoke`
on AOT builds.

## 3. Render Charts

Install Python chart dependencies:

```bash
pip install matplotlib numpy
```

Generate charts:

```bash
python Packages/com.onity.framework/Benchmarks/Tools/render_di_benchmark_charts.py \
  --input Packages/com.onity.framework/Benchmarks/Results/di-benchmark-latest.json \
  --output-dir Packages/com.onity.framework/Benchmarks/Results
```

If you are currently inside `Benchmarks/Tools`, run:

```bash
python render_di_benchmark_charts.py \
  --input ../Results/di-benchmark-latest.json \
  --output-dir ../Results
```

Generated chart files:
- `Benchmarks/Results/di-runtime-comparison.png`
- `Benchmarks/Results/di-benchmark-summary.md`

`di-gc-alloc-comparison.png` is generated only when the input report explicitly
marks allocation measurement available. Current Unity 2022.3 Editor and IL2CPP
reports mark it unavailable, so allocation cells are published as `n/a`.

## 4. Publishing Recommendations

- Run benchmark in a dedicated scene and stable environment.
- Disable domain reload changes during benchmark runs.
- Report Unity version, platform, sample count, and iteration count with every chart.
- Keep benchmark code and registration graph identical across frameworks.
