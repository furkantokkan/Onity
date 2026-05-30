# Onity

**One Unity package for dependency injection, reactive programming, and events — one idiom, one disposal model, an engine-free core, and hot paths designed to avoid per-call managed allocation.**

[![Onity CI](https://github.com/furkantokkan/Onity/actions/workflows/onity-ci.yml/badge.svg)](.github/workflows/onity-ci.yml)
![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)
![EditMode tests green](https://img.shields.io/badge/EditMode%20tests-green-brightgreen)
![IL2CPP validated](https://img.shields.io/badge/IL2CPP-validated-blue)
![License MIT](https://img.shields.io/badge/license-MIT-green)

---

## Why Onity

A typical Unity project bolts together four assets to ship gameplay: a DI container (Zenject or VContainer), a reactive library (R3 or UniRx), a message bus (MessagePipe), and async/pooling helpers on top. That is four installs, four mental models, four disposal idioms, and four places for an AI agent — or a new teammate — to guess wrong.

Onity replaces all of that with **one package and one mental model**:

- **DI is the spine.** Bind services in a `MonoInstaller`; consume them through constructor injection.
- **Events ride the broker.** `IMessageBroker` and `OnityEventHub` are auto-bound in every scope — publish and subscribe with no setup line.
- **Reactive operators ride both.** `Subject<T>`, `ReactiveProperty<T>`, and `broker.Observe<T>()` are all the *same* `IOnityObservable<T>`, so `Where`/`Select`/`Subscribe` work on state and events alike.
- **Everything disposes the same way.** Every `Subscribe` returns `IDisposable`; `AddTo(this)` (Unity) or `AddTo(CompositeDisposable)` (plain C#) scopes its lifetime — across DI, events, and reactive, identically.

The runtime core (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Factory`) is **engine-free** — no `UnityEngine` dependency — so domain logic is testable in plain EditMode with no scene. The hot-path machinery (resolve via compiled activators, pooled argument arrays, and cached construction plans; publish, `OnNext`, `EveryUpdate`, subscription steady state) is **designed to avoid per-call managed allocation** — though a transient resolve still allocates the instance it returns, and the published allocation figures were unreliable and are being re-measured (see [Benchmarks](#benchmarks)). The core uses no `System.Linq`; **ZLinq is the only third-party runtime dependency** (used by the `Onity.Unity` layer). The Zenject-familiar `Bind<T>().To<C>().AsSingle()` vocabulary, fluent discoverable builders, a verified [machine-readable usage guide](docs/Onity-AI-Usage-Guide.md), and a [Roslyn analyzer pack](tools/Onity.Analyzers) (`ONITY001`–`ONITY006`) make it **AI-friendly** by design: an agent reading one guide writes correct, compiling code across all three pillars, and the analyzer turns common misuse into inline diagnostics.

The DI fast path compiles constructor activators and member setters with `Expression.Compile` on JIT runtimes (Editor, Mono players) and **automatically falls back to reflection on AOT/IL2CPP**, where dynamic code generation is unavailable — so the same container runs on both. This fallback was verified all-green on an IL2CPP build.

---

## Features

### DI — `Onity.DI` (replaces Zenject / VContainer)

- Fluent binding: `Bind<T>().To<C>().AsSingle()` / `.AsTransient()` / `.NonLazy()`, plus self-bind shorthand `Bind<T>().AsSingle()`.
- `BindInterfacesAndSelfTo<T>()` / `BindInterfacesTo<T>()` to share one instance across a concrete and all its interfaces.
- `BindInstance<T>(instance)` for pre-built objects; `BindFactory<...>()` (0/1/2-parameter variants) for runtime-argument construction via `IFactory<...>`.
- **Automatic entry-point lifecycle** — implement `IOnityInitializable` / `IOnityTickable` / `IOnityFixedTickable` / `IOnityLateTickable` on a bound singleton and the container wires it up: no manual entry-point registration (unlike VContainer). `Initialize()` runs at the end of `Build()`; the Unity context pumps `Tick` / `FixedTick` / `LateTick` from `Update` / `FixedUpdate` / `LateUpdate`.
- **Collection injection** — bind a contract several times and inject every registration as `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IList<T>`, `ICollection<T>`, `List<T>`, or `T[]`.
- **Open-generic registration** — `Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle()`; resolving a closed `IRepository<Foo>` constructs `Repository<Foo>` on demand. (On IL2CPP the closed type must survive stripping — reference it statically or preserve it.)
- `[Inject]` on constructor, field, property, or method — constructor injection preferred; greediest public constructor (or a single `[Inject]` ctor) wins.
- `Resolve<T>()` / `TryResolve<T>(out T)` / `Inject(existing)` / `CanResolve(type)`.
- Child containers as Onity's "scoped" lifetime — a child bind shadows the parent only inside the child.
- `RegisterBuildCallback` / `RegisterBuildCallbackAsync`, then `Build()` / `await BuildAsync(ct)` for sync and async startup.
- Engine-free and testable without a scene: `using OnityContainer c = new OnityContainer();`.
- **IL2CPP-validated**: a compiled `Expression.Compile` fast path on JIT, with an automatic reflection fallback on AOT/IL2CPP detected by a one-time probe — verified all-green on an IL2CPP build.
- Actionable, fix-oriented errors: `OnityResolveException`, `OnityBindingException`; opt-in binding-source attribution and diagnostics.
- A [Roslyn analyzer pack](tools/Onity.Analyzers) (`ONITY001`–`ONITY006`) catches resolve-in-`Update`, register-after-`Build`, dropped subscriptions, multiple `[Inject]` constructors, invalid `[Inject]` members, and manual `new` on a container-managed type.

### Reactive — `Onity.Reactive` (replaces R3 / UniRx)

- `Subject<T>` (steady-state `OnNext` designed allocation-free) and `ReactiveProperty<T>` (built-in `DistinctUntilChanged`, emits current value on subscribe, `SetValue(T) -> bool`).
- Synchronous operators: `Where`, `Select`, `DistinctUntilChanged`, `Skip`/`SkipWhile`, `Take`/`TakeWhile`, `StartWith`, `Scan`, `Pairwise`, `Merge`, `CombineLatest`, `Sample` — emit paths designed allocation-free (subscribe-time wrapper only).
- Async / time operators: `Debounce`, `ThrottleLast`, `TakeUntil(CancellationToken)` / `TakeUntil(Task)`, `SelectAwait`, `WhereAwait`, plus `FirstAsync` / `ToTask` reactive-to-async bridges.
- Deterministic time testing via pluggable `OnityTimeProvider`.
- Unity bridges (`Onity.Unity.Reactive`): `OnityUnityObservable.EveryUpdate()` / `EveryFixedUpdate()` / `EveryLateUpdate()`, `Timer`, `Interval`; lifetime helpers `AddTo(Component)` / `TakeUntilDestroy(Component)` / `TakeUntilDisable(Behaviour)`.
- Input System reactive bridge (`Onity.Unity.Input`): `PerformedAsObservable()` / `StartedAsObservable()` / `CanceledAsObservable()` and long-press observables.

### Events — `Onity.Messaging` (replaces MessagePipe and the UniRx `MessageBroker`)

- Typed pub/sub: `IPublisher<T>` / `ISubscriber<T>` from `IMessageBroker`; steady-state `Publish` designed allocation-free, re-entrancy-safe (unsubscribe inside a handler is OK).
- `OnityEventHub` facade — `Publish<T>` / `Subscribe<T>` / `Observe<T>()` — **auto-bound in every scope**, no installer line required.
- `broker.Observe<T>()` returns `IOnityObservable<T>`, so any event flows into the full reactive operator chain — no hand-written adapter.
- Allocation-free diagnostics: `GetDiagnostics(List<...>)` and `ChannelCount` built into the core type.
- `BindMessageChannel<T>()` when you want to inject a typed `IPublisher<T>` / `ISubscriber<T>` directly.

---

## Onity vs Zenject / VContainer / R3 / MessagePipe

| Concern | Zenject + VContainer + R3 + MessagePipe (4 libs) | Onity (1 package) |
| --- | --- | --- |
| Installs / mental models | 4 packages, 4 idioms | One `Onity.*` stack, one container spine |
| DI → Events wiring | manual `AddMessagePipe()` + binds | `IMessageBroker` + `OnityEventHub` **auto-bound** per scope |
| Events → reactive stream | hand-write a `MessagePipe → R3` adapter | `broker.Observe<T>()` returns `IOnityObservable<T>` |
| Observable type | R3 `Observable<T>`; events need bridging | one `IOnityObservable<T>` for subjects, properties, and events |
| Disposal | 3+ different disposal idioms | one `IDisposable` + `AddTo(...)` everywhere |
| DI resolve / build speed | baseline | faster than VContainer and Zenject on the measured paths below (Editor-Mono, one machine — indicative, not guaranteed) |
| Hot-path allocation (steady state) | varies | resolve machinery designed allocation-free (a transient still allocates the returned instance; alloc figures pending a corrected re-measure) |
| Entry-point lifecycle | automatic (Zenject); manual wiring (VContainer) | **automatic** — `IOnityTickable` etc. need no registration |
| Collection / open-generic binds | yes (both) | **yes** — `IEnumerable<T>`…`T[]` and `Bind(typeof(IRepo<>))` |
| IL2CPP / AOT | mature, broadly shipped | compiled fast path + automatic reflection fallback; validated on one IL2CPP build |
| Engine-free, scene-free testing | no (Zenject); partial (VContainer) | **yes** — `new OnityContainer()` in EditMode |
| Compile-time analyzer | partial (Zenject validation) | **yes** — `ONITY001`–`ONITY006` with code fixes |
| Machine-readable AI usage guide | none | **yes** — verified against source |
| Production maturity / ecosystem | **mature, battle-tested, large ecosystem** | **younger** — feature-complete DI, but smaller real-world track record |

Onity's DI now covers the feature axes VContainer and Zenject are known for — collection injection, open-generic binds, and automatic entry-point lifecycle (where Onity is actually *ahead*: no manual registration). It still deliberately omits a few competitor features that fight the predictable single-model and allocation-conscious hot-path goals — e.g. no `WhenInjectedInto`/`WithId` conditional binds, no `Unbind`, no leading-edge `Throttle` (only `ThrottleLast`), no buffered/request-response messaging. Zenject and VContainer remain more **mature** and have a larger ecosystem; Onity is the younger project. See **[Onity vs VContainer / Zenject](docs/Onity-vs-VContainer-Zenject.md)** for the honest per-axis breakdown, and the [competitive roadmap](docs/Plan/07-Competitive-And-AI-Roadmap.md) for the full adopt/non-goal matrix.

---

## Benchmarks

Measured by `OnityDiBenchmarkRunner` (Unity 2022.3.62f3, Windows, **Mono editor**; 512 warmup / 8 samples / median). These numbers were **measured in the Editor on a single machine — they are indicative, not a guarantee**; your hardware, Unity version, and scripting backend will differ. On these runs Onity beat both VContainer and Zenject on every scenario:

| Scenario | Onity | VContainer | Zenject | Onity vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~152 ns | ~195 ns | ~2,326 ns | ~+28% |
| Resolve Transient | ~996 ns | ~1,421 ns | ~12,670 ns | ~+43% |
| Resolve Combined | ~1,883 ns | ~2,462 ns | ~20,392 ns | ~+31% |
| Resolve Complex (6-level) | ~37,895 ns | ~47,117 ns | ~302,383 ns | ~+24% |
| Prepare & Register Complex | ~30,085 ns | ~145,953 ns | ~191,297 ns | ~+384% |

The **timing** numbers above are the trustworthy part. The committed allocation figures were **not reliable** — the same harness reported 0 B for VContainer and Zenject too, which cannot be correct (a transient resolve allocates the instance it returns, a 6-level graph allocates roughly one object per level, and Zenject is allocation-heavy), so the measurement was not capturing gross allocations. The resolve machinery (compiled activators, a pooled argument array, cached construction plans) is **designed** to avoid per-call managed allocation beyond the constructed instances themselves, but the published alloc numbers are withdrawn pending a corrected in-editor re-measure.

The speed comes from a process-wide compiled-activator cache (`Expression.Compile` once per `ConstructorInfo`), compiled member setters, a `[ThreadStatic]` lock-free argument-array pool, and a per-plan per-slot constructor-dependency cache — with no `builder.Build()` ceremony before resolve and no engine coupling (`Onity.DI` is `noEngineReferences: true`). On AOT/IL2CPP these compiled paths automatically fall back to reflection (slower per call, but allocation-comparable and guaranteed to run). Full numbers and deltas: [`di-benchmark-summary.md`](Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md).

**The full EditMode suite runs green** (last full run: 203/203 in Unity 6.4; the suite has since grown with the IL2CPP-fallback, lifecycle, collection-injection, and open-generic tests). CI runs EditMode and PlayMode on every push — see [`.github/workflows/onity-ci.yml`](.github/workflows/onity-ci.yml).

---

## Install

> This repository is a minimal Unity project: the package itself lives at `Packages/com.onity.framework`, so you can clone-and-open it directly or consume the package in your own project. Onity targets **Unity 2022.3 LTS or newer**.
>
> **Required dependency — ZLinq.** Onity's Unity layer uses [ZLinq](https://github.com/Cysharp/ZLinq) — its only external runtime dependency (Onity itself uses no `System.Linq`). Add ZLinq to your project's `Packages/manifest.json` (e.g. via NuGetForUnity, package `ZLinq`) before importing Onity.

### Option A — UPM git URL

Add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.onity.framework": "https://github.com/furkantokkan/Onity.git?path=Packages/com.onity.framework"
  }
}
```

### Option B — embedded package (used by the Onity Example Game)

Copy or submodule the package folder into your project's `Packages/` directory as an embedded package:

```
<YourUnityProject>/
  Packages/
    com.onity.framework/        # <- contents of Packages/com.onity.framework
      package.json
      Runtime/
        Core/        Onity.Core.asmdef
        DI/          Onity.DI.asmdef
        Reactive/    Onity.Reactive.asmdef
        Messaging/   Onity.Messaging.asmdef
        Factory/     Onity.Factory.asmdef
        Pooling/     Onity.Pooling.asmdef
        Unity/       Onity.Unity.asmdef
        DOTS/        Onity.DOTS.asmdef
      Editor/
      Tests/
      Samples/
```

Reference the assemblies you need from your own asmdef (`Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Unity`, …). The engine-free core assemblies (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Factory`) carry no `UnityEngine` reference, so plain-C# domain assemblies can depend on them directly.

---

## Quick Start

One `MonoInstaller` binds a service, shared reactive state, a typed message channel, and a plain-C# game loop that ticks itself. A thin `MonoBehaviour` consumes the rest through the auto-bound `OnityEventHub`. Every snippet uses the real shipped API.

```csharp
using Onity.DI;
using Onity.Reactive;
using Onity.Unity.Installers;   // MonoInstaller
using Onity.Unity.Messaging;    // BindMessageChannel<T>, OnityEventHub

public readonly struct PlayerDamaged
{
    public readonly int Amount;
    public PlayerDamaged(int amount) { Amount = amount; }
}

// A plain-C# service the container resolves, initializes, and ticks for you —
// no MonoBehaviour, no manual entry-point registration.
public sealed class GameClock : IOnityInitializable, IOnityTickable
{
    private readonly IScoreService m_score;   // constructor injection
    public GameClock(IScoreService score) { m_score = score; }

    public void Initialize() { /* runs once at the end of Build() */ }
    public void Tick() { /* runs every frame from the context's Update */ }
}

public sealed class GameInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.Bind<IScoreService>().To<ScoreService>().AsSingle();   // a service
        container.BindInterfacesAndSelfTo<GameClock>().AsSingle();       // ticks automatically
        container.BindInstance(new ReactiveProperty<int>(100));          // shared reactive state
        container.BindMessageChannel<PlayerDamaged>();                   // a typed event channel
        // IMessageBroker + OnityEventHub are auto-bound — no line needed.
    }
}

public sealed class HealthHud : MonoBehaviour
{
    [Inject] private OnityEventHub m_events;           // auto-bound facade
    [Inject] private ReactiveProperty<int> m_health;   // shared state from the installer

    private void OnEnable()
    {
        m_health.Subscribe(value => Debug.Log($"Health: {value}")).AddTo(this);   // emits current value first

        m_events.Observe<PlayerDamaged>()                 // event -> reactive stream
                .Where(e => e.Amount > 0)
                .Subscribe(e => m_health.Value -= e.Amount)
                .AddTo(this);                              // disposed on Destroy
    }
}
```

Wire it in the scene: add a context (`ProjectContext` / `SceneContext` / `GameObjectContext` from `Onity.Unity.Contexts`), assign `GameInstaller` to its installer list, and place `HealthHud` under the context root. The context creates the container, registers default bindings, runs installers, builds, and auto-injects the hierarchy.

For the complete, source-verified API across all three pillars, read the [Onity AI Usage Guide](docs/Onity-AI-Usage-Guide.md).

---

## Documentation

- **[Onity AI Usage Guide](docs/Onity-AI-Usage-Guide.md)** — the source-of-truth, machine-readable reference for the real public API across DI, Reactive, and Events. Read this first.
- **[Getting Started](docs/Getting-Started.md)** — a step-by-step human walkthrough: install, your first installer, DI + reactive state + events, and common mistakes.
- **[Architecture review](docs/Architecture-Review.md)** — module/dependency map, SOLID assessment, and the PASS verdict on the structure.
- **Sample — Coin Rush** (`Packages/com.onity.framework/Samples/OnityShowcase/`) — a runnable mini-game wiring DI + Reactive + Events with thin MonoBehaviours; see its folder README for scene setup.
- **Migration guides** — moving an existing project over:
  - [From Zenject](docs/Migration/From-Zenject.md)
  - [From VContainer](docs/Migration/From-VContainer.md)
  - [From R3 / UniRx](docs/Migration/From-R3.md)
- **[Competitive analysis & AI roadmap](docs/Plan/07-Competitive-And-AI-Roadmap.md)** — per-pillar feature comparison vs Zenject / VContainer / R3 / MessagePipe, the adopt/non-goal matrix, and the development roadmap.
- **[Project overview & plan](docs/Plan/00-Overview.md)** — vision, goals, current state, and the implementation phases.
- **Package engineering notes** — [`Packages/com.onity.framework/ENGINEERING.md`](Packages/com.onity.framework/ENGINEERING.md).
- **DI benchmark results** — [`di-benchmark-summary.md`](Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md).

---

## Requirements

- **Unity 2022.3 LTS or newer** (the full EditMode suite is verified green on **Unity 6.4**; benchmarks were captured on 2022.3.62f3).
- **ZLinq is the only third-party runtime dependency** (used by the `Onity.Unity` layer; the core uses no `System.Linq`). The Input System reactive bridge requires `ENABLE_INPUT_SYSTEM`.
- Unity-only: standalone .NET / Godot / cross-engine runtimes are out of scope by design.

---

## License

Licensed under the **MIT License** — see [LICENSE](LICENSE).
