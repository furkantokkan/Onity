# 05 - Implementation Phases

Phase order is hard. Do not skip ahead. Each phase has an explicit exit gate.

## Deferrals and pivots

Decisions made during execution that diverge from the original phase plan
are recorded here. Update this section every time the plan flexes.

### 2026-05-24 - Phase 0.1 (Onity.Async extraction) deferred

The plan originally called for `Onity.Async` to be extracted before any
Phase 1 work. Investigation revealed a circular dependency: `OnityAsync`
calls `OnityUnityObservable.EveryUpdate()` which lives in `Onity.Unity`,
while `Onity.Unity` would need to reference `Onity.Async` for the
ergonomic facade. Resolving this cleanly requires moving the PlayerLoop
runner into `Onity.Async` (a larger refactor than Phase 0.1 scoped).

**Decision:** Defer all Phase 0 extraction tasks. Start with Phase 1.1
(DI compiled activator), which is internal to `Onity.DI` and directly
addresses the user's top-priority performance gap. Module extraction
returns after Phase 1 ships measurable DI wins.

### 2026-05-29 - Phase 1 hot-path follow-through landed (no full baked graph)

Implemented the surgical, parity-preserving subset of the Phase 1.3/1.4 design
instead of the full `BakedGraph` / `TypeId` slot-array rewrite (deferred as still
out of scope). Changes are internal only; the public API is unchanged.

- **Static activator cache** (`ActivatorCompiler`): `Expression.Compile` now runs
  once per `ConstructorInfo` per process via a `ConcurrentDictionary`, instead of
  once per container `Build`. Targets the earlier `Prepare & Register Complex`
  regression introduced by Phase 1.1.
- **Lock-free `ArgumentArrayPool`** (`OnityContainer`): replaced the global locked
  `Dictionary<int,Stack<>>` with a `[ThreadStatic]` per-length free-list. Removes
  the lock from every `CreateInstance` / inject-method frame; re-entrant-correct
  (each nested rent pops a distinct buffer) and zero-alloc in steady state.
- **Per-plan constructor-dependency cache** (`OnityContainer`): caches each ctor
  dependency's resolution (self-resolve / same-scope provider + binding-version /
  deferred) so the `Resolve Complex` graph skips the per-dependency dictionary
  lookup and self-resolve compares. Parent/implicit/abstract deps still defer to
  `Resolve` for identical behavior; a `m_bindingVersion` guard re-resolves on rebind.
  Deferred slots early-out without re-allocating, preserving zero-alloc on
  child-container resolves.
- **Generic `Resolve<T>()` / `TryResolve<T>()`**: skip the `Resolve(Type)` box/cast
  round trip by calling `TryResolveInternal` directly.
- **Guard-test fix** (`OnityContainerTests.OnitySources_DoNotUseSystemLinq`): now
  bans `System.Linq` / `System.Linq.*` (LINQ-to-objects; plain loops are the
  project default) while allowing `System.Linq.Expressions`, which the compiled
  activator needs. The prior `Contains("System.Linq")` check false-failed on Phase 1.1.

Competitor parity test suites ported onto `OnityContainer` (public API only):

- `OnityZenjectParityTests.cs` - 25 tests (Zenject `OptionalExtras/UnitTests`).
- `OnityVContainerParityTests.cs` - 22 tests (VContainer behavior suite).
- `OnityBakedContainerTests.cs` - 9 tests (optimization-specific: lifetime identity,
  inject ordering, ctor selection, value-type/generic args, deep graph, re-entrancy).

Documented divergences (Onity throws on member-injection cycles; resolve-time vs
build-time circular detection; most-params ctor selection; scope == child container)
are asserted against Onity's real behavior with inline comments, not faked parity.

**Verification status:** `dotnet build Onity.DI.csproj` clean (0 errors).
The 2026-05-30 follow-up Unity benchmark confirmed that baked resolve now beats
VContainer in every measured timing scenario, including
`Prepare & Register Complex` (~61,044 ns vs VContainer ~150,730 ns). The
internal 15,000 ns build gate and allocation measurement remain
open release-hardening work.

### 2026-05-29 - In progress: parallel increments started this session

Several lanes were started in parallel on top of the Phase 1 hot-path work
above. All are internal/additive and preserve the public API; the 147 EditMode
tests are green.

- **Compiled member-setters (Phase 1.2):** extend the `Expression.Compile`
  approach from constructor activators to field/property setters and `[Inject]`
  method invocations so `InjectMembers` runs compiled delegates instead of
  reflection. (`Onity.DI` internal.)
- **Reactive exception-isolation:** harden the `Subscribe` path so a throwing
  observer does not tear down the rest of a multicast fan-out. (`Onity.Reactive`.)
- **Keyed messaging:** explore a per-key channel lookup on top of the existing
  Type-keyed broker (currently messaging is Type-keyed only; this is additive
  and stays behind the existing `IMessageBroker` surface). (`Onity.Messaging`.)
- **Analyzer scaffold:** stand up a Roslyn analyzer project to enforce the
  package rules (no `System.Linq`, `m_`/`s_`/`k_` naming, mandatory `AddTo`
  on subscriptions) at compile time.

Docs reconciled alongside these increments: `ENGINEERING.md` sections 7-8 now
mirror `docs/Plan/03-Reactive-Design.md` (`ThrottleLast` not `Throttle`; no
unshipped `Never`/`Create`/`Defer` or `AddTo(GameObject)`; corrected
`MessageChannel<T>` / Type-keyed-only claims), and
`docs/Onity-AI-Usage-Guide.md` gained an "Error -> fix" table (the shipped
`OnityResolveException` / `OnityBindingException` plus the standard .NET
exceptions Reactive/Messaging throw) and an "Events decision rule" block
(message vs `ReactiveProperty<T>` vs direct service call).

**Note:** `OnityReactiveException` now **ships** (added with the reactive
exception-isolation increment, alongside the settable `OnityObservableExceptionHandler`
hook that `Subject<T>.OnNext` routes a throwing subscriber's exception to).
`OnityMessagingException` is **not yet shipped** — the messaging increment added keyed
channels only; messaging code still throws standard .NET exceptions today (error-standard
roadmap, `07-Competitive-And-AI-Roadmap.md`). Docs reflect the shipped reality.

### 2026-05-30 - Historical full EditMode suite pass in Unity 6.4

Ran the entire `Onity.DI.Tests` EditMode suite in the Onity Example Game (Unity
6000.4.7f1) via the Test Runner: **203 tests, 203 passed, 0 failed.** This covers
the 147 ported parity tests (Zenject 6-category + VContainer + baked) plus every
increment shipped this cycle: compiled member-setters, BakedGraph parity (15),
reactive operators + exception-isolation + Throttle/Buffer + ObserveOn, and keyed +
async messaging. Two `OnityAsyncMessagingTests` cancellation cases initially failed
because awaiting a CT-canceled async `ValueTask` surfaces `TaskCanceledException`
(a subclass of `OperationCanceledException`); fixed by asserting with
`Assert.CatchAsync<OperationCanceledException>` (production code, which throws via
`ThrowIfCancellationRequested`, was correct and unchanged).

All engine-free projects (`Onity.Core/Factory/DI/Reactive/Messaging`) and
`Onity.Analyzers` compile clean via `dotnet build` (0 errors).

This was the full suite count at that point in the development branch. The suite
has grown since then; current docs should reference the live test runner/CI
result instead of freezing this count as the current total.

**`UseBakedResolve` stays `false` (shipping default) by design.** `OnityBakedGraphParityTests`
asserts the default is `false` (it is opt-in until validated). Flipping it to the
default is the final perf-activation step, gated on: (1) updating that guard assertion,
(2) a full EditMode run with the flag forced `true` (confirm the whole suite passes on
the baked path, not just the 15 parity scenarios), and (3) `OnityDiBenchmarkRunner` (in the Onity
repo, which references VContainer/Zenject) confirming the baked path meets the Resolve
Complex <= 35,000 / Combined <= 1,550 gates. Until then the proven reflection/compiled
path remains the default.

### 2026-05-30 - P0-1 (IL2CPP/AOT safety): reflection fallback now real

`08-Surpass-VContainer.md` track P0-1, first half. `ActivatorCompiler` and
`MemberSetterCompiler` claimed in their XML docs to fall back to reflection "on some
AOT platforms" but did not - both called `Expression.Compile()` directly. On IL2CPP
(no JIT) that throws or yields an invoke-time-throwing delegate, so the container would
have **crashed at runtime** instead of degrading. Since Onity's entire speed lead comes
from these compiled activators, this was the real "not production-ready on IL2CPP" gap.

- **`RuntimeCompileSupport`** (new, `Onity.DI.Internal`): probes once per process whether
  `Expression.Compile` both compiles AND invokes a representative ctor-shaped lambda
  (the invoke matters - some AOT runtimes let `Compile()` succeed but throw on first
  call). On JIT it returns true (compiled fast path); on AOT/IL2CPP it returns false
  (reflection). Engine-free; uses only `System` + `System.Linq.Expressions`.
- **Both compilers** now gate on the probe and additionally wrap `Compile()` in
  try/catch, falling back to a reflection delegate (`ctor.Invoke` / `field.SetValue` /
  `property.SetValue` / `method.Invoke`) - per-runtime AND per-member safety. Reflection
  receives exactly-sized argument arrays from `ArgumentArrayPool`, so no length juggling.
  The XML docs are now accurate.
- **`OnityContainer.ForceReflectionActivation`** (internal, mirrors `UseBakedResolve`):
  forces the reflection path on a JIT runtime so a graph can be pre-flighted in the
  Editor under the exact strategy IL2CPP uses.
- **`OnityAotFallbackTests`** (new, 5 tests): reflects into the flag (like the baked
  parity suite), forces reflection, and asserts transient/singleton activation,
  field/property/method injection, and value-type box/unbox all stay correct.

**Verification:** the three compiler sources were compiled unmodified in a throwaway
net8 console and exercised on both paths - 8/8 checks pass (probe true on JIT; compiled
ctor; force-reflection disables compile; reflection ctor; value-type box/unbox;
field/property/method setters). The then-current expanded EditMode suite still
needed a Unity re-run at this point.

**Remaining for P0-1:** (1) build an IL2CPP player that runs a resolve smoke + benchmark
on device to confirm the probe trips and the reflection path performs acceptably;
(2) optional `Onity.SourceGen` - compile-time activators so IL2CPP keeps the *speed*
lead (reflection only guarantees it *runs*, matching VContainer's no-codegen mode).

### 2026-05-30 - P1-3 (entry-point lifecycle): automatic like Zenject

`08-Surpass-VContainer.md` track P1-3. Direct DX answer to "VContainer makes you wire
everything by hand; Zenject is automatic." A bound singleton/instance that implements a
lifecycle interface is now wired by the container itself - no register-entry-point call.

- **Interfaces** (`Onity.DI`, engine-free, `OnityLifecycle.cs`): `IOnityInitializable`
  (`Initialize()`), `IOnityTickable` (`Tick()`), `IOnityFixedTickable` (`FixedTick()`),
  `IOnityLateTickable` (`LateTick()`). `IDisposable` singletons are already auto-disposed
  by their provider on container Dispose, so no disposable interface is added.
- **`OnityContainer.Build()`** scans `m_ownedProviders` once (distinct providers - a
  multi-contract `BindInterfacesAndSelfTo` instance is collected once, not per contract),
  eagerly creates only singleton/instance bindings implementing a lifecycle interface
  (others stay lazy), runs `Initialize()` in registration order, and exposes
  `Tick()/FixedTick()/LateTick()`. Lazily-allocated lists keep lifecycle-free containers
  allocation-free here. Transients are skipped (no single stable instance to tick).
- **`OnityContext`** (Unity layer) pumps `Update->Tick`, `FixedUpdate->FixedTick`,
  `LateUpdate->LateTick`, so ticking is fully automatic in play mode.
- **`OnityLifecycleTests`** (9): auto-init-at-Build-once, not-before-Build, init order,
  tick/fixed/late dispatch, both-from-one-binding, transient-not-collected,
  multi-contract-ticks-once, disposable-disposed-on-Dispose.

**Verification:** `Onity.Core`+`Onity.Factory`+`Onity.DI` compiled headlessly (net8) -
0 warnings, 0 errors. The then-current expanded EditMode suite still needed a
Unity re-run; the `OnityContext` pump needed a PlayMode check.

### 2026-05-30 - P1-1 (collection injection): register many, inject all

`08-Surpass-VContainer.md` track P1-1. Another "VContainer makes you do it by hand" gap:
bind a contract multiple times and inject every registration. All additive; the single-
resolve hot path is untouched.

- **`m_multiProviderMap`** (`Dictionary<Type, List<IProvider>>`, lazily allocated):
  accumulates every explicit provider per contract, populated at the two explicit-binding
  write sites. `m_providerMap` still keeps only the last (single-resolve last-wins is
  preserved). Implicit auto-resolved concretes are excluded.
- **`TryResolveInternal`** gains one branch after the `m_providerMap` miss (so it never
  burdens a normal single resolve, which returns earlier): if the requested type is
  `IEnumerable<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>` / `IList<T>` /
  `ICollection<T>` / `List<T>` / `T[]` AND element `T` has >= 1 explicit binding here or
  in an ancestor, it synthesizes the collection. Items are gathered ancestors-first, each
  resolved in its owning container; materialized as a typed `T[]` (satisfies every
  interface form) or copied into a real `List<T>` when that exact type was requested. An
  element type with no bindings stays unresolvable (no silent empty - safer than the MS.DI
  convention; can relax later). `CanResolve` updated to match.
- An explicit binding of the collection type itself still wins (matched by `m_providerMap`
  before synthesis). Works under the baked path (collections are not baked, so they fall
  through to synthesis).
- **`OnityCollectionInjectionTests`** (11): enumerable/array/readonlylist/list resolves,
  registration order, single last-wins, ctor `IEnumerable`/`IReadOnlyList` injection,
  singleton-shared/transient-fresh element lifetime, child-aggregates-ancestors-first,
  multi-contract-contributes-once, unbound-element-unresolvable, CanResolve parity.

**Verification:** compiled Core+Factory+DI headlessly AND ran an 11-assertion harness
through the real `OnityContainer` on net8 - all pass (enumerable/array/readonlylist/list,
order, last-wins, ctor injection, CanResolve, hierarchy 3+1=4 ancestors-first, unbound
throws). The then-current expanded EditMode suite still needed a Unity re-run.

### 2026-05-30 - P1-2 (open generic registration): last DI feature gap closed

`08-Surpass-VContainer.md` track P1-2. `Bind(typeof(IRepository<>)).To(typeof(Repository<>))`
makes a later resolve of a closed `IRepository<Foo>` construct `Repository<Foo>` on demand.
This was the last DI-feature axis where VContainer was ahead.

- **`RuntimeTypeBindingBuilder`** (new): non-generic `Bind(Type)` fluent path (open generics
  cannot use the typed `Bind<T>()`), also handles closed runtime-typed binds (useful for
  reflection/AI-driven registration). `To(Type)` / `AsSingle` / `AsTransient` / `NonLazy`
  (NonLazy rejected for open generics - no closed type yet).
- **`OnityContainer.Bind(Type)`** + `RegisterRuntime` (routes open vs closed) +
  `RegisterOpenGeneric` (validates: both open defs, concrete impl, equal arity, impl
  implements/derives the contract - fail-fast at bind, not at resolve). Open registrations
  live in a lazily-allocated `m_openGenericMap` keyed by open contract definition.
- **Resolve**: `TryResolveInternal` gains an open-generic branch after the collection check
  and before the parent walk (so a child open binding overrides a parent's, like closed
  binds); ancestor open bindings are reached through the recursive parent walk. On first
  resolve of a closed contract, `MakeGenericType` builds the closed impl, a normal provider
  is registered for it (so later resolves hit the fast path and the closed singleton's
  lifetime is owned by the registering scope), and it resolves. `CanResolve` updated to
  match.
- **AOT caveat:** runtime `MakeGenericType` requires the closed type to survive IL2CPP
  stripping (reference it statically or preserve it). Fully works in Editor/Mono; the
  eventual `Onity.SourceGen` (P0-1) would remove the caveat.
- **`OnityOpenGenericTests`** (11): closed-resolve-with-dependency, singleton-per-closed-type,
  distinct-closed-types, transient-distinct, ctor-injection-of-closed-contract, CanResolve,
  child-resolves-ancestor, non-generic-closed-bind, mismatch-throws, non-concrete-throws.

**Verification:** compiled Core+Factory+DI headlessly (full DI assembly, all P0-1/P1-1/P1-2/
P1-3 changes) AND ran an 11-assertion harness through the real `OnityContainer` on net8 -
all pass. The then-current expanded EditMode suite still needed a Unity re-run.

**DI feature parity with VContainer is now complete:** collection injection (P1-1),
open generics (P1-2), and automatic entry-point lifecycle (P1-3, where Onity is *ahead* -
no manual registration). Remaining VContainer edges are non-feature: IL2CPP-proven-on-device
(P0-1 device run + optional source-gen) and platform/docs polish (P2/P3).

### 2026-05-30 - IL2CPP validated on device + parallel P0-2/P2/P3/SourceGen lanes

**IL2CPP smoke passed all checks on an actual IL2CPP build** (user-run): compiled/reflection
activation, member injection, collection, open-generic, and lifecycle all green. P0-1
correctness is now *validated on device*, not just in theory - the last hard "not
production-ready on IL2CPP" blocker is gone. Source-gen is now a speed optimization, not a
correctness requirement.

Then a 5-lane parallel workflow landed the remaining surpass-VContainer work (file-disjoint;
SourceGen headless-verified, the rest write-only pending a Unity run):
- **CI (P2):** `.github/workflows/onity-ci.yml` - GameCI, matrix `testMode: [editmode, playmode]`,
  Unity 2022.3.62f3, on push/PR to main+master. Needs 3 repo secrets (UNITY_LICENSE/EMAIL/PASSWORD).
- **PlayMode tests (P2):** `Tests/PlayMode/` (asmdef mirroring EditMode + Onity.Unity) -
  `OnityContextLifecyclePlayModeTests` (proves the OnityContext Update->Tick pump drives ticks)
  and `OnityResolveSoakPlayModeTests` (120-frame resolve + subscribe/dispose churn).
- **Benchmark (P0-2):** `OnityDiBenchmarkRunner` now measures Onity twice per scenario -
  `Onity (Reflection)` vs `Onity (Baked)` - reflecting `UseBakedResolve`, restored in finally.
  Also added `container.Build()` to the Onity registration helpers so the baked pass is real and
  the build scenario fairly includes `Build()` (the prior Onity build number omitted it). Re-run
  in Unity (menu Onity > Benchmarks) for corrected, baked-vs-reflection numbers.
- **SourceGen scaffold (P0-1 follow-on):** `tools/Onity.SourceGen/` - an `IIncrementalGenerator`
  emitting AOT-safe `new T(...)` activators (no Expression/reflection) for `[OnityGenerateActivator]`
  types, registered via `[ModuleInitializer]` into a future `Onity.DI.Internal.GeneratedActivators`
  hook. **`dotnet build` clean (0/0) + an end-to-end generator smoke passed.** Runtime wiring
  (the hook + ActivatorCompiler consulting it first) is the explicit next step; member-setter
  generation + attribute-free discovery are future work.
- **Release docs (P3):** README refreshed (new features, comparison table, indicative-
  benchmark caveat, CI badge, lifecycle quickstart), new `docs/Onity-vs-VContainer-Zenject.md`,
  `package.json` 0.1.0 -> 0.2.0.

Follow-ups surfaced: reconcile `docs/Plan/07` (still lists collection/open-generic/analyzer as
Adopt/Non-goal - now shipped); wire the SourceGen runtime hook when IL2CPP speed warrants it.

## Phase 0 - Module reorganization

**Goal:** Move existing code into the target asmdef layout from
`01-Architecture.md`. No behavior changes.

### 0.1 Extract `Onity.Async`

- Create folder `Assets/Onity-Packages/Onity/Runtime/Async/`.
- Create `Onity.Async.asmdef` with `references: [ Onity.Core, Onity.Reactive ]`,
  `noEngineReferences: true`.
- Move from `Runtime/Unity/Scripts/Async/`:
  - `OnityAsync.cs` -> rename to `Onity.cs` (static class becomes `Onity`)
  - `OnityTaskTracker.cs`
  - `OnityTimeoutController.cs`
  - `OnityCancellationTokenSourceExtensions.cs`
  - `OnityTaskExtensions.cs`
- Update namespaces from `Onity.Unity.Async` to `Onity.Async`.
- Leave `OnitySceneLoader.cs` and `OnityAsyncOperationExtensions.cs` in
  `Onity.Unity` because they touch `UnityEngine.AsyncOperation` /
  `SceneManager`.

### 0.2 Extract `Onity.Input`

- New folder `Assets/Onity-Packages/Onity.Input/Runtime/`.
- Create `Onity.Input.asmdef` with `references: [ Onity.Core, Onity.Reactive,
  Unity.InputSystem ]`.
- Move from `Onity.Unity/Scripts/Input/`:
  - `OnityInputActionKey.cs`
  - `OnityReactiveInputPlayer.cs`
  - `OnityInputActionReactiveExtensions.cs`

### 0.3 Extract `Onity.UI`

- New folder `Assets/Onity-Packages/Onity.UI/Runtime/`.
- Create `Onity.UI.asmdef` with `references: [ Onity.Core, Onity.DI,
  Onity.Unity ]`.
- Move from `Onity.Unity/Scripts/UI/`:
  - `OnityUiPresenter.cs`
  - `OnityUiServiceLocator.cs`
  - `OnityUiResolverBridge.cs`
  - `OnityUiInjectAttribute.cs`
  - `OnityUiPresenterFactory.cs`

### 0.4 Extract `Onity.SceneFlow`

- New folder `Assets/Onity-Packages/Onity.SceneFlow/Runtime/`.
- Create `Onity.SceneFlow.asmdef` with `references: [ Onity.Core, Onity.Async,
  Onity.Reactive ]`.
- Move from `Onity.Unity/Scripts/SceneFlow/`:
  - `OnitySceneFlow.cs`
  - `OnitySceneFlowProfile.cs`
  - `OnitySceneFlowStateMachine.cs`
  - `OnitySceneFlowStateId.cs`
  - `OnitySceneFlowTransitionPlan.cs`
  - `OnitySceneTransitionStore.cs`
  - `OnitySceneInitiator.cs`
  - `IOnitySceneEnterData.cs`
- Move `OnitySceneLoader.cs` here too (depends on AsyncOperation but the
  context is scene transitions).

### 0.5 Test split

- Replace `Onity.Tests.EditMode` umbrella with per-module test asmdefs:
  - `Onity.Tests.Core.EditMode`
  - `Onity.Tests.DI.EditMode`
  - `Onity.Tests.Reactive.EditMode`
  - `Onity.Tests.Async.EditMode`
  - `Onity.Tests.Messaging.EditMode`
- Existing test files move to the matching asmdef.

### 0.6 Phase 0 exit gate

- All asmdefs compile via Unity Editor.
- `dotnet build` succeeds for every `Onity.*.csproj` after Unity regenerates
  them.
- Existing benchmark and sample scenes still work.
- No public API changes.
- `01-Architecture.md` "Module table" reflects reality.

## Phase 1 - DI hot path

**Goal:** Beat VContainer on Resolve Transient, Combined, and Complex per
the gates in `04-Performance-Targets.md`. No public API changes.

### 1.1 Compiled activator

- Add `Onity.DI/Scripts/Internal/ActivatorCompiler.cs` with the
  `Expression.Compile()` implementation from `02-DI-Design.md` section 3.3.
- `BuildPlan(...)` in `OnityContainer.cs` calls `ActivatorCompiler.Compile`
  and stores the delegate on `TypeInjectionPlan`.
- `CreateInstance` calls `plan.Activator(args)` instead of
  `Constructor.Invoke(args)`.

### 1.2 Compiled member injection

- Compile field, property setters and method invocations into delegates the
  same way.
- `InjectMembers` uses the compiled delegates.

### 1.3 TypeId cache and baked graph

- Add `Onity.DI/Scripts/Internal/TypeIdRegistry.cs` and
  `TypeIdCache<T>`.
- Add `Onity.DI/Scripts/Internal/BakedGraph.cs` with the layout from
  `02-DI-Design.md` section 3.1.
- `OnityContainer.Build()` populates the baked graph from the registration
  dictionary.
- `Resolve<T>` uses the baked path. `Resolve(Type)` falls back to the
  dictionary path for diagnostics and reflection callers.

### 1.4 Singleton cache

- `BakedGraph.m_singletonCache` holds resolved singletons by provider slot.
- First resolve writes the slot; subsequent resolves return the cached
  reference.

### 1.5 Feature flag

- Add `internal static bool OnityContainer.UseBakedResolve = true` (settable
  for tests).
- All `Resolve<T>` calls check this flag and fall back to the reflection
  path when false.
- This flag is **internal** and removed in Phase 2 after CI is green for
  one release cycle.

### 1.6 New tests

- `Onity.Tests.DI.EditMode/OnityBakedContainerTests.cs` covers:
  - Transient resolve correctness vs reflection path
  - Singleton caching identity
  - `Inject` ordering (fields -> properties -> methods)
  - Multi-constructor selection
  - Generic dependency resolution
  - Array dependency resolution
  - Re-entrant resolve detection

### 1.7 Benchmark gate

- Run `OnityDiBenchmarkRunner` after each meaningful commit.
- Each commit message references the delta vs baseline.

### 1.8 Phase 1 exit gate

- All performance gates in `04-Performance-Targets.md` section 3.1 pass.
- All existing tests pass.
- IL2CPP build of the benchmark runner runs without throwing.
- `UseBakedResolve = false` path still produces identical results (parity
  guarantee before the reflection path is removed in Phase 2).

## Phase 2 - Reactive operator parity and tracker UI

**Goal:** Ship the operator catalog and the diagnostics window.

### 2.1 Operators

For each operator in `03-Reactive-Design.md` section 3.2, in this order:

1. `DistinctUntilChanged`
2. `Skip`, `SkipWhile`, `Take`, `TakeWhile`
3. `StartWith`
4. `Merge` (variadic + pooled subscriber array)
5. `CombineLatest` (2/3/4-arity)
6. `Zip` (2/3/4-arity)
7. `Scan`, `Aggregate`
8. `Sample`
9. `Buffer(int)` and `Buffer(TimeSpan)`
10. `Window(int)`
11. `Concat`, `Switch`
12. `Timeout`, `Catch`, `Retry`

Each operator gets:

- One implementation file under `Onity.Reactive/Scripts/Operators/`.
- One EditMode test file under `Onity.Tests.Reactive.EditMode/Operators/`.
- One benchmark scenario.

### 2.2 Reactive benchmark runner

- New file:
  `Packages/com.onity.framework/Benchmarks/Editor/Scripts/OnityReactiveBenchmarkRunner.cs`
- Scenarios from `04-Performance-Targets.md` section 2.2.
- Output files alongside DI results.

### 2.3 ObservableTracker UI

- The `OnityObservableTracker` core already records subscriptions.
- New editor window `Assets/Onity-Packages/Onity/Editor/Scripts/
  Diagnostics/ObservableTrackerWindow.cs`.
- Menu item: `Onity/Diagnostics/Observable Tracker`.
- Columns: source type, subscriber stack, age, completion state.
- Sortable, filterable, refresh button.

### 2.4 Remove reflection-path fallback

- Drop the `UseBakedResolve` flag from Phase 1 once Phase 2 starts so the
  codebase carries only the baked path.

### 2.5 Phase 2 exit gate

- All operators implemented and tested.
- All reactive performance gates pass.
- Observable Tracker window opens, lists live subscriptions for a leak-test
  sample scene, and updates as subscriptions dispose.

## Phase 3 - Messaging EventHub expansion

**Goal:** Execute `EVENT_HUB_PLAN.md`.

### 3.1 Tasks

Follow the five phases in `EVENT_HUB_PLAN.md`:

1. API phase - event contracts and facade
2. Runtime phase - keyed registry, pooled subscriber arrays
3. Binding phase - `Bind` / `Unbind` plus `[OnEvent]` attribute
4. Integration phase - register `EventHub` in Onity contexts
5. Validation phase - tests + microbenchmarks

### 3.2 Phase 3 exit gate

- All acceptance criteria from `EVENT_HUB_PLAN.md` pass.
- A new sample scene `04-EventHubChat` demonstrates emitter, channel, and
  attribute-driven binding.

## Phase 4 - DOTS bridge widening

**Goal:** Generalize from the current int-event-only bridge to typed events
and an ECS-source observable.

### 4.1 Generic typed event bridge

- New file: `Onity.DOTS/Scripts/OnityDotsEventBridge.cs`
- Generic over `T : unmanaged`.
- Same queue / accumulator pattern as `OnityDotsIntEventQueue` but typed.
- Singleton entity per event type, created on first `Publish<T>`.

### 4.2 ECS -> Observable

- New API: `system.AsObservable<T>()` where `T : unmanaged` and the system
  produces a stream of events.
- Implementation drains the DOTS event queue on the main thread at the end
  of the simulation group and pumps a managed `Subject<T>`.

### 4.3 Burst-safe threading for reactive `EveryUpdate`

- Implement `OnityUnityThreadMode.JobMultiThread` and
  `BurstJobMultiThread` in `OnityUnityObservable.EveryUpdate` and friends.
- Validate against Phase 2 reactive benchmark gates.

### 4.4 Phase 4 exit gate

- Sample `06-DOTS-Survivors-Mini` runs 5000 entities, uses SkillStats and
  the typed event bridge, holds frame time on the baseline machine.

## Phase 5 - Asset Store and UPM packaging

**Goal:** Make Onity installable as a UPM package and shippable on Asset
Store.

### 5.1 Tasks

- Add `package.json` to each `Assets/Onity-Packages/Onity*/` folder.
- Pin a version (`0.1.0` for the first cut).
- Add per-package `README.md`, `CHANGELOG.md`, `LICENSE.md`.
- Keep the distributable package sample-free; host examples outside the package.
- Build an Asset Store metadata bundle.

### 5.2 Migration guide

- `docs/Migration/From-Zenject.md`
- `docs/Migration/From-VContainer.md`
- `docs/Migration/From-R3.md`
- `docs/Migration/From-UniTask.md`

Each guide is a flat table: their API -> Onity API. No prose beyond a one-
paragraph intro.

### 5.3 Phase 5 exit gate

- A fresh Unity project can install `com.onity.framework` via Git URL and
  compile a small scene using DI, messaging, and reactive state without sample imports.
- The Asset Store package validates against Unity's submission tool.

## Phase deferral rules

If a phase runs long, the items that slip are:

| Phase | First to defer |
|---|---|
| 1 | Generic and array dependency resolution tests (still ship, but in 1.1 patch) |
| 2 | Buffer(TimeSpan), Window, Switch (move to 2.1 patch) |
| 3 | None - EventHub plan is small |
| 4 | BurstJobMultiThread for reactive (move to 4.1 patch) |
| 5 | Asset Store - keep UPM as the v1 channel if Asset Store metadata blocks |

Do **not** defer:

- Performance gates - if Phase 1 cannot hit them, do not ship Phase 1.
- Zero allocation gates - same.
- Test coverage for the headline change in the phase.
