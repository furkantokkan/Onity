---
title: "DI API"
parent: "Reference"
nav_order: 1
---

# DI API Reference

A complete catalog of the `OnityContainer` binding and resolve surface. `OnityContainer` is a sealed, engine-free (`Onity.DI`, no `UnityEngine`), parent-scoped container that implements `IResolver` and `IDisposable`. You can use it directly in EditMode tests with no Unity scene:

```csharp
using Onity.DI;
using OnityContainer container = new OnityContainer();
container.Bind<IClock>().To<SystemClock>().AsSingle();
container.Build();
IClock clock = container.Resolve<IClock>();
```

A lifetime call (`AsSingle()` / `AsTransient()`) is **required** to actually register a binding; `Bind<T>()` / `To<T>()` alone register nothing. The lifetime enum is exactly `{ Singleton, Transient }` — there is no `Scoped` keyword; a per-scope instance is a child-container `AsSingle` (see [Lifecycle & Scopes](../guide/lifecycle-and-scopes.html)).

The resolve machinery is designed to avoid per-call managed allocation (compiled constructor activators on JIT runtimes, pooled argument arrays, cached injection plans, an optional dense-id baked graph). A transient resolve still allocates the instance it returns. On IL2CPP/AOT the container falls back to reflection-based activation and constructs correctly either way — read `OnityContainer.IsCompiledActivationSupported` to see which path is live.

> Onity has no third-party runtime dependencies; the DI core does not use `System.Linq`.

---

## Binding entry points

Each entry point returns a fluent builder (except `BindInstance` and `BindFactory`, which register immediately). All are instance methods on `OnityContainer`.

| API | Signature | Notes |
| --- | --- | --- |
| `Bind<TContract>` | `Bind<TContract>() -> TypeBindingBuilder<TContract>` | Start a single-contract binding. Chain `.To<TConcrete>()` then a lifetime. |
| `Bind` (runtime type) | `Bind(Type contractType) -> RuntimeTypeBindingBuilder` | Bind from a runtime `Type`. Supports **open generic** definitions, e.g. `Bind(typeof(IRepo<>)).To(typeof(Repo<>)).AsSingle()`. Null contract throws `OnityBindingException`. |
| `BindInterfacesAndSelfTo<TConcrete>` | `BindInterfacesAndSelfTo<TConcrete>() -> MultiTypeBindingBuilder` where `TConcrete : class` | Share **one** instance across the concrete type **and** all interfaces it implements. |
| `BindInterfacesTo<TConcrete>` | `BindInterfacesTo<TConcrete>() -> MultiTypeBindingBuilder` where `TConcrete : class` | Share one instance across the implemented interfaces only. Throws `OnityBindingException` if the type implements no interfaces. |
| `BindInstance<TContract>` | `BindInstance<TContract>(TContract instance) -> void` | Register a pre-built instance. Rejects null with `OnityBindingException`. |
| `BindFactory<TValue, TFactory>` | `BindFactory<TValue, TFactory>() -> void` where `TFactory : class, IFactory<TValue>` | Bind a factory `AsSingle` via `BindInterfacesAndSelfTo`. You author the `IFactory<TValue>` impl. |
| `BindFactory<TParam, TValue, TFactory>` | `BindFactory<TParam, TValue, TFactory>() -> void` where `TFactory : class, IFactory<TParam, TValue>` | One-parameter factory variant. |
| `BindFactory<TParam1, TParam2, TValue, TFactory>` | `BindFactory<TParam1, TParam2, TValue, TFactory>() -> void` where `TFactory : class, IFactory<TParam1, TParam2, TValue>` | Two-parameter factory variant. |

### `TypeBindingBuilder<TContract>`

Returned by `Bind<TContract>()`.

| API | Signature | Notes |
| --- | --- | --- |
| `To<TConcrete>` | `To<TConcrete>() -> TypeBindingBuilder<TContract>` where `TConcrete : TContract` | Set the implementation. Optional — defaults to `TContract` (self-bind). |
| `AsSingle` | `AsSingle() -> TypeBindingBuilder<TContract>` | Register as a shared singleton. |
| `AsTransient` | `AsTransient() -> TypeBindingBuilder<TContract>` | Register as a new instance per resolve. |
| `NonLazy` | `NonLazy() -> TypeBindingBuilder<TContract>` | Resolve eagerly at `Build()`. Throws `OnityBindingException` if called before `AsSingle()`/`AsTransient()`. |

### `MultiTypeBindingBuilder`

Returned by `BindInterfacesAndSelfTo<T>()` / `BindInterfacesTo<T>()`.

| API | Signature | Notes |
| --- | --- | --- |
| `AsSingle` | `AsSingle() -> MultiTypeBindingBuilder` | One shared instance across all collected contracts. |
| `AsTransient` | `AsTransient() -> MultiTypeBindingBuilder` | New instance per resolve. |
| `NonLazy` | `NonLazy() -> MultiTypeBindingBuilder` | Resolve the implementation eagerly at `Build()`. Throws if no lifetime was set first. |

### `RuntimeTypeBindingBuilder`

Returned by `Bind(Type)`. Handles both closed runtime-typed bindings and open generics.

| API | Signature | Notes |
| --- | --- | --- |
| `To` | `To(Type implementationType) -> RuntimeTypeBindingBuilder` | Set the implementation. For an open generic contract the implementation must be an open generic definition with the same type-parameter count. Null throws `OnityBindingException`. |
| `AsSingle` | `AsSingle() -> RuntimeTypeBindingBuilder` | Singleton. For an open generic, each distinct closed contract gets its own singleton. |
| `AsTransient` | `AsTransient() -> RuntimeTypeBindingBuilder` | Transient. |
| `NonLazy` | `NonLazy() -> RuntimeTypeBindingBuilder` | Eager at `Build()`. **Not supported for open generic bindings** (the closed type is unknown until resolve) — throws `OnityBindingException`. |

---

## Resolve and inject

Defined on `IResolver` (and therefore on `OnityContainer`). The container always self-resolves `OnityContainer` and `IResolver` to the active scope, so a factory can inject `IResolver` to do manual resolves.

| API | Signature | Notes |
| --- | --- | --- |
| `Resolve<TService>` | `Resolve<TService>() -> TService` | Resolve by generic type. Throws `OnityResolveException` if unresolvable. |
| `Resolve` | `Resolve(Type serviceType) -> object` | Runtime-type overload. Null type throws `OnityResolveException`. |
| `TryResolve<TService>` | `TryResolve<TService>(out TService instance) -> bool` | Returns false instead of throwing when unresolvable. |
| `TryResolve` | `TryResolve(Type serviceType, out object instance) -> bool` | Runtime-type overload. Null type returns false. |
| `Inject` | `Inject(object target) -> void` | Member-injects an already-constructed object (fields, properties, methods). Null target throws `OnityResolveException`. |
| `CanResolve` | `CanResolve(Type serviceType) -> bool` | Reports resolvability without constructing. Accounts for explicit bindings, parent scope, collection element bindings, open generic registrations, and concrete auto-resolve. |

### Resolution rules

| Case | Behavior |
| --- | --- |
| Unbound **concrete** class | Auto-resolves as an implicit transient. Not shared state — each resolve is a new instance. |
| Unbound **interface / abstract / open-generic definition** | Throws `OnityResolveException`. Bind it. |
| Re-binding the same contract | Last binding wins — the previous binding is replaced (no duplicate-binding error). |
| Two separate `Bind<IFoo>().To<C>()` + `Bind<IBar>().To<C>()` | Produces **distinct** singletons. Use `BindInterfacesAndSelfTo<C>().AsSingle()` to share one instance. |
| Constructor selection | A single `[Inject]` constructor wins; otherwise the greediest **public** constructor (most parameters). A second `[Inject]` constructor throws `OnityBindingException`. |
| Circular dependency | Constructor **and** member cycles throw `OnityResolveException` at **resolve time** (not build time), with the full resolution chain. Break with a factory or `IResolver`. |
| Closed generics | A closed contract (`Repo<int>`) resolves if bound explicitly or via an open generic registration. |
| Collection contracts | `IEnumerable<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>` / `IList<T>` / `ICollection<T>` / `List<T>` / `T[]` are synthesized from **every explicit binding** of element type `T` across this scope and its ancestors (ancestors first). An explicit binding of the collection type itself wins over synthesis. |

---

## Build and lifecycle

| API | Signature | Notes |
| --- | --- | --- |
| `RegisterBuildCallback` | `RegisterBuildCallback(Action<IResolver> callback) -> void` | Run a sync callback during `Build()`. |
| `RegisterBuildCallbackAsync` | `RegisterBuildCallbackAsync(Func<IResolver, Task> callback) -> void` | Async callback (no token). |
| `RegisterBuildCallbackAsync` | `RegisterBuildCallbackAsync(Func<IResolver, CancellationToken, Task> callback) -> void` | Async callback with a cancellation token. |
| `Build` | `Build() -> void` | Run sync callbacks, finalize bindings, collect lifecycle entry points, and run `Initialize()`. Idempotent (a second call is a no-op). |
| `BuildAsync` | `BuildAsync(CancellationToken cancellationToken = default) -> Task` | Run `Build()` then async callbacks. The result task is cached; on cancel/failure it is re-armed so a later `BuildAsync` retries. |
| `Tick` | `Tick() -> void` | Run `IOnityTickable.Tick()` on collected tickables in registration order. The owning Unity context calls this from `Update`. No-op before `Build()`. |
| `FixedTick` | `FixedTick() -> void` | Run `IOnityFixedTickable.FixedTick()`. Pumped from the context's `FixedUpdate`. |
| `LateTick` | `LateTick() -> void` | Run `IOnityLateTickable.LateTick()`. Pumped from the context's `LateUpdate`. |
| `Dispose` | `Dispose() -> void` | Dispose owned singletons in reverse registration order; clear all maps. Resolve/inject after dispose throws `OnityResolveException`. |

> Build callbacks cannot be registered after the build is finalized — both `RegisterBuildCallback` overloads throw `OnityBindingException` once `Build()`/`BuildAsync()` has run.

### Lifecycle interfaces

Implement on a singleton (or bound instance) to be wired up automatically — binding the type is enough, no explicit entry-point registration. Transient bindings are not ticked (no single stable instance).

| Interface | Member | Called |
| --- | --- | --- |
| `IOnityInitializable` | `Initialize()` | Once, at the end of `Build()`, in binding-registration order. Lifecycle singletons are created eagerly here. |
| `IOnityTickable` | `Tick()` | Every frame from the context's `Update` (via `OnityContainer.Tick`). |
| `IOnityFixedTickable` | `FixedTick()` | Every physics step from `FixedUpdate` (via `OnityContainer.FixedTick`). |
| `IOnityLateTickable` | `LateTick()` | Late each frame from `LateUpdate` (via `OnityContainer.LateTick`). |

---

## Injection sites — `[Inject]`

`InjectAttribute` targets `Constructor | Field | Property | Method`. Constructor injection is preferred; use the others only when a constructor cannot.

| Site | Rule |
| --- | --- |
| Constructor | A single `[Inject]` constructor is selected; otherwise the greediest public constructor. Multiple `[Inject]` constructors throw `OnityBindingException`. |
| Field | Private fields are allowed. |
| Property | A setter is **required** (private is fine). An `[Inject]` get-only property or an `[Inject]` indexer throws `OnityBindingException`. |
| Method | Runs after constructor, fields, and properties. **Cannot be generic** — a generic `[Inject]` method throws `OnityBindingException`. |
| Static members | Never injected (an `[Inject]` static member is silently ignored). |

Member-injection order: base class → derived class, and within a type **fields → properties → methods**.

---

## Diagnostics and binding sources

For editor tooling and debugging. None of these are needed for normal binding/resolve.

| API | Signature | Notes |
| --- | --- | --- |
| `OnityContainer.DiagnosticsCollectionEnabled` | `static bool { get; set; }` | Enable per-resolve timing/count collection. |
| `OnityContainer.IsCompiledActivationSupported` | `static bool { get; }` | True when the compiled `Expression.Compile` activation path is live (JIT: Editor and Mono players); false on AOT/IL2CPP (reflection fallback). |
| `GetDiagnostics` | `GetDiagnostics() -> OnityContainerDiagnostics` | Snapshot of binding/plan/owned-provider counts and whether a parent exists. |
| `GetBindingDiagnostics` | `GetBindingDiagnostics(List<OnityBindingDiagnostics> results) -> void` | Fill a caller-supplied list with per-binding rows (impl type, contracts, lifetime, resolve count, timings). |
| `PushBindingSource` | `PushBindingSource(string sourceName) -> IDisposable` | Label subsequent bindings (shown by inspector tooling) for the current thread; dispose to pop. |
| `TryGetBindingSource` | `TryGetBindingSource(Type contractType, out OnityBindingSourceInfo sourceInfo) -> bool` | Look up source metadata for a contract in this scope or an ancestor. |
| `TryGetLocalBindingSource` | `TryGetLocalBindingSource(Type contractType, out OnityBindingSourceInfo sourceInfo) -> bool` | Same, but this scope only. |

Constructor: `new OnityContainer(OnityContainer parent = null)` — pass a parent to create a child scope.

---

## Factories — `Onity.Factory`

Author one of these interfaces and bind it with `BindFactory` to pass a runtime value into an injected object. There is no `container.Instantiate<T>(args)` and no fluent factory body (`.FromMethod` etc.).

| Interface | Member |
| --- | --- |
| `IFactory<TValue>` | `TValue Create()` |
| `IFactory<TParam, TValue>` | `TValue Create(TParam param)` |
| `IFactory<TParam1, TParam2, TValue>` | `TValue Create(TParam1 p1, TParam2 p2)` |

---

## Exceptions

Two sealed exception types, both in `Onity.DI`.

| Type | Raised by |
| --- | --- |
| `OnityResolveException` | Resolve/inject failures: unbound contract, circular dependency (resolve-time), failed construction, null service type, null inject target, resolve after dispose. |
| `OnityBindingException` | Binding/config failures: null/non-assignable/abstract implementation, no-interface `BindInterfacesTo`, empty contract list, no accessible constructor, multiple `[Inject]` constructors, setterless/indexer `[Inject]` property, generic `[Inject]` method, callback registration after build, `NonLazy` before a lifetime. |

For the full message-to-fix table, see the [AI Usage Guide](../Onity-AI-Usage-Guide.html) error section. Narrative usage lives in the [Dependency Injection guide](../guide/dependency-injection.html).

> Not shipped (Zenject parity gaps): no `WhenInjectedInto`, no `WithId`, no `Instantiate(args)`. Use a typed factory or distinct contracts instead.
