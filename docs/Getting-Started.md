---
title: "Getting Started"
nav_order: 1
---

# Getting Started with Onity

Welcome. This is the hands-on walkthrough: by the end you will have a scene that
wires up dependency injection, reacts to a player's health dropping to zero, and
broadcasts a gameplay event that another object listens to as a live stream — all
with one package and one set of idioms.

Onity replaces three libraries you might already know:

- **Dependency injection** (instead of Zenject / VContainer)
- **Reactive programming** (instead of R3 / UniRx)
- **Events / messaging** (instead of MessagePipe and the UniRx `MessageBroker`)

You do not glue three libraries together. Onity is one coherent API with one
mental model and one disposal rule. That is the whole point of this guide: show
you the single idiom for each job so you are not stitching adapters between
frameworks.

> Looking for the dense, every-signature reference instead? See
> [`Onity-AI-Usage-Guide.md`](Onity-AI-Usage-Guide.html). This page is the friendly
> first read; that page is the lookup table. Migrating from another framework?
> See [`Migration/From-Zenject.md`](Migration/From-Zenject.html),
> [`Migration/From-VContainer.md`](Migration/From-VContainer.html), and
> [`Migration/From-R3.md`](Migration/From-R3.html).

Everything below compiles against the real shipped API. Copy the blocks as-is.

---

## Before you start

Onity's namespaces split along a simple line:

- The **core** (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`,
  `Onity.Factory`) is **engine-free** — no `UnityEngine` dependency. You can unit
  test it with no scene.
- The **Unity glue** (`Onity.Unity.*`) is where the `MonoBehaviour` contexts,
  frame loops, and lifetime helpers live.

When you see a `using` like `Onity.DI` it is engine-free; `Onity.Unity.Reactive`
is the Unity bridge. You will mix both in gameplay code, and that is expected.

This guide targets Unity. Code style in the package is Allman braces with Unity
naming (`m_camelCase` instance fields, `s_camelCase` statics, `k_camelCase`
constants). Match it if you contribute to the package, but your own game code can
follow your own conventions.

---

## Step 1 — Install Onity

Onity is a UPM package (`com.onity.framework`). Pick one method.

### A. Unity Package Manager — git URL (recommended)

In Unity, open **Window → Package Manager → `+` → Add package from git URL…** and
paste the short URL:

```
https://github.com/furkantokkan/Onity.git#upm
```

The `upm` branch is the package at its repository root (auto-mirrored by CI on
every change). To pin a release instead, use the explicit form
`https://github.com/furkantokkan/Onity.git?path=Packages/com.onity.framework#v0.3.4`.
Git must be installed on your machine.

### B. Embedded package

Copy the package folder into your project's `Packages/` directory:

```
<YourProject>/Packages/com.onity.framework/
```

That folder contains the package runtime, editor tooling, tests, benchmarks, and
metadata. It intentionally does not ship bundled samples.

### No third-party dependencies

Onity has no non-Unity third-party runtime dependencies — the engine-free core
uses no `System.Linq`, and the former ZLinq dependency was removed in 0.3.1.
Unity first-party package dependencies are declared in `package.json` and
resolved by UPM.

### Referencing the assemblies

The package's runtime assemblies are auto-referenced (`Onity.Core`, `Onity.DI`,
`Onity.Reactive`, `Onity.Messaging`, `Onity.Factory`, `Onity.Unity`), so your game
scripts can call Onity without editing any `.asmdef` — you only need the right
`using` directives. If your game code lives in its own assembly definition that
turns off auto-referencing, add the assemblies you use (typically `Onity.DI`,
`Onity.Reactive`, `Onity.Messaging`, and `Onity.Unity`) to that asmdef's
references.

### Verify the install

A one-line script anywhere in your project — if it compiles, Onity is wired in:

```csharp
using Onity.DI;

public static class OnityInstallCheck
{
    public static bool Works()
    {
        using OnityContainer container = new OnityContainer();
        container.Bind<OnityInstallCheck>().AsSingle();
        return container.CanResolve(typeof(OnityInstallCheck));
    }
}
```

No compiler error means you are ready.

---

## Step 2 — Add a SceneContext and write your first installer

In Onity, a **context** is a `MonoBehaviour` that owns a dependency-injection
container for a slice of your game. The one you reach for first is
**`SceneContext`**: a per-scene container that automatically parents itself to the
global `ProjectContext` if one exists, and falls back to standing alone if not.

A context does five things in its `Awake`, in order:

1. Creates the container (parented to a discovered project/parent context).
2. Registers default bindings (the container itself, `IResolver`, the context, a
   `MessageBroker`, and an `OnityEventHub` — more on those in Steps 5–6).
3. Runs your **installers** to add your bindings.
4. Calls `Build()` to finalize the container.
5. Injects every `MonoBehaviour` in the scene hierarchy under the context root.

You do not call any of that yourself. You just create the context object, write
an installer, and drop it in the context's installer list.

**Scene setup (Editor):**

1. Create an empty GameObject in your scene, name it `SceneContext`.
2. Add the **Scene Context** component to it (`Add Component → Onity → Scene Context`,
   or search "Scene Context").
3. You will add the installer component to the same object in a moment and drag it
   into the context's **Installers** list.

> Tip: for state that must survive scene loads (audio, save service, settings),
> create a `ProjectContext` once via the menu
> `Onity → Contexts → Create ProjectContext Prefab`. A `SceneContext`
> automatically becomes its child, so scene services can resolve project-wide
> services. For this walkthrough a lone `SceneContext` is enough.

> **Project vs scene — where does an installer go?** Put session-wide services that
> must survive scene loads (catalogs, save/currency/inventory, settings, audio,
> scene-flow) in an installer on the `ProjectContext` prefab; put per-scene
> collaborators (a match's logic, presentation/spawn factories, per-screen
> controllers) on that scene's `SceneContext`. Never put a project-scope installer
> on a `SceneContext` — it is rebuilt on every scene load, so its singletons would
> not persist. See
> [Lifecycle &amp; Scopes → Project vs scene context](guide/lifecycle-and-scopes.html#project-vs-scene-context--where-each-installer-goes).

**Now write a service and an installer.** An installer is a `MonoInstaller`
subclass with one method, `InstallBindings`, where you register your types.

Here is a tiny game-clock service and an installer that binds it:

```csharp
// GameClock.cs
using UnityEngine;

public interface IGameClock
{
    float Now { get; }
}

public sealed class GameClock : IGameClock
{
    public float Now => Time.time;
}
```

```csharp
// GameInstaller.cs
using Onity.DI;
using Onity.Unity.Installers;

public sealed class GameInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        // Contract -> implementation -> lifetime.
        // The lifetime call (AsSingle/AsTransient) is REQUIRED — without it,
        // nothing is actually registered.
        container.Bind<IGameClock>().To<GameClock>().AsSingle();
    }
}
```

Add the **Game Installer** component to your `SceneContext` GameObject, then drag
that component into the context's **Installers** list in the Inspector. When you
press Play, the context builds the container and your `IGameClock` is live.

The binding vocabulary mirrors Zenject, so it should feel familiar:

```csharp
container.Bind<IGameClock>().To<GameClock>().AsSingle();        // one shared instance
container.Bind<IPathfinder>().To<AStar>().AsTransient();        // new instance each resolve
container.Bind<GameState>().AsSingle();                         // self-bind (To<> defaults to the type)
container.BindInterfacesAndSelfTo<PlayerService>().AsSingle();  // share ONE instance across all its interfaces + the class
container.BindInstance<IConfig>(loadedConfig);                  // a pre-built instance you already have
```

> One trap worth knowing now: two separate `Bind<IFoo>().To<C>()` and
> `Bind<IBar>().To<C>()` calls produce **two different** `C` instances. To share a
> single instance across a class and all its interfaces, use
> `BindInterfacesAndSelfTo<C>().AsSingle()`.

---

## Step 3 — Consume the service by constructor injection

Now that `IGameClock` is bound, anything the container builds can simply ask for
it in its constructor. **Constructor injection is the preferred style** — your
class declares what it needs, and the container supplies it. No service locator,
no static singletons.

```csharp
// ScoreService.cs
using Onity.DI;

public sealed class ScoreService
{
    private readonly IGameClock m_clock;
    private int m_score;

    // The container sees this constructor and supplies IGameClock automatically.
    public ScoreService(IGameClock clock)
    {
        m_clock = clock;
    }

    public void Add(int points)
    {
        m_score += points;
        // m_clock.Now is available because the clock was injected.
    }
}
```

Bind it alongside the clock so the container knows how to build it:

```csharp
public override void InstallBindings(OnityContainer container)
{
    container.Bind<IGameClock>().To<GameClock>().AsSingle();
    container.Bind<ScoreService>().AsSingle();   // ScoreService(IGameClock) is wired for you
}
```

**MonoBehaviours are different**: Unity creates them, not the container, so they
cannot use constructor injection. For a `MonoBehaviour`, mark the dependency with
`[Inject]` on a field (or property/method) and let the context inject it. Because
`SceneContext` auto-injects the whole hierarchy under it during `Awake`, any
`MonoBehaviour` placed under the context just works:

```csharp
// ScoreHud.cs
using Onity.DI;
using UnityEngine;

public sealed class ScoreHud : MonoBehaviour
{
    [Inject] private ScoreService m_score;   // private field injection is fine

    private void Start()
    {
        // m_score is already populated by the SceneContext before Start runs.
        m_score.Add(10);
    }
}
```

Put `ScoreHud` on any GameObject **under the SceneContext root** in the hierarchy
and it gets injected automatically. If you ever create an object at runtime that
the context did not inject, you can inject it manually with
`container.Inject(thatObject)` (resolve the context's `Container` first, or inject
an `IResolver`).

> Prefer constructor injection for plain C# classes. Use `[Inject]` only where the
> engine owns construction (MonoBehaviours, ScriptableObjects). Field, property
> (needs a setter), and method `[Inject]` are all supported; constructor is still
> the cleanest.

---

## Step 4 — Add reactive state and react to "health reached zero"

Gameplay is full of values that change over time and that several systems care
about: health, score, ammo, wave number. Onity models these with
**`ReactiveProperty<T>`** — a value you can read and write like a normal field,
but that also notifies subscribers when it changes.

Two things make `ReactiveProperty<T>` the right tool for *current state*:

- Subscribing **emits the current value immediately**, then every real change
  after. A health bar that subscribes late still gets the current health at once.
- It has **`DistinctUntilChanged` built in** — setting the same value twice does
  not fire a duplicate notification.

The classic example is "when health hits zero, die." You filter the stream with
`Where(...)` and subscribe. Crucially, **every `Subscribe` returns an
`IDisposable`, and you must tie it to a lifetime** — in a `MonoBehaviour` that is
`.AddTo(this)`, which disposes the subscription automatically when the object is
destroyed.

```csharp
// Health.cs
using Onity.Reactive;          // ReactiveProperty, Where, Subscribe
using Onity.Unity.Reactive;    // AddTo(Component)
using UnityEngine;

public sealed class Health : MonoBehaviour
{
    private readonly ReactiveProperty<int> m_hp = new ReactiveProperty<int>(100);

    // Expose state read-only so other systems can observe but not mutate it.
    public IReadOnlyReactiveProperty<int> Hp => m_hp;

    private void Start()
    {
        m_hp.Where(value => value <= 0)
            .Subscribe(_ => Die())
            .AddTo(this);           // disposed automatically on Destroy — no leak
    }

    public void TakeDamage(int amount)
    {
        // SetValue updates and notifies only if the value actually changed.
        m_hp.SetValue(m_hp.Value - amount);
    }

    private void Die()
    {
        Debug.Log("Player died");
        // play death animation, disable input, etc.
    }
}
```

The same operators that work on a `ReactiveProperty<T>` work on any Onity stream —
`Where`, `Select`, `DistinctUntilChanged`, `Scan`, `Merge`, `CombineLatest`,
`Pairwise`, and more. That uniformity is deliberate: state and events share one
operator vocabulary.

Onity also gives you Unity-driven streams. For example, a per-frame loop without
writing `Update()`:

```csharp
using Onity.Reactive;          // Subscribe
using Onity.Unity.Reactive;    // OnityUnityObservable, AddTo
using UnityEngine;

public sealed class RegenTicker : MonoBehaviour
{
    [SerializeField] private Health m_health;

    private void OnEnable()
    {
        // EveryUpdate() emits once per frame. AddTo(this) cleans it up on Destroy.
        OnityUnityObservable.EveryUpdate()
            .Subscribe(_ => { /* slowly regenerate, tween a bar, etc. */ })
            .AddTo(this);
    }
}
```

> `AddTo`, `TakeUntilDestroy`, and `TakeUntilDisable` extend `IDisposable`, so they
> go **after** `Subscribe` (which returns the disposable), never on the observable
> itself. Use `TakeUntilDisable(this)` instead of `AddTo(this)` when you want the
> subscription to stop on disable rather than destroy.

---

## Step 5 — Send and receive a gameplay event

State is for "what the value is right now." **Events** are for "something just
happened" — a fire-and-forget notification that any number of decoupled listeners
can pick up: `PlayerDamaged`, `EnemyKilled`, `LevelLoaded`. The sender does not
know or care who is listening.

Onity's front door for events is **`OnityEventHub`**, and here is the nice part:
**every context auto-binds an `OnityEventHub` (and the underlying
`MessageBroker`)**. You do not write a single installer line for it — just inject
it and go.

Define an event as a small struct, then publish it from one system:

```csharp
// PlayerDamaged.cs
public readonly struct PlayerDamaged
{
    public readonly int Amount;
    public readonly bool IsCritical;

    public PlayerDamaged(int amount, bool isCritical)
    {
        Amount = amount;
        IsCritical = isCritical;
    }
}
```

```csharp
// CombatSystem.cs — a plain C# service that broadcasts the event
using Onity.Unity.Messaging;   // OnityEventHub

public sealed class CombatSystem
{
    private readonly OnityEventHub m_events;

    // OnityEventHub is auto-bound by the context — constructor injection just works.
    public CombatSystem(OnityEventHub events)
    {
        m_events = events;
    }

    public void ApplyHit(int amount, bool isCritical)
    {
        m_events.Publish(new PlayerDamaged(amount, isCritical));
    }
}
```

Now receive it. A `MonoBehaviour` listener injects the same hub and subscribes.
Subscriptions return an `IDisposable`, so own the lifetime — here we use a
`CompositeDisposable` cleared in `OnDisable`, which is the right pattern when you
subscribe in `OnEnable`:

```csharp
// DamageFlash.cs
using Onity.DI;                // Inject
using Onity.Reactive;          // CompositeDisposable
using Onity.Unity.Messaging;   // OnityEventHub
using Onity.Unity.Reactive;    // AddTo(CompositeDisposable)
using UnityEngine;

public sealed class DamageFlash : MonoBehaviour
{
    [Inject] private OnityEventHub m_events;
    private readonly CompositeDisposable m_subscriptions = new CompositeDisposable();

    private void OnEnable()
    {
        m_events.Subscribe<PlayerDamaged>(OnDamaged).AddTo(m_subscriptions);
    }

    private void OnDisable()
    {
        m_subscriptions.Clear();   // drop subscriptions while disabled; reusable on next enable
    }

    private void OnDamaged(PlayerDamaged message)
    {
        Debug.Log($"Flash! took {message.Amount}");
    }
}
```

Bind `CombatSystem` in your installer (the hub itself needs no binding):

```csharp
public override void InstallBindings(OnityContainer container)
{
    container.Bind<IGameClock>().To<GameClock>().AsSingle();
    container.Bind<ScoreService>().AsSingle();
    container.Bind<CombatSystem>().AsSingle();   // OnityEventHub is supplied automatically
}
```

### Treat the event as a filtered stream

Because Onity is one coherent API, an event channel is *also* an observable.
`OnityEventHub.Observe<T>()` returns the same `IOnityObservable<T>` you used for
reactive state — so you can `Where` / `Select` over events exactly like over a
`ReactiveProperty<T>`. For example, react only to critical hits:

```csharp
// CriticalHitWatcher.cs
using Onity.DI;                // Inject
using Onity.Reactive;          // Where, Subscribe
using Onity.Unity.Messaging;   // OnityEventHub
using Onity.Unity.Reactive;    // AddTo(Component)
using UnityEngine;

public sealed class CriticalHitWatcher : MonoBehaviour
{
    [Inject] private OnityEventHub m_events;

    private void Start()
    {
        m_events.Observe<PlayerDamaged>()
                .Where(evt => evt.IsCritical)
                .Subscribe(_ => Debug.Log("Critical hit!"))
                .AddTo(this);
    }
}
```

That is the same `Where(...).Subscribe(...).AddTo(this)` shape from Step 4. One
idiom, whether the source is state or an event.

> **Which should I use — event, `ReactiveProperty<T>`, or a direct method call?**
> Rule of thumb: need a return value or exactly one handler → call the injected
> service directly. Modelling *current* state a fresh subscriber must see → use a
> `ReactiveProperty<T>`. A past-tense "this happened" with unknown listeners → use
> an event via `OnityEventHub`. There is no replay/buffer on events, so a late
> subscriber misses past events by design — if you need "the latest value for a
> new listener", that is a `ReactiveProperty<T>`.

---

## You now have the whole framework

Look back at what you wrote. The shape repeats:

```csharp
// Register in an installer:        container.Bind<IThing>().To<Thing>().AsSingle();
// Consume by constructor:          public Service(IThing thing) { ... }
// Inject a MonoBehaviour:          [Inject] private Service m_service;
// Hold current state:              var hp = new ReactiveProperty<int>(100); hp.Value = 90;
// React to state:                  hp.Where(v => v <= 0).Subscribe(_ => Die()).AddTo(this);
// Send an event:                   events.Publish(new PlayerDamaged(10, true));
// Receive an event:                events.Subscribe<PlayerDamaged>(OnDamaged).AddTo(this);
// Receive it as a stream:          events.Observe<PlayerDamaged>().Where(...).Subscribe(...).AddTo(this);
```

DI is the spine. Events ride the auto-bound hub. Reactive operators ride both,
because `ReactiveProperty<T>` and `events.Observe<T>()` are the *same*
`IOnityObservable<T>`. And lifetime is one rule everywhere: `Subscribe` returns an
`IDisposable`, so `AddTo(this)` it.

---

## Common mistakes (read this once, save yourself an afternoon)

> **No `AddTo` == leak.** Every `Subscribe` returns an `IDisposable`. If you do not
> `AddTo(this)` (MonoBehaviour) or `AddTo(someCompositeDisposable)` (plain C#), the
> subscription outlives the object and leaks. This is the single most common Onity
> mistake. Tie *every* subscription to a lifetime.

> **Do not resolve before the container is built.** The `SceneContext` builds the
> container in `Awake` and injects the hierarchy before `Start`. Read injected
> dependencies in `Start` (or later), not in `Awake`/field initializers of an
> injected `MonoBehaviour`. Never add bindings after `Build()` — it throws.

> **Do not `Resolve<T>()` every frame.** Resolve once (constructor, or `[Inject]`)
> and cache the reference. Calling `Resolve` inside `Update`/`FixedUpdate` is slow
> and unnecessary — injection already handed you the instance.

> **A `Bind<>()` without a lifetime registers nothing.** You must finish with
> `.AsSingle()` or `.AsTransient()`. `container.Bind<IFoo>().To<Foo>();` on its own
> does nothing.

> **Two binds of the same class are two instances.** `Bind<IFoo>().To<C>()` plus
> `Bind<IBar>().To<C>()` gives you two `C` objects. Use
> `BindInterfacesAndSelfTo<C>().AsSingle()` to share one instance across all
> contracts.

> **`AddTo` goes after `Subscribe`, not on the observable.** `AddTo` /
> `TakeUntilDestroy` / `TakeUntilDisable` extend the `IDisposable` that `Subscribe`
> returns, so they are the last link in the chain. The lifetime overloads take a
> `Component`/`Behaviour` — pass `this` from a MonoBehaviour (there is no
> `AddTo(GameObject)`).

> **One idiom, not three.** Do not bolt R3, UniRx, MessagePipe, or another DI
> container alongside Onity to fill a perceived gap. Model current state as a
> `ReactiveProperty<T>`, transient notifications as events, and single-owner
> commands as direct service calls. The operator named for rate-limiting is
> `ThrottleLast` (there is no leading-edge `Throttle`); `Buffer`/`Zip`/`Switch` and
> keyed/buffered messaging are intentionally not shipped — reach for the
> primitives above instead.

---

## Where to go next

- **Full API reference:** [`Onity-AI-Usage-Guide.md`](Onity-AI-Usage-Guide.html) —
  every binding form, operator, and error-to-fix table.
- **Coming from another framework:**
  [`Migration/From-Zenject.md`](Migration/From-Zenject.html),
  [`Migration/From-VContainer.md`](Migration/From-VContainer.html),
  [`Migration/From-R3.md`](Migration/From-R3.html).
- **Factories with runtime arguments, child/scoped containers, async startup
  (`BuildAsync`), and the typed `IPublisher<T>`/`ISubscriber<T>` channels** are all
  covered in the AI Usage Guide once you outgrow the basics here.
