# Onity

**One Unity package for dependency injection, reactive programming, events, and async — one idiom, one lifetime and disposal model, an engine-free core, and hot paths designed to avoid per-call managed allocation.**

[![Onity CI](https://github.com/furkantokkan/Onity/actions/workflows/onity-ci.yml/badge.svg)](https://github.com/furkantokkan/Onity/actions/workflows/onity-ci.yml)
![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)
![EditMode tests green](https://img.shields.io/badge/EditMode%20tests-green-brightgreen)
![IL2CPP verified](https://img.shields.io/badge/IL2CPP-verified-blue)
![DOTS / ECS bridge](https://img.shields.io/badge/DOTS%2FECS-Burst%20event%20bridge-orange)
![AI-indexed docs](https://img.shields.io/badge/docs-AI--indexed-blueviolet)
![License MIT](https://img.shields.io/badge/license-MIT-green)

**Repository Editor version:** Unity `2022.3.62f2`. The package supports Unity 2022.3 LTS or newer.

**0.6.0:** OnityTask is faster than UniTask 2.5.11 in all 29 gated scenarios of the
published IL2CPP Release Player suite (median speedups 1.2x to 12x), measured at
Onity's default context-flow setting with pool retention matched to UniTask for the
1,024- and 4,096-operation bursts. Mono, opt-in `AsyncLocal` flow and
default-retention results, including the rows where Onity is slower, are in the
[comparison guide](docs/guide/onitytask-comparison.md) and the
[gate evidence](docs/assets/benchmarks/onitytask-surpass-2026-10-02.md). OnityTask
now covers UniTask's runtime API under Onity names, apart from the
[listed gaps](docs/guide/onitytask-comparison.md#feature-coverage) (19 PlayerLoop
timings, timers, tuple `WhenAll`/`WhenAny`, triggers, UnityEvent/uGUI/UI Toolkit
events, async LINQ, channels, async reactive properties), and connects async work
to the rest of Onity in ways a standalone async library cannot: DI scope lifetime
tokens, awaited `IOnityAsyncInitializable` startup, `ReactiveProperty` waits and
message-bus receives with backpressure. Context flow is now off by default
(UniTask semantics); see the [changelog](CHANGELOG.md) for this and the other
breaking changes.

**0.5.0:** DI adds identified and conditional bindings (`WithId`,
`WhenInjectedInto<T>()`, `[Inject(Id = ...)]`), a native `AsScoped()` lifetime,
`FromSubContainerResolve`, and `Unbind` / `Rebind`. `OnityObjectPool<T>` now keeps
its own stack and checks duplicate returns in players too. OnityTask sources are
lock-free; a consumed pooled task retires its token. See the
[changelog](CHANGELOG.md) for verification and the breaking change.

**0.4.0:** OnityTask gained thread-pool work, a JobHandle bridge, explicit
PlayerLoop timing, cancellation/timeout composition, native async streams,
channels and sequential awaitable operators. See the [0.4.0 release verification and limits](docs/assets/benchmarks/onity-0.4.0-release-2026-09-27.md).

**📖 [Documentation, guides & API reference](https://furkantokkan.github.io/Onity/)**  ·  [Install](#install)  ·  [OnityTask](docs/guide/onitytask.md)  ·  [AI usage guide](docs/Onity-AI-Usage-Guide.md)  ·  [Onity vs VContainer / Zenject](docs/Onity-vs-VContainer-Zenject.md)

---

## Why Onity

A typical Unity project bolts together four or five assets to ship gameplay: a DI container (Zenject or VContainer), a reactive library (R3 or UniRx), a message bus (MessagePipe), an async library (UniTask), and pooling helpers on top. That is several installs, mental models and disposal idioms, and as many places for an AI agent — or a new teammate — to guess wrong.

Onity replaces all of that with **one package and one mental model**:

- **DI is the spine.** Bind services in a `MonoInstaller`; consume them through constructor injection.
- **Events ride the broker.** `IMessageBroker` and `OnityEventHub` are auto-bound in every scope — use `OnityEvent.Publish(...)` / `OnityEvent.Subscribe(...)` from Unity code with no setup line.
- **Reactive operators ride both.** `Subject<T>`, `ReactiveProperty<T>`, and `broker.Observe<T>()` are all the *same* `IOnityObservable<T>`, so `Where`/`Select`/`Subscribe` work on state and events alike.
- **Everything disposes the same way.** Every `Subscribe` returns `IDisposable`; `AddTo(this)` (Unity), `AddTo(CompositeDisposable)` (plain C#) or `AddTo(scope)` (a DI scope) scopes its lifetime — across DI, events, and reactive, identically.
- **Async ends with its owner.** `OnityTask` waits take the destroy token of a component or the lifetime token of the DI scope that owns a service, so async work stops before the services it uses are disposed.

The runtime core (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Factory`, `Onity.Composition`) is **engine-free** — no `UnityEngine` dependency — so domain logic is testable in plain EditMode with no scene. The hot-path machinery (resolve via compiled activators, pooled argument arrays, and cached construction plans; publish, `OnNext`, `EveryUpdate`, subscription steady state) is **designed to avoid per-call managed allocation** — though a transient resolve still allocates the instance it returns. The DI allocation figures below come from separate raw Profiler passes with positive controls. The core uses no `System.Linq`; **Onity has no non-Unity third-party runtime dependencies**. The Zenject-familiar `Bind<T>().To<C>().AsSingle()` vocabulary, fluent discoverable builders, a verified [machine-readable usage guide](docs/Onity-AI-Usage-Guide.md), and a [Roslyn analyzer pack](tools/Onity.Analyzers) (`ONITY001`–`ONITY006`) make it **AI-friendly** by design: an agent reading one guide writes correct, compiling code across all three pillars, and the analyzer turns common misuse into inline diagnostics.

The DI fast path uses source-generated constructor activators when available, compiles constructor activators and member setters with `Expression.Compile` on JIT runtimes, and **falls back to reflection when neither generated nor compiled activation is available** — so the same container runs across Editor, Mono player, and IL2CPP player builds. IL2CPP correctness and timing are covered separately because AOT backends do not behave like Editor/Mono.

---

## Features

### DI — `Onity.DI` (replaces Zenject / VContainer)

- Fluent binding: `Bind<T>().To<C>().AsSingle()` / `.AsScoped()` / `.AsTransient()` / `.NonLazy()`, plus self-bind shorthand `Bind<T>().AsSingle()`.
- `BindInterfacesAndSelfTo<T>()` / `BindInterfacesTo<T>()` to share one instance across a concrete and all its interfaces.
- `BindInstance<T>(instance)` for pre-built objects; `BindFactory<...>()` (0/1/2-parameter variants) for runtime-argument construction via `IFactory<...>`.
- **Automatic entry-point lifecycle** — implement `IOnityInitializable` / `IOnityTickable` / `IOnityFixedTickable` / `IOnityLateTickable` on a bound singleton and the container wires it up: no manual entry-point registration (unlike VContainer). `Initialize()` runs at the end of `Build()`; the Unity context pumps `Tick` / `FixedTick` / `LateTick` from `Update` / `FixedUpdate` / `LateUpdate`.
- **Collection injection** — bind a contract several times and inject every registration as `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IList<T>`, `ICollection<T>`, `List<T>`, or `T[]`.
- **Open-generic registration** — `Bind(typeof(IRepository<>)).To(typeof(Repository<>)).AsSingle()`; resolving a closed `IRepository<Foo>` constructs `Repository<Foo>` on demand. (On IL2CPP the closed type must survive stripping — reference it statically or preserve it.)
- `[Inject]` on constructor, field, property, or method — constructor injection preferred; greediest public constructor (or a single `[Inject]` ctor) wins.
- `Resolve<T>()` / `TryResolve<T>(out T)` / `Inject(existing)` / `CanResolve(type)`.
- Identified and conditional bindings with `WithId`, `WhenInjectedInto<T>()`, `[Inject(Id = ...)]`, local `Unbind` / `Rebind`, and `FromSubContainerResolve`.
- Native `AsScoped()` lifetime: one instance per resolving container, including inherited parent bindings; a child bind shadows the parent only inside the child.
- `RegisterBuildCallback` / `RegisterBuildCallbackAsync`, then `Build()` / `await BuildAsync(ct)` for sync and async startup.
- Engine-free and testable without a scene: `using OnityContainer c = new OnityContainer();`.
- **IL2CPP-safe activation**: source-generated constructor activators for AOT speed, a compiled `Expression.Compile` fast path when the runtime supports it, and a reflection fallback detected by a one-time probe.
- Actionable, fix-oriented errors: `OnityResolveException`, `OnityBindingException`; opt-in binding-source attribution and diagnostics.
- A [Roslyn analyzer pack](tools/Onity.Analyzers) (`ONITY001`–`ONITY006`) catches resolve-in-`Update`, register-after-`Build`, dropped subscriptions, multiple `[Inject]` constructors, invalid `[Inject]` members, and manual `new` on a container-managed type.

### Reactive — `Onity.Reactive` (replaces R3 / UniRx)

- `Subject<T>` (steady-state `OnNext` designed allocation-free) and `ReactiveProperty<T>` (built-in `DistinctUntilChanged`, emits current value on subscribe, `SetValue(T) -> bool`).
- Synchronous operators: `Where`, `Select`, `DistinctUntilChanged`, `Skip`/`SkipWhile`, `Take`/`TakeWhile`, `StartWith`, `Scan`, `Pairwise`, `Merge`, `CombineLatest`, `Sample` — emit paths designed allocation-free (subscribe-time wrapper only).
- Async / time operators: `Debounce`, `ThrottleLast`, `TakeUntil(CancellationToken)` / `TakeUntil(Task)`, `SelectAwait`, `WhereAwait`, plus `FirstAsync` / `ToTask` reactive-to-async bridges.
- Thread-pool scheduling for pure managed work: `ObserveOnThreadPool()` and `SelectOnThreadPool(selector, maxConcurrency)`, followed by `ObserveOnMainThread()` before touching Unity APIs.
- Deterministic time testing via pluggable `OnityTimeProvider`.
- Unity bridges (`Onity.Unity.Reactive`): `OnityUnityObservable.EveryUpdate()` / `EveryFixedUpdate()` / `EveryLateUpdate()`, `Timer`, `Interval`; lifetime helpers `AddTo(Component)` / `TakeUntilDestroy(Component)` / `TakeUntilDisable(Behaviour)`.
- Input System reactive bridge (`Onity.Unity.Input`): `PerformedAsObservable()` / `StartedAsObservable()` / `CanceledAsObservable()` and long-press observables.

### Events — `Onity.Messaging` (replaces MessagePipe and the UniRx `MessageBroker`)

- Typed pub/sub: `IPublisher<T>` / `ISubscriber<T>` from `IMessageBroker`; steady-state `Publish` designed allocation-free, re-entrancy-safe (unsubscribe inside a handler is OK).
- `OnityEvent.Publish(...)` / `OnityEvent.Subscribe(...)` / `OnityEvent.Observe<T>()` shortcuts for Unity code, backed by the auto-bound `OnityEventHub`.
- `OnityEventHub` facade — `Publish<T>` / `Subscribe<T>` / `Observe<T>()` — **auto-bound in every scope**, no installer line required for explicit DI.
- `broker.Observe<T>()` returns `IOnityObservable<T>`, so any event flows into the full reactive operator chain — no hand-written adapter.
- Allocation-free diagnostics: `GetDiagnostics(List<...>)` and `ChannelCount` built into the core type.
- `BindMessageChannel<T>()` when you want to inject a typed `IPublisher<T>` / `ISubscriber<T>` directly.

### Async — `Onity.Unity.Async` (replaces UniTask)

- `OnityTask` / `OnityTask<T>` / `OnityTaskVoid` with Onity's own pooled method builders; a suspended method's runner returns to its pool when its task is consumed. Context flow is off by default, as in UniTask, and `AsyncLocal<T>` flow is an opt-in.
- Frame waits and all of UniTask's PlayerLoop timings (plus three after-script positions), `Yield`, `Delay` / `WaitForSeconds` with scaled, unscaled or real time, `WaitUntil` / `WaitWhile`, `WaitUntilValueChanged`, `OnityPlayerLoopTimer`, `Timeout`, `CancelAfterSlim`. In Play, frame waits without a cancelable token are stateless and can be awaited by any number of consumers.
- Composition: `WhenAll` / `WhenAny` over arrays, sequences and typed tuples, left/right `WhenAny`, `WhenEach`, and `await (a, b)`.
- Unity integration: destroy tokens, lifecycle and MonoBehaviour message triggers, `UnityEvent` and UI Toolkit events, optional uGUI events, scene loading, web requests, `AsyncOperation` and asset adapters, `JobHandle`, coroutines, thread switches.
- Async streams (`IOnityAsyncEnumerable<T>`) with UniTask's async LINQ operators, `Subscribe`, `Publish`, `BindTo`, channels with backpressure and `OnityAsyncReactiveProperty<T>`.
- Integration a standalone async library cannot offer: the DI scope lifetime token (`IOnityScopeLifetime`, `AddTo(scope)`, `GetScopeCancellationToken()`), awaited `IOnityAsyncInitializable` startup and `OnityContext.WaitReadyAsync`, `ReactiveProperty.WaitAsync` / `WaitUntilAsync`, and message-bus `ReceiveAsync` / `ReceiveAllAsync` / `SubscribeQueued` with backpressure.
- [Async with OnityTask](docs/guide/onitytask.md) · [Migrating from UniTask](docs/Migration/From-UniTask.md) · [Measured comparison](docs/guide/onitytask-comparison.md)

### DOTS / ECS — `Onity.DOTS` (Burst-compiled bridge)

- A Burst-compiled `ISystem` layer bridges Onity's managed event broker into **Entities**: publish a message and a `[BurstCompile]` system drains it off an entity event queue, so managed gameplay and DOTS systems share one event model instead of a hand-written sync layer.
- DOTS-side helpers: entity pooling (`OnityDotsPoolEntityUtils`, with `IEnableableComponent` tags) and an ECS session bridge, built on `Unity.Entities` / `Unity.Burst` / `Unity.Collections` / `Unity.Mathematics`.
- The bridge activates under the `ONITY_ENTITIES` define (when `com.unity.entities` ≥ 1.0 is installed). The engine-free core (DI, Reactive, Events) carries no DOTS coupling.

### Built for AI-assisted development

Onity is deliberately structured — and its documentation is **indexed for AI** — so a coding assistant can work with it comfortably and produce correct, compiling code with minimal guesswork:

- **AI-indexed docs.** A source-verified, [machine-readable usage guide](docs/Onity-AI-Usage-Guide.md) plus a [browsable docs site](https://furkantokkan.github.io/Onity/): one place an AI agent reads to emit correct, compiling code across DI, Reactive, and Events.
- **Claude Code and Codex skills.** Use [onity-use](.agents/skills/onity-use/SKILL.md) for game integration or [onity-develop](.agents/skills/onity-develop/SKILL.md) for package work. Codex discovers them under `.agents/skills`; Claude Code loads matching `.claude/skills` entries as `/onity-use` and `/onity-develop`.
- **One idiom, one disposal model.** Far less for an agent — or a new teammate — to guess wrong than stitching four libraries with four mental models together.
- **A Roslyn analyzer (`ONITY001`–`ONITY006`)** turns the most common mistakes into inline compiler diagnostics, catching a slip at compile time rather than at runtime.
- **XML docs on every public API**, so editor/agent IntelliSense surfaces the right call and signature.

---

## Onity vs Zenject / VContainer / R3 / MessagePipe

| Concern | Zenject + VContainer + R3 + MessagePipe (4 libs) | Onity (1 package) |
| --- | --- | --- |
| Installs / mental models | 4 packages, 4 idioms | One `Onity.*` stack, one container spine |
| DI → Events wiring | manual `AddMessagePipe()` + binds | `IMessageBroker` + `OnityEventHub` **auto-bound** per scope |
| Events → reactive stream | hand-write a `MessagePipe → R3` adapter | `broker.Observe<T>()` returns `IOnityObservable<T>` |
| Observable type | R3 `Observable<T>`; events need bridging | one `IOnityObservable<T>` for subjects, properties, and events |
| Disposal | 3+ different disposal idioms | one `IDisposable` + `AddTo(...)` everywhere |
| DI resolve / build speed | baseline | faster than VContainer and Zenject on the measured Editor-Mono and Windows IL2CPP paths below (one machine — indicative, not guaranteed) |
| Hot-path allocation (steady state) | varies | raw Profiler passes measured 0 B/op for warmed singleton, keyed singleton, and scoped singleton; transient still allocates its returned instance |
| Entry-point lifecycle | automatic (Zenject); manual wiring (VContainer) | **automatic** — `IOnityTickable` etc. need no registration |
| Collection / open-generic binds | yes (both) | **yes** — `IEnumerable<T>`…`T[]` and `Bind(typeof(IRepo<>))` |
| IL2CPP / AOT | AOT-compatible DI | Source-generated constructor activators, runtime-probed compiled activation, and reflection fallback; current IL2CPP player timing beats VContainer on the measured resolve/build paths |
| Async | a fifth package (UniTask) with its own lifetime handling | `OnityTask` in the same package: faster than UniTask 2.5.11 in all 29 gated IL2CPP Release Player rows (matched pool retention for bursts; see the comparison for Mono and opt-in flow), and async waits end with DI scope tokens |
| DOTS / ECS event bridge | not built in | **yes** — Burst `ISystem`s drain the event broker into Entities |
| Engine-free, scene-free testing | no (Zenject); partial (VContainer) | **yes** — `new OnityContainer()` in EditMode |
| Compile-time analyzer | partial (Zenject validation) | **yes** — `ONITY001`–`ONITY006` with code fixes |
| Machine-readable AI usage guide | none | **yes** — verified against source |

Onity's DI covers collection and open-generic injection, conditional and identified bindings, native scoped lifetime, sub-container exports, and automatic entry-point lifecycle. Its pool layer supports prewarm, fixed capacity, and parameterized reuse. Zenject and VContainer remain more **mature** and have larger ecosystems; Onity is the younger project. See **[Onity vs VContainer / Zenject](docs/Onity-vs-VContainer-Zenject.md)** for the per-axis breakdown, and the [competitive roadmap](docs/Plan/07-Competitive-And-AI-Roadmap.md) for the full adopt/non-goal matrix.

---

## Benchmarks

Measured by `OnityDiBenchmarkRunner` / `OnityDiBenchmarkPlayerRunner` (Unity 2022.3.62f3, Windows; 512 warmup / 8 samples / mean). These numbers were measured on a single machine and are **indicative, not a guarantee**; hardware, Unity version, backend, and graph shape can change both absolute timings and relative ordering.

Editor / Mono run (`2026-09-23`, `WindowsEditor`):

| Scenario | Onity | VContainer | Zenject | Onity vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~106 ns | ~210 ns | ~2,773 ns | ~+50% |
| Resolve Transient | ~689 ns | ~1,872 ns | ~11,785 ns | ~+63% |
| Resolve Combined | ~547 ns | ~1,886 ns | ~13,900 ns | ~+71% |
| Resolve Complex (6-level) | ~12,465 ns | ~37,633 ns | ~272,418 ns | ~+67% |
| Prepare & Register Complex | ~53,284 ns | ~153,133 ns | ~183,996 ns | ~+65% |
| Resolve Keyed Singleton | ~140 ns | ~252 ns | ~2,951 ns | ~+45% |
| Resolve Scoped Singleton | ~166 ns | ~300 ns | ~2,819 ns | ~+45% |

IL2CPP release player run (`2026-09-23`, `WindowsPlayer`, `19` generated activators registered):

| Scenario | Onity | VContainer | Zenject | Onity vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~21 ns | ~91 ns | ~463 ns | ~+77% |
| Resolve Transient | ~129 ns | ~529 ns | ~2,333 ns | ~+76% |
| Resolve Combined | ~126 ns | ~588 ns | ~2,901 ns | ~+79% |
| Resolve Complex (6-level) | ~3,936 ns | ~13,078 ns | ~61,883 ns | ~+70% |
| Prepare & Register Complex | ~22,575 ns | ~40,919 ns | ~63,623 ns | ~+45% |
| Resolve Keyed Singleton | ~31 ns | ~86 ns | ~574 ns | ~+64% |
| Resolve Scoped Singleton | ~47 ns | ~109 ns | ~563 ns | ~+57% |

On Mono/JIT, the speed comes from a process-wide compiled-activator cache (`Expression.Compile` once per `ConstructorInfo`), compiled member setters, a `[ThreadStatic]` lock-free argument-array pool, and a per-plan per-slot constructor-dependency cache. On IL2CPP, generated activators register direct constructor delegates before construction plans are built. The [September 23 timing and allocation reports](docs/benchmarks/advanced-di-summary-2026-09-23.md) support the tables above; a [September 24 repeat](docs/benchmarks/di-speed-followup-2026-09-24.md) records fresh IL2CPP results and measurement variability.

A [2026-10-01 re-measurement](docs/benchmarks/remeasure-2026-10-01.md) on Unity 2022.3.62f2 reproduced the IL2CPP ordering in three fresh processes (Onity/VContainer 0.23x–0.57x across scenarios) and the Editor allocation event counts exactly.

Benchmark verification behind these numbers: Unity batchmode Editor DI benchmark,
Windows IL2CPP player DI benchmark, separate raw Profiler allocation passes, and
the local EditMode/PlayMode suites. GitHub runs the engine-free build on every
push; Unity jobs run when the repository Unity license secret is configured —
see [`.github/workflows/onity-ci.yml`](.github/workflows/onity-ci.yml).

Advanced DI verification (`2026-09-23`): the full Onity EditMode suite passed
`464/464`; seven DI scenarios were measured in Editor/Mono and Windows IL2CPP,
with separate raw Profiler allocation passes. See the [run summary](docs/benchmarks/advanced-di-summary-2026-09-23.md).

---

## Install

> This repository is a minimal Unity project: the package itself lives at `Packages/com.onity.framework`, so you can clone-and-open it directly or consume the package in your own project. Onity targets **Unity 2022.3 LTS or newer**.
>
> **No non-Unity third-party runtime dependencies — no NuGet/Cysharp package to install.** Onity's core uses no `System.Linq`. Unity first-party package dependencies are declared in `package.json` and resolved by UPM.

### Option A — UPM git URL (recommended)

In Unity, open **Window → Package Manager → `+` → Add package from git URL…** and paste the short URL:

```
https://github.com/furkantokkan/Onity.git#upm
```

The `upm` branch is the package at its repository root (auto-mirrored by CI on every change). The equivalent explicit form — handy for pinning a release — is:

```
https://github.com/furkantokkan/Onity.git?path=/Packages/com.onity.framework#v0.6.0
```

…or in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.onity.framework": "https://github.com/furkantokkan/Onity.git#upm"
  }
}
```

(`#upm` tracks the latest package; use the `?path=/Packages/com.onity.framework#v0.6.0` form to pin a specific release.)

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
        Composition/ Onity.Composition.asmdef
        Pooling/     Onity.Pooling.asmdef
        Unity/       Onity.Unity.asmdef
        DOTS/        Onity.DOTS.asmdef
      Editor/
      Tests/
```

Reference the assemblies you need from your own asmdef (`Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Unity`, …). The engine-free core assemblies (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Factory`, `Onity.Composition`) carry no `UnityEngine` reference, so plain-C# domain assemblies can depend on them directly.

---

## Quick Start

One `MonoInstaller` binds a service, shared reactive state, a typed message channel, and a plain-C# game loop that ticks itself. A thin `MonoBehaviour` consumes the rest through the Unity `OnityEvent.Publish` / `OnityEvent.Observe` shortcuts backed by the auto-bound `OnityEventHub`. Every snippet uses the real shipped API.

```csharp
using Onity.DI;
using Onity.Reactive;
using Onity.Unity;              // OnityEvent.Publish / Subscribe / Observe
using Onity.Unity.Installers;   // MonoInstaller
using Onity.Unity.Messaging;    // BindMessageChannel<T>
using Onity.Unity.Reactive;     // TakeUntilDisable
using UnityEngine;

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
    [Inject] private ReactiveProperty<int> m_health;   // shared state from the installer

    private void OnEnable()
    {
        m_health.Subscribe(value => Debug.Log($"Health: {value}"))
                .TakeUntilDisable(this);                       // no duplicate after re-enable

        OnityEvent.Observe<PlayerDamaged>(this)                 // event -> reactive stream
              .Where(e => e.Amount > 0)
              .Subscribe(e => m_health.Value -= e.Amount)
              .TakeUntilDisable(this);                     // disposed on Disable

        OnityEvent.Publish(new PlayerDamaged(10));
    }
}
```

Wire it in the scene: add a context (`ProjectContext` / `SceneContext` / `GameObjectContext` from `Onity.Unity.Contexts`), assign `GameInstaller` to its installer list, and place `HealthHud` under the context root. The context creates the container, registers default bindings, runs installers, builds, and auto-injects the hierarchy.

> **Two DI styles, on purpose.** `GameClock` is a plain C# service: it receives its dependencies through its **constructor** (`GameClock(IScoreService score)`), so it needs **no `[Inject]`** — binding it is enough for the container to construct it with its dependencies and run `Initialize`/`Tick` automatically. `HealthHud` uses **`[Inject]` fields** only because it is a `MonoBehaviour` — Unity instantiates those, so the container can't call a constructor and instead fills `[Inject]`-marked members when the context injects the hierarchy. Rule of thumb: **put logic in constructor-injected services (no `[Inject]`); keep MonoBehaviours thin views that `[Inject]` only what they render.** ([Getting Started](docs/Getting-Started.md) walks through both styles in depth.)

For the complete, source-verified API across all three pillars, read the [Onity AI Usage Guide](docs/Onity-AI-Usage-Guide.md).

---

## Documentation

- **[Onity AI Usage Guide](docs/Onity-AI-Usage-Guide.md)** — the source-of-truth, machine-readable reference for the real public API across DI, Reactive, and Events. Read this first.
- **[Getting Started](docs/Getting-Started.md)** — a step-by-step human walkthrough: install, your first installer, DI + reactive state + events, and common mistakes.
- **[Async with OnityTask](docs/guide/onitytask.md)** — frame waits and PlayerLoop timings, cancellation, composition, triggers and UI events, async streams, scene/web operations, DI-scoped async work, and pooled-task safety.
- **[OnityTask and UniTask comparison](docs/guide/onitytask-comparison.md)** — Release Player gate results, API coverage, and current limits.
- **[Events & Messaging](docs/guide/events-messaging.md)** — MessageBroker/MessagePipe-style publish, subscribe, typed channel, and reactive event examples.
- **[Factories & Pooling](docs/guide/factories-and-pooling.md)** — `BindFactory`, `BindPooledFactory`, `IPool<T>`, `IPoolHooks`, and prefab pool examples.
- **[Refactoring from Existing Architecture](docs/guide/refactoring-from-existing-architecture.md)** — moving from `GameManager.Instance`, Unity reference graphs, ScriptableObject config, VContainer, Zenject, or static events to Onity services.
- **[Architecture](docs/Architecture-Review.md)** — module boundaries, composition roots, activation paths, and Unity adapters.
- **Migration guides** — moving an existing project over:
  - [From Zenject](docs/Migration/From-Zenject.md)
  - [From VContainer](docs/Migration/From-VContainer.md)
  - [From R3 / UniRx](docs/Migration/From-R3.md)
  - [From UniTask](docs/Migration/From-UniTask.md)
- **Package engineering notes** — [`Packages/com.onity.framework/ENGINEERING.md`](Packages/com.onity.framework/ENGINEERING.md).
- **DI benchmark results** — [`di-benchmark-summary.md`](Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md).

---

## Requirements

- **Unity 2022.3 LTS or newer** (the current benchmark numbers were captured on Unity 2022.3.62f3, Windows Editor/Mono and Windows IL2CPP Player).
- **Onity has no non-Unity third-party runtime dependencies** (the core uses no `System.Linq`). The Input System reactive bridge requires `ENABLE_INPUT_SYSTEM`.
- Unity-only: standalone .NET / Godot / cross-engine runtimes are out of scope by design.

---

## Credits

Onity is inspired by the libraries it sets out to unify and improve on:

- **[Zenject / Extenject](https://github.com/modesttree/Zenject)** — the DI binding vocabulary (`Bind<T>().To<C>().AsSingle()`) and the automatic entry-point lifecycle.
- **[VContainer](https://github.com/hadashiA/VContainer)** — the DI performance focus and the container diagnostics window.
- **[R3](https://github.com/Cysharp/R3) / [UniRx](https://github.com/neuecc/UniRx)** — the reactive model (operator chains, `Subject`, `ReactiveProperty`) and UniRx's `MessageBroker`.
- **[MessagePipe](https://github.com/Cysharp/MessagePipe)** — the typed, DI-native pub/sub broker.
- **[UniTask](https://github.com/Cysharp/UniTask)** — the PlayerLoop-driven async helper patterns.

Onity has no non-Unity third-party runtime dependencies.

## Author

**Furkan Tokkan** — [@furkantokkan](https://github.com/furkantokkan)

## License

**MIT** — see [LICENSE](LICENSE).
