# Typed `WhenAny<int>` bounded-pool experiment — rejected candidate

## Decision

**NO-GO for the pooled candidate** (`2f995d947d9fa3f9d5950e3295bec5b1de3a7210`). Keep the accepted `8fa63aca3596e263b6d44dde5469381e7b70a112` revision for release. The candidate reduces steady-state main-thread allocation, but repeatable completed-input and tracker-off lifecycle timing regressions do not meet the release bar. This experiment does not establish general superiority over UniTask or VContainer.

## Provenance and controls

- Harness branch revision: `ecaaa4a21fb54fa0e81ff5a54be8bd816ec805a5` (`Measure typed WhenAny pool saturation`). Baseline product revision: `8fa63aca3596e263b6d44dde5469381e7b70a112`; candidate product revision: `2f995d947d9fa3f9d5950e3295bec5b1de3a7210`. UniTask pin: `2e993ff18f28c931602a07292df0b0804eebef99`.
- Windows Unity `2022.3.62f3` Editor/Mono, main thread. Two independent baseline/candidate timing pairs were collected in B1 → C1 → C2 → B2 order, followed by calibrated Profiler allocation captures for each report. The timing fields in each profiled report retain its corresponding timing run.
- Every report has the same completion-source, runner, menu, runtime/editor asmdef, manifest, lock, and project-settings SHA-256. The only reported source hash difference is `OnityAsync`: baseline `f276cc8e2205384ffd96551007cd3de26263ad394cee5847308d3fb25d679432`; candidate `49fa7c66345535644aa4ac1577e5a8d6fb7a5b10de7822d9983f24d7602653e7`. Runner SHA-256 is `f6c9204b8f45cf5c1a90fa98c89bef95077c0065cb8f1a72c2bc595036542e20` in all eight reports.
- All four Profiler captures passed the native pending-source gate and measured the positive allocation control at **65,568 B**, the empty control at **0 B**, and eight empty 128-operation and eight empty 384-operation samples at **0 B**. Timing and allocation were measured separately; the allocation counter is Unity Profiler main-thread `GC.Alloc`.
- Each ordinary case has eight samples of 128 operations after ten warmup batches. Success timing uses 16 batches per sample; failure timing uses two. The 384-outstanding scheduling slice has one warm-saturation burst followed by eight samples. The tracker switch changes Onity tracking only.

## Paired results

Values below are native Onity median ns/operation from the two timing runs, baseline → candidate. Allocation values are B/operation from the calibrated captures and match across both runs.

| Case | B1 → C1 | B2 → C2 | Allocation B → C |
|---|---:|---:|---:|
| Pending success schedule, tracker on | 721.0 → 681.8 | 735.9 → 698.8 | 408 → 0 |
| Pending success lifecycle, tracker off | 1984.4 → 2237.1 (+12.7%) | 1978.2 → 2223.0 (+12.4%) | 408 → 0 |
| Both completed, first wins, tracker on | 1332.0 → 1746.8 (+31.1%) | 1355.1 → 1859.7 (+37.2%) | 152 → 0 |
| Both completed, first wins, tracker off | 1346.5 → 1715.5 (+27.4%) | 1365.3 → 1718.5 (+25.9%) | 152 → 0 |
| Second completed first, tracker on | 1703.3 → 2004.1 (+17.7%) | 1719.1 → 2032.6 (+18.2%) | 280 → 0 |
| Second completed first, tracker off | 1691.6 → 1995.5 (+18.0%) | 1762.1 → 2075.5 (+17.8%) | 280 → 0 |
| Pending fault or cancellation lifecycle, either tracker state | See raw reports | See raw reports | 920 → 512 |
| 384 outstanding pending schedule, tracker on | 735.3 → 771.0 | 723.3 → 838.3 | 408 → 144 |

The first-observed native call allocated **920 → 1,416 B** (baseline → candidate) in both calibrated pairs. This call precedes warmups but is not process-cold for every library route. The 384-outstanding row is scheduling only: completion, observation, and cleanup happen outside its marker. A bounded pool can allocate once outstanding work exceeds its capacity. Worker allocations and Player/IL2CPP performance were not measured.

The complete raw reports retain every scenario, route, timing sample, allocation sample, and source/config identity:

- Baseline B1: [timing](onity-typed-whenany-pool-2026-09-24-b1-timing.json), [profiled](onity-typed-whenany-pool-2026-09-24-b1-profiled.json).
- Candidate C1: [timing](onity-typed-whenany-pool-2026-09-24-c1-timing.json), [profiled](onity-typed-whenany-pool-2026-09-24-c1-profiled.json).
- Candidate C2: [timing](onity-typed-whenany-pool-2026-09-24-c2-timing.json), [profiled](onity-typed-whenany-pool-2026-09-24-c2-profiled.json).
- Baseline B2: [timing](onity-typed-whenany-pool-2026-09-24-b2-timing.json), [profiled](onity-typed-whenany-pool-2026-09-24-b2-profiled.json).
