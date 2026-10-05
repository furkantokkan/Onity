# Onity

Onity (`com.onity.framework`) is a Unity package that puts dependency injection, reactive state, typed
messaging, async (`OnityTask`) and factories with pooling in one package with one lifetime model: a scope
owns its services, subscriptions, tasks and pools, and disposes them together. The core is engine-free and
has no non-Unity third-party runtime dependency. Unity 2022.3 LTS or newer; this is version 0.8.2.

This file describes what is in the installed package. The documentation site is
[furkantokkan.github.io/Onity](https://furkantokkan.github.io/Onity/), and the same pages ship inside this
package under `Documentation~/`.

## For AI coding assistants

- Start with [`AGENTS.md`](AGENTS.md) (Claude Code reaches it through `CLAUDE.md`), then
  `Documentation~/AI/Onity-AI-Usage-Guide.md` for the rules, the canonical scene, the API index and the
  error table, and the feature guides under `Documentation~/guide/`. [`llms.txt`](llms.txt) lists every
  shipped page.
- `Documentation~/` is a generated copy of the repository's `docs/` folder. It ships inside the package,
  so it is available after a Git or registry install, and Unity ignores it.
- To give your project's assistants the `onity-use` skill and the usage guide, run
  `Onity/AI/Install Assistant Guidance...` in the Unity Editor. Nothing is written until you confirm;
  `Onity/AI/Check Assistant Guidance` reports whether the installed copy matches this package version.

## Assemblies

| Assembly | Engine-free | Contents |
| --- | --- | --- |
| `Onity.Core` | yes | `Unit`, `Lifetime`, `DisposableAction` |
| `Onity.Factory` | yes | `IFactory<...>` |
| `Onity.DI` | yes | `OnityContainer`, bindings, `[Inject]`, lifecycle interfaces, `IOnityScopeLifetime` |
| `Onity.Messaging` | yes | `IMessageBroker`, `MessageChannel<T>`, keyed and async channels |
| `Onity.Reactive` | yes | `IOnityObservable<T>`, `Subject<T>`, `ReactiveProperty<T>`, operators, providers |
| `Onity.Composition` | yes | `BindReactiveProperty`, `BindSubject`, `DeclareMessage`, `DeclareAsyncMessage` |
| `Onity.Pooling` | no | `OnityObjectPool<T>`, `PrefabComponentPool<T>`, `IPool<T>`, `IPoolHooks`, `PooledFactory<...>` |
| `Onity.DOTS` | no | Burst system groups, the integer event bridge, entity pooling helpers (`com.unity.entities` 1.0+) |
| `Onity.Unity` | no | contexts, installers, `OnityEvent`, `OnityEventHub`, Unity reactive bridges, Input System bridge, `OnityTask` and the async layer, UI Toolkit bridges, physics helpers, scene flow |
| `Onity.Unity.UGUI` | no | uGUI async events; compiled only when `com.unity.ugui` is installed |

Every runtime assembly is auto-referenced. Unity assembly references are not transitive: an assembly
definition that calls `Onity.Unity.Async` extension methods also references `Onity.Reactive`, and
`Onity.Messaging` for the messaging bridges. `Editor/` holds the Editor tooling (`Onity.Editor`), `Tests/`
the EditMode and PlayMode suites, `Benchmarks/` the define-gated comparison runners, and `Analyzers/` the
`Onity.Analyzers.dll` (`ONITY001` to `ONITY006`) and `Onity.SourceGen.dll` that ship with the package.

## Quick start

An installer binds the scene's services; a plain C# service receives them through its constructor. This is
the installer and the service of the canonical scene from `Documentation~/Getting-Started.md`, which also
shows the `HealthHud`, the pooled `HitMarker`, the `DamageZone` and a test that runs the service without a
scene:

```csharp
using Onity.Composition;        // BindReactiveProperty
using Onity.DI;                 // OnityContainer
using Onity.Unity.Installers;   // MonoInstaller, BindPooledFactory
using Onity.Unity.Messaging;    // BindMessageChannel
using UnityEngine;

public sealed class GameInstaller : MonoInstaller
{
    [SerializeField] private HitMarker m_hitMarkerPrefab;
    [SerializeField] private Transform m_hitMarkerRoot;

    public override void InstallBindings(OnityContainer container)
    {
        // ReactiveProperty<int> and IReadOnlyReactiveProperty<int>; the scope disposes it.
        container.BindReactiveProperty(initialValue: 100);

        // IPublisher<PlayerDamaged> and ISubscriber<PlayerDamaged> on the scope's message broker,
        // the same channel OnityEvent and OnityEventHub use.
        container.BindMessageChannel<PlayerDamaged>();

        // IFactory<HitMarker> to spawn and IPool<HitMarker> to release; the scope disposes the pool.
        container.BindPooledFactory(m_hitMarkerPrefab, m_hitMarkerRoot, defaultCapacity: 8, maxSize: 32);

        // The container constructs it, runs Initialize() at Build() and disposes it with the scope.
        container.BindInterfacesAndSelfTo<HealthService>().AsSingle();
    }
}
```

```csharp
using System;
using System.Threading;
using Onity.DI;                 // IOnityInitializable, IOnityScopeLifetime, AddTo(scope)
using Onity.Messaging;          // ISubscriber<T>
using Onity.Reactive;           // ReactiveProperty<T>
using Onity.Unity.Async;        // OnityTask, OnityTaskVoid

public sealed class HealthService : IOnityInitializable
{
    private const int k_maxHealth = 100;
    private const float k_regenerationInterval = 1f;

    private readonly ReactiveProperty<int> m_health;
    private readonly IOnityScopeLifetime m_scope;

    public HealthService(ReactiveProperty<int> health, ISubscriber<PlayerDamaged> damage, IOnityScopeLifetime scope)
    {
        m_health = health;
        m_scope = scope;
        damage.Subscribe(OnDamaged).AddTo(scope);     // unsubscribed when the scope ends
    }

    public void Initialize()
    {
        RegenerateAsync(m_scope.Token).Forget();      // the token is canceled before the scope disposes its services
    }

    private void OnDamaged(PlayerDamaged message)
    {
        m_health.SetValue(Math.Max(0, m_health.Value - message.Amount));
    }

    private async OnityTaskVoid RegenerateAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await OnityTask.Delay(k_regenerationInterval, token);
            m_health.SetValue(Math.Min(k_maxHealth, m_health.Value + 1));
        }
    }
}
```

Add a `SceneContext` to the scene (search for it in Add Component), add the installer to the same object
and to the context's Installers list, and place the MonoBehaviours under the context so it injects them in
`Awake`. Services that must outlive scene loads go on the `ProjectContext` prefab, created once with
`Onity/Contexts/Create ProjectContext Prefab`.

## Scene flow

`Onity.Unity.SceneFlow` provides `OnitySceneFlow`, `OnitySceneLoader`, `OnitySceneTransitionStore`,
`OnitySceneFlowProfile` and `OnitySceneFlowStateMachine`, with bootstrap readiness and the default UI
Toolkit loading initiators. A profile routes grouped scenes with optional singleton Bootstrap and Loading
scenes plus as many Menu, Hub and Gameplay scenes as the game needs; `Onity/Tools/Scene Flow Manager`
creates profiles from 2-, 3- and 4-stage presets or a blank template.

```csharp
using System.Threading.Tasks;
using Onity.Unity.SceneFlow;

public static class BootFlow
{
    public static Task GoToGameplayAsync(OnitySceneFlowProfile profile)
    {
        return OnitySceneFlow.TransitionAsync(profile, OnitySceneFlowStateId.Gameplay);
    }

    public static Task GoToBossLevelAsync(OnitySceneFlowProfile profile)
    {
        return OnitySceneFlow.TransitionAsync(profile, "BossLevelScene");
    }
}
```

## Diagnostics and validation

Editor windows: `Onity/Tools/Monitor`, `Onity/Tools/Container Diagnostics`, `Onity/Tools/Task Tracker`,
`Onity/Tools/Observable Tracker`, `Onity/Tools/Pool Monitor`, `Onity/Tools/Scene Flow Manager`. Commands:
`Onity/Validation/Validate Scene`, `Onity/Validation/Validate All Scenes`,
`Onity/Contexts/Create ProjectContext Prefab`. The Task Tracker's stack-trace capture is for leak
debugging only; it adds allocation in the Editor.

## Evidence

Measured with the runners in this repository on one Windows PC with Unity 2022.3.62f2; a ratio is Onity's
time divided by the other library's time in the same process, so below 1 means Onity took less time. These
are timing results, not allocation or cross-platform claims; the full tables, conditions and known slower
cases are on the documentation site's [Comparisons](https://furkantokkan.github.io/Onity/comparisons/)
pages.

- Dependency injection (Windows IL2CPP release Player, 2026-10-02, three processes): Onity Baked fastest in
  all seven scenarios in every process, Onity / VContainer 0.21 to 0.59 per process.
- Reactive (Release Players, 2026-10-02, R3 1.3.0 and UniRx 7.1.0, three processes per backend): faster
  than R3 and than UniRx in all nine IL2CPP rows (medians 0.145 to 0.805 and 0.129 to 0.872); on Mono
  faster than R3 in seven rows and than UniRx in five, with `CombineLatest` slower than both.
- Async (Release Player gate, 2026-10-02, UniTask 2.5.11, three processes per backend and suite): faster in
  all 29 gated IL2CPP rows (medians 0.085 to 0.808); on Mono faster in 25 of 29, with the four synchronous
  completed-result rows 1.09x to 1.92x slower.
- Messaging (Release Players, 2026-10-02, MessagePipe 1.8.1 as the Unity package with UniTask 2.5.10,
  three processes per backend): faster in all nine IL2CPP rows (medians 0.150 to 0.846) and in all nine
  Mono rows (0.280 to 0.756); also faster in all nine rows on both backends against MessagePipe's .NET
  build.

## Build and test

From a clone of the development repository, compile the engine-free assemblies at the repository root:

```powershell
dotnet build onity-core-ci.csproj -c Release -nologo
```

Open the repository as a Unity project and run the Test Runner for the EditMode and PlayMode suites under
`Tests/`. A project that consumes only the UPM package validates it through its own Unity compilation and
tests.

## Notes

- The runtime implementation is self-owned under `Runtime`; the development repository's third-party
  frameworks are reference and comparison inputs only.
- Performance claims are measurements; validate them with `Benchmarks/` before relying on them for your
  target platform.
