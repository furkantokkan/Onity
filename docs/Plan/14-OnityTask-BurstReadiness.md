# 14 - OnityTask Burst readiness feasibility

## Objective and decision

Test whether Burst can reduce the data-processing portion of frame/timer
readiness work enough to justify a later async integration. General async
superiority remains the product goal; this first probe cannot establish it.
The user approved execution of the Burst plan on 2026-09-30.

Existing default-flow manual-await cycles remain about 1.8-2.1 times UniTask.
That control contains no frame/timer scan to offload. Removing its gap would
require roughly 44-52% less total time; compiling numerical readiness work
does not remove managed state-machine, context, continuation or lifetime work.

The first packet is benchmark-only: synchronous Burst IJob.Run, avoiding
worker scheduling and function-pointer transition costs. Compare it with both
an ordinary managed-array loop and a non-Burst IJob.Run using NativeArrays.
Use the existing Burst 1.8.29 / Collections dependencies and current f2 host.

## Scope and ownership

- Source: the existing isolated onity-release-038 checkout on
  codex/onity-0.5-async-performance, base 5c09749 plus the retained local packet.
- Implementer owns the new task benchmark readiness runner and its script
  metadata, plus readiness suite wiring in the existing Player/BuildRunner.
- Root owns this plan, benchmark documentation, generated results/provenance
  and the existing isolated onity-builder-benchmark-4be50dc verification host.
- Production Runtime, public task contracts, packages, asmdefs, settings and
  serialized content are outside source-edit scope. Preserve all prior dirty
  work. No commit, push or release is authorized by this packet.
- The existing approved build tool owns only its temporary host scene:
  Assets/OnityBenchmarkTemp/OnityTaskBenchmarkPlayer.unity. Record and verify
  setting restoration and removal after each build. Do not touch other Editors.

## Probe contract

Use preallocated blittable records containing slot/generation, current
generation, frame/delay kind, start frame, remaining frames/seconds and flags.
Pass current frame and scaled/unscaled deltas as scalar snapshots. Scan in the
existing reverse order; ignore stale/nonpending records, prioritize cancellation,
skip same-frame advancement when required, then decrement frames or subtract
the selected float delta. Preserve strict floating-point behavior. Return
updated state and terminal slot/generation/outcome records in matching order.

Run frame and delay cohorts at 1/32/128/4096 records, with all pending,
every eighth ready, and all ready. Rotate variant order across eight measured
samples following two warmups. Warm compilation and prove actual Burst
execution using the existing BurstDiscard pattern, outside reported timings.

Report three boundaries: kernel call including IJob.Run overhead; kernel plus
generation-checked managed sink dispatch; and required staging, kernel, output
transfer and dispatch together. Report raw ticks, invocation count and per-record
conversion, explicit setup exclusions, native buffer capacity, and unsubtracted
timestamp/loop controls. Managed sinks are synthetic: they do not verify native
OnityTask lifetime or measure complete async execution. Never infer zero GC
allocation from a coarse heap-delta reading.

## Acceptance and advance gate

- Roslyn compiles each source change; fix errors before the next step.
- Independent final verification checks same-frame skip, scaled pause/unscaled
  advance, exact completion boundaries, cancellation/stale precedence, updated
  state, output order/count and generation rejection with explicit golden cases.
- Mono and IL2CPP Release Players complete all cases with identical outputs
  and correct managed/non-Burst/Burst execution proofs.
- Both backends retain repeated raw reports and source/binary hashes. Record
  other Unity CPU activity; contaminated timing cannot establish a clean gain.
  Before inspecting results, the feasibility inclusion screen is fixed: no
  task-owned builds/tests/Roslyn overlap; summed other-Unity CPU must be at most
  5% of report wall time and at most 0.5 CPU-seconds per report. Do not divide
  by core count. Missing CPU observations or a new untracked Unity process
  make noise status unknown. Keep all reports; allow at most one extra repeat
  after a flagged condition demonstrably changes. Passing screens only known
  Unity interference, not all host activity or causal Release performance.
- Restore host settings, remove the temporary build scene and preserve the
  exact prior runtime hashes. No full-suite repeat for benchmark-only changes.
- Advance only with a repeatable material benefit after required staging and
  managed result costs at 128 or 4096. A kernel-only gain is insufficient.

If this gate passes, the next bounded packet connects the same readiness engine
to Onity and UniTask consumers, measures the full default-flow lifecycle including
deferred returns, and verifies cancellation, reentry, reuse and native-memory
shutdown. If the gate fails, leave runtime unchanged and retain the measured limit.
The proposed eventual target is at least 5% lower complete-lifecycle time for the
predeclared general async workloads, with no material small-load, latency or
allocation regression. It is a target, not a promised outcome.

## Execution checkpoint

- Preflight: READY WITH WARNINGS. CLI 1.0.0-beta.11 and exact f2 source/host
  verified; live Pipeline/MCP identity is unavailable for this Unity 2022 route.
  Source and pinned headless CLI verification remain available.
- Domain workflow: unity-game-dev, supported by onity-develop,
  unity-optimization, unity-cli, implement-task and agent-orchestration.
- First packet complete: affected benchmark assemblies pass Roslyn; two
  Release build validations and six process repeats pass goldens and actual
  Burst proof. Four repeats pass the predeclared noise screen; two flagged
  repeats remain in the archive. Host settings and runtime hashes preserved.
  Independent report/observation/binary/settings audit passed; managed
  allocation remains unavailable, with no zero-allocation claim.
- Narrow advance gate passes for 4096 all-ready delays (combined Burst/C#
  ratio 0.862-0.894 Mono, 0.865-0.888 IL2CPP). General production adoption is
  NO-GO: pending/sparse IL2CPP cohorts regress and timer-free async is unaffected.
  Evidence: [readiness report](../assets/benchmarks/onitytask-readiness-performance-2026-09-30.md).
- Consumer packet implemented: benchmark-only managed/Burst x Onity/UniTask consumers, eight
  pending delay ticks then completion, forwarding updated state; 4096 cohort
  and 128 control. Count registration, every tick/transfer, consumption and
  actual deferred-return drain. Verify cancellation, generation reuse/reentry,
  single consumption and cleanup. Both Release backends passed these checks;
  Roslyn and independent source/binary/report/settings verification passed.
  All four initial timing repeats failed the frozen interference screen. A
  later quiet check permitted the final extra pair, one accepted process per
  backend; this does not establish replicated lifecycle improvement. Plan 15's
  audit found this suite excludes UniTask IL2CPP deferred-return CPU; its ratios
  are not matched complete lifecycles. Runtime integration remains NO-GO
  pending valid complete-cycle evidence, not because excluded ratios prove a regression.
  Evidence: [consumer lifecycle report](../assets/benchmarks/onitytask-readinesscycle-performance-2026-09-30.md).
- Next action: retain the frozen binaries and runtime. The conditional extra
  allowance is exhausted; general managed-async superiority remains unverified.
  [Plan 15](15-OnityTask-GeneralAsyncPerformance.md) uses a new matched return
  boundary and investigates managed completion costs.

### Consumer packet ownership and timing

- Sole implementer may add OnityTaskReadinessCycleBenchmarkRunner.cs and its
  script metadata under Benchmarks/Tasks/Runtime. It may expose internal-only
  helpers in the existing readiness runner to reuse the exact engine, and wire
  the readinesscycle suite in the existing Player/BuildRunner. All other source
  paths remain forbidden, including production Runtime and public task APIs.
- Count summed controlled synchronous elapsed phases: registration, eight pending
  ticks, terminal tick/callbacks, consumption, and two calls of the exact runtime
  deferred-return Drain bound before warmup. The same two drains run in every
  arm. Require an empty queue before/after, reject logged errors and residual
  work, and fail explicitly if reflection cannot bind. Validation and two later
  Unity frames are excluded; this is not natural PlayerLoop timer latency.
- Default flow remains true and runner retention remains the existing default.
  Record native retained memory; do not infer zero managed allocation. Native
  storage is disposed only after the last synchronous job. Use two warmups and
  eight samples per arm at 128/4096, with rotating arm order and repeated Players.
- Root alone owns the same temporary host build scene and restored settings,
  generated evidence and documentation. No commit, push or release.
