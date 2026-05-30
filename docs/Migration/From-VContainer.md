---
title: "From VContainer"
parent: "Migration"
nav_order: 2
---

# Migrating from VContainer to Onity

VContainer registers with `builder.Register<TImpl>(Lifetime.Singleton).As<TInterface>()` inside a `LifetimeScope`; Onity binds with the Zenject-style fluent vocabulary `container.Bind<TInterface>().To<TImpl>().AsSingle()` directly on an `OnityContainer`. The translation is mechanical, but a few things differ in shape: Onity's lifetime enum is exactly `{ Singleton, Transient }` — there is **no `Lifetime.Scoped`**, so a per-scope instance becomes a **child-container `AsSingle`**; Onity binds an instance with `BindInstance` (which **rejects null**, unlike VContainer's `RegisterInstance`); and Onity selects the **greediest resolvable public constructor** (or a single `[Inject]` ctor). Circular dependencies are caught at **resolve time** (VContainer catches them at `Build()`), but either way an exception is thrown. Two capabilities older drafts of this guide listed as missing now ship and map cleanly from VContainer: **collection injection** (`IEnumerable<T>` / `IReadOnlyList<T>` / `T[]` / `List<T>`) and an **entry-point-style lifecycle** (`IOnityInitializable` / `IOnityTickable` / `IOnityFixedTickable` / `IOnityLateTickable`, the analogue of `IStartable`/`ITickable`, pumped by the Unity context). **Open-generic registration** (`Bind(typeof(IRepo<>)).To(typeof(Repo<>))`) also ships. The `Func<>`-factory surface and `RegisterInstance(null)` remain deliberate divergences — see the last section. Every mapping below is verified against the shipped Onity public API (`Onity.DI`, `Onity.Factory`, `Onity.Unity.Reactive`).

## Registration

| VContainer | Onity | Notes |
| --- | --- | --- |
| `builder.Register<Impl>(Lifetime.Singleton).As<IService>()` | `container.Bind<IService>().To<Impl>().AsSingle()` | One shared instance. |
| `builder.Register<Impl>(Lifetime.Transient).As<IService>()` | `container.Bind<IService>().To<Impl>().AsTransient()` | New instance per resolve. |
| `builder.Register<Impl>(Lifetime.Singleton)` (self, no `As`) | `container.Bind<Impl>().AsSingle()` | `To` defaults to the contract (self-bind). |
| `builder.Register<Impl>(Lifetime.Scoped)` | `child.Bind<Impl>().AsSingle()` on `new OnityContainer(parent)` | **Divergence:** there is no Scoped lifetime keyword. Per-scope == child-container `AsSingle`. |
| `builder.Register<Impl>(Lifetime.Singleton).As<IA>().As<IB>().AsSelf()` | `container.BindInterfacesAndSelfTo<Impl>().AsSingle()` | Share one instance across the concrete + all its interfaces. |
| `builder.Register<Impl>(Lifetime.Singleton).AsImplementedInterfaces()` | `container.BindInterfacesTo<Impl>().AsSingle()` | Interfaces only (no concrete). Throws `OnityBindingException` if `Impl` has no interfaces. |
| `builder.RegisterInstance(instance).As<IService>()` | `container.BindInstance<IService>(instance)` | **Divergence:** `BindInstance(null)` throws `OnityBindingException` (VContainer allows null instances). |
| `builder.RegisterInstance(instance).AsImplementedInterfaces().AsSelf()` | `container.BindInstance<IA>(instance); container.BindInstance<Impl>(instance);` | Bind the same object under each contract you need. |
| Re-register the same type twice → last wins (no error) | `container.Bind<Foo>()…; container.Bind<Foo>()…;` → last wins (no error) | Onity is last-binding-wins, matching the "no duplicate-binding exception" behavior. |
| `builder.RegisterComponentInHierarchy<T>()` / `RegisterComponentInNewPrefab<T>()` | *(no direct equivalent)* | Component/prefab registration is not part of the engine-free core. Bind the instance after you obtain it, or use the Unity-layer `BindScriptableObject`/`BindPooledFactory` helpers. |
| `builder.RegisterEntryPoint<T>()` / `IStartable`, `ITickable`, `IInitializable` | `container.Bind<T>().AsSingle()` where `T : IOnityInitializable` / `IOnityTickable` / … | Binding a singleton that implements an `IOnity*` lifecycle interface is enough — no `RegisterEntryPoint`. The Unity context pumps `Tick`/`FixedTick`/`LateTick`. See Lifecycle below. |

> **Shared-instance trap (verified):** two separate `Bind<IFoo>().To<C>()` and `Bind<IBar>().To<C>()` calls create **distinct** singletons. To get VContainer's `As<IA>().As<IB>()` "one instance, many contracts" behavior, use `BindInterfacesAndSelfTo<C>().AsSingle()` (or `BindInterfacesTo<C>()`).

## Injection (`[Inject]`)

VContainer uses `[Inject]` on constructors, fields, properties, and methods; Onity's `Onity.DI.InjectAttribute` targets the same member kinds with the same intent.

| VContainer | Onity | Notes |
| --- | --- | --- |
| Constructor injection (single ctor) | Constructor injection (single ctor) | Identical and preferred. |
| Greediest resolvable ctor chosen automatically | Greediest **public** ctor chosen (most params) | Same "greediest" spirit. A single `[Inject]` ctor overrides selection. |
| `[Inject] public Foo(IBar b)` | `[Inject] public Foo(IBar b)` | The `[Inject]`-marked ctor wins over other ctors. |
| `[Inject] IBar m_bar;` (field, non-public OK) | `[Inject] private IBar m_bar;` | Field injection, private allowed. Static members are never injected. |
| `[Inject] public IBar Bar { get; set; }` | `[Inject] public IBar Bar { get; set; }` | Property injection — **setter required** (setterless `[Inject]` property throws `OnityBindingException`). |
| `[Inject] public void Construct(IBar b)` (method) | `[Inject] private void Initialize(IBar b)` | Method injection, runs after ctor + fields + properties. Must be non-generic (else `OnityBindingException`). |
| `objectResolver.Inject(instance)` | `container.Inject(instance)` | Member-injects an existing object. |
| `[Inject] IEnumerable<IFoo> all` (collection inject) | `[Inject] private IReadOnlyList<IFoo> m_all;` (or ctor param) | Collection injection is supported: `IEnumerable<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>` / `IList<T>` / `ICollection<T>` / `List<T>` / `T[]`, gathered from every explicit `T` binding in this scope and its ancestors. |

Member-injection order is base → derived, and within a type **fields → properties → methods**.

## Resolution

| VContainer | Onity | Notes |
| --- | --- | --- |
| `resolver.Resolve<IFoo>()` | `container.Resolve<IFoo>()` | Throws `OnityResolveException` if unresolvable. |
| `resolver.Resolve(typeof(IFoo))` | `container.Resolve(typeof(IFoo))` | Runtime-type overload. |
| `resolver.TryResolve<IFoo>(out var foo)` | `container.TryResolve<IFoo>(out IFoo foo)` | Returns `bool`; `out` null on miss. |
| *(via `IObjectResolver`)* | `container.TryResolve(typeof(IFoo), out object foo)` | Runtime-type `TryResolve`. |
| `builder.Register<IObjectResolver>` is implicit; inject `IObjectResolver` | inject `IResolver` (or `OnityContainer`) | The active container self-resolves to `IResolver` and `OnityContainer`. Use `IResolver` for manual resolves inside a factory. |
| Resolve unregistered → `VContainerException` | Resolve unbound interface/abstract → `OnityResolveException` | Unbound **concrete** classes auto-resolve as implicit transients; unbound interfaces/abstracts/open-generics throw. |

## Factories / runtime arguments

VContainer offers `Func<TParam, TValue>` factory registration and `RegisterFactory`. Onity has neither a `Func<>` factory nor `Instantiate(args)`; you author an `IFactory<...>` (`Onity.Factory`) and bind it with `BindFactory` (always `AsSingle`).

| VContainer | Onity | Notes |
| --- | --- | --- |
| `builder.RegisterFactory<TValue>(...)` | `container.BindFactory<TValue, TFactory>()` where `TFactory : IFactory<TValue>` | Resolve `IFactory<TValue>`, call `Create()`. |
| `builder.RegisterFactory<TParam, TValue>(...)` | `container.BindFactory<TParam, TValue, TFactory>()` where `TFactory : IFactory<TParam, TValue>` | Resolve `IFactory<TParam, TValue>`, call `Create(param)`. |
| `Func<T1, T2, TValue>` factory | `container.BindFactory<T1, T2, TValue, TFactory>()` where `TFactory : IFactory<T1, T2, TValue>` | `IFactory<TParam1, TParam2, TValue>` is the largest arity shipped. |

```csharp
using Onity.DI;
using Onity.Factory;

public sealed class EnemyFactory : IFactory<string, Enemy>
{
    private readonly IResolver m_resolver;
    public EnemyFactory(IResolver resolver) { m_resolver = resolver; }
    public Enemy Create(string id) => new Enemy(id, m_resolver.Resolve<IClock>());
}

// container.BindFactory<string, Enemy, EnemyFactory>();
// container.Resolve<IFactory<string, Enemy>>().Create("goblin");
```

## Collection injection (matches VContainer's `IEnumerable<T>` resolve)

VContainer collects every registration of an interface when you inject `IEnumerable<T>`. Onity does the same — request `IReadOnlyList<T>` (or `IEnumerable<T>` / `T[]` / `List<T>`), gathered from this scope and its ancestors in registration order.

```csharp
// VContainer
// builder.Register<CritRule>(Lifetime.Singleton).As<IDamageRule>();
// builder.Register<ArmorRule>(Lifetime.Singleton).As<IDamageRule>();
public DamagePipeline(IEnumerable<IDamageRule> rules) { /* all registrations */ }

// Onity
container.Bind<IDamageRule>().To<CritRule>().AsSingle();
container.Bind<IDamageRule>().To<ArmorRule>().AsSingle();

public sealed class DamagePipeline
{
    private readonly IReadOnlyList<IDamageRule> m_rules;
    public DamagePipeline(IReadOnlyList<IDamageRule> rules) { m_rules = rules; }
}
```

> A plain `Resolve<IDamageRule>()` returns the **last** registration (last-binding-wins); only a collection-typed request gathers all of them.

## Entry-point lifecycle (matches `IStartable` / `ITickable`)

```csharp
// VContainer
public sealed class WaveSpawner : IStartable, ITickable
{
    public void Start() { /* ... */ }
    public void Tick() { /* ... */ }
}
// builder.RegisterEntryPoint<WaveSpawner>();

// Onity
using Onity.DI;

public sealed class WaveSpawner : IOnityInitializable, IOnityTickable
{
    public void Initialize() { /* runs once at Build() */ }
    public void Tick() { /* runs every Update from the owning context */ }
}
// container.Bind<WaveSpawner>().AsSingle();   // binding is the whole wiring
```

## Open-generic registration (matches `Register(typeof(IRepo<>), ...)`)

```csharp
// container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();
// container.Resolve<IRepository<Player>>();   // builds Repository<Player> on first resolve
```

The first resolve of a closed contract builds and caches it as a normal binding. On IL2CPP the closed type must survive AOT stripping. `NonLazy()` is not supported on an open-generic binding.

## Scopes & lifecycle

| VContainer | Onity | Notes |
| --- | --- | --- |
| `LifetimeScope` (root) | `ProjectContext` / `SceneContext` / `GameObjectContext` (`Onity.Unity.Contexts`) | The context builds the container, registers defaults, runs installers, builds, auto-injects. |
| `Configure(IContainerBuilder builder)` | `MonoInstaller.InstallBindings(OnityContainer container)` (`Onity.Unity.Installers`) | Where you bind. You receive the `OnityContainer` directly. |
| Child `LifetimeScope` / `CreateChild()` | `new OnityContainer(parent)` | Child inherits parent bindings; a child bind shadows the parent only inside the child. |
| `Lifetime.Scoped` | child-container `AsSingle` | Shared within a child scope, distinct across sibling child scopes. |
| `builder.RegisterBuildCallback(resolver => …)` | `container.RegisterBuildCallback(r => …)` | Runs in `Build()`. Also `RegisterBuildCallbackAsync(Func<IResolver, CancellationToken, Task>)`. |
| `IInitializable.Initialize()` / `IStartable.Start()` | `IOnityInitializable.Initialize()` (`Onity.DI`) | Automatic: a bound singleton/instance implementing it is initialized once at `Build()`, in registration order. No entry-point call. (`RegisterBuildCallback`, an `[Inject]` method, or `NonLazy()` remain available for ad-hoc startup work.) |
| `ITickable.Tick()` | `IOnityTickable.Tick()` (`Onity.DI`) | Automatic: a bound singleton/instance is pumped every `Update` by the owning context. **Transients are not ticked.** |
| `IFixedTickable.FixedTick()` / `ILateTickable.LateTick()` | `IOnityFixedTickable.FixedTick()` / `IOnityLateTickable.LateTick()` | Pumped from the context's `FixedUpdate` / `LateUpdate`. |
| Tick a non-registered object per frame | `OnityUnityObservable.EveryUpdate().Subscribe(...).AddTo(this)` (`Onity.Unity.Reactive`) | For per-frame work that is not a bound singleton, subscribe the frame loop directly. |
| `IDisposable` registered services disposed on scope dispose | `container.Dispose()` | Disposes owned singletons (including lifecycle singletons) in reverse registration order. |
| Build-time validation of the whole graph | resolve-time validation | `Build()` runs `NonLazy`/callbacks; missing/circular bindings surface at resolve time as `OnityResolveException`. |

## Errors

VContainer throws `VContainerException` for both registration and resolution problems. Onity splits these: `OnityBindingException` (binding/config: null instance, non-assignable `To<>`, multiple `[Inject]` ctors, setterless/indexer/generic `[Inject]`, post-build registration) and `OnityResolveException` (unresolvable type, circular dependency, ctor throw, inject into null). Both live in `Onity.DI`.

## Not supported — do this instead

These VContainer features are deliberate Onity non-goals (see `docs/Plan/07-Competitive-And-AI-Roadmap.md` section 6). Do not call the VContainer API; use the Onity replacement.

| VContainer feature | Why it is a non-goal | Do this in Onity |
| --- | --- | --- |
| `Lifetime.Scoped` keyword | A true Scoped lifetime forces the resolver to key singletons by requesting scope, complicating the hot path for marginal benefit. Parent/child containers already cover it. | Use a child container: `new OnityContainer(parent)` and `Bind…AsSingle()` in the child. Parent `AsSingle` stays shared; the child instance is per-scope. |
| `RegisterEntryPoint<T>()` **in-container tick loop driven by `Onity.DI` alone** | Per-frame dispatch from the engine-free DI core in isolation would couple it to the Unity update loop. | The lifecycle interfaces themselves **are supported** (`IOnityInitializable`/`IOnityTickable`/…); they live in `Onity.DI` but are pumped by the Unity **context**. See the lifecycle section above. |
| `Func<TParam, TValue>` factory registration | No `Func<>`/`Instantiate(args)` factory surface. | Author an `IFactory<TParam, TValue>` and `BindFactory`. |
| `RegisterInstance(null)` | Onity treats a null instance binding as a configuration error. | Pass a non-null instance to `BindInstance`. |

> **Now supported (no longer non-goals):** **collection injection** (`IEnumerable<T>` / `IReadOnlyList<T>` / `T[]` / `List<T>`) and **open-generic registration** (`Bind(typeof(IRepo<>)).To(typeof(Repo<>))`) ship today — see the dedicated sections above.
