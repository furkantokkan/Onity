# Migrating from Zenject / Extenject to Onity

Onity's DI surface is intentionally Zenject-familiar: you still `Bind<TContract>().To<TConcrete>().AsSingle()`, you still mark injection points with `[Inject]`, and child containers still inherit parent bindings. The mechanical translation below is therefore mostly one-to-one on the common paths. The differences that matter are: Onity's lifetime enum is exactly `{ Singleton, Transient }` (no `Scoped`/`AsCached`/`AsTransient`-with-id distinctions), Onity uses **last-binding-wins** instead of throwing on duplicate binds, Onity selects the **greediest public constructor** (or the single `[Inject]` ctor) rather than Zenject's fewest-argument rule, circular dependencies are detected at **resolve time** rather than build time, and a number of Zenject features (conditions, ids, memory pools, signals, sub-container facades) are deliberate non-goals — see the last section. Every mapping below is verified against the shipped Onity public API (`Onity.DI`, `Onity.Factory`); do not assume any Zenject API exists on Onity unless it appears here.

## Binding

| Zenject | Onity | Notes |
| --- | --- | --- |
| `Container.Bind<IFoo>().To<Foo>().AsSingle()` | `container.Bind<IFoo>().To<Foo>().AsSingle()` | Identical. One shared instance. |
| `Container.Bind<IFoo>().To<Foo>().AsTransient()` | `container.Bind<IFoo>().To<Foo>().AsTransient()` | New instance per resolve. |
| `Container.Bind<Foo>().AsSingle()` (ToSelf) | `container.Bind<Foo>().AsSingle()` | `To` defaults to the contract type (self-bind shorthand). |
| `Container.Bind<Foo>().ToSelf().FromNew().AsSingle()` | `container.Bind<Foo>().To<Foo>().AsSingle()` | The explicit ToSelf/FromNew ceremony collapses to a plain `To<Foo>()`. |
| `Container.Bind<IFoo>().To<Foo>().AsCached()` | `container.Bind<IFoo>().To<Foo>().AsSingle()` | `AsCached` maps to `AsSingle` (one cached instance). There is no separate cached lifetime. |
| `Container.Bind<IFoo>().To<Foo>().AsSingle().NonLazy()` | `container.Bind<IFoo>().To<Foo>().AsSingle().NonLazy()` | `NonLazy()` resolves eagerly in `Build()`. It throws `OnityBindingException` if called before `AsSingle()`/`AsTransient()`. |
| `Container.BindInstance(foo)` | `container.BindInstance<IFoo>(foo)` | Binds a pre-built instance. **Rejects null** with `OnityBindingException` (Zenject allows it). |
| `Container.BindInterfacesTo<Foo>().AsSingle()` | `container.BindInterfacesTo<Foo>().AsSingle()` | Binds all of `Foo`'s interfaces to one shared instance; the concrete type is **not** bound. Throws `OnityBindingException` if `Foo` has no interfaces. |
| `Container.BindInterfacesAndSelfTo<Foo>().AsSingle()` | `container.BindInterfacesAndSelfTo<Foo>().AsSingle()` | Binds the concrete **and** all its interfaces to one shared instance. |
| `Container.Bind(typeof(IA), typeof(IB)).To<C>().AsSingle()` | `container.BindInterfacesAndSelfTo<C>().AsSingle()` (or two separate binds — see trap) | Onity has no multi-`Bind(typeof,…)` overload. Use `BindInterfacesAndSelfTo`/`BindInterfacesTo` to share one instance across many contracts. |
| `Container.Bind<Foo>().AsSingle(); Container.Bind<Foo>().AsSingle();` → **throws** (duplicate) | `container.Bind<Foo>().AsSingle(); container.Bind<Foo>().AsSingle();` → **no throw** | Divergence: Onity is last-binding-wins. Re-binding the same contract replaces the previous binding instead of erroring. |

> **Shared-instance trap (verified):** two separate `Bind<IFoo>().To<C>()` and `Bind<IBar>().To<C>()` calls produce **distinct** singletons, not one shared `C`. To share one instance across a concrete plus its interfaces, use `BindInterfacesAndSelfTo<C>().AsSingle()`.

## Injection (`[Inject]`)

`Onity.DI` ships a single `[Inject]` attribute (`Onity.DI.InjectAttribute`) targeting constructor / field / property / method — same shape as Zenject's `[Inject]`.

| Zenject | Onity | Notes |
| --- | --- | --- |
| Constructor injection (single ctor) | Constructor injection (single ctor) | Identical and preferred. |
| `[Inject] public Foo(IBar bar)` (tagged ctor) | `[Inject] public Foo(IBar bar)` | A single `[Inject]` ctor always wins over other ctors. |
| Multiple ctors, **fewest** args chosen | Multiple ctors, **greediest** (most params) public ctor chosen | Divergence (test-locked). Without an `[Inject]` ctor, Onity picks the highest-scoring public constructor, not Zenject's least-arguments one. |
| `[Inject] IClock m_clock;` (field, private OK) | `[Inject] private IClock m_clock;` | Field injection, private allowed. Static fields are never injected. |
| `[Inject] public ILogger Logger { get; set; }` | `[Inject] public ILogger Logger { get; set; }` | Property injection — **setter required** (a get-only `[Inject]` property throws `OnityBindingException`). |
| `[Inject] void Init(IConfig c)` (post-inject method) | `[Inject] private void Init(IConfig c)` | Method injection. Runs after ctor + fields + properties. Must be non-generic (generic `[Inject]` method throws `OnityBindingException`). |
| `container.Inject(existingObject)` | `container.Inject(existingObject)` | Member-injects an already-constructed object. Throws `OnityResolveException` on a null target. |
| `[Inject(Optional = true)]` | *(no equivalent)* | Optional/default-value injection is not modeled — bind the dependency. |
| `[Inject(Id = "x")]` / `WithId(...)` | *(no equivalent — non-goal)* | No id/keyed binds. Use distinct contract types or a typed factory. |

Member-injection order is base class → derived class, and within a type **fields → properties → methods**, matching Zenject's general ordering for the supported member kinds.

## Resolution

| Zenject | Onity | Notes |
| --- | --- | --- |
| `container.Resolve<IFoo>()` | `container.Resolve<IFoo>()` | Throws `OnityResolveException` if unresolvable. |
| `container.Resolve(typeof(IFoo))` | `container.Resolve(typeof(IFoo))` | Runtime-type overload. |
| `container.TryResolve<IFoo>()` (null on miss) | `container.TryResolve<IFoo>(out IFoo foo)` | Returns `bool`; `out` is null on miss (no exception). |
| `container.TryResolve(typeof(IFoo))` | `container.TryResolve(typeof(IFoo), out object foo)` | Runtime-type `TryResolve`. |
| `container.HasBinding(typeof(IFoo))` | `container.CanResolve(typeof(IFoo))` | Check without instantiating. |
| `container.ResolveAll<IFoo>()` / `IEnumerable<IFoo>` inject | *(no equivalent — non-goal)* | No collection/multi injection. Inject a single registry or factory and resolve a known set. |
| Self-resolve `DiContainer` | `container.Resolve<OnityContainer>()` / `Resolve<IResolver>()` | The active container self-resolves to itself and to `IResolver`. Inject `IResolver` to do manual resolves (e.g. inside a factory). |

## Factories

Onity has no fluent factory body (no `.FromMethod`, no `.FromFactory`, no `PlaceholderFactory` subclassing magic) and no `container.Instantiate<T>(args)`. To pass a runtime argument into a constructed object you author an `IFactory<...>` implementation (`Onity.Factory`) and register it with `BindFactory`. The factory is always bound `AsSingle`.

| Zenject | Onity | Notes |
| --- | --- | --- |
| `Container.BindFactory<Foo, Foo.Factory>()` (zero-arg) | `container.BindFactory<Foo, FooFactory>()` where `FooFactory : IFactory<Foo>` | Resolve `IFactory<Foo>` and call `Create()`. |
| `Container.BindFactory<string, Foo, Foo.Factory>()` (1 arg) | `container.BindFactory<string, Foo, FooFactory>()` where `FooFactory : IFactory<string, Foo>` | Resolve `IFactory<string, Foo>` and call `Create(arg)`. |
| 2-arg placeholder factory | `container.BindFactory<string, int, Foo, FooFactory>()` where `FooFactory : IFactory<string, int, Foo>` | `IFactory<TParam1, TParam2, TValue>` is the largest arity shipped. |
| `PlaceholderFactory<T>` subclass | hand-authored `IFactory<...>` impl (inject `IResolver`, `new` the value yourself) | No generated placeholder factory. The factory body constructs the instance, optionally resolving collaborators via the injected `IResolver`. |

```csharp
using Onity.DI;
using Onity.Factory;

public sealed class EnemyFactory : IFactory<string, Enemy>
{
    private readonly IResolver m_resolver;            // IResolver self-injects
    public EnemyFactory(IResolver resolver) { m_resolver = resolver; }
    public Enemy Create(string id) => new Enemy(id, m_resolver.Resolve<IClock>());
}

// container.BindFactory<string, Enemy, EnemyFactory>();
// container.Resolve<IFactory<string, Enemy>>().Create("goblin");
```

## Scopes / sub-containers / lifecycle

| Zenject | Onity | Notes |
| --- | --- | --- |
| Sub-container / `CreateSubContainer()` | `new OnityContainer(parent)` | A child container inherits parent bindings; a child bind shadows the parent **only inside the child**. This is Onity's "scoped". |
| `GameObjectContext` / sub-container facade | child `OnityContainer` (no facade type) | No `FromSubContainerResolve` / facade binding. Compose with a plain child container. |
| `MonoInstaller.InstallBindings()` | `MonoInstaller.InstallBindings(OnityContainer container)` (`Onity.Unity.Installers`) | Same idea; you receive the `OnityContainer` to bind into. |
| `ProjectContext` / `SceneContext` | `ProjectContext` / `SceneContext` / `GameObjectContext` (`Onity.Unity.Contexts`) | Context creates the container, registers defaults (container, `IResolver`, `MessageBroker`, `OnityEventHub`), runs installers, builds, auto-injects. |
| `IInitializable.Initialize()` | `container.RegisterBuildCallback(r => …)` or `[Inject]` method or `NonLazy()` | Sync startup work runs in `Build()` via a build callback or a post-inject method. |
| `ITickable.Tick()` | `OnityUnityObservable.EveryUpdate().Subscribe(...)` (`Onity.Unity.Reactive`) | Ticking lives in Reactive, not the DI layer — subscribe to the frame loop and `AddTo(this)`. |
| `IDisposable` / `OnDestroy` cleanup | `container.Dispose()` | Disposes owned singletons in reverse registration order. |
| *(none)* | `container.Build()` / `await container.BuildAsync(ct)` | Runs build callbacks (sync, then async). Bindings cannot be added after build is finalized (throws `OnityBindingException`). Note: a bare `Resolve` works without an explicit build, unlike a strict builder ceremony. |

## Errors

Onity ships two DI exception types, both in `Onity.DI`: `OnityResolveException` (resolve/inject failures, including circular dependencies and unresolvable types) and `OnityBindingException` (binding/config failures: null instance, non-assignable `To<>`, multiple `[Inject]` ctors, setterless/indexer/generic `[Inject]`, post-build registration). Map any Zenject `ZenjectException` catch onto these two.

## Not supported — do this instead

These Zenject features are deliberate Onity non-goals (see `docs/Plan/07-Competitive-And-AI-Roadmap.md` section 6). Do not attempt the Zenject API; use the Onity replacement.

| Zenject feature | Why it is a non-goal | Do this in Onity |
| --- | --- | --- |
| Conditional / contextual bindings (`WhenInjectedInto`, `WhenNotInjectedInto`) | Biggest source of Zenject complexity and per-resolve ambiguity; adds condition evaluation to the hot path and fights a predictable model. | Use a typed factory, or distinct contract types, for "two impls of one interface". |
| Id / keyed bindings (`WithId`, `[Inject(Id=…)]`) | Same predictability/hot-path reason as conditions. | Distinct contract interfaces or a typed factory. |
| Memory pools (`MemoryPool<T>`, `BindMemoryPool`) | Out of the engine-free DI core's scope. | Author an `IFactory<...>` (and pool inside it if needed); the Unity layer offers `BindPooledFactory` for component pools. |
| Signals (`SignalBus`, `DeclareSignal`, `BindSignal`) | Messaging is a separate pillar with one model. | Use `Onity.Messaging` — `IPublisher<T>`/`ISubscriber<T>` via the auto-bound `MessageBroker`/`OnityEventHub`, and `broker.Observe<T>()` for reactive chains. |
| Sub-container facades (`FromSubContainerResolve`, `ByInstaller`) | Adds facade indirection; child containers already cover scoping. | Compose a child `new OnityContainer(parent)` and bind into it directly. |
| `Container.Instantiate<T>(args)` | No runtime-arg construction on the container. | Author an `IFactory<TParam, TValue>` and `BindFactory`. |
| Open-generic binds (`Bind(typeof(Repository<>))`) | Requires on-demand closed-activator construction — an AOT/IL2CPP and hot-path complication. | Bind **closed** generics explicitly: `Bind<Repository<int>>()…`. Unbound interfaces/abstracts/open-generics throw `OnityResolveException`. |
| Unbind / Rebind API | Conflicts with the "no bindings after Build" rule and the cached resolution model. | Rely on last-binding-wins to override a binding before `Build()`. |
| `IInitializable`/`ITickable` auto-tick inside the DI layer | Per-frame dispatch in the engine-free DI core couples it to the Unity loop. | Sync init via `RegisterBuildCallback`/`NonLazy`; per-frame work via `EveryUpdate()` in `Onity.Unity.Reactive`. |
| Collection injection (`List<T>`/`IEnumerable<T>` of all bindings) | Last-binding-wins overwrites; no closed `IEnumerable<T>` resolve. | Inject a single registry/factory; resolve a known set explicitly. |
