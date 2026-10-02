# 15 - OnityTask general async performance

## Objective and accepted execution

The user requested a plan followed by subagent implementation on 2026-09-30.
Target general async performance, with ExecutionContext flow enabled and runner
retention 128. Success means at least 5% lower complete-cycle elapsed time than
pinned UniTask 2.5.11 on every predeclared default-flow workload, without a
material latency, allocation or behavior regression. Flow-off is diagnostic.
Universal superiority across arbitrary workloads is not promised.

The prior timer-free logical cycle still shows a substantial gap. Burst only
accelerates compatible numeric work; it does not remove managed builder,
context, continuation and lifetime costs. Plan 14 remains a separate probe.
Discarded fused/based dispatch experiments are not fresh candidates.

## Sequence

1. Add a benchmark-only `builderlifecycle` suite before changing runtime.
   Compare actual typed/untyped async consumers of the same preallocated gates,
   with 1/128/4096 operations and 1/4 sequential suspensions. Include registration,
   every continuation, one consumption and exactly two common deferred-return
   passes before the next cohort. Each pass invokes Onity's production Drain
   and UniTask's exact LastPostLateUpdate ContinuationQueue.Run, after all
   producing stacks unwind. Freeze baseline source and Release binaries.
2. Implement only a cancellation-registration disposal guard in the two source
   bases' `TrySetStatus` methods. A real registration is assigned only for
   `CanBeCanceled` tokens; uncancelable completion can skip disposal and its
   immediate default write. Preserve its position before continuation exchange
   and status publication. Keep final reference cleanup and synchronization.
3. Run focused final verification and matched Release comparisons. Retain the
   change only if its mechanism and complete-cycle benefit are corroborated;
   restore the exact frozen runtime if inconclusive. Retain excluded raw evidence.
4. After refreshed attribution, investigate IL2CPP deferred-return enqueue in a
   separate packet. A dedicated return queue needs proof of worker publication,
   drain boundaries, reentry, depth checks, session retirement and shutdown.
   This optimization alone cannot close the Mono gap.
5. Reassess direct successful native consumption and cold cancellation/bridge
   payload costs only after those measurements. Keep the version-retiring CAS,
   registration pins, bridge reservation and ambient-context isolation. A broad
   runner rewrite needs a concrete design and new verification scope first.

## Ownership and preservation

- Source: existing `onity-release-038/Onity`, base `5c09749` plus retained dirty
  work. Preserve that packet and the primary checkout's unrelated changes.
- Decision owner: architecture/performance review, read-only. Sole implementer:
  new `Benchmarks/Tasks/Runtime/OnityTaskBuilderLifecycleBenchmarkRunner.cs` and
  its new `.meta`, existing task PlayerRunner/Editor BuildRunner suite wiring;
  after baseline capture, only the two disposal blocks in `OnityAsync.cs`.
- Sequential verifier: focused existing source-state/cancellation/builder test
  files under `Tests/EditMode/Scripts`; production writes remain forbidden.
- Root: this plan, benchmark docs/changelogs, generated evidence and the isolated
  f2 headless host. The approved build tool owns only the temporary scene
  `Assets/OnityBenchmarkTemp/OnityTaskBenchmarkPlayer.unity`; verify its removal
  and all original setting hashes after every build.
- No dependency, asmdef, public API, default, user scene or other Editor change.
  No commit, push or release in this packet. One source writer at a time.

## Frozen measurement contract

- Unity 2022.3.62f2, Windows x64 Mono and IL2CPP non-development Release Players.
  Same harness/source hashes and UniTask revision for baseline and candidate.
- Two warmups and eight samples per library/case, rotating library order.
  Invocations per sample are fixed at `max(1, 4096 / concurrency)` before results.
  Every invocation registers, completes, consumes and drains its cohort before
  reuse. Actual builder allocation/reset/capture, callbacks and returns stay timed.
- Report raw phase ticks and their sum, normalized by count times invocations.
  These are controlled synchronous elapsed slices, not thread CPU counters or
  natural PlayerLoop latency. Gate/counter reset, preallocated arrays, reflection,
  validation and two later safety frames are excluded equally. Matched timestamp
  and array-loop controls retain identical bracket counts without subtraction.
  Count 1 is harness-sensitive diagnostic evidence, not sole adoption evidence.
- Bind both return delegates before warmup; reject missing bindings and logged
  queue errors. Check both queue identities, idle/empty state before and after
  each invocation and zero work between passes. Before the first IL2CPP pass,
  the active library queue must contain the cohort count and the other zero;
  Mono both are zero. Do not run unrelated UniTask PlayerLoop/timer work.
- No AsyncLocal seeded by the harness; semantics have separate focused tests.
  Record default/effective flow, tracker state and both libraries' pool policy.
  Unavailable allocation data is -1 and cannot establish zero GC allocation.
- Three paired process rounds per backend/revision, reversing revision/backend
  order between rounds. All builds/Roslyn/tests finish before retained timing.
  Screen summed other-Unity CPU at <=5% report wall AND <=0.5 CPU-seconds,
  without core normalization; missing/new observations are unknown. Retain all.
  One replacement paired round is allowed only after demonstrated quiet if an
  original round is flagged; stop with inconclusive timing if still insufficient.
- Packet retention requires consistent raw Onity improvement beyond repeat
  spread at default 128, corroborated in completion, with no material adverse
  typed/untyped, four-suspension or 4096 result. A >=2% change is an exploratory
  screen, not proof alone. Check controls and UniTask denominator stability.
  This packet screen is distinct from the overall >=5% UniTask target.

## Verification and checkpoint

- Preflight: READY WITH WARNINGS. Exact f2 source/host and CLI 1.0.0-beta.11
  verified. Unity 2022 live Pipeline identity is unavailable; pinned headless
  CLI builds/tests and Roslyn remain the verification route.
- Roslyn checks each development change. Sequential final tests cover success,
  fault, cancellation token identity, real registration disposal, late callback
  after reuse, AsTask/Preserve/native consumption, two-suspension context isolation
  and Player smoke/deferred returns. No routine full-suite repeats.
- Benchmark packet complete: Roslyn passed 19 benchmark sources; reviewed runner
  SHA256 `AF5A62CDBC42BFDFEF0E8AF3727E360CCC66FE9C4C7718943C29AEBFA7B0E6DE`.
  Mono and IL2CPP Release baseline Players each passed all 48 arms and eight
  multi-suspension goldens with both return queues counted. All 660 staged files
  matched; baseline binary manifest records 213 entries. Settings restored and
  the temporary scene removed after both builds. Startup timings are validation,
  not adoption evidence. Untimed reflection validation can perturb cache/GC.
- Baseline runtime is frozen at OnityAsync.cs SHA256
  `3D66BDB8C0A0F27DFA75649283C75AB9E0289866F9D61DDDA80A99B74685128F`.
  The bounded two-site completion guard is implemented at SHA256
  `01BF0C57CAC4373BA9A9228B5BD5397380881CED9E265E29CBAAC585651036C1`.
  Exact-file audit confirmed only those guards; Roslyn passed six affected
  assemblies. The candidate passed 94 focused Release EditMode and one Mono
  Editor PlayMode case, with all 24 added regressions included. Twelve matched
  Release processes passed the frozen screen, selecting rounds 1-3 per backend.
- Completion guard NO-GO: required consistent beyond-spread benefit was not
  established across shapes/depths. Both guards were reverted to exact frozen
  bytes; Roslyn passed and final retained source-state tests passed 40/40,
  including all 24 additions. Keep harness/tests/evidence. General superiority
  remains unmet. See the [paired lifecycle report](../assets/benchmarks/onitytask-builderlifecycle-performance-2026-09-30.md).
- Next packet: independently scoped main-thread IL2CPP return buffering,
  preserving worker synchronization and lifecycle boundaries. Refresh aggregate
  queue diagnostics and freeze a new matched baseline before runtime changes.

## Return-buffer packet contract

Proceed under the user's instruction to plan and implement with subagents.
The completion packet is documented and its final retained tests passed.
The measured IL2CPP consumption/return slice justifies a bounded experiment;
it does not predict the removable fraction or a Mono improvement.

1. The sole benchmark writer changes only the lifecycle runner's Onity pending
   binding to a cached existing `PendingCount` getter and its binding description.
   Keep every timed boundary, common drain, proof, workload and repeat policy.
   Release file ownership after Roslyn; root freezes fresh baseline binaries.
2. The sole feature writer then owns only `OnityTaskThreadSwitch.cs` and
   `OnityAsyncStateMachineRunner.cs`. Add internal `EnqueueDeferredReturn(Action)`
   for the two IL2CPP release and two depth-retry sites. Main-thread returns use
   lazy Action-array double buffers; worker returns use existing locked Enqueue.
   Ordinary Enqueue and worker/thread-switch FIFO remain unchanged.
3. Drain snapshots both queues before callbacks; fixed batches, exception
   isolation and the existing reentry gate remain. New/retried work waits for
   the next drain. Keep cached delegates, MoveNext depth, source retirement,
   bridges, context, shutdown/drop and missing-runner wakeup behavior. Clear
   both pending buffers at session boundaries, preserving active snapshots.
   Diagnostics and runner recreation count both queues; publish the main count
   with Volatile for worker readers. No extra arrays on unused Mono paths.
4. After feature ownership releases, the verifier owns only existing
   `OnityTaskThreadSwitchEditModeTests.cs`,
   `OnityTaskThreadSwitchEditorLifecycleTests.cs` and
   `OnityTaskAsyncBuilderPlayModeTests.cs`. Cover only missing main/worker/mixed
   producers, growth, aggregate counts, FIFO, both directions of cross-queue
   reentry, recursive drain, thrown callbacks, session/shutdown and missing
   runner behavior. Root runs final focused tests once and the existing real
   IL2CPP smoke return/reentry/held-worker cases.
   Initial test writes are limited to the thread-switch EditMode file; reuse
   existing lifecycle and Player cases. Root owns temporary changes to the
   isolated host's `ProjectSettings/EditorSettings.asset` made by the existing
   lifecycle fixture and verifies its original hash after the run.
5. Root owns fresh isolated builds, profiles and fixed three paired Release
   rounds, with the same screen and conditional one-replacement allowance.
   Use existing full-cycle Development profiles for monitor/GC.Alloc attribution,
   after warmup of both buffers; these timings cannot establish Release benefit.
   Calibrated counters remain unavailable if unsupported. Record lazy/initial
   and actual retained queue capacities, including mixed worker storage.

Retention requires removed per-item main-return monitors, preserved worker
synchronization, no new steady-state allocation callsites, lower raw
consumption-plus-return cost and a >=2% default-128 full-cycle improvement beyond
repeat spread. Protect typed/untyped, four-suspension, 4096 and Mono cases.
If unproven, restore only these two files' exact pre-packet runtime snapshots
and the new queue-specific test packet if it requires the discarded internal
entry point. Keep its source snapshot with the experimental evidence; preserve
the 24 independently retained source-state regressions.
No public API, dependency, default, user asset, commit, push or release change.

## Day-end checkpoint - 2026-09-30

The user requested finishing today's work before return-buffer implementation.
All agent source ownership is released; no return-buffer runtime or test change
was started. The only retained preparation change is the cached aggregate
`PendingCount` getter in the lifecycle benchmark, SHA256
`C2E7B6A68AD0D72CCFDB03AF95E618B494AD0D54C59EF4B3FED2129436498FE9`.
Roslyn passed both benchmark assemblies; stdout and timestamps are saved.

- Source workspace: `onity-release-038/Onity`, existing
  `codex/onity-0.5-async-performance`, base `5c09749` plus retained local work.
  Verification host: `onity-builder-benchmark-4be50dc/Onity`, Unity f2.
  Primary checkout remains untouched; no VCS mutation was made.
- Both new Release baselines passed all 48 arms and eight goldens with the
  updated getter. Mono GUID `5ffaf32e3fa24d52a5033e930fc016cd`; IL2CPP GUID
  `40b6fd18888644cdb189f48a90f71d75`. All 660 staged files match source.
  No candidate or paired timing runs exist for this return-buffer packet.
- The baseline Development IL2CPP 128/flow-on/drain capture and export passed.
  Each library completed 256 operations after two warmup batches. Onity's
  Main Thread Consume has 256 `ReleaseSource -> Enqueue -> Monitor.Enter`
  samples; its lifecycle has six dispatcher Drain monitor samples. This is
  mechanism attribution, not Release speed. No GC.Alloc events were observed;
  exported allocated bytes remain unavailable (-1), not a calibrated zero.
- Runtime snapshots are unchanged: ThreadSwitch SHA256
  `CBB4B6666538DB2AE9F0CB206192F8D450BBDD579294956A6F680F2C2FE1C8CA`;
  StateMachineRunner SHA256
  `8D26126887CEE171C85086AF2972C008F659BDDD3357A96DA6F56C0F30E0F11F`.
  OnityAsync remains the exact frozen `3D66...5128F` runtime.
- The diagnostic build restored compiler arguments but left an empty
  Standalone dictionary entry in ProjectSettings. After the Editor exited,
  the exact original empty dictionary representation was restored. All four
  original setting hashes match; the temporary scene is absent.
- [Prepared baseline evidence](../assets/benchmarks/onitytask-returnbuffer-evidence-2026-09-30/baseline-checkpoint.zip),
  SHA256 `9024249834FB8CAE305839A36AF329D152B85CCA938082B5FC9EFC48A3A74FCF`;
  [manifest](../assets/benchmarks/onitytask-returnbuffer-evidence-2026-09-30/archive-manifest.json)
  and [checkpoint](../assets/benchmarks/onitytask-returnbuffer-evidence-2026-09-30/checkpoint.json).
  Players/full logs remain on the host under
  `BenchmarkResults/onitytask-returnbuffer-20260930`.

Resume: revalidate ownership, source/host hashes and settings; capture/export
the existing baseline diagnostic Player at 4096/flow-on/drain before runtime
edits. Then dispatch the two-file feature writer, followed by the verifier's
11 new cases in the existing thread-switch EditMode file. Run final focused
tests, existing real IL2CPP smoke cases, matched candidate profiles and fixed
three paired Release rounds. Apply the frozen retention gate; do not infer
general superiority or retain an unproven experiment.
