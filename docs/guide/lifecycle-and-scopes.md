---
title: "Lifecycle & Scopes"
parent: "Guides"
nav_order: 4
---

# Lifecycle & Scopes

Onity has two lifetimes (`Singleton`, `Transient`) and models everything VContainer expresses as `Lifetime.Scoped` with **child containers**. The automatic startup and per-frame lifecycle (the Zenject-style entry points) is opt-in by interface: bind a type that implements a lifecycle interface and the container wires it up — no separate registration call.

## Child containers — the "scoped" lifetime

There is no `Scoped` keyword. A per-scope instance is a child-container `AsSingle`. A child inherits the parent's bindings; a binding declared on the child **shadows** the parent only inside that child.

```csharp
using Onity.DI;

using OnityContainer parent = new OnityContainer();
parent.Bind<IDependency>().To<Dependency>().AsSingle();
parent.Build();

using OnityContainer child = new OnityContainer(parent);
child.Bind<IDependency>().To<AlternateDependency>().AsSingle();   // shadows in the child only
child.Build();

// child.Resolve<IDependency>()  -> AlternateDependency
// parent.Resolve<IDependency>() -> Dependency (unchanged)
```

Mapping from other containers:

| Other container | Onity equivalent |
| --- | --- |
| VContainer `Lifetime.Singleton` (root) | `AsSingle()` on the root container |
| VContainer `Lifetime.Scoped` | child-container `AsSingle()` (`new OnityContainer(parent)`) |
| VContainer `Lifetime.Transient` | `AsTransient()` |

Disposing a child container disposes the singletons it **owns** in reverse registration order; it does not dispose the parent.

## Disposal ownership

`Dispose()` disposes the singletons a container created, in reverse registration order. Bound instances passed in via `BindInstance` are owned by the caller — the container does not dispose them. In a Unity scene, the context disposes its container automatically on `OnDestroy`, so you rarely call `Dispose()` by hand.

## The automatic lifecycle

Implement a lifecycle interface (from `Onity.DI`) on a **singleton or bound instance**, bind it, and the owning container collects it at `Build()`. Transient bindings are not collected — there is no single stable instance to drive.

| Interface | Method | When it runs |
| --- | --- | --- |
| `IOnityInitializable` | `Initialize()` | once, at the end of `Build()`, in binding-registration order — all dependencies are resolvable |
| `IOnityTickable` | `Tick()` | once per frame, from the context's `Update` |
| `IOnityFixedTickable` | `FixedTick()` | once per physics step, from the context's `FixedUpdate` |
| `IOnityLateTickable` | `LateTick()` | once per frame after all `Tick()` work, from the context's `LateUpdate` |

```csharp
using Onity.DI;

public sealed class WaveDirector : IOnityInitializable, IOnityTickable
{
    private readonly IEnemyFactory m_factory;
    public WaveDirector(IEnemyFactory factory) { m_factory = factory; }

    public void Initialize()
    {
        // runs once after Build(); safe to resolve / wire up here
    }

    public void Tick()
    {
        // runs every frame while the owning context is alive
    }
}

// Binding it is all the registration the lifecycle needs:
container.BindInterfacesAndSelfTo<WaveDirector>().AsSingle();
```

`BindInterfacesAndSelfTo` registers the lifecycle interfaces along with the concrete type, so the same instance is both injectable and driven by the lifecycle. Outside a Unity context, drive the ticks yourself by calling `container.Tick()` / `FixedTick()` / `LateTick()`; `Build()` already runs `Initialize()`.

### `IOnityTickable` vs `EveryUpdate()`

For a singleton service that ticks for the lifetime of its scope, prefer `IOnityTickable` — it costs no subscription and is driven directly by the context. Reach for `OnityUnityObservable.EveryUpdate()` (see [Reactive](reactive.html)) when a **MonoBehaviour** wants a frame stream it can compose with operators and scope with `AddTo(this)` / `TakeUntilDisable(this)`.

## Unity contexts

A context is a `MonoBehaviour` that owns a container for a slice of the scene. On `Awake` it creates the container (discovering its parent context if any), registers the default bindings, runs the assigned installers, calls `Build()`, and — when **Auto Inject Hierarchy** is enabled — member-injects every MonoBehaviour under its root. On `Update` / `FixedUpdate` / `LateUpdate` it pumps the lifecycle ticks; on `OnDestroy` it disposes the container.

Every context auto-binds: the container, `IResolver`, the context itself, `MessageBroker` (and its interfaces), and `OnityEventHub` (and its interfaces). That is why a service can inject `OnityEventHub` or `IMessageBroker` with no installer line (see [Events & Messaging](events-messaging.html)).

| Context | Role | Notes |
| --- | --- | --- |
| `ProjectContext` | Global root that persists across scene loads | Singleton (`ProjectContext.Instance`), `DontDestroyOnLoad`; the natural parent for scene scopes. Execution order `-10000`. |
| `SceneContext` | Per-scene scope | Child of the project context when one exists. Execution order `-9800`. |
| `GameObjectContext` | Per-object subscope | A nested scope for a prefab/object subtree; child of the enclosing context. |

Serialized context fields:

- **Installers** — the `MonoInstaller[]` run in order during `Awake`.
- **Parent Context** — an explicit parent; leave null for automatic parent discovery.
- **Auto Inject Hierarchy** — member-inject all MonoBehaviours under the root on `Awake` (default on). Installers and context components are skipped.
- **Run Async Build Callbacks** — run `BuildAsync()` post-build callbacks in `Start` (default on).

### Scene wiring

```csharp
using Onity.DI;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;   // BindMessageChannel<T>
using UnityEngine;

public sealed class CombatInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.BindMessageChannel<PlayerDamaged>();
        container.BindInterfacesAndSelfTo<WaveDirector>().AsSingle();   // lifecycle: Initialize + Tick
        container.Bind<IScoreService>().To<ScoreService>().AsSingle();
    }
}
```

1. Add a context component (`ProjectContext`, `SceneContext`, or `GameObjectContext`) to a root object.
2. Assign `CombatInstaller` to the context's **Installers** list.
3. Put the consuming MonoBehaviours under the context root so they are auto-injected.

The context creates the container, registers defaults, runs the installer, builds (firing `Initialize()`), injects the hierarchy, and from then on pumps `Tick()` / `FixedTick()` / `LateTick()` each frame.

### Project vs scene context — where each installer goes

The choice of context decides a service's lifetime, so put each installer on the context whose lifetime matches the service:

- **Project-scope services go on the `ProjectContext` prefab.** Anything that must live for the whole session and survive scene loads — catalogs, save/currency/inventory, settings, RNG, the `MessageBroker`, audio, scene-flow — belongs in an installer on the auto-loaded `ProjectContext`.
- **Per-scene collaborators go on that scene's `SceneContext`** — a match's board/turn machine/combat, presentation/spawn factories, per-screen controllers. A `SceneContext` is a **child** of the project context, so a scene installer can depend on project-scope services without rebinding them; the parent chain resolves them.
- **Never put a project-scope installer on a `SceneContext`.** A `SceneContext` is created on every scene load, so its singletons are rebuilt per scene and do not persist — the session-wide instance you expected is gone after the next load.

`ProjectContext` is auto-loaded **before any scene** by `ProjectContextBootstrap` (a `[RuntimeInitializeOnLoadMethod]` that runs `BeforeSceneLoad`) from `Resources/Onity/ProjectContext` — the prefab at `Assets/Resources/Onity/ProjectContext.prefab`. It loads only when no `ProjectContext` already exists. Create the prefab once via the menu **`Onity → Contexts → Create ProjectContext Prefab`**, then add your project-scope installer(s) to its **Installers** list. A `SceneContext` discovers `ProjectContext.Instance` automatically (or you can wire its explicit **Project Context** field), so no extra parenting is needed.

```
ProjectContext (Assets/Resources/Onity/ProjectContext.prefab)
              -> Installers: [SaveInstaller, CurrencyInstaller, AudioInstaller]   // persist across scenes
SceneContext  -> Installers: [MatchInstaller, PresentationInstaller]              // per scene; resolves the project services above
```

## See also

- [Dependency Injection](dependency-injection.html) — bindings, the four injection sites, and `MonoInstaller`.
- [Events & Messaging](events-messaging.html) — the broker and `OnityEventHub` each context auto-binds.
- [Reactive](reactive.html) — `EveryUpdate()` as the MonoBehaviour-side alternative to `IOnityTickable`.
- [Migration: From VContainer](../Migration/From-VContainer.html) — scope mapping in detail.
