# Pending two-input `OnityTask.WhenAll` comparison (2026-09-24)

## Scope and verdict

Unity 2022.3.62f3 Editor/Mono, same benchmark runner and pinned UniTask 2.5.11 on both hosts. The candidate is product commit `824b2f7`; the baseline is `6f32e15`. The candidate's pooled coordinator reduces Onity allocations on the pending success path, but remains slower than UniTask in every measured pending success case. This is a prototype result, not evidence of general UniTask superiority or release readiness.

The table uses the independent warm timing repeat. Timing is the median of eight samples. Allocations are Unity Profiler `GC.Alloc` bytes per operation from eight samples; a second independent allocation pass reproduced the values. Inputs are constructed before the measured slice. The lifecycle slice includes scheduling, input completion, output `GetResult`, and cleanup. Tracker settings affect Onity only; UniTask stays at its default.

| Scenario | Baseline Onity ns/op | Candidate Onity ns/op | UniTask ns/op (candidate host) | Baseline Onity B/op | Candidate Onity B/op | UniTask B/op |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Pending success, schedule, tracker on | 4,285 | 3,471 | 772 | 1,408 | 1,040 | 160 |
| Pending success, schedule, tracker off | 2,206 | 1,360 | 739 | 544 | 176 | 160 |
| Pending success, output lifecycle, tracker on | 15,504 | 7,934 | 1,534 | 1,489 | 1,049 | 160 |
| Pending success, output lifecycle, tracker off | 13,734 | 2,425 | 1,541 | 625 | 176 | 160 |
| Pending cancel, output lifecycle, tracker on | 22,646 | 18,684 | 7,949 | 1,905 | 1,825 | 600 |
| Pending cancel, output lifecycle, tracker off | 21,159 | 12,458 | 7,894 | 1,041 | 952 | 600 |
| Both inputs completed, schedule, tracker on | 115 | 119 | 320 | 0 | 0 | 128 |

Onity pending success scheduling allocation fell 26.1% with the tracker on and 67.6% with it off. The output lifecycle fell about 29.6% and 71.8%, respectively. Completed-input scheduling remained zero allocation. The candidate still allocates 1,040 B/op versus UniTask's 160 B/op with tracking on, and 176 versus 160 with tracking off. Timing improvements do not cross the UniTask baseline for pending success.

## Invalid and unavailable measurements

**Fault timing and allocation rows in the raw JSON/CSV/Markdown reports are invalid.** The harness reused one `InvalidOperationException` instance across both libraries, all operations, and all samples. Repeated throws accumulated stack trace state; all eight fault allocation samples rose within each pass, and UniTask's companion fault measurements drifted with Onity's. Do not compare or cite those fault numbers. A corrected harness must create a fresh exception per operation in `PrepareBatch`, outside the measured slice, with separate instances for each library, then rerun both hosts.

The Editor/Mono direct per-thread GC counter returned `0` for its 65,536-byte positive control on both main and worker threads. Cold-call allocation, worker-thread allocation, and the 384-outstanding coordinator saturation diagnostic are therefore **unavailable**, not zero. The Unity Profiler main-thread allocation controls passed exactly `65,568` B positive and `0` B empty on both hosts and both passes. One candidate allocation invocation missed a Profiler marker; its retry succeeded, and the failed invocation contributes no data.

The first candidate timing process followed a fresh Library import and was globally slower for both Onity and UniTask; the independent warmed processes are the timing basis above. These Editor/Mono slices exclude input construction, Player/IL2CPP, frame-time, and device results. The output lifecycle slice is not an end-to-end scene workload.

## Evidence

- [Baseline primary report](onity-pending-whenall-baseline-6f32e15-2026-09-24.json) and [candidate primary report](onity-pending-whenall-candidate-824b2f7-2026-09-24.json)
- [Baseline repeat](onity-pending-whenall-baseline-6f32e15-repeat-2026-09-24.json) and [candidate repeat](onity-pending-whenall-candidate-824b2f7-repeat-2026-09-24.json)
- [Provenance](onity-pending-whenall-comparison-2026-09-24.provenance.json) records exact product commits, matching benchmark/config Git blobs, report hashes, calibration, and rejected runs. Each report has adjacent generated CSV and Markdown views.

The benchmark branch changes only benchmark assemblies/runner, its pinned comparison dependency, and these evidence files. It does not change Onity product runtime.
