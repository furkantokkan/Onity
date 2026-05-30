# Migrating from VContainer to Onity

VContainer registers with `builder.Register<TImpl>(Lifetime.Singleton).As<TInterface>()` inside a `LifetimeScope`; Onity binds with the Zenject-style fluent vocabulary `container.Bind<TInterface>().To<TImpl>().AsSingle()` directly on an `OnityContainer`. The translation is mechanical, but four things differ in shape: Onity's lifetime enum is exactly `{ Singleton, Transient }` — there is **no `Lifetime.Scoped`**, so a per-scope instance becomes a **child-container `AsSingle`**; Onity binds an instance with `BindInstance` (which **rejects null**, unlike VContainer's `RegisterInstance`); Onity selects the **greediest resolvable public constructor** (or a single `[Inject]` ctor); and there is **no `IEnumerable<T>` collection injection** and **no `RegisterEntryPoint`/`IStartable` tick loop** — those route through other Onity pillars. Circular dependencies are caught at **resolve time** (VContainer catches them at `Build()`), but either way an exception is thrown. Every mapping below is verified against the shipped Onity public API (`Onity.DI`, `Onity.Factory`).

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
| `builder.RegisterEntryPoint<T>()` / `IStartable`, `ITickable`, `IInitializable` | *(no equivalent — non-goal)* | No entry-point/tick interfaces. See Lifecycle below and the non-goals section. |

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
| `[Inject] IEnumerable<IFoo> all` (collection inject) | *(no equivalent — non-goal)* | No `IEnumerable<T>`/`IReadOnlyList<T>`/`T[]` multi-injection. Inject a single registry/factory instead. |

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

## Scopes & lifecycle

| VContainer | Onity | Notes |
| --- | --- | --- |
| `LifetimeScope` (root) | `ProjectContext` / `SceneContext` / `GameObjectContext` (`Onity.Unity.Contexts`) | The context builds the container, registers defaults, runs installers, builds, auto-injects. |
| `Configure(IContainerBuilder builder)` | `MonoInstaller.InstallBindings(OnityContainer container)` (`Onity.Unity.Installers`) | Where you bind. You receive the `OnityContainer` directly. |
| Child `LifetimeScope` / `CreateChild()` | `new OnityContainer(parent)` | Child inherits parent bindings; a child bind shadows the parent only inside the child. |
| `Lifetime.Scoped` | child-container `AsSingle` | Shared within a child scope, distinct across sibling child scopes. |
| `builder.RegisterBuildCallback(resolver => …)` | `container.RegisterBuildCallback(r => …)` | Runs in `Build()`. Also `RegisterBuildCallbackAsync(Func<IResolver, CancellationToken, Task>)`. |
| `IInitializable.Initialize()` | `RegisterBuildCallback` or an `[Inject]` method or `NonLazy()` | Sync post-construction wiring. |
| `IStartable.Start()` / `ITickable.Tick()` | `OnityUnityObservable.EveryUpdate().Subscribe(...).AddTo(this)` (`Onity.Unity.Reactive`) | Per-frame work lives in Reactive, not the DI layer. |
| `IDisposable` registered services disposed on scope dispose | `container.Dispose()` | Disposes owned singletons in reverse registration order. |
| Build-time validation of the whole graph | resolve-time validation | `Build()` runs `NonLazy`/callbacks; missing/circular bindings surface at resolve time as `OnityResolveException`. |

## Errors

VContainer throws `VContainerException` for both registration and resolution problems. Onity splits these: `OnityBindingException` (binding/config: null instance, non-assignable `To<>`, multiple `[Inject]` ctors, setterless/indexer/generic `[Inject]`, post-build registration) and `OnityResolveException` (unresolvable type, circular dependency, ctor throw, inject into null). Both live in `Onity.DI`.

## Not supported — do this instead

These VContainer features are deliberate Onity non-goals (see `docs/Plan/07-Competitive-And-AI-Roadmap.md` section 6). Do not call the VContainer API; use the Onity replacement.

| VContainer feature | Why it is a non-goal | Do this in Onity |
| --- | --- | --- |
| `Lifetime.Scoped` keyword | A true Scoped lifetime forces the resolver to key singletons by requesting scope, complicating the hot path for marginal benefit. Parent/child containers already cover it. | Use a child container: `new OnityContainer(parent)` and `Bind…AsSingle()` in the child. Parent `AsSingle` stays shared; the child instance is per-scope. |
| Collection injection (`IEnumerable<T>` / `IReadOnlyList<T>` / `T[]`) | Last-binding-wins overwrites; no closed `IEnumerable<T>` resolve in the current model. | Inject a single registry or a typed factory; resolve a known set explicitly. |
| `RegisterEntryPoint<T>()` / `IStartable` / `ITickable` / `IInitializable` (in-container tick loop) | Per-frame dispatch in the engine-free DI core couples it to the Unity update loop, against the engine-free-core philosophy. | Sync startup via `RegisterBuildCallback`/`NonLazy`; per-frame work via `EveryUpdate()` in `Onity.Unity.Reactive` (`Subscribe(...).AddTo(this)`). |
| Open-generic registration (`Register(typeof(Repository<>))`) | Requires on-demand closed-activator construction — an AOT/IL2CPP and hot-path complication. | Bind **closed** generics explicitly (e.g. `Bind<Repository<int>>()`). Unbound open generics throw `OnityResolveException`. |
| `Func<TParam, TValue>` factory registration | No `Func<>`/`Instantiate(args)` factory surface. | Author an `IFactory<TParam, TValue>` and `BindFactory`. |
| `RegisterInstance(null)` | Onity treats a null instance binding as a configuration error. | Pass a non-null instance to `BindInstance`. |
