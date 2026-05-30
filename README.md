# Onity Core

Core runtime, editor integration, and tests for the `Onity` package stack.

Current Unity target in this repository: `2022.3.62f3`.

## Scope

- Runtime root: `Runtime`
- Editor root: `Editor`
- Tests root: `Tests`
- Benchmarks root: `Benchmarks`

In this development repository the package is mirrored at
`Assets/Onity-Packages/Onity`. On the published `upm` branch these paths are at
the package root.

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
  - `ObserveOnThreadPool`, `SelectOnThreadPool`
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
- Reactive thread-pool hops for pure managed work (`ObserveOnThreadPool`,
  `SelectOnThreadPool`) with `ObserveOnMainThread()` as the Unity API return hop.
- Task tracking window for long-running/leaking tasks.
- Reusable timeout flow with `CancelAfterSlim` and `OnityTimeoutController`.

What remains intentionally scoped:

- Onity keeps API surface tighter than UniTask and focuses on Unity-first DI/reactive integration.
- Additional async-enumerable breadth (full LINQ-like async stream API) can be added incrementally based on usage pressure.

## Benchmark Snapshot

Latest published DI runs: Unity 2022.3.62f3, Windows, 512 warmup iterations,
8 measured samples, mean ns/op. See `Benchmarks/Results/di-benchmark-summary.md`
and `Benchmarks/Results/di-benchmark-player-latest.md` for full reports.

Editor / Mono (`2026-05-30T19:38:06Z`):

| Scenario | Onity Baked | VContainer | Zenject | Onity vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~63 ns | ~214 ns | ~2,866 ns | ~+71% |
| Resolve Transient | ~1,083 ns | ~1,879 ns | ~12,356 ns | ~+42% |
| Resolve Combined | ~972 ns | ~2,079 ns | ~17,248 ns | ~+53% |
| Resolve Complex (6-level) | ~22,905 ns | ~42,158 ns | ~289,823 ns | ~+46% |
| Prepare & Register Complex | ~61,044 ns | ~150,730 ns | ~215,537 ns | ~+60% |

Windows IL2CPP Player (`2026-05-30T20:09:24Z`):

| Scenario | Onity Baked | VContainer | Zenject | Result |
| --- | ---: | ---: | ---: | --- |
| Resolve Singleton | ~17 ns | ~86 ns | ~469 ns | Onity faster |
| Resolve Transient | ~1,431 ns | ~580 ns | ~2,458 ns | VContainer faster |
| Resolve Combined | ~1,263 ns | ~602 ns | ~3,525 ns | VContainer faster |
| Resolve Complex (6-level) | ~34,729 ns | ~12,918 ns | ~62,689 ns | VContainer faster |
| Prepare & Register Complex | ~23,872 ns | ~38,465 ns | ~61,060 ns | Onity faster |

Timing numbers are from one machine and are indicative, not a guarantee. The
committed allocation columns are withdrawn until the allocation harness is
corrected, because the earlier Editor harness reported 0 B for every container.
Onity is ahead on every measured Editor/Mono timing path, but the current IL2CPP
player run shows VContainer ahead on transient, combined, and complex resolve
paths. A source-generated/AOT-specialized activator is required before Onity can
claim an IL2CPP speed lead across every scenario.

## Build and Test

```powershell
dotnet build Onity.Core.csproj -nologo
dotnet build Onity.DI.csproj -nologo
dotnet build Onity.Unity.csproj -nologo
dotnet build Onity.Tests.EditMode.csproj -nologo
```

EditMode tests are under:

- `Tests/EditMode/Scripts`

## Notes

- Runtime implementation is self-owned under `Runtime`.
- Third-party frameworks in the development repository are reference and
  comparison inputs only.
- Performance claims should be validated with `Benchmarks`.
