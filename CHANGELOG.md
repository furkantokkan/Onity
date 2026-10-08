# Changelog

All notable changes to the Onity framework are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.4] - 2026-10-08

### Added

- Scene service: `IOnitySceneService` / `OnitySceneService`, bound once on the `ProjectContext` with
  `container.BindSceneService(profile, defaultCover, preloader, revealPolicy)`. `LoadAsync(OnitySceneRequest)`
  and `UnloadAsync(Scene)` return `OnityTask<OnitySceneResult>` and complete only when the exact destination
  scene's `SceneContext` is ready, its cover has lifted and replaced scenes have unloaded, also when the
  profile routes a Single load through the Loading scene (its `OnityLoadingSceneInitiator` takes the
  operation's handoff; without one it keeps the pending-target flow). One operation runs at a time; others
  get `Busy`. Results tell success from `InvalidTarget` (missing or ambiguous names), `NotLoaded`,
  `Disposed`, `LoadFailed`, `ReadinessFailed` and `ReadinessTimedOut`. Requests choose the default cover, no
  cover or their own, can start an `OnityScenePreloader` scene (`WithPreparedScene`), and load additively
  with an explicit activation choice. `State` publishes the step and progress only on change, `Completed`
  each result once. Enter data is kept per destination scene
  (`OnitySceneTransitionStore.TryGetEnterData<T>(Scene, out T)`) and mirrored into the shared slot.

### Documentation

- The Lifecycle and Scopes guide gains a scene service section with an installer and usage example; the AI
  usage guide, the `onity-use` skill, the package README, `AGENTS.md` and `llms.txt` describe the scene
  service and its rules.

## [0.8.3] - 2026-10-07

Fix scope initialization and teardown, reactive subscription ownership, timer
callbacks, UI resolution and Editor save isolation. Public API signatures and
dependencies are unchanged; the pool counts and TryGet APIs from 0.8.1 remain.

### Fixed

- DI: stop Tick, FixedTick and LateTick dispatch when a callback disposes its
  scope. Inherited scoped services now join the child scope's initialization
  and tick lifecycle, including services resolved during or after Build.
  Asynchronous initialization joins the existing BuildAsync queue.
- Pooling and reactive cleanup: drain all owned items when a destroy/dispose
  callback throws, and preserve items added during reentrant cleanup.
  Cleanup reports collected failures as an AggregateException after draining.
- Reactive operators: roll back acquired subscriptions when a later Merge,
  CombineLatest or Sample subscription fails. Recheck TakeUntil cancellation
  and completion at subscription time. Timed buffers retain ownership after
  observer failures and release upstream when their time provider fails.
- Timers: keep registry iteration stable when callbacks add or remove timers,
  and stop interval catch-up immediately after Stop, Pause or Dispose.
- UI: remove the exact resolver registration when scope tokens are disposed
  out of order, and skip disposed presenter-factory bridges during restoration.
- Editor: gather contexts under every scene root, and save only the intended
  ProjectContext prefab instead of saving unrelated dirty assets.
- Analyzer: ONITY002 no longer flags valid Resolve calls after Build.
- Tests: enforce the no-LINQ policy against actual runtime source roots and
  resolve deferred-load scene fixtures in either repository layout without
  changing the project's Build Settings.

## [0.8.2] - 2026-10-05

Scene changes can run behind a screen cover (a fade, an image, an iris wipe or
a custom cover) that lifts only once the new scene's `SceneContext` is ready,
and a scene can be prepared hidden behind the active one and started in one
frame. The DI, reactive, messaging, OnityTask and pooling code is unchanged.

### Added

- Covered scene transitions in `Onity.Unity.SceneFlow`:
  `OnityCoveredSceneTransition` runs one scene change at a time behind an
  `IOnitySceneCover`, waits until the new active scene's `SceneContext` is
  ready (`IOnitySceneReadiness`, `OnityActiveSceneReadiness`,
  `OnitySceneRevealPolicy` with settle frames and a maximum wait), then hides
  the cover. `RunAsync(change, showCover, token)` chooses per change whether
  the default cover shows, `RunAsync(change, cover, token)` uses another cover,
  and both return `Task<bool>`, false when another change runs. The change gets
  the transition's lifetime token; the caller's token ends only the caller's
  wait.
- Cover views on UI Toolkit: the abstract `OnitySceneCoverView` (unscaled
  timing, curves, cancellation, input blocking, no per-frame allocation; a
  derived cover implements `ApplyProgress`), `OnityScreenFadeView` (USS color
  or an optional image) and `OnityIrisCoverView` (a `Painter2D` circle wipe
  around `Center`), with the templates `OnityScreenFade.uxml`/`.uss` and
  `OnityIrisCover.uxml`/`.uss` under `Runtime/Unity/UI/SceneFlow`.
- `OnityScenePreloader`: prepares a scene additively behind the active one
  (`PrepareAsync`) and starts it in one frame (`TryStartAsync`), hiding
  (`OnitySceneVisuals.Hide`) and unloading the scene it replaces. The prepared
  scene reads its prepare data with `TryGetPrepareData<T>(Scene, out T)`, kept
  per loading scene instead of the shared `OnitySceneTransitionStore` slot,
  and binds an `IOnityPreparedScene`. The preloader knows nothing about covers;
  the two compose inside the transition's scene change.
- `OnitySceneScopes.TryFind(Scene, out SceneContext)` finds a loaded scene's
  context.

## [0.8.1] - 2026-10-05

Pools report their counts and can decline a get without throwing, prefab pools
take a one-time create hook, and a prefab pool no longer loses count of its
checked-out instances on `Clear()`. The DI, reactive, messaging and OnityTask
code is unchanged.

### Added

- Pools: `CountAll`, `CountActive` and `CountInactive` properties on
  `OnityObjectPool<T>` and `PrefabComponentPool<T>`, and `TryGet(out item)`
  with one- and two-parameter forms, which return `false` instead of throwing
  when a fixed-size pool has every item checked out. A caller no longer keeps
  its own active counter beside a fixed-size pool.
- `PrefabComponentPool<T>` takes an optional `actionOnCreate` that runs once for
  each new clone, prewarmed ones included, while the clone is inactive under its
  parent and before its first `Awake` and `OnEnable`, so one-time setup no longer
  needs a rent-all-then-release loop. A throwing hook destroys that clone.

### Fixed

- `PrefabComponentPool<T>` lost count of checked-out instances on `Clear()`,
  because `UnityEngine.Pool.ObjectPool<T>.Clear()` resets its `CountAll`. A
  fixed-size prefab pool could then create more than `maxSize` instances,
  `Prewarm` created too many, and diagnostics reported a wrong `CountActive`.
  The pool now counts its checked-out instances itself.

## [0.8.0] - 2026-10-03

Onity.Messaging is faster than MessagePipe 1.8.1 in all nine rows of the
published IL2CPP Release Player comparison, measured against the MessagePipe
Unity package that Unity users install and against its .NET build, after a
redesign of the channel subscriptions and of the async publish pass, with no
public API change. One behavior change is listed under Changed. The reactive,
DI and OnityTask code is unchanged, so the 0.7.0 results stand.

### Added

- A define-gated messaging comparison benchmark (`Benchmarks/Messaging`,
  assemblies `Onity.MessagingBenchmarks` and `Onity.MessagingBenchmarks.Editor`
  under `ONITY_MESSAGING_BENCHMARKS`): nine workloads (publish to no, one and
  eight subscribers, subscribe and dispose with one and with 64 residents,
  keyed publish and keyed subscribe and dispose over 16 keys, async publish to
  one and to eight handlers) run through Onity.Messaging and MessagePipe 1.8.1
  with a library-independent golden model, an untimed probe and alternating
  library order, a Release Player build runner
  (`OnityMessagingBenchmarkPlayerBuildRunner`), a Player entry point
  (`OnityMessagingBenchmarkPlayerRunner`), and the host tools
  `tools/benchmark-host/run-messaging-comparison.ps1` and
  `messaging-summary.py`. MessagePipe is measured in one of two builds: the
  NuGet netstandard2.0 dll, or the Unity package compiled against UniTask under
  the host define `ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK`. The harness
  README holds the pre-registered classification rule. A normal project never
  compiles the benchmark.
- EditMode tests for the new core: `MessageChannelSubscriptionTests` (17 cases
  for the slot bookkeeping: removal of the first, middle and last subscriber,
  double and late dispose, subscribe and dispose during a pass, nested publish,
  a throwing handler, channel dispose during a pass, and the keyed channel) and
  `OnityAsyncMessagingPassTests` (20 cases for the async pass: the inline pass,
  the pending continuation, faulted and canceled results, the token checks,
  dispose and subscribe during a pass, nested and overlapping passes, and the
  single consumption of a synchronously completed `IValueTaskSource`), and
  `OnityAsyncMessagingContextTests` (10 cases that pin what the pass does to
  the caller's `AsyncLocal<T>` value and `SynchronizationContext`, each for a
  non-`async` handler, an `async` handler that completes synchronously, a
  handler after the first pending one, while the pass is pending and after it
  finishes, and the synchronous prefix of a non-`async` first pending handler).
- Documentation: the comparison page `docs/comparisons/messaging-vs-messagepipe.md`
  and the evidence record
  `docs/assets/benchmarks/messaging-surpass-messagepipe-2026-10-02.md` with the
  three run summaries beside it.

### Changed

- Messaging core, no public API change: a `MessageChannel<T>` or
  `AsyncMessageChannel<T>` subscription is one object, both the entry the
  channel keeps and the `IDisposable` returned to the subscriber, and it knows
  its slot. Dispose runs at most once and removes its slot in constant time:
  outside a publish pass the last subscriber moves into the freed slot; during
  a pass the slot is cleared and the slots are compacted in order after the
  outermost pass, after the async channel has copied its handler array once so
  the passes in flight keep the array they hold. In `MessageChannel<T>`,
  delivery order, delivery to a handler added during a pass, skipping of a
  handler removed during a pass, nested publish and exception propagation are
  unchanged; `AsyncMessageChannel<T>` still delivers each pass to the handlers
  registered when it started. `Publish` and `PublishAsync` return at once when
  the channel has no subscriber.
- `AsyncMessageChannel<T>.PublishAsync` is no longer an `async` method.
  Handlers whose `ValueTask` completes synchronously run inline; the first
  pending one hands the rest of the pass to an awaiting continuation. The
  handler array is copied only when a subscription is disposed while a pass
  holds it (copy-on-write), instead of a snapshot copy on every publish; each
  pass still delivers to the handlers registered when it started. In-flight
  passes are counted, where a bool flag used to be cleared early by a nested
  publish. Failures complete the returned `ValueTask` exactly as an `async`
  method would: canceled for `OperationCanceledException`, faulted otherwise,
  never thrown synchronously.
- Behavior: because `PublishAsync` is no longer an `async` method, a handler
  that is not itself an `async` method runs its synchronous work in the
  caller's context, whether it returns a completed `ValueTask` or is the first
  to return a pending one. An `AsyncLocal<T>` value or a
  `SynchronizationContext` it sets stays visible to the caller after
  `PublishAsync` returns, as with the synchronous `Publish`, OnityTask at its
  default (`FlowExecutionContext` off) and MessagePipe's Unity build for a
  handler of that shape. An `async` handler keeps its own scope, as in 0.7.0:
  its builder restores the context, so the caller does not see its change.
  Handlers after the first pending one run inside the continuation, and the
  caller does not see their changes either.
- IL2CPP null checks and array-bounds checks are off on the hot messaging
  members through `Onity.Messaging`'s own internal copies of the
  `Unity.IL2CPP.CompilerServices` attributes
  (`OnityMessagingIl2CppCompilerServices.cs`), as `Onity.Reactive` has since
  0.7.0.

### Performance

- Messaging, 2026-10-02 Release Player comparison (Unity 2022.3.62f2, Windows
  x64, MessagePipe 1.8.1, three processes per backend; pre-registered rule:
  faster when the median Onity/MessagePipe time ratio is at most 0.95 and the
  worst process at most 1.00). The headline run measures the MessagePipe Unity
  package, the build Unity users install (`MessagePipe.1.8.1.unitypackage`
  compiled against UniTask 2.5.10), at source `52e209a`, which is the shipped
  runtime `4cc5763` plus the harness's Unity-package flavor. On IL2CPP
  Onity.Messaging is faster in all nine rows, median ratios 0.150 to 0.846,
  worst process 0.895:

  | Scenario | Onity ns/op | MessagePipe ns/op | Median ratio | Worst process |
  | --- | ---: | ---: | ---: | ---: |
  | `PublishNoSubscribers` | 2.25 | 5.32 | 0.416 | 0.423 |
  | `Publish1` | 6.21 | 9.16 | 0.726 | 0.767 |
  | `Publish8` | 20.97 | 39.40 | 0.526 | 0.532 |
  | `SubscribeDispose` | 63.49 | 377.52 | 0.168 | 0.193 |
  | `SubscribeDispose64` | 60.13 | 417.30 | 0.150 | 0.216 |
  | `KeyedPublish` | 28.35 | 67.83 | 0.418 | 0.436 |
  | `KeyedSubscribeDispose` | 104.37 | 552.26 | 0.172 | 0.189 |
  | `AsyncPublish1` | 27.32 | 43.86 | 0.623 | 0.740 |
  | `AsyncPublish8` | 103.19 | 126.20 | 0.846 | 0.895 |

  Mono, reported and not gated: faster in all nine rows, median ratios 0.280 to
  0.756, worst process 0.862. Against MessagePipe's .NET build (the NuGet
  netstandard2.0 dll, whose async publish is an `async ValueTask` method) the
  same runtime was faster in all nine IL2CPP rows (medians 0.197 to 0.807,
  worst process 0.974) and in all nine Mono rows (0.280 to 0.762, worst
  process 0.830). The Unity package's `async UniTask` publish measured cheaper
  (IL2CPP `AsyncPublish1` 43.86 against 78.49 ns, `AsyncPublish8` 126.20
  against 167.42 ns), consistent with UniTask's builder running the state
  machine without the ExecutionContext scope the `async ValueTask` builder
  pays, a code fact the measurement does not isolate; the claim therefore
  stands on the Unity package.
  The released 0.7.0 runtime, measured first as the baseline against the .NET
  build, was faster in seven IL2CPP rows and slower in `AsyncPublish1` (1.138)
  and `AsyncPublish8` (1.301). Gen-0 collections during the three
  subscribe-and-dispose rows fell from 426 to 430 per row to 90 to 93 in the
  Unity-package run and 92 to 93 in the NuGet run (IL2CPP, sum over three
  processes). Allocation bytes were not measured on either
  backend. Evidence:
  `docs/assets/benchmarks/messaging-surpass-messagepipe-2026-10-02.md`.
- Reactive, DI and OnityTask: their code is unchanged in 0.8.0, so the 0.7.0
  results below stand (reactive: faster than R3 1.3.0 and than UniRx 7.1.0 in
  all nine IL2CPP rows; DI: Onity Baked fastest in all seven scenarios in every
  process; OnityTask: faster than UniTask 2.5.11 in all 29 gated IL2CPP rows).

### Tested

- Unity 2022.3.62f2 EditMode, batch mode, with default and with Release code
  optimization: 2,409 tests, 2,406 passed, 0 failed, 3 explicit benchmarks
  skipped.
- Unity 2022.3.62f2 PlayMode, batch mode: 255 tests. Default code optimization:
  252 passed, 0 failed, 2 skipped, 1 inconclusive. Release code optimization:
  251 passed, 0 failed, 3 skipped, 1 inconclusive. The skipped and inconclusive
  cases are the same explicit benchmark, graphics-device and `AsyncGPUReadback`
  tests as in 0.7.0.
- The suites ran on the runtime this release ships (source `4cc5763`), with the
  test suites at `c3ef04a`.
- Every sample of both libraries matched the golden model, and the checksums
  of Onity and MessagePipe were identical, in every process of the three
  messaging comparison runs.

## [0.7.0] - 2026-10-02

Onity.Reactive is faster than R3 1.3.0 and than UniRx 7.1.0 in all nine rows of
the published IL2CPP Release Player comparison after a redesign of its
subscription and delivery core, with no public API change and no change to
reactive behavior. The documentation is rewritten around one canonical scene
and the measured comparisons, and the package ships the updated guides. The DI
and OnityTask results of 0.6.0 stand for 0.7.0: DI was re-measured on the
published 0.6.0 package, and the only async change is a fix on the worker-thread
registration path of stateless waits, which the async gate does not run.

### Added

- A define-gated reactive comparison benchmark (`Benchmarks/Reactive`,
  assemblies `Onity.ReactiveBenchmarks` and `Onity.ReactiveBenchmarks.Editor`
  under `ONITY_REACTIVE_BENCHMARKS`): nine synchronous workloads run through
  Onity.Reactive, R3 1.3.0 and UniRx 7.1.0 with a library-independent golden
  model and rotated library order, a Release Player build runner
  (`OnityReactiveBenchmarkPlayerBuildRunner`), a Player entry point
  (`OnityReactiveBenchmarkPlayerRunner`), and the host tools
  `tools/benchmark-host/run-reactive-comparison.ps1` and `reactive-summary.py`.
  The harness README holds the pre-registered classification rule. A normal
  project never compiles the benchmark.
- EditMode tests for the new core: `OnityReactiveSubscriptionListTests` (24
  cases for the node list and the subscription lifecycle),
  `OnityReactiveOperatorSinkTests` (23 cases for the sink-based operators) and
  `OnityReactiveSingleSubscriberTests` (25 cases for the lone-subscriber
  delivery path: subscriber-count transitions, and subscribe, unsubscribe,
  throw, nested publish and dispose inside the callback, each compared with the
  general pass).
- Documentation: a comparisons hub (`docs/comparisons/`) with the reactive
  comparison page, a measurement-history archive (`docs/archive/`), and the
  evidence records `docs/assets/benchmarks/reactive-surpass-r3-2026-10-02.md`
  (with the five run summaries beside it) and
  `docs/benchmarks/di-remeasure-2026-10-02.md`.

### Changed

- Reactive core, no public API change: a subscription is one internal node
  (`OnityObserverNode<T>`) that is both the entry the source keeps and the
  `IDisposable` returned to the subscriber. `Subject<T>` and `ReactiveProperty<T>`
  keep their nodes in an array-backed `OnityNodeList<T>`: removal outside a
  notification is an O(1) slot swap, removal during a notification clears the
  slot and the list compacts after the outermost notification ends, and nodes
  added during a notification are reached by the pass in progress.
  `ReactiveProperty<T>` owns its node list.
- While exactly one live subscriber is registered, `Subject<T>.OnNext` and
  `ReactiveProperty<T>.SetValue` deliver to it through a direct reference the
  node list keeps, without the general loop, in their own exception region
  under the same depth bookkeeping. A subscribe, unsubscribe or dispose inside
  that callback falls back to the general pass from the next slot, so a
  subscriber added during the callback still receives the value and no
  observable behavior changes.
- One exception region per notification pass in `Subject<T>.OnNext` and
  `ReactiveProperty<T>.SetValue`. A throwing observer is still reported to
  `OnityObservableExceptionHandler`, and delivery continues with the next
  observer.
- An internal node fast path (`IOnityNodeSource<T>`) on `Subject<T>`,
  `ReactiveProperty<T>` and the operator observables: operator sinks and
  `Subscribe(this IOnityObservable<T>, Action<T>)` subscribe without creating a
  delegate, and the action is stored in the node and invoked directly. Other
  `IOnityObservable<T>` implementations keep the public `Subscribe(Observer<T>)`
  path.
- `Where`, `Select`, `DistinctUntilChanged`, `Skip`, `SkipWhile`, `Take`,
  `TakeWhile`, `StartWith`, `Scan` and `Pairwise` are sink-based operators
  (`OnityOperatorObservable<T>`, `OnityOperatorSink<TSource, TResult>`), with
  `Where` followed by `Select` fused into one sink. Their semantics are
  unchanged: `Take` and `TakeWhile` dispose upstream on completion, `Skip(0)`
  returns the source, `Take(0)` returns `Empty<T>()`, and argument validation
  and exception types are the same.
- IL2CPP null checks and array-bounds checks are off on the hot reactive classes
  through `Onity.Reactive`'s own internal copies of the
  `Unity.IL2CPP.CompilerServices` attributes (`OnityIl2CppCompilerServices.cs`),
  which IL2CPP recognizes by name; node delivery is an instance method so that
  generic calls carry no per-call class-initialization check.
- The documentation is rewritten: `README.md`, the package `README.md`, the
  site home, Getting Started (one canonical scene: `PlayerDamaged`,
  `GameInstaller`, `HealthService`, `HealthHud`, `HitMarker`, `DamageZone` and
  a scene-free test), every guide, reference and migration page, the AI usage
  guide, `AGENTS.md`, the `onity-use` skill, the Architecture page, the DI
  comparison and the performance guide. The dated measurements those pages
  carried moved verbatim to `docs/archive/`. The `onity-use` agent skill (also
  shipped as `Documentation~/AI/skills/onity-use/SKILL.md`) documents project
  and scene scopes (ProjectContext prefab, installer lists,
  SceneContext/GameObjectContext parent lookup), re-awaitable frame waits and
  pool retention for bursts; the repository `onity-develop` skill records the
  development and release rules.

### Fixed

- OnityTask: a stateless frame or yield wait (`NextFrame()`, token-less
  `DelayFrames(n)`, `NextFixedFrame()`, `NextLateFrame()`, or a `Yield()` used as
  a task) that was already due when a worker thread registered its continuation
  ran that continuation inline on the worker. It now resumes on the main thread
  at the next drain of its timing, as a worker registration on a pending wait
  always did; a wait timed to `FixedUpdate` therefore resumes at the next fixed
  step. A worker registration that arrives just after a Play session closed now
  runs at once instead of waiting in a queue that might never drain. Main-thread
  registrations are unchanged. Regression tests:
  `WorkerRegistration_OnADueFrameWait_ResumesOnTheMainThread` and
  `WorkerRegistration_OnADueYieldWait_ResumesOnTheMainThread`.

### Performance

- Reactive, 2026-10-02 Release Player comparison (Unity 2022.3.62f2, Windows
  x64, R3 1.3.0 and UniRx 7.1.0, three processes per backend, source `36c68c5`;
  pre-registered rule: faster when the median Onity/other time ratio is at most
  0.95 and the worst process at most 1.00): on IL2CPP Onity.Reactive is faster
  than R3 in all nine rows, median ratios 0.145 to 0.805, worst process 0.811,
  and faster than UniRx in all nine rows, 0.129 to 0.872, worst process 0.874.
  Mono, reported and not gated: faster than R3 in seven rows, on par in
  `PropertySetSame` (1.040), slower in `CombineLatestOnNext` (1.378, not
  redesigned); against UniRx faster in five rows, on par in `PropertySetSame`
  (1.001) and `WhereSelectOnNext` (1.015), slower in `SubjectOnNext1` (1.098)
  and `CombineLatestOnNext` (1.527). Allocation was not measured on either
  backend. The published 0.6.0 runtime, measured first as the baseline, was
  slower than R3 in four IL2CPP rows and in every Mono row. The R3 gate (every
  IL2CPP row faster than R3) failed at the first attempt at the new core
  (`b73c20e`, `SubjectOnNext1` 1.061) and passed at the second (`9563453`, with
  single-subscriber publish on par with UniRx at 1.044); the UniRx follow-up
  gate (every IL2CPP row faster than both libraries, at most two attempts)
  passed at its first attempt, `36c68c5`. Evidence:
  `docs/assets/benchmarks/reactive-surpass-r3-2026-10-02.md`.
- DI, 2026-10-02 Windows IL2CPP release Player re-measurement of the published
  0.6.0 package (Unity 2022.3.62f2, 19 generated activators, three processes):
  Onity Baked fastest in all seven scenarios in every process, Onity/VContainer
  0.21 to 0.59 per process. 0.7.0 does not change DI. Evidence:
  `docs/benchmarks/di-remeasure-2026-10-02.md`.
- OnityTask: the measured paths are unchanged from 0.6.0 (the fix above touches
  only worker-thread registration), so the 2026-10-02 gate (29 of 29 IL2CPP
  rows, median ratios 0.085 to 0.808) stands.

### Tested

- Unity 2022.3.62f2 EditMode, batch mode, with default and with Release code
  optimization: 2,362 tests, 2,359 passed, 0 failed, 3 explicit benchmarks
  skipped.
- Unity 2022.3.62f2 PlayMode, batch mode: 255 tests. Default code optimization:
  252 passed, 0 failed, 2 skipped, 1 inconclusive. Release code optimization:
  251 passed, 0 failed, 3 skipped, 1 inconclusive. The skipped and inconclusive
  cases are the same explicit benchmark, graphics-device and `AsyncGPUReadback`
  tests as in 0.6.0.
- The suites ran on the runtime this release ships (source `b49ea69`).
- Checksums matched across Onity, R3 and UniRx in every process of the reactive
  comparison.

## [0.6.0] - 2026-10-02

OnityTask is faster than UniTask 2.5.11 in all 29 gated rows of the published
IL2CPP Release Player gate (default context flow, pool retention matched for
large bursts; see Performance), covers UniTask's runtime API under Onity names
apart from the gaps listed in the OnityTask comparison guide, and connects async
work to Onity's DI scopes, reactive properties and message bus.
The package now ships its documentation and AI assistant guidance. Read
**Breaking** before upgrading.

### Added

- `OnityTaskVoid` and its method builder for fire-and-forget
  `async OnityTaskVoid` methods, plus the `OnityTask.Void`, `OnityTask.Action`
  and `OnityTask.UnityAction` factories in UniTask 2.5.11's 17 shapes. A fault
  goes to `OnityTaskScheduler`, which drops `OperationCanceledException` by
  default; the builder allocates nothing for a method that completes without
  suspending.
- `OnityTask.Create`, `Defer`, `Never` and `Lazy` (`OnityAsyncLazy`,
  `OnityAsyncLazy<T>`, `ToAsyncLazy`), generic `FromException<T>` and
  `FromCanceled<T>`, `Status` with `OnityTaskStatus` and
  `OnityTaskStatusExtensions`, `ToString()`, `OnityTask<T>.AsOnityTask()` and
  `AsUnitTask()`.
- `ContinueWith` (8 forms) and `Unwrap` (10 forms); `AsValueTask`, implicit
  `ValueTask` / `ValueTask<T>` conversions and `ValueTask.AsOnityTask()`;
  `Task.AsOnityTask(useCurrentSynchronizationContext)`.
- An implicit `OnityTask<T>` to `OnityTask` conversion: a view on the same
  source and token that neither consumes nor allocates.
- Cancellation helpers: `ToCancellationToken` (with an optional link token),
  `CancellationToken.ToOnityTask()`, an awaitable `WaitUntilCanceled()`,
  `IDisposable.AddTo(CancellationToken)`, `RegisterWithoutCaptureExecutionContext`,
  `IsOperationCanceledException` and `OnityCancellationTokenEqualityComparer`.
- Destroy-bound cancellation: `GetCancellationTokenOnDestroy` for MonoBehaviour,
  GameObject and Component, and `CancellationTokenSource.RegisterRaiseCancelOnDestroy`
  for GameObject and Component (UniTask parity).
- `OnityTaskScheduler` (`UnobservedTaskException`,
  `PropagateOperationCanceledException`, `UnobservedExceptionWriteLogType`,
  `DispatchUnityMainThread`) and `Forget(handler, handleExceptionOnMainThread)`.
- `OnityTask(<T>).ToObservable()`, a native `IOnityObservable<T>.ToOnityTask(ct)`
  that completes with the last value, and `ToOnityTask(useFirstValue, ct)`;
  `OnityProgress.Create<T>` and `CreateOnlyValueChanged<T>`.
- `OnityAutoResetTaskCompletionSource` and `OnityAutoResetTaskCompletionSource<T>`:
  pooled, single-consumption, version-reset completion sources (UniTask
  `AutoResetUniTaskCompletionSource` parity).
- `SwitchToTaskPool`, `SwitchToSynchronizationContext`,
  `ReturnToSynchronizationContext` and `ReturnToCurrentSynchronizationContext`
  (`await using` scopes), `SwitchToMainThread(timing, ct)`,
  `ReturnToMainThread(ct)` and `ReturnToMainThread(timing, ct)`,
  `Post(action, timing)`, and six more `RunOnThreadPool` overloads (state and
  async delegates) with the existing main-thread return rules.
- Coroutine interop: `OnityTask.ToCoroutine(factory)` and `task.ToCoroutine(...)`,
  awaiting an `IEnumerator`, `WithCancellation`, `ToOnityTask(timing, ct)`
  (driven by the PlayerLoop), `ToOnityTask(MonoBehaviour)` and
  `MonoBehaviour.StartAsyncCoroutine`.
- OnityTask adapters for `ResourceRequest`, `AssetBundleRequest`,
  `AssetBundleCreateRequest`, `UnityWebRequestAsyncOperation` and
  `AsyncGPUReadbackRequest`, plus `OnityProgress.Create`.
- `AsyncOperation.AsOnityTask(IProgress<float>, ct)`, `await` on a `JobHandle`
  and `JobHandle.WaitAsync(timing, ct)`, `AsInstancesOnityTask` for
  `AsyncInstantiateOperation(<T>)` (Unity 2022.3.20+ and Unity 6),
  `Awaitable(<T>).AsOnityTask()` (Unity 2023.1+), the `UnityWebRequest`,
  `IsNetworkError`, `IsHttpError`, `Error`, `Text` and `ResponseHeaders`
  members of `OnityUnityWebRequestException`, and the awaiters'
  `SourceOnCompleted`.
- `OnityPlayerLoopTiming` gains 16 members appended after `LateUpdate`
  (values 3-18): `Initialization`, `LastInitialization`, `EarlyUpdate`,
  `LastEarlyUpdate`, `FixedUpdateBegin`, `LastFixedUpdate`, `PreUpdate`,
  `LastPreUpdate`, `UpdateBegin`, `LastUpdate`, `PreLateUpdate`,
  `LastPreLateUpdate`, `PostLateUpdate`, `LastPostLateUpdate`, `TimeUpdate` and
  `LastTimeUpdate`. Their nodes install on first use; the three existing
  timings keep their values and eager nodes. UniTask's `Update` and
  `FixedUpdate` are `UpdateBegin` and `FixedUpdateBegin`.
- `OnityTaskPlayerLoop.Initialize(params timings)`, `InitializeAll()`,
  `IsInjected(timing)`, `DumpCurrentPlayerLoop()`, `AddAction(timing,
  IOnityPlayerLoopItem)`, `AddContinuation(timing, action)`, `IsMainThread`,
  `MainThreadId` and `UnitySynchronizationContext`.
- `Yield()` and `Yield(timing)` returning `OnityYieldAwaitable`, `Yield(ct)`,
  `Yield(ct, cancelImmediately)` and `Yield(timing, ct, cancelImmediately)`;
  `NextFrame()`, `NextFrame(ct, cancelImmediately)` and
  `NextFrame(timing, ct, cancelImmediately)`; `DelayFrame(count, timing, ct,
  cancelImmediately)`; `WaitForFixedUpdate()` and
  `WaitForFixedUpdate(ct, cancelImmediately)`; `WaitForEndOfFrame(MonoBehaviour)`
  and `WaitForEndOfFrame(MonoBehaviour, ct, cancelImmediately)`.
  `cancelImmediately` publishes a cancellation on the canceling thread.
- Timed waits on any timing: `OnityDelayType` (`DeltaTime`,
  `UnscaledDeltaTime`, `Realtime`), `Delay(TimeSpan, ignoreTimeScale or
  OnityDelayType, timing, ct, cancelImmediately)`, `WaitForSeconds(float or
  int, ...)`, `WaitUntil` / `WaitWhile` with a timing or a state,
  `WaitUntilCanceled(ct, timing, completeImmediately)` and
  `WaitUntilValueChanged`. Integer-millisecond `Delay` overloads are
  deliberately absent because `OnityTask.Delay(2)` already means two seconds.
- `OnityPlayerLoopTimer` (`Create`, `StartNew`, `Restart`, `Restart(TimeSpan)`,
  `Stop`, `Dispose`, `IsRunning`); `CancelAfterSlim(TimeSpan or int
  milliseconds, OnityDelayType, timing)`; `OnityTimeoutController(OnityDelayType,
  timing)`, its link-source constructor and `Timeout(int millisecondsTimeout)`;
  `Timeout` and `TimeoutWithoutException(TimeSpan, OnityDelayType, timing,
  taskCancellationTokenSource)`.
- `OnityTask.SourcePoolCapacity` (default 256), the per-source-type retention
  cap next to `RunnerPoolCapacity` (default 128); the equivalent of UniTask's
  `TaskPool.SetMaxPoolSize`.
- `OnityTrackedTaskInfo.IsNative` and a constructor overload with `isNative`.
- Composition: `WhenAll` and `WhenAny` over `IEnumerable<OnityTask>` and
  `IEnumerable<OnityTask<T>>`; `WhenAny<T>(OnityTask<T> left, OnityTask right)`
  returning `(bool hasResultLeft, T result)`; generated typed tuple
  `WhenAll<T1..T15>` and mixed-type `WhenAny<T1..T15>` returning
  `(int winArgumentIndex, T1 result1, ...)` on pooled native sources; `WhenEach`
  with `OnityWhenEachResult<T>`; `await` on task arrays, sequences and typed or
  untyped tuples (2-15 elements); `IEnumerable<T>.Select` to task sequences.
- Async lifecycle triggers (UniTask parity): `AwakeAsync`, `StartAsync`,
  `OnEnableAsync` / `OnDisableAsync` handlers, `OnDestroyAsync`,
  `GetAsync<Name>Trigger()`, and `OnityAsyncTriggerBase<T>` as an
  `IOnityAsyncEnumerable<T>` (`using Onity.Unity.Async.Triggers;`).
- 55 generated MonoBehaviour message triggers with `GetAsync<Name>Trigger()`,
  one-shot `<Message>Async(ct)` waits and reusable handlers, and the public
  `OnityTriggerEvent<T>` and `IOnityTriggerHandler<T>`. Physics, physics 2D and
  particle triggers compile only with their Unity modules (`ONITY_PHYSICS`,
  `ONITY_PHYSICS2D` and `ONITY_PARTICLESYSTEM` version defines); mouse triggers
  are compiled out on iOS, Android and WSA, as in UniTask.
- `UnityEvent` and `UnityEvent<T>` waits (`OnInvokeAsync`,
  `OnInvokeAsAsyncEnumerable`, `GetAsyncEventHandler`) and the eight
  `IOnityAsync*EventHandler` interfaces. UI Toolkit (beyond UniTask):
  `Button.OnClickAsync`, `VisualElement.OnEventAsync<TEvent>`,
  `INotifyValueChanged<T>.OnValueChangedAsync`, their stream forms, and
  `BindTo` for `TextElement` and value controls, which never raises a
  `ChangeEvent` and unbinds when the element leaves its panel.
- The optional `Onity.Unity.UGUI` assembly, compiled only when `com.unity.ugui`
  is installed (Onity adds no package dependency): 17 EventSystems triggers,
  `Button`, `Toggle`, `Scrollbar`, `ScrollRect`, `Slider`, `InputField` and
  `Dropdown` waits, streams and handlers, and `BindTo` for `Text` and
  `Selectable.interactable`.
- Reactive stream adapters: `IOnityObservable<T>.AsOnityAsyncEnumerable(capacity)`
  (lazy, bounded FIFO, overflow faults after draining accepted values) and
  `IOnityAsyncEnumerable<T>.AsObservable()` (sequential pump per subscription,
  terminal after native cleanup). Cancellation round-trips as a faulted OCE
  because `OnityResult` has no canceled status. Pending state allocates; no
  thread hop is added.
- Async LINQ operators on `IOnityAsyncEnumerable<T>` (class
  `OnityAsyncEnumerableLinq`, UniTask parity): `Skip`, `SkipLast`, `TakeLast`,
  `SkipWhile`, `TakeWhile`, indexed `Select` and `Where`, `Distinct`,
  `DistinctUntilChanged`, `CountAsync`, `LongCountAsync`, `AnyAsync`,
  `AllAsync`, `ContainsAsync`, `SequenceEqualAsync`, `AggregateAsync`,
  `FirstAsync`, `LastAsync`, `SingleAsync`, `ElementAtAsync` (each with an
  `OrDefault` form), `SumAsync`, `AverageAsync`, `MinAsync` and `MaxAsync` for
  int, long, float, double, decimal and their nullable forms (generic
  comparer-based `MinAsync` / `MaxAsync` too), `ToListAsync`, `ToHashSetAsync`,
  `ToDictionaryAsync`, `ToLookupAsync`, `ForEachAsync` and `ForEachAwaitAsync`,
  each with `Await` and `AwaitWithCancellation` forms. `ToLookupAsync` returns
  Onity's `IOnityLookup` / `IOnityGrouping`, not `System.Linq.ILookup`. Numeric
  aggregates follow System.Linq where UniTask's template differs (a nullable
  `Sum` starts at zero, `Average` of an empty non-nullable stream faults).
  Index-only `SelectAwait` and `WhereAwait` are not provided; the indexed forms
  take a token.
- `OfType`, `Cast`, `Do`, `DefaultIfEmpty`, `Pairwise`, `Buffer` and `Reverse`;
  `OnityAsyncEnumerable.Create` with an `IOnityAsyncWriter<T>` (lazy producer,
  back-pressure, producer token canceled on disposal), `Never`, `Throw` and
  `Repeat`; `ToOnityAsyncEnumerable` for `IEnumerable<T>`, `Task<T>`,
  `OnityTask<T>` and `OnityTask`; `Subscribe` (16 overloads) and
  `SubscribeAwait` (12) consumers, whose handler faults are reported without
  stopping the loop.
- `TakeUntil` and `SkipUntil` (task or token-factory signal),
  `TakeUntilCanceled`, `SkipUntilCanceled`, `Append`, `Prepend`, `Concat`,
  `Merge` (instance and static), `Zip`, `ZipAwait`, `ZipAwaitWithCancellation`,
  `CombineLatest` (2-15 sources), `SelectMany` (12 shapes), `GroupBy`
  (24 shapes), `Join` and `GroupJoin` (with await forms; null keys never match),
  stable `OrderBy`, `OrderByDescending`, `ThenBy` and `ThenByDescending` (with
  await forms) and `IOnityOrderedAsyncEnumerable<T>`, `Union`, `Intersect`,
  `Except`, `Publish` with `IOnityConnectableAsyncEnumerable<T>`, `Queue`, and a
  generic `BindTo` with UniTask's rebind-once-on-error semantics.
- Timing streams: `EveryUpdate(timing, cancelImmediately)`, `Timer` (one-shot and
  periodic), `Interval`, `TimerFrame`, `IntervalFrame` and `EveryValueChanged`.
- Channels: `Reader.Completion`, `Reader.WaitToReadAsync`, `Writer.Complete`
  (throws `OnityChannelClosedException` when already completed), implicit
  conversions from `OnityChannel<T>` to its reader and writer, and message
  constructors on `OnityChannelClosedException`.
- `OnityAsyncReactiveProperty<T>`, `OnityReadOnlyAsyncReactiveProperty<T>`, their
  interfaces and `ToReadOnlyAsyncReactiveProperty` (UniTask
  `AsyncReactiveProperty` parity). Setting the value from a continuation of its
  own publication throws `InvalidOperationException`; like UniTask it never
  skips equal values, unlike `ReactiveProperty<T>`.
- A scope lifetime token: `OnityContainer` implements `IOnityScopeLifetime`
  (`Token`, `IsDisposed`) with a lazily created `LifetimeToken` linked to the
  parent's; `disposable.AddTo(scope)` disposes with the scope; every Unity
  context binds `IOnityScopeLifetime` and exposes `OnityContext.LifetimeToken`;
  `component.GetScopeCancellationToken()` returns the nearest context's token.
- `IOnityAsyncInitializable`, collected like `IOnityInitializable` and awaited
  one at a time by `BuildAsync` after the async build callbacks, and
  `OnityContext.WaitReadyAsync(ct)`, a native `OnityTask` that resumes on the
  main thread.
- `ReactiveProperty` async bridges: `WaitAsync` and `WaitUntilAsync` on
  `IReadOnlyReactiveProperty<T>` (pooled waits sharing one subscription per
  property), the conflating `AsLatestAsyncEnumerable`, `ToAsyncReactiveProperty`,
  and `BindTo` from an async property into a `ReactiveProperty<T>`.
- Message-bus async streams: `ReceiveAsync(ct)` and `ReceiveAsync(predicate, ct)`
  on `ISubscriber<T>`, `ReceiveAllAsync(capacity, OnityBufferOverflow)` with the
  `Fault`, `DropOldest` and `DropNewest` policies, and
  `SubscribeQueued(handler, capacity, lifetimeToken)`, a bounded queue between
  `IAsyncSubscriber<T>` publishers and one ordered handler, so `PublishAsync`
  waits only while the queue is full.
- `container.DeclareAsyncMessage<T>()` and
  `container.BindAsyncReactiveProperty<T>(initialValue)`.
- The package ships its documentation and AI assistant guidance:
  `Documentation~/` (generated from `docs/` and the `onity-use` skill by
  `tools/docs/sync-package-docs.py`, whose `--check` mode runs in CI),
  `AGENTS.md`, `CLAUDE.md` and `llms.txt` at the package root, and the
  `Onity/AI/Install Assistant Guidance...` and `Onity/AI/Check Assistant
  Guidance` Editor commands, which write nothing until the user confirms.
- Benchmarks: a `throughput` Release Player suite (N concurrent `NextFrame` and
  `Yield` loops under the real PlayerLoop, control-subtracted ns per await),
  `-onityTaskBenchmarkRetention default|matched` for `primary` and
  `builderlifecycle`, `-onityTaskBenchmarkSelfTest`, a build-only Player entry
  point, and the host tools in `tools/benchmark-host` (`stage-host.ps1`,
  `verify-host-settings.ps1`, `quiet-check.ps1`, `run-paired-reports.ps1`,
  `validate-reports.py`, `host-compile-check.py`, `run-attempt.ps1` and
  `surpass-gate.py`).
- A Release Player `builderlifecycle` benchmark includes actual typed/untyped
  1/4-suspension consumers at 1/128/4096 concurrency and both libraries' exact
  deferred-return queues before reuse. Prior readinesscycle IL2CPP totals
  exclude UniTask return CPU; that boundary limitation is now explicit in its
  report and harness documentation.
- A benchmark-only Release Player `readiness` suite compares C#, synchronous
  Jobs and Burst frame/delay scans, including representation copies and
  synthetic managed dispatch. It validates execution proof and generation
  rejection; it does not measure complete async performance.
- A controlled `readinesscycle` benchmark joins the same engine to actual
  OnityTask/UniTask consumers and counts forwarded timer steps, consumption and
  deferred-return drain work with the default Onity flow and retention policy.
- A Release Player `buildercycle` benchmark for one manual-awaitable suspension,
  comparing typed/untyped OnityTask and UniTask with alternating order, checked
  results and real frame drains. Reports separate schedule, completion and
  consumption windows; their sum excludes deferred pool-return CPU.
- `framelifecycle` Player suite: Onity vs UniTask 2.5.11 async lifecycles under
  the real PlayerLoop. Covers NextFrame, Yield, DelayFrames, Delay and WaitUntil
  with token none, registered and 50%-canceled. Every library PlayerLoop system
  is bracketed; control frames are subtracted; a whole-loop sanity bracket
  checks for work outside the brackets. Allocation is control-subtracted
  bytes/op, or -1 when no calibrated counter exists. Validator:
  `tools/benchmark-host/validate-framelifecycle.py`.
- A documented Mono/IL2CPP runner-dispatch investigation with retained raw
  evidence. Neither experimental runtime change was retained.

### Changed

- Pooled task sources are class-rooted (`OnityTaskCore`, `OnityTaskSourceCore`)
  with one int state word holding the status, claim, consumption mode,
  registration pin, bridge bits and version: owned completion is one
  compare-and-swap, consumption retires the version in one compare-and-swap,
  and completers read the registered continuation before publishing.
- Every pool is an array-slot stack guarded by one compare-and-swap gate; a
  contended rent allocates and a contended return lets the object be
  collected. Every pooled source type retains up to `SourcePoolCapacity`
  (default 256), so the PlayerLoop, end-of-frame and `JobHandle` sources move
  from 128 to 256; async-method runners keep `RunnerPoolCapacity` (default 128).
- Array `WhenAny` adds a separate 32-slot bucket retaining up to 128
  coordinators per output shape. The 16-slot bucket retains up to
  `SourcePoolCapacity`; arrays above 32 remain unpooled. Shareable-only arrays
  avoid creating the native-identity HashSet. Loser observation and callback
  retirement are unchanged.
- A suspended `async OnityTask` / `async OnityTask<T>` method's runner returns to
  its pool as soon as its result is consumed, on every backend; the main-thread
  deferred-return queue is no longer used for runners. The return-on-unwind
  gate stays available only behind `ONITY_RUNNER_RETURN_GATE`.
- In Play, frame waits without a cancelable token (`NextFrame()`,
  `DelayFrames(n)`, `NextFixedFrame()`, `NextLateFrame()` and the token-less
  explicit-timing waits) are stateless: they rent nothing, any number of
  consumers may await them, `Preserve()` returns them unchanged, reading the
  result again does not throw, and `Forget()` does nothing. Cancelable waits
  are pooled single-consumer sources on the PlayerLoop scheduler; Edit Mode and
  worker callers keep the legacy runner.
- `Forget()` always observes single-consumer native tasks directly; while task
  tracking is on it also records a native tracker row (negative synthetic id)
  instead of bridging to a .NET task.
- Unobserved faults of `Forget()` without a handler, `async OnityTaskVoid`
  methods and unawaited completion sources go to `OnityTaskScheduler`, which
  drops `OperationCanceledException` unless `PropagateOperationCanceledException`
  is set; `Forget()` without a handler therefore no longer logs cancellation.
- `WaitForFixedUpdate()` resumes at `LastFixedUpdate`, after the physics step,
  like UniTask and Unity's coroutine instruction; `NextFixedFrame()` keeps
  resuming right after the fixed scripts. `DelayFrame(0)` waits for the next
  drain, as in UniTask; `DelayFrames(0)` still completes immediately.
- Posted continuations (`Post`, `AddContinuation`) are dropped, not run, when
  the session ends; awaited yields still observe the retirement.
- `OnityContainer.Dispose()` cancels the scope's `LifetimeToken` before it
  disposes child scopes, scoped instances and singletons. Async build callbacks
  receive that token (linked with the caller's token), and a pass that outlives
  `Dispose` ends canceled.
- `BuildAsync` no longer uses `ConfigureAwait(false)`: every callback and async
  initializer starts on the context the build started on (the main thread in
  Play Mode). A retry resumes at the first initializer that has not completed.
  `OnityContext.IsReady` and `ReadyTask` cover the async initializers, and
  destroying a context mid-build cancels the build without logging an error.
- `BindReactiveProperty`, `BindSubject` and `DeclareMessage` register the
  primitive they create with the scope, so the container disposes it after
  canceling the lifetime token. `BindInstance` values stay caller-owned.
- `OnityTask`, `OnityTask<T>` and `OnityTaskExtensions` are `partial`, and
  `OnityTask` has no static constructor: its settings live in an eagerly
  constructed holder.
- Assemblies that call `Onity.Unity.Async` extension methods should reference
  `Onity.Reactive` (and `Onity.Messaging` for the messaging bridges): the
  `AsOnityAsyncEnumerable`, `BindTo`, `ToOnityTask` and `WaitAsync` overload
  sets include `Onity.Reactive` types. The `ISubscriber<T>` buffered stream is
  named `ReceiveAllAsync` so that it does not join the `AsOnityAsyncEnumerable`
  set and require `Onity.Messaging` too.
- Benchmark harness measures at the library default
  `FlowExecutionContext=false`; flow-on is the explicitly labelled secondary
  arm. The launcher forwards `-onityTaskBenchmarkRetention default|matched` and
  rejects a missing or invalid value; matched `builderlifecycle` runs label
  1/128 arms `retention=default` or `retention=default-after-matched`.
  `framelifecycle` measures the per-frame harness call count and reports
  pre-arm discarded events; its validator reports allocation availability and
  the UniTask denominator CV.

### Breaking

- `OnityTask.FlowExecutionContext` now defaults to `false`, matching UniTask:
  `AsyncLocal<T>` values no longer flow across awaits of `async OnityTask` and
  `async OnityTask<T>` methods unless you opt in, and no execution context is
  captured per suspension. If your code relies on `AsyncLocal<T>` (logging
  scopes, correlation IDs, ambient services) surviving an await inside an async
  Onity method, set `OnityTask.FlowExecutionContext = true` once at startup,
  before any async Onity method runs. Flow-on behaviour is unchanged. Known
  limitation while flow is on: code before an async method's first await runs
  without a copy-on-write scope, so an `AsyncLocal<T>` write there can leak to
  the caller (UniTask behaves the same; a later release will scope it).
- `OnityTask.WhenAll(a, b, ...)` with 2 to 15 separate typed arguments of the
  same type now binds to the tuple overload and returns `(T, T, ...)` instead of
  `T[]`. Pass an array (`WhenAll(new[] { a, b })`) to keep `T[]`.
- `OnityTask.WhenAny<T>(OnityTask<T>, OnityTask<T>)` returning
  `(int winnerIndex, T result)` was removed. Two typed arguments bind to the
  mixed overload, which returns `(int winArgumentIndex, T result1, T result2)`;
  pass an array for `(winnerIndex, result)`.
- `OnityTask.Yield()` and `Yield(timing)` return `OnityYieldAwaitable` instead of
  `OnityTask`. It converts implicitly to `OnityTask`, but extension methods on
  `OnityTask` need `.ToOnityTask()` first (`Yield().ToOnityTask().Timeout(1f)`).
- `OnityTask.Yield(default)` is ambiguous between `Yield(timing)` and
  `Yield(CancellationToken)`; pass a named argument.
- A `null` literal passed to `Select`, `Where`, `ForEachAsync`, `SelectAwait` or
  `WhereAwait` on an async stream is ambiguous between the new delegate
  overloads; cast it to the delegate type. `AsOnityTask(null, ct)` on an
  `AsyncOperation` (`Action<float>` or `IProgress<float>`) and
  `new OnityChannelClosedException(null)` (inner exception or message) are
  ambiguous the same way.
- In Play, the default frame waits (`NextFrame`, `DelayFrames`, `NextFixedFrame`,
  `NextLateFrame`) run on Onity's PlayerLoop nodes after the Update, FixedUpdate
  and LateUpdate scripts instead of on the hidden legacy runner component, and
  destroying that runner no longer cancels them.
- `BindPooledFactory(prefab, ...)` pools are now disposed with their scope, so a
  `Release` into such a pool after its scope ended throws
  `ObjectDisposedException`. Release pooled instances before the scope ends.
- An assembly definition without an `Onity.Reactive` reference that calls the
  0.5.0 `AsOnityAsyncEnumerable()` adapter on an `IAsyncEnumerable<T>` now fails
  with CS0012, because the new `IOnityObservable<T>` overload of that name joins
  overload resolution; add the reference.

### Fixed

- The ONITY003 analyzer reports only `Subscribe` / `SubscribeAwait` expression
  statements whose result is an `IDisposable` that is discarded; the `void`
  overloads that take a `CancellationToken` are no longer flagged, and the
  message names the invoked method.
- The prefab pool that `BindPooledFactory(prefab, ...)` creates is disposed with
  its container or context: it leaves `OnityPoolDiagnosticsRegistry` and its
  inactive clones are destroyed, while checked-out instances stay with their
  owners. A pool passed to `BindPooledFactory(pool)` stays caller-owned; tie it
  to the scope with `pool.AddTo(container)`.
- Native task bridges reserve the current source cycle before publishing their
  reference. Continuation registration pins that cycle until publication
  finishes, preventing a paused caller from modifying a later rental. Repeated
  bridge reads validate the token after reading the reference. Typed and
  untyped paths preserve single consumption, cancellation and context flow.
- A session retirement during a PlayerLoop drain restores the markers'
  pre-drain stamps, so waits that the drain had not resumed report the
  retirement instead of success.

### Performance

- In the 2026-10-02 Release Player gate (Unity 2022.3.62f2, Windows x64,
  non-development Players, UniTask 2.5.11 at `2e993ff18f`, three processes per
  backend and suite), OnityTask was faster than UniTask in all 29 gated IL2CPP
  rows: `primary`, `builderlifecycle` and `throughput`, median Onity/UniTask
  time ratios 0.085-0.808, worst process 0.906. Onity ran at its default
  `FlowExecutionContext = false`, with pool retention matched for the 1,024-
  and 4,096-operation bursts. Mono (reported, not gated) is faster in 25 of 29
  rows; its four synchronous completed-result rows are 1.09x-1.92x slower.
  Report-only: with opt-in flow, the 4-suspension lifecycle is 1.5x-1.6x slower
  on IL2CPP and 2.6x-2.7x on Mono; at default retention, 4,096-call lifecycle
  bursts are up to 2.6x slower. Evidence and raw-data hashes:
  `docs/assets/benchmarks/onitytask-surpass-2026-10-02.md` in the repository.
- `OnityTask` is free of a static constructor, and completed awaits store no
  write barrier; the hot task, runner, source and PlayerLoop types turn off
  IL2CPP null checks (`Il2CppSetOption`), and the settings holder and
  PlayerLoop owner use eager static construction. The generated IL2CPP C++ of
  the measured Player contains no class-initialization check for them.
- The small typed/untyped builder result paths and awaiter completion fast
  paths are inlined.
- The awaited `Yield` appends its continuation inline when the timing's node is
  installed on the main thread, and drains skip the item pass when no item is
  queued.

### Tested

- The Mono and IL2CPP Release Players measured by the gate (build GUIDs
  `ad8bc783e0d34baab4bf1afb000e3141` and `a1e17c822d2b46799b38e6621ec43969`)
  pass all 42 Player smoke cases.
- Release Player gate: PASS, 29 of 29 IL2CPP rows (see Performance).
- Unity 2022.3.62f2 EditMode, batch mode, with default and with Release code
  optimization: 2,290 tests, 2,287 passed, 0 failed, 3 explicit benchmarks
  skipped.
- Unity 2022.3.62f2 PlayMode, batch mode: 253 tests. Default code optimization:
  250 passed, 0 failed, 2 skipped, 1 inconclusive. Release code optimization:
  249 passed, 0 failed, 3 skipped, 1 inconclusive. The skipped cases are an
  explicit benchmark and tests that need a graphics device or `AsyncGPUReadback`
  support; the inconclusive case is the end-of-frame coroutine test, which needs
  a graphics device.
- With `com.unity.ugui` 1.0.0 added to a scratch copy of the project (the
  published manifest is unchanged), the optional `Onity.Unity.UGUI` assembly
  compiles and its 32 PlayMode tests pass.
- New regression coverage: twenty-four source-state cases (typed/untyped
  completion outcomes, native/AsTask/Preserve consumption, cancellation token
  identity, registration disposal before callbacks and stale cancellation
  after same-runner reuse), twelve typed/untyped builder cases for context
  capture, caller isolation and flow-setting changes across two pending
  suspensions, 28 cases for competing consumers and the 16/17/32/33-input
  array boundaries, and EditMode/PlayMode fixtures for the new APIs. The paired
  lifecycle experiment's small disposal guard was reverted because consistent
  complete-cycle benefit was not established.

## [0.5.0] - 2026-10-02

### Added

- Identified and conditional DI bindings: `WithId(...)`, `WhenInjectedInto<T>()`,
  `[Inject(Id = ...)]` on constructor, field, property and method parameters, and
  `IResolver.Resolve<T>(object id)` / `TryResolve<T>(object id, out T)`. A local
  explicit default shadows ancestor conditions; ambiguous conditions throw.
- Native `AsScoped()` lifetime: one instance per resolving container, including
  bindings inherited from a parent, disposed with that scope. Keyed and
  open-generic bindings follow the requesting scope.
- `FromSubContainerResolve` exports a sub-container's local binding per
  requesting scope and forwards ticks to the installed child.
- `Unbind` / `Rebind` after `Build()`: a rebound tickable stops the old instance
  and starts the replacement; keyed child rebinding leaves the parent unchanged.
- Pools: `Prewarm`, fixed capacity (`fixedSize`), `initialSize`, and
  one/two-parameter `Get` overloads on `OnityObjectPool<T>` and
  `PrefabComponentPool<T>`, plus one/two-parameter `PooledFactory` adapters that
  apply runtime parameters before the get hook. Prefab clones are configured
  under an inactive parent before their first `OnEnable`.
- DI benchmark: keyed and scoped singleton scenarios, a focused hot-path mode
  (`-onityBenchmarkHotPathsOnly`), and an IL2CPP Development allocation capture
  (`-onityCaptureDiAllocationBytes`) that validates empty, 1 MiB and 2 MiB
  controls. The player build runner forwards `-onityBenchmarkIterations`,
  `-onityBenchmarkSamples`, `-onityBenchmarkWarmup` and `-onityBenchmarkScenario`,
  and restores the original build target afterwards.

### Changed

- **Breaking:** a pooled `OnityTask` retires its token as soon as it is consumed.
  Reading `IsCompleted` or `IsCompletedSuccessfully` after `await`,
  `GetResult()` or a `WhenAny` consumption now throws
  `InvalidOperationException`, like a second await. Read the result once and
  keep it instead of querying the task afterwards. Five EditMode tests were
  updated to this contract.
- `OnityObjectPool<T>` keeps inactive items in its own array stack instead of
  wrapping Unity's `ObjectPool<T>`. With `collectionCheck: true` it rejects
  duplicate returns by reference in Editor **and** players (Unity 2022.3 checks
  only in the Editor); the top of the stack is the duplicate-check fast slot.
  A failing release hook leaves the pool unchanged, get-hook failures return the
  item, and operations after `Dispose` throw. `Clear` subtracts only the
  destroyed inactive items from `CountAll`.
- Baked resolve is the default; generic `Resolve<T>()` keeps the container-local
  dense type-id provider slots, which now stay in step with `Unbind`, `Rebind`
  and generated open-generic providers.
- The pooled native task sources (suspended `async OnityTask` methods, frame,
  delay, predicate, player-loop, end-of-frame, job-handle, timeout,
  cancellation and `WhenAny` sources) keep their token version, completion
  claim, consumption mode and lifecycle bits in one packed state word and
  publish the terminal status with one volatile write after a compare-and-swap
  claim. Registration, completion, the `AsTask()` bridge and result
  consumption no longer enter a monitor; a stale token is rejected by the same
  compare-and-swap that would have changed a later cycle, and a misuse such as
  two consumers or `AsTask()` racing `GetResult()` still throws
  `InvalidOperationException`. Eight new EditMode cases cover stale tokens
  after reuse, registration racing completion, `AsTask()` racing completion
  and runner cancellation with a retired version. On the desktop Mono harness
  (not a Unity measurement) a suspended-method cycle at 128 concurrent
  operations fell from 197 to 127 ns with `FlowExecutionContext` off and from
  260 to 193 ns with it on, against UniTask's 88 ns. The Unity Editor suites
  below were rerun on this change; the Windows Player suites were not.

### Performance

- Re-measured on Unity 2022.3.62f2 (one Windows machine, indicative): Windows
  IL2CPP Release DI resolve, three fresh processes — Onity baked was fastest in
  all seven scenarios in every process (Onity/VContainer 0.23x–0.57x). Editor
  `GC.Alloc` event counts matched the September report exactly.
- Checked single-item Editor/Mono pool rent/return against Zenject
  `MemoryPool`: 1.48x before the own-stack change, 0.58x–0.59x after. 32-item,
  two-parameter IL2CPP Release factory burst against Zenject: 0.796x/0.856x
  before, 0.738x/0.729x after (old/new/new/old). Raw reports:
  `docs/benchmarks/remeasure-2026-10-01.md` and `docs/benchmarks/pool-own-stack-2026-10-01.md` in the repository.

### Tested

- Unity 2022.3.62f2 EditMode: 997 passed, 0 failed, 3 explicit benchmarks skipped.
- Unity 2022.3.62f2 PlayMode: 95 passed, 0 failed, 1 explicit benchmark skipped.
- `dotnet build onity-core-ci.csproj -c Release -nologo`: 0 warnings, 0 errors.
- DI benchmark assemblies compiled with `ONITY_BENCHMARKS` in the development
  project, where VContainer and Zenject are available.

## [0.4.0] - 2026-09-27

This release includes Plan 13 through the verified Stage 4c awaitable operators.
Reactive stream adapters, WhenEach and additional Unity adapters remain planned.
It does not claim full UniTask parity or overall speed superiority. Historical
measurement records retain their tested 0.3.14 metadata and source hashes.

### Fixed

- Legacy runner destruction now retires accepted waits, preserving tokens,
  caller-owned operations and replacement runners through callback reentrancy
  and pooled source reuse. Both optimizations pass 782 EditMode/73 PlayMode
  cases; both Release Players pass 24 smoke cases. Focused scheduling and
  GetResult slices show no measured allocation increase, while scheduling
  means increased 9–18% in one before/after run per backend. Full-cycle cost
  remains unmeasured; this is a lifecycle correction, not a speedup.

### Changed

- Pending typed `WhenAll<T>` can reuse a bounded coordinator for 1–16 unique
  built-in completion sources without existing bridges, preserving shareable
  output, tracking, ordered faults and cancellation precedence. Other inputs
  retain the previous fallback. Focused normal/Release tests and both Player
  smoke suites pass. Repeated construction measurements show 60–93% lower
  heap-delta estimates, Mono improvements at 8/16 inputs, and slower IL2CPP
  construction; full-lifecycle performance remains unmeasured.

### Added

- Added sequential async-stream SelectAwait, WhereAwait and ForEachAsync with
  owned delegate cancellation, exact task observation and shared cleanup.
  Both optimizations pass 947 EditMode/94 PlayMode cases; both Release Players
  pass 35 smoke cases. Initial fixture failures and their corrections are
  retained in the report. Pending state allocates; no speed claim is made.
- Added bounded/unbounded channels with FIFO backpressure, cancellation,
  single-consumer leases and ReadAll cleanup. Full suites pass 913 EditMode/
  92 PlayMode cases in both optimizations; the strengthened channel fixture
  passes 34 cases in each, and both Release Players pass 33 smoke cases.
  Warmed buffered operations show no retained-heap increase in calibrated
  windows; exact zero allocation and comparative speed remain unproven.
- Added pull-based `EveryUpdate` and native/BCL async-enumerable adapters with
  exact ValueTask consumption, cancellation ownership and shared cleanup.
  Both optimizations pass 879 EditMode/90 PlayMode cases; Mono and IL2CPP
  Release Players each pass 31 smoke cases. Pending adapter state allocates;
  no comparative speed or allocation quantity is claimed.
- Added native finite async streams with await-foreach cleanup, cancellation,
  Select/Where/Take and First/ToArray consumers. Both optimizations pass 851
  EditMode/86 PlayMode cases; each Release Player passes 29 smoke cases.
  Primed finite iteration shows no retained-heap increase in calibrated
  HeapDelta windows; exact allocation and comparative speed are not established.
- Added real `WaitForEndOfFrame` through one shared Unity coroutine, with
  Update-driven cancellation and safe host/session retirement. Both Editor
  optimizations pass 814 EditMode/84 PlayMode tests. Mono and IL2CPP Release
  Players each pass six graphics groups with rendered pixel proof and 27
  paired headless checks. Allocation and speed remain unmeasured.
- Added typed/untyped `Timeout` and `TimeoutWithoutException`, preserving
  late producer observation, cancellation tokens and producer faults. Private
  scaled/unscaled timers share the explicit PlayerLoop session. Both
  optimizations pass 812 EditMode/82 PlayMode tests and each Release Player
  passes 26 smoke cases. Wrappers and timer entries are unpooled; allocation
  quantities and speed remain unmeasured.
- Added explicit Update/FixedUpdate/LateUpdate task timing with a separate
  session owner, current-loop repair and reentrant cancellation safeguards.
  Both optimizations pass 780 EditMode/64 PlayMode cases; Release Mono/IL2CPP
  pass 22 smoke cases each. Repeated warmed tokenless Update Yield brackets
  show no measured heap growth under calibrated HeapDelta; exact zero GC,
  other timing variants and general speed superiority are not established.
- Added typed/untyped `AttachExternalCancellation` and
  `SuppressCancellationThrow`, preserving producer observation, winning tokens
  and faulted cancellation exceptions. Pending native wrappers are unpooled.
  Both optimization modes pass 777 EditMode and 54 PlayMode cases; Release
  Mono/IL2CPP pass 20 smoke cases each. Allocation and speed are unmeasured.
- Added typed and untyped array `WhenAny`, with native outputs, input snapshots,
  duplicate single-consumer rejection and observation of every loser. Corrected
  fault observation for public completion-source subclasses and later bridges.
  Both optimization modes pass 744 EditMode and 52 PlayMode cases; Release
  Mono/IL2CPP pass 18 smoke cases each. Repeated composition benchmarks favor
  UniTask in time; Onity has no measurable heap growth at 2/16 inputs under the
  coarse counter, but the unpooled 32-input path allocates substantially more.
- Added native `JobHandle.AsOnityTask()` observation with main-thread registration,
  completion before data access, and cleanup of accepted jobs during runner or
  session teardown. Caller-owned native containers remain with the caller.
  Verified with 690 EditMode and 50 PlayMode tests in both normal and Release
  optimizations, including actual reload-disabled Play/Edit/Play transitions.
- Added a separate Jobs benchmark with serial C#, plain Jobs and Burst compute
  rows, checked output/Burst proof, and independently reported Onity/UniTask
  adapter registration and completion measurements.
- Added `OnityTask.SwitchToThreadPool` and synchronous `RunOnThreadPool`
  action/result overloads, with explicit cancellation, execution-context and
  originating-session return semantics. WebGL Players fail promptly as
  unsupported. Verified with 682 EditMode and 46 PlayMode tests in both
  optimizations, plus 16-case Mono/IL2CPP smoke suites. Separate repeated
  thread-pool measurements do not establish an overall performance winner.

## [0.3.14] - 2026-09-27

### Added

- Added `OnityTask.SwitchToMainThread(cancellationToken)`, returning the
  `OnityTaskThreadSwitch` awaitable. Awaiting it on Unity's main thread
  completes synchronously without a frame delay (the 0 B/op design target for
  that path is not yet measured; see the comparison guide); awaiting it on
  another thread queues the continuation and resumes it during the Update phase
  through the task runner. Cancellation is observed at `GetResult` on the
  destination thread, including tokens that were already canceled. Outside
  Play Mode the Editor drains the queue from `EditorApplication.update` while
  it is not compiling or importing assets. Entering or exiting Play Mode and
  quitting the player start a new session, and continuations queued in an
  earlier session are discarded. A worker-thread switch requested before the
  runner exists, or after the runner was destroyed, recreates the runner
  through Unity's synchronization context. Thread-pool switching and timing
  selection are not included.
- Added focused EditMode, PlayMode, and Editor lifecycle tests for the thread
  switch, plus the `Onity/Benchmarks/Run OnityTask Thread Switch Benchmarks
  (Play Mode)` comparison harness. The tests were compile-checked outside
  Unity; the 2026-09-25 benchmark report cites 614 EditMode, 37 PlayMode, and
  26 analyzer passes whose counts match the suites at `3260c40`, and the
  harness ran in two Editor/Mono processes. Subsequent full-suite verification
  at `528d52c` plus the benchmark startup fix passed 668/668 EditMode and 41/41
  PlayMode tests in both default and Release optimization on Unity 2022.3.62f2.
- Added `OnityTaskAsyncBuilderEditModeTests` and
  `OnityTaskAsyncBuilderPlayModeTests` for the pooled builder: representation,
  single-consumer rules, fault and cancellation instance mapping on the native,
  bridged, and preserved paths, pool reuse on both return sites including the
  deferred IL2CPP return, execution-context flow under both switch settings
  and a flip while suspended, `Forget`, `WhenAll`, and `WhenAny` with runner
  inputs. These passed in the full Unity suites above. Final Mono and IL2CPP
  Players also passed 13 separate semantic smoke cases, including context flow
  and deferred pool return, with the internal capture/run pair active.
- Added benchmark startup traces, separate startup/measurement deadlines,
  stale-report rejection and nine watchdog tests. Primary schema 6 reports
  effective runtime/build settings and rejected allocation-counter candidates.
  A separately labelled two-frame drain control diagnoses pool replenishment
  without changing the default benchmark workload.
- Added bounded Development IL2CPP lifecycle profiles and headless raw-data
  export. Eight configurations/16 library windows passed with optimized,
  verified value-type state machines, complete resumption/return counts and
  allocation-site metadata. Deep-profile timings remain diagnostic only;
  Release scheduling still misses the performance target. Native source
  synchronization is the next investigation, with flow and pool defaults kept.

### Fixed

- Kept the task benchmark initialization assembly reachable with
  `AlwaysLinkAssembly`. The empty-scene IL2CPP build previously stripped it,
  producing a Player that never entered the benchmark. Rebuilt Mono and IL2CPP
  Players now pass smoke and two independent primary runs each. Runtime library
  sources are unchanged by this fix.

### Changed

- Replaced the wrapped `AsyncTaskMethodBuilder` in `OnityTaskMethodBuilder` and
  `OnityTaskMethodBuilder<T>` with pooled native runners. A suspended
  `async OnityTask` or `async OnityTask<T>` method now returns a
  single-consumer native task backed by a runner that holds the state machine
  by value and resumes through one cached delegate; await it once, or call
  `Preserve()` to share it, and expect `InvalidOperationException` from status
  reads after it was consumed. Synchronous success still stores the result
  inline; a synchronous fault or cancellation is Task-backed with the thrown
  instance preserved. A fault or `OperationCanceledException` thrown after a
  suspension is rethrown as the same instance. `AsyncLocal<T>` values flow
  across awaits by default through the new `OnityTask.FlowExecutionContext`
  switch. The builders bind the class library's internal
  `ExecutionContext.FastCapture` and `RunInternal(..., preserveSyncCtx: true)`
  pair through reflection, proven by a probe and kept reachable for managed
  code stripping by an in-assembly reference to the class library's builder
  (Unity ignores a `link.xml` inside a package), so a suspension allocates no
  context until a thread has
  stored an `AsyncLocal<T>` value and resumption keeps the thread's
  synchronization context; without the pair they use the public capture and
  run, which allocate the captured context on every suspension (about 72
  bytes, plus about 56 bytes on Mono) and re-install the synchronization
  context. Disabling the switch gives UniTask's no-flow semantics. Writes made
  before the first await are no longer isolated from the caller and persist
  in the thread's ambient context. The same-instance guarantee holds for the
  native await; `AsTask()` consumers receive a `TaskCanceledException` and
  `Preserve()` re-raises a new `OperationCanceledException` with the same
  token. Verified at `4be50dc` on Unity 2022.3.62f2 with the public path:
  full EditMode and PlayMode suites passed in both code optimizations, and
  two Release runs measured async-method scheduling 2.25x to 2.55x slower
  than UniTask with flow on and 1.44x to 1.78x with flow off. Verified again
  at `f682b7c` with the fast path bound: 658/658 EditMode and 41/41 PlayMode
  in both code optimizations, flow-on scheduling 1.62x to 2.10x and flow-off
  1.40x to 1.83x UniTask in two Release runs, Onity slower in every scenario,
  and the new allocation counter calibrated with about 0.04 to 0.06 B/op at
  128 concurrent operations and about 399 to 407 B/op in the 4,096 burst,
  which exceeds the 128-runner and 256-source pool caps. On desktop Mono 6.8,
  which compiles the same reference-source `ExecutionContext`, a
  capture-and-run pair read 0 B/op without stored `AsyncLocal` values against
  72 B/op on the public path.
- Runner pools use one compare-and-swap gate and an intrusive list instead of
  a locked `Stack<T>`, runners are reused without the source lock after their
  token was retired, and `InvalidateVersion` no longer takes the lock, which
  removes four of the seven monitor acquisitions a suspended method paid per
  cycle. `OnityTask.RunnerPoolCapacity` (default 128) caps the runners kept
  per method. On desktop Mono 6.8 with a manual awaitable, one suspended
  method fell from about 320 to about 225 ns per cycle with flow on and from
  about 265 to about 165 ns with flow off, against about 90 ns for UniTask;
  not a Unity measurement.
- Native single-consumer sources rethrow faults and cancellations through
  `ExceptionDispatchInfo` and keep the thrown `OperationCanceledException`
  instance, and runner-backed sources retire their token when they return to
  the pool. Their `AsTask()` bridges are created without capturing the
  caller's execution context. `Forget()` on such a task observes it directly
  while task tracking is disabled, and the two-input untyped `WhenAll` accepts
  pending single-consumer native inputs, including suspended async methods,
  on the pooled coordinator path. Inputs that already have an awaiter, a
  bridge, or a consumer stay on the .NET path, whose `AsTask()` reports the
  conflict; a registration failure or an input consumed elsewhere before the
  coordinator reads it now faults the combined task instead of leaving it
  pending. On IL2CPP a runner's deferred pool return waits while a worker is
  still unwinding the completing `MoveNext`.
- Both task benchmark runners select a calibrated allocation counter at run
  start: the per-thread counter, the engine's `GC Allocated In Frame` counter
  through `ProfilerRecorder`, or a `GC.GetTotalMemory` delta that disables the
  collector only in players and discards slices interrupted by a collection
  in the Editor, which rejects `GarbageCollector.GCMode` changes. Reports
  record the counter, its rejected alternatives, and per-metric valid sample
  counts (introduced in primary schema 5, thread-switch schema 2). The primary suite
  measures the eight async-method `NextFrame` cases with
  `OnityTask.FlowExecutionContext` on and again with it off, suffixed
  ` (flow off)`, for 24 scenarios, and records the setting's default and the
  task tracker state. The benchmark README no longer describes a separate
  `-onityTaskAllocationsOnly` Profiler pass, which never shipped in the
  package. New menu items and a command-line entry point build a
  non-development Mono or IL2CPP Standalone player, run either suite headless
  in it, and restore the build settings; Editor timings inflate call-heavy
  paths and a player run is the source for any performance claim.

### Improved

- Rewrote the main-thread switch drain to array-backed double buffers with one
  session read per batch and an inline exception guard. Two Editor/Mono runs of
  the earlier `List<T>` drain measured main-thread dispatch about 25 to 36
  percent (24.7 to 36.1) slower than pinned UniTask; two runs of the rewrite on
  the same Unity 2022.3.62f2 host measured it 9.0 to 36.1 percent faster in
  both cohorts, with worker enqueue of 4,096 continuations still 7.2 to 17.1
  percent faster and the synchronous switch within noise. Allocation counters
  were unavailable in all four runs, so no allocation claim follows. A runner
  destroyed while continuations are queued now requests its replacement at
  once, and a fresh player domain keeps its first session so awaits requested
  before Onity's load hook still resume. The focused thread-switch EditMode
  and PlayMode filters passed on that host.

## [0.3.13] - 2026-09-24

### Added

- Added typed homogeneous two-input `OnityTask.WhenAny<T>(first, second)`,
  returning the winner index and value. It consumes both inputs, observes the
  loser, and rejects duplicate single-consumer native inputs.
- Added typed and untyped `OnityTask.Preserve()` for sharing one pooled native
  operation with multiple pending or late consumers. The original task is
  claimed once; completed, Task-backed, and completion-source tasks are returned
  without a new retained source.
- Added a two-input untyped `OnityTask.WhenAll(first, second)` overload. Two
  already successful inputs are consumed immediately. Eligible pending
  completion-source inputs use a pooled coordinator; other states retain the
  existing `Task.WhenAll` behavior.

### Fixed

- Kept the task tracker dictionary and display order consistent when a fault's
  custom `Exception.Message` clears or clears and retracks during completion.
  Error text is read outside the tracker lock; an already completed replacement
  entry retains its own completion time.
- Marked an input `AsTask()` fault bridge observed when the pending two-input
  coordinator consumes its fault, including bridges created during or after
  completion. Pending calls with an existing input bridge use the prior
  `Task.WhenAll` path.

### Improved

- Deferred typed `WhenAny` callbacks until inputs are pending, reducing
  completed-input allocation from 424 to 152 B/op in Unity 2022 Editor/Mono.
- Read completed multi-consumer results without taking the completion-source
  lock, while preserving the bridge/status publication order for concurrent
  completion.
- Removed the separate lock-object allocation from internal `Preserve()`
  sources. The measured pending native conversion fell from 264 to 248 B/task
  in the Unity 2022 Editor/Mono benchmark.
- Stored the `Preserve()` adapter directly in the native source's task-bridge
  slot, removing its bound callback allocation. Pending conversion fell from
  248 to 120 B/task; ordinary native awaiting stayed at 656 B/task in the
  calibrated lifecycle benchmark. The original task cannot consume the result
  while the adapter owns it.
- Returned the pending completion-source status without taking its gate when
  no .NET task bridge exists. A published bridge still uses the gate to keep
  terminal status ordered after bridge completion; all measured status reads
  remained at 0 B/op.
- Removed Task bridges from the already successful two-input `WhenAll` path.
  Calibrated Unity 2022 Editor/Mono scheduling allocation fell from 276 to
  0 B/op; the pinned UniTask comparison used 128 B/op. Pending calls saved
  64 B/op by avoiding the `params` input array. Already-completed calls do not
  add a tracker entry.
- Collected eligible already successful typed `WhenAll<T>` results directly in
  input order, without a .NET Task bridge or tracker entry. Duplicate
  single-consumer native inputs retain the previous conversion behavior, and
  native duplicate scans are bounded to 16 inputs before falling back. In the
  calibrated Unity 2022 Editor/Mono scheduling slice, completed two-input
  allocation fell from 392 to 40 B/op and four-input allocation from 592 to
  48 B/op; the pinned UniTask controls were 112 and 120 B/op respectively.
  Pending two-input Onity allocation remained at 1,408 B/op.
- Reused a static task tracker completion callback instead of allocating a
  capture and delegate per pending task. In the two-input `WhenAll` scheduling
  comparison with tracking enabled, allocation fell from 1,556 to 1,408 B/op;
  tracking-disabled allocation stayed at 544 B/op. ExecutionContext flow and
  the tracker result behavior were preserved.
- Reused a bounded internal coordinator for eligible pending untyped two-input
  completion-source `WhenAll` calls. The output remains Task-backed and
  shareable; faults retain argument order and dominate cancellation. In two
  calibrated Unity 2022 Editor/Mono passes, main-thread full-lifecycle
  allocation fell from 624.56 to 176 B/op for tracker-off success, 1,288.56
  to 880 B/op for fault, and 1,040.56 to 592 B/op for cancellation. Tracker-on
  allocation also fell in all three cases, but remains above pinned UniTask in
  the measured pass. The first observed pending schedule increased by 434 B;
  completed two-input scheduling stayed at 0 B/op. The
  [full report](https://github.com/furkantokkan/Onity/blob/b6233dd84533c517890ecb3705b7f285269b42ca/docs/assets/benchmarks/onity-pending-whenall-v6-bridge-fallback-2026-09-24.md)
  records first-call, bridge-present, saturation, timing, and worker limits.

### Tested

- At the previous `6f32e15` product revision, Unity `2022.3.62f3` EditMode
  `549/549` and PlayMode `23/23` passed, including
  native sharing, fault/cancellation propagation, source reuse, and main-thread
  continuation tests. The final bridge-publication adjustment passed `36/36`
  focused completion-source EditMode tests; the two-input `WhenAll` subset
  passed `7/7`. Both reentrant tracker regressions failed before their fixes
  and passed after them. Typed `WhenAll` covered completed and pending inputs,
  duplicate native sources, source consumption, faults, cancellation, and large
  result sets. The Release `Onity.Unity` build passed with no errors.
- At the integrated pending-pair revision `556602a`, independent Unity
  `2022.3.62f3` verification passed EditMode `579/579` and PlayMode `25/25`.
  `Onity.Unity` Release built with zero errors and 16 preexisting Unity
  reference warnings. The new coverage includes pending success, ordered
  faults, cancellation, preexisting and concurrent `AsTask()` bridges, worker
  completion, tracker context, and coordinator reuse.
- At the accepted typed `WhenAny` revision `8fa63ac`, local Unity
  `2022.3.62f3` verification passed EditMode `602/602` and PlayMode `28/28`.
  The Release `Onity.Unity` build had zero errors and 16 preexisting MSB3277
  reference warnings. The engine-free core Release build had zero warnings and
  errors, and analyzer/source-generator tests passed `26/26`. GitHub docs and
  core checks passed on PR #14; Unity CI was skipped because `UNITY_LICENSE`
  is not configured.
- A test-only Unity `2022.3.62f3` StandaloneWindows64 IL2CPP Player build
  succeeded with the accepted runtime (`OnityAsync` blob `1a7fdd8`). The
  `OnityTaskSafetyPlayModeTests` local XML recorded `20/20` passed with no
  failures, skips, or inconclusive results; the Player callback recorded 20
  starts and 20 finishes, and the Editor exited with code 0. This focused
  Player result is not a Player performance benchmark.

## [0.3.12] - 2026-09-23

### Added

- Added typed and untyped `OnityTaskCompletionSource` for completing retained,
  multi-consumer tasks from callbacks. Multiple pending or late awaiters can
  observe the same result, fault, or cancellation without consuming a pool token.
- Allowed the same completion-source task in both inputs of the two-task
  `WhenAny` overload while retaining the single-consumer guard for pooled tasks.

### Fixed

- Kept completion-source status reads coherent with an already completed .NET
  task bridge during concurrent completion.

### Improved

- Avoided Unity synchronization-context capture while creating a completion
  source's .NET task bridge, preserving asynchronous bridge continuations.
  Pending typed and untyped `AsTask()` conversion fell from 848 to 176 B/op
  in the calibrated Unity 2022 Editor/Mono comparison; pinned UniTask used
  120 B/op in the same workloads. The other 30 allocation metrics did not change.

### Tested

- Unity `2022.3.62f3`: EditMode `515/515` and PlayMode `21/21` passed,
  including concurrent completion, synchronization-context, and `AsTask()`
  bridge tests.
- Compared 16 completion-source scenarios with pinned UniTask `2.5.11` using
  eight timing and allocation samples per scenario. Results vary by workload;
  see the [comparison](https://furkantokkan.github.io/Onity/guide/onitytask-comparison.html)
  and raw reports.
- A separate calibrated `IsCompleted` probe allocated zero bytes for both
  libraries; Onity's pending read remained slower in this Editor/Mono run.

## [0.3.11] - 2026-09-23

### Fixed

- Completed the `AsTask()` bridge before a pooled OnityTask source can be
  released during concurrent completion and a second bridge request. This
  preserves typed results, faults, and cancellation tokens.

### Tested

- Unity `2022.3.62f3`: EditMode `480/480` and PlayMode `21/21` passed,
  including concurrent typed and untyped `AsTask()` regressions.

## [0.3.10] - 2026-09-23

### Added

- Added two-input `OnityTask.WhenAny` with a native result source. It consumes
  both single-consumer inputs and preserves the winner's fault or cancellation.
- Expanded the optional UniTask benchmark harness with matched typed and
  untyped async-method workloads at 128 and 4,096 concurrent operations.

### Fixed

- Kept typed `AsTask()` conversion from invoking user-defined result equality.
- Forwarded task-backed `UnsafeOnCompleted` registrations to the underlying
  unsafe awaiter in both typed and untyped tasks.
- Kept deferred scene loads reachable after cancellation or a progress callback
  failure, and activated prepared loads during loading-scene cancellation so
  Unity's async operation queue cannot remain stalled.
- Kept exceptions from native await continuations from stopping the task runner
  or faulting a newly rented pooled wait; unhandled callback errors reach the
  Unity log.
- Made positive scaled and unscaled delays wait until a later rendered frame
  when scheduled before the task runner's `Update`.

### Performance

- Removed Unity object equality from the cached OnityTask runner lookup on every
  scheduled frame wait.
- Stored synchronous successful `async OnityTask<T>` results inline. Suspended
  and exceptional methods continue to use .NET Task internals.

### Measurement

- Measured 16 matched OnityTask and pinned UniTask 2.5.11 workloads in Unity
  `2022.3.62f3` Windows Editor/Mono. Async-method scheduling remains slower for
  OnityTask in the measured cohorts; the faster `GetResult` slices exclude the
  suspended frame and continuation work. Allocation bytes are unavailable
  because the 64 KiB positive control failed calibration. No overall
  OnityTask superiority claim is made.

### Tested

- Unity `2022.3.62f3`: integrated EditMode `476/476` and PlayMode `21/21` passed.
- Engine-free core, analyzer, and source-generator Release builds passed with
  zero warnings; package metadata covered all `234/234` files.
- The 16-scenario Editor/Mono comparison completed in a separate host with
  official UniTask 2.5.11 pinned by Git commit. Raw reports accompany the
  GitHub release; IL2CPP player performance was not measured for this version.

## [0.3.9] - 2026-09-23

### Added

- Added `OnityTask.DelayFrames` and ordered typed `OnityTask.WhenAll<T>` results
  for common Unity async flows.

### Fixed

- Made `OnityTask.NextFrame` wait until a later rendered frame even when called
  from an `Update` callback before the Onity task runner.

### Tested

- Unity `2022.3.62f3`: EditMode `452/452` and PlayMode `15/15` passed.
- Roslyn analyzer and source-generator tests: `26/26` passed; engine-free core,
  analyzer, and source-generator Release builds passed; package metadata covered
  all `228/228` files.
- An independent code review found no actionable correctness or regression issues.
- No OnityTask versus UniTask allocation or throughput benchmark was run for
  this release; typed `WhenAll<T>` uses .NET Task interop and allocates.

## [0.3.8] - 2026-09-23

### Fixed

- Rebuilt the shipped Roslyn analyzer and source generator with compiler APIs
  compatible with Unity 2022.3, removing newer-Roslyn load warnings.
- Made generated activators compile on Unity 2022.3 by emitting an internal
  `ModuleInitializerAttribute` only when the target framework lacks it.
- Kept nested message and reactive dispatch stable when subscriptions change
  during callbacks, and restored throttle state after a callback throws.
- Allowed an async container build to retry after cancellation or failure,
  kept lifecycle registrations distinct by object identity, and completed
  disposal of remaining services when one disposal throws.
- Kept child context injection within its nearest owning context.

### Tested

- Unity `2022.3.62f3`: EditMode `445/445` and PlayMode `13/13` passed in the
  release candidate worktree.
- Roslyn analyzer/source-generator tests: `26/26` passed.
- Engine-free core, analyzer, and source-generator Release builds passed;
  package metadata coverage remained `228/228` files.

### Documentation

- Updated the release install pins and analyzer/source-generator build guidance.

## [0.3.7] - 2026-07-30

### Added

- Added a distinct `Hub` scene-flow state while preserving all existing enum
  values, plus a four-stage `Loading -> Menu -> Hub -> Game` ready profile.
- Added Bootstrap readiness and default UI Toolkit Loading scene initiators,
  including progress text and a non-interactive normalized slider.
- Added a Blank Template action that creates only an empty scene-flow profile.

### Changed

- Updated the Unity 2022.3-compatible package baselines to Input System `1.19.0`,
  Entities `1.4.8`, Burst `1.8.29`, Collections `2.6.8`, and Mathematics `1.3.3`.
- Updated first-party GitHub Actions to Node 24-backed major versions, removing
  the Node.js 20 runner deprecation warning.

### Fixed

- Added stable Unity metadata for the retained benchmark JSON so immutable UPM
  installs import it without repeated missing-meta errors.
- Replaced editor-only `UnityEngine.Object.GetInstanceID()` usage with the
  compatible object hash identity path so Onity compiles on Unity `6000.5` while
  preserving Unity `2022.3` support.
- Bootstrap scene flow now waits for synchronous and asynchronous
  `ProjectContext` build callbacks before transitioning exactly once.
- Scene Flow Manager Apply now adds or repairs Bootstrap and Loading support
  idempotently and keeps Menu, Hub, and Gameplay groups distinct.

### Tested

- Package metadata completeness validation passes for every non-hidden file in
  `Packages/com.onity.framework`.
- Unity `2022.3.62f3`: EditMode `439/439` and PlayMode `12/12` passed.
- Unity `6000.5.2f1`: EditMode `434/434` and PlayMode `10/10` passed.
- Unity `2022.3.62f3` focused scene-flow EditMode tests: `11/11` passed.
- Unity `2022.3.62f3` transition-store EditMode tests: `2/2` passed.
- Unity `2022.3.62f3` context-readiness/loading-gate PlayMode tests: `2/2` passed.

### Documentation

- Added a first-class OnityTask guide, current architecture overview, custom
  404 page, canonical metadata, and sitemap-backed GitHub Pages navigation.
- Corrected stale DI/reactive API claims, subscription lifetime examples,
  messaging-order wording, benchmark delta labels, and package setup examples.
- Added reproducible Jekyll dependencies plus strict build and internal-link
  checks for documentation pull requests and Pages deployments.

## [0.3.6] - 2026-07-12

### Added

- Added `OnityRaycastCommandBatch.Complete()` as an explicit synchronization
  point for scheduled raycast jobs.
- Added `-onityBenchmarkScenario <name>` to the IL2CPP benchmark runner for
  focused high-sample release gates.

### Changed

- Added a dense type-id provider slot to the standard generic DI resolve path,
  removing the steady-state `Dictionary<Type, ...>` lookup while preserving
  provider lifetime, diagnostics, rebind, parent fallback, and self-resolve
  behavior.
- Bumped the package and release pin examples to `0.3.6`.

### Fixed

- Added generation validation and single-consumer guards to pooled `OnityTask`
  sources so stale task copies cannot observe a later pooled operation.
- Routed `OnityTask` cancellation completion through the Unity runner so
  continuations resume on the main thread, including fixed-frame waits while
  `Time.timeScale` is zero.
- Returned sources materialized through `AsTask()` / `Forget()` to their pools
  after the independent `Task` completes.
- Made `OnityRaycastCommandBatch` own and complete pending jobs before reading,
  reusing, resizing, clearing, or disposing native buffers, and schedule only
  the active command range.
- Made the IL2CPP benchmark runner preserve menu scene/build-target state and
  delete only its generated temporary scene.
- Marked uncalibrated Editor allocation metrics unavailable, published them as
  `n/a`, and removed the stale zero-allocation comparison chart.

### Performance

- Windows IL2CPP focused singleton gate (`1000` samples, `10,000` resolves per
  sample): Onity standard/reflection `18.80 ns/op`, Onity baked `17.40 ns/op`,
  VContainer `94.39 ns/op`, and Zenject `435.24 ns/op`.
- The final full Windows IL2CPP suite keeps both Onity resolve lanes ahead of
  VContainer on every measured resolve and prepare/register scenario.

### Tested

- Unity EditMode: `434/434` passed.
- Unity PlayMode: `10/10` passed after adding OnityTask safety coverage.
- Editor/Mono DI, Windows IL2CPP DI, and OnityTask vs UniTask benchmarks rerun
  on Unity `2022.3.62f3`.

## [0.3.5] - 2026-06-21

### Added

- Added `OnityTask` / `OnityTask<T>` as the Onity-owned Unity async awaitable
  surface for frame waits, delays, scene loads, `AsyncOperation`,
  `UnityWebRequest`, reactive awaits, and async messaging bridges.
- Added OnityTask vs UniTask benchmark tooling behind `ONITY_BENCHMARKS`.
- Added a UniTask migration guide covering common async mappings and web/scene
  request examples.

### Changed

- Bumped package and release pin examples to `0.3.5`.

### Tested

- `dotnet build Onity.Unity.csproj -nologo`
- `dotnet build Onity.Tests.EditMode.csproj -nologo`
- `dotnet build onity-core-ci.csproj -c Release -nologo`

## [0.3.4] - 2026-05-31

### Changed

- Renamed the Unity event shortcut facade from `Onity` to `OnityEvent` so event
  publishing, subscribing, observing, and event hub access are explicit at call
  sites.
- Gated benchmark comparison assemblies behind `ONITY_BENCHMARKS` so package
  users do not need VContainer or Zenject installed unless they run benchmarks.
- Updated README and documentation examples to use `OnityEvent.Publish`,
  `OnityEvent.Subscribe`, and `OnityEvent.Observe`.

### Tested

- `dotnet build onity-core-ci.csproj -c Release -nologo`

## [0.3.3] - 2026-05-31

### Added

- Added Scene Flow ready profile presets that create missing loading, menu, and
  gameplay scenes under `Assets/Scenes/OnitySceneFlow`.
- Added one-click 2-scene and 3-scene Scene Flow setup paths for quick project
  bootstrapping.
- Added a Single Scene Setup preset that creates one gameplay scene with a
  `SceneContext`, prepares the runtime-loaded `ProjectContext` prefab, and
  applies Build Settings immediately.
- Added Unity event shortcuts over the auto-bound event hub, plus
  component-scoped overloads for `GameObjectContext` usage.

### Changed

- Removed bundled package samples and the `samples` manifest entry so the UPM
  package stays focused on the runtime, editor tooling, tests, and benchmarks.
- Consolidated editor entries under the `Onity/...` menu and removed duplicate
  `Tools/Onity` / `Window/Onity` aliases.
- Polished diagnostics, pool, observable, and task tracker toolbars so the search
  clear button is only drawn when needed and toolbar labels do not overlap.
- Updated docs to describe the package as sample-free and to use the consolidated
  `Onity/...` menu paths.

### Tested

- `dotnet build onity-core-ci.csproj -c Release -nologo`

## [0.3.2] - 2026-05-30

### Added

- Added an AOT-safe generated constructor activator registry for `Onity.DI`.
- Added `[OnityGenerateActivator]` and shipped `Onity.SourceGen.dll` as a
  Roslyn analyzer asset so marked DI types can register direct `new T(...)`
  activators before construction plans are built.
- Added IL2CPP player benchmark bootstrap support and configurable player
  benchmark iterations, samples, and warmup arguments.
- Added generated-activator EditMode coverage.

### Changed

- `ActivatorCompiler` now prefers registered generated activators before the
  runtime `Expression.Compile` / reflection paths.
- Updated README, comparison docs, IL2CPP guide, and performance plans with the
  latest Windows IL2CPP player benchmark results.
- Bumped package version and release pin examples to `0.3.2`.

### Tested

- `dotnet build tools/Onity.SourceGen/Onity.SourceGen.csproj -c Release`
- `dotnet build onity-core-ci.csproj -c Release`
- Windows IL2CPP player DI benchmark with 19 generated activators registered:
  Onity baked was faster than VContainer and Zenject on all five measured
  timing scenarios.

## [0.3.1] - 2026-05-31

### Changed

- Removed the external LINQ replacement dependency — Onity now has **zero third-party runtime
  dependencies**. `OnityUiPresenterFactory` uses a hand-rolled loop instead of
  `AsValueEnumerable`; install no longer needs NuGetForUnity.

## [0.3.0] - 2026-05-30

### Added

- Added engine-free reactive thread-pool scheduling:
  - `ObserveOnThreadPool()` re-posts values to a .NET thread-pool worker while
    preserving source order.
  - `SelectOnThreadPool(...)` runs CPU-bound selectors on the .NET thread pool
    with configurable max concurrency.
- Added EditMode coverage for thread-pool scheduling, configured parallelism,
  source-order preservation when concurrency is one, and invalid argument
  validation.
- Added ADR 0002 documenting the managed thread-pool boundary and why Unity
  Job/Burst modes remain separate from managed reactive operator execution.

### Changed

- Updated reactive architecture and migration docs to distinguish real managed
  thread-pool operators from the experimental Unity job/Burst frame boundary.
- Bumped package version to `0.3.0`.
- Updated UPM install documentation and release pin examples to `v0.3.0`.

### Tested

- `dotnet build onity-core-ci.csproj -c Release`
- Focused reactive thread-pool smoke coverage for ordered thread hops and
  configured selector parallelism.

## [0.2.1] - 2026-05-30

### Changed

- Optimized the baked DI build path by reusing the existing provider records in
  the baked graph instead of collecting unused dependency and activator data.
- Updated DI benchmark reports and charts. `Onity (Baked)` now beats VContainer
  in every measured Editor/Mono resolve and prepare/register scenario, including
  `Prepare & Register (Complex)`.
- Documented the DOTS/Burst boundary for DI performance: managed DI stays in the
  C# container; DOTS remains a bridge for blittable, batchable workloads.
- Refreshed the public VContainer/Zenject comparison with the latest 0.2.1
  benchmark numbers.
- Added message broker and MessagePipe migration examples to the events
  documentation.

### Tested

- `dotnet build Onity.DI.csproj`
- `dotnet build Onity.Tests.EditMode.csproj`
- Unity EditMode filtered baked parity suite: 15/15 passed.
- Unity batchmode DI benchmark generated
  `Benchmarks/Results/di-benchmark-latest.json`.

## [0.1.0] - 2026-05-30

First public preview. Three feature-complete pillars on a shared engine-free core
with one disposal model and hot-path machinery designed to avoid per-call managed
allocation. The core uses no `System.Linq`.

### Added

- **Dependency Injection (`Onity.DI`)**
  - Zenject-familiar fluent binding: `Bind<T>().To<C>().AsSingle()` /
    `.AsTransient()` / `.NonLazy()`, self-bind shorthand, `BindInstance<T>`,
    `BindInterfacesAndSelfTo<T>` / `BindInterfacesTo<T>`, and `BindFactory<...>`
    (0/1/2-parameter `IFactory<...>`).
  - `[Inject]` on constructor, field, property, or method; `Resolve<T>` /
    `TryResolve<T>` / `Inject(existing)` / `CanResolve(type)`; child containers as
    the scoped lifetime; sync `Build()` and async `BuildAsync(ct)` startup.
  - Optimized resolve path: process-wide compiled-activator cache
    (`Expression.Compile` once per `ConstructorInfo`), a `[ThreadStatic]`
    lock-free argument-array pool, and a per-plan per-slot dependency cache.
    Designed to avoid per-call managed allocation beyond the constructed
    instances themselves; beats VContainer and Zenject on all measured timing
    scenarios, while transient resolves still allocate the instance they return.
  - Member injection setters (field/property) wired into the activation pipeline.
  - Opt-in **BakedGraph** resolve path behind a feature flag for pre-resolved
    construction plans.
  - Actionable, fix-oriented errors: `OnityResolveException`,
    `OnityBindingException`, with opt-in binding-source attribution.

- **Reactive (`Onity.Reactive`)**
  - Primitives: `Subject<T>` (steady-state `OnNext` designed allocation-free) and
    `ReactiveProperty<T>` (built-in `DistinctUntilChanged`, emits current value on
    subscribe, `SetValue(T) -> bool`).
  - Synchronous operators: `Where`, `Select`, `DistinctUntilChanged`,
    `Skip`/`SkipWhile`, `Take`/`TakeWhile`, `StartWith`, `Scan`, `Pairwise`,
    `Merge`, `CombineLatest`, `Sample` - emit paths designed allocation-free.
  - Async / time operators: `Debounce`, `ThrottleLast`,
    `TakeUntil(CancellationToken)` / `TakeUntil(Task)`, `SelectAwait`,
    `WhereAwait`, plus `FirstAsync` / `ToTask` reactive-to-async bridges.
  - Exception isolation so one faulting observer cannot break sibling observers
    or the source stream; deterministic time testing via pluggable
    `OnityTimeProvider`.

- **Messaging (`Onity.Messaging`)**
  - Typed pub/sub broker: `IPublisher<T>` / `ISubscriber<T>` from
    `IMessageBroker`; steady-state `Publish` designed allocation-free, re-entrancy-safe
    (unsubscribe inside a handler is allowed).
  - `OnityEventHub` facade (`Publish<T>` / `Subscribe<T>` / `Observe<T>()`) and
    `broker.Observe<T>()` returning `IOnityObservable<T>`, so any event flows into
    the full reactive operator chain.
  - Keyed message channels and async publish/subscribe support.
  - Allocation-free diagnostics: `GetDiagnostics(List<...>)` and `ChannelCount`.

- **Unity integration (`Onity.Unity`)**
  - `MonoInstaller`, `ProjectContext` / `SceneContext` / `GameObjectContext`,
    auto-bound `IMessageBroker` + `OnityEventHub` per scope,
    `BindMessageChannel<T>()`.
  - Reactive Unity bridges: `EveryUpdate` / `EveryFixedUpdate` / `EveryLateUpdate`,
    `Timer`, `Interval`; lifetime helpers `AddTo(Component)` /
    `TakeUntilDestroy(Component)` / `TakeUntilDisable(Behaviour)`.
  - Input System reactive bridge: `PerformedAsObservable()` /
    `StartedAsObservable()` / `CanceledAsObservable()` and long-press observables.

- **Roslyn analyzer pack (ONITY001-ONITY006)** - six diagnostics that catch
  common misuse at compile time and steer code toward the correct Onity idiom.

- **Sample - "Coin Rush"** (`Samples/OnityShowcase`) - a runnable mini-game
  wiring all three pillars through a single installer with thin MonoBehaviours.

- **Documentation** - machine-readable AI Usage Guide (verified against source),
  Getting Started, architecture review, and migration guides from Zenject,
  VContainer, and R3 / UniRx.

### Tested

- Full EditMode suite green: **203/203** in Unity 6.4.
- Timing benchmarks captured on 2022.3.62f3 (Editor-Mono, Windows PC — indicative,
  not guaranteed). The resolve/publish/`OnNext`/`EveryUpdate` paths are designed to
  avoid per-call managed allocation, but the published allocation figures were
  unreliable and need a corrected in-editor re-measure; a transient resolve still
  allocates the instance it returns.

[0.4.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.4.0
[0.3.14]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.14
[0.3.13]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.13
[0.3.12]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.12
[0.3.11]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.11
[0.3.10]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.10
[0.3.9]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.9
[0.3.8]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.8
[0.3.7]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.7
[0.3.6]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.6
[0.3.5]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.5
[0.3.4]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.4
[0.3.3]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.3
[0.3.2]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.2
[0.3.1]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.1
[0.3.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.3.0
[0.2.1]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.2.1
[0.1.0]: https://github.com/FurkanTokkan/Onity/releases/tag/v0.1.0
