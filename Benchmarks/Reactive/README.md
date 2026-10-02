# Reactive comparison benchmark (Onity.Reactive vs R3 vs UniRx)

This benchmark measures Onity.Reactive against R3 1.3.0 and UniRx 7.1.0 in Windows x64 Release
Players, IL2CPP first and Mono reported the same way. All three libraries run the same workloads with the
same value sequence, and every run checks that they produced the same results.

It is a comparison, not a gate. The results are reported whatever they are.

The code is gated behind `ONITY_REACTIVE_BENCHMARKS`, and its assembly references R3 and UniRx. A normal
project that uses the package never compiles it. Only the comparison host defines the symbol and holds the
third-party libraries.

## Contents

| Path | Purpose |
| --- | --- |
| `Runtime/Onity.ReactiveBenchmarks.asmdef` | Runtime assembly. References `Onity.Core`, `Onity.Reactive` and `UniRx`. R3 comes from the auto-referenced `R3.dll` (`overrideReferences: false`). |
| `Runtime/OnityReactiveBenchmarkOnityWorkloads.cs`, `...R3Workloads.cs`, `...UniRxWorkloads.cs` | One file per library. Each imports only its own namespace, because `Subject`, `ReactiveProperty`, `Where`, `Select` and `Subscribe` exist in all three. |
| `Runtime/OnityReactiveBenchmarkScenarios.cs` | Scenario table and the golden model, which computes the expected sink totals without any reactive library. |
| `Runtime/OnityReactiveBenchmarkRunner.cs` | Warmups, rotation, timing, the allocation pass, statistics and ratios. |
| `Runtime/OnityReactiveBenchmarkPlayerRunner.cs` | Player entry point and command-line arguments. |
| `Editor/OnityReactiveBenchmarkPlayerBuildRunner.cs` | Release Player build entry and the `.build.json` sidecar. |
| `tools/benchmark-host/run-reactive-comparison.ps1` (repository) | Runs the Player processes and keeps the evidence. |
| `tools/benchmark-host/reactive-summary.py` (repository) | Validates the reports and writes the summary. |

## Host setup

The comparison host is a separate scratch Unity 2022.3.62f2 project. On the reference machine it is
`C:\Users\e-fur\.codex\worktrees\onity-compare-host\Onity`. Its `HOST-SETUP.md` records every copied
source, version, license and hash. It contains the following:

- `Assets/`, `Packages/manifest.json`, `Packages/packages-lock.json` and `ProjectSettings/` from the
  public project.
- The package, staged with `tools/benchmark-host/stage-host.ps1 -Source <worktree> -HostProject <host>
  -ManifestPath <file>`.
- R3 1.3.0 (`lib/netstandard2.1/R3.dll`) and its netstandard2.1 dependencies, all in
  `Assets/Plugins/R3/` with license texts:
  - `Microsoft.Bcl.TimeProvider` 8.0.0
  - `Microsoft.Bcl.AsyncInterfaces` 9.0.9, which TimeProvider references
  - `System.ComponentModel.Annotations` 5.0.0
  - `System.Threading.Channels` 8.0.0
  - `System.Runtime.CompilerServices.Unsafe` 6.1.2. Neither Unity nor a host package provides it for the
    .NET Standard 2.1 profile.

  R3 1.3.1 has no runtime change from 1.3.0; it changed only CI, packaging and a Unity 6.2 editor fix.
- UniRx 7.1.0 from `https://github.com/neuecc/UniRx/releases/download/7.1.0/UniRx.unitypackage`, extracted
  with its GUID metas into `Assets/Plugins/UniRx/`. The vendor files are unmodified. Examples are skipped.
  UniRx 7.1.0's asmdef does not reference the UGUI package assembly, so it cannot compile its UGUI and
  EventSystems bridge files on Unity 2022.3. Those 22 files and the 13 files whose compilation depends on
  them are not imported. They are frame-timing operators, coroutine and WWW helpers, `ObserveExtensions`
  and `ObjectPool`. None of the benchmarked types is among them: `Subject`, `ReactiveProperty`, the
  `Where`, `Select`, `WhereSelect` and `CombineLatest` operators, the `Subscribe` observers and the
  disposables are all byte-identical to the vendor files. The host manifest also gains the built-in module
  `com.unity.modules.unitywebrequestwww`, which the Editor coroutine path of UniRx's `MainThreadDispatcher` needs for `UnityEngine.WWW`.
- VContainer 1.17.0 as an embedded package, and Zenject 9.2.0 runtime in `Assets/Plugins/Zenject/`.
  These are for the DI benchmark in the same host.
- Standalone scripting defines `ONITY_BENCHMARKS;ONITY_REACTIVE_BENCHMARKS`. These are host only.
- The PlayMode test fixture scene copied with the project's build settings is disabled in the host. This
  lets the DI build runner generate its bootstrap scene. With an enabled build scene, the IL2CPP linker
  removes the DI benchmark assembly, which has no `AlwaysLinkAssembly` and is referenced by nothing, and
  the DI Player never starts its run.

## Build

Run one Editor at a time, in batch mode, on the host only:

```text
Unity.exe -batchmode -nographics -quit -projectPath <host> -logFile <log>
  -executeMethod Onity.Editor.Benchmarks.OnityReactiveBenchmarkPlayerBuildRunner.BuildPlayerFromCommandLine
  -onityReactiveBenchmarkBackend IL2CPP|Mono
  -onityReactiveBenchmarkBuildPath <dir>/<IL2CPP|Mono>/OnityReactiveBenchmark.exe
  [-onityReactiveBenchmarkSourceHead <git sha>]
```

The build is Windows x64 and Release (`BuildOptions.None`, no Development flag). It uses a temporary empty
scene (`Assets/OnityBenchmarkTemp/OnityReactiveBenchmarkPlayer.unity`), which is deleted afterwards. The
scripting backend and the active build target are restored. The build refuses to start when the
Standalone defines lack `ONITY_REACTIVE_BENCHMARKS`.

Next to the exe it writes `<exe>.build.json`. The sidecar records the backend, Unity version, build GUID,
time, build options and Development flag, Standalone defines, IL2CPP configuration, stripping level, Onity
package id and version, R3 version, UniRx version and source HEAD.

## Run

Player arguments:

| Argument | Meaning |
| --- | --- |
| `-onityRunReactiveBenchmark` | Run the benchmark, then quit. |
| `-onityReactiveBenchmarkOutput <json>` | Report path. The default is the Player's persistent data folder. |
| `-onityReactiveBenchmarkSelfTest` | Tiny counts (default ops / 1000, 3 samples). The report is marked `selfTest`. Never evidence. |
| `-onityReactiveBenchmarkSamples <n>` | Measured samples per library and scenario (default 15). |
| `-onityReactiveBenchmarkScenario <id>` | Run one scenario only. |
| `-onityReactiveBenchmarkBuildMetadata <path>` | Build sidecar to embed. The default is `<exe>.build.json`. |

The Player writes the report and exits with 0. It exits with 1 on any failure, such as a golden mismatch,
an exception or an invalid argument. It writes the report in every case where the output path is known.

Measurement, with 3 processes per backend:

```text
pwsh -File tools/benchmark-host/run-reactive-comparison.ps1 -HostProject <host>
  -PlayersFrom <dir with IL2CPP/ and Mono/> -EvidenceRoot <host>/BenchmarkResults/reactive-<label>
python tools/benchmark-host/reactive-summary.py --evidence-root <host>/BenchmarkResults/reactive-<label>
```

The runner runs one Player process at a time and alternates the backend order per round. Each process
runs at `-Priority High` with an affinity mask that excludes cores 0 and 1, under a timeout. It refuses
to start a process while a Unity Editor runs on the host or another benchmark Player is running.

For each process it keeps `runs/<stem>.json`, `runs/<stem>.player.log` and
`runs/<stem>.observation.json`. The observation file holds the exit code, wall time, applied priority and
affinity, binary hashes, and the CPU used by other Unity Editors during the process. The runner also
writes `run-manifest.json`. It refuses a non-empty evidence root.

For a self-test, add `-SelfTest` to the runner and `--allow-self-test` to the summary.

## Scenarios

All payloads are `int`. Each library uses its idiomatic public API with cached delegates. Every
subscription uses each library's `Subscribe(Action<int>)` with one cached `Action<int>` per workload, so
library-side allocations count and user-side delegate creation does not.

| Id | Untimed setup | Timed operation (per op) | Ops/sample |
| --- | --- | --- | ---: |
| `SubjectOnNext1` | subject + 1 subscriber | `subject.OnNext(i)` | 200,000 |
| `SubjectOnNext8` | subject + 8 subscribers | `subject.OnNext(i)` | 50,000 |
| `SubjectSubscribeDispose` | subject with 1 resident subscriber | `Subscribe(cachedAction)` then `Dispose()` | 20,000 |
| `PropertySetChanged` | property = 0 + 1 subscriber | `Value = i + 1` (always a new value) | 200,000 |
| `PropertySetSame` | property = 42 + 1 subscriber | `Value = 42` (no notification) | 200,000 |
| `PropertySubscribeDispose` | property = 7 | `Subscribe(cachedAction)` (receives 7) then `Dispose()` | 20,000 |
| `WhereSelectOnNext` | `subject.Where(x => (x & 1) == 0).Select(x => x * 2).Subscribe(sink)` | `subject.OnNext(i)` | 200,000 |
| `ChainSubscribeDispose` | subject, cached predicate, selector and sink | build `Where().Select().Subscribe()` then `Dispose()` | 10,000 |
| `CombineLatestOnNext` | `a.CombineLatest(b, (x, y) => x + y).Subscribe(sink)`, primed with 1 and 2 | `a.OnNext(i)` for even `i`, `b.OnNext(i)` for odd `i` | 100,000 |

The sink sums and counts the values it receives during the timed loop. After the timed loop, outside
timing, the runner pushes one probe value (-2) through the scenario's source. The probe proves that the
graph is still connected. After a subscribe/dispose loop it proves that every transient subscription was
really removed.

A library-independent model (`OnityReactiveBenchmarkGolden`) computes the expected checksum, notification
count and probe totals. Every sample of every library must match them: warmups, measured samples and the
allocation pass. Otherwise the scenario fails, the run exits with 1, and the report names the first
mismatch.

## Measurement

- Per scenario: a full GC (`Collect`, `WaitForPendingFinalizers`, `Collect`), then 2 discarded warmup
  samples per library, then S measured samples (default 15).
- Sample k runs the libraries in the order Onity, R3, UniRx rotated left by k mod 3, warmups included.
  No library always runs first.
- Each sample creates its sources and subscriptions untimed, times only the loop with
  `Stopwatch.GetTimestamp`, and checks and disposes untimed. ns/op = ticks x 1e9 / `Stopwatch.Frequency`
  / ops.
- The report lists per library all samples (ns/op and raw ticks), each sample's order position, gen-0
  collections during the measured samples, the median, mean, min and max, the checksums, and the
  per-scenario median ratios Onity/R3 and Onity/UniRx.
- Allocation is a separate untimed pass per library and scenario. It reads
  `GC.GetAllocatedBytesForCurrentThread()` around the timed loop and reports bytes/op. The counter is
  used only when a 64 KiB array reads at least 64 KiB (positive control) and two consecutive reads differ
  by 0 (empty control). Otherwise allocation is reported unavailable for that backend.
- On IL2CPP the counter is never called, because the DI harness observed IL2CPP crashes with it. The
  report therefore says allocation is unavailable there.
- In the 2026-10-02 self-test on the reference host, the Mono Release Player's positive control also read
  0 bytes. As it stands, this harness reports allocation as unavailable on both backends.
- The report also records the Unity version, backend, Development flag, OS, CPU, processor count, UTC
  time, command line, self-test flag, GC mode and the embedded build sidecar.

## Fairness notes

- The user code is the same shape for all three libraries: same loops, same cached static predicate,
  selector and combiner, same sink. The R3 and UniRx workload files differ only in their `using` line.
- `Subscribe(Action<int>)` is each library's idiomatic overload, and each wraps the action differently:
  - Onity's extension stores the action in the subscription node, and the source invokes it directly.
    Onity 0.6.0 wrapped the action in an `Observer<T>` delegate, which cost one extra delegate call per
    notification.
  - R3 wraps the action in an `AnonymousObserver`.
  - UniRx wraps the action in a subscribe observer.

  These wrappers are part of what is measured.
- UniRx, R3 and Onity fuse `Where(...).Select(...)` into one operator. Onity 0.6.0 composed two
  delegate-backed operators. This is each library's design, and it is measured as such.
- Onity's `Subject` isolates each observer's exceptions, with one exception region per notification pass
  (0.6.0 caught around each call). UniRx's `CombineLatest` takes a lock per notification. Neither is
  disabled.
- Library defaults are left alone. R3 is used without `R3.Unity`, so there are no frame providers, and its
  tracking is off by default. The benchmarked UniRx types do not touch `MainThreadDispatcher`.
- The whole measurement runs synchronously inside one `AfterSceneLoad` callback, so no frame or
  PlayerLoop work interleaves with it. The Mono Player has no stripping and loads every host assembly,
  including Onity.Unity's PlayerLoop hooks. The IL2CPP linker removes unreferenced assemblies, so
  `Onity.ReactiveBenchmarks` carries `[assembly: AlwaysLinkAssembly]`.
- Samples are short (milliseconds at the default counts), so they are sensitive to scheduling noise. Use
  the raw samples and the per-process ratios, not one number, when a difference is small.

## Pre-registered classification

This rule was fixed before any measurement. Each scenario gets a ratio for Onity/R3 and for Onity/UniRx.
A process ratio is the Onity process median ns/op divided by the other library's process median ns/op,
so a ratio below 1 means Onity was faster. Across the processes of one backend:

- `faster` when the median ratio <= 0.95 and the worst (largest) process ratio <= 1.00;
- `slower` when the median ratio >= 1.05;
- `on par` otherwise.

IL2CPP is the headline. Mono is classified the same way and reported alongside.

## Reading the summary

`reactive-summary.py` validates the evidence before it summarizes. It checks the following:

- no self-test report, unless `--allow-self-test` is given;
- every golden check passed, and checksums are identical across the libraries;
- every report is from a Release, non-development Player whose build sidecar matches it;
- there are exactly N processes per backend (`--processes`, default 3) and one build per backend;
- all reports have the same scenarios, operation counts and sample counts;
- the ns/op values and medians agree with the raw Stopwatch ticks.

It then writes `reactive-summary.md` and `reactive-summary.json`, and never overwrites them without
`--force`.

Per backend and scenario, the summary lists the median over processes of the process medians for each
library, the per-process ratios, the median and worst ratio, and the class. When the counter passed its
controls, it also lists allocation bytes/op.

A self-test summary carries a banner and is never performance evidence.
