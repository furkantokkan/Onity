# Migrating from Zenject / Extenject to Onity

Onity's DI surface is intentionally Zenject-familiar: you still `Bind<TContract>().To<TConcrete>().AsSingle()`, you still mark injection points with `[Inject]`, and child containers still inherit parent bindings. The mechanical translation below is therefore mostly one-to-one on the common paths. The differences that matter are: Onity's lifetime enum is exactly `{ Singleton, Transient }` (no `Scoped`/`AsCached`/`AsTransient`-with-id distinctions), Onity uses **last-binding-wins** instead of throwing on duplicate binds, Onity selects the **greediest public constructor** (or the single `[Inject]` ctor) rather than Zenject's fewest-argument rule, and circular dependencies are detected at **resolve time** rather than build time. Three features that older drafts of this guide listed as missing now ship: **collection injection** (`IEnumerable<T>`/`IReadOnlyList<T>`/`T[]`/`List<T>` of every binding of `T`), **open-generic binds** (`Bind(typeof(IRepo<>)).To(typeof(Repo<>))`), and an **automatic lifecycle** (`IOnityInitializable`/`IOnityTickable`/`IOnityFixedTickable`/`IOnityLateTickable`) where binding a type is enough to be initialized and ticked — no manual entry-point registration. The features that remain deliberate non-goals (conditions, ids, `Unbind`, memory pools, signals, sub-container facades, `Instantiate(args)`) are listed in the last section. Every mapping below is verified against the shipped Onity public API (`Onity.DI`, `Onity.Factory`, `Onity.Unity.Reactive`); do not assume any Zenject API exists on Onity unless it appears here.

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
| `container.ResolveAll<IFoo>()` | `container.Resolve<IReadOnlyList<IFoo>>()` (or `IEnumerable<IFoo>` / `IFoo[]` / `List<IFoo>`) | Resolves every explicit `IFoo` binding in this scope and its ancestors. There is no separate `ResolveAll` method — request a collection type. |
| `[Inject] List<IFoo> all` (collection inject) | `[Inject] private IReadOnlyList<IFoo> m_all;` (or ctor param) | Collection injection is supported. See the collection example below. |
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

## Automatic lifecycle (replaces `IInitializable` / `ITickable` registration)

In Zenject you declare the lifecycle interface and then register the type as an entry point (`BindInterfacesAndSelfTo<...>()` plus the entry-point binding). In Onity, binding a singleton that implements an `IOnity*` lifecycle interface is enough — the container collects and drives it. The context pumps `Tick`/`FixedTick`/`LateTick` for you.

```csharp
// Zenject
public sealed class WaveSpawner : IInitializable, ITickable
{
    public void Initialize() { /* ... */ }
    public void Tick() { /* ... */ }
}
// Container.BindInterfacesAndSelfTo<WaveSpawner>().AsSingle(); // + entry-point wiring

// Onity
using Onity.DI;

public sealed class WaveSpawner : IOnityInitializable, IOnityTickable
{
    public void Initialize() { /* runs once at Build(), in registration order */ }
    public void Tick() { /* runs every Update from the owning context */ }
}
// container.Bind<WaveSpawner>().AsSingle();          // binding is the whole wiring
// (or BindInterfacesAndSelfTo<WaveSpawner>().AsSingle() to also resolve by interface)
```

## Collection injection (replaces `ResolveAll` / hand-rolled registries)

Bind several implementations of one contract, then inject the whole set as `IReadOnlyList<T>` (or `IEnumerable<T>` / `T[]` / `List<T>`). The set is gathered from every explicit binding in this scope and its ancestors, in registration order.

```csharp
// Zenject
// Container.Bind<IDamageRule>().To<CritRule>().AsSingle();
// Container.Bind<IDamageRule>().To<ArmorRule>().AsSingle();
public DamagePipeline(List<IDamageRule> rules) { /* Zenject collects all binds */ }

// Onity
container.Bind<IDamageRule>().To<CritRule>().AsSingle();
container.Bind<IDamageRule>().To<ArmorRule>().AsSingle();

public sealed class DamagePipeline
{
    private readonly IReadOnlyList<IDamageRule> m_rules;
    public DamagePipeline(IReadOnlyList<IDamageRule> rules) { m_rules = rules; }
}
```

> A plain `Resolve<IDamageRule>()` still returns the **last** binding (last-binding-wins), so existing single-resolve call sites are unchanged; only a collection-typed request gathers all of them.

## Open-generic binds (replaces per-closed-type binding boilerplate)

Bind an open generic once and let any closed form resolve on demand:

```csharp
// container.Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle();
// container.Resolve<IRepository<Player>>();   // builds Repository<Player> on first resolve
```

The first resolve of `IRepository<Player>` builds the closed `Repository<Player>` and caches it as a normal binding, so later resolves take the fast path. On IL2CPP the closed type must survive AOT stripping (reference it statically or preserve it). `NonLazy()` is not supported on an open-generic binding — the closed type is unknown until resolve.

## Scopes / sub-containers / lifecycle

| Zenject | Onity | Notes |
| --- | --- | --- |
| Sub-container / `CreateSubContainer()` | `new OnityContainer(parent)` | A child container inherits parent bindings; a child bind shadows the parent **only inside the child**. This is Onity's "scoped". |
| `GameObjectContext` / sub-container facade | child `OnityContainer` (no facade type) | No `FromSubContainerResolve` / facade binding. Compose with a plain child container. |
| `MonoInstaller.InstallBindings()` | `MonoInstaller.InstallBindings(OnityContainer container)` (`Onity.Unity.Installers`) | Same idea; you receive the `OnityContainer` to bind into. |
| `ProjectContext` / `SceneContext` | `ProjectContext` / `SceneContext` / `GameObjectContext` (`Onity.Unity.Contexts`) | Context creates the container, registers defaults (container, `IResolver`, `MessageBroker`, `OnityEventHub`), runs installers, builds, auto-injects. |
| `IInitializable.Initialize()` | `IOnityInitializable.Initialize()` (`Onity.DI`) | Automatic, like Zenject: bind a singleton/instance implementing it and `Build()` calls `Initialize()` once, in binding-registration order. No entry-point registration. (A `RegisterBuildCallback(r => …)`, an `[Inject]` method, or `NonLazy()` are still available for ad-hoc startup work.) |
| `ITickable.Tick()` | `IOnityTickable.Tick()` (`Onity.DI`) | Automatic: bind a singleton/instance implementing it and the owning Unity context pumps it every `Update`. Binding the type is enough — no registration. **Transients are not ticked** (no single stable instance). |
| `IFixedTickable.FixedTick()` / `ILateTickable.LateTick()` | `IOnityFixedTickable.FixedTick()` / `IOnityLateTickable.LateTick()` | Pumped from the context's `FixedUpdate` / `LateUpdate`. Same automatic, singleton-only rule. |
| Tick a plain stream / non-singleton per frame | `OnityUnityObservable.EveryUpdate().Subscribe(...).AddTo(this)` (`Onity.Unity.Reactive`) | For per-frame work that is not a bound singleton, subscribe the frame loop directly. |
| `IDisposable` / `OnDestroy` cleanup | `container.Dispose()` | Disposes owned singletons (including lifecycle singletons) in reverse registration order. |
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
| Unbind / Rebind API | Conflicts with the "no bindings after Build" rule and the cached resolution model. | Rely on last-binding-wins to override a binding before `Build()`. |
| `IInitializable`/`ITickable` auto-tick **inside the `Onity.DI` core** | Per-frame dispatch in the engine-free DI core would couple it to the Unity loop. | The interfaces themselves **are supported** (`IOnityInitializable`/`IOnityTickable`/…) — they live in `Onity.DI` but are pumped by the Unity **context**, not by the core in isolation. See the lifecycle section above. |

> **Now supported (no longer non-goals):** **collection injection** and **open-generic binds** ship today — see the dedicated sections above. The roadmap (`docs/Plan/07-Competitive-And-AI-Roadmap.md`) marks both as *Adopt*, and they are implemented in the shipped `Onity.DI`.
