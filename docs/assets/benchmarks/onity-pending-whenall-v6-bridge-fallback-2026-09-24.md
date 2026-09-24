# Pending two-input `OnityTask.WhenAll` bridge fallback benchmark (2026-09-24)

Unity 2022.3.62f3 Editor/Mono compared product baseline `6f32e15` with the prebridged-input fallback candidate `bc4e469`. The eight-case v6 harness and five other benchmark/configuration Git blobs were identical on both hosts; UniTask was pinned to 2.5.11. Two independent timing and two independently calibrated Profiler sessions ran per product revision. Each case has eight samples per session.

The earlier `855dce2` candidate's [bridge-present fault allocation regression](onity-pending-whenall-v6-comparison-2026-09-24.md) is absent here: **baseline and `bc4e469` both allocated 936.56 main-thread B/op in both bridge-present fault Profiler sessions**. All 32 raw samples were exactly 119,880 B per 128 operations. Bridge-present median timing was 27,285/25,486 ns/op baseline and 26,393/27,064 ns/op candidate across independent sessions, with no repeatable direction. The completed-input Onity schedule stayed at 0 B/op in every sample; median timing was 119/117 ns/op baseline and 119/130 ns/op candidate, so these sessions do not show a repeatable completed-path regression.

The table gives median timing for each independent process and main-thread Profiler marker B/op for both independent Profiler processes. Timing samples cover 2,048 operations for success/completed and 256 for fault/cancel; allocation samples cover 128. Inputs and fresh per-operation fault exceptions are prepared outside the measured slice. Lifecycle includes scheduling, input completion, output observation, and cleanup. **Main-thread marker B/op excludes later worker tracker/continuation allocation.**

| Scenario | Baseline median ns/op, passes 1/2 | Candidate median ns/op, passes 1/2 | Baseline main B/op, passes 1/2 | Candidate main B/op, passes 1/2 |
| --- | ---: | ---: | ---: | ---: |
| Pending success, tracker on | 14,105 / 14,595 | 8,102 / 8,116 | 1,488.56 / 1,488.93 | 1,048.28 / 1,048.28 |
| Pending success, tracker off | 12,138 / 13,163 | 2,275 / 2,209 | 624.56 / 624.56 | 176 / 176 |
| Pending fault, tracker on | 28,783 / 28,038 | 17,677 / 16,944 | 2,152.56 / 2,152.56 | 1,752.28 / 1,752.28 |
| Pending fault, tracker off | 26,125 / 28,333 | 10,860 / 10,450 | 1,288.56 / 1,288.56 | 880 / 880 |
| Pending cancel, tracker on | 20,561 / 22,780 | 14,221 / 14,376 | 1,904.56 / 1,904.56 | 1,464.28 / 1,464.28 |
| Pending cancel, tracker off | 20,503 / 20,085 | 8,042 / 7,982 | 1,040.56 / 1,040.56 | 592 / 592 |
| Both inputs completed, tracker on, scheduling only | 119 / 117 | 119 / 130 | 0 / 0 | 0 / 0 |
| Pending fault, input AsTask bridges present, tracker off, Onity only | 27,285 / 25,486 | 26,393 / 27,064 | **936.56 / 936.56** | **936.56 / 936.56** |

The Onity-only bridge diagnostic creates both input `AsTask` bridges and a distinct fault exception per operation before the measured slice. It calls native `WhenAll`, completes input one, faults input two, observes the output, and cleans up. The harness does not read either input bridge's `.Exception`, and it does not claim a UniTask-equivalent bridge row. The `bc4e469` fallback reuses the materialized task path for this case.

## Cold, saturation, and attribution

- Main-thread Profiler calibration passed exactly 65,568 B positive, 0 B empty, and eight 0 B empty-harness samples in all four sessions. Direct worker-thread allocation remained unavailable because its positive control returned 0 B.
- First observed pending scheduling in a warmed Editor process allocated 792 B baseline and 1,226 B candidate in both sessions. This **434 B first-call increase** is separate from the steady pending-lifecycle results and is not process-startup or JIT-cold evidence.
- With 384 outstanding pending outputs, Onity baseline allocated 208,896 B for every scheduling sample. Candidate allocated 163,840 B in each session's first sample and 115,712 B in each later sample. UniTask's companion burst was 81,920 B first and 61,440 B later on both hosts. These are raw totals for prepared inputs, with cold pool saturation separated from warmed samples.
- The all-thread Profiler window passed its main, empty, and ThreadPool controls in baseline pass 1 and candidate pass 1. Both pass 2 windows failed the ThreadPool positive control at 65,602 B instead of 65,568 B; their all-thread rows are unavailable. Valid tracker-on fault all-thread totals were 371,272 B per 128 operations baseline and alternating 263,424/265,544 B candidate. These include a tracked-batch completion barrier and must not be equated with main-thread B/op.
- In candidate pass 2, UniTask companion allocations were 160 B/op pending success, 1,666 B/op fault, and 600 B/op cancel. Tracker-on Onity exceeded those values and its lifecycle timing remained slower in these cases. The benchmark does not establish all-around UniTask superiority for Onity.
- Raw runner SHA-256 differs across hosts solely from mixed CRLF/LF baseline versus CRLF candidate files. Their normalized runner Git blob `6fd82090` and all five other configuration blobs match. [Provenance](onity-pending-whenall-v6-bridge-fallback-2026-09-24.provenance.json) records both raw hashes.
- Editor/Mono only. No Player, IL2CPP, device, frame-time, or battery claim. No other Unity 2022 process overlapped these sessions; Unity 6.5 Coin processes were ambient.

## Raw evidence

[Baseline pass 1](onity-pending-whenall-v6-bridge-fallback-baseline-pass1-2026-09-24.json), [baseline pass 2](onity-pending-whenall-v6-bridge-fallback-baseline-pass2-2026-09-24.json), [candidate pass 1](onity-pending-whenall-v6-bridge-fallback-candidate-pass1-2026-09-24.json), and [candidate pass 2](onity-pending-whenall-v6-bridge-fallback-candidate-pass2-2026-09-24.json) preserve every timing, main-thread allocation, first-call, burst, and valid all-thread sample. Editor logs remain outside the Unity projects under `C:\Users\e-fur\.codex\bench-output\pending-v6-bc4e469`.
