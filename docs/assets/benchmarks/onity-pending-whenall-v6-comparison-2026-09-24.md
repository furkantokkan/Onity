# Pending two-input `OnityTask.WhenAll` benchmark, v6 (2026-09-24)

## Decision evidence

Unity 2022.3.62f3 Editor/Mono compared product baseline `6f32e15` with candidate runtime `855dce2`. Both used the same normalized runner Git blob `6fd82090`, benchmark configuration, and UniTask 2.5.11 pin. Two independent timing and two independent calibrated Profiler processes ran per revision. Each case has eight samples per process.

**The candidate fails the input-bridge fault allocation gate.** The Onity-only bridge-present fault diagnostic rose from **936.56 to 1,384.00 main-thread B/op** (+447.44 B/op, +47.8%) in both independent Profiler passes. It was faster in both timing sessions, but the repeated allocation increase prevents a no-regression claim. This result supports Astra's NO-GO decision for this candidate revision; it is not a release or UniTask-superiority result.

The table gives the median timing in each independent process and the main-thread Profiler marker allocation in both independent processes. Each timing sample covers 2,048 operations for success/completed or 256 for fault/cancel. Each allocation sample covers 128 operations. Source/input/exception construction is outside the measured slice. A lifecycle slice includes native `WhenAll` scheduling, input completion, output `GetResult`, and cleanup. **Main-thread marker B/op excludes later worker continuation/tracker allocation.**

| Scenario | Baseline median ns/op, passes 1/2 | Candidate median ns/op, passes 1/2 | Baseline main B/op, passes 1/2 | Candidate main B/op, passes 1/2 |
| --- | ---: | ---: | ---: | ---: |
| Pending success, tracker on | 14,805 / 14,145 | 8,380 / 8,365 | 1,488.93 / 1,488.56 | 1,048.28 / 1,048.65 |
| Pending success, tracker off | 14,035 / 12,437 | 2,269 / 2,235 | 624.56 / 624.56 | 176 / 176 |
| Pending fault, tracker on | 30,000 / 28,181 | 17,800 / 17,188 | 2,152.56 / 2,152.56 | 1,752.28 / 1,752.28 |
| Pending fault, tracker off | 27,515 / 25,030 | 10,704 / 10,674 | 1,288.93 / 1,288.56 | 880 / 880 |
| Pending cancel, tracker on | 21,765 / 21,899 | 14,512 / 14,571 | 1,904.56 / 1,904.93 | 1,464.28 / 1,464.65 |
| Pending cancel, tracker off | 24,059 / 23,020 | 7,994 / 7,963 | 1,040.56 / 1,040.56 | 592 / 592 |
| Both inputs completed, tracker on, scheduling only | 116 / 118 | 116 / 117 | 0 / 0 | 0 / 0 |
| Pending fault, input AsTask bridges present, tracker off, Onity only | 23,238 / 25,950 | 12,103 / 12,601 | **936.56 / 936.56** | **1,384 / 1,384** |

The bridge diagnostic creates both input `AsTask` bridges and a fresh exception per operation in `PrepareBatch`, before measurement. It completes input one successfully, faults input two, observes the native `WhenAll` output, then clears references. It never reads either input bridge's `.Exception`. No UniTask row is asserted as an equivalent of this Onity-specific diagnostic.

## Controls and limitations

- All four Profiler passes passed exactly 65,568 B positive, 0 B empty, and eight 0 B empty-harness controls. All eight raw allocation samples for the bridge diagnostic were 119,880 B baseline and 177,152 B candidate in both passes.
- The completed-input Onity path remained 0 B/op across all 32 Profiler samples (two revisions × two passes × eight samples); candidate median timing of 116/117 ns/op matches baseline 116/118 ns/op within observed variation.
- First observed pending scheduling in a warmed Editor process allocated 792 B baseline and 1,226 B candidate for Onity (one cold sample per process, repeated exactly). This is distinct from process startup or JIT cold.
- In the 384-outstanding burst, Onity baseline allocated 208,896 B in every sample. Candidate allocated 163,840 B on the first sample and 115,712 B on each later sample in both processes. These are raw scheduling totals with prepared inputs; they show pool saturation and warming separately.
- Direct worker-thread allocation is unavailable: `GC.GetAllocatedBytesForCurrentThread` failed its 65,568/0 B controls. The all-thread Profiler window passed main, empty, and ThreadPool positive controls in baseline pass 2 and both candidate passes. Baseline pass 1 failed the ThreadPool control (65,602 B instead of 65,568 B) and its all-thread samples are excluded. Valid all-thread tracker-on fault samples are 371,272 B for baseline pass 2 and alternating 263,424/265,544 B for both candidate passes; these include the tracked-batch completion barrier and are not directly comparable to main-thread marker B/op.
- The installed UniTask companion in candidate pass 2 measured 160 B/op for pending success, 1,666 B/op for fault, and 600 B/op for cancel; tracker-on Onity remained above those allocations. This benchmark does not establish that Onity is faster or leaner than UniTask in every case.
- The runner's raw file SHA-256 differs between hosts because the baseline file had mixed CRLF/LF after the visible patch and the candidate file had CRLF throughout. Git's normalized runner blob and the other five configuration blobs are identical; C# source tokens and compiled behavior are the same. [Provenance](onity-pending-whenall-v6-comparison-2026-09-24.provenance.json) records both raw hashes. No remeasurement was required for this line-ending difference.
- Editor/Mono only. No Player, IL2CPP, device, frame-time, or battery claim. Unity 6.5 Coin processes were present in the background, as in the previous calibrated run; no other Unity 2022 process overlapped these sessions.

## Raw evidence

[Baseline pass 1](onity-pending-whenall-v6-baseline-pass1-2026-09-24.json), [baseline pass 2](onity-pending-whenall-v6-baseline-pass2-2026-09-24.json), [candidate pass 1](onity-pending-whenall-v6-candidate-pass1-2026-09-24.json), and [candidate pass 2](onity-pending-whenall-v6-candidate-pass2-2026-09-24.json) contain every timing, main-thread allocation, first-call, burst, and valid all-thread sample. Logs remain outside the Unity project under `C:\Users\e-fur\.codex\bench-output\pending-v6-855dce2`.
