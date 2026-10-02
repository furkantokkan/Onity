# Onity

Core runtime, editor integration, and tests for the `Onity` package stack:
dependency injection, reactive state, typed messaging and `OnityTask` async in
one package, with one lifetime model across them.

Current Unity target in this repository: `2022.3.62f2`.

**0.6.0:** OnityTask is faster than UniTask 2.5.11 in all 29 gated rows of the
published IL2CPP Release Player suite (median speedups 1.2x to 12x), measured at
Onity's default context-flow setting with pool retention matched to UniTask for
the 1,024- and 4,096-operation bursts. Mono, opt-in `AsyncLocal` flow and
default-retention results, including the rows where Onity is slower, are in the
[comparison guide](https://furkantokkan.github.io/Onity/guide/onitytask-comparison.html).
The release also covers UniTask's runtime API under Onity names, apart from the
gaps listed in that guide (PlayerLoop timings, timers, composition, triggers, UI
events, async LINQ, channels), and adds what a standalone async library cannot:
DI scope tokens, awaited `IOnityAsyncInitializable` startup, `ReactiveProperty`
waits and message-bus receives with backpressure. See `CHANGELOG.md` for the
breaking changes.

## For AI coding assistants

- Start with [`AGENTS.md`](AGENTS.md) (Claude Code reaches it through `CLAUDE.md`), then
  `Documentation~/AI/Onity-AI-Usage-Guide.md` for the API rules and the feature guides under
  `Documentation~/guide/`. [`llms.txt`](llms.txt) indexes every shipped page.
- `Documentation~/` is a generated copy of the repository's `docs/` folder. It ships inside the package, so it
  is available after a Git or registry install, and Unity ignores it.
- To give your project's assistants the `onity-use` skill and the usage guide, run
  `Onity/AI/Install Assistant Guidance...` in the Unity Editor. Nothing is written until you confirm.

## Scope

- Runtime root: `Runtime`
- Editor root: `Editor`
- Tests root: `Tests`
- Benchmarks root: `Benchmarks`

In this development repository the package lives at `Packages/com.onity.framework`.
On the published `upm` branch these paths are at the package root.

Core assemblies in this package:

- `Onity.Core`
- `Onity.DI`
- `Onity.Messaging`
- `Onity.Reactive`
- `Onity.Factory`
- `Onity.Composition`
- `Onity.Pooling`
- `Onity.Unity`
- `Onity.Unity.UGUI` (optional; compiled only when `com.unity.ugui` is installed)
- `Onity.DOTS`
- `Onity.Editor`
- `Onity.Tests.EditMode`
- `Onity.Tests.PlayMode`

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
  - `OnityEvent.Publish(...)`, `OnityEvent.Subscribe(...)`, `OnityEvent.Observe<TMessage>()`
  - `OnityEventHub` with `Publish`, `Subscribe`, `Observe<TMessage>()`
- Reactive:
  - `Subject<T>`, `ReactiveProperty<T>`, `CompositeDisposable`
  - `Where`, `Select`, `FromEvent`
  - `Debounce`, `Throttle`, `ThrottleLast`, `Buffer`, `TakeUntil(Task/Token)`
  - `SelectAwait`, `WhereAwait`
  - `ObserveOnThreadPool`, `SelectOnThreadPool`, `ObserveOnMainThread`
  - `EveryUpdate`, `EveryFixedUpdate`, `EveryLateUpdate`
  - Optional thread mode for frame streams:
    - `OnityUnityThreadMode.SingleThread`
    - `OnityUnityThreadMode.JobMultiThread`
    - `OnityUnityThreadMode.BurstJobMultiThread`
    - `OnityUnityThreadMode.DotsEventDriven`
  - Task bridge (`FirstAsync`, `ToTask`)
- Async (`Onity.Unity.Async`, replaces UniTask):
  - `OnityTask` / `OnityTask<T>` / `OnityTaskVoid` with Onity's own pooled method builders
  - Frame waits, 19 PlayerLoop timings (`OnityPlayerLoopTiming`), `Yield`, `Delay`,
    `WaitForSeconds`, `WaitUntil` / `WaitWhile`, `WaitUntilValueChanged`,
    `OnityPlayerLoopTimer`, `CancelAfterSlim`, `OnityTimeoutController`, `Timeout`
  - `WhenAll` / `WhenAny` over arrays, sequences and typed tuples, `WhenEach`
  - `SwitchToMainThread`, `ReturnToMainThread`, `SwitchToThreadPool`, `RunOnThreadPool`
  - `OnityTaskCompletionSource(<T>)`, `OnityAutoResetTaskCompletionSource(<T>)`
  - Destroy tokens, lifecycle and MonoBehaviour message triggers, `UnityEvent`
    and UI Toolkit events, optional uGUI events (`Onity.Unity.UGUI`)
  - `IOnityAsyncEnumerable<T>` streams with UniTask's async LINQ operators,
    `Subscribe`, `Publish`, `BindTo`, channels and `OnityAsyncReactiveProperty<T>`
  - Scene, web request, `AsyncOperation`, asset, `JobHandle` and coroutine bridges
  - DI and reactive integration: scope lifetime tokens, `IOnityAsyncInitializable`,
    `ReactiveProperty.WaitAsync`, message-bus `ReceiveAsync` / `SubscribeQueued`

Pooled `OnityTask` values (cancelable and timed waits, Unity operations and
suspended async methods) are single-consumer. Await each value once. If several
consumers must share an operation, call `Preserve()` once and share its returned
`OnityTask`, or call `AsTask()` once and share the returned `Task`. In Play, frame
waits without a cancelable token and `Yield()` are stateless and may be awaited
by any number of consumers. For an operation completed by a callback, use
`OnityTaskCompletionSource<T>` or its untyped variant.

Suspended `async OnityTask` methods bind a pooled native runner that returns to
its pool when the task is consumed. By default they do not flow the execution
context across awaits, matching UniTask; set `OnityTask.FlowExecutionContext =
true` before any async Onity method starts to flow `AsyncLocal<T>` values, which
allocates a context per suspension only once a thread has stored an
`AsyncLocal<T>` value. Assemblies that call `Onity.Unity.Async` extension methods
must reference `Onity.Reactive` (and `Onity.Messaging` for the messaging bridges).

See the [Async with OnityTask guide](https://furkantokkan.github.io/Onity/guide/onitytask.html)
for cancellation, timings, composition, streams, interop, and diagnostics.
- Pool and factory convenience:
  - `IFactory<T>`, `IFactory<TParam,T>`, `IFactory<TParam1,TParam2,T>`
  - `IPool<T>`, `OnityObjectPool<T>`, `PrefabComponentPool<T>`
  - `IPoolHooks` for pooled component reset callbacks
  - `BindPooledFactory(...)`
  - `BindScriptableObject(...)`
- Scene flow helpers:
  - `OnitySceneFlow`
  - `OnitySceneLoader`
  - `OnitySceneTransitionStore`
  - `OnitySceneFlowProfile`
  - `OnitySceneFlowStateMachine`
  - Bootstrap readiness and default UI Toolkit loading initiators

## Quick Start

1. Create a runtime-loadable `ProjectContext` prefab:
   - `Onity/Contexts/Create ProjectContext Prefab`
2. Add `SceneContext` to gameplay scenes and assign installers.
3. Add `GameObjectContext` for local per-prefab scope when needed.
4. Use `OnitySceneFlow` + `OnitySceneInitiator` for SEP-style scene entry.
5. Optional: create and assign an `OnitySceneFlowProfile` to drive
   grouped scene routing with optional singleton `Bootstrap` / `Loading`
   scenes plus as many `Menu`, `Hub`, and `Gameplay` scenes as your game needs.
   The Scene Flow Manager includes 2-, 3-, and 4-stage ready presets plus a
   Blank Template that creates only an empty profile asset.

## Minimal Installer Example

```csharp
using Onity.DI;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;
using UnityEngine;

public sealed class GameInstaller : MonoInstaller
{
    [SerializeField] private GameConfig m_config;

    public override void InstallBindings(OnityContainer container)
    {
        container.BindScriptableObject(m_config);
        container.BindInterfacesAndSelfTo<PlayerService>().AsSingle().NonLazy();
        container.BindFactory<Projectile, ProjectileFactory>();
        container.BindMessageChannel<PlayerDamagedMessage>();
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

- `Onity/Tools/Monitor`
- `Onity/Tools/Container Diagnostics`
- `Onity/Tools/Task Tracker`
- `Onity/Tools/Observable Tracker`
- `Onity/Tools/Pool Monitor`
- `Onity/Tools/Scene Flow Manager`
- `Onity/Validation/Validate Scene`
- `Onity/Validation/Validate All Scenes`

Task tracker notes:

- Stack trace capture toggle is available in `Onity/Tools/Task Tracker`.
- Use stack trace capture only for leak debugging. It has extra allocation cost in editor.

## UniTask Comparison Snapshot

The 2026-10-02 Release Player gate (Unity 2022.3.62f2, Windows x64, UniTask 2.5.11,
three processes per backend and suite) measured OnityTask faster in all 29 gated
IL2CPP rows, with median Onity/UniTask time ratios from 0.085 to 0.808, at the
default `FlowExecutionContext = false` and with pool retention matched for the
1,024- and 4,096-operation bursts. It is a timing result for those workloads on
one machine, not an allocation or cross-platform claim. Reported limits:

- Mono: the four synchronous completed-result rows are 1.09x to 1.92x slower.
- Opt-in `AsyncLocal` flow: a complete method lifecycle with four suspensions is
  1.5x to 1.6x slower on IL2CPP and 2.6x to 2.7x on Mono.
- Default pool retention: 4,096-call bursts of one async method are up to 2.6x
  slower; raise `OnityTask.RunnerPoolCapacity` / `SourcePoolCapacity` for them.

Remaining API gaps against UniTask: timing and `cancelImmediately` overloads of the
Unity operation adapters, a native shareable `WhenAll` output,
`UniTaskSynchronizationContext`, index-only `SelectAwait` / `WhereAwait`, and an
integer-milliseconds `Delay`. The
[measured OnityTask comparison](https://furkantokkan.github.io/Onity/guide/onitytask-comparison.html)
records every row, the feature coverage and the evidence.

## Benchmark Snapshot

Latest published DI runs: Unity 2022.3.62f3, Windows, 512 warmup iterations,
8 measured samples, mean ns/op. See `Benchmarks/Results/di-benchmark-summary.md`
and `Benchmarks/Results/di-benchmark-player-latest.md` for full reports.

Editor / Mono (`2026-07-12T13:31:37Z`):

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~69 ns | ~78 ns | ~217 ns | ~2,778 ns | ~68% |
| Resolve Transient | ~1,030 ns | ~1,366 ns | ~2,352 ns | ~12,561 ns | ~56% |
| Resolve Combined | ~980 ns | ~875 ns | ~1,905 ns | ~14,382 ns | ~49% |
| Resolve Complex (6-level) | ~20,874 ns | ~20,828 ns | ~40,270 ns | ~281,814 ns | ~48% |
| Prepare & Register Complex | ~40,613 ns | ~54,996 ns | ~139,246 ns | ~188,865 ns | ~71% |

Windows IL2CPP Player (`2026-07-12T13:34:55Z`, source-generated activators, 10,000 iterations):

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~18 ns | ~18 ns | ~95 ns | ~449 ns | ~82% |
| Resolve Transient | ~159 ns | ~191 ns | ~541 ns | ~2,448 ns | ~71% |
| Resolve Combined | ~176 ns | ~196 ns | ~612 ns | ~3,080 ns | ~71% |
| Resolve Complex (6-level) | ~5,107 ns | ~5,071 ns | ~12,475 ns | ~59,327 ns | ~59% |
| Prepare & Register Complex | ~21,128 ns | ~24,490 ns | ~34,888 ns | ~59,567 ns | ~39% |

Timing numbers are from a Windows PC and are indicative, not a guarantee. The
committed allocation columns are withdrawn until the allocation harness is
corrected, because the earlier Editor harness reported 0 B for every container.
Onity is ahead on every measured Editor/Mono and Windows IL2CPP player timing
path in the current benchmark reports. The raw `Onity (Reflection)` label is the
standard dense-provider lane; reflection is only its activation fallback. A
focused 1000-sample IL2CPP singleton gate measured standard Onity at `18.80 ns/op`
versus VContainer at `94.39 ns/op`. Source-generated activators keep hot
IL2CPP construction away from `ConstructorInfo.Invoke` on AOT builds.

## Build and Test

From a clone of the full development repository, run this at the repository
root to compile the engine-free assemblies:

```powershell
dotnet build onity-core-ci.csproj -c Release -nologo
```

Unity generates `Onity.Core.csproj`, `Onity.DI.csproj`, `Onity.Unity.csproj`,
and the test projects after opening the development repository in Unity. Those
generated files can then be built from the command line for local compile
checks. A project consuming only the UPM package should validate it through its
own Unity compilation and tests.

EditMode tests are under:

- `Tests/EditMode/Scripts`

## Notes

- Runtime implementation is self-owned under `Runtime`.
- Third-party frameworks in the development repository are reference and
  comparison inputs only.
- Performance claims should be validated with `Benchmarks`.
