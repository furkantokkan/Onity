# Onity Showcase — "Coin Rush"

A small, runnable mini-game that demonstrates **all three Onity pillars working together**
through one installer and clean architecture:

- **DI** — every service is registered in one installer and consumed by constructor / `[Inject]`
  injection. No `new`-ing of services, no service locators.
- **Reactive** — score and remaining time are `ReactiveProperty<T>` state; a `Select` +
  `DistinctUntilChanged` operator pipeline derives a "low time" warning.
- **Events** — coin pickups and game-over are typed messages on a `MessageBroker`
  (`IPublisher<T>` / `ISubscriber<T>`), fully decoupling senders from receivers.

The loop: coins spawn on a timer, you click them to score, a countdown runs the round, and when
it hits zero a single `GameOverMessage` is published with the final score. The HUD reflects all of
it reactively.

---

## Important: which Onity API this sample uses

This Example Game embeds **only the engine-free Onity cores**: `com.onity.di`, `com.onity.reactive`,
`com.onity.messaging` (all `noEngineReferences: true`). It does **not** embed the `Onity.Unity` glue
assembly that ships in the main package. So the Unity-side conveniences from the AI Usage Guide are
**not available here** and are intentionally not used:

- no `MonoInstaller` / `SceneContext` / `ProjectContext`
- no auto-bound `OnityEventHub` or `MessageBroker`
- no `OnityUnityObservable.EveryUpdate/Timer/Interval`
- no `AddTo(Component)` / `TakeUntilDestroy(Component)`
- no `BindMessageChannel<T>()` extension or `broker.Observe<T>()`

This sample therefore ships **three tiny, sample-owned bridge types** that play those roles using
**only the real shipped engine-free API** (`OnityContainer`, `MessageBroker`, `ReactiveProperty<T>`,
`CompositeDisposable`, the reactive operators). They are clearly commented as the local equivalents:

| Sample bridge type | Stands in for (full package) | Built on (real API present here) |
| --- | --- | --- |
| `OnityMonoInstaller` (abstract) | `Onity.Unity.Installers.MonoInstaller` | `OnityContainer` |
| `OnityShowcaseContext` (MonoBehaviour) | `Onity.Unity.Contexts.SceneContext` | `OnityContainer.Build()` / `Inject()` / `Dispose()` |
| `ShowcaseBehaviour` (base + `Subscriptions` bag) | `AddTo(this)` Component lifetime | `CompositeDisposable` disposed in `OnDestroy` |

When this sample runs inside the full Onity package, you would delete these three bridge types and
use the real `MonoInstaller` + `SceneContext` + `AddTo(this)` instead — the service code is unchanged.

---

## What each file is

**Engine-free domain (plain, testable C# — no UnityEngine):**

- `ShowcaseSettings` — tuning values (round length, coin value, spawn interval, area). Bound via
  `BindInstance` so services carry no magic numbers.
- `CoinCollectedMessage` / `GameOverMessage` — message structs (Events).
- `IScoreService` / `ScoreService` — subscribes to `CoinCollectedMessage`, accumulates a
  `ReactiveProperty<int> Score`.
- `ICountdownService` / `CountdownService` — owns `ReactiveProperty<float> TimeRemaining`, is fed
  delta time via `Tick`, publishes `GameOverMessage` once at zero, and exposes a
  `LowTimeWarning` observable built with reactive operators.
- `ICoinSpawnService` / `CoinSpawnService` — engine-free spawn planner (accumulator + injected
  `Func<float>` random source) returning planar spawn positions.

**Composition root + installer:**

- `OnityShowcaseInstaller : OnityMonoInstaller` — the **one** installer. Binds settings, one shared
  `MessageBroker` and its typed channels, the random source, and the three services.
- `OnityShowcaseContext` — creates the container, runs the installer, builds, injects children,
  ticks the countdown each frame, disposes on destroy.

**Thin MonoBehaviours (resolve / inject / forward input only):**

- `CoinSpawnerBehaviour` — forwards `Time.deltaTime` to `ICoinSpawnService`; spawns a clickable
  coin sphere when the service says so.
- `CoinBehaviour` — on click, publishes `CoinCollectedMessage` and removes itself.
- `HudBehaviour` — binds `Score`, `TimeRemaining`, `LowTimeWarning`, and `GameOverMessage` to an
  IMGUI overlay (no Canvas wiring needed).
- `PlayerBehaviour` — new Input System movement (WASD/arrows/left-stick), pure input→transform.

---

## How to wire the scene (do this in the Unity Editor)

There is no `.unity` asset in this folder — these are scripts only. Build the scene once in the
Editor; it takes about a minute.

1. **Open** `Assets/Scenes/SampleScene.unity` (or create a new empty scene).

2. **Ground (optional, for visuals):** GameObject ▸ 3D Object ▸ Plane at origin, scale ~`(1,1,1)`.
   Coins spawn within ±`SpawnAreaHalfSize` (default 4) on X/Z at Y = 0.5.

3. **Camera:** select `Main Camera`. A top-down angle reads best — e.g. Position `(0, 12, -6)`,
   Rotation `(60, 0, 0)`. (Any angle works; you click coins with the mouse.)

4. **Context GameObject (the composition root):**
   - Create an empty GameObject named **`OnityShowcaseContext`** at origin.
   - Add the **`OnityShowcaseInstaller`** component to it. Tune the round fields if you like
     (defaults: 30 s round, 10 pts/coin, spawn every 1.25 s, area half-size 4).
   - Add the **`OnityShowcaseContext`** component to the **same** GameObject. Leave its
     `Installer` field empty — `Awake` auto-finds the installer on the same GameObject (or assign
     it explicitly via the inspector).

5. **Spawner (child of the context):**
   - Create an empty child GameObject under `OnityShowcaseContext` named **`CoinSpawner`**.
   - Add the **`CoinSpawnerBehaviour`** component. (It is a `ShowcaseBehaviour`, so the context
     injects it automatically.) Optionally tweak coin height / scale.

6. **HUD (child of the context):**
   - Create an empty child GameObject under `OnityShowcaseContext` named **`Hud`**.
   - Add the **`HudBehaviour`** component. It draws via `OnGUI`, so nothing else is needed.

7. **Player (optional, child anywhere):**
   - GameObject ▸ 3D Object ▸ Capsule named **`Player`** at `(0, 1, 0)`.
   - Add the **`PlayerBehaviour`** component. `PlayerBehaviour` is a plain MonoBehaviour (no
     injection needed), so it does not have to be under the context. Move with WASD / arrows /
     gamepad left stick.

   > The project uses the new Input System. If the Editor prompts about the Active Input Handling,
   > set it to **Input System Package** (or **Both**) under Project Settings ▸ Player. The script is
   > guarded by `ENABLE_INPUT_SYSTEM` and simply does nothing if the Input System is disabled.

**Required hierarchy** (injection only reaches `ShowcaseBehaviour`s **under** the context):

```
OnityShowcaseContext      (OnityShowcaseInstaller + OnityShowcaseContext)
├── CoinSpawner           (CoinSpawnerBehaviour)
└── Hud                   (HudBehaviour)
Player                    (PlayerBehaviour)   // optional, can live outside the context
```

---

## Quick play-test (in the Editor)

Press **Play** and confirm:

1. The HUD shows `Score: 0`, `Time: 30.0`, `Collect the coins!`.
2. Coin spheres appear roughly every 1.25 s inside the play area.
3. Clicking a coin removes it and **Score increases by 10** (Events → ScoreService → reactive HUD).
4. The timer counts down; under 5 s the **time turns red** (the `Select`+`DistinctUntilChanged`
   `LowTimeWarning` pipeline).
5. At `Time: 0.0`, spawning stops and the status shows **`Game Over! Final score: N`** (the single
   `GameOverMessage`, carrying the score read at that instant).

No console errors should appear. On stop/exit, `OnityShowcaseContext.OnDestroy` disposes the
container (and the broker, services, and reactive properties), and each `ShowcaseBehaviour` disposes
its subscription bag.

> Scene-asset wiring and this play-test must be performed in the Unity Editor; the scripts compile
> against the embedded engine-free Onity cores but the `.unity` scene cannot be authored from here.
