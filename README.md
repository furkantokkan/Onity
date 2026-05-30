# Onity Core

Core runtime, editor integration, and tests for the `Onity` package stack.

Current Unity target in this repository: `2022.3.62f3`.

## Scope

- Runtime root: `Assets/Onity/Runtime`
- Editor root: `Assets/Onity/Editor`
- Tests root: `Assets/Onity/Tests`

Core assemblies in this package:

- `Onity.Core`
- `Onity.DI`
- `Onity.Messaging`
- `Onity.Reactive`
- `Onity.Factory`
- `Onity.Pooling`
- `Onity.Unity`
- `Onity.DOTS`
- `Onity.Editor`
- `Onity.Tests.EditMode`

Split-ready optional modules are documented in:

- `Assets/Onity.Physics/README.md`
- `Assets/Onity.SkillStats/README.md`

## Core Features

- DI container with Zenject-familiar API:
  - `Bind<TContract>().To<TConcrete>().AsSingle()/AsTransient()`
  - `BindInterfacesAndSelfTo<T>()`, `BindInterfacesTo<T>()`
  - `BindInstance(instance)`, `TryResolve<T>(out T instance)`
  - Constructor, field, property, and method injection
  - `RegisterBuildCallback(...)`, `RegisterBuildCallbackAsync(...)`
- Context hierarchy:
  - `ProjectContext`, `SceneContext`, `GameObjectContext`
- Messaging:
  - `IMessageBroker`, `IPublisher<TMessage>`, `ISubscriber<TMessage>`
  - `IMessageBroker.Publish(...)`, `Subscribe(...)`
  - `OnityEventHub` with `Publish`, `Subscribe`, `Observe<TMessage>()`
- Reactive:
  - `Subject<T>`, `ReactiveProperty<T>`, `CompositeDisposable`
  - `Where`, `Select`, `FromEvent`
  - `Debounce`, `ThrottleLast`, `TakeUntil(Task/Token)`
  - `SelectAwait`, `WhereAwait`
  - `EveryUpdate`, `EveryFixedUpdate`, `EveryLateUpdate`
  - Optional thread mode for frame streams:
    - `OnityUnityThreadMode.SingleThread`
    - `OnityUnityThreadMode.JobMultiThread`
    - `OnityUnityThreadMode.BurstJobMultiThread`
    - `OnityUnityThreadMode.DotsEventDriven`
  - Task bridge (`FirstAsync`, `ToTask`)
- Async helpers:
  - `OnityAsync.DelayAsync`, `NextFrameAsync`, `NextFixedFrameAsync`
  - `OnityAsync.WhenAll`, `OnityAsync.WhenAny`
  - `CancellationTokenSource.CancelAfterSlim(...)`
  - `OnityTimeoutController`
  - `AsyncOperation.AsTask()`, `WithCancellation(...)`, direct `await`
- Pool and factory convenience:
  - `BindPooledFactory(...)`
  - `BindScriptableObject(...)`
- Scene flow helpers:
  - `OnitySceneFlow`
  - `OnitySceneLoader`
  - `OnitySceneTransitionStore`
  - `OnitySceneFlowProfile`
  - `OnitySceneFlowStateMachine`

## Quick Start

1. Create a runtime-loadable `ProjectContext` prefab:
   - `Tools/Onity/Contexts/Create ProjectContext Prefab`
2. Add `SceneContext` to gameplay scenes and assign installers.
3. Add `GameObjectContext` for local per-prefab scope when needed.
4. Use `OnitySceneFlow` + `OnitySceneInitiator` for SEP-style scene entry.
5. Optional: create and assign an `OnitySceneFlowProfile` to drive
   grouped scene routing with optional singleton `Bootstrap` / `Loading`
   scenes plus as many `Menu` and `Level` scenes as your game needs.

## Minimal Installer Example

```csharp
using Onity.DI;
using Onity.Unity.Installers;
using UnityEngine;

public sealed class GameInstaller : MonoInstaller
{
    [SerializeField] private GameConfig m_config;

    public override void InstallBindings(OnityContainer container)
    {
        container.BindScriptableObject(m_config);
        container.BindInterfacesAndSelfTo<PlayerService>().AsSingle().NonLazy();
        container.BindFactory<Projectile, ProjectileFactory>();
    }
}
```

## Minimal Consumer Example

```csharp
using Onity.DI;
using Onity.Messaging;

public sealed class PlayerService
{
    private readonly ISubscriber<PlayerDamagedMessage> m_damageStream;

    public PlayerService(ISubscriber<PlayerDamagedMessage> damageStream)
    {
        m_damageStream = damageStream;
    }
}
```

## SEP Scene Flow Example

```csharp
using Onity.Unity.SceneFlow;
using System.Threading.Tasks;
using UnityEngine;

public static class BootFlow
{
    public static Task GoToGameplayAsync(OnitySceneFlowProfile profile)
    {
        return OnitySceneFlow.TransitionAsync(
            profile,
            OnitySceneFlowStateId.Gameplay);
    }

    public static Task GoToBossLevelAsync(OnitySceneFlowProfile profile)
    {
        return OnitySceneFlow.TransitionAsync(profile, "BossLevelScene");
    }
}
```

## Diagnostics and Validation

- `Onity/Diagnostics/Monitor`
- `Onity/Diagnostics/Container Diagnostics`
- `Onity/Diagnostics/Task Tracker`
- `Onity/Diagnostics/Observable Tracker`
- `Onity/Diagnostics/Pool Monitor`
- `Onity/Diagnostics/Scene Flow Manager`
- `Tools/Onity/Validation/Validate Scene`
- `Tools/Onity/Validation/Validate All Scenes`

Task tracker parity notes:

- Stack trace capture toggle is available in `Onity/Diagnostics/Task Tracker`.
- Use stack trace capture only for leak debugging. It has extra allocation cost in editor.

## UniTask Comparison Snapshot

What is already covered in Onity:

- Unity loop-driven async waits (`NextFrameAsync`, fixed frame waits, provider-based delays).
- Unity `AsyncOperation` await support with task/cancellation bridge.
- Reactive + async bridge (`SelectAwait`, `WhereAwait`, `TakeUntil(Task/Token)`).
- Task tracking window for long-running/leaking tasks.
- Reusable timeout flow with `CancelAfterSlim` and `OnityTimeoutController`.

What remains intentionally scoped:

- Onity keeps API surface tighter than UniTask and focuses on Unity-first DI/reactive integration.
- Additional async-enumerable breadth (full LINQ-like async stream API) can be added incrementally based on usage pressure.

## Build and Test

```powershell
dotnet build Onity.Core.csproj -nologo
dotnet build Onity.DI.csproj -nologo
dotnet build Onity.Unity.csproj -nologo
dotnet build Onity.Tests.EditMode.csproj -nologo
```

EditMode tests are under:

- `Assets/Onity/Tests/EditMode/Scripts`

## Notes

- Runtime implementation is self-owned under `Assets/Onity`.
- Third-party frameworks under `Assets/ThirdParty` are reference and comparison inputs.
- Performance claims should be validated with `Assets/Onity/Benchmarks`.
