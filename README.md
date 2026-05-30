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

## Benchmark Snapshot

Latest published DI run: `2026-05-30T18:32:48Z`, Unity 2022.3.62f3,
Windows Editor/Mono, 512 warmup iterations, 8 measured samples, mean ns/op.
See `Benchmarks/Results/di-benchmark-summary.md` for the full report.

| Scenario | Onity Baked | VContainer | Zenject | Onity vs VContainer |
| --- | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~94 ns | ~202 ns | ~3,137 ns | ~+53% |
| Resolve Transient | ~775 ns | ~1,697 ns | ~11,681 ns | ~+54% |
| Resolve Combined | ~896 ns | ~1,712 ns | ~15,400 ns | ~+48% |
| Resolve Complex (6-level) | ~22,787 ns | ~57,995 ns | ~285,394 ns | ~+61% |
| Prepare & Register Complex | ~47,243 ns | ~135,140 ns | ~197,132 ns | ~+65% |

Timing numbers are Editor/Mono results from one machine. The committed
allocation columns are withdrawn until the allocation harness is corrected,
because the same run reported 0 B for every container. On IL2CPP, Onity uses the
safe reflection fallback instead of the Mono compiled activator path; measure a
player build before making IL2CPP speed claims.

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
