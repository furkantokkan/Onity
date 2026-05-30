# 04 - Performance Targets

This document defines the measurable bar Onity must hit. Performance is part
of the product, not an afterthought. Every PR that touches DI, Reactive, or
messaging hot paths must report measured numbers against these gates.

## 1. Hardware and Unity baseline

The headline numbers are gathered on:

- Unity 2022.3.62f3
- Windows 11 Pro 64-bit
- Editor mode, Mono runtime (IL2CPP gates are run separately on player
  builds)
- The single development machine that produced
  `Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-summary.md`

Numbers in this file are **relative**. A scenario that is "2x faster than
VContainer" must remain so on the same hardware after a change. Absolute
ns/op numbers will drift across machines and Unity versions; ratios should
not.

## 2. Benchmark methodology

### 2.1 DI

Runner: `Assets/Onity-Packages/Onity/Benchmarks/Editor/Scripts/OnityDiBenchmarkRunner.cs`

Each scenario is run for all three containers (Onity, VContainer, Zenject):

| Scenario | Workload |
|---|---|
| Resolve Singleton | 10,000 `Resolve<IService>()` calls against a singleton binding |
| Resolve Transient | 10,000 `Resolve<IService>()` calls against a transient binding |
| Resolve Combined | 10,000 mixed singleton + transient resolves |
| Resolve Complex | 10,000 resolves of a 6-level dependency graph |
| Prepare and Register Complex | Build container with the complex graph and finalize |

Each run:

- 512 warmup iterations
- 8 measured samples
- Median ns/op reported
- GC allocation per sample measured via
  `GC.GetTotalAllocatedBytes(precise: true)` deltas

Results land in:

- `Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-latest.json`
- `Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-latest.md`
- `Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-summary.md`
- A timestamped JSON for historical comparison

### 2.2 Reactive

A new runner is added in Phase 2:
`Assets/Onity-Packages/Onity/Benchmarks/Editor/Scripts/OnityReactiveBenchmarkRunner.cs`

Scenarios:

| Scenario | Workload |
|---|---|
| Subject OnNext | 10,000 emissions to a Subject with 100 subscribers |
| ReactiveProperty SetValue | 10,000 assignments to a ReactiveProperty with 1 subscriber |
| ReactiveProperty SetValue Deduped | Same with 100 redundant writes per change |
| Where->Select->Subscribe chain | 10,000 emissions through a 3-operator chain |
| EveryUpdate frame loop | 1,000 simulated frames with 50 subscribers |
| MessageBroker Publish | 10,000 publish calls with 10 subscribers |
| EventHub Listen + Publish | 10,000 listen + publish round-trips |

Output format mirrors DI.

### 2.3 Allocation gates

Steady-state allocation per call must be **0 bytes** for:

- `OnityContainer.Resolve<T>()` (singleton or transient)
- `Subject<T>.OnNext(...)`
- `ReactiveProperty<T>.Value = ...` (when value actually changes)
- `IPublisher<T>.Publish(...)`
- `EveryUpdate()` per-frame tick

Allocation per **subscribe**, **build**, or **register** is allowed but
should be minimized.

### 2.4 Hardware drift handling

A single machine produces baseline numbers. To avoid false alarms when a CI
agent rotates:

- Each run records `Environment.MachineName`, Unity version, OS version.
- A baseline file
  `Assets/Onity-Packages/Onity/Benchmarks/Results/baseline-<machine>.json`
  stores the reference ns/op for that machine.
- Regression detection compares against that machine's baseline, not a
  global one.

## 3. DI performance gates

### 3.1 Headline gates (Phase 1 exit)

| Scenario | Today (Onity) | Today (VContainer) | Phase 1 target | Stretch |
|---|---:|---:|---:|---:|
| Resolve Singleton (ns/op) | 175 | 187 | <= 150 | <= 130 |
| Resolve Transient (ns/op) | 2,088 | 1,801 | <= 1,500 | <= 1,200 |
| Resolve Combined (ns/op) | 2,163 | 1,833 | <= 1,550 | <= 1,250 |
| Resolve Complex (ns/op) | 52,386 | 40,916 | <= 35,000 | <= 28,000 |
| Prepare and Register Complex (ns/op) | 12,492 | 143,123 | <= 15,000 | <= 12,000 |
| Resolve allocation per sample (B) | 0 | 0 | 0 | 0 |

The gates ratchet:

- A PR that improves a number sets the new baseline.
- A PR that regresses a number by more than **5%** without a documented
  reason fails CI.

### 3.2 IL2CPP variant

Phase 1 ships an IL2CPP build of `OnityDiBenchmarkRunner` to confirm:

- `Expression.Compile()` activators do not throw at startup on IL2CPP.
- Resolve Transient is still <= 1.5x slower than the Mono baseline.

If Expression-based activators hit a hard wall on IL2CPP, Phase 2 brings in
source-generated activators (see `02-DI-Design.md` section 3.4).

## 4. Reactive performance gates

### 4.1 Headline gates (Phase 2 exit)

Baseline numbers will be produced during Phase 2 implementation. Targets are
qualitative until then:

| Scenario | Target |
|---|---|
| Subject OnNext (100 subs, ns/op) | 0 alloc, faster than `event Action<T>` invocation |
| ReactiveProperty SetValue (no-op change) | 0 alloc, near-zero cost (just comparer call) |
| ReactiveProperty SetValue (real change) | 0 alloc, single subject pump |
| 3-operator chain emission | 0 alloc, < 1.5x bare-Subject cost |
| EveryUpdate frame loop, 50 subs | 0 alloc per frame |
| MessageBroker.Publish, 10 subs | 0 alloc, < `event Action<T>` + dictionary lookup |

Phase 2 ships when these numbers exist in
`Assets/Onity-Packages/Onity/Benchmarks/Results/reactive-benchmark-summary.md`
and every gate is met.

### 4.2 Threading mode targets

`OnityUnityThreadMode.JobMultiThread` is judged against
`SingleThread` for the same scenario:

- Batch size >= 64 emissions: JobMultiThread should be at least 1.2x faster.
- Batch size < 32: JobMultiThread is allowed to be slower; the user picked
  the wrong mode.

`BurstJobMultiThread` is judged against `JobMultiThread`:

- Compute-heavy operators (Aggregate, Scan over POD types): expect >= 2x.
- Reference-type pipelines: expect <= 1.1x because Burst cannot help.

## 5. Compile and import time gates

These do not have hard gates yet but are tracked:

| Metric | Target |
|---|---|
| Cold full project compile (clean Library) | < 90 seconds on baseline machine |
| Incremental compile after one-file edit | < 5 seconds |
| Onity asmdef compile in isolation | < 2 seconds each |

If a refactor pushes these past target, the refactor is rejected.

## 6. Reporting format

Every benchmark PR description includes a markdown table of the form:

```
| Scenario | Before | After | Delta |
|---|---:|---:|---:|
| Resolve Transient | 2088 | 1320 | -36.78% |
```

PRs without numbers for the affected scenarios are not merged.

## 7. CI integration plan

Phase 1 deliverable:

- A GitHub Actions workflow that:
  - Restores Unity 2022.3.62f3
  - Runs `dotnet build` on all Onity asmdefs
  - Runs `Unity.exe -batchmode -nographics -executeMethod
    Onity.Editor.Benchmarks.OnityDiBenchmarkRunner.RunBenchmarksFromCommandLine`
  - Compares the resulting `di-benchmark-latest.json` against the baseline
    file for the runner
  - Fails the build if any gate regresses > 5%

Phase 2 extends the workflow to run the reactive benchmark runner.

## 8. References

- DI runner: `Assets/Onity-Packages/Onity/Benchmarks/Editor/Scripts/OnityDiBenchmarkRunner.cs`
- Results folder: `Assets/Onity-Packages/Onity/Benchmarks/Results/`
- Latest summary: `Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-summary.md`
- Engineering perf rules: `Assets/Onity-Packages/Onity/ENGINEERING.md` (section 11)
