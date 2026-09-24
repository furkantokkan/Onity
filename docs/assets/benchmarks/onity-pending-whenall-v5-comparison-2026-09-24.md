# Corrected pending two-input `OnityTask.WhenAll` benchmark (2026-09-24)

## Result

Unity 2022.3.62f3 Editor/Mono compared product baseline `6f32e15` with pooled-coordinator candidate `824b2f7`, using the same benchmark Git blob and pinned UniTask 2.5.11. The corrected fault workload creates a distinct exception for every operation and library before its measured slice. Two independent timing processes and two independent Profiler processes ran per product revision; every case has eight samples per process.

**The candidate has a repeatable fault-allocation regression.** Main-thread fault lifecycle allocation rose from 2,152.56 to 3,306.28 B/op with tracking on (+53.6%) and from 1,288.56 to 2,434.00 B/op with tracking off (+88.9%). Both Profiler processes on each host reproduced the exact raw sample patterns. The candidate still trails UniTask on every measured pending lifecycle timing and allocation comparison. These results support keeping the candidate outside the release branch.

The table shows median timing from the second independent process and main-thread `GC.Alloc` bytes per operation from both identical Profiler passes. Input sources and fault exceptions were prepared outside the slice. Lifecycle timing includes scheduling, input completion, output result consumption, and cleanup. **The allocation column covers only the main-thread Profiler marker; it does not include worker tracker continuations.**

| Pending lifecycle | Baseline Onity ns/op | Candidate Onity ns/op | UniTask ns/op on candidate host | Baseline Onity main B/op | Candidate Onity main B/op | UniTask main B/op |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Success, tracker on | 16,279 | 7,963 | 1,494 | 1,488.56 | 1,048.28 | 160 |
| Success, tracker off | 13,096 | 2,265 | 1,542 | 624.56 | 176 | 160 |
| Fault, tracker on | 28,552 | 31,298 | 15,854 | 2,152.56 | 3,306.28 | 1,666 |
| Fault, tracker off | 29,633 | 26,131 | 15,911 | 1,288.56 | 2,434 | 1,666 |
| Cancel, tracker on | 21,461 | 18,224 | 7,724 | 1,904.56 | 1,824.28 | 600 |
| Cancel, tracker off | 21,938 | 12,278 | 7,863 | 1,040.56 | 952 | 600 |

The first independent timing process also showed candidate fault tracker-on slower (31,452 to 32,244 ns/op) and tracker-off faster (27,513 to 25,514 ns/op). Timing is less stable than allocation across processes; it should not be treated as Player or device performance.

## First call and above-capacity burst

Profiler controls passed exactly 65,568 B positive, 0 B empty, and eight 0 B empty-harness samples in all four reports. The first pending schedule was measured once per process after Editor startup and Profiler calibration, before the harness warmed `WhenAll`. It is a first observed call in a warmed Editor process, not startup or JIT cold. Both independent processes returned the same bytes:

| One pending schedule, tracker off | Baseline | Candidate | UniTask on both hosts |
| --- | ---: | ---: | ---: |
| First observed call, raw B | 792 | 1,226 | 2,032 |

The 384-outstanding scheduling cohort prepares inputs before the marker and completes all outputs afterward. It exceeds the candidate coordinator pool's 256-entry cap. Values are raw bytes for all 384 calls; each report contains eight samples, reproduced in the independent process.

| 384 pending outputs, tracker off | Baseline Onity | Candidate Onity | UniTask on both hosts |
| --- | ---: | ---: | ---: |
| First burst, raw B | 208,896 | 163,840 | 81,920 |
| Each of seven subsequent bursts, raw B | 208,896 | 115,712 | 61,440 |

The candidate improves warmed above-capacity scheduling over the baseline, while UniTask remains lower. The earlier [v4 report](onity-pending-whenall-comparison-2026-09-24.md) retains the valid completed-input and pending scheduling results; its fault rows remain invalid and are never combined with this corrected harness's fault values.

## Worker and tracker attribution

The all-thread Profiler window includes the batch's tracker completion barrier. Its first baseline and both candidate processes passed exact 65,568 B main and unregistered ThreadPool positive controls and 0 B empty controls. Their tracker-on lifecycle diagnostics were:

| Case | Baseline first-pass total / other-thread B/op | Candidate first-pass total / other-thread B/op | Candidate repeat total / other-thread B/op |
| --- | ---: | ---: | ---: |
| Success | 1,556.73 / 68.04 | 1,083.50 / 34.72 | 1,082.41 / 34.00 |
| Fault | 2,900.56 / 748 | 3,620.28 / 314 | 3,620.28 / 314 |
| Cancel | 2,044.56 / 140 | 1,858.28 / 34 | 1,858.28 / 34 |

**The baseline repeat all-thread data is unavailable.** Its ThreadPool positive control measured 65,602 B on other threads (and 88 B on main), so the harness rejected all its all-thread rows. The accepted windows are diagnostics: they include benchmark barrier work and any unrelated allocation inside the time window, and cannot prove exclusive tracker attribution. The direct per-thread GC counter returned zero for its 64 KiB positive control; its worker figures are unavailable rather than zero. The main-thread marker comparisons above have independent calibrated repeats.

## Evidence and limits

- [Baseline report](onity-pending-whenall-v5-baseline-6f32e15-2026-09-24.json), [candidate report](onity-pending-whenall-v5-candidate-824b2f7-2026-09-24.json), [baseline repeat](onity-pending-whenall-v5-baseline-6f32e15-repeat-2026-09-24.json), and [candidate repeat](onity-pending-whenall-v5-candidate-824b2f7-repeat-2026-09-24.json). Adjacent CSV and Markdown files are generated views; JSON holds all eight raw samples per case.
- [Provenance](onity-pending-whenall-v5-comparison-2026-09-24.provenance.json) records product revisions, matching benchmark/config Git blobs, file hashes, calibration, and the rejected all-thread pass.

This is an Editor/Mono microbenchmark with input construction excluded. It does not measure Player/IL2CPP, scene frame time, or devices. Three unrelated Unity 6000.5.6f1 Coin processes remained open; no Unity 2022 Editor/Player run overlapped the retained processes. The first baseline timing invocation wrote JSON/CSV under Unity's `Temp` directory, which were removed at shutdown; its log was retained and its values were not used. All retained reports were written outside either Unity project root and copied into this benchmark branch.
