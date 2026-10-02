# 16 - OnityTask: surpass UniTask 2.5.11 (roadmap)

Decision owner document, 2026-10-01. Base: branch `codex/onity-0.5-async-performance`
at `f9a3a0a` in `C:/Users/e-fur/.codex/worktrees/onity-release-038/Onity`. Evidence map:
plans 11-15, the 2026-09-27..30 benchmark reports, and the read-only evidence pass of
2026-10-01 (perf history, hot-path accounting, flow-off table, IL2CPP C++ inspection).

> **Status (2026-10-02): performance lane superseded.** The packets below were replaced by
> the executed core redesign, PERF-7 ([`notes/perf-7-core-redesign.md`](notes/perf-7-core-redesign.md)):
> class-rooted sources with one state word, array-slot pools, runner return at consumption on
> every backend (the return-on-unwind gate of R1 compiles only under `ONITY_RUNNER_RETURN_GATE`)
> and stateless token-less frame waits. The 2026-10-02 Release Player gate measured that
> candidate and passed all 29 IL2CPP rows; see
> [`../assets/benchmarks/onitytask-surpass-2026-10-02.md`](../assets/benchmarks/onitytask-surpass-2026-10-02.md).
> The parity packets are recorded in [`notes/`](notes/) and the 0.6.0 CHANGELOG. This document
> is kept as the decision record; where it disagrees with those notes, the CHANGELOG or the
> code, they win.

## 0. Decisions in force

Binding user decisions (do not reopen):

1. `OnityTask.FlowExecutionContext` defaults to `false` (UniTask semantics). AsyncLocal flow
   stays available as opt-in. Speed claims are measured at the default.
2. IL2CPP first (Windows x64 now, ARM64 later). Mono is a non-regression check, never a gate.
3. Parity scope: gameplay lifecycle, factory + timing, streams. Addressables/DOTween/TMP out.
4. Local commits of verified packets are allowed; no push, no release. The 0.5 branch keeps
   its name; new branches use `perf/<slug>` or `parity/<slug>`.

Decision-owner rulings from the evidence:

- R1. The IL2CPP deferred-return queue is replaced by return-on-unwind (candidate C1), not
  buffered (plan 15 return-buffer packet). The C1 premise is confirmed from the generated
  C++: no struct copy-back exists after `MoveNextCore`; the existing depth guard already
  proves the only hazard. The plan 15 packet is retired; its 11 planned dispatcher tests are
  dropped. Its frozen baseline Players stay as the pre-roadmap reference.
- R2. ExecutionContext elision (C7) is closed: not feasible with .NET semantics on Mono or
  IL2CPP. The flow-on path keeps FastCapture + RunInternal. Flow-on cost is a feature cost,
  reported, never gated.
- R3. The fence diet (C2) and write-barrier/null-store diet (C4) are funded as one IL2CPP
  packet. Mono x64 sees no change; ARM64 benefits later.
- R4. Interlocked contracts (registration pin, version-retiring CAS, claim+exchange) stay.
  The flow-off IL2CPP projection after C1+C2+C4 is 140-175 ns vs UniTask ~177 ns at 128,
  so the contracts are touched only if that projection fails (conditional packet PERF-10).
- R5. Pool retention: defaults stay 128 runners / 256 sources. The 4096 claim is made at a
  declared matched policy (Onity caps raised to the cohort before warmup), with the default
  arm reported next to it. The source cap becomes configurable (`SourcePoolCapacity`),
  which is UniTask `TaskPool.SetMaxPoolSize` parity, not a new default. Needs U-1.
- R6. Not re-proposed: fused/based dispatch, the `CanBeCanceled` completion guard, Burst
  scheduler integration, class-based awaiter dispatch (C6).
- R7. `OnityTask`, `OnityTask<T>` and `OnityTaskExtensions` become `partial` in the first
  packet so parity work lands in new files and never queues on `OnityAsync.cs`.

## 1. Claim contract

### 1.1 Backends, flow, cohorts, retention

| Item | Gate setting | Reported alongside |
| --- | --- | --- |
| Backend | IL2CPP, Windows x64, Unity 2022.3.62f2 Release Player, non-development | Mono Release Player (non-regression vs previous Onity revision; ratio vs UniTask reported) |
| Flow | `FlowExecutionContext = false` (new default) | flow on, lifecycle arms only, labelled "feature cost" |
| Cohort 128 | retention default (128 runners, 256 sources) | - |
| Cohort 4096 | matched policy: `RunnerPoolCapacity` and `SourcePoolCapacity` >= 4096 set before warmup, restored after | default-128 arm (documented policy difference) |
| Cohort 1 | excluded (diagnostic; a ~630 ns fixed drain pass compresses ratios) | reported |
| UniTask | pinned 2.5.11 `2e993ff18f`, unbounded pool, no flow | - |

### 1.2 Predeclared workload set (final gate)

| Gate | Workload | Arms | Exists | Threshold |
| --- | --- | --- | --- | --- |
| G1 | Builder lifecycle gate (W2): typed/untyped x 1/4 suspensions x 128 and 4096-matched, both libraries' return passes | 8 per backend | `builderlifecycle` (matched-policy arm added by PERF-2) | median <= 0.95 |
| G2 | Frame lifecycle (W1): `async OnityTask` awaiting `NextFrame` and `Yield(Update)`, 1 and 3 suspensions, 128, real PlayerLoop, both return passes | 4 | new `framelifecycle` (PERF-3) | median <= 0.95 |
| G3 | Timed waits (W5/W8): `Delay(0.05 s)`, `DelayFrames(3)`, `WaitUntil(counter)`, 128, token none / registered-not-cancelled / cancelled 50% | 9 | `framelifecycle` (PERF-3) | median <= 0.95 |
| G4 | Synchronous completion (W4): completed async method typed/untyped, `FromResult<int>`, completed `GetResult` | 4 | `primary` | median <= 1.05 |
| G5 | Composition (W6): `WhenAny` 32 inputs (exists), `WhenAll` 32 async-method inputs (UniTask arm added by PAR-B5) | 4 | `whenany`; `whenall` + new arm | median <= 0.95 |
| G6 | Thread switch round trip (W7): `SwitchToThreadPool` Action/Func then main, 128, flow off | 4 | `threadpool` (refresh) | median <= 1.05 |

Ratio = Onity / UniTask complete-cycle ns per op. Per gate arm: median of the three
per-round ratios meets the threshold AND the worst per-round ratio stays <= 1.00 (G4/G6:
<= 1.08). Allocation: steady-state 128 arms within 16 B/op of UniTask by calibrated
HeapDelta, or both zero; the IL2CPP immediate-reuse arm reads 0 B/op after PERF-4.
Not gated, reported: channels (W9), composite script (W10, dropped), WhenEach, streams.
Mono: every arm within its repeat spread of the previous Onity revision.

The headline claim text once G1-G6 pass: "OnityTask completes the measured async
lifecycles faster than UniTask 2.5.11 on IL2CPP Windows x64 at the default settings; Mono
figures and the flow-on feature cost are reported separately."

### 1.3 Per-packet adoption gate (runtime packets)

- Candidate vs frozen baseline, same Players protocol, both backends.
- Target arms (named in the packet) improve on IL2CPP by more than max(2%, 1.5 x pooled
  repeat CV); the phase the packet targets (schedule / completion / consume / return) moves
  in the expected direction (mechanism corroboration).
- No arm on either backend regresses beyond its repeat spread; typed/untyped, 4-suspension
  and 4096 shapes protected.
- UniTask denominator CV across the six processes <= 5% at 128 and <= 10% at 4096; a round
  that fails is replaced (max two replacements per backend).
- Unproven: restore the packet's runtime files to the frozen bytes, keep tests and evidence.
- Benchmark-only packets have no timing gate: arms run, goldens pass, validator passes.

### 1.4 Protocol, right-sized

Kept (cheap, load-bearing): Release Players for both backends; 2 warmups + 8 samples per
case (3 + 12 at 4096); three paired process rounds per backend with reversed order
(~6 processes x ~7 s); the noise screen (other-Unity CPU <= 0.5 s and <= 5% of wall);
runtime-file and settings hashes; Roslyn on every change; one final focused test run per
packet; IL2CPP Player smoke for runtime packets; all raw reports retained.

Dropped per packet (overkill): Development-profile builds and exporter runs (only on a
failed gate, for attribution); manual 660-file hash checks (the staging script does it);
baseline rebuilds when the frozen baseline Players already match the current revision;
separate 48-arm "validation" runs when the build hashes match a validated build.

Noise controls (new):

- `quiet-check` pre-gate: 10 s CPU sample of every non-host Unity/Hub/compiler process;
  refuse to start timing if any averages > 2% CPU; verify the High performance power plan.
- Player processes start with High priority and an affinity mask excluding cores 0-1.
- Denominator stability check in the validator (1.3).
- For the final claim rounds only, ask the user to close the other Editors (currently
  BRN-Game-2, relic-rescue, Coin, ColorBlockJamCase2026) for a ~5 minute window (U-3).
- Per-packet rounds accept the screen plus two replacement rounds instead.

## 2. Performance packets (ordered)

Hot-file write queues (one writer at a time):

- `OnityAsync.cs`: PERF-1 -> PERF-5 -> PERF-7 -> PAR-B5 tail -> PERF-9 -> PERF-8.
- `OnityAsyncStateMachineRunner.cs`: PERF-4 -> PERF-5 -> PERF-9.
- `OnityTaskPlayerLoop.cs`, `OnityPlayerLoopTaskSource.cs`: PERF-6 -> PAR-B3 -> PAR-B7.
- `OnityTaskBuilderLifecycleBenchmarkRunner.cs`: PERF-2 -> PERF-4 (assertion only).
- `OnityTaskBenchmarkPlayerRunner.cs`, `OnityTaskBenchmarkPlayerBuildRunner.cs`,
  `Benchmarks/Tasks/README.md`: PERF-3 -> PAR-C1 -> PAR-B5.
- `OnityTaskPlayerSmokeRunner.cs`: PERF-4 -> PAR-B3 -> PAR-B5.
- `CHANGELOG.md` (root and package), `docs/guide/*`, `docs/Migration/From-UniTask.md`:
  integrator only; packets deliver their lines in the packet note.

| ID | Packet | Kind | Model | Work / process | Depends on |
| --- | --- | --- | --- | --- | --- |
| PERF-0 | Host tooling (staging, settings hashes, quiet-check, paired runner, validator) | tooling | sonnet medium | 3-4 h / 0 | - |
| PERF-1 | Flow default off + `partial` + docs + test fixtures; new frozen baseline | runtime (semantic) | sonnet high | 1-2 h / 1 h | - |
| PERF-2 | Matched-policy 4096 lifecycle arm, 4096 sampling | benchmark | sonnet medium | 1-2 h / 0 | - |
| PERF-3 | `framelifecycle` suite (G2, G3) | benchmark | opus high | 1 day / 1-2 h | PERF-2 (wiring order) |
| PERF-4 | C1 return-on-unwind (IL2CPP) | runtime | opus high | 3-4 h / 2 h | PERF-1 baseline, PERF-2 |
| PERF-5 | C2 fence diet + C4 null-store/barrier diet (IL2CPP) | runtime | opus high | 3-4 h / 2 h | PERF-4 |
| PERF-6 | C3a intrusive pools outside `OnityAsync.cs`, drop redundant `InvalidateVersion` | runtime | sonnet high | 3-4 h / 2 h | parallel with PERF-4/5 |
| PERF-7 | C3b pools inside `OnityAsync.cs` + `SourcePoolCapacity` | runtime | sonnet high | 2-3 h / 2 h | PERF-5, U-1 |
| PERF-8 | C5 icall hoist (`Time.frameCount` / `isPlaying` once per tick) | runtime | sonnet medium | 2 h / 1 h | PERF-7; only if G2/G3 miss narrowly |
| PERF-9 | Flow-on `Start` copy-on-write scope | runtime (opt-in path) | opus high | 4-6 h / 2 h | PERF-5 |
| PERF-10 | Interlocked audit (pin merge, claim+exchange merge) | runtime | opus high | design first | only if G1/G2 miss after PERF-7 |

### PERF-0 Host tooling

New directory `tools/benchmark-host/` (no Unity code):

- `stage-host.ps1 -Source <worktree> -HostProject <host> [-DryRun]`: mirrors
  `Packages/com.onity.framework` into the host, lists extras before deleting, refuses
  unexpected extras, writes `staging.json` (source HEAD, dirty list, per-file SHA256 of
  source and host, count). Leaves `Packages/manifest.json` and `ProjectSettings` untouched.
  Note: host `package.json` moves from 0.3.14 to the source version; sidecar labels change.
- `verify-host-settings.ps1 -HostProject <host> -Record|-Check|-Restore`: the four files
  (`ProjectSettings.asset`, `EditorBuildSettings.asset`, `EditorSettings.asset`,
  `ProjectVersion.txt`); originals saved under `BenchmarkResults/host-settings-original/`
  from the state matching `before-settings.json`; restore by file copy, never `git checkout`;
  refuse while `Temp/UnityLockfile` exists.
- `quiet-check.ps1 [-Seconds 10]`: see 1.4; prints PIDs and project names.
- `run-paired-reports.ps1`: promoted from
  `BenchmarkResults/onitytask-returnbuffer-20260930/run-paired-reports.ps1`; parameters
  `-EvidenceRoot -Suite -Backends -Rounds 3 -MaxReplacement 2 -Priority High -AffinityMask`;
  quiet-check pre-gate; keeps the noise screen and refuses to overwrite evidence.
- `validate-reports.py`: promoted; adds the denominator CV check, `--gate claim|packet`
  evaluation per 1.2/1.3, phase table, `generated-summary.json` + `summary.md`.
- `README.md`: the packet procedure end to end (stage, settings record, Roslyn, tests,
  candidate build, quiet-check, paired rounds, validate, settings restore, archive + SHA256).

Verification: re-run the validator on `onitytask-builderlifecycle-20260930` reports; it must
reproduce the published ranges (Mono 128 1-susp 2.006-2.053 flow on, etc.). Dry-run staging
against the host must report zero diffs for the frozen runtime files.

### PERF-1 Flow default off, partial types, docs, baseline freeze

Files: `OnityAsync.cs` line 30 (`s_flowExecutionContext = false`), lines 34-42 XML doc
(default off = UniTask semantics; set `true` before any async Onity method starts for
AsyncLocal flow; note the synchronous-prefix limitation until PERF-9), line 23 and 1432
(`public readonly partial struct`), `OnityTaskExtensions.cs` line 11 (`partial`).
Tests (verifier): grep `FlowExecutionContext` and `AsyncLocal` under `Tests/EditMode/Scripts`
and `Tests/PlayMode`; the 12 context-isolation tests and any flow-dependent test set the
flag explicitly in SetUp and restore it in TearDown; add two tests: default is `false`;
AsyncLocal written before an await is not visible after it at the default.
Benchmarks: README wording for "default flow" in the `primary` suite.
Docs lines delivered for CHANGELOG (Changed + Migration), `From-UniTask.md`,
`onitytask.md`, `onitytask-comparison.md`, `performance-and-il2cpp.md`.
Preserved: every flow-on behaviour when the flag is set; no other default changes.
Verification: Roslyn; focused EditMode (builder, source-state, context, preserve, cancellation
classes) + `OnityTaskAsyncBuilderPlayModeTests`; then stage to host, build Mono and IL2CPP
`builderlifecycle` Players, run 48 arms + 8 goldens + 35 smoke per backend; freeze as
`BenchmarkResults/onitytask-16-baseline-<date>/` with runtime hashes and binary manifest.
This revision is the baseline for every later packet.

### PERF-2 Matched-policy 4096 arm

File: `OnityTaskBuilderLifecycleBenchmarkRunner.cs` only. Player argument
`-onityTaskBenchmarkRetention default|matched` (default unchanged); matched sets
`OnityTask.RunnerPoolCapacity = max(128, cohort)` before the 4096 warmup and restores it
after; arm label `retention=matched`; JSON field `retentionPolicy`; 4096 arms use 3 warmups
and 12 samples. Lands before the PERF-1 baseline build so the baseline Players carry it.

### PERF-3 `framelifecycle` suite (G2, G3)

New `Benchmarks/Tasks/Runtime/OnityTaskFrameLifecycleBenchmarkRunner.cs` (+ `.meta`);
wiring in `OnityTaskBenchmarkPlayerRunner.cs`, `OnityTaskBenchmarkPlayerBuildRunner.cs`,
README section; new `tools/benchmark-host/validate-framelifecycle.py`.

Mechanism: PlayerLoop brackets, extending the `InsertBracket` Before/After marker pattern of
`OnityTaskPlayerLoopBenchmarkRunner.cs`. Per library, markers around every system the
library runs in a frame: Onity = `Update.ScriptRunBehaviourUpdate` (hosts
`OnityTaskRunner.Update`: tick sources, job registry, IL2CPP `Drain`) plus the
`OnityTaskPlayerLoop` Update/FixedUpdate/LateUpdate markers; UniTask = the injected
`PlayerLoopHelper` systems for Update, LastUpdate, PostLateUpdate and LastPostLateUpdate
(runner and yield queues; bracket by type name from the UniTask assembly, validated at
start). Harness work (schedule, consume) runs only inside its own bracketed systems: a
schedule system before the library systems in Update, a consume system after the UniTask
LastPostLateUpdate pass. The harness MonoBehaviour is inert during measured frames.
Workers: `async OnityTask Worker(n) { for (i < n) await OnityTask.NextFrame(); }` and the
UniTask twin; variants per 1.2 (Yield, DelayFrames(3), Delay(0.05 s), WaitUntil(counter),
token none / registered / cancelled 50%); untyped, plus one typed NextFrame-1 arm.
Accounting: per frame, library ticks = sum of bracketed After-Before plus that library's
harness consume ticks; control frames (empty cohort) subtracted (median of 8); cost per op =
(sum over the cohort's frames - controls) / ops. 3 warmup cohorts; 8 sample cohorts (6 at
4096); libraries interleaved A,B,B,A per sample; per-frame breakdown kept (schedule frame,
completion frames, return frame). Calibrated HeapDelta per sample (-1 when unavailable).
Goldens: completions == ops; no exception; both libraries' queues empty at sample end.
4096 arms set both Onity caps when `SourcePoolCapacity` exists (PERF-7), else record
`policy: default` and skip the matched label.
Verification: Mono and IL2CPP Release Players run every arm; three processes per backend;
validator passes. Output is the baseline G2/G3 report.
Boundary checklist (must be in the report): both return passes inside the brackets; no
library work outside brackets (assert by a frame-level sanity bracket around the whole loop
that must be within 5% of the sum); same frame count per library; cancellation registrations
disposed inside the measured frames.

### PERF-4 C1 return-on-unwind (IL2CPP)

File: `OnityAsyncStateMachineRunner.cs`, both runner classes (IL2CPP blocks at 417-458 and
592-631, `MoveNext` at 446-458 and the typed mirror). Verifier: `OnityTaskPlayerSmokeRunner.cs`
cases `TypedReentrantConsumption`, `TypedHeldWorker`, `UntypedHeldWorker` updated; two added.
Benchmark writer: lifecycle runner IL2CPP pre-pass expectation (Onity pending count 0; the
Onity `Drain` pass stays in the protocol and now measures ~0).

Mechanism: pack `m_moveNextDepth` as depth (low bits) plus a return-pending bit
(`k_returnPending = 0x40000000`).

- `MoveNext`: `Interlocked.Increment`; `try { MoveNextCore(); } finally { int after =
  Interlocked.Decrement(ref m_moveNextDepth); if (after == k_returnPending &&
  Interlocked.CompareExchange(ref m_moveNextDepth, 0, k_returnPending) == k_returnPending)
  ReturnToPoolCore(); }`.
- `ReleaseSource` (IL2CPP): loop `v = Volatile.Read(depth)`; if `v == 0` return now
  (`ReturnToPoolCore`); else `CompareExchange(v | pending, v)`; on failure re-read.
  Exactly one party pools the runner; no `MoveNext` can start after the result was consumed,
  so `v == 0` means no frame of this rental is on any stack.
- `ReturnToPoolCore`: `m_stateMachine = default`; clear `m_executionContext` and
  `m_resumeContext` only when non-null; `s_pool.TryPush(this, OnityTask.RunnerPoolCapacity)`.
  Depth is 0 at push; the next rental counts from 0.
- Mono path unchanged (`#else ReturnToPool()`). Dispatcher untouched; thread-switch
  continuations keep FIFO, session and shutdown semantics. Shutdown-time returns now pool
  instead of being dropped (harmless).

Preserved contracts: no pooling while any `MoveNext` of the rental is on any stack; version
retired by the release CAS before return; retention 128; zero steady-state allocation;
worker completion safety; re-rent inside a producer's `MoveNext`; cached delegates.
Smoke additions: (1) 1,000 iterations of a worker completing and unwinding while the main
thread consumes: exactly one return, pool count consistent, no double push; (2) depth-2
nested chain (consumer resumed inline inside the producer's `SetResult`, consumes the
producer's runner inside the producer's frame): pending set, pooled at the outer unwind.
Verification: Roslyn; Mono EditMode retirement/builder/source-state classes (no change
expected); IL2CPP smoke 35 + 2; candidate Players; paired rounds; gate arms G1 (IL2CPP
consume 85 -> ~55 ns, return 37 -> ~3 ns; expected -35..-65 ns per cycle at 128; 4096 also
improves because pools refill within the frame) and G2 once PERF-3 exists.

### PERF-5 Fence diet + barrier diet (IL2CPP)

Files: `OnityAsync.cs` 43-46 and 58-62 (static getters), 2407/3007 (`Version`),
2614-2620/3217-3223 (`ResetRetired`), 2676-2690/3281-3297 (`TrySetStatus`),
2981-2988/3588-3596 (`ClearCompletionReferences`); `OnityAsyncStateMachineRunner.cs` 366/543
(`Task` getters) and `ReturnToPoolCore` null guards if PERF-4 left any.

Sites:
- (a) `FlowExecutionContext` and `RunnerPoolCapacity` getters: plain reads; setters keep
  `Volatile.Write`; docs say "set before any async method starts".
- (b) `ResetRetired`: the source is unreachable by other threads right after a pool pop;
  make the status / cancellation-flag stores plain and keep one `Volatile.Write` of the new
  state word last (3 fences -> 1).
- (c) Runner `Task` getter: read the version with a plain load through an internal
  `VersionUnsynchronized`, valid because `Rent` and the compiler's `Task` read happen on the
  same thread before any consumer can retire it; other `Version` callers keep the fence.
- (d) Null-store guards after the claim CAS: `TrySetStatus` writes `m_exception` only when
  non-null (position before the continuation exchange unchanged); `ClearCompletionReferences`
  guards each reference store; struct defaults stay as `initobj`.
- (e) Optional: fold status into the packed word so `GetStatus` needs one fence, only if two
  bits are free and the publication order (outcome fields, continuation exchange, status)
  is provably unchanged; otherwise skip and record.

Retained synchronization (table required in the packet note: site, writer, reader, retained
fence): every CAS loop; status publication after outcome fields; `GetStatus` order when (e)
is skipped; pool gate release; the PERF-4 depth protocol. ARM64: removals are of redundant
fences only, so weak-ordering safety rests on the retained ones.
Expected: -30..-70 ns per IL2CPP cycle at 128 (4-6 fences, 3-5 barriers, +1 fence per extra
suspension); Mono x64 ~0. Gate arms: G1, G2.

### PERF-6 Intrusive pools outside `OnityAsync.cs` (parallel lane)

Files: `OnityPlayerLoopTaskSource.cs` 24-114, `OnityEndOfFrameTaskSource.cs` 9-98,
`OnityJobHandleExtensions.cs` 339-376, `OnityWhenAllTypedCoordinator.cs` 14-56 and 317,
`OnityWhenAnyArrayTaskSource.cs` 333-418 and 300. Replace `lock(s_pool)` Stack pools with
the existing intrusive CAS-gate pool (`OnityRunnerPool`, used by `OnityFrameTaskSource`);
remove `InvalidateVersion` at each `ReleaseSource` site with a per-site proof that every
caller already retired the version (`GetResultCore`, `AsTask`, `TrySetStatus` bridge;
WhenAny line 300 gets its own check). Keep caps, contended-rent-allocates, the
registration-before-enqueue order in PlayerLoop `Rent`, cancellation flags; the WhenAll
`lock(this)` callback pinning stays. Cold cost +8 B per source (document).
Tests: existing PlayerLoop/EndOfFrame/JobHandle/WhenAny/WhenAll classes plus pool-reuse
tests (rent, release, rent returns the same instance with a new version).
Expected: -40..-90 ns per IL2CPP Yield/timed cycle, -20..-35 ns on Mono; WhenAny-32 IL2CPP
from 1.36-1.55 toward parity. Gate arms: G3 (Yield), G5 WhenAny, `eof` report.

### PERF-7 Pools inside `OnityAsync.cs` and `SourcePoolCapacity`

`OnityAsync.cs`: `OnityDelayTaskSource` (4317-4385), `OnityPredicateTaskSource` (4387-4463),
`OnityAsyncOperationTaskSource` (4465-4555) and the pair coordinator (1732-1990) pools to the
intrusive pool; the five `k_maxPoolSize = 256` constants (1734, 4254, 4319, 4389, 4470)
replaced by `OnityTask.SourcePoolCapacity` (public static int, default 256, plain get,
`Volatile` set, XML doc naming the UniTask `TaskPool.SetMaxPoolSize` equivalence). After U-1.
Gate arms: G3 Delay/WaitUntil, G1/G2 4096-matched.

### PERF-8 Icall hoist (conditional)

`OnityTaskRunner` reads `Time.frameCount` once per tick and passes it through the internal
`IOnityTaskTickSource.Tick` signature; `Rent` reuses a session-cached `isPlaying` on the main
thread with a live fallback outside Update. Gain 3-12 ns per op. Only if G2/G3 miss by < 5%.

### PERF-9 Flow-on `Start` copy-on-write scope (opt-in lane)

Gap: `OnityTaskMethodBuilder.Start` (OnityAsync.cs 4601, 4749) runs the synchronous prefix
bare, so AsyncLocal writes before the first await leak to the caller when flow is on (BCL
wraps the first `MoveNext` in `EstablishCopyOnWriteScope`; UniTask has the same leak).
Recommended design A (holder frame): when `FlowExecutionContext` is true, rent a pooled
`OnityAsyncStartFrame<TStateMachine>` (class with a `TStateMachine Value` field, small
per-type pool for nesting), copy the state machine in, run
`TryRunPreservingContext(FastCapture(), s_moveNextStartFrame, frame)` (the existing
RunInternal binding gives the copy-on-write scope and Undo), copy `frame.Value` back to the
caller's stack state machine, clear and return the frame. The suspension path is unchanged:
a first await inside the frame rents the runner from the frame's copy, and the copy-back
propagates `m_runner` to the stack copy, so `builder.Task` works; synchronous completion
keeps today's inline result representation (no pooled single-consumer task for sync
results). Flow off: bare `MoveNext`, unchanged. Alternative B (rent the runner at `Start`)
is allowed only if A measures worse on the flow-on lifecycle arms. Files: `OnityAsync.cs`
(both `Start`), `OnityAsyncStateMachineRunner.cs` (frame type, callback). Tests: prefix
isolation typed/untyped, nested calls, exception in prefix, sync completion result identity,
the 12 context tests. Not on the claim path; flow-on lifecycle arms reported.

### PERF-10 Interlocked audit (conditional)

Only if G1 or G2 miss after PERF-7. Design packet first: fold the `FinishRegistration`
pin-release CAS into the continuation-slot publication; merge the completion claim CAS with
the continuation exchange. Stale-token throws and misuse detection must survive; if a
semantic change is unavoidable, stop and request a user decision on a documented fast mode.

## 3. Parity packets

Rules: Onity naming (`Onity` prefix on new types, `OnityTask.*` statics in `partial` files),
no API beyond UniTask parity, XML docs on every public member, no `System.Linq`, no third-party
runtime dependency, main-thread affinity rules documented per API. Each packet owns new files
plus at most one existing file. Each lands with EditMode tests (success, cancellation, fault,
disposal/order) and PlayMode tests where Unity lifecycle is involved.

### 3.1 Area A: gameplay lifecycle

| ID | API (Onity names) | New files | Model | Depends |
| --- | --- | --- | --- | --- |
| PAR-A1 | `component.GetCancellationTokenOnDestroy()`, `gameObject.GetCancellationTokenOnDestroy()`, `cts.RegisterRaiseCancelOnDestroy(component/gameObject)`, `cts.AddTo(component/gameObject)`; MonoBehaviour path uses the native `destroyCancellationToken` | `Async/OnityAsyncDestroyTrigger.cs` (internal component), `Async/OnityTaskLifetimeExtensions.cs`; tests `OnityTaskLifetimeEditModeTests.cs`, `OnityTaskLifetimePlayModeTests.cs` | sonnet medium | - |
| PAR-B1 (shared primitive) | `OnityAutoResetTaskCompletionSource`, `<T>`: pooled, version-reset, `Create()`, `TrySetResult/Exception/Canceled`, `Task` | `Async/OnityAutoResetTaskCompletionSource.cs` built on the internal `OnityTaskSourceBase` (same assembly) | sonnet high | - |
| PAR-A2a | Trigger base + lifecycle triggers: `OnityAsyncTriggerBase<T>`, `OnityAsyncTriggerHandler<T>` (`OnNextAsync`, `IDisposable`), `AwakeAsync`, `StartAsync`, `OnEnableAsync`, `OnDisableAsync`, `OnDestroyAsync`, `GetAsyncXTrigger()`, `AsAsyncEnumerable()` | `Async/Triggers/OnityAsyncTriggerBase.cs`, `OnityAsyncTriggerHandler.cs`, `OnityAsyncLifecycleTriggers.cs`, `OnityAsyncTriggerExtensions.cs`; tests `OnityAsyncTriggerEditModeTests.cs`, `OnityAsyncTriggerPlayModeTests.cs` | opus high (multi-waiter handler, cancellation, destroy order) | A1, B1 |
| PAR-A2b | The full MonoBehaviour message set (collision/trigger 2D/3D, visibility, application, mouse, animator, transform, particle, joint, audio, GUI, render) generated from the pinned UniTask list, one trigger class per message | `Async/Triggers/OnityAsyncMessageTriggers.cs` (generated, plus the generator script under `tools/`) | sonnet high | A2a |
| PAR-A3 | `OnityTaskVoid` + `OnityTaskVoidMethodBuilder` (wraps the untyped builder and forwards to `Forget`; exceptions through the Forget observer, no new runner type), `OnityTask.Void(Func<OnityTaskVoid>)`, `OnityTask.Action(...)`, `OnityTask.UnityAction(...)` | `Async/OnityTaskVoid.cs`, `Async/OnityTask.Void.cs`; tests `OnityTaskVoidEditModeTests.cs` | sonnet high | PERF-1 (`partial`) |
| PAR-A4a | UnityEvent and UI Toolkit waits: `unityEvent.OnInvokeAsync(ct)`, `GetAsyncEventHandler()`, `OnInvokeAsAsyncEnumerable()`, `UnityEvent<T>` variants; `button.OnClickAsync(ct)`, `OnClickAsAsyncEnumerable()`, `element.OnEventAsync<TEvent>()`, `OnValueChangedAsync<T>()` for `INotifyValueChanged<T>` | `Async/OnityUnityEventAsyncExtensions.cs`, `Async/OnityUIToolkitAsyncExtensions.cs`; tests EditMode + PlayMode | sonnet high | A2a handler, B1 |
| PAR-A4b | uGUI: `Button.OnClickAsync`, `Toggle/Slider/InputField/Dropdown.OnValueChangedAsync`, `OnEndEditAsync`, `BindTo` | optional assembly `Runtime/Unity.UGUI/Onity.Unity.UGUI.asmdef` with `versionDefines` on `com.unity.ugui` (define `ONITY_UGUI`), files under it | sonnet high | A4a, U-2 |

### 3.2 Area B: factory and timing

| ID | API (Onity names) | New files | Model | Depends |
| --- | --- | --- | --- | --- |
| PAR-B2 | `OnityTask.Create(Func<OnityTask>)`, `Create<T>`, `Defer`, `Defer<T>`, `Never(ct)`, `Never<T>(ct)`, `Lazy` -> `OnityAsyncLazy`/`<T>` (`ToAsyncLazy()`), `ContinueWith` (all four shapes), `Unwrap`, `AsUnitTask` (`Onity.Core.Unit`), `AsOnityTask(ValueTask)`, `AsValueTask()` | `Async/OnityTask.Factories.cs`, `Async/OnityAsyncLazy.cs`, `Async/OnityTaskContinuationExtensions.cs`, `Async/OnityTaskValueTaskExtensions.cs`; tests per file | sonnet medium | PERF-1 |
| PAR-B3 | Timing expansion: `OnityPlayerLoopTiming` gains every pinned UniTask member (existing three keep names and values; new members appended); `OnityTaskPlayerLoop` injects a node per phase; `OnityYieldAwaitable` returned by `OnityTask.Yield()` / `Yield(timing)` without a token (token overloads keep `OnityTask`); `OnityDelayType { DeltaTime, UnscaledDeltaTime, Realtime }`; `Delay(TimeSpan|int ms, OnityDelayType, OnityPlayerLoopTiming, ct, cancelImmediately)`, `WaitForSeconds`, `DelayFrame(n, timing, ct, cancelImmediately)`, `WaitUntil(predicate, timing, ct, cancelImmediately)`, `WaitUntil<T>(state, Func<T,bool>, ...)`, `WaitWhile` same, `WaitUntilValueChanged<T,U>(target, monitor, timing, comparer, ct)`, `WaitUntilCanceled(ct)`, `ct.WaitUntilCanceled()`, `ct.ToOnityTask()`, `task.ToCancellationToken()`; `CancelAfterSlim(delay, delayType, timing)` | `Async/OnityYieldAwaitable.cs`, `Async/OnityTask.Timing.cs`, `Async/OnityTimedTaskSources.cs` (timed delay/predicate/value-changed sources registering on the phase nodes; existing Update-only sources in `OnityAsync.cs` untouched), edits to `OnityTaskPlayerLoop.cs`, `OnityPlayerLoopTaskSource.cs`, `OnityCancellationTokenSourceExtensions.cs`; tests `OnityTaskTimingEditModeTests.cs`, `OnityTaskTimingPlayModeTests.cs`, `OnityYieldAwaitableEditModeTests.cs`; 2 smoke cases | opus high | PERF-6 (same files) |
| PAR-B4 | `OnityTask.SwitchToSynchronizationContext`, `SwitchToTaskPool`, `ReturnToMainThread()`, `ReturnToCurrentSynchronizationContext()` scopes; `RunOnThreadPool(Func<OnityTask>)`, `Func<OnityTask<T>>`, state-argument overloads with `configureAwait`; coroutine interop `task.ToCoroutine(Action<Exception>)`, `enumerator.ToOnityTask(host, timing, ct)`, `host.StartAsyncCoroutine(Func<ct, OnityTask>)` | `Async/OnityTaskSynchronizationContextSwitch.cs`, `Async/OnityTask.Switching.cs`, `Async/OnityTaskCoroutineExtensions.cs`, edits to `OnityTaskThreadPoolSwitch.cs`; tests EditMode + PlayMode | sonnet high | PERF-1 |
| PAR-B5 | Composition breadth: `WhenAll`/`WhenAny` `IEnumerable` overloads, tuple overloads for 2-15 mixed types (generated), `WhenEach` -> `IOnityAsyncEnumerable<OnityWhenEachResult<T>>`; native multi-consumer WhenAll output (C9) replacing the TCS/Task/tracker path while preserving multi-consumer awaits, ordered results, fault aggregation, cancellation identity, duplicate detection, tracking; UniTask-paired `whenall` benchmark arm (G5) | `Async/OnityTask.WhenAll.Generated.cs`, `Async/OnityTask.WhenAny.Generated.cs`, `Async/OnityWhenEach.cs`, `Async/OnityWhenAllNativeSource.cs`; tail edit in `OnityAsync.cs` 822-1039 routing existing overloads to the native source; `OnityTaskWhenAllBenchmarkRunner.cs` arm | opus high | PERF-7 (OnityAsync queue) |
| PAR-B6 | Unity payloads (Stage 5a): `ResourceRequest.AsAssetOnityTask<T>()`, `AssetBundleRequest.AsAssetBundleOnityTask()`, `AssetBundleCreateRequest...`, `UnityWebRequestAsyncOperation.AsWebRequestOnityTask()`, `AsyncGPUReadbackRequest.AsOnityTask()`, `AwaitForAllAssets`, `IProgress<float>` overloads, `OnityProgress.Create(Action<float>)` | `Async/OnityUnityOperationExtensions.cs`, `Async/OnityProgress.cs`; tests EditMode + PlayMode | sonnet medium | - |
| PAR-B7 | `OnityPlayerLoopTimer.Create/StartNew` (delay type, timing, periodic, `Restart/Stop/Dispose`) backing `CancelAfterSlim` and `Timeout(delayType, timing)` | `Async/OnityPlayerLoopTimer.cs`; edits `OnityTaskExtensions` Timeout overloads in a new partial file | sonnet medium | PAR-B3 |
| PAR-B8 | `OnityTaskScheduler` (`UnobservedTaskException` event, `PropagateOperationCanceledException`, `DispatchUnityMainThread`, `UnobservedExceptionWriteLogType`) wired into completion-source and Forget fault reporting; tracker coverage of native sources (zero cost while tracking is off) | `Async/OnityTaskScheduler.cs`; edits `OnityTaskCompletionSource.cs` 52-114, `OnityTaskTracker.cs` | sonnet high | - |

### 3.3 Area C: streams

| ID | API | New files | Model | Depends |
| --- | --- | --- | --- | --- |
| PAR-C1 | Stage 4d integration and verification (see 3.5) | `Async/OnityAsyncEnumerableReactiveAdapters.cs` + two methods in `OnityAsyncEnumerableExtensions.cs`, tests, `OnityReactiveAdapterBenchmarkRunner.cs` | sonnet high | - (wiring after PERF-3) |
| PAR-C2a | Filtering, paging, aggregates, materialization: `Skip`, `SkipWhile`, `TakeWhile`, `TakeLast`, `SkipLast`, indexed `Select`/`Where`, `Distinct`, `DistinctUntilChanged`, `Count/Any/All/Contains`, `Sum/Min/Max/Average` (generated per numeric type), `Aggregate`, `First/Last/Single(+OrDefault)`, `ElementAt`, `SequenceEqual`, `ToList/ToDictionary/ToHashSet/ToLookup`, `ForEachAwaitAsync`, `*WithCancellation` variants | `Async/OnityAsyncEnumerableOperators.Filtering.cs`, `.Aggregates.cs` (+ generated), `.Materialize.cs` | sonnet high | PAR-C1 (file order) |
| PAR-C2b | Combining: `Zip`, `Merge`, `Concat`, `CombineLatest`, `Pairwise`, `SelectMany`, `Append/Prepend`, `DefaultIfEmpty`, `Buffer`, `GroupBy`, `OrderBy/ThenBy`, `Reverse`, `Publish`, `Queue` | `Async/OnityAsyncEnumerableOperators.Combining.cs`, `.Grouping.cs` | opus high (multi-upstream pumps) | PAR-C1 |
| PAR-C2c | Factories and sources: `OnityAsyncEnumerable.Create(Func<writer, ct, OnityTask>)` with `IOnityAsyncWriter<T>`, `Never`, `Throw`, `Repeat`, `Timer`, `TimerFrame`, `EveryUpdate(timing)`, `EveryValueChanged`, `IEnumerable/Task/OnityTask.ToOnityAsyncEnumerable`, `Subscribe` (returns `IDisposable`) | `Async/OnityAsyncEnumerable.Sources.cs` (make `OnityAsyncEnumerable` partial), `Async/OnityAsyncWriter.cs` | sonnet high | PAR-B3 for timings |
| PAR-C3 | `OnityAsyncReactiveProperty<T>`, `OnityReadOnlyAsyncReactiveProperty<T>`, `WaitAsync`, `WithoutCurrent`, `ToReadOnlyAsyncReactiveProperty` | `Async/OnityAsyncReactiveProperty.cs`; tests | sonnet high | PAR-B1, PAR-C1 |

### 3.4 Parallelism

Runs in parallel with the perf lane, each in its own worktree/branch, because it touches only
new files: PAR-A1, PAR-B1, PAR-A3, PAR-B2, PAR-B6, PAR-B8, PAR-C1 (minus wiring), PAR-C2a/b
after C1, PAR-C3. Forced sequencing: PAR-B3 and PAR-B7 after PERF-6 (PlayerLoop files);
PAR-B5 after PERF-7 (`OnityAsync.cs`); PAR-A4b after U-2; PAR-C2c after PAR-B3; benchmark
wiring after PERF-3; smoke-runner edits after PERF-4. Recommended parity order by user value:
A1, A3, B2, C1, B1, A2a, A4a, B3, B6, C2a, B4, C3, A2b, C2c, C2b, B5, B7, A4b, B8.

### 3.5 Stage 4d integration (PAR-C1)

Source: `C:/Users/e-fur/.codex/worktrees/onity-fast-capture-f682b7c/Onity` (uncommitted,
base 3f09893). Take file bytes, not the branch: `Async/OnityAsyncEnumerableReactiveAdapters.cs`
(+ `.meta`, 990 lines), `Tests/EditMode/Scripts/OnityAsyncEnumerableReactiveEditModeTests.cs`
(780), `Tests/PlayMode/OnityAsyncEnumerableReactivePlayModeTests.cs` (166),
`Benchmarks/Tasks/Runtime/OnityReactiveAdapterBenchmarkRunner.cs` (454),
`docs/assets/benchmarks/onitytask-stage4d-reactive-adapters-2026-09-27.md`. Insert only the two
methods `AsOnityAsyncEnumerable(IOnityObservable<T>, int capacity)` and
`AsObservable(IOnityAsyncEnumerable<T>)` (lines 11-58 of the 4d copy) into the current
`OnityAsyncEnumerableExtensions.cs`; take nothing else from that worktree (its `OnityAsync.cs`
is 0.3.14-era and superseded). Then the outstanding items from the 4d report: the two
test-quality corrections, PlayMode run, Mono and IL2CPP Player smoke, and the allocation
workload (pending state allocates; no zero-allocation claim). Benchmark wiring lines land
after PERF-3 through the benchmark writer.

### 3.6 Test expectations

- EditMode per packet: synchronous success, cancellation (pre-cancelled, during wait),
  faults including OCE-as-fault rules, disposal and ordering, stale-token behaviour,
  main-thread validation where applicable.
- PlayMode for destroy, triggers, UI events, coroutine interop, timings, timers.
- Smoke cases only for packets touching runtime hot files or PlayerLoop injection.
- Allocation statements: "pending state allocates" is allowed; zero-allocation claims need a
  calibrated measurement in a Release Player.
- Parity is declared per area only when every row of that area in the parity matrix is
  `implemented` with tests; `From-UniTask.md` stops saying "not a full replacement" only when
  areas A, B and C are complete.

## 4. Infrastructure

Hosts:

- Benchmark host (timing, Player builds, smoke): `onity-builder-benchmark-4be50dc/Onity`,
  exclusive through a lease file `BenchmarkResults/HOST-LEASE.json` {packet, owner, started,
  expires (<= 60 min, renewable)}; one lease at a time; the orchestrator grants leases.
- Test hosts: every packet worktree is a full 2022.3.62f2 project; EditMode/PlayMode run
  headless in the worktree (first run imports `Library`, minutes). Many may run concurrently
  on different worktrees. Rule: no Unity process (test, build, Roslyn through Unity) may
  start while a timing lease is active; timing windows are short (~3-5 min per round set).
- Roslyn: `unity_compile_check.py --project <worktree> --files ...`; packet worktrees need the
  generated `.csproj` once (`Unity.exe -batchmode -quit -projectPath <wt> -executeMethod
  UnityEditor.SyncVS.SyncSolution -logFile <log>`); on `COMPILE_UNVERIFIED` use the headless
  Unity compile.
- Tests: `unity_test_batch.py submit --project <wt> --mode EditMode|PlayMode --filter <classes>
  --files <changed> --no-wait` then `wait --id`, or direct
  `Unity.exe -batchmode -nographics -runTests -projectPath <wt> -testPlatform EditMode
  -testFilter "<classes>" -testResults <xml> -logFile <log> -releaseCodeOptimization`.

Staging and settings: PERF-0 scripts; staging manifest archived with every evidence packet;
settings recorded before and verified after every Editor run on the host; temp scene
`Assets/OnityBenchmarkTemp/OnityTaskBenchmarkPlayer.unity` must be absent afterwards.

Integration onto `codex/onity-0.5-async-performance` (integration checkout: release-038):
PERF-0, PERF-2, PERF-1 (baseline freeze), PERF-3 (baseline G2/G3 report), PERF-4, PERF-6,
PERF-5, PERF-7, PAR-C1, parity packets as verified (3.4 order), PERF-9, conditional PERF-8/10,
final claim rounds, docs update (comparison guide, plan 13/15 status, CHANGELOG), user release
decision. Each packet = one branch, one local commit after verification, merged (no squash of
evidence paths) by the orchestrator; commit messages per the repository rule (no AI
attribution). After any merge touching a hot file (`OnityAsync.cs`,
`OnityAsyncStateMachineRunner.cs`, `OnityTaskThreadSwitch.cs`, `OnityTaskPlayerLoop.cs`,
`OnityPlayerLoopTaskSource.cs`, any task source file), rebuild the baseline Players and run G1
for one round per backend as a tripwire before the next runtime packet freezes against it.

## 5. User decisions needed

- U-1 Retention claim policy. Claim the 4096 cohort at matched policy (Onity caps raised to the
  cohort) with the default-128 arm reported next to it; keep defaults 128/256; add the public
  `OnityTask.SourcePoolCapacity`. Recommendation: approve. Alternative: raise defaults to
  match UniTask's unbounded retention (memory retained until domain reload).
- U-2 uGUI parity packaging. Optional assembly `Onity.Unity.UGUI` with `versionDefines` on
  `com.unity.ugui` (no package dependency) versus adding `com.unity.ugui` to `package.json`.
  Recommendation: optional assembly.
- U-3 Final claim rounds. Close the other Unity Editors for the ~5 minute final timing windows
  (per-packet rounds rely on the screen plus replacements). Recommendation: yes for the final
  rounds only.

## 6. First wave (dispatch now, independent)

Common to every assignment: base commit `f9a3a0a`, repo
`C:/Users/e-fur/.codex/worktrees/onity-release-038/Onity`; create a worktree
`C:/Users/e-fur/.codex/worktrees/onity-<slug>/Onity` on a new branch from that commit; never
touch `C:/Users/e-fur/Documents/Repos/Onity`; no push, no release, no dependency/asmdef/
manifest/ProjectSettings change unless listed; Allman braces, `m_`/`s_`/`k_`, XML docs on
public API, no `System.Linq` in runtime; Roslyn after each edit; one final focused test run;
commit on the packet branch only after verification; output contract = packet note with
changed files, verification evidence paths, changelog lines, open questions.

### W1-A: PERF-1 Flow default off (sonnet high)

- Branch `perf/flow-default-off`, worktree `onity-flow-default-off`.
- Objective: `FlowExecutionContext` defaults to `false`; `OnityTask`, `OnityTask<T>`,
  `OnityTaskExtensions` become `partial`; docs and tests follow; new frozen baseline.
- Allowed writes: `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsync.cs`
  (lines 23, 30-47, 1432 only), `Runtime/Unity/Scripts/Async/OnityTaskExtensions.cs` (line 11
  only), `Tests/EditMode/Scripts/*` and `Tests/PlayMode/*` files that reference
  `FlowExecutionContext` or `AsyncLocal`, `Benchmarks/Tasks/README.md` (flow wording), and a
  packet note `docs/Plan/notes/perf-1-flow-default.md` carrying the CHANGELOG, migration and
  guide lines for the integrator.
- Forbidden: any other runtime edit; changing flow-on behaviour; benchmark runner edits.
- Acceptance: default is `false` (test); AsyncLocal does not flow at the default (test); the
  12 context-isolation tests pass with the flag set explicitly; Roslyn `COMPILE_OK`; focused
  EditMode classes `OnityTaskAsyncBuilderEditModeTests`, `OnityTaskBuilderEditModeTests`,
  `OnityTaskSourceStateEditModeTests`, `OnityTaskPreserveEditModeTests`,
  `OnityTaskCancellationEditModeTests`, `OnityAsyncTests` and PlayMode
  `OnityTaskAsyncBuilderPlayModeTests` pass in Release optimization.
- Host step (needs the host lease; use PERF-0 scripts if landed, else the manual procedure in
  plan 15): stage, build Mono and IL2CPP `builderlifecycle` Players, run 48 arms + 8 goldens +
  35 smoke per backend, record runtime hashes and binary manifest under
  `BenchmarkResults/onitytask-16-baseline-<date>/`, verify settings hashes, temp scene absent.
- Stop conditions: any flow-on test fails after fixture changes; smoke failure; settings hash
  mismatch that cannot be restored by copy.

### W1-B: PERF-0 Host tooling (sonnet medium)

- Branch `perf/benchmark-host-tooling`, worktree `onity-benchmark-host-tooling`.
- Objective: the six deliverables of section 2 PERF-0 under `tools/benchmark-host/`.
- Allowed writes: `tools/benchmark-host/**` only. Read-only inputs: the host packet
  directories `BenchmarkResults/onitytask-builderlifecycle-20260930` and
  `onitytask-returnbuffer-20260930` (scripts, reports, `before-settings.json`).
- Forbidden: running Unity; writing into the host; editing package files.
- Acceptance: validator reproduces the published lifecycle ranges from the archived reports;
  `stage-host.ps1 -DryRun` lists zero diffs for the five frozen runtime files against the host;
  `verify-host-settings.ps1 -Check` matches `before-settings.json`; `quiet-check.ps1` lists
  the running Editors with PIDs; README documents the full packet procedure.
- Stop conditions: a required original settings file cannot be matched to the recorded hash.

### W1-C: PERF-2 Matched-policy lifecycle arm (sonnet medium)

- Branch `perf/lifecycle-matched-retention`, worktree `onity-lifecycle-matched-retention`.
- Objective: section 2 PERF-2.
- Allowed writes: `Benchmarks/Tasks/Runtime/OnityTaskBuilderLifecycleBenchmarkRunner.cs`, its
  README paragraph delivered in the packet note (README itself belongs to PERF-3's queue).
- Forbidden: runtime edits; PlayerRunner/BuildRunner edits.
- Acceptance: Roslyn `COMPILE_OK` for `Onity.TaskBenchmarks`; a Mono Editor-run of the suite
  with `-onityTaskBenchmarkRetention matched` (allowed: an Editor Player build is not required
  for acceptance; the Player run happens in PERF-1's baseline build) shows the arm labels,
  `retentionPolicy` field, and restored `RunnerPoolCapacity == 128` after the run.
- Stop conditions: the harness cannot restore the capacity without a domain reload.

### W1-D: PERF-3 `framelifecycle` suite (opus high)

- Branch `perf/frame-lifecycle-suite`, worktree `onity-frame-lifecycle-suite`.
- Objective: section 2 PERF-3, including the boundary checklist in the report.
- Allowed writes: new `Benchmarks/Tasks/Runtime/OnityTaskFrameLifecycleBenchmarkRunner.cs`
  (+ `.meta`), `OnityTaskBenchmarkPlayerRunner.cs`, `OnityTaskBenchmarkPlayerBuildRunner.cs`,
  `Benchmarks/Tasks/README.md` (new section; also carry PERF-2's paragraph), new
  `tools/benchmark-host/validate-framelifecycle.py`.
- Forbidden: runtime edits; changes to existing suites' arms or output formats.
- Acceptance: Roslyn `COMPILE_OK`; the suite runs in a Mono Editor Player-less dry run
  (allowed for development only); with the host lease after PERF-1's baseline exists, Mono and
  IL2CPP Release Players run every arm three times per backend, goldens pass, validator
  passes, the frame-level sanity bracket agrees within 5%, and the report is archived as the
  baseline G2/G3 evidence with the staging manifest and settings hashes.
- Stop conditions: a bracketed system cannot be identified for either library; the sanity
  bracket disagrees by more than 5% after investigation (report the attribution, do not
  "fix" by moving work outside brackets).

Ready queue (dispatch as slots free; all independent of the wave above except where noted):
PERF-4 (after PERF-1 baseline and PERF-2), PERF-6, PAR-C1 (wiring after PERF-3), PAR-A1,
PAR-A3 (after PERF-1), PAR-B2 (after PERF-1), PAR-B6, PAR-B1.

## 7. Closed items

Not re-proposed without new evidence: fused dispatch, fieldless runner bases, the
`CanBeCanceled` disposal guard, ExecutionContext elision on resume, Burst scheduler
integration, class-based awaiter dispatch, the plan 15 return-buffer packet. The readinesscycle
suite stays unmatched and is not cited for Onity/UniTask ratios.
