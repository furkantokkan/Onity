# Onity Samples

This folder includes lightweight samples for the current Onity runtime modules.

## Coverage Status

Current sample coverage focuses on:

- DI contexts and installers.
- Messaging and reactive state flows.
- Pool + factory composition.
- UI Toolkit HUD/presenter patterns.
- DOTS bridge usage from managed gameplay.
- Package-aware optional paths via preprocessor defines (`ONITY_ENTITIES`, `ONITY_UNITY_PHYSICS`).

Note:
- Samples are designed as reference implementations for API ergonomics and integration shape.
- Performance claims should be validated via `Assets/Onity-Packages/Onity/Benchmarks`.

## Quick Start

Generate ready-to-run scenes from Unity menu:

- `Onity/Samples/Generate Basic Gameplay Scene`
- `Onity/Samples/Generate GameObject Scope Scene`
- `Onity/Samples/Generate Roll A Ball Scene`
- `Onity/Samples/Generate Tank Arena 2D Scene`
- `Onity/Samples/Generate All Sample Scenes`

Generated scenes:
- `Assets/Onity-Packages/Onity/Samples/BasicGameplay/Scenes/OnityBasicGameplaySample.unity`
- `Assets/Onity-Packages/Onity/Samples/GameObjectContextScope/Scenes/OnityGameObjectScopeSample.unity`
- `Assets/Onity-Packages/Onity/Samples/RollABall/Scenes/Game.unity`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/BostrapScene - 1.unity`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/LoadingScene.unity`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/MainMenuHub - 2.unity`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/GameModeOrGameScene - 3.unity`

Generated profile asset:
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Data/OnityTankArenaSceneFlowProfile.asset`

## External Art Assets

- `Assets/Onity-Packages/Onity/Samples/Art/2D Tanks` uses Kenney "Tanks (pack)" assets.
- License: Creative Commons Zero (CC0) 1.0.
- Source: `https://kenney.nl`
- Local license file: `Assets/Onity-Packages/Onity/Samples/Art/2D Tanks/License.txt`

## Diagnostics Monitor (UI Toolkit)

Open:
- `Onity/Tools/Monitor`
- `Onity/Tools/Scene Flow Manager`

Provides:
- Active `ProjectContext`, `SceneContext`, `GameObjectContext` list.
- Per-context container diagnostics (binding and cached plan counts).
- Message broker channel diagnostics with live subscriber counts.

## DI Quick Patterns

Constructor injection:

```csharp
public sealed class PlayerStateService : IPlayerStateService
{
    private readonly ISubscriber<PlayerDamagedMessage> m_damageSubscriber;

    public PlayerStateService(ISubscriber<PlayerDamagedMessage> damageSubscriber)
    {
        m_damageSubscriber = damageSubscriber;
    }
}
```

ScriptableObject injection (bind + inject in one line):

```csharp
[SerializeField] private GameBalanceConfig m_balanceConfig;

public override void InstallBindings(OnityContainer container)
{
    container.BindScriptableObject<GameBalanceConfig>(m_balanceConfig);
}
```

DOTS queue bridge (enabled when `com.unity.entities` is installed):

```csharp
OnityDotsIntEventBridge.TryPublish(10);
```

This queue is consumed by Burst systems in `Onity.DOTS`:
- `OnityDotsIntEventBootstrapSystem`
- `OnityDotsIntEventAccumulateSystem`

Pool + factory registration in one line:

```csharp
container.BindPooledFactory(projectilePrefab, poolRoot, 16, 128);
```

Async helpers (Unitask-like flow without external runtime dependency):

```csharp
await OnityAsync.DelayAsync(0.25f, cancellationToken: token);
await OnityAsync.NextFrameAsync(token);
await OnityAsync.WaitUntilAsync(() => m_isReady, token);
```

Reactive to Task bridge:

```csharp
int value = await scoreSubject.FirstAsync(token);
await OnityUnityObservable.EveryUpdate().FirstAsync(token);
```

DOTS async bridge:

```csharp
int current = await OnityDotsIntEventAsync.WaitForAccumulatorAtLeastAsync(50, token);
```

Non-alloc physics query helpers:

```csharp
private readonly RaycastHit[] m_hits = new RaycastHit[16];
int hitCount = OnityNonAllocPhysics.Raycast(
    transform.position,
    transform.forward,
    m_hits,
    100f,
    gameplayMask);
```

Persistent batch raycasts (no per-frame managed allocations):

```csharp
private OnityRaycastCommandBatch m_batch;

void Awake()
{
    m_batch = new OnityRaycastCommandBatch(1024);
}

void OnDestroy()
{
    m_batch.Dispose();
}
```

Async post-build callback (VContainer `RegisterBuildCallback` parity):

```csharp
container.RegisterBuildCallbackAsync(
    async (resolver, token) =>
    {
        await OnityAsync.DelayAsync(0.1f, cancellationToken: token);
        resolver.Resolve<BootstrapRunner>().Run();
    });
```

InputSystem reactive stream (R3-style event wiring):

```csharp
#if ENABLE_INPUT_SYSTEM
moveAction.PerformedAsObservable(token)
    .Select(ctx => ctx.ReadValue<Vector2>())
    .Subscribe(input => m_move = input)
    .AddTo(this);
#endif
```

UI resolver stack + presenter custom factory bridge:

```csharp
OnityUiServiceLocator.PushResolver(sceneContext.Container.Resolve);
OnityUiPresenterFactory.Create<InventoryPresenter>();
```

## Sample 1: Basic Gameplay

Shows:
- DI container + installers.
- Message bus (`IPublisher<T>` / `ISubscriber<T>`).
- Reactive state (`ReactiveProperty<T>`).
- Factory + pooling (`IFactory<T>` + `PrefabComponentPool<T>`).
- UI Toolkit integration.

Scripts:
- `Assets/Onity-Packages/Onity/Samples/BasicGameplay/Scripts/SampleMessagingInstaller.cs`
- `Assets/Onity-Packages/Onity/Samples/BasicGameplay/Scripts/SampleProjectileInstaller.cs`
- `Assets/Onity-Packages/Onity/Samples/BasicGameplay/Scripts/SampleHudController.cs`
- `Assets/Onity-Packages/Onity/Samples/Editor/Scripts/OnitySampleSceneGenerator.cs`

UI Toolkit assets:
- `Assets/Onity-Packages/Onity/Samples/BasicGameplay/UI/OnityBasicHud.uxml`
- `Assets/Onity-Packages/Onity/Samples/BasicGameplay/UI/OnityBasicHud.uss`

Scene setup:
1. Create a new scene.
2. Add `SceneContext` to a root object.
3. Add both installers to `SceneContext` installers list:
   - `SampleMessagingInstaller`
   - `SampleProjectileInstaller`
4. Assign projectile prefab on `SampleProjectileInstaller`.
5. Put gameplay objects and HUD under the same `SceneContext` hierarchy for auto injection.
6. Add a `UIDocument` to a HUD object and assign `OnityBasicHud.uxml`.
7. Add `SampleHudController` and wire:
   - `Damage Emitter`
   - `Projectile Spawner`

## Sample 2: GameObjectContext Scope

Shows:
- Per-object scope isolation with `GameObjectContext`.

Scripts:
- `Assets/Onity-Packages/Onity/Samples/GameObjectContextScope/Scripts/SampleGameObjectScopeInstaller.cs`
- `Assets/Onity-Packages/Onity/Samples/GameObjectContextScope/Scripts/SampleScopeCounterPresenter.cs`

UI Toolkit assets:
- `Assets/Onity-Packages/Onity/Samples/GameObjectContextScope/UI/OnityScopeCounter.uxml`
- `Assets/Onity-Packages/Onity/Samples/GameObjectContextScope/UI/OnityScopeCounter.uss`

Scene setup:
1. Create a prefab with root `GameObjectContext`.
2. Add `SampleGameObjectScopeInstaller` to that context's installers.
3. Add a `UIDocument` using `OnityScopeCounter.uxml`.
4. Add `SampleScopeCounterPresenter` on the same root.
5. Instantiate the prefab multiple times in scene.
6. Press each local increment button and observe isolated counts.

## Sample 3: Roll A Ball (Full Stack)

Shows:
- DI installers + injected ScriptableObject config.
- Constructor-injected score service.
- Message bus pickup flow (`IPublisher<T>` / `ISubscriber<T>`).
- Reactive HUD bindings (`ReactiveProperty<T>`).
- One-line pooled factory setup with `BindPooledFactory`.
- UI Toolkit gameplay HUD.
- DOTS int event bridge integration (`OnityDotsIntEventBridge`).
- Smooth follow camera (`RollABallCameraFollow`) + runtime target binding.
- Player trail VFX + visible rotation marker for motion readability.
- Package-aware ground probe:
- Uses `OnityNonAllocPhysics` when `ONITY_UNITY_PHYSICS` is available.
- Falls back to `UnityEngine.Physics.RaycastNonAlloc` when package is missing.

Scripts:
- `Assets/Onity-Packages/Onity/Samples/RollABall/Scripts/RollABallInstaller.cs`
- `Assets/Onity-Packages/Onity/Samples/RollABall/Scripts/RollABallPickupSpawner.cs`
- `Assets/Onity-Packages/Onity/Samples/RollABall/Scripts/RollABallScoreService.cs`
- `Assets/Onity-Packages/Onity/Samples/RollABall/Scripts/RollABallHudController.cs`
- `Assets/Onity-Packages/Onity/Samples/RollABall/Scripts/RollABallPlayerController.cs`

UI Toolkit assets:
- `Assets/Onity-Packages/Onity/Samples/RollABall/UI/OnityRollABallHud.uxml`
- `Assets/Onity-Packages/Onity/Samples/RollABall/UI/OnityRollABallHud.uss`

Setup:
1. Run `Onity/Samples/Generate Roll A Ball Scene`.
2. Open `Assets/Onity-Packages/Onity/Samples/RollABall/Scenes/Game.unity`.
3. Press Play and move with WASD/Arrow keys.
4. Collect pickups to see score/reactive/dots values update.
5. If UI Toolkit is unavailable in the current scene state, TMP fallback HUD is used automatically.

## Sample 4: Tank Arena 2D (Kenney Art + Full Onity Stack)

Shows:
- DI context + installer composition.
- Message channels for spawn/destroy/damage/wave/restart flow.
- Reactive operators (`Where`, `Select`, `FromEvent`, `EveryUpdate`).
- Async post-build startup (`RegisterBuildCallbackAsync`) for wave loop.
- Pool + factory bindings for enemies and projectiles.
- UI Toolkit view + Onity presenter factory + resolver bridge.
- Non-alloc physics queries through `OnityNonAllocPhysics`.
- DOTS bridge scoring (`OnityDotsIntEventBridge`) and async watcher (`OnityDotsIntEventAsync`).
- Package-aware input flow:
- Uses InputSystem streams when `ENABLE_INPUT_SYSTEM` and references are provided.
- Falls back to `Input.GetAxisRaw` / `Input.GetKey` when InputSystem binding is not configured.

Scripts:
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/TankArenaInstaller.cs`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/TankArenaEnemySpawner.cs`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/TankArenaPlayerController.cs`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/TankArenaHudPresenter.cs`
- `Assets/Onity-Packages/Onity/Samples/Editor/Scripts/OnityTankArenaSceneGenerator.cs`

UI Toolkit assets:
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/UI/OnityTankArenaHud.uxml`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/UI/OnityTankArenaHud.uss`

Setup:
1. Run `Onity/Samples/Generate Tank Arena 2D Scene`.
2. Apply `Onity/Samples/Tank Arena/Apply Scene Flow To Build Settings`.
3. Open `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scenes/BostrapScene - 1.unity`.
4. Press Play. Bootstrap opens `MainMenuHub - 2` first.
5. Press `Start Battle`. The sample routes through `LoadingScene` before entering `GameModeOrGameScene - 3`.
6. Move with WASD / Arrow keys (or mapped InputSystem actions if assigned).
7. Fire with `Space` (or mapped InputSystem fire action).
8. Survive waves and watch Score/Health/Wave/Enemies/DOTS values update on HUD.

Multi-scene architecture option:
- Tank Arena sample flow is `Bootstrap -> MainMenu`, then later scene changes route through `LoadingScene`.
- If you want the same routed setup, use:
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/SceneFlow/TankArenaBootstrapSceneController.cs`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/SceneFlow/TankArenaLoadingSceneController.cs`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/SceneFlow/TankArenaMainMenuSceneController.cs`
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/SceneFlow/TankArenaGameSceneController.cs`
- Scene names are centralized in:
- `Assets/Onity-Packages/Onity/Samples/TankArena2D/Scripts/SceneFlow/TankArenaSceneIds.cs`
- Build settings order helper:
- `Onity/Samples/Tank Arena/Apply Scene Flow To Build Settings`
- Note:
- `Bootstrap` and `Loading` scenes are intentionally lightweight shell scenes. Their job is scene routing and transition UX, not full gameplay authoring.

## Validation and Test Hooks

Runtime validations:
- `Tools/Onity/Validation/Validate Scene`
- `Tools/Onity/Validation/Validate All Scenes`

EditMode test suite:
- `Assets/Onity/Tests/EditMode/Scripts`

## Zenject Parity Reference (CodeBase)

Use `Assets/CodeBase/Gameplay/GameplaySceneInstaller.cs` as the Zenject baseline.

- `Container.BindInterfacesAndSelfTo<T>().AsSingle()`:
  Onity equivalent is `container.BindInterfacesAndSelfTo<T>().AsSingle()`.
- `Container.BindInterfacesTo<T>().AsSingle()`:
  Onity equivalent is `container.BindInterfacesTo<T>().AsSingle()`.
- `NonLazy()`:
  Onity supports `.AsSingle().NonLazy()` and `.AsTransient().NonLazy()`.
- `BindFactory<TParam, TValue, TFactory>()`:
  Onity supports 0/1/2 parameter factory bindings with `IFactory<>` contracts.
- Scene-level installer composition:
  Zenject `GameplaySceneInstaller` maps to Onity `SceneContext` + `MonoInstaller` list.
- Pool registration:
  Zenject gameplay pool service maps to `SampleProjectileInstaller` with `PrefabComponentPool<T>`.

Primary Onity sample scene for this mapping:
- `Assets/Onity-Packages/Onity/Samples/BasicGameplay/Scenes/OnityBasicGameplaySample.unity`

## Mini RPG 2D

- Menu: `Onity/Samples/Mini RPG 2D/Generate Scene`
- Generated scene: `Assets/Onity-Packages/Onity/Samples/MiniRpg2D/Scenes/OnityMiniRpg2DSample.unity`
- Highlights: `SceneContext` DI setup, typed message channels, reactive HUD state, and simple 2D top-down combat.
- Controls: `WASD` or arrow keys to move, `Space` to attack, `Restart Encounter` to reset the sample.

