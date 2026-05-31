# 07 - Competitive Analysis and AI-Friendliness Roadmap

This document positions Onity against the three libraries it intends to
replace - **Zenject / VContainer** (DI), **R3 / UniRx** (Reactive), and
**MessagePipe** (Events/messaging) - and lays out the development roadmap to
lead all three while becoming the most **AI-friendly** Unity framework: one
consistent mental model, predictable Zenject-familiar naming, fluent
discoverable builders, actionable errors, compile-time safety, and a concise
machine-readable usage guide.

It is grounded in the real shipped surface (see per-file reads in the pillar
analyses), the measured DI benchmark
(`Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md`),
the performance gates in `04-Performance-Targets.md`, the architecture rules in
`01-Architecture.md`, and `EVENT_HUB_PLAN.md`.

---

## 1. Executive summary

### 1.1 Where Onity stands today

| Pillar | Competitor target | Status | One-line verdict |
|---|---|---|---|
| DI | Zenject, VContainer | **Editor + IL2CPP lead in current benchmarks** | Faster than both on all 5 Editor/Mono timing scenarios with baked resolve; Windows IL2CPP player with generated AOT activators is also faster than VContainer and Zenject on all 5 measured timing scenarios |
| Reactive | R3, UniRx | **Strong core, expanding coverage** | Subject/ReactiveProperty are re-entrancy-correct and allocation-aware; core combinators, error isolation, and Unity timing bridges are present, with broader operator/perf proof still planned |
| Events | MessagePipe | **On-philosophy, smaller surface** | Same algebra as reactive, DI-native auto-bind, keyed channels, async channels, and broker examples; no published MessagePipe publish microbenchmark yet |
| Cross-cutting | (all three combined) | **Unified DX now documented** | DI is the spine; broker + hub auto-bound in every scope; events bridge into reactive operators; AI usage guide and analyzer scaffold exist, with compile-time guidance still expanding |

### 1.2 The measured DI win (already true)

From `di-benchmark-summary.md` (Unity 2022.3.62f3, Windows, Mono editor,
512 warmup / 8 samples / mean). Onity baked resolve beats **both** VContainer
and Zenject on every measured Editor/Mono timing scenario:

| Scenario | Onity Baked (ns/op) | VContainer (ns/op) | Zenject (ns/op) | Onity vs VContainer |
|---|---:|---:|---:|---:|
| Resolve Singleton | 63 | 214 | 2,866 | **+71%** |
| Resolve Transient | 1,083 | 1,879 | 12,356 | **+42%** |
| Resolve Combined | 972 | 2,079 | 17,248 | **+53%** |
| Resolve Complex (6-level) | 22,905 | 42,158 | 289,823 | **+46%** |
| Prepare & Register Complex | 61,044 | 150,730 | 215,537 | **+60%** |

The Editor/Mono "beats VContainer everywhere" claim is true **including build**,
not just resolve. The Windows IL2CPP player run now uses generated AOT activators
for the benchmark graph and also beats the local VContainer baseline across
singleton, transient, combined, complex, and prepare/register. On Mono/JIT, the
speed comes from a process-wide compiled-activator cache (`Expression.Compile`
once per `ConstructorInfo`), compiled member setters, a `[ThreadStatic]`
lock-free `ArgumentArrayPool`, per-plan constructor-dependency caches, and a
baked provider-slot map. On IL2CPP, generated direct `new T(...)` activators
avoid `ConstructorInfo.Invoke` for hot types. Both paths avoid an explicit
`builder.Build()` ceremony before resolve and keep `Onity.DI.asmdef`
`noEngineReferences: true`.

### 1.3 The plan to also lead Reactive and Events

The DI lead is real; Reactive and Events need feature completion, not a
rewrite. The strategy is:

1. **Finish the gameplay-critical reactive operators.** CombineLatest, Merge,
   Scan, Pairwise, and Sample have **shipped** (`Reactive/Scripts/Operators/` +
   EditMode tests); remaining: Buffer, Window, Zip, Switch, and a leading-edge
   Throttle — all on a **single** unified observable model, 0-alloc-per-emit.
2. **Harden the reactive/event core**: global unhandled-exception hook so one
   throwing subscriber cannot break a frame loop, and a main-thread
   `ObserveOn(frameProvider)` hop to kill the async cross-thread footgun.
3. **Add the two real Events capabilities** MessagePipe has and Onity lacks:
   keyed messaging and async handlers - reusing the existing `MessageChannel`
   internals and staying 0-alloc on publish.
4. **Prove it**: ship the reactive/messaging benchmark runner and gates that
   `04-Performance-Targets.md` already specifies but has no results for yet.
5. **Win on AI-friendliness** - the differentiator no competitor has: one
   machine-readable usage guide, actionable Onity-branded errors across all
   three pillars, and a Roslyn analyzer pack that turns runtime failure modes
   into inline red squiggles.

---

## 2. Per-pillar comparison tables

Legend - **Onity has?**: Yes / Partial / No / **Auto** (works with zero user
wiring). **Verdict**: Adopt (build it), Doc (cover in the AI guide), or
Non-goal (deliberately omitted, see section 6).

### 2.1 DI - Onity vs Zenject / VContainer

| Feature | Zenject / VContainer | Onity has? | Onity does differently | Verdict |
|---|---|---|---|---|
| Fluent bind `To/AsSingle/AsTransient/NonLazy` | Yes (Zenject) | Yes | Same vocabulary; self-bind shorthand | Keep |
| Share one instance across many contracts | `Bind(typeof(IA),typeof(IB)).To<C>()` | Partial | Only via `BindInterfacesAndSelfTo` / `BindInterfacesTo`; two separate `Bind<I>().To<C>()` make **distinct** singletons | **Adopt** (`.And<>()` multi-contract bind) |
| Collection injection (`IEnumerable<T>`/`T[]`) | Yes (both) | No | Last-binding-wins overwrites; no closed `IEnumerable<T>` resolve | **Adopt** (build once at Build into cached provider) |
| Runtime-arg construction | `Instantiate<T>(args)` (Zenject) | Partial | Only via hand-authored `IFactory<TParam,TValue>` | **Adopt** (`Instantiate<T>(in args)` / placeholder factory) |
| Constructor selection | Fewest/greediest (Zenject) | Yes (diverges) | Single `[Inject]` ctor, else **most-params** public ctor | Doc (test-locked divergence) |
| Circular detection | Build-time (both) | Yes (diverges) | **Resolve-time** via `[ThreadStatic]` stack; member cycles **throw** | Doc |
| Scoped lifetime | `Lifetime.Scoped` (VContainer) | Partial | Lifetime enum is `{Singleton, Transient}`; scope == child container | **Non-goal** (document child-container recipe) |
| Conditional / id bindings | `WhenInjectedInto`, `WithId` (Zenject) | No | Intentionally omitted | **Non-goal** |
| Open-generic binding `typeof(T<>)` | Yes | No | Closed generics only | **Non-goal** (defer; document closed-only) |
| Unbind / Rebind | Yes (Zenject) | Partial | Rebind via last-binding-wins; no Unbind | **Non-goal** |
| Lifecycle entry points | `IInitializable/ITickable` | Partial | `NonLazy` + `RegisterBuildCallback` + `[Inject]` methods; ticking lives in Reactive | **Adopt** lite (`IOnityInitializable`/`IOnityTickable` via reactive pump, not in `Onity.DI`) |
| Async build callbacks | Limited | Yes | `BuildAsync` runs + caches + re-arms on cancel/failure | Keep |
| Edit-time validation | `ValidateAll` / Roslyn (both) | No | Detects missing/circular only at resolve time | **Adopt** (Roslyn analyzer) |
| Engine-free, test-without-scene | No (Zenject); partial (VContainer) | **Yes** | `new OnityContainer()` in EditMode, zero Unity setup | **Onity win** |
| Binding-source attribution | No (default) | Yes | `PushBindingSource` / `TryGetBindingSource` per-binding | **Onity win** |
| Resolve performance | Baseline | **Faster** | Beats both on all 5 scenarios; 0 B/op resolve | **Onity win** |

### 2.2 Reactive - Onity vs R3 / UniRx

| Feature | R3 / UniRx | Onity has? | Onity does differently | Verdict |
|---|---|---|---|---|
| `Subject<T>` multicast | Yes | Yes | Struct-array + swap-back + deferred compaction; **0 B/op OnNext** | **Onity win** |
| `ReactiveProperty<T>` | Yes | Yes | `DistinctUntilChanged` folded into `SetValue`; emit-current-on-subscribe; `SetValue -> bool` | **Onity win** |
| Re-entrant dispose during OnNext | UniRx historically buggy | Yes | Slot-null + deferred compact, test-locked | **Onity win** |
| `Where/Select/Take/Skip/DistinctUntilChanged/StartWith` | Yes | Yes | 0-alloc per emit (subscribe-time wrapper only) | Keep |
| `CombineLatest / Merge / Zip / Switch` | Yes | **No** | Absent across the package | **Adopt** (CombineLatest, Merge first) |
| `Scan / Pairwise` | Yes | **No** | No stateful accumulation | **Adopt** |
| `Buffer / Sample / Window` | Yes | **No** | No batching | **Adopt** (Buffer+Sample; defer Window) |
| Error-flow `Catch / Retry / Timeout` | Yes | **No** | `Subject.OnNext` has **no try/catch**; `Action<T>` path has no OnError channel | **Adopt** (hook first, then operators) |
| Global unhandled-exception sink | `ObservableSystem.UnhandledException` | **No** | One throwing subscriber can abort the multicast loop | **Adopt** (P0 reliability) |
| Main-thread hop `ObserveOn(frameProvider)` | Yes (R3 headline) | **No** | `SelectAwait/WhereAwait` resume on threadpool | **Adopt** (P1, owns the pump already) |
| Leading-edge `Throttle` | Yes | Partial | Only `ThrottleLast`; docs call it `Throttle` (drift) | **Adopt** + fix doc drift |
| Deterministic time testing | R3 `TimeProvider` | Yes | Pluggable abstract `OnityTimeProvider` (operators accept one); EditMode tests inject a manual `OnityTimeProvider` subclass to drive delays deterministically. No public `Manual`/`Fake` provider ships yet (each test defines its own) | **Onity win** (add a public manual provider for parity polish) |
| Unity lifetime `AddTo/TakeUntilDestroy` | Yes (both) | Yes | `AddTo(Component)` / `TakeUntilDisable(Behaviour)` via on-demand notifier | Keep |
| Frame loops `EveryUpdate/Interval/Timer` | Yes (R3.Unity) | Yes | Shared lazy singleton subjects, one hidden pump | Keep |
| Input System observables | Thin (R3) | Yes | `Started/Performed/CanceledAsObservable` + long-press in-box | **Onity win** |
| Leak diagnostics | `ObservableTracker` | Partial | `OnityObservableTracker` ring buffer; only on the `OnityObserver` path | Doc + extend |
| Single observable type | Yes (R3 `Observable<T>`) | **No** | Public `IOnityObservable<T>` **and** internal `IOnityObservableV2<T>` with `ToV2/ToLegacy` | **Adopt** (collapse to one) |
| Hot/cold `Publish/Share/RefCount` | Yes | No | Multicast served by `Subject<T>` | **Non-goal** |
| `Create/Defer/Never` cold factories | Yes | No | Docs claim them; not implemented | **Non-goal** (cheap later; not blocking) |
| `ObserveEveryValueChanged` (poll) | Yes (UniRx) | No | Conflicts with push + 0-alloc | **Non-goal** |
| Real off-thread Job/Burst operators | N/A | **No (stub)** | `JobMultiThread/Burst/Dots` insert a marker job only | **Non-goal for perf claims** (label experimental) |

### 2.3 Events - Onity vs MessagePipe

| Feature | MessagePipe | Onity has? | Onity does differently | Verdict |
|---|---|---|---|---|
| Typed `IPublisher<T>/ISubscriber<T>` | Yes | Yes | Identical vocabulary | Keep |
| 0-alloc steady-state publish | Yes | Yes | Struct-array for-loop, no enumerator/closure | **Onity win (parity)** |
| Re-entrant unsubscribe during publish | Yes | Yes | Slot-null + deferred compact, test-locked | Keep |
| DI integration | Yes (`AddMessagePipe`) | **Auto** | `MessageBroker` + `OnityEventHub` auto-bound `AsSingle` in every scope | **Onity win** |
| Inject `IPublisher<T>/ISubscriber<T>` for any T | Yes (auto) | Partial | Requires explicit `BindMessageChannel<T>()` per type | **Adopt** (open-generic auto-resolve from broker) |
| Events as reactive stream | Needs manual adapter | **Yes** | `broker.Observe<T>()` -> full operator chain, same model | **Onity win** |
| Keyed `IPublisher<TKey,TMessage>` | Yes | **No** | Keyed only by message Type | **Adopt** (`KeyedMessageChannel`, EVENT_HUB item) |
| Async handlers `PublishAsync` | Yes (Parallel/Seq/FAF) | **No** | Sync void handlers only | **Adopt** (Sequential default, async slice) |
| Subscribe-time filters | `MessageHandlerFilter<T>` | No | Covered by `Observe<T>().Where(...)` | **Non-goal** (use reactive operators) |
| Buffered/replay events | `IBufferedPublisher<T>` | No | Covered by `ReactiveProperty<T>` + `StartWith` | **Non-goal** |
| Handler ordering / priority | `Order` int | No | Subscription order only | **Non-goal** core (revisit via EventHub if a sample needs it) |
| Request-response | `IRequestHandler` | No | This is a DI service call, not a bus concern | **Non-goal** |
| Global static facade | `GlobalMessagePipe` | No | Breaks scope isolation | **Non-goal** (injected hub instead) |
| Built-in diagnostics | Separate window/pkg | Yes | `GetDiagnostics(List<...>)` + `ChannelCount` in core type, no alloc | **Onity win** |
| Emitter / channel / `[OnEvent]` | Filters/order | No | Planned in `EVENT_HUB_PLAN.md` | **Adopt later** (only if a sample needs it) |
| Published microbenchmark | Yes | **No** | `04` specifies scenarios; `reactive-benchmark-summary.md` missing | **Adopt** (perf proof before any "beats MessagePipe" claim) |

### 2.4 Cross-cutting (the unified-framework advantage)

| Concern | Zenject + R3 + MessagePipe (3 libs) | Onity has? | Onity does differently | Verdict |
|---|---|---|---|---|
| One install / one namespace family | No (3 packages) | Yes | Single `Onity.*` stack, one container spine | **Onity win** |
| DI <-> Events glue | Hand-rolled adapter | **Auto** | Broker + hub auto-bound per scope; ctor-inject `IPublisher<T>` | **Onity win** |
| Events <-> Reactive bridge | Manual `ISubscriber`->`Observable` | Yes | `Observe<T>()` returns `IOnityObservable<T>`, same operators | **Onity win** |
| One disposal model | 3 disposal idioms | Yes | One `IDisposable` + `AddTo(...)` everywhere | **Onity win** |
| One registration vocabulary | 3 vocabularies | Partial | DI `Bind`, Events `Declare/GetPublisher`, Reactive `new`+`BindInstance` | **Adopt** (`DeclareMessage<T>`, `BindReactiveProperty<T>`, `BindSubject<T>`) |
| One observable type | R3: one; Onity: two | **No** | Public `IOnityObservable<T>` + internal `IOnityObservableV2<T>`; async/time operators are authored on v2 and re-exposed via public `IOnityObservable<T>` wrappers | **Adopt** (collapse) |
| Consistent error UX | Per-library | Partial | Only DI throws Onity-branded fix-oriented errors | **Adopt** (Onity error standard across pillars) |
| Compile-time misuse safety | None of the three | **No** | Runtime-only failures today | **Adopt** (Onity.Analyzers - the leapfrog) |
| Machine-readable AI usage guide | None | **No** | Only ENGINEERING.md + design docs | **Adopt** (P0, single biggest AI lever) |

---

## 3. Performance posture and the gates to hold

### 3.1 DI - strong and verified (hold the lead)

DI already meets its competitive goal. The gates in
`04-Performance-Targets.md` section 3.1 are the contract to **not regress**
(ratchet rule: a >5% regression without a documented reason fails CI).

| Scenario | Onity baked now (ns/op) | Phase 1 gate | Stretch | Gate status |
|---|---:|---:|---:|---|
| Resolve Singleton | 63 | <= 150 | <= 130 | **Beats stretch** |
| Resolve Transient | 1,083 | <= 1,500 | <= 1,200 | **Passes gate** |
| Resolve Combined | 972 | <= 1,550 | <= 1,250 | **Beats stretch** |
| Resolve Complex | 22,905 | <= 35,000 | <= 28,000 | **Beats stretch** |
| Prepare & Register Complex | 61,044 | <= 15,000 | <= 12,000 | **Missed internal gate** (still ~60% faster than VContainer) |
| Resolve alloc / sample (B) | Pending | 0 | 0 | Add allocation benchmark coverage |

Benchmark note: Onity wins every Editor/Mono timing head-to-head, but the internal
`Prepare & Register Complex` gate is still not met and allocation coverage is
tracked separately. The Windows IL2CPP player benchmark now proves the
benchmark graph runs without crashing, registers generated AOT activators, and
beats the measured VContainer baseline on singleton, transient, combined,
complex, and prepare/register. The next meaningful step is broader
generated-activator coverage plus Android/WebGL/device runs.

### 3.2 Reactive - strong primitives, prove the rest

The synchronous push core is genuinely fast: `Subject<T>.OnNext` and
`ReactiveProperty<T>.SetValue` are **0 B/op steady-state**, and sync operators
allocate only at subscribe time (one wrapper + one closure per operator). No
`System.Linq` anywhere (verified).

Gaps that must be fixed before claiming R3 parity in production:

- **Subscribe-time allocation**: every `Subject.Subscribe` allocates a
  capturing closure + `DisposableAction`. Replace with a
  `struct Subscription { owner, id } : IDisposable` (mirrors the DI pillar's
  zero-alloc ethos) - matters for spawn/despawn churn.
- **Async operators are heavy and off-thread**: `SelectAwait/WhereAwait`
  allocate a per-subscription `Queue<T>` + CTS + closures **and** dispatch the
  user delegate on `Task.Run`, then call `OnNext` from a threadpool thread - a
  GC cost and a main-thread-safety hazard. Pool the state; add a main-thread
  hop.
- **Threading modes are aspirational**: Job/Burst/Dots schedule a marker
  `IJobParallelFor` only; operator work stays on the main thread, and each
  subscription allocates a `NativeArray(Persistent)`. Label experimental;
  exclude from perf claims.

Proposed reactive gates (already specified qualitatively in `04` section 4.1;
make them numeric when the runner lands in
`Packages/com.onity.framework/Benchmarks/Results/reactive-benchmark-summary.md`):

| Scenario | Gate |
|---|---|
| Subject OnNext (100 subs) | 0 alloc/emit; faster than `event Action<T>` invocation |
| ReactiveProperty SetValue (no-op change) | 0 alloc; near-zero (comparer call only) |
| ReactiveProperty SetValue (real change) | 0 alloc; single subject pump |
| 3-operator chain emission | 0 alloc/emit; < 1.5x bare-Subject cost |
| EveryUpdate, 50 subs | 0 alloc per frame |
| New operators (CombineLatest/Merge/Scan/Buffer/Sample) | 0 alloc per emit (state in wrapping observer) |

### 3.3 Events - strong and on-philosophy, but unmeasured

`MessageChannel<T>.Publish` is a verified 0-alloc for-loop over a struct array;
`Subscribe` allocates exactly one `DisposableAction` (subscribe is not a hot
path, allowed by `04` section 2.3). Re-entrant unsubscribe is O(1) slot-null +
amortized deferred compaction.

Gates to add (specified in `04` section 2.2, no results yet - **required before
any "beats MessagePipe on Publish" claim**):

| Scenario | Gate |
|---|---|
| MessageBroker.Publish (10 subs) | 0 alloc; < `event Action<T>` + dictionary lookup |
| EventHub Listen + Publish | 0 alloc steady-state publish |
| Keyed publish (when shipped) | dictionary lookup + existing for-loop; 0 alloc |
| Async PublishAsync Sequential | low-alloc (pooled awaiter); Parallel is the only allocating opt-in |

**Threading contract to document (not change):** `MessageBroker` channel
*creation* is locked (thread-safe), but `Publish/Subscribe` and `Subject.OnNext`
are **not** internally locked - they assume the Unity main thread. The XML docs
must say so explicitly so an AI never publishes from a job/thread.

### 3.4 Cross-cutting performance invariants (must hold as the unifying layer lands)

The unification is **additive sugar + editor tooling over an already-optimized
core**. None of it may regress the verified 0-alloc hot paths:

1. Open-generic `IPublisher<T>/ISubscriber<T>` resolution must **cache** the
   per-closed-type provider on first resolve (mirror the existing
   implicit-provider pattern) so it never re-runs `GetPublisher` reflection.
2. The fluent feature-installer DSL is **build-time only** (runs inside
   `InstallBindings` before `Build()`) - zero steady-state cost.
3. `IOnityTickable` pumping subscribes each tickable **once** to the shared
   `EveryUpdate` stream and dispatches via a plain indexed loop - no per-frame
   LINQ, no per-frame delegate allocation.
4. Collapsing v1/v2 observables must keep `Subscribe` 0-alloc steady-state.
5. EventHub emitter/channel/priority work keeps reflection on the bind/setup
   path only (already mandated by `EVENT_HUB_PLAN.md`).
6. Analyzers and the AI usage guide are authoring artifacts with **no runtime
   footprint**.

Enforce each with the existing benchmark + GC-allocation assertions when the
piece lands.

---

## 4. AI-friendliness strategy

The central goal: an AI agent writing gameplay code uses Onity correctly and
comfortably with **one mental model** across DI + Reactive + Events. The
following improvements are aggregated across all four pillar analyses and
de-duplicated by priority.

### 4.1 The unified-API design (the through-line)

The mental model the AI must internalize, in one sentence each:

- **DI is the spine.** Bind in a `MonoInstaller`; resolve via constructor
  params. `IMessageBroker` and `OnityEventHub` are auto-bound in every scope.
- **Events ride the broker.** Publish a typed struct; subscribe and `AddTo`.
- **Reactive operators ride both.** `Observe<T>()` turns any event into a
  stream; `ReactiveProperty<T>` is state; the same `Where/Select/...` operators
  apply to subjects, properties, and events.
- **Everything disposes the same way.** `Subscribe` returns `IDisposable`;
  `AddTo(this)` / `AddTo(CompositeDisposable)` scopes lifetime for DI, events,
  and reactive identically.
- **Scopes are child containers.** Parent `AsSingle` is shared; a per-scope
  instance is a child-container `AsSingle`.

To make that model real and unambiguous, the cross-cutting work is:
collapse to **one** observable type, make `IPublisher<T>/ISubscriber<T>`
**auto-resolvable** per type, add a **one-verb-family** installer DSL
(`Bind*`/`Declare*`), standardize **one error format**, and ship **one**
machine-readable guide plus a Roslyn **analyzer** pack.

### 4.2 P0 - do first (highest leverage, lowest risk)

| # | Improvement | Pillar(s) | Why it matters for an AI |
|---|---|---|---|
| P0-1 | **Single machine-readable AI usage guide** (`docs/AI/onity-ai-usage-guide.md` + per-pillar recipe sections / `llms.txt`) | All | The single biggest lever. Flat tables, real-API-only: binding surface, `[Inject]` rules, action->API naming map, the auto-bound `IPublisher<T>` recipe, and the **DO/DON'T divergence tables** mirrored from the parity tests (e.g. "DON'T expect two `Bind<I>().To<C>()` to share an instance; DO use `BindInterfacesAndSelfTo` or `.And<>()`"; "scope == child container"; "closed generics only"; "operator is `ThrottleLast`, not `Throttle`"). Prevents hallucinated API. |
| P0-2 | **Onity error-message standard across all pillars** | All | DI already names the type + fix; Reactive/Messaging throw bare `ArgumentNullException`/`ObjectDisposedException`. Standard: `[Onity.<Pillar>] <what failed> for <type>. <why>. Fix: <action>.` Enrich `OnityResolveException` with the dependency chain (`A->B->C` from `s_resolutionStack`), the injection site, the full circular cycle, and a one-line fix. Failures become self-correcting signals. |
| P0-3 | **Collapse to one observable type** (`IOnityObservable<T>`; fold the internal `IOnityObservableV2<T>` operator implementations onto it directly; keep all operators on the public interface) | Reactive, Cross-cut | `IOnityObservableV2<T>`/`ToV2`/`ToLegacy` are already internal, so authored code never sees two interfaces; but the async/time operators are still authored against v2 and only re-exposed via thin public wrappers, which is internal duplication and a maintenance hazard. Removing the second interface keeps the public surface single and the operator set unified. |
| P0-4 | **Reconcile docs to the shipped surface** | Reactive | `03-Reactive-Design.md`/ENGINEERING.md disagree with source (`Throttle` vs `ThrottleLast`; unshipped `Never/Create/Defer`; claimed `AddTo(GameObject)`; wrong `MessageChannel` `IOnityObservable` claim). An AI loading these as ground truth emits non-compiling code. |
| P0-5 | **Gameplay-critical operators** (CombineLatest 2-4, Merge, Scan, Pairwise) on the unified model | Reactive | These are what an AI reaches for (derive a value from two stats, merge inputs, accumulate a combo). Absence forces verbose manual `ReactiveProperty` bookkeeping the AI gets subtly wrong. 0-alloc per emit via wrapping-observer state. |
| P0-6 | **Global unhandled-exception hook + try/catch around `Subject<T>.OnNext`** | Reactive, Events | Reliability, not a feature. One throwing subscriber must not abort the multicast loop or corrupt swap-back state in an `EveryUpdate`-driven frame loop. Mirror R3 `ObservableSystem.UnhandledException`. |

### 4.3 P1 - do next

| # | Improvement | Pillar(s) | Why |
|---|---|---|---|
| P1-1 | **Roslyn analyzer pack** (`Onity.Analyzers`, editor-only, zero runtime/engine cost) | All | The leapfrog feature - none of the three competitors has compile-time safety. Rules: unbound `[Inject]`/ctor-param, multiple `[Inject]` ctors, setterless/indexer/generic/static `[Inject]`, likely cycles, undisposed `Subscribe` (missing `AddTo`), `Resolve` in `Update`, references to the v2 observable stack, `Publish` off main thread. Each ships with a code-fix. Turns runtime failure modes into inline red squiggles an AI self-corrects on. |
| P1-2 | **Fluent multi-contract bind** `Bind<IFoo>().And<IQux>().To<Foo>().AsSingle()` | DI | Closes the #1 Zenject silent-mismatch trap. Reuses the existing shared `Register(Type[],...)` provider; 0-alloc resolve; engine-free. |
| P1-3 | **Collection injection** (`IEnumerable<T>`/`IReadOnlyList<T>`/`T[]`) built once at Build into a cached provider | DI | Plugin/handler/strategy registries are everyday gameplay; both competitors have it. Build-time array build keeps resolve 0-alloc. |
| P1-4 | **Auto-resolve open-generic `IPublisher<T>/ISubscriber<T>`** from the auto-bound broker (cached per closed type) | Events, Cross-cut | Removes the non-obvious `BindMessageChannel<T>()` prerequisite; matches MessagePipe "inject `IPublisher<T>` for any T" ergonomics. |
| P1-5 | **Fluent feature-installer DSL** (`DeclareMessage<T>()`, `BindReactiveProperty<T>(initial)`, `BindSubject<T>()`) | Cross-cut | One `Bind*`/`Declare*` vocabulary, one location to register a feature's DI + events + state. Build-time only. |
| P1-6 | **Main-thread hop** `ObserveOnMainThread()` / `ObserveOn(OnityFrameProvider)` re-posting onto the existing pump | Reactive | Closes R3's headline Unity advantage and removes the cross-thread crash footgun after any `*Await`/async operator. Onity already owns the frame providers + pump. |
| P1-7 | **Runtime-arg construction** `Instantiate<T>(in args)` / generated placeholder-factory binding | DI | Removes per-type bespoke `IFactory` boilerplate for spawn-with-position/config. Reuses compiled activator + `ArgumentArrayPool`; stays engine-free. |
| P1-8 | **Time/batch operators** leading-edge `Throttle`, `Sample(signal)`, `Buffer(int)`/`Buffer(TimeSpan)` with pooled buffers | Reactive | Batch per-frame damage, sample input on FixedUpdate. Reuses `OnityTimeProvider` (deterministic tests). |
| P1-9 | **Keyed messaging** `IKeyedPublisher<TKey,TMessage>` / `KeyedMessageChannel` | Events | Biggest real Events gap + `EVENT_HUB_PLAN` item 2. Reuses `MessageChannel` internals; 0-alloc publish; MessagePipe names for AI transfer. |
| P1-10 | **Reactive/messaging benchmark runner + published results** | Reactive, Events | Required by `04` before any "beats R3/MessagePipe" claim; the DI runner pattern already exists. |

### 4.4 P2 - polish and rounding out

| # | Improvement | Pillar(s) | Why |
|---|---|---|---|
| P2-1 | **Canonical DI+Reactive+Messaging wiring** as the headline recipe in the AI guide | Cross-cut | The proof of "one mental model": inject `IPublisher<DamageEvent>` with no manual bind. Should be the first example an agent sees. |
| P2-2 | **XML-doc cross-linking + naming-consistency pass** (`<see>` links, standardized subscribe/dispose phrasing, the `OnNext` vs `Value=` rule, fill missing docs on reactive operators + Unity bridges) | All | IntelliSense is what an AI reads at the call site; consistent cross-references multiply the guide's effect. |
| P2-3 | **Disposal-leak guidance in the tracker** (optional log with captured subscribe stack when a subscription outlives its owner) | Reactive | Gives the agent a concrete "you forgot to dispose at <file:line>" signal during play. |
| P2-4 | **Async handlers** `IAsyncSubscriber<T>/IAsyncPublisher<T>.PublishAsync` (Sequential default, Parallel opt-in) | Events | "await all listeners before scene unload" is a real need; `ValueTask`+CT, pooled awaiter, in an async slice (not engine-free core if it must touch the loop). |
| P2-5 | **Unified DI lifecycle interfaces** `IOnityInitializable`/`IOnityTickable` pumped via `EveryUpdate` | Cross-cut | Familiar Zenject/VContainer idiom; pure-C# services join the frame loop with no MonoBehaviour. Indexed dispatch, 0 per-frame alloc. Pumping stays in the Unity layer, **not** `Onity.DI`. |
| P2-6 | **Error-flow operators** `Catch/Retry/Timeout` | Reactive | Now feasible once the model carries `OnError`. Lower urgency than combinators. |
| P2-7 | **`IOnityMessage` marker (documentary, zero-cost) + zero-subscriber debug warning** | Events | Light discoverability nudge for message types; debug-only warning gated behind diagnostics opt-in. |

---

## 5. Prioritized roadmap

Ordered phases. Each phase: goal, items (with effort S/M/L), and an exit gate.
Phases A-B are pure DX/correctness with little code risk; later phases add
features and tooling. **Adopt** vs **Non-goal** is marked per item; non-goals
are expanded in section 6.

### Phase A - Authoring foundation (no/low code risk)

**Goal:** Give an AI an authoritative, compiling mental model and consistent
errors before any new feature lands.

| Item | Effort | Adopt? |
|---|---|---|
| Write `docs/AI/onity-ai-usage-guide.md` (mental model, action->API map, canonical recipes, DO/DON'T divergence tables mirrored from `OnityZenject*ParityTests`, error->fix table skeleton, "which observable type", lifetime/threading rules) | M | Adopt (P0-1) |
| Reconcile `03-Reactive-Design.md` + ENGINEERING.md to the shipped surface (`Throttle`->`ThrottleLast`; remove unshipped `Never/Create/Defer`/`AddTo(GameObject)`; correct `MessageChannel` claim; mark Phase-2 items "planned") | S | Adopt (P0-4) |
| Author events decision rules (message vs `ReactiveProperty` vs service call) + add main-thread/lifetime/dispose contracts to public XML docs | S | Adopt |
| Define + apply the Onity error standard to all Reactive + Messaging throws (`OnityReactiveException`/`OnityMessagingException` or shared base); enrich `OnityResolveException`/`OnityBindingException` with chain + site + cycle + fix | S | Adopt (P0-2) |

**Exit gate:** the AI guide compiles against the current surface (every snippet
builds); docs no longer name a non-existent API; a focused test asserts each new
error message contains the dependency chain / fix text.

### Phase B - Unify the surface

**Goal:** Deliver "one mental model" structurally: one observable type, one
auto-resolving event injection, one registration vocabulary.

| Item | Effort | Adopt? |
|---|---|---|
| Collapse public observable to `IOnityObservable<T>`; make v2 internal/advanced; forward all async operators onto v1 so `ToV2/ToLegacy` never appear in authored code | M | Adopt (P0-3) |
| Add global unhandled-exception hook + try/catch around `Subject<T>.OnNext` (test: throwing observer does not stop siblings) | S | Adopt (P0-6) |
| Auto-resolve open-generic `IPublisher<T>/ISubscriber<T>` from the auto-bound broker (cache provider per closed type into `m_providerMap`) | M | Adopt (P1-4) |
| Fluent feature-installer DSL: `DeclareMessage<T>()`, `BindReactiveProperty<T>(initial)`, `BindSubject<T>()` | M | Adopt (P1-5) |

**Exit gate:** authored gameplay code never references v2 or `ToV2/ToLegacy`;
`Subscribe` stays 0-alloc steady-state (benchmark assertion); a parity test
matches MessagePipe "inject `IPublisher<T>` for any T"; the feature DSL has zero
steady-state cost.

### Phase C - Reactive operator parity (the gameplay catalog)

**Goal:** Ship the operators an AI reaches for, on the unified model,
0-alloc per emit. Aligns with `05-Implementation-Phases.md` Phase 2.

| Item | Effort | Adopt? |
|---|---|---|
| CombineLatest (2-4), Merge(params), Scan(seed,fn), Pairwise() via wrapping-observer state | M | Adopt (P0-5) |
| Leading-edge `Throttle(TimeSpan, OnityTimeProvider)`, `Sample(signal)`, `Buffer(int)`/`Buffer(TimeSpan)` with pooled/cleared buffers | M | Adopt (P1-8) |
| `ObserveOnMainThread()` / `ObserveOn(OnityFrameProvider)` re-posting onto the pump; documented as mandatory after any `*Await`/async operator | M | Adopt (P1-6) |
| Reactive benchmark runner + `reactive-benchmark-summary.md` (scenarios from `04` section 2.2) | M | Adopt (P1-10) |
| Label Job/Burst/Dots thread modes experimental/no-op in docs + XML until they offload real work | S | Adopt (truth-in-perf) |
| Defer `Zip/Switch/Window/Concat/Publish/Share/RefCount/ObserveEveryValueChanged` | L | **Non-goal / backlog** |

**Exit gate:** each new operator has an EditMode test + a benchmark scenario;
all reactive gates in section 3.2 pass with published numbers; no
`System.Linq`; 0-alloc per emit confirmed.

### Phase D - Events capability + DX

**Goal:** Close the two real MessagePipe capability gaps and prove publish
perf. Aligns with `05` Phase 3 / `EVENT_HUB_PLAN.md`.

| Item | Effort | Adopt? |
|---|---|---|
| Keyed messaging: `IKeyedPublisher<TKey,TMessage>`/`IKeyedSubscriber` + `KeyedMessageChannel<TKey,TMessage>` (reuse `MessageChannel` internals); `BindKeyedMessageChannel` | M | Adopt (P1-9) |
| Messaging microbenchmarks (Publish 10 subs, EventHub Listen+Publish) in the reactive runner; publish results with 0-alloc gate | M | Adopt (P1-10) |
| Async handlers: `IAsyncSubscriber<T>/IAsyncPublisher<T>.PublishAsync(msg, ct)` - Sequential default (low-alloc), Parallel opt-in; `ValueTask`+CT, pooled awaiter, async slice | L | Adopt (P2-4) |
| End-to-end DI+message+reactive sample + smoke test exercising all three pillars | S | Adopt |

**Exit gate:** keyed publish is 0-alloc steady-state with re-entrancy +
per-key disposal tests; published messaging numbers beat
`event Action<T>` + dictionary lookup; async Sequential path is low-alloc.

### Phase E - Compile-time safety (the leapfrog)

**Goal:** Turn the runtime failure modes of all three pillars into authoring-
time diagnostics no competitor offers.

| Item | Effort | Adopt? |
|---|---|---|
| `Onity.Analyzers` v1 (high-confidence, syntactic): register-after-build, missing-`AddTo`, wrong-observable-stack, `Resolve`-in-`Update`; each with a code-fix | L | Adopt (P1-1) |
| Analyzer DI rules: unbound `[Inject]`/ctor-param, multiple `[Inject]` ctors, setterless/indexer/generic/static `[Inject]`, likely cycles | L | Adopt (P1-1) |
| Analyzer v2 (semantic, cross-file): unbound-contract heuristic (whitelist auto-bound `IMessageBroker/IResolver/OnityEventHub` + implicit concretes), subscribe/property-without-dispose | L | Adopt |
| Ship `REACTIVE_USAGE.md` + events recipe sections folded into the AI guide | M | Adopt |

**Exit gate:** the pack is a separate analyzer assembly with **no UnityEngine
reference and zero runtime cost**; v1 rules have a measured low false-positive
rate on the Onity samples; each diagnostic names the offending type and offers a
fix.

### Phase F - Perf follow-through and platform hardening

**Goal:** Strictly meet the DI gate numbers and de-risk on-device. Aligns with
`05` Phase 1 deferred items + `04` section 3.2.

| Item | Effort | Adopt? |
|---|---|---|
| Keep the provider-slot baked graph and parity suite green while adding regression tests around build/resolve timing thresholds | M | Adopt |
| Add allocation benchmark coverage for singleton, transient, and complex resolves | M | Adopt |
| Reduce baked `Prepare & Register Complex` under 15,000 ns with source-generated or IL post-processed activators | M | Adopt |
| Keep Windows IL2CPP benchmark coverage green, add Android/WebGL coverage, and broaden source-generated or IL-postprocessed activator coverage for IL2CPP resolve speed | L | Adopt (platform hardening) |
| Replace per-subscribe closure+`DisposableAction` in `Subject<T>`/`ReactiveProperty` with `struct Subscription { owner, id } : IDisposable` | M | Adopt |
| Pool async-operator state (`Queue<T>`/CTS) in `SelectAwait/WhereAwait/Debounce/ThrottleLast`; default Debounce/ThrottleLast to a Unity time provider in the Unity bridge | M | Adopt |

**Exit gate:** baked resolve remains parity-green; allocation benchmark coverage
is published; IL2CPP benchmark runs without throwing and generated
activators stay ahead of the local VContainer baseline across singleton,
transient, combined, and complex resolve; `Prepare & Register Complex` is either
below the 15,000 ns internal gate or the remaining build-time gap is explicitly
scoped into the source-generation milestone; subscribe-time GC drops measurably
on a spawn/despawn benchmark.

### Phase G - Ergonomic event layer + lifecycle (optional, demand-driven)

**Goal:** The `EVENT_HUB_PLAN.md` ergonomic surface and unified lifecycle - only
if a sample needs them (no-speculative-API rule).

| Item | Effort | Adopt? |
|---|---|---|
| `OnityEventHub` emitter/channel/priority + `Bind/Unbind` + `[OnEvent]` (over `IMessageBroker`, still `Observe`-able; reflection at setup only) | L | Adopt later (if a sample blocks) |
| `IOnityInitializable`/`IOnityTickable` pumped once via `EveryUpdate` for NonLazy services (indexed dispatch, 0 per-frame alloc; pumping in the Unity layer) | M | Adopt (P2-5) |
| XML-doc cross-linking + naming-consistency pass across pillars + Unity bridges | M | Adopt (P2-2) |

**Exit gate:** EventHub publish is 0-alloc steady-state with no reflection on
the publish path (ordering/re-entrancy/unsubscribe tests + microbench vs
`MessageChannel`); tickable init order + disposal-on-teardown tested.

---

## 6. Explicit non-goals (do NOT adopt)

These exist in competitors but are deliberately omitted. Each fights the
single-mental-model / zero-hot-path-allocation / engine-free-core goals, or is
already covered more cleanly by another Onity pillar.

| Non-goal | From | Why Onity does not adopt it |
|---|---|---|
| First-class `Scoped` lifetime keyword | VContainer | Parent/child containers already cover it: parent `AsSingle` is shared, per-scope is a child-container `AsSingle`. A true Scoped lifetime forces the resolver to key singletons by requesting scope, complicating the hot path for marginal benefit. **Document the child-container recipe instead.** |
| Conditional / contextual / id bindings (`WhenInjectedInto`, `WithId`, `FromSubContainerResolve`) | Zenject | The biggest source of Zenject complexity and runtime ambiguity; adds per-resolve condition evaluation to the hot path and fights a predictable model. The Onity answer to "two impls of one interface" is collection injection or a typed factory. |
| Open-generic binding `typeof(T<>)` | Zenject | Requires constructing closed activators/plans on demand - a real hot-path and AOT/IL2CPP complication. Defer as a later measured feature; **document the closed-generic requirement** so the AI does not attempt `typeof(T<>)` binds. |
| Unbind / Rebind API | Zenject | Last-binding-wins covers the common rebind case; removing bindings post-Build conflicts with the "no bindings after Build" rule and the baked/cached resolution model. |
| `IInitializable/ITickable` auto-tick **inside `Onity.DI`** | Zenject, VContainer | Per-frame dispatch in the DI layer couples it to the Unity update loop and adds a managed iteration cost every frame - against the engine-free-core philosophy. Onity routes per-frame work through Reactive (`EveryUpdate`) and offers `IOnityTickable` **in the Unity layer** instead. |
| Hot/cold conversion `Publish/Share/RefCount` | R3 | Implicit ref-counting contradicts the "one subscribe = one disposable" principle; multicast is already served by `Subject<T>`. |
| `ObserveEveryValueChanged` (polling) | UniRx | Inherently a per-frame polling allocation/CPU pattern that conflicts with push-based + 0-alloc. Steer users to `ReactiveProperty`/`Observe<T>`. |
| Real claim of off-thread Job/Burst/Dots reactive operators | (none) | The current implementation schedules a marker job only; operator work stays single-threaded. **Label experimental and exclude from perf claims** rather than implying parallelism. |
| MessagePipe-style subscribe-time filter pipeline | MessagePipe | Adds a per-message virtual-call chain + DI resolution into the hot publish path and duplicates `Observe<T>().Where(...)`. Two ways to do one thing. **Document the reactive-operator path instead.** |
| Buffered/replay events (`IBufferedPublisher<T>`) | MessagePipe | Duplicates `ReactiveProperty<T>` (BehaviorSubject equivalent) + `StartWith`; a stored-message field per channel raises memory/staleness questions and fragments the model. "Current state new listeners need" = `ReactiveProperty`; "transient notification" = message. |
| Handler ordering / priority on the core bus | MessagePipe | Priority forces either a per-publish sort (alloc + cost, breaks the gate) or an ordered-insert that complicates the swap-back removal that keeps publish fast and re-entrancy-safe. Model ordered phases as separate sequential message types (`PreDamage -> Damage -> PostDamage`). Revisit only via EventHub if a real sample blocks. |
| Request-response (`IRequestHandler<TReq,TRes>`) | MessagePipe | 1:1 ask-with-one-owner is a DI service call (resolve an interface, call a method, optionally return `Task<T>`). Putting it on the broker blurs the pub/sub boundary and tempts routing ordinary calls through events. |
| Global static facade (`GlobalMessagePipe`) | MessagePipe | Breaks scope isolation (each context owns its broker - a deliberate strength), risks cross-scene message leaks, and contradicts the no-public-static-mutable-state rule. The injected `OnityEventHub` already gives manager-style ergonomics. |

---

## 7. References

- DI sources: `Assets/Onity-Packages/Onity/Runtime/DI/Scripts/` (`OnityContainer.cs`, `TypeBindingBuilder.cs`, `MultiTypeBindingBuilder.cs`, `Internal/ActivatorCompiler.cs`)
- Reactive sources: `Assets/Onity-Packages/Onity/Runtime/Reactive/Scripts/` (`Subject.cs`, `ReactiveProperty.cs`, `OnityObservable*.cs`, `OnityObservableAsyncExtensions.cs`)
- Messaging sources: `Assets/Onity-Packages/Onity/Runtime/Messaging/Scripts/` (`MessageBroker.cs`, `MessageChannel.cs`) and `Runtime/Unity/Scripts/Messaging/` (`OnityEventHub.cs`, `OnityMessageReactiveExtensions.cs`, `OnityMessageBindingExtensions.cs`)
- Unity wiring: `Assets/Onity-Packages/Onity/Runtime/Unity/Scripts/Contexts/OnityContext.cs`, `Installers/MonoInstaller.cs`
- Parity tests (divergence source of truth): `Assets/Onity-Packages/Onity/Tests/EditMode/Scripts/OnityZenject*ParityTests.cs`, `OnityVContainerParityTests.cs`, `OnityBakedContainerTests.cs`
- Benchmarks: `Packages/com.onity.framework/Benchmarks/Editor/Scripts/OnityDiBenchmarkRunner.cs`, results in `Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md`
- Plan docs: `docs/Plan/00-Overview.md` .. `06-Agent-Playbook.md`, `04-Performance-Targets.md`, `05-Implementation-Phases.md`
- Engineering rules: `Assets/Onity-Packages/Onity/ENGINEERING.md`
- Event hub plan: `EVENT_HUB_PLAN.md`
- Style: `codex-code-style.md`
