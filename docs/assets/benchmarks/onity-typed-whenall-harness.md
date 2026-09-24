# Typed pending-pair WhenAll benchmark harness

The dedicated runner compares `OnityTask.WhenAll<int>` and UniTask 2.5.11 with
prebuilt typed input arrays. It is separate from the existing primitive benchmark.
Apply the identical harness and editor entry point to both product revisions.
Check both product source hashes (`OnityAsync.cs` and
`OnityTaskCompletionSource.cs`), runner/menu/asmdef/manifest/lock hashes,
UniTask commit, Unity version, scenario definitions, and independent Editor
logs before comparing revisions. A timing report and its later allocation
pass must have identical source hashes. Compare normalized Git blobs when
checkout line endings cause raw SHA-256 differences.

## Sessions

- Use Unity 2022.3.62f3 Editor/Mono. Run one Unity 2022 Editor process at a time.
- Enable `ONITY_TASK_BENCHMARKS` before Editor startup so both benchmark asmdefs
  compile. Confirm the benchmark entry point exists before starting a measured
  session.
- For each revision, run two independent timing processes and two independent
  Profiler processes. Each process has eight samples per case. A Profiler process
  adds allocations to one timing report from the same exact harness and product
  source hash; it does not supply a second timing replicate.
- Use the installed Unity CLI for mandatory preflight: verify `unity --version`,
  `unity run --help`, and pinned `unity status --json`. Installed CLI
  1.0.0-beta.3 injects `-quit` into `unity run`; the first baseline log
  confirmed this. It closes the Editor before this Play Mode coroutine can
  finish, so launch the pinned Unity 2022.3.62f3 Editor directly for benchmark
  sessions.
- Timing invocation: `Unity.exe -batchmode -nographics -projectPath
  <absolute-project> -executeMethod
  Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine
  -onityTypedWhenAllBenchmark -onityTaskBenchmarkOutput <absolute-json-path>
  -logFile <absolute-log-path>`. Do not pass `-quit`; the menu exits after
  writing the report.
- For allocations, use a fresh direct Editor process with the same entry point
  and output path. Add `-profiler-enable` and `-onityTaskAllocationsOnly`,
  with a separate log path. Retain the
  raw JSON, CSV, Markdown, and Editor log for each process. The output path must
  be outside the Unity package or otherwise approved as generated output.

## Measured slices

Inputs, pair arrays, fresh per-operation fault exceptions, and cancellation
token are prepared outside markers. Pending lifecycle markers include
scheduling, completion of both inputs, observation of the combined output,
validation of the ordered `int[]`, and cleanup. Scheduling controls measure
`WhenAll` and output storage; completion and observation happen after the
marker. The output result array is included in lifecycle allocations.
No input bridge's `.Exception` is read. Tracker on is Onity's default; tracker
off changes only Onity. UniTask uses its default behavior.

Cases are pending-success scheduling and lifecycle with tracker on/off,
pending fault and cancellation lifecycle with tracker on/off;
an Onity-only pending fault diagnostic with both input `AsTask` bridges
prepared before measurement; completed two- and four-input controls; first
observed pending scheduling in a warmed Editor; and 384 simultaneously
outstanding pending outputs. Cases have 128 operations per allocation sample.
Success/completed timing has 2,048 operations per sample; fault/cancel timing
has 256.

Main-thread bytes come from child `GC.Alloc` samples inside each Profiler
marker. A separate all-thread time-window pass includes tracker completion
and a tracked-batch barrier; those totals must remain separate from main-thread
bytes. Profiler calibration requires exactly 65,568 B for a 64 KiB positive
control, 0 B for empty controls and all eight empty-harness samples. The
all-thread window additionally requires a main-thread positive, empty, and
ThreadPool positive control. If worker or all-thread calibration fails, report
those rows as unavailable; do not treat the missing values as zero.

These are Editor/Mono measurements. They make no Player, IL2CPP, device,
frame-time, or whole-library superiority claim.
