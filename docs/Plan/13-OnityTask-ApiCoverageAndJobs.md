# OnityTask API coverage and Jobs integration

## 0.4.0 release checkpoint

- The user requested a release on 2026-09-27 and deferred remaining work.
- Release scope ends at the verified Stage 4c awaitable operators: 947 EditMode
  and 94 PlayMode tests in both optimizations, plus 35 smoke cases per fresh
  Mono/IL2CPP Release Player. The 574-file source manifest defines that boundary.
- Stage 4d reactive adapters are excluded. Their initial 31-case EditMode run
  passed, but two test-quality corrections, PlayMode, Player and allocation
  verification remain outstanding. The implementation, corrected fixtures and
  unfinished benchmark are preserved in the original development worktree.
- Resume with Stage 4d verification before implementing WhenEach, then continue
  the remaining Unity adapters described below. Do not treat future contracts
  in this plan as APIs shipped in 0.4.0.
- Functional coverage is broader; full UniTask parity and general performance
  superiority remain unproven. Burst measurements apply to job computation.

## Status and baseline

- Staged plan approved by the user on 2026-09-27. Stages 1, 2, 3a–3f and 4a–4c, plus the discovered legacy-runner teardown correction, are implemented and verified within the limits below. Stage 4d and remaining Unity adapters are deferred after the 0.4.0 release.
- Baseline: Onity 0.3.14, `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`.
- Reference: UniTask 2.5.11, `2e993ff18f28c931602a07292df0b0804eebef99`.
- Target: Unity 2022.3.62f2, Windows Mono and IL2CPP; unsupported platform behavior must be explicit.
- Objective: close meaningful API gaps using original Onity implementations, and measure managed scheduling and Burst compute separately.
- The user approved this public API plan and staged implementation. This is not a claim of completed parity; later-stage API details are reviewed within the approved scope before coding.

## Existing capability and gaps

| Area | Existing Onity behavior | Work to add or expand |
| --- | --- | --- |
| Async methods | Typed/untyped pooled native builders, context flow, sharing, completion sources | Preserve these semantics while reducing measured synchronization costs |
| Threading | Main-thread switch with session-aware dispatch | Thread-pool switch and explicit background work; later context-switch/return scopes |
| Frame scheduling | Update, fixed and late waits; delays and predicate waits | Explicit PlayerLoop timing, Yield, end-of-frame, value-change waits and immediate cancellation options |
| Composition | WhenAll and two-input WhenAny; some paths use Task bridges | More native composition, arbitrary-input WhenAny, mixed-result composition, completion-order WhenEach |
| Cancellation | Tokens and a timeout controller | External-cancellation decorators, timeout result forms, cancellation suppression, documented race precedence |
| Async streams | Push-based Onity reactive operators | Pull-based await-foreach streams, bounded/unbounded channels, async operators and reactive adapters |
| Unity operations | Generic AsyncOperation, scene and web bridges | Typed operation/progress adapters, coroutine conversion, async events/UI and lifecycle helpers |
| Optional integrations | Existing Input System and DOTS support | Assess Addressables adapters separately; do not introduce third-party runtime dependencies |
| Jobs/Burst | Burst DOTS systems and raycast batches; DOTS waits currently use Task.Yield | Safe JobHandle-to-OnityTask observation, native DOTS event waits and real compute examples |

Parity is tracked by supported behavior, cancellation, ownership and tests, not method counts. Specialized third-party integrations such as DOTween require a separate optional-package decision; they are not silently added to the core.

## Burst decision

Burst supports a restricted unmanaged subset of C#. The current task sources, delegates, execution contexts and exceptions are managed objects. Annotating their state-machine structs does not make the async scheduler Burst-compatible.

IL2CPP already translates the current managed code into native code. The measured async overhead remains present in the IL2CPP Player. Optimizing source registration/completion/result synchronization is therefore a separate workstream from compiling data-processing jobs with Burst.

Use this integration boundary:

1. Caller owns native input/output memory and schedules an ordinary Burst-compatible job.
2. A managed Onity adapter observes its JobHandle from the Unity thread.
3. The adapter calls Complete before publishing successful completion and data access.
4. Onity resumes managed gameplay code after the dependency is safe to observe.

Cancellation cannot terminate an already scheduled job. The first adapter should avoid an early-cancellation overload until its completion, memory ownership and shutdown behavior are designed and tested. It must not silently dispose caller-owned containers. Runner destruction/domain reload must not drop an outstanding completion obligation.

Place the bridge in the existing Onity.Unity assembly, which already references Unity.Jobs, Burst and Onity.DOTS. A reverse reference from Onity.DOTS to Onity.Unity would introduce a cycle.

## Delivery order

1. **Thread-pool support:** the concrete contract below, focused tests, documentation and matched scheduling probes.
2. **Jobs safety and Burst integration:** runner teardown design, JobHandle observation, native-memory lifetime tests, Burst compute sample and separate compute/bridge measurements.
3. **Scheduling and composition:** PlayerLoop phase contract, cancellation/timeout adapters, native combinators and completion-order consumption.
4. **Async streams and channels:** await-foreach, disposal/cancellation/backpressure, async operators, reactive/event adapters.
5. **Unity API coverage:** operation-specific adapters, progress, lifecycle/UI and coroutine interop; assess optional integration dependencies before adding them.

Each stage is independently reviewed and verified before the next stage changes its foundations. Later public API details are reviewed within the approved scope before implementation. Do not declare full UniTask parity while any required capability is partial or unverified.

## Stage 1 public contract

In namespace `Onity.Unity.Async`:

```csharp
OnityTaskThreadPoolSwitch OnityTask.SwitchToThreadPool(
    CancellationToken cancellationToken = default);

OnityTask OnityTask.RunOnThreadPool(
    Action action,
    bool returnToMainThread = true,
    CancellationToken cancellationToken = default);

OnityTask<T> OnityTask.RunOnThreadPool<T>(
    Func<T> function,
    bool returnToMainThread = true,
    CancellationToken cancellationToken = default);
```

- Switch always queues, including when called from a worker. Its awaiter reports IsCompleted=false and observes cancellation in GetResult on the worker, including a pre-canceled token.
- Both OnCompleted and UnsafeOnCompleted queue without independent execution-context capture. Async builders retain responsibility for context flow, consistent with the current main-thread switch. Direct/manual registration does not flow context.
- Run rejects a null delegate. Pre-canceled work never invokes the delegate; cancellation is checked again before worker invocation.
- Cancellation cannot interrupt an executing delegate. After a successful delegate, cancellation is observed before success is reported. A delegate exception takes precedence over cancellation.
- With returnToMainThread=true, dispatched work publishes success, fault or cancellation after returning through the existing main-thread dispatcher; its cleanup hop does not use the canceled token. With false, it completes on the worker.
- A later await of an already completed task may run inline; completion-thread selection is not a promise to switch every consumer.
- Main-thread returns belong to the originating session. Preserve existing stale-session discard behavior; background work is not forcibly stopped when a Play session ends.
- Keep FlowExecutionContext=true and runner retention=128. Preserve cancellation tokens, fault identity, bridge/sharing rules and IL2CPP deferred return protection.
- In a WebGL Player (`UNITY_WEBGL && !UNITY_EDITOR`), factories and the queue entry fail promptly with PlatformNotSupportedException. Keep the API compilable, with no same-thread fallback and no permanently pending await. The Editor remains supported when WebGL is the selected build target.
- Async-delegate and state-argument overload families are follow-up work.

## Ownership and verification

### Stage 2 design within the approved roadmap

- Public entry: `OnityTask AsOnityTask(this JobHandle handle)` in `Onity.Unity.Async`; call it on Unity's main thread. The first version has no cancellation overload.
- Caller schedules the job and owns its native containers. Acceptance transfers only the obligation to call Complete. Completed/default handles call Complete inline; pending handles are retained even when the returned task is never awaited.
- A dedicated main-thread registry polls once per Update and flushes newly registered batches once. It detaches an entry before completion publication and clears its handle before a continuation can consume/re-rent the source.
- Runner destruction, Editor/Play transitions, quit, assembly reload and subsystem reset settle every accepted handle. Retirement rejects new registrations, detaches all pending entries, calls Complete, then publishes cancellation; a Complete failure faults that task while cleanup continues for other handles.
- Use an epoch/reentrancy guard so a continuation-triggered teardown cannot skip remaining handles. Native source pooling must tolerate result consumption off the main thread.
- Manual runner destruction allows a later registration in the same valid session after retirement completes. Quit, reload or session transition during retirement keeps registration closed. Rejected worker/closed-registry calls throw InvalidOperationException before accepting the handle; the caller still owns completion.
- After acceptance, an inline or pending Complete failure faults the returned task. A new-session initialization must preserve jobs already accepted by Awake in that session.
- Keep ordinary frame-source cancellation behavior unchanged. Add only job-registry calls to the runner's Update and owned-instance OnDestroy paths; place other job-specific hooks in the new adapter file.
- Source scope: new `OnityJobHandleExtensions.cs` and metadata beside existing async code; scoped runner hooks; focused EditMode/PlayMode JobHandle fixtures and metadata; a real compute benchmark/sample under the existing task benchmark assembly; associated docs.
- The benchmark may reference the already installed Unity.Burst and Unity.Collections assemblies. JobHandle is supplied by the engine in this host; do not add an unresolved Unity.Jobs assembly reference. No new assembly or package dependency is required. Keep ordinary job tests free of Burst references; prove actual Burst execution in the Player sample.
- Verify completed/pending/combined handles, dependent disposal, unawaited tasks, rejected worker calls, sharing/bridge/pool semantics, teardown and reentrancy. Never free or modify a container while its job still owns it.
- Measure the same compute kernel with managed execution, Jobs and Burst, including scheduling/completion costs. Compare the Onity and UniTask job adapters at the same Update timing separately. A faster compute kernel is not evidence that the async scheduler is faster.

### Stage 1 execution boundary

Source checkout: `C:/Users/e-fur/.codex/worktrees/onity-fast-capture-f682b7c/Onity`.
Unity verification host: `C:/Users/e-fur/.codex/worktrees/onity-builder-benchmark-4be50dc/Onity`.

Stage 1 proposed source scope, relative to the source checkout:

- `Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsync.cs`
- New `OnityTaskThreadPoolSwitch.cs` in that directory and its metadata
- Focused thread-pool fixtures under `Packages/com.onity.framework/Tests/EditMode/Scripts/` and `Tests/PlayMode/`, with their metadata
- Existing task benchmark/thread-switch/smoke tools under `Packages/com.onity.framework/Benchmarks/Tasks/`
- This plan, async comparison/usage docs and root/package changelogs

One implementation writer, then an independent verification handoff. Root owns Unity execution. Preserve unrelated dirty work in both checkouts. No dependency, assembly, scene, project-setting, public-default or unrelated-module change is included. New script metadata belongs exclusively to the implementation writer; existing GUIDs remain unchanged. A new commit, push or release is not part of this stage.

Acceptance:

- Typed/untyped worker execution and main-thread return behave as documented in EditMode and PlayMode.
- Pre-cancel, queued cancel, cancel during work, delegate fault, null argument, concurrent producers, context flow on/off and session boundaries have focused coverage.
- Existing async builder, bridge, sharing, cancellation and session tests remain passing.
- Verify both default and Release optimization, then Mono/IL2CPP Player semantics using the existing isolated build scope.
- Compare equivalent UniTask operations in repeated Release runs with matching context semantics and calibrated allocation controls. Measure switching separately from compute; report regressions and unsupported cases.
- No speedup, zero-allocation, all-platform or full-parity claim without matching evidence.

## Stage 1 verification checkpoint

- Unity 2022.3.62f2: 682/682 EditMode and 46/46 PlayMode tests passed with both normal and Release code optimization. Results are `TestResults/plan13-stage1-*-verified.xml` in the verification host.
- New coverage includes 14 EditMode and five PlayMode thread-pool cases, with typed/untyped outcome matrices. Mono and IL2CPP Release Players each passed the expanded 16-case smoke suite.
- The first full run exposed a test-owned AsyncLocal value left on a reused worker. Tests now restore worker-local values and separate flow probes; production context semantics remain unchanged. Full reruns above include the correction.
- Unity Roslyn 4.3 compiled the WebGL conditional branch. IL inspection confirms the unsupported-platform guard and absence of a thread-pool queue call in that branch. This is compilation evidence, not a WebGL browser run.
- The new `threadpool` benchmark separates submission time from gated worker completion latency. Submission allocation requires a calibrated per-thread counter; cross-worker lifecycle allocation is explicitly unavailable.
- Two independent Release processes per backend completed all ten rows with eight samples each. The per-thread allocation counter was unavailable on both runtimes; no zero-allocation claim follows. Matched submission generally favored UniTask; IL2CPP Action/Func cohort completion favored Onity in these runs. No overall performance winner or default change is justified.
- [Stage 1 report, source identity and raw samples](../assets/benchmarks/onitytask-stage1-threadpool-2026-09-27.md). This stage is complete; no commit, push or release was performed.

## Stage 2 verification checkpoint

- Native JobHandle adapter and runner hooks are implemented. Review corrected the Editor quit hook: Application.quitting also fires when leaving Play Mode, so only standalone Players use it for permanent shutdown; Editor session transitions remain reopenable.
- Current runtime passed all 690 EditMode and 50 PlayMode tests with both normal and Release code optimization, with no skipped cases.
- All eight new EditMode cases pass, including two actual reload-disabled Play sessions, Awake registration before EnteredPlayMode, and intervening Edit Mode work. The corrected fixture uses EnterPlayMode(false), a runtime test probe, domain sentinels and reload-safe native cleanup. No serialization repair, unexpected forced reload or native leak was reported in the corrected focused run.
- Runtime, tests and task benchmark sources/metadata match the verification host across 462 files. Test XML identities and source hashes are retained under docs/assets/benchmarks/onitytask-stage2-jobs-2026-09-27/.
- Mono and IL2CPP Release Players each passed all 16 smoke cases and two independent jobs runs. All six compute rows validated 19 executions each, with explicit Burst proof. Every adapter panel retained two warmups and 16 measured observations per library. Original build settings were restored and the temporary scene removed.
- At 65,536 elements, serial/Burst ratios were 14.08-14.46 and plain Jobs/Burst ratios 1.21-1.35. These describe compute parallelism and compilation, not scheduler superiority. Adapter registration favored UniTask; an IL2CPP outlier is retained. Allocation remains unavailable, so no zero-allocation criterion is marked verified.
- [Stage 2 report and raw evidence](../assets/benchmarks/onitytask-stage2-jobs-2026-09-27.md). Bounded functionality/compute milestone reviewed complete; no commit, push or release was performed. Stage 3a source work follows with independent tests.

## Later-stage design constraints

### Stage 3a implementation checkpoint

- Reviewed complete as a behavior-preserving construction-allocation tradeoff. Keep its bounded eligibility and defaults unchanged; the isolated timing slice does not justify backend-specific routing. No commit, push or release was performed.
- The bounded typed coordinator, eligibility helpers and typed outcome reader are implemented and reviewed. Runtime files are frozen for independent verification.
- Existing OnityAsync, completion-source and async-builder EditMode fixtures passed 200/200 under Release optimization. The independent new fixture passed all 25 race, sharing, fallback, context and fault-observation cases in both normal and Release optimization, with zero skips. The PlayMode builder regression fixture passed its single matrix test under Release. Both Release Players passed 16/16 smoke cases; all 468 staged source/metadata hashes match.
- Two independent construction runs per backend validated every 128-aggregate cohort at input counts 2, 8 and 16 against the exact previous Task bridge path. Mono improved at 8/16 inputs; IL2CPP construction was 1.03–1.73 times slower. Calibrated heap-delta estimates were 60–93% lower. This is a bounded allocation tradeoff, not an overall speedup. Inputs and completion are outside construction timing, and result arrays are allocated at different stages. [Report and raw evidence](../assets/benchmarks/onitytask-stage3a-whenall-2026-09-27.md).

### Composition and cancellation contracts

- Preserve the existing `WhenAll<T>` output Task, multi-consumer behavior, tracking and repeated pending input bridge semantics. The initial pending optimization accepts only 1-16 unique exact built-in OnityTaskCompletionSource<T> inputs without preexisting bridges, mixed with inline/default values. Require at least one pending input. Pooled, preserved, Task-backed, derived/unknown, duplicate, prebridged or larger cohorts retain the existing fallback; empty/all-success paths remain unchanged.
- Snapshot all inputs before registration. Shareable inputs may acquire a bridge concurrently afterward; read their typed outcome under the existing source gate and preserve observed-fault bookkeeping. Preserve input-order results, original ordered exceptions (including nested aggregates), fault-over-cancel precedence and the first canceled input's token. Registration pins and active callback counts prevent early coordinator reuse; the result array escapes and is never pooled.
- Verify completion/registration races, caller-array mutation, context/tracking, concurrent bridge creation, late/repeated output consumers, fallback contracts and pool reuse before claiming the eligible input bridges were removed. The output Task and result array still allocate; this is not an all-native or zero-allocation combinator claim.
- Arbitrary-input `WhenAny` must observe every accepted loser without canceling it. Reject duplicate single-consumer identities before claiming inputs; preserved and Task-backed duplicates remain valid.
- External cancellation and timeout stop waiting; they do not cancel the producer or release its resources. Continue observing the original operation. Cancellation suppression must distinguish genuinely canceled status from a faulted OperationCanceledException.
- PlayerLoop timing needs explicit insertion points, teardown and loop-replacement handling. Same-frame Yield and strictly later-frame NextFrame are different contracts; LateUpdate is not end-of-frame.
- Add completion-order WhenEach only after async-stream cancellation and disposal ownership are verified.

### Stage 3 API details and order

- After typed WhenAll: array WhenAny, external cancellation/suppression, explicit PlayerLoop timing, timeout using that same session owner, then separately verified end-of-frame support.
- Add WhenAny(params OnityTask[]) returning OnityTask<int> and WhenAny<T>(params OnityTask<T>[]) returning OnityTask<(int winnerIndex, T result)>. Preserve existing pair overloads. Null/empty arrays throw; one/default input is supported. Snapshot before callbacks. Reject repeated single-consumer source/token identities before any claim; shareable/Task-backed duplicates are valid. Register in order: first observed terminal callback wins, lowest index for already terminal inputs. This is not an atomic completion-time snapshot. Consume every loser without canceling it; an indefinitely pending loser retains its observer.
- Only null, empty and duplicate inputs are rejected synchronously before claiming. A stale/preclaimed nonduplicate status or registration failure is an input fault outcome; keep an earlier winner and continue observing the others. Do not add a racy HasConsumer preflight or bridge invalid native inputs. Any registration exception disables coordinator pooling before registration unpins. Account a failure once but preserve an observer that may have been accepted before the throw; a late callback still consumes its outcome without changing the winner or decrementing twice. Outcome readers cover the full public completion-source inheritance family, including preserved sources and the generic bool base used by untyped preservation. Ordinary subclasses inherit explicit nonvirtual implementations of the internal source interfaces; do not exclude these supported subclasses with an exact-type whitelist. Sources outside that family use their actual interface status and result methods. This does not widen Stage 3a's separate exact-type optimization eligibility.
- Add AttachExternalCancellation(CancellationToken) extensions preserving task shape. A noncancelable token or input observed complete at entry returns the original. Otherwise pre-cancellation wins but still observes the producer; later races use one atomic winner. Preserve the external token and never cancel the producer.
- Add SuppressCancellationThrow() returning OnityTask<bool> / OnityTask<(bool isCanceled, T result)>. Only actual canceled status maps to success; a faulted OperationCanceledException remains faulted. Read status before consuming pooled input. Do not implement this with a naive async catch that changes fault status.
- Internal observers use native callbacks or Task-backed ConfigureAwait(false), without incidental creation-context capture. Token registration suppresses execution-context capture and synchronization-context use, with a pin for synchronous callback-before-assignment. Dispose registrations outside locks. Native publication may occur on the producer/canceling worker; no implicit main-thread hop is promised.
- Add Timeout(float seconds, bool useUnscaledTime = true) preserving task shape, and TimeoutWithoutException with bool / (bool isTimeout, T result) output. Require finite nonnegative seconds before claiming inputs. Completed input wins at entry; pending zero times out immediately while retaining its producer observer. Positive pending timeout requires main-thread Play/player creation. Use Unity unscaled/scaled frame clocks, checked in Update; scaled time pauses at timeScale zero.
- Only a private timer win becomes TimeoutException or the timeout flag. Producer TimeoutException and faulted OperationCanceledException remain faults; producer cancellation retains its token. Producer victory retires only the private timer. Session teardown cancels the wrapper rather than reporting a timeout.
- Invalid seconds throw synchronously before status inspection. Stale entry-status and pending registration failures become native fault outcomes, preserving any earlier winner and a possibly accepted late observer. Plain Timeout still returns an input observed complete unchanged; do not add a preclaimed-input check to that identity fast path. TimeoutWithoutException maps the completed outcome normally. Accept float.MaxValue. Store double start clock and duration, then compare elapsed time against duration: adding float.Epsilon to a nonzero clock can round an absolute deadline back to now and falsely expire a paused scaled timer. Verify that positive tiny durations stay pending until their selected clock advances.
- Begin timeout wrappers and private timer entries unpooled. The pooling conditions below are prerequisites for a later measured optimization, not a requirement to pool the first implementation. Pending timeout work allocates; do not claim a non-alloc path. Validate positive-pending session/acceptance before claiming the producer, enqueue the timer without synchronous callbacks, assign its identity, then register the producer observer. Failed loop repair faults pending wrappers through the timer retirement acknowledgment.
- Reuse the new PlayerLoop session owner: Update cancellation pass, public Update drain, then private timer drain, with epoch checks between passes. Private registrations carry entry identity/generation; worker Stop only flags retirement. Detach before exactly one main-thread sink acknowledgment (Expired, Stopped or SessionEnded). Reentrant registration during close rejects. Pool only after output release, producer observation, timer acknowledgment, registration completion and all active callbacks have ended. This avoids abandoned timers in the legacy runner's destruction path without changing legacy queues.
- Verify early result consumption followed by delayed losers/old timer acknowledgments, synchronous token callbacks, no incidental context capture, fault observation including later input bridges, default-valued success flags, producer TimeoutException, scaled/unscaled clocks and closing a session between Update subpasses.
- For exact session closure between public Update and private timer passes, tests may invoke the existing private CloseSession method from a public Update continuation. Assert cancellation rather than timeout and no subsequent timer publication; restore through existing BeginSession/Initialize methods in finally. Keep actual exit/reentry tests as well. No new runtime hook or direct lifecycle-field rewriting is allowed; failed loop repair is a separate fault-retirement check.

### Stage 3b verification and measurement boundary

- Runtime and independent tests are implemented. Review found an exact-type outcome-reader gap for public completion-source subclasses; three dedicated bridge-timing cases failed before the two-reader correction and passed afterward. All 29 new EditMode cases pass, including corrected Task callback ordering and genuine native preservation coverage.
- Full Unity 2022.3.62f2 suites pass 744/744 EditMode and 52/52 PlayMode in both normal and Release optimization, with zero skipped cases. All 439 runtime/test source and metadata files match the verification host. Release Mono and IL2CPP Players pass 18/18 smoke cases each; all 476 runtime/test/benchmark files match the build host. Build settings were restored.
- Array coordinators own retained input storage for up to 16 inputs, with a 256-entry retention cap per output shape. Snapshot into that storage and validate before initialization or observer registration. A rejected unstarted rental is cleared and returned safely. Larger sources remain unpooled. Verify rejection followed by reuse, 16-to-2 reuse, and the 32-input path.
- Independent tests cover both result shapes, duplicate rejection before any claim, supported sharing, ordered registration, caller mutation, winner outcomes, late loser observation and bridges, early output release, bounded races, stale/preclaimed failures and Task observers without context capture. Add two PlayMode thread/context checks and two Mono/IL2CPP Player smoke cases. Malicious custom internal-interface implementations have no ordinary consumer/test seam and remain a code-review case without new runtime hooks.
- Add a separate synthetic composition benchmark: typed/untyped array calls at 2, 16 and 32 inputs against pinned UniTask (12 rows). Prepare 128 fresh pending public completion-source groups and holders outside measurement, construct all outputs, complete all producers synchronously in reverse order, then consume each output once inside the slice. Validate stored winner/value results outside it. This includes source completion and loser observation costs, not isolated scheduler CPU.
- Use three warmups and eight samples of two cohorts with alternating path order, disabled trackers, documented context settings and the calibrated allocation-counter chain. Explicit array arguments avoid selecting the existing two-input overload. Cleanup completes every prepared producer and observes every constructed output at most once. Run each Release Player twice. This measures shareable-success inputs; it does not establish native single-consumer, cancellation or general performance superiority.
- All four measured runs passed all 12 rows. Onity/UniTask mean time was 1.008–1.664x on Mono and 1.416–2.142x on IL2CPP. Onity reported no measurable heap growth at 2/16 inputs, but the unpooled 32-input path measured about 6.4 kB/group on Mono and 7.5 kB/group on IL2CPP. HeapDelta is coarse and process-wide; zero readings do not prove zero allocation. Another Editor consumed CPU during these runs. Profile before changing pool capacity or synchronization.
- [Stage 3b report and raw evidence](../assets/benchmarks/onitytask-stage3b-whenany-2026-09-27.md). Functional checkpoint reviewed complete; Stage 3c can proceed. No commit, push or release was performed.

### Stage 3c implementation boundary

- Add the four cancellation extensions in OnityTaskExtensions.cs and original typed/untyped native wrapper sources in a new OnityTaskCancellationSources.cs with metadata. Reuse the array-combinator status/outcome/configured Task observer helpers. No runner, timer, assembly or dependency change is needed.
- Begin with unpooled wrappers so early output consumption cannot recycle state while a producer or cancellation callback still owns it. Pending wrappers allocate; warmed allocation remains measurable follow-up work, not a zero-allocation claim.
- Initialize the native base with Reset(default), not the external token: the legacy base cancellation path targets the runner and captures context. Own the new registration separately, suppress context flow, and reserve an atomic external winner for a pending pre-canceled input before observing the producer.
- Pin registration assignment and producer registration. A synchronous token callback can request cleanup before the registration field is assigned. Detach under a private gate and dispose outside every lock. ReleaseSource must not clear a pending producer observer. Registration failures preserve a possibly accepted late callback; observe it once without replacing an earlier winner.
- Suppression maps actual completed success/cancellation directly; pending inputs use native sources. Publish faults through protected native TrySetException, because public completion-source TrySetException converts OperationCanceledException into cancellation. Preserve faulted OCE status.
- Verify both shapes, terminal-input precedence, noncancelable identity, cancellation/producer races, synchronous registration, context suppression/restoration, nonposting Task observation, late faults with later bridges, and early consumption before producer completion.

### Stage 3c verification checkpoint

- All four APIs are implemented with XML/usage documentation and independent tests. Full default/Release suites pass 777/777 EditMode and 54/54 PlayMode; Release Mono and IL2CPP Players pass 20/20 smoke cases each. Runtime and test sources match the host; settings are restored.
- One initial PlayMode test incorrectly required exact OCE from a Task bridge, where BCL returns TaskCanceledException. Its assertion was corrected without a runtime change, retaining token/context checks. The original failure and final passes are recorded.
- Pending wrappers intentionally allocate and remain unpooled. Retained native output values retain the wrapper/external token; registration cleanup removes the reverse root. No allocation quantity, zero-allocation or speed claim is made. Public scheduling races plus review cover registration assignment; tests do not force every private interleaving.
- [Stage 3c report and evidence](../assets/benchmarks/onitytask-stage3c-cancellation-2026-09-27.md). Functional checkpoint complete; proceed to explicit timing. No commit, push or release was performed.

### Stage 3d: explicit PlayerLoop timing

- Start with OnityPlayerLoopTiming.Update, FixedUpdate and LateUpdate. Add Yield(timing = Update, cancellationToken = default). New NextFrame(timing, cancellationToken) and DelayFrames(count, timing, cancellationToken) require the token argument to preserve existing calls such as NextFrame(default) and DelayFrames(1, default).
- Insert dedicated nodes immediately after Update.ScriptRunBehaviourUpdate, FixedUpdate.ScriptRunBehaviourFixedUpdate and PreLateUpdate.ScriptRunBehaviourLateUpdate. Preserve existing waits, MonoBehaviour runner queues, JobHandle polling and main-thread dispatch positions.
- Yield waits for the next selected drain, possibly this frame; registrations during a drain wait for its next occurrence. Explicit NextFrame requires a later rendered frame. DelayFrames uses elapsed rendered-frame distance, so multiple fixed ticks cannot shorten it.
- Advance each phase's pass number before draining; for Update, do this before its cancellation pass. Requests created by a cancellation continuation target the next pass, preventing a new Yield(Update) from completing again during that same drain. Snapshot iteration bounds and check the session epoch across publication.
- New timing factories require the main thread and Play/player execution. Validate arguments first; pre-cancel does not install/enqueue. Worker cancellation is published on main at the selected phase or the Update cancellation pass, allowing paused fixed waits to cancel. Immediate cancellation remains separate work.
- Validation order is enum/count, main-thread and active-Play requirements, pre-cancellation, then the zero-count fast path. Worker/Edit calls reject even with a pre-canceled token or zero frames. A valid pre-canceled call returns canceled; otherwise DelayFrames(0, timing, token) completes immediately. Neither fast path installs nodes or enqueues a source.
- Install before scene loading. OnityTaskPlayerLoop.Initialize() repairs explicit loop replacement idempotently while retaining pending requests. Edit the current loop, remove only owned marker nodes, validate all anchors before installing, and never restore a stale/default loop snapshot. Missing anchors fault/detach existing timing requests and reject new acceptance. Do not poll GetCurrentPlayerLoop each frame.
- Initialize must inspect the current loop even when an installed flag is set. Successful repair retains pending queues, frame targets and session identity. After failed repair, an explicit Initialize can retry in the same valid Play session once the external loop has restored its anchors.
- Pinned Entities initialization appends to the current loop and preserves these nodes. A custom bootstrap that replaces the whole loop must call Initialize afterward. No new ECS dependency or arbitrary-replacement watchdog is included.
- The default ECS SimulationSystemGroup is appended after the injected after-script Update node; Yield(Update) from that group normally resumes on the next Update occurrence. Do not promise same-frame completion from every caller.
- Timing queues own their session: close acceptance and detach before cancellation callbacks, guard epochs/reentrancy, remove owned nodes on exit, and preserve Awake registrations when EnteredPlayMode follows. Destroying the legacy runner does not retire these independent queues.
- Real WaitForEndOfFrame is a follow-on coroutine-backed operation using UnityEngine.WaitForEndOfFrame, not a LateUpdate alias. Reject unsupported Editor batch/null-graphics execution; require graphics-enabled Player verification. Editor Scene-view switching can suspend this wait.
- Verify phase ordering, strict frame distances, paused cancellation, worker rejection, source reuse, idempotence, external repair, ECS coexistence, actual reload-disabled sessions and both Player backends. Allocation validation remains a separate evidence gate.
- Runtime ownership for this step is limited to public factory additions in OnityAsync.cs, new OnityTaskPlayerLoop.cs and OnityPlayerLoopTaskSource.cs with metadata. Keep legacy runner, Job registry and dispatcher code unchanged. Add the private timeout lane only when implementing Timeout, after timing verification.

### Stage 3d verification checkpoint

- Both optimization modes pass 780/780 EditMode and 64/64 PlayMode cases with zero failures/skips. Corrected fixture issues and the separately discovered legacy limitation are retained in the evidence; explicit timing runtime remained unchanged during verification.
- Non-development Release Mono and IL2CPP Players pass 22/22 smoke cases. Two independent timing processes per backend each validate 384 warmup and 2,048 measured native waits. All eight allocation samples pass calibration and same-bracket controls; HeapDelta reads zero for warmed no-token Update Yield. Other variants and exact zero GC are not established; timing has no UniTask baseline.
- All 494 runtime/test/benchmark source, assembly and metadata hashes match the verification host. Saved build/settings hashes restore and the temporary scene is removed. [Report and raw evidence](../assets/benchmarks/onitytask-stage3d-playerloop-2026-09-27.md). No commit, push or release was performed.
- Proceed to the bounded legacy-runner retirement correction below, then Timeout using the new owner. Preserve the explicit timing source snapshot and the existing primary Player harness for focused before/after legacy allocation slices.

### Discovered legacy runner teardown gap

- Stage 3d's first PlayMode run exposed an existing limitation: destroying the legacy OnityTaskRunner leaves its pending frame/delay/predicate/AsyncOperation lists without a drain. OnDestroy retires Jobs and the dispatcher, but does not retire those lists. Token callbacks only set flags, so cancellation cannot rescue abandoned entries. This predates the explicit timing owner.
- Keep Stage 3d's independence test scoped by completing the legacy setup wait before destroying its host. The new injected pending wait still must complete normally. Preserve the initial failing result as evidence of the separate limitation.
- Follow with a bounded legacy teardown correction before Timeout: cancel accepted waits without canceling their producers, preserve source versions and token identity, detach all queues before publication, protect active Tick callbacks/reuse, and allow manual replacement without affecting it from old cleanup. Closing sessions must reject replacement. No per-tick allocations or unrelated scheduling change is intended.
- Verify tokenless/token-backed work in every phase, predicates/progress/completion reentrancy, caller-owned AsyncOperation lifetime, replacement runner completion, old cancellation callbacks, explicit timing/Jobs independence and reload-disabled transitions. Final implementation requires the decision review and sequential implementation/verification handoff.
- Production ownership is OnityAsync.cs only. Store each queued source's registration version in a struct. Mark retired, detach all three queues and clear only the owned singleton before callbacks; retire old Jobs before legacy callbacks can create replacement work. Settle legacy entries even when Job retirement throws, and notify the dispatcher only if no replacement exists.
- Defer an active Tick entry until its finally block. Predicate and both progress callback boundaries, including exception paths, check owner retirement before further publication. Force cancellation checks the saved version and pending state under the existing source lock, retains the original token and invokes callbacks outside locks. Ordinary already-published completion wins; a consumed/re-rented object must not be canceled using an old queue version.
- Validate lifecycle acceptance before rental/reset/token registration. Session close captures and retires the exact old host, then destroys that captured object in finally (Destroy in Play, DestroyImmediate in Edit). OnDestroy only retires idempotently. Preserve ExecuteAlways Edit scheduling, fresh Play/Awake acceptance and manual replacement; quit/reload/transition closure rejects replacement.
- Reuse the unchanged primary Player harness for before/after plain NextFrame scheduling/GetResult allocation at 128 and 4096 operations. Retain raw samples/calibration and review ordinary Tick for allocations. Tick/full-cycle allocation and speed remain unmeasured; require a new full-cycle probe only for uncertain Tick allocations, material pooling-timing changes or an unexplained focused regression.

### Legacy retirement verification checkpoint

- The bounded correction is implemented in OnityAsync.cs. Both default and Release optimizations pass 782/782 EditMode and 73/73 PlayMode tests with no skips; both Release Players pass 24/24 smoke cases. All 500 runtime/test/benchmark source and metadata hashes match the verification host; build settings are restored and the temporary scene removed.
- Two new EditMode and nine grouped PlayMode cases verify queues, tokens, predicate/progress/completion reentrancy, same-source reuse, underlying operation lifetime, replacement ownership, paused worker cancellation and actual reload-disabled transitions. Eleven old reflection helper calls were updated for the internal Tick owner argument after the initial existing run failed ten cases; all original assertions remain.
- The unchanged primary harness shows no measured allocation increase in selected plain NextFrame scheduling/GetResult slices. Scheduling means increased 9–18% in one before/after process per backend; additional ownership/version work plausibly contributes, but its individual cost was not isolated. Burst allocations remain above the retention cap. HeapDelta does not prove zero GC allocation, and active Tick/full-cycle costs remain unmeasured.
- Functional checkpoint reviewed complete with this documented cost. No extra performance harness or speculative optimization is required. [Report and raw evidence](../assets/benchmarks/onitytask-legacy-retirement-2026-09-27.md). Proceed to Timeout; no commit, push or release was performed.

### Stage 3e: Timeout implementation checkpoint

- Four public extensions, a shared unpooled producer/timer observer, four native output shapes and the private PlayerLoop timer lane are implemented. Production scope is OnityTaskExtensions.cs, OnityTaskPlayerLoop.cs and new OnityTaskTimeoutSources.cs with metadata; no assembly/dependency or legacy-runner change.
- Source review corrected a positive tiny-duration rounding bug before verification: double elapsed time preserves paused scaled waits, and float.MaxValue remains accepted. An empty timer snapshot returns before reading Unity clocks. Producer/failure winner paths request timer retirement before output publication.
- The frozen runtime passes standalone Roslyn semantic compilation (60 sources, 210 references), actual Unity compilation and full default/Release suites: 812/812 EditMode and 82/82 PlayMode, no skips. Both fresh Release Players pass 26/26 smoke cases. All 508 source/assembly/metadata hashes match; build settings are restored and the temporary scene removed.
- New coverage adds 30 EditMode cases, nine PlayMode groups and two Player smokes. Initial 26/30 and 29/30 Edit runs exposed incorrect test expectations for repeated native AsTask conversion and completed-input identity fault bookkeeping; corrections preserve stronger pending-decorator checks and the runtime remained unchanged. [Report, earlier failures and final evidence](../assets/benchmarks/onitytask-stage3e-timeout-2026-09-27.md).
- New wrappers and entries intentionally allocate and remain unpooled. Keep late producer observation after early output consumption; no allocation quantity or performance-equivalence claim is established.

### Real end-of-frame contract after Timeout

- Add OnityTask.WaitForEndOfFrame(CancellationToken = default) using a dedicated internal host owned by the explicit PlayerLoop session. One persistent shared coroutine yields a cached UnityEngine.WaitForEndOfFrame and drains queued native waits; no per-request coroutine or legacy-runner dependency.
- Require main thread, active accepting Play session and supported rendering before handling pre-cancellation. Reject Editor batch mode and GraphicsDeviceType.Null. Completion uses the real rendering primitive and may occur in the registration frame; requests created during its drain wait for a later drain.
- Update cancellation must remain live when the coroutine stalls. Token callbacks only flag; remove each source and dispose its registration before publication. The shared coroutine never holds an individual request across a yield. Bounded pooling may reuse a consumed source only after that cleanup.
- Manual host retirement affects only that host's EOF waits; independent timing and Timeout lanes remain live. Global session closure or failed loop repair detaches timing, timer and EOF queues before the first publication. Capture an immutable generation per pump invocation, invalidate the owner's current generation before StopCoroutine, then settle detached waits. Recheck generation/retirement after every publication so a callback cannot let an old pump drain a replacement host. Handle OnDisable/OnDestroy idempotently; permit manual host replacement only after retirement finishes in an otherwise valid session.
- Headless tests prove rejection and lifecycle guards, not completion. Verify graphics-enabled Mono/IL2CPP Players without batch/nographics: non-null graphics device, Update registration pending at LateUpdate, completion after a rendering callback, small ReadPixels alternating-color proof, reentrant next-drain behavior, deliberately stalled-pump cancellation via Update, reuse and host replacement. Restore runtime test resources and retain external deadlines.
- Editor Scene-view switching can stall the Unity primitive; a controlled stalled-pump test does not claim actual Scene-view interaction coverage. Cold host/coroutine allocations are outside any later warmed-source allocation claim.

### End-of-frame Player verification tooling

- Runtime implementation checkpoint: the documented factory, scoped PlayerLoop ownership hooks and new OnityEndOfFrameRunner/OnityEndOfFrameTaskSource files are implemented and source-reviewed. Standalone Roslyn semantic compilation passes 62 sources with 210 references.

- Add an `eof` suite through a new OnityTaskEndOfFramePlayerRunner.cs and metadata in the existing benchmark assembly, plus scoped PlayerRunner dispatch and PlayerBuildRunner suite/launch changes. Reuse the existing temporary empty scene; create cameras, textures and probes at runtime. No new assembly, package or serialized setting is required.
- Only this suite launches the Player with `-screen-fullscreen 0 -screen-width 320 -screen-height 240`, omitting both `-batchmode` and `-nographics`. The build Editor may stay headless. Preserve metadata, startup trace, handshake, watchdog, fresh-report and exit-code checks. Build once per backend and run headless smoke plus graphics EOF from the same binary.
- Require standalone/non-development execution, expected backend/build identity, non-batch mode, absence of both forbidden flags, non-null graphics and a successfully created render target. Record the actual graphics API/device, active pipeline asset type, color space and screen/texture dimensions. Do not assume URP or repair unrelated graphics settings: the current host references a pipeline GUID without a matching checked package/asset, so actual runtime rendering is the gate.
- Use an owned solid-color Camera with cullingMask zero, HDR/MSAA off and a small ARGB32 RenderTexture. After warmup, register EOF in Update, require pending state in LateUpdate and alternate the camera's red/blue clear color there. The raw completion callback consumes once on the main thread, reads a pixel through a reused 1x1 Texture2D, and checks color plus matching LateUpdate/completion frame. Do not use Camera.Render or manually paint the target. This proves scheduled camera rendering; it does not measure GPU presentation latency or independently instrument every GUI pass.
- Add reentrant-next-frame, controlled shared-coroutine stall with worker-token cancellation through Update, distinct host replacement, and early source reuse/old activity cases. Reflection failure is a failure, never a skip; add no production hooks. Stopping the captured coroutine is controlled stall evidence, not proof of an actual Scene-view interaction.
- Add only explicit headless rejection to the ordinary smoke suite; never count rejection as graphics completion. Ordinary Update owns 10-second case and 60-second suite wall deadlines independent of EOF pumping. Save case traces/frame/thread/pixel metadata, capture callback exceptions, restore RenderTexture.active, join owned workers, detach/release/destroy owned camera/texture objects and retire a deliberately stalled host. Failure or cleanup failure writes a failed report and exits nonzero. Make no allocation or speed claim for this functional suite.
- Filter both Camera.onPostRender and RenderPipelineManager.endCameraRendering to the owned test camera so the render proof follows the actual built-in/SRP path. Pass expected backend and build GUID from the BuildReport through explicit launch arguments; reject missing/mismatched identities rather than deriving the expected identity from the Player itself. Existing metadata and handshake validation remain in force.

### Stage 3f: end-of-frame verification checkpoint

- Full default/Release suites pass 814/814 EditMode and 84/84 PlayMode tests, zero skips. Both graphics-enabled Release Players pass six rendering/lifecycle groups and four exact alternating-color readbacks. Each fresh binary also passes its paired 27-case headless smoke. All 520 source/assembly/metadata hashes match after execution; settings are restored and the temporary scene removed.
- Actual rendering is Direct3D11, RTX 3090, built-in pipeline and Linear color space. Both backends' four requests complete on registration frames 3–6 after LateUpdate and the owned camera's render callback, on thread 1. Stalled-pump cancellation, same-source reuse, manual retirement/replacement and global close/repair reentrancy pass. Source review fixed a harness anchor-restoration deadlock risk before execution; runtime remained unchanged.
- Graphics runs retain tracker enabled; paired smoke disables it. These are functional checks without allocation/speed claims, actual Scene-view interaction or independently instrumented GUI/presentation latency. Functional checkpoint reviewed complete. [Report and raw evidence](../assets/benchmarks/onitytask-stage3f-endofframe-2026-09-27.md). Proceed to Stage 4a; no commit, push or release was performed.

### Async streams and channels

- Put native contracts in the existing Onity.Unity.Async assembly, which already depends on Onity.Reactive: `IOnityAsyncEnumerable<T>` creates `IOnityAsyncEnumerator<T>` with Current, `OnityTask<bool> MoveNextAsync()` and `OnityTask DisposeAsync()`.
- One outstanding move per enumerator; concurrent moves reject. Normal termination remains false, faults/cancellation terminate with their original identity/token, and disposal is idempotent and awaits owned cleanup. No implicit main-thread hop.
- Start with Empty/Return/Range, WithCancellation, on-demand EveryUpdate, Select/Where/Take/FirstAsync/ToArrayAsync. Add sequential awaitable selectors/actions next. Early break, selector failure and Take must dispose the upstream exactly once.
- Add explicit BCL adapters using the installed .NET Standard 2.1 async interfaces/ValueTask. C# async-yield methods return BCL IAsyncEnumerable; custom Onity interfaces support consumption but are not compiler-generated async-yield return types.
- Channels initially have multiple producers and one consumer, unbounded or positive bounded capacity. Bounded writes wait without dropping; accepted items and waiting writers are FIFO. TryWrite cannot bypass waiting writers. Cancellation removes unaccepted waiters, while completed acceptance wins later cancellation.
- Completion drains accepted items before normal end or the original terminal error; blocked writers fail once closed. Canceling an individual operation or disposing ReadAll does not close a shared channel. Capacity bounds buffered items, not arbitrarily many outstanding WriteAsync calls.
- Push-to-pull adapters require explicit capacity and overflow behavior. Start with failure on overflow; synchronous callbacks cannot provide waiting backpressure. Subscribe per enumeration and detach exactly once, including synchronous subscribe emissions and cancellation before subscription assignment.
- Verify callback reentrancy outside locks, pending operation cancellation, disposal, waiter reuse, await-foreach break/throw, warmed allocation and IL2CPP behavior before building WhenEach.

### Stage 4a: finite async streams and synchronous operators

- Implementation started after Stage 3f's reviewed functional checkpoint. The native disposal order below is authoritative; the BCL adapter's serialized move/disposal restriction must not be copied into native operators.
- The verified host uses C# 9 and .NET Standard 2.1; BCL async interfaces and ValueTask are present. Use the existing Onity.Unity assembly and Onity.Unity.Async namespace. Both native interfaces are covariant: IOnityAsyncEnumerable<out T> returns IOnityAsyncEnumerator<T>, and IOnityAsyncEnumerator<out T> exposes get-only Current, OnityTask<bool> MoveNextAsync() and OnityTask DisposeAsync(). GetAsyncEnumerator accepts an optional CancellationToken.
- First implement reusable descriptions via OnityAsyncEnumerable.Empty<T>(), Return<T>(T value) and Range(int start, int count), plus OnityAsyncEnumerableExtensions.WithCancellation, Select, Where, Take, FirstAsync and ToArrayAsync. Select/Where use synchronous Func delegates; terminal consumers accept optional tokens. These receivers do not collide with existing reactive/AsyncOperation extensions. EveryUpdate and explicit BCL adapters follow; channels/reactive adapters remain separate steps.
- Validate null sources/delegates and negative counts synchronously. Range checks its last value using long; positive ranges beyond int.MaxValue reject, while zero count is valid for every start. Empty, Range(..., 0) and Take(0) end without acquiring upstream resources, even with a pre-canceled token. WithCancellation and terminal consumers preserve that empty outcome: FirstAsync faults as empty and ToArrayAsync returns an empty array. Nonempty built-ins check cancellation before advancing; cancellation after committed normal exhaustion cannot change false to cancellation.
- Each enumeration owns independent state. WithCancellation combines wrapper/enumeration tokens with OR semantics, including nested wrappers. Reuse a sole/equal token; create a linked CTS only for distinct cancelable tokens and dispose it after upstream cleanup. Preserve upstream cancellation tokens; cancellation generated from a linked effective token reports that token, without promising an original-token winner. Arbitrary upstream implementations must cooperate with the supplied token.
- Current is valid only after MoveNextAsync(true) and until the next move/disposal; otherwise it throws InvalidOperationException. Reject overlapping moves without advancing. Normal end remains false. Fault/cancellation stays terminal with its original status and identity/token until disposal; subsequent moves after disposal return false.
- Disposal closes acceptance, stops owned pending work and awaits cleanup. If it wins over an outstanding move, that move ends false; already-published outcomes are unchanged. Repeated disposal shares completion and never repeats cleanup. Enumerators are initially unpooled.
- Explicit disposal reserves false only when a pending move has no committed outcome. Publish that false outside the gate once active synchronous calls/Current mutation are safe; it need not wait for asynchronous cleanup. DisposeAsync itself waits for the original move observation, active calls and upstream cleanup. Cleanup failure affects DisposeAsync with its actual status and cannot replace disposal-winning false; late upstream outcomes are consumed only. Normal-end/Take/terminal-consumer cleanup still precedes committing the proposed ordinary result. If explicit disposal wins during that ordinary cleanup, use the explicit-disposal rule.
- Select/Where invoke delegates sequentially outside locks. Delegate exceptions, including OCE, are faults. Take disposes upstream once at its limit, on early disposal or failure. First disposes after the first item and faults on empty; ToArray disposes on every exit. Cleanup failure takes precedence over an earlier operation failure, matching ordinary finally behavior; do not aggregate automatically. Preserve the chosen outcome's actual fault/cancellation status, avoiding naive async catch/rethrow that changes faulted OCE to cancellation.
- Verify actual await-foreach break/throw disposal, covariance compilation, range boundaries, linked-token ownership, concurrent move/dispose, cleanup-failure precedence and faulted-OCE preservation. Native await-foreach uses pattern support; custom interfaces are not compiler-generated async-yield return types. [C# async-stream specification](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-8.0/async-streams.md).
- Implementation should reuse RegisterWhenAnyObserver/ReadWhenAnyOutcome and internal OnityTaskCompletionSource<T>.TrySetFault. Keep synchronous item/end results inline; allocate shareable sources for pending or sticky faulted outcomes and cache shared disposal completion. Never route a genuine faulted OCE through async catch/rethrow that turns it into cancellation.
- Use a small iterative pump with driving/requested flags and a registration pin, not recursive MoveNext callbacks; a long synchronous Where rejection must not grow the stack. Commit outcome/Current under a private gate, publish outside it, and never invoke upstream/user code while holding the gate. Disposal waits only for active synchronous invocations to return before calling native upstream DisposeAsync, which may itself settle the outstanding move. Observe that move and cleanup concurrently, finishing after both settle. Do not wait for the pending move before invoking native cleanup or add blanket owned CTS to these finite operators; forward the effective caller/WithCancellation token. Uncooperative upstream work may keep cleanup pending. The BCL import restriction below is different.
- Acquire upstream lazily, clean it once on normal end/failure, and retain terminal status until explicit disposal. Take awaits cleanup at its limit before publishing the last item, allowing cleanup failure to replace that proposed result. First/ToArray use the same actual-status outcome handling and cleanup precedence. Add explicit long-Where, blocked-delegate disposal, pending-dispose sharing, both completion orders, and native/preserved/Task upstream tests.
- Check closing between separate user invocations: if GetAsyncEnumerator reentrantly disposes the wrapper, clean the acquired enumerator without starting its first move; if Current reentrantly disposes Select/Where, do not invoke the selector/predicate afterward. Keep upstream calls outside locks. Supported stale/preclaimed task registration failures terminate with cleanup; an invalid internal source that accepts a callback and then throws has only best-effort late observation, not a guaranteed settlement acknowledgment.
- Descriptions, enumerators, linked CTS instances and pending completion sources may allocate; ToArray also allocates result storage. No general non-allocation claim follows from inline finite moves. New source ownership is limited to the two interfaces, enumerable factories, extensions, lifecycle pump, operators and terminal-consumer files plus metadata in the existing Unity assembly.

### Stage 4a verified checkpoint (2026-09-27)

- Seven new runtime files and their metadata implement the finite surface in the existing Unity assembly. Semantic compilation checks 69 sources/210 references; actual C# 9 await-foreach resolves native DisposeAsync. Reentrant close checks prevent a fresh move/delegate after acquisition/Current closes the wrapper.
- New Release tests pass 37 EditMode/2 PlayMode cases. Full default and Release suites pass 851 EditMode/86 PlayMode, with no skips. Initial Release Play was 85/86: an old 240-frame real-operation wait expired in 0.148 seconds. Its helper now uses five real seconds with unchanged predicates/assertions; focused 11-case and both full Play reruns pass. Runtime remained frozen.
- Both Release Players pass paired 29-case smoke. Each also measures two finite scenarios after explicit lazy-chain priming, with three warmups and two runs of eight windows. All windows read zero HeapDelta with 0/69,632-byte controls; this is retained-heap evidence, not exact zero allocation or a UniTask comparison. Construction, priming, linked tokens, pending sources and array consumers are outside the probe.
- All 540 runtime/test/benchmark source hashes match source and host after execution; all three settings hashes are restored and the temporary scene is absent. Release EditMode preceded only the Play-fixture deadline and final benchmark corrections; its runtime/Edit fixtures were unchanged. [Report and raw evidence](../assets/benchmarks/onitytask-stage4a-streams-2026-09-27.md). Functional checkpoint review passed. No commit, push or release was performed; proceed to Stage 4b.

### Stage 4b: on-demand Update and BCL adapters

- Implementation started after Stage 4a's reviewed checkpoint; preserve its frozen lifecycle base and existing finite behavior.
- Player verification consumes the public Unit result directly, so the existing benchmark assembly also needs a direct reference to the already installed Onity.Core assembly. This is a test integration reference, with no new runtime assembly, package or dependency version; keep the existing asmdef metadata GUID.
- After finite streams, add OnityAsyncEnumerable.EveryUpdate() returning IOnityAsyncEnumerable<Onity.Core.Unit>, AsOnityAsyncEnumerable<T>(IAsyncEnumerable<T>) and AsAsyncEnumerable<T>(IOnityAsyncEnumerable<T>). Validate null adapter sources synchronously. Unit matches the existing Onity reactive streams and pinned UniTask's value-less AsyncUnit. Add OnityEveryUpdateAsyncEnumerable.cs and OnityAsyncEnumerableAdapters.cs with metadata, plus scoped factory/extension additions; no owner, assembly or dependency changes.
- EveryUpdate factory/enumerator creation is inert. Each accepted move schedules one existing Yield(Update, token), returns Unit.Default and obeys Stage 4a Current/overlap rules. Before-node requests may complete that frame; reentrant moves wait for a later Update pass. There is no subscription, buffering or replay while the consumer is idle. Active moves require main-thread Play acceptance and validate the current session; session-retired pending work becomes terminal.
- Each active enumeration lazily owns its cancellation source, linked when necessary without canceling/disposing the caller's CTS. Worker disposal may request cancellation, but pending Unity waits settle through the main-thread Update pass. Disposal that wins ends its move false, observes the underlying wait and awaits cleanup; dispose owned tokens only after pending work and active cancellation invocations finish. Idle disposal needs no Unity access.
- BCL imports must serialize upstream move and disposal: never call upstream DisposeAsync while its last MoveNextAsync ValueTask remains unconsumed. Lazily acquire with an owned linked token, hold one ValueTask, observe with ConfigureAwait(false)/UnsafeOnCompleted, read its terminal status before exactly one GetResult, then dispose upstream once. Disposal closes acceptance and signals owned cancellation outside locks; an uncooperative upstream move can keep disposal pending. Repeated disposal shares the adapter completion, never reconsumes the upstream ValueTask. [BCL async-stream design](https://github.com/dotnet/runtime/issues/27547), [ValueTask consumption](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask?view=netstandard-2.1).
- BCL exports observe each native move/disposal once with existing actual-status helpers. Synchronous success may return inline ValueTask values; pending/fault/canceled outcomes initially use explicit TaskCompletionSource-backed ValueTasks with cached disposal completion. Faulted OCE uses TrySetException; only actual cancellation uses TrySetCanceled(token). Never normalize status through naive async catch/rethrow, mix direct ValueTask consumption with AsTask, or consume a reusable source twice.
- Both directions preserve Current, sticky terminal state, disposal-winner and cleanup-precedence rules. Forward/compose tokens without taking caller ownership. Keep upstream calls, token callbacks and output publication outside gates, with initialization/active-call pins. Synchronous invocation/Current exceptions, including OCE, are faults; internal observation captures neither creator SynchronizationContext nor ExecutionContext.
- Verify native/BCL await-foreach break/throw cleanup, independent enumerations, phase placement, no accumulated ticks while idle, worker disposal/cancellation, session retirement and token ownership. Include Task-backed and reusable IValueTaskSource-backed inputs, a real compiler-generated async iterator whose pending move is consumed before disposal, move/cleanup faulted OCE versus cancellation, completion orders, cleanup precedence and both Player backends. Enumerators, CTS/linkage, pending sources and adapter Task results may allocate; pooling and zero-allocation claims remain deferred.
- Implementation packet: keep the verified finite lifecycle base unchanged. EveryUpdate and BCL import derive from it. EveryUpdate maps one existing Yield(Update) outcome to true with native actual-status observation; Yield validates each active call's thread/session before pre-cancellation. BCL import confines its consume-before-dispose restriction to a cleanup coordinator, without changing native cleanup ordering. Check closing between GetAsyncEnumerator and its first move, and between separate user callbacks.
- Share a small private token-lifetime owner inside the new adapter files. Lazily create an owned CTS and forward a cancelable caller token through suppressed-context, non-synchronization-context registration. Pin registration assignment and every owned Cancel invocation. Cleanup closes forwarding and requests cancellation outside gates; finish only after registration detachment, active cancellation calls, pending move observation and upstream cleanup. Dispose the owned CTS at that final boundary; caller cancellation can synchronously finish the move and reenter disposal, so a naive untracked linked CTS is insufficient. Cancellation callback failures fault cleanup while observation and upstream disposal still proceed; later upstream cleanup failure has ordinary finally precedence.
- BCL upstream disposal can start once its held Move ValueTask has been consumed and synchronous upstream calls have returned. Observe upstream cleanup and cancellation quiescence concurrently; quiescence is a final cleanup barrier, not a prerequisite for starting upstream disposal. Snapshot ValueTask terminal status before its single GetResult, since a reusable source may invalidate that status during consumption. Verify this with a real async iterator, a reusable IValueTaskSource and a callback-before-registration-return case.
- ValueTask awaiter registration is also an active synchronous upstream call. A callback may consume the terminal result during OnCompleted, but upstream disposal waits for registration to return; pin cleanup registration likewise before final token disposal and shared cleanup publication. Resume the pump after releasing that pin rather than blocking the callback. Verify with a held registration and an independently released gate.
- BCL registration failure does not consume a pending ValueTask. Retain that obligation, recheck terminal status once after the throw and when applicable after owned cancellation returns, and consume at most once through the existing observer arbitration or an actually accepted late callback. Never re-register, poll, use AsTask as a repair, or start BCL disposal before move consumption. Preserve an earlier committed outcome; otherwise registration failure is the proposed fault, with later cleanup-failure precedence. The cleanup ValueTask has the same consumption obligation before final token disposal. A malformed, uncooperative source may leave cleanup pending indefinitely; the finite native adapter's bounded invalid-source rejection is not a BCL termination guarantee.
- Export through a private identity enumerator using the existing native source-enumerator base. Forward the caller token and convert each native outcome once; cache shared BCL disposal completion before invoking native cleanup outside the gate. The adapter adds no internal creator-context dispatch, while a caller awaiting the exported Task-backed ValueTask retains normal BCL context behavior. Test internal nonposting observation separately from ordinary caller await capture.

### Stage 4b verification checkpoint

- EveryUpdate and both BCL adapters are implemented with independent tests and public usage/XML documentation. Frozen finite lifecycle code remains unchanged. Source review fixed a lost-notification window in concurrent status observation and suppressed Mono export-bridge creation context before Unity execution.
- Full Unity 2022.3.62f2 suites pass 879/879 EditMode and 90/90 PlayMode in normal and Release optimization, zero skipped. New focused tests pass 28/28 EditMode and 4/4 PlayMode. The initial 26/28 run is retained: canceled exception identity and normal-exit probe repair were fixture errors, corrected without production changes; worker failures are explicitly observed.
- Fresh non-development Release Mono and IL2CPP Players pass 31/31 smoke cases each, including actual compiler iterators and reusable ValueTask sources. All 552 runtime/test/benchmark source, assembly and metadata hashes match both checkouts after execution; 509 runtime/test hashes identify the full Editor runs. Saved settings are restored and the temporary scene is removed.
- [Stage 4b report and raw evidence](../assets/benchmarks/onitytask-stage4b-adapters-2026-09-27.md). Independent closure review is GO for functionality. Pending adapters allocate; no allocation quantity or comparative speed claim. Channels are the next bounded packet; awaitable operators follow separately. No commit, push or release was performed.

### Stage 4c: channels and sequential awaitable operators

- Keep the retained surface: OnityChannel.CreateUnbounded<T>() and CreateBounded<T>(int capacity), returning sealed OnityChannel<T> with sealed OnityChannelReader<T> Reader and OnityChannelWriter<T> Writer handles. Reader exposes TryRead(out T), ReadAsync(CancellationToken = default) and ReadAllAsync(CancellationToken = default); writer exposes TryWrite(T), WriteAsync(T, CancellationToken = default) and TryComplete(Exception error = null). Positive bounded capacity is required. No abstract hierarchy, options, drop modes or separate Completion API.
- Multiple producers share one gate and a single consumer lease. FIFO is acceptance order under that gate, not cross-thread invocation time. A bounded read promotes the oldest queued writer into the freed slot before publishing continuations; TryWrite cannot bypass waiting writers. Use a bounded ring and growable unbounded queue, with removable FIFO waiter nodes rather than cancellation scans/tombstones.
- ReadAll uses a dedicated small enumerator governed by that same channel gate, not the finite-stream base. Dequeue, Current assignment and successful move commitment must be atomic with lease/cancellation/disposal decisions. A generic base could dequeue first and later let disposal replace its uncommitted move with false, losing the item. Keep the finite base unchanged; no reservation/rollback or transaction hook is needed.
- A disposal/cancellation win before delivery detaches only the pending reader and preserves the item for a replacement reader. Delivery committed first stays true, although later disposal invalidates Current. Direct empty-buffer handoff commits writer acceptance and reader delivery together; bounded dequeue promotes the oldest waiting writer in the same transaction. Capture publication packets under the gate, then detach registrations and invoke callbacks outside it. Explicitly test both disposal/delivery orders and cancellation versus FIFO promotion.
- ReadAll acquires its lease on the first move and holds it through terminal cleanup. A pending standalone ReadAsync holds a lease until its outcome commits. Conflicting ReadAsync rejects before consumption; TryRead returns false when a lease is already held. Immediate direct reads reserve/consume atomically. Idle enumeration cancellation is observed at the next move/disposal; abandoned enumerators must be disposed.
- ReadAll retains its captured token and GetAsyncEnumerator token directly, with no linked or child CTS. Before acceptance, check the captured token first, then the enumeration token; if both are pre-canceled, report the captured token. A pending move registers sole/equal tokens once or distinct cancelable tokens twice, under one initialization pin spanning both assignments. The first cancellation callback committing under the channel gate supplies the exact reported token. Shared active-callback tracking protects both registrations until cleanup; dispose them outside the gate and never cancel caller sources. Delivery already committed stays true. Do not internally wrap this enumerator in the finite WithCancellation base.
- Conflicting ReadAsync, conflicting first ReadAll move and overlapping ReadAll moves throw InvalidOperationException synchronously before consumption/state changes, matching the existing native enumerator's overlap guard. Pre-canceled direct ReadAsync still returns canceled before lease acquisition/conflict checks. A failed first-move lease acquisition leaves the enumeration unacquired and retryable. An overlap rejection does not disturb the accepted move or Current. Operational channel closure/errors remain task outcomes.
- Pre-canceled direct reads/writes return canceled tasks before acquiring leases or accepting items. Pending cancellation removes only uncommitted waiters; committed acceptance wins subsequent cancellation. Enumeration cancellation/disposal releases its lease after cleanup without closing the channel or discarding buffered data. Disposal-winning pending moves return false; committed delivery is unchanged. Old callbacks carry lease identities so they cannot affect a replacement reader.
- TryComplete commits once; accepted buffered items drain before end/error and unaccepted writers fail. ReadAll ends normally or faults with the original error. ReadAsync after normal closure faults with a new sealed OnityChannelClosedException : InvalidOperationException; error closure faults with the original error. Rejected writes use OnityChannelClosedException with the terminal error as InnerException. An error supplied as OCE remains Faulted, never canceled.
- Reserve outcomes/detach waiters under the gate; dispose registrations and invoke continuations outside it. Cover completion before cancellation-registration assignment and active cancellation callbacks with initialization/active-call pins. Never block on CancellationTokenRegistration.Dispose while holding the channel gate. Pending waiters/sources start unpooled.
- Add SelectAwait<T, TResult>(source, Func<T, CancellationToken, OnityTask<TResult>>), WhereAwait<T>(source, Func<T, CancellationToken, OnityTask<bool>>) and ForEachAsync<T>(source, Func<T, CancellationToken, OnityTask>, CancellationToken = default) extensions on the native stream interface. Receiver types keep these distinct from reactive operators.
- Awaitable operators run one upstream move and delegate task at a time, consuming each native output once. Synchronous delegate exceptions, including OCE, are faults; actual returned canceled/faulted status is retained. Disposal signals an owned delegate token and observes pending delegate work. Once active synchronous invocations return, native upstream DisposeAsync may start even while that delegate task remains pending; own disposal awaits every outstanding observation and cleanup. Uncooperative work may keep cleanup pending. Reuse the iterative pump, shared disposal completion and cleanup-failure precedence; no implicit Unity/context hop.
- Bounded source files: OnityChannel.cs, OnityChannelReader.cs, OnityChannelWriter.cs, OnityChannelClosedException.cs, private channel waiters and OnityAsyncStreamAwaitOperators.cs with metadata; scoped stream-extension additions. Existing assembly and no dependency changes. Implement channels and awaitable operators sequentially, with separate verification handoffs.
- Verify FIFO/backpressure, no TryWrite bypass, head/middle/tail cancellation, acceptance/cancel/close races, buffered error drain, faulted OCE, reader conflicts/disposal/replacement, synchronous callbacks and registration handshakes. Verify sequential delegates, long synchronous WhereAwait rejection without recursion, exact consumption and cleanup precedence. Run both Player backends and warmed bounded TryWrite/TryRead allocation validation. Pending waits, descriptions, registrations and unbounded growth may allocate; state those limits.

### Stage 4c channel verification checkpoint

- Added bounded/unbounded channels, Reader/Writer handles, ReadAll enumeration,
  closed exception and private waiters in five new runtime files. Existing
  runtime, assemblies and dependencies stayed unchanged.
- Initial builder/stream regression passed 107 Release EditMode cases. New
  channel fixtures passed 34 EditMode and 2 PlayMode cases; full suites passed
  913 EditMode/92 PlayMode cases in each normal/Release optimization, all CLI 0
  with no skips. Review strengthened the disposal-publication test after full
  suites; the final 34-case fixture passed both optimizations without a runtime
  change. Both fixture hashes and the original snapshot are retained.
- Release Mono and IL2CPP each pass 33 smoke cases from the same binary as
  their measurement. Warmed buffered TryWrite/TryRead at capacities 1/128 have
  16 valid HeapDelta-zero windows per capacity/backend; empty/held-64-KiB
  controls read 0/69,632 B. One process per backend, two internal runs; no
  exact-zero, pending-wait, unbounded-growth or comparative-speed claim.
- All 568 final source/host hashes match after execution, settings hashes are
  restored and the temporary scene is absent. [Report and raw evidence](../assets/benchmarks/onitytask-stage4c-channels-2026-09-27.md).
  Independent functional closure review passed. No commit, push or release was
  performed. Awaitable operators follow next.

### Stage 4c awaitable-operator implementation packet

- Start after the separate channel checkpoint. Add one private implementation file
  and metadata, with the three scoped public extension additions above. Preserve
  the finite base, source enumerator, terminal consumers and token lifetime helper.
- Derive coordinator enumerators directly from OnityAsyncEnumeratorBase<TOutput>;
  the source-enumerator wrapper hides upstream ownership needed for delegate cleanup.
  Observe one upstream move, read Current once and observe at most one delegate per
  internal move. Store the selected value or predicate result. Let the existing
  iterative base consume synchronous false predicates without recursive chaining.
- Keep a private iterative driving/requested pump and invocation/registration pins.
  Later upstream callbacks can invoke user delegates after the base's synchronous
  MoveCore pin has ended. Check closing between separate user calls; publish outside
  locks and retain accepted operations until observed exactly once.
  Commit and retire each operation before publishing from locals with driver
  ownership released. Callback/finally paths retain exact operation identity and
  must not clear a reentrant successor's state.
- Pass the original enumeration token upstream. Lazily create the existing token
  lifetime for the first delegate and pass its owned token to delegates. Empty
  upstream can end normally when pre-canceled. If upstream ignores cancellation and
  yields an item, check the owned token before invoking its delegate. Successful
  delegates that ignore cancellation remain successful; synchronous thrown OCE is
  Faulted, while returned task status and cancellation token remain authoritative.
- Cleanup closes delegate admission and signals the owned token outside locks. Once
  active invocations and registration calls return, start native upstream disposal
  even if a delegate remains pending. Own disposal waits for upstream/delegate
  observations, upstream cleanup and token quiescence before releasing references.
  Upstream cleanup failure precedes cancellation-callback failure; either supersedes
  ordinary completion or prior operation failure. An explicitly committed false
  remains false, with cleanup failure reported by DisposeAsync.
  Support nonblocking cancellation callback reentry and await callback quiescence;
  do not add hidden worker dispatch to break arbitrary blocking user callbacks
  that wait for cleanup before allowing their own synchronous Cancel call to return.
- ForEach begins immediately and uses a terminal enumerator plus the existing
  Consumers.Read helper. Map its bool completion to untyped OnityTask with a small
  actual-status observer in the new file. Use native observers and TrySetFault;
  avoid async-builder normalization or Task conversions.
- Verify inline/suspended/Task-backed outcomes, exact consumption, faulted OCE,
  real cancellation, empty and cancellation-ignoring sources, 100,000 synchronous
  false predicates, reentrant disposal, pending delegate cleanup, token callback
  reentry/failure and cleanup precedence. Include Mono/IL2CPP smoke. Coordinators,
  tokens and pending observers start unpooled; make no zero-allocation or speed claim.

### Stage 4c awaitable-operator verification checkpoint

- Runtime implementation and extension hashes remained fixed throughout verification.
  Initial EditMode timing and Player single-use-gate fixture failures were corrected
  without production changes; both original failures and source snapshots remain.
- Focused Release fixtures pass 34 EditMode/2 PlayMode. Full suites pass 947/94
  in normal and Release optimizations, zero skipped, CLI exit 0.
- Fresh Release Mono and IL2CPP Players each pass 35 smoke cases, CLI exit 0.
  All 574 source/host hashes match. The sole host backend setting left after
  the failed run was restored with the host closed; all three original settings
  hashes now match, and the temporary scene is absent.
- Independent functional closure review passed. Pending state allocates; no
  allocation quantity or comparative speed claim. [Report and raw evidence](../assets/benchmarks/onitytask-stage4c-await-operators-2026-09-27.md).
  No commit, push or release was performed. Proceed to the reactive adapter packet.

### Stage 4d: reactive adapters, then completion-order consumption

- Add AsOnityAsyncEnumerable<T>(this IOnityObservable<T> source, int capacity) and AsObservable<T>(this IOnityAsyncEnumerable<T> source). Validate positive capacity/null synchronously. Push overflow is fixed Fail behavior, with no policy enum/drop modes. Implement reactive adapters and verify them before WhenEach.
- Subscribe lazily once per enumeration with the public Subscribe(OnityObserver<T>) surface; IOnityObservableV2<T> is internal. Buffer FIFO. Overflow rejects the new value, stops acceptance and detaches, then drains accepted values before an InvalidOperationException identifying overflow. OnError and failed OnCompleted become Faulted, including OCE; successful completion drains then ends.
- Cancellation/disposal stops callbacks, clears discarded buffered references and detaches once. Disposal-winning pending moves end false; independent cancellation retains its token. Subscription initialization must handle synchronous values/completion/overflow/cancellation before the returned IDisposable is assigned. Dispose that token once outside gates. Subscribe throwing without a returned token stops observation but cannot fabricate an unsubscribe handle. Retain cleanup-failure precedence/shared disposal completion.
- The reverse adapter needs a dedicated IOnityObservable<T> implementation; existing OnityObservable<T>(delegate) forwards only values. Each subscription owns a sequential enumerator pump. Dispose upstream before terminal notification: normal end uses OnCompleted(OnityResult.Success()), failure uses one OnError(original), independent cancellation uses OnError(OCE with token). Subscriber disposal suppresses later notifications and observes asynchronous cleanup; post-disposal cleanup errors go to OnityObservableExceptionHandler. A throwing observer stops the subscription and reports through that handler, never recursively notifying the same observer.
- OnityResult has no canceled-status field: native cancellation converted through an observable returns as faulted OCE. Document that limit without widening OnityResult. There is no implicit thread hop; callers remain responsible for observable subscription/disposal thread affinity, and this adapter does not make Subject<T> thread-safe.
- Reuse the frozen finite base for observable-to-stream enumeration: each subscription owns its private buffer, and cancellation/disposal explicitly discards it. MoveCore signals availability/end; ReadCurrentCore dequeues under the adapter gate. A disposal win between dequeue and base commitment is permitted for this private buffer, unlike the shared channel. Pre-canceled first Move skips Subscribe. After subscription, completion/error drains accepted values; cancellation before consumer outcome reservation discards them and uses the original enumeration token, without rewriting committed delivery or a reserved terminal outcome.
- Pin the full Subscribe invocation plus returned-handle assignment, cancellation registration and active callbacks. Synchronous callbacks may request detach before assignment; dispose the captured handle once afterward, outside gates. Late publication/finally paths retain exact operation identity and cannot clear successor state. Unsubscription failure follows cleanup-failure precedence.
- A null returned subscription is an empty/no-op handle, matching existing public OnityObservable and V2 normalization. Stop the adapter's own observer and buffer during cleanup, but do not fabricate an unsubscribe capability when the source supplies none.
- The reverse pump reserves each observer invocation with an active-call pin and invokes outside gates. Dispose immediately closes future notifications; an already-entered callback may return. Once synchronous calls finish, start native upstream Dispose even with a pending Move, then observe both plus owned-token quiescence before terminal notification. A throwing observer is reported once through the global handler and closes the subscription; an independent cleanup failure is reported separately, never recursively through that observer. Use native actual-status observers, not async-builder normalization.
- The new reverse adapter's value-only Subscribe(Observer<T>) uses a private lifecycle wrapper: forward values, ignore successful completion, and route terminal source faults or independent cancellation OCE to OnityObservableExceptionHandler. This is explicit adapter policy, since the existing base observer's default OnError is a no-op. Explicit subscription disposal suppresses its cancellation terminal; a throwing value callback is reported once by the pump without a second wrapper OnError notification. Document the policy on AsObservable and leave existing primitives unchanged.
- Reverse subscription teardown owns one observer.Dispose call, matching existing subscription conventions. Natural termination performs native cleanup, terminal notification, then observer disposal in finally. Explicit subscription disposal suppresses terminal notification and disposes the observer after active callbacks return, without waiting indefinitely for asynchronous upstream cleanup. Invoke OnDisposed outside gates and never concurrently with an active OnNext/terminal callback; report its exception globally without skipping upstream cleanup.
- Implement this reactive packet in one new private adapter file plus metadata and scoped extension additions. Keep channel, finite-base, consumers, token helper and existing reactive primitives unchanged. Verify the reactive packet before starting WhenEach.
- Bounded reactive measurement uses two pending-delivery rows, each 4096 items per window, three warmups and two runs of eight measured windows. Push: real Subject<int> to a capacity-one adapter; prime a pending move plus sentinel delivery outside, then measure pending Move, synchronous OnNext, exact consumption/Current and scalar accounting per item. Reverse: a benchmark-only native source owns preallocated public completion-source slots; prepare its observable subscription and sentinel outside, then measure 4096 slot completions, value callbacks and registration of each next pending move. Keep a final slot pending for explicit cleanup outside. Producer-slot allocations, description/subscription setup and teardown are excluded; public dispatch, observation state and accounting are included. Use the existing calibrated counter with same-bracket empty and held-64-KiB controls; record all raw samples, environment, counter/rejection reasons and count/checksum. Restore GC before cleanup and settle accepted work in finally. HeapDelta is heap growth, not exact allocated bytes or a zero-GC proof. Run fresh Mono/IL2CPP Players plus paired smoke; no extra buffered/subscription-lifecycle rows or speed comparison.
- Add OnityTask.WhenEach<T>(params OnityTask<T>[]) returning IOnityAsyncEnumerable<OnityWhenEachResult<T>>, plus untyped OnityTask[] returning results of Onity.Core.Unit. One readonly result type exposes Index, IsCompletedSuccessfully, IsFaulted, IsCanceled, Result (default on non-success), Exception (fault only), CancellationToken and GetResult() that returns/throws the preserved outcome.
- Snapshot inputs and reject null/duplicate single-consumer identities before claims; empty arrays produce an empty stream. Cohorts permit only one enumerator, unlike reusable finite descriptions. The first move starts all observation; pre-cancellation/disposal before starting claims none. Register in input order, so already-completed inputs appear in that order; concurrent results use coordinator commit order rather than claimed wall-clock order.
- Emit one result per input; producer fault/cancellation is a result value, not stream failure. Stale/preclaimed/registration-failing inputs emit a fault result for their index while other registrations continue. Keep result-accounted and producer-observed states separate for accepted-before-throw callbacks. Once started, early disposal/cancellation stops delivery but observes every accepted producer, never canceling it. Discard later results/clear buffered references. Use an initially unpooled coordinator with registration/active-callback pins and input-count-bounded storage; producer callbacks must not block on buffer capacity.
- Reuse the finite base for WhenEach consumption, with a separate observation coordinator and a private N-result ring. MoveCore signals readiness; ReadCurrentCore dequeues and clears one result. Discarding a private item when disposal wins is permitted. Factory validation uses the existing single-consumer identity helper/equality on the snapshot, without status or preclaimed preflight; use a WhenEach-specific duplicate error. Repeated shareable/Task/inline inputs remain legal.
- One CAS-protected GetAsyncEnumerator acquisition applies even to empty cohorts. Empty first Move returns false despite pre-cancellation; nonempty pre-canceled first Move cancels without claiming inputs. Pre-start disposal also claims none. Once startup is committed, register every input in index order despite concurrent delivery cancellation/disposal; hold a startup pin through the loop and enumeration-token registration assignment.
- Use native RegisterWhenAnyObserver/ReadWhenAnyOutcome, with separate observed-once and result-accounted-once state per input. Registration failure can account one fault; a callback accepted before that failure still consumes its input without emitting a second result. Non-null fault means Faulted; unexpected nonterminal status becomes an invalid-operation result. Do not repair malformed sources with AsTask or re-registration.
- WhenEach disposal waits its own startup/registration, consumer publication and cancellation-registration cleanup, but does not wait independent unfinished producers. Close delivery and detach wake callbacks, clear buffered/consumer references, and let accepted producer observers consume late outcomes and release completed input references. Direct token registration suppresses context flow, uses no synchronization context and never cancels producers.
- The readonly result's default value is invalid: all terminal flags are false and GetResult throws InvalidOperationException. Successful results return their value; faults retain the original exception through EDI; cancellation throws OCE with the actual token. Keep implementation to OnityWhenEachResult.cs, OnityWhenEachEnumerable.cs, their metadata and two factories/XML in OnityAsync.cs; frozen helpers remain unchanged.
- Scope: reactive adapter implementation, OnityWhenEachResult.cs, OnityWhenEachEnumerable.cs and metadata, scoped stream extensions and two OnityTask factories in the existing assembly. Verify synchronous Subscribe/assignment races, overflow, late callbacks, throwing cleanup, independent subscriptions, reverse terminal/error-handler behavior; then snapshot/duplicate/empty inputs, controlled ordering, OCE versus cancellation, early disposal/late faults, registration failures, exact consumption and reentrancy. Both Player backends and bounded allocation evidence are required. Document subscription/enumerator/coordinator/buffer/waiter allocations; no general zero-allocation claim.

### Unity adapter order

- The generic native AsyncOperation adapter already exists. Add payload-specific adapters without changing overload resolution of existing AsOnityTask calls: AsAssetOnityTask, AsAssetBundleOnityTask and AsWebRequestOnityTask.
- Existing FirstOnityTask and Unit ToOnityTask remain Task-backed for compatibility. FirstAsync currently uses a value-only subscription and a RunContinuationsAsynchronously Task; replacing it with a native public completion source would change lifecycle interpretation and continuation dispatch even if sharing were retained. Native conversion is deferred. New lifecycle-aware pull consumption can use the separately verified reactive stream adapter and its FirstAsync operator instead.
- An operation adapter observes an already started operation. Resource/bundle cancellation stops observation only; it does not unload, dispose or abort caller-owned work. A native Send replacement must preserve the existing web request error and abort contract, with Unity calls on the main thread.
- After stream ownership is verified, add UnityEvent and UI Toolkit event waits/streams. Buffer value snapshots, never pooled EventBase or transient InputAction.CallbackContext objects. Preserve explicit owner/destroy/disable lifetime semantics.
- Add task-to-coroutine conversion before hosted coroutine-to-task conversion. Preserve native single-consumer behavior, exact result/fault observation and nested disposal; do not simulate unsupported Unity yield instructions.
- Scene adapters retain deferred activation ownership. GPU readback and optional Addressables integration need their own data-lifetime/dependency decisions and verification. No new third-party runtime package is included.

### Stage 5a: typed operation payloads

- Add AsAssetOnityTask(ResourceRequest, Action<float> onProgress = null, CancellationToken = default) and the same for AssetBundleRequest, returning OnityTask<UnityEngine.Object>; AsAssetBundleOnityTask(AssetBundleCreateRequest, Action<float> = null, CancellationToken = default) returning OnityTask<AssetBundle>; AsWebRequestOnityTask(UnityWebRequestAsyncOperation, Action<float> = null, CancellationToken = default) returning OnityTask<UnityWebRequest>. No generic casts/all-assets family or changes to existing AsOnityTask/GetAwaiter/AsTask overloads.
- Validate null synchronously, then main-thread/current accepting session before Unity property access, then pre-cancellation without progress/payload reads, then completed fast path. Pending work composes the existing generic native AsyncOperation adapter with one status-aware observer and static payload projectors. Progress remains clamped operation.progress plus final 1; projection/progress exceptions remain faults, including OCE. Do not normalize through async catch/rethrow.
- Resource/bundle null payload is a successful Unity result. Web ConnectionError/ProtocolError/DataProcessingError maps to the existing OnityUnityWebRequestException; success returns the same caller-owned request. This observes an already-started request: never SendWebRequest/Abort/Dispose or own handlers. Existing Send retains its separate start/abort/shareable Task contract.
- Pending outputs are single-consumer with Preserve/AsTask sharing escapes. Cancellation/retirement stops observation without unloading or disposing caller resources. Start with a small unpooled mapping source and document allocation limits; any pooling requires established registration/active-callback/release pins and measured evidence.
- Scope: new OnityUnityOperationTaskExtensions.cs and internal mapping source/metas in existing Unity assembly. Verify actual null/missing resource results, payload identity, completed/pending/precanceled paths, progress faults/OCE, underlying lifetime after cancellation, consumer/bridge/preserve behavior, thread guards, retirement and deterministic local web success/failure/cancellation. Real bundle fixtures require explicit ownership of generated fixtures; do not infer permission to mutate existing package/assets.
- Preserve deferred SceneLoader behavior: after activation-disabled load starts, cancellation no longer aborts its ownership obligation, it returns the operation at progress >= 0.9, and progress exceptions log/disable reporting. Generic isDone observation would strand that operation. Existing scene Task APIs remain for a separate ownership-aware conversion.

### Native DOTS wait companion

- Existing Task.Yield loops are in OnityDotsIntEventAsync. Onity.Unity already references Onity.DOTS, so put a separately reviewed OnityDotsTask companion in Unity without a reverse assembly reference. Proposed native methods: WaitForAccumulatorAtLeast, WaitForAccumulatorChange and WaitForNextAccumulatorUpdate, returning OnityTask<int> and reading the public bridge TryGetAccumulatedValue.
- Retain existing Task-returning *Async APIs. New waits poll only on explicit main-thread Update in an accepting session; cancellation/retirement stops observation, never jobs or producers. A missing world remains pending, query exceptions fault, and the adapter owns no cached EntityManager/query. Next-update preserves snapshot-value-change semantics (net-zero changes do not complete); it is not an every-event stream. Verify as a separate bounded follow-up after payload adapters.

## Sources

- [UniTask 2.5.11](https://github.com/Cysharp/UniTask/releases/tag/2.5.11)
- [Burst type support](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html)
- [Unity 2022.3 IL2CPP](https://docs.unity3d.com/2022.3/Documentation/Manual/IL2CPP.html)
- [Unity 2022.3 WebGL restrictions](https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-technical-overview.html)
- [Unity 2022.3 WaitForEndOfFrame](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/WaitForEndOfFrame.html)
- [Unity 2022.3 batch-mode coroutine support](https://docs.unity3d.com/2022.3/Documentation/Manual/CLIBatchmodeCoroutines.html)
- [Current API and performance comparison](../guide/onitytask-comparison.md)
- [Verified Player baseline](12-OnityTask-PlayerVerification.md)
