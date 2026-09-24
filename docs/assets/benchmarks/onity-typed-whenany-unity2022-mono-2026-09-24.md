# Typed `OnityTask.WhenAny<T>` benchmark — Unity 2022 Editor/Mono

## Provenance and method

- Product revision: `37c309a950b2142c91eeb62ad12f0a8a585b1d76` (`OnityTask.WhenAny<int>`); harness revision: `7035c5f`; UniTask is pinned to `2e993ff18f28c931602a07292df0b0804eebef99`.
- Unity `2022.3.62f3`, Windows Editor/Mono. Two independent timing processes and two independent calibrated Profiler processes measured 12 cases (six outcomes with Onity task tracking on/off), three routes, and eight timing samples per case. Each timing sample uses 128 operations; the harness warms 10 batches and records 16 success or 2 failure timing batches.
- Routes: native typed Onity, UniTask's two-element `params` overload, and the same-revision `OnityAsync` Task-bridge migration path. Native Onity versus UniTask is the primary comparison; the Task bridge is migration evidence, not a product baseline.
- The two final reports match on product revision, UniTask pin, and every listed source/config SHA-256: `OnityAsync`, completion source, runner, menu, runtime and editor asmdefs, package manifest and lock, and `ProjectSettings`.
- Allocation measurements are Unity Profiler main-thread `GC.Alloc`. Both Profiler processes measured the positive control at exactly **65,568 B**, the empty control at **0 B**, and all eight empty harness samples at **0 B**. The native pending gate passed in both reports.

## Results

Times are median ns/operation from each independent timing process (run 1 / run 2). Allocation bytes are B/operation; the values matched across both calibrated Profiler runs. Tracker on/off changes OnityTask tracking only; UniTask retains its default tracking.

| Onity tracker | Scenario | Native Onity time (R1 / R2), B/op | UniTask time (R1 / R2), B/op | Result in both timing runs |
|---|---|---:|---:|---|
| On | Pending success schedule | 703.6 / 708.6, 424 | 802.1 / 826.3, 160 | Onity faster; allocates 264 B/op more |
| On | Pending success lifecycle | 1954.5 / 1925.3, 424 | 2026.0 / 2015.7, 160 | Onity faster; allocates 264 B/op more |
| On | Pending fault lifecycle | 12251.0 / 12736.5, 936 | 18436.3 / 18587.9, 1666 | Onity faster and allocates 730 B/op less |
| On | Pending cancel lifecycle | 10024.4 / 10067.0, 936 | 8515.8 / 8339.8, 600 | UniTask faster; Onity allocates 336 B/op more |
| On | Both completed, first wins | 1445.1 / 1460.1, 424 | 1140.4 / 1174.3, 128 | UniTask faster; Onity allocates 296 B/op more |
| On | Second completed first | 1738.9 / 1783.0, 424 | 1650.1 / 1707.3, 144 | UniTask faster; Onity allocates 280 B/op more |
| Off | Pending success schedule | 679.4 / 688.2, 424 | 784.4 / 838.5, 160 | Onity faster; allocates 264 B/op more |
| Off | Pending success lifecycle | 1970.5 / 1960.8, 424 | 2012.0 / 2015.4, 160 | Onity faster; allocates 264 B/op more |
| Off | Pending fault lifecycle | 11619.3 / 11897.9, 936 | 16895.1 / 17099.6, 1666 | Onity faster and allocates 730 B/op less |
| Off | Pending cancel lifecycle | 10143.8 / 10401.6, 936 | 8300.0 / 8649.0, 600 | UniTask faster; Onity allocates 336 B/op more |
| Off | Both completed, first wins | 1465.4 / 1522.2, 424 | 1142.5 / 1199.4, 128 | UniTask faster; Onity allocates 296 B/op more |
| Off | Second completed first | 1741.6 / 1707.2, 424 | 1643.5 / 1624.8, 144 | UniTask faster; Onity allocates 280 B/op more |

Across both repetitions, native Onity is faster for pending success scheduling, pending success lifecycle, and pending fault lifecycle. It also uses fewer bytes only in the pending fault case. UniTask is faster and allocates fewer bytes for pending cancellation and both completed-input controls. These measurements do not establish a general performance or allocation advantage.

## Measurement boundaries and artifacts

- Sources and fresh faults are prepared outside timing markers. Pending schedule markers stop before completion and consumption; those happen during cleanup outside the marker. Pending lifecycle markers include winner completion and observation, loser completion and validation, and reference cleanup.
- Completed-input cases measure lifecycle: both inputs are completed during preparation for “Both completed”; the loser is completed inside the marker for “Second completed first.”
- Legacy Task bridge tracker callbacks may execute off the measured main thread. First-observed calls precede warmups, but routes run sequentially, so these are not three process-cold measurements. Worker-thread allocations and Player/IL2CPP performance are unavailable.
- An earlier Profiler marker attempt did not produce attributable allocation data and is excluded. The cause of the initial missing marker remains unresolved; using the stronger collector for the calibrated runs does not prove that the original cause was fixed. The retained collector record is diagnostic only and is not part of the comparison: [collector diagnostic](onity-typed-whenany-unity2022-mono-2026-09-24-collector-diagnostic.json).

Raw final reports: [run 1](onity-typed-whenany-unity2022-mono-2026-09-24-run1.json) and [run 2](onity-typed-whenany-unity2022-mono-2026-09-24-run2.json).
