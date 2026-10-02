# OnityTask vs UniTask 2.5.11: Release Player gate - 2026-10-02

## Result

The gate passed. On IL2CPP, the gate backend, every one of the 29 gated rows
met its threshold. The median Onity/UniTask time ratio ranges from 0.085
(`NextFrame GetResult`, 4,096 operations) to 0.808 (`FromResult<int> GetResult`):
UniTask took 1.24 to 11.8 times as long as OnityTask in these rows. No gated
IL2CPP process measured Onity slower than UniTask; the highest single-process
ratio is 0.906.

Mono is reported and not gated. Onity's median is lower in 25 of the 29 rows.
The four synchronous rows that consume an already completed task are 1.09x to
1.92x slower on Mono. Three of the four 4-suspension builder rows would miss the
IL2CPP thresholds although their medians are below 1 (0.935 to 0.973; one
process of the untyped 4,096 row measured 1.009).

These results cover the workloads below at the stated settings. They are not a
claim about every async pattern, about other platforms, or about allocation.

## What was measured

- **Source:** `perf/surpass-unitask` at `6cc21159461c5a970cdbc7de4e4522b20c8aa002`,
  clean. All 1,128 package files were staged byte-identically into the
  benchmark host (`staging.json`), and the host compile check passed for all
  14 compiled assemblies (`roslyn-host.log`, `roslyn-files.txt`). The build
  sidecar reads package version `0.5.0` because the version was raised after
  the measurement; the staged source identity, not that label, names the
  measured code.
- **Players:** Unity 2022.3.62f2, Windows x64 standalone, non-development
  Release Players (`BuildOptions.None`), IL2CPP compiler configuration
  `Release`, managed stripping `Minimal`. Mono build GUID
  `ad8bc783e0d34baab4bf1afb000e3141`, IL2CPP build GUID
  `a1e17c822d2b46799b38e6621ec43969`. The attempt reused the Players of the
  preceding harness self-test of the same source; `run-attempt.ps1 -PlayersFrom`
  accepts them only when the staged sources are byte-identical and every binary
  matches its manifest (`binary-manifest-*.json`).
- **UniTask:** 2.5.11 pinned to `2e993ff18f28c931602a07292df0b0804eebef99`, with
  its default settings.
- **Onity settings:** `OnityTask.FlowExecutionContext = false` (the library
  default; UniTask has no context flow). Task tracking off (it is on by default,
  but none of the measured paths call the tracker). Pool retention at the
  defaults, `RunnerPoolCapacity` 128 and `SourcePoolCapacity` 256, except that
  "matched" runs raise both caps to the cohort size before each 1,024- or
  4,096-operation cohort and restore them afterwards. UniTask's pools retain
  every released object by default, so matched retention compares the two
  libraries at equal retention; cohorts of 1 and 128 stay within Onity's
  defaults either way.
- **Suites** (defined in the
  [harness README](https://github.com/furkantokkan/Onity/blob/main/Packages/com.onity.framework/Benchmarks/Tasks/README.md)):
  - `primary`: synchronous main-thread slices. Completed and `FromResult<int>`
    `GetResult` and synchronously completing async methods (1,000,000 calls per
    sample), `NextFrame` scheduling and `GetResult`, and async methods that
    await one `NextFrame`, at 128 and 4,096 concurrent operations. Frame waiting
    and resumption are outside these slices, and raw times include the
    harness's delegate call (no baseline is subtracted).
  - `builderlifecycle`: the complete lifecycle of typed and untyped async
    methods that suspend 1 or 4 times on preallocated gates, at concurrency
    1, 128 and 4,096: registration, every resumption, consumption, and both
    libraries' deferred return work before reuse.
  - `throughput`: N concurrent loops of 16 awaits of `NextFrame()` or `Yield()`
    under the real PlayerLoop, from the scheduling frame to the frame in which
    every loop completed and was consumed, minus idle control frames.
- **Processes:** three Player processes per backend for each of the three suites
  at matched retention (the gate input), plus one process per backend for
  `primary` and `builderlifecycle` at default retention (report-only): 22
  processes, interleaved across backends and suites (`attempt-manifest.json`).
- **Statistic:** for each process and row, Onity's mean time divided by
  UniTask's mean time; the gate reads the median and the worst of the three
  processes. Each suite alternates library order between samples.
- **Thresholds:** gate backend IL2CPP. Synchronous `primary` rows (N = 1):
  median at most 1.00. Every other gated row: median at most 0.95 and worst
  process at most 1.00. Rows measured with flow on, with N = 1 in
  `builderlifecycle`, or at default retention are report-only.

## IL2CPP gate rows

Times are nanoseconds per operation as each suite defines it (per await for
`throughput`): the median across processes of each library's per-process mean.
A ratio below 1 means OnityTask took less time.

| Suite | Row | N | Onity ns | UniTask ns | Median ratio | Worst ratio |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| primary | Completed GetResult | 1 | 2.67 | 5.18 | 0.515 | 0.516 |
| primary | FromResult<int> GetResult | 1 | 9.45 | 11.8 | 0.808 | 0.906 |
| primary | NextFrame scheduling | 128 | 13.5 | 125 | 0.108 | 0.110 |
| primary | NextFrame GetResult | 128 | 3.81 | 30.3 | 0.125 | 0.126 |
| primary | NextFrame scheduling | 4096 | 11.8 | 120 | 0.098 | 0.098 |
| primary | NextFrame GetResult | 4096 | 2.37 | 27.9 | 0.085 | 0.086 |
| primary | Async method completed GetResult | 1 | 9.60 | 15.3 | 0.629 | 0.634 |
| primary | Async method completed<int> GetResult | 1 | 15.6 | 20.7 | 0.750 | 0.753 |
| primary | Async method NextFrame scheduling | 128 | 77.8 | 188 | 0.415 | 0.415 |
| primary | Async method NextFrame GetResult | 128 | 22.9 | 70.5 | 0.326 | 0.329 |
| primary | Async method NextFrame scheduling | 4096 | 77.1 | 188 | 0.411 | 0.413 |
| primary | Async method NextFrame GetResult | 4096 | 22.0 | 69.5 | 0.307 | 0.317 |
| primary | Async method NextFrame<int> scheduling | 128 | 81.3 | 196 | 0.415 | 0.418 |
| primary | Async method NextFrame<int> GetResult | 128 | 24.5 | 74.2 | 0.330 | 0.341 |
| primary | Async method NextFrame<int> scheduling | 4096 | 77.1 | 194 | 0.397 | 0.399 |
| primary | Async method NextFrame<int> GetResult | 4096 | 22.6 | 72.4 | 0.311 | 0.325 |
| builderlifecycle | Untyped, 1 suspension | 128 | 88.0 | 174 | 0.511 | 0.534 |
| builderlifecycle | Untyped, 4 suspensions | 128 | 146 | 243 | 0.603 | 0.612 |
| builderlifecycle | Untyped, 1 suspension | 4096 | 87.4 | 178 | 0.490 | 0.494 |
| builderlifecycle | Untyped, 4 suspensions | 4096 | 150 | 252 | 0.598 | 0.598 |
| builderlifecycle | Typed `int`, 1 suspension | 128 | 88.4 | 174 | 0.501 | 0.623 |
| builderlifecycle | Typed `int`, 4 suspensions | 128 | 146 | 241 | 0.605 | 0.874 |
| builderlifecycle | Typed `int`, 1 suspension | 4096 | 88.2 | 178 | 0.495 | 0.523 |
| builderlifecycle | Typed `int`, 4 suspensions | 4096 | 154 | 259 | 0.594 | 0.637 |
| throughput | NextFrame loops, K = 16 | 1024 | 59.8 | 203 | 0.293 | 0.299 |
| throughput | NextFrame loops, K = 16 | 4096 | 52.6 | 188 | 0.280 | 0.280 |
| throughput | Yield loops, K = 16 | 1024 | 31.5 | 89.6 | 0.352 | 0.377 |
| throughput | Yield loops, K = 16 | 4096 | 30.0 | 87.5 | 0.353 | 0.355 |
| throughput | NextFrame<int> loops, K = 16 | 1024 | 55.4 | 199 | 0.285 | 0.288 |

All 29 rows pass. The three per-process ratios of each row are listed in
`surpass-gate.md`.

## Mono rows (reported, not gated)

| Suite | Row | N | Onity ns | UniTask ns | Median ratio | Worst ratio |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| primary | Completed GetResult | 1 | 3.95 | 3.05 | 1.291 | 1.319 |
| primary | FromResult<int> GetResult | 1 | 14.8 | 7.68 | 1.924 | 1.925 |
| primary | NextFrame scheduling | 128 | 37.5 | 119 | 0.314 | 0.320 |
| primary | NextFrame GetResult | 128 | 3.92 | 29.4 | 0.133 | 0.138 |
| primary | NextFrame scheduling | 4096 | 37.4 | 117 | 0.319 | 0.321 |
| primary | NextFrame GetResult | 4096 | 3.52 | 28.8 | 0.120 | 0.125 |
| primary | Async method completed GetResult | 1 | 17.8 | 16.4 | 1.087 | 1.104 |
| primary | Async method completed<int> GetResult | 1 | 25.8 | 21.6 | 1.196 | 1.218 |
| primary | Async method NextFrame scheduling | 128 | 145 | 247 | 0.581 | 0.587 |
| primary | Async method NextFrame GetResult | 128 | 23.2 | 29.7 | 0.783 | 0.784 |
| primary | Async method NextFrame scheduling | 4096 | 132 | 225 | 0.586 | 0.588 |
| primary | Async method NextFrame GetResult | 4096 | 21.1 | 25.9 | 0.814 | 0.826 |
| primary | Async method NextFrame<int> scheduling | 128 | 124 | 221 | 0.564 | 0.570 |
| primary | Async method NextFrame<int> GetResult | 128 | 21.0 | 27.2 | 0.766 | 0.789 |
| primary | Async method NextFrame<int> scheduling | 4096 | 119 | 209 | 0.566 | 0.572 |
| primary | Async method NextFrame<int> GetResult | 4096 | 20.7 | 27.5 | 0.754 | 0.758 |
| builderlifecycle | Untyped, 1 suspension | 128 | 127 | 144 | 0.885 | 0.887 |
| builderlifecycle | Untyped, 4 suspensions | 128 | 222 | 237 | 0.932 | 0.943 |
| builderlifecycle | Untyped, 1 suspension | 4096 | 128 | 146 | 0.889 | 0.894 |
| builderlifecycle | Untyped, 4 suspensions | 4096 | 233 | 250 | 0.935 | 1.009 |
| builderlifecycle | Typed `int`, 1 suspension | 128 | 131 | 145 | 0.895 | 0.912 |
| builderlifecycle | Typed `int`, 4 suspensions | 128 | 235 | 241 | 0.973 | 0.976 |
| builderlifecycle | Typed `int`, 1 suspension | 4096 | 131 | 144 | 0.909 | 0.916 |
| builderlifecycle | Typed `int`, 4 suspensions | 4096 | 246 | 253 | 0.971 | 0.977 |
| throughput | NextFrame loops, K = 16 | 1024 | 83.9 | 229 | 0.365 | 0.366 |
| throughput | NextFrame loops, K = 16 | 4096 | 82.3 | 230 | 0.361 | 0.366 |
| throughput | Yield loops, K = 16 | 1024 | 43.3 | 63.7 | 0.674 | 0.709 |
| throughput | Yield loops, K = 16 | 4096 | 46.9 | 65.5 | 0.721 | 0.730 |
| throughput | NextFrame<int> loops, K = 16 | 1024 | 84.6 | 232 | 0.370 | 0.384 |

Had the IL2CPP thresholds applied, seven Mono rows would fail them: the four
synchronous completed-result rows (Onity slower) and three 4-suspension builder
rows (Onity faster, but not by the required margin).

## Report-only arms

### Opt-in context flow (`FlowExecutionContext = true`)

UniTask has no execution-context flow, so these rows price a feature UniTask
does not offer; they are never gated. Median ratios at matched retention, three
processes:

| Row | IL2CPP | Mono |
| --- | ---: | ---: |
| primary async method `NextFrame` scheduling, 128/4096 (typed and untyped) | 0.499-0.525 | 0.753-0.769 |
| primary async method `NextFrame` `GetResult`, 128/4096 | 0.313-0.325 | 0.724-0.819 |
| builderlifecycle, 1 suspension, N = 128/4096 | 0.816-0.836 | 1.626-1.651 |
| builderlifecycle, 4 suspensions, N = 128/4096 | 1.514-1.601 | 2.646-2.721 |
| builderlifecycle, 1 suspension, N = 1 | 0.946-0.955 | 1.336-1.355 |
| builderlifecycle, 4 suspensions, N = 1 | 1.071-1.075 | 1.875-1.911 |

With flow on, every suspension captures and restores the execution context.
The full method lifecycle therefore costs more than UniTask's, by 1.5x to 1.6x
at four suspensions on IL2CPP and by 1.3x to 2.7x on Mono, while the
synchronous scheduling and consumption slices stay faster.

### Default retention for 4,096-operation bursts

One process per backend. The 1- and 128-operation rows of these runs, which stay
within the default caps, agree with the matched runs within 7 percent.

| Row (N = 4096) | IL2CPP | Mono |
| --- | ---: | ---: |
| builderlifecycle untyped, 1 suspension | 2.575 | 2.495 |
| builderlifecycle untyped, 4 suspensions | 1.820 | 1.805 |
| builderlifecycle typed `int`, 1 suspension | 1.472 | 2.370 |
| builderlifecycle typed `int`, 4 suspensions | 0.946 | 1.632 |
| primary async method `NextFrame` scheduling (untyped / typed) | 0.715 / 0.710 | 0.882 / 0.891 |
| primary `NextFrame` scheduling | 0.096 | 0.315 |

At its defaults Onity keeps 128 runners per async method and 256 sources per
source type. A burst of 4,096 concurrent calls of one method allocates a runner
for every call above the cap, and that allocation dominates the complete
lifecycle, so those bursts run 1.5x to 2.6x slower than UniTask, which keeps
every runner (IL2CPP typed `int` with 4 suspensions is the exception, at 0.946). Raise `OnityTask.RunnerPoolCapacity` (and `SourcePoolCapacity`)
before such a burst when the retained memory is acceptable; the gate rows above
show the matched result. The synchronous `primary` slices stay faster at the
default caps.

### Builder lifecycle with one call (N = 1)

IL2CPP medians 0.862-0.901 (flow off); Mono medians 0.970-1.003, within noise of
UniTask.

## Noise and limits

- The quiet pre-gate refused the host: during its 11-second sample another
  Unity Editor used about 100% of one core and two more used about 2% each
  (`quiet-pregate.json`). The attempt continued and recorded noise per process;
  the screen flagged all 22 processes. On each backend 21 of the 29 rows have
  their three per-process ratios within 5% of each other; the widest IL2CPP
  spreads are typed `int` at N = 128 with 4 suspensions (0.603, 0.874, 0.605)
  and 1 suspension (0.501, 0.623, 0.501), and `FromResult<int> GetResult`
  (0.780, 0.906, 0.808). Every IL2CPP process still measured Onity faster in
  every gated row.
- One Windows PC. Other machines, other platforms (ARM64, consoles, mobile),
  other Unity versions and Development Players are not covered.
- The rows are timing results only. Allocation was not part of the gate.
- `throughput` windows contain whole engine frames; idle control frames are
  subtracted (see the harness README for the scope of that subtraction).
- The default-retention arms ran in one process per backend.

## Generated C++ check

The IL2CPP output of this exact Player (build GUID
`a1e17c822d2b46799b38e6621ec43969`, `il2cppOutput` of the self-test build that
the attempt reused; not committed) was inspected for the safety of returning an
async-method runner to its pool as soon as its result is consumed:

- The runner's `MoveNext` calls the stored state machine's `MoveNext` by address
  (`&__this->___m_stateMachine`): no copy into a local and no copy back after the
  call, so a runner that was returned to the pool and rented again during that
  call is not overwritten when the call unwinds.
- `ReturnToPool` clears the stored state machine with `il2cpp_codegen_initobj`
  before the runner goes back to the pool.
- No generated function contains a class-initialization check for `OnityTask`:
  the struct has no static constructor, so its inlined members read
  `CompletedTask` without one.
- The static-field reads of `OnityTaskSettings` (436 sites) and
  `OnityTaskPlayerLoop` (244 sites) carry no class-initialization check either;
  both types are marked `[Il2CppEagerStaticClassConstruction]`.

## Player smoke

The same Mono and IL2CPP Players (same build GUIDs) passed all 42 cases of the
`smoke` suite, flow off, runner capacity 128, non-development:
`smoke/smoke-mono.json` and `smoke/smoke-il2cpp.json`.

## Files

| File | Content |
| --- | --- |
| `onitytask-surpass-2026-10-02/surpass-gate.md` | Gate report: every row with its three per-process ratios, the report-only rows and the noise screen. |
| `onitytask-surpass-2026-10-02/gate-summary.json` | Machine-readable gate result, inputs with SHA-256, thresholds and per-row values. |
| `onitytask-surpass-2026-10-02/attempt-manifest.json`, `attempt.log` | Attempt steps, Player processes with arguments, timing, exit codes and noise status. |
| `onitytask-surpass-2026-10-02/staging.json` | Hash of every staged package file. |
| `onitytask-surpass-2026-10-02/binary-manifest-mono.json`, `binary-manifest-il2cpp.json` | Hash of every Player file. |
| `onitytask-surpass-2026-10-02/quiet-pregate.json` | The refused quiet pre-gate sample. |
| `onitytask-surpass-2026-10-02/roslyn-host.log`, `roslyn-files.txt` | Host compile check. |
| `onitytask-surpass-2026-10-02/gate.log` | Gate script output. |
| `onitytask-surpass-2026-10-02/files.sha256` | SHA-256 of every attempt file, including the raw runs. |
| `onitytask-surpass-2026-10-02/smoke/` | Player smoke reports for both backends. |

`quiet-pregate.json` and `attempt.log` are published with the paths of the other
local Unity projects replaced by `<other Unity project A/B/C>`; their hashes in
`files.sha256` are those of the unredacted originals. Every other attempt file is
byte-identical to the attempt directory; the folder's `.gitattributes` turns off
line-ending conversion so the hashes verify on every platform.

The 22 per-process reports, their observation files and Player logs (`runs/`,
112 files, about 18 MB) are not committed. They are attached to the v0.6.0
GitHub release as `onitytask-surpass-2026-10-02-runs.zip`; `files.sha256`
lists the hash of each file.

## Reproduce

On a Windows host with the pinned UniTask package and the `ONITY_TASK_BENCHMARKS`
define, `tools/benchmark-host/run-attempt.ps1` stages the package, compiles,
builds or reuses one Release Player per backend, runs the 22 processes and calls
`tools/benchmark-host/surpass-gate.py`, which writes `surpass-gate.md` and
`gate-summary.json`. See `tools/benchmark-host/README.md`.
