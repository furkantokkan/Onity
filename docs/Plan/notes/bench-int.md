# BENCH-INT note: benchmark integration fixes on `perf/benchmark-baseline`

Packet: BENCH-INT (plan 16). Worktree `onity-benchmark-baseline`, base `be13218` (PERF-1 + PERF-2 + PERF-3).
Benchmark-only: no runtime, test, asmdef, ProjectSettings or manifest edits. No git writes, no Unity launch,
no slot taken.

Status: implemented and proxy-compiled. NOT run in a Player. The real Roslyn/Unity compile and the Player runs
happen on the benchmark host in the next step.

## Changed files

- `Packages/com.onity.framework/Benchmarks/Tasks/Editor/OnityTaskBenchmarkPlayerBuildRunner.cs`
- `Packages/com.onity.framework/Benchmarks/Tasks/Runtime/`: `OnityTaskBenchmarkRunner.cs`,
  `OnityTaskBuilderCycleBenchmarkRunner.cs`, `OnityTaskBuilderLifecycleBenchmarkRunner.cs`,
  `OnityTaskFrameLifecycleBenchmarkRunner.cs`, `OnityTaskReadinessCycleBenchmarkRunner.cs`,
  `OnityTaskJobBenchmarkRunner.cs`, `OnityTaskPlayerLoopBenchmarkRunner.cs`, `OnityTaskWhenAllBenchmarkRunner.cs`,
  `OnityTaskWhenAnyBenchmarkRunner.cs`, `OnityAsyncEnumerableBenchmarkRunner.cs`, `OnityChannelBenchmarkRunner.cs`,
  `OnityThreadSwitchBenchmarkRunner.cs`, `OnityTaskPlayerSmokeRunner.cs`
- `Packages/com.onity.framework/Benchmarks/Tasks/README.md`
- `tools/benchmark-host/validate-framelifecycle.py`
- `docs/Plan/notes/bench-int.md` (this note)

## 1. BuildRunner forwards `-onityTaskBenchmarkRetention`

- New `ForwardRetentionArgument(suite)`. Absent returns empty. Present: a trailing flag with no value, a value other
  than `default|matched` (case-insensitive), or use with a suite other than `builderlifecycle` throws
  `ArgumentException`. Otherwise the lowercased value is appended to the Player arguments, in the same place as the
  existing optional forwards (`... + frameLifecycleArms + retentionArgument`).
- `BuildAndRunFromCommandLine` calls it before the build, so a bad launch fails in seconds, not after a build.
  `RunPlayer` calls it again to build the Player argument string.
- The strict "builderlifecycle only" rule is a launcher choice: a matched flag on another suite would otherwise be
  silently ignored and the run would be mislabelled by the operator.

## 2. Flow default in the harness (exact before/after labels)

Library default is flow off (PERF-1). Every Onity-side runner now sets `FlowExecutionContext = false` at start
(was `true`) and restores the previous value in the existing restore path. `FlowExecutionContext` is set explicitly
(not left alone) so a host that changed the flag cannot leak into a run; the runner that records the default
(`OnityTaskBenchmarkRunner`) now records the value read before the harness touched it.

Runners that measure both flow arms (primary arm is now off, on is the labelled secondary arm):

| Runner | Field | Before | After |
| --- | --- | --- | --- |
| `OnityTaskBenchmarkRunner` (primary), scenarios 8-15 | name suffix / workload | unsuffixed, `; OnityTask.FlowExecutionContext on` | unsuffixed, `; OnityTask.FlowExecutionContext off` |
| same, scenarios 16-23 | name suffix / workload | ` (flow off)`, `...Context off` | ` (flow on)`, `...Context on` |
| same | `measurementScope` | `... with OnityTask.FlowExecutionContext on (unsuffixed) and off (suffixed 'flow off') ...` | `... with OnityTask.FlowExecutionContext off, the library default (unsuffixed, primary), and on (suffixed 'flow on', secondary) ...` |
| same | `flowExecutionContextDefault` | value after the harness forced true (always true) | value at `Run` start, before the harness sets the flag |
| `OnityTaskBuilderCycleBenchmarkRunner` | arm `flow` 0 / 1 (`metrics`, `controls`) | 0 = `onityFlowExecutionContext` true, 1 = false | 0 = false (primary), 1 = true |
| `OnityTaskBuilderLifecycleBenchmarkRunner` | arm `flow` 0 / 1 | 0 = true (primary), 1 = false and `diagnosticOnly` | 0 = false (primary), 1 = true and `diagnosticOnly` |
| same | `flowPolicy` | `Flow=true primary; flow=false diagnostic only; no AsyncLocal seeded. UniTask no-flow controls do not provide Onity default execution-context semantics.` | `Flow=false (library default) primary; flow=true is an explicitly labelled secondary arm, diagnostic only; no AsyncLocal seeded. UniTask no-flow controls match the primary arm's execution-context semantics.` |
| same | `measurementScope` | `... default-flow capture ...` | `... per-arm execution-context flow setting ...` |
| `OnityThreadSwitchBenchmarkRunner` (threadpool probes) | per-probe flags and labels | unchanged (already `OnityTask flow-off` / `flow-on`); start-level flag true | start-level flag false; probes still set their own arm |

Array positions are kept (arm 0 first), so schema, counts and indexes are unchanged. A consumer that keys on
position, or on the ` (flow off)` suffix, sees the flow value swapped; consumers that key on names or
`onityFlowExecutionContext` are unaffected. Reports from before this change are not comparable by name for
scenarios 8-23 of the primary suite.

Single-arm runners (flag only, `true` to `false`, no label change): `OnityTaskJobBenchmarkRunner`,
`OnityTaskPlayerLoopBenchmarkRunner`, `OnityTaskWhenAllBenchmarkRunner`, `OnityTaskWhenAnyBenchmarkRunner`,
`OnityAsyncEnumerableBenchmarkRunner`, `OnityChannelBenchmarkRunner`,
`OnityTaskReadinessCycleBenchmarkRunner` (also `flowPolicy`: `Onity default FlowExecutionContext=true, extra context
semantics disclosed; UniTask has no-flow semantics. No AsyncLocal seeded.` became `Onity FlowExecutionContext=false
(library default, UniTask-equivalent no-flow semantics). No AsyncLocal seeded.`). Their environment capture now
records `flowExecutionContext: false`.

Not a measurement, still changed: `OnityTaskPlayerSmokeRunner` start flag and three `finally` restores are now
`false`; cases that need flow on already set it themselves (`WorkerContext`, `MainThreadContext`,
`ThreadPoolContext` and the safe-await check), so the other cases now run at the default. Unverified: not run.

Left alone on purpose: `OnityTaskFullCycleProfileRunner` (flow is an explicit required `-onityTaskProfileFlow on|off`
case argument) and `OnityTaskFrameLifecycleBenchmarkRunner` (already sets false).

## 3. Minors from the PERF-2 and PERF-3 notes and verdicts

PERF-2 (builderlifecycle):

- Report `retentionPolicy` prose: re-checked. In default mode it is byte-identical to the pre-PERF-2 string at
  `530c4dd` (untouched line); in matched mode it is a matched-specific string (rewritten here to describe the new
  labels).
- A trailing `-onityTaskBenchmarkRetention` with no value: the runner already throws like an invalid value
  (`ReadMatchedRetention`). The launcher now throws for it too (item 1). No runner change needed.
- 1/128 cohort labels in a matched run: chose "label truthfully" over re-ordering the loops (simpler, no change to
  metric order). Metric `retentionPolicy`/`retentionLabel` is `matched` for 4096 arms; for 1 and 128 arms it is
  `default` until the first 4096 arm has started and `default-after-matched` afterwards
  (`retention=default`, `retention=default-after-matched`). `runnerPoolCapacity` stays 128 for those arms. Default
  mode labels are unchanged (`retention=default` everywhere). Previously the whole run was labelled
  `retention=matched`, which overstated the 1/128 arms.

PERF-3 (framelifecycle runner and validator):

- Control-block heap slice in `bytesPerOperation`: already implemented in PERF-3 fix round 1
  (`Summarize`: `(heapBytes - controlHeapBytes * frames / 8) / ops`, `-1` unless both slices are valid, raw kept in
  `rawBytesPerOperation`) and recomputed by the validator. Re-read, no change.
- Bracket anomaly counters at the first arm start: `StartNextArm` now, for arm 0, moves the unresolved-bracket,
  call-anomaly and harness-call anomaly counters into the new report field `armStartDiscardedEvents` and zeroes
  them. The existing first-frame-begin discard (`installFrameDiscardedEvents`) stays. Idle frames before the first
  arm can no longer fail the checklist.
- `harnessInertOutsideBrackets`: now partly measured. New per-frame counters check that the frame-begin, schedule and
  consume systems each ran exactly once per measured frame (`boundaryChecklist.harnessCallAnomalies`, additive
  field). The flag is the conjunction of that, the install-time structural checks (no Update/FixedUpdate/LateUpdate
  on the MonoBehaviour, expected system count) and `measuredFrames > 0`; `harnessEvidence` now separates the
  structural and measured parts. Work outside brackets stays covered by the whole-loop sanity bracket. Not renamed:
  the measured part is real, and the structural part is labelled as structural in the evidence string.
- `returnPassesInsideBrackets`: already measured in PERF-3 fix round 1 (queues empty at every cohort end, sanity
  bracket, positive median control-subtracted return-frame cost where a library defers returns). Re-read, no change.
- Validator (`validate-framelifecycle.py`):
  - `OSError` is caught (missing or unreadable file prints `FAIL` and exits 1).
  - Allocation availability is reported, never failed: no calibrated counter prints
    `allocation unavailable (counter <kind>; bytes/op reported as -1)`, a calibrated report with some slices at -1
    prints `partial (n of m slices unavailable ...)`. Previously only `unavailable`/`calibrated`. Strict recomputation
    of valid slices is unchanged.
  - UniTask denominator CV (sample stdev over mean of the UniTask ns/op samples) per arm, limit 5% at 128 and 10% at
    4096: new `uniCV%` column, `!` marker and one `INFO` line. Informational only; it never changes the exit code.
    Also in `--json-out` (`uniTaskCv`, `uniTaskCvLimit`, `uniTaskCvWithinLimit`).
  - Requires `boundaryChecklist.harnessCallAnomalies == 0` and the new `armStartDiscardedEvents` field, so it rejects
    reports from the pre-BENCH-INT runner. Prints both discarded-event counts on the PASS line.

## Verification

| Check | Command | Result | Artifact |
| --- | --- | --- | --- |
| Exact-reference proxy Roslyn compile | `python <scratchpad>/benchint/proxy_compile.py`: csc.dll of Unity 2022.3.62f2, `ONITY_TASK_BENCHMARKS`, `-nostdlib`, references are the PERF-3 worktree csproj HintPaths plus its `Onity.Unity`/`Onity.Core` ScriptAssemblies and its pinned UniTask 2.5.11 build (no `Onity.Reactive`, as in the asmdef) | `Onity.TaskBenchmarks` COMPILE_OK (0 errors, 8 warnings, all pre-existing CS0649 in `OnityTaskFullCycleProfileRunner`); `Onity.TaskBenchmarks.Editor` COMPILE_OK (0 errors, 107 warnings, all pre-existing CS0649 DTO fields). Warning counts equal the PERF-3 round 1 counts. | `Logs/BENCH-INT/*.txt`, `.rsp` (gitignored) |
| Validator self-test | `PYTHONDONTWRITEBYTECODE=1 python tools/benchmark-host/validate-framelifecycle.py <synthetic>.json --allow-subset` on synthetic reports (scratchpad, not evidence of a run) | Passes: calibrated, uncalibrated HeapDelta (prints unavailable), noisy UniTask (prints CV 25% INFO, exit 0). Fails as intended: wrong control-subtracted bytes, valid heap slice without a calibrated counter, harness system count mismatch, missing `harnessCallAnomalies`, missing file (`OSError`). | scratchpad only |
| Official Roslyn gate / Unity compile / Player run | not run | The benchmark assemblies are define-gated and need UniTask; this worktree has neither the define nor the package. The real Roslyn/Unity compile and the Player runs happen on the host in the next step. | - |

The proxy compile uses the PERF-3 worktree's `Onity.Unity.dll`, which predates PERF-1's `partial`/default change;
the benchmark sources use no API that PERF-1 touched, so this does not weaken the check, but the host compile is the
authoritative one.

## Integrator lines

- README (done in this packet, `Benchmarks/Tasks/README.md`): flow wording, the `-onityTaskBenchmarkRetention`
  paragraph with the three retention labels, the frame-lifecycle harness-call and discarded-event wording, and the
  validator's informational output.
- CHANGELOG, Benchmarks: "Benchmark harness measures at the library default `FlowExecutionContext=false`; flow-on is
  the explicitly labelled secondary arm. The launcher forwards `-onityTaskBenchmarkRetention default|matched` for
  `builderlifecycle` and rejects a missing or invalid value; matched runs label 1/128 arms `retention=default` or
  `retention=default-after-matched`. framelifecycle measures the per-frame harness call count and reports pre-arm
  discarded events; its validator reports allocation availability and the UniTask denominator CV."

## Open questions

1. Smoke suite now runs its non-flow cases at the default; not run here. If a smoke case implicitly relied on flow on,
   the host smoke run will show it.
2. Primary-suite scenarios 8-23 swap meaning by position; any host script that indexes them instead of using names
   needs the same swap (none found under `tools/` in this tree).
3. `flowExecutionContextDefault` in the primary report is now the value before the harness sets the flag. A host that
   pre-sets the flag to true would report true there while the primary arm still runs false. Add a host-side check
   that it reads false if the claim must prove the library default.
4. The first IL2CPP host run still has to confirm that `armStartDiscardedEvents` and `installFrameDiscardedEvents`
   are small, and the sanity-bracket noise risk noted in the PERF-3 note is unchanged.
