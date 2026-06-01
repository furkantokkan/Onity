---
title: "Refactoring from Existing Architecture"
parent: "Guides"
nav_order: 7
---

# Refactoring from Existing Architecture

This guide shows the refactoring shape Onity is meant to encourage: move game
rules into constructor-injected plain C# services, keep `MonoBehaviour` classes
as thin Unity adapters, and keep all wiring in one installer. The result is less
global state, fewer scene-order assumptions, and services that can be tested
without loading a scene.

Use this page as the reference when converting a manager-heavy Unity script,
serialized Unity reference graph, ScriptableObject-driven setup,
VContainer/Zenject lifetime scope, or static event bus into Onity.

## Refactoring rules

| Smell | Onity target |
| --- | --- |
| `GameManager.Instance` from unrelated scripts | Inject a small role interface such as `IScoreService` |
| One manager owns score, UI, scene loading, and spawn rules | Split state/rules into services; keep views in MonoBehaviours |
| `Update` does manual resolve or scene search | Resolve once through the context, then call plain methods |
| Event sender references every receiver | Publish a typed message through `OnityEventHub` or `OnityEvent` |
| ScriptableObject stores runtime state | Bind ScriptableObjects as read-only config; keep runtime state in services |
| Serialized references connect gameplay systems directly | Inject role interfaces; keep serialized refs for view/prefab assets |
| VContainer entry point registration for each manager loop | Bind an `IOnityTickable` / `IOnityInitializable` singleton; Onity collects it automatically |
| Zenject `SignalBus` used only for simple gameplay notifications | Use `OnityEventHub` or `OnityEvent` typed messages |

## Example 1: From `GameManager.Instance`

### Before

The usual singleton manager is convenient at first, but the dependencies are
hidden and the class changes for too many reasons: score rules, UI, enemy flow,
and scene transitions are all coupled.

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private ScoreHud m_hud;
    [SerializeField] private int m_pointsPerEnemy = 10;
    [SerializeField] private int m_winScore = 100;

    private int m_score;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void EnemyKilled()
    {
        m_score += m_pointsPerEnemy;
        m_hud.SetScore(m_score);

        if (m_score >= m_winScore)
        {
            SceneManager.LoadScene("Win");
        }
    }
}

public sealed class EnemyHealth : MonoBehaviour
{
    private void Die()
    {
        GameManager.Instance.EnemyKilled();
        Destroy(gameObject);
    }
}
```

### After

The score rule becomes a plain service. UI listens to a reactive property. The
enemy script only calls the small interface it was given by the context; it does
not know where score is stored or which UI will update.

```csharp
using Onity.Reactive;
using Onity.Unity.Messaging;

public readonly struct ScoreChanged
{
    public readonly int Value;

    public ScoreChanged(int value)
    {
        Value = value;
    }
}

public interface IScoreService
{
    ReactiveProperty<int> Score { get; }
    void AddEnemyKill();
}

public sealed class ScoreService : IScoreService
{
    private const int k_pointsPerEnemy = 10;

    private readonly OnityEventHub m_events;

    public ReactiveProperty<int> Score { get; } = new ReactiveProperty<int>(0);

    public ScoreService(OnityEventHub events)
    {
        m_events = events;
    }

    public void AddEnemyKill()
    {
        Score.Value += k_pointsPerEnemy;
        m_events.Publish(new ScoreChanged(Score.Value));
    }
}
```

```csharp
using System;
using Onity.DI;
using UnityEngine;

public sealed class EnemyHealth : MonoBehaviour
{
    [Inject] private IScoreService m_score;

    private void Die()
    {
        m_score.AddEnemyKill();
        Destroy(gameObject);
    }
}

public sealed class ScoreHud : MonoBehaviour
{
    [Inject] private IScoreService m_score;

    private IDisposable m_subscription;

    private void OnEnable()
    {
        m_subscription = m_score.Score.Subscribe(SetScore);
    }

    private void OnDisable()
    {
        m_subscription?.Dispose();
        m_subscription = null;
    }

    private void SetScore(int value)
    {
        // Update UI Toolkit, TMP, or UGUI here.
    }
}
```

```csharp
using Onity.DI;
using Onity.Unity.Installers;

public sealed class GameInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.Bind<IScoreService>().To<ScoreService>().AsSingle();
    }
}
```

The important part is not the number of files. The important part is the new
direction of dependency flow:

```text
EnemyHealth -> IScoreService <- ScoreHud
                  |
                  v
             OnityEventHub
```

`EnemyHealth` and `ScoreHud` no longer reference each other, no object calls a
global singleton, and `ScoreService` can be tested as a plain class.

## Example 2: From a VContainer manager

### Before

A typical VContainer setup registers a service and an entry point separately.
That is a good DI model, but in a project moving to Onity the same role can use
the Onity lifecycle interfaces directly.

```csharp
using VContainer;
using VContainer.Unity;

public sealed class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<IScoreService, ScoreService>(Lifetime.Singleton);
        builder.RegisterEntryPoint<GameSessionManager>();
    }
}

public sealed class GameSessionManager : IInitializable, ITickable
{
    private readonly IScoreService m_score;

    public GameSessionManager(IScoreService score)
    {
        m_score = score;
    }

    public void Initialize()
    {
        m_score.Score.Value = 0;
    }

    public void Tick()
    {
        // Session-level per-frame rule.
    }
}
```

### After

In Onity, the manager becomes a service with explicit lifecycle contracts. A
singleton that implements `IOnityInitializable` or `IOnityTickable` is collected
automatically when the container builds, so there is no separate entry-point
registration line.

```csharp
using Onity.DI;

public interface IGameSessionService
{
    bool IsRunning { get; }
}

public sealed class GameSessionService :
    IGameSessionService,
    IOnityInitializable,
    IOnityTickable
{
    private readonly IScoreService m_score;

    public bool IsRunning { get; private set; }

    public GameSessionService(IScoreService score)
    {
        m_score = score;
    }

    public void Initialize()
    {
        m_score.Score.Value = 0;
        IsRunning = true;
    }

    public void Tick()
    {
        if (!IsRunning)
        {
            return;
        }

        // Session-level per-frame rule.
    }
}
```

```csharp
using Onity.DI;
using Onity.Unity.Installers;

public sealed class GameInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.Bind<IScoreService>().To<ScoreService>().AsSingle();
        container.BindInterfacesAndSelfTo<GameSessionService>().AsSingle().NonLazy();
    }
}
```

`BindInterfacesAndSelfTo<GameSessionService>()` makes one instance visible as
`IGameSessionService`, `IOnityInitializable`, `IOnityTickable`, and
`GameSessionService`. `NonLazy()` constructs it during `Build()`, then Onity
runs `Initialize()` and pumps `Tick()` from the owning context.

## Example 3: From Unity references and ScriptableObject config

### Before

Serialized references are useful for assets and views, but they become brittle
when they wire gameplay systems together. In this example, the reward rule, UI,
audio, and runtime score live in one scene object. Moving the HUD object or audio
object can break the rule code.

```csharp
using UnityEngine;

[CreateAssetMenu]
public sealed class EnemyRewardSettings : ScriptableObject
{
    public int PointsPerEnemy = 10;
}

public sealed class EnemyRewardManager : MonoBehaviour
{
    [SerializeField] private EnemyRewardSettings m_settings;
    [SerializeField] private ScoreHud m_hud;
    [SerializeField] private AudioSource m_audio;
    [SerializeField] private AudioClip m_killClip;

    private int m_score;

    public void OnEnemyKilled()
    {
        m_score += m_settings.PointsPerEnemy;
        m_hud.SetScore(m_score);
        m_audio.PlayOneShot(m_killClip);
    }
}
```

### After

Keep the ScriptableObject as config, not as the runtime owner. Bind it through
the installer as a small read-only interface. The score rule becomes a service;
the HUD and audio stay as Unity views that observe state/events.

```csharp
using UnityEngine;

public interface IEnemyRewardConfig
{
    int PointsPerEnemy { get; }
}

[CreateAssetMenu]
public sealed class EnemyRewardConfig : ScriptableObject, IEnemyRewardConfig
{
    [SerializeField] private int m_pointsPerEnemy = 10;

    public int PointsPerEnemy => m_pointsPerEnemy;
}
```

```csharp
using Onity.Reactive;
using Onity.Unity.Messaging;

public readonly struct EnemyRewarded
{
    public readonly int Score;

    public EnemyRewarded(int score)
    {
        Score = score;
    }
}

public interface IEnemyRewardService
{
    ReactiveProperty<int> Score { get; }
    void AddEnemyKill();
}

public sealed class EnemyRewardService : IEnemyRewardService
{
    private readonly IEnemyRewardConfig m_config;
    private readonly OnityEventHub m_events;

    public ReactiveProperty<int> Score { get; } = new ReactiveProperty<int>(0);

    public EnemyRewardService(IEnemyRewardConfig config, OnityEventHub events)
    {
        m_config = config;
        m_events = events;
    }

    public void AddEnemyKill()
    {
        Score.Value += m_config.PointsPerEnemy;
        m_events.Publish(new EnemyRewarded(Score.Value));
    }
}
```

```csharp
using System;
using Onity.DI;
using Onity.Unity;
using UnityEngine;

public sealed class EnemyDeathReporter : MonoBehaviour
{
    [Inject] private IEnemyRewardService m_rewards;

    public void ReportEnemyKilled()
    {
        m_rewards.AddEnemyKill();
    }
}

public sealed class EnemyRewardHud : MonoBehaviour
{
    [Inject] private IEnemyRewardService m_rewards;

    private IDisposable m_subscription;

    private void OnEnable()
    {
        m_subscription = m_rewards.Score.Subscribe(SetScore);
    }

    private void OnDisable()
    {
        m_subscription?.Dispose();
        m_subscription = null;
    }

    private void SetScore(int value)
    {
        // Update UI here.
    }
}

public sealed class EnemyRewardAudio : MonoBehaviour
{
    [SerializeField] private AudioSource m_audio;
    [SerializeField] private AudioClip m_killClip;

    private IDisposable m_subscription;

    private void OnEnable()
    {
        m_subscription = OnityEvent.Observe<EnemyRewarded>(this).Subscribe(OnEnemyRewarded);
    }

    private void OnDisable()
    {
        m_subscription?.Dispose();
        m_subscription = null;
    }

    private void OnEnemyRewarded(EnemyRewarded message)
    {
        m_audio.PlayOneShot(m_killClip);
    }
}
```

```csharp
using Onity.DI;
using Onity.Unity.Installers;
using UnityEngine;

public sealed class GameInstaller : MonoInstaller
{
    [SerializeField] private EnemyRewardConfig m_rewardConfig;

    public override void InstallBindings(OnityContainer container)
    {
        container.BindScriptableObject<IEnemyRewardConfig, EnemyRewardConfig>(m_rewardConfig);
        container.Bind<IEnemyRewardService>().To<EnemyRewardService>().AsSingle();
    }
}
```

The ScriptableObject still gives designers a familiar asset workflow, but the
runtime state no longer lives inside the asset or a scene reference web.

## Example 4: From Zenject manager + SignalBus

### Before

Zenject can solve the singleton problem, but a project may still accumulate
manager classes and `SignalBus` wiring for simple notifications.

```csharp
using Zenject;

public readonly struct WaveStartedSignal
{
    public readonly int Wave;

    public WaveStartedSignal(int wave)
    {
        Wave = wave;
    }
}

public sealed class CombatInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        SignalBusInstaller.Install(Container);
        Container.DeclareSignal<WaveStartedSignal>();
        Container.BindInterfacesAndSelfTo<WaveManager>().AsSingle();
    }
}

public sealed class WaveManager : IInitializable, ITickable
{
    private readonly SignalBus m_signals;
    private int m_wave;

    public WaveManager(SignalBus signals)
    {
        m_signals = signals;
    }

    public void Initialize()
    {
        m_wave = 1;
        m_signals.Fire(new WaveStartedSignal(m_wave));
    }

    public void Tick()
    {
        // Wave progression.
    }
}
```

### After

In Onity, the manager becomes a role service and the signal becomes a typed
message. The lifecycle is collected automatically, and the event path is the
same messaging system used by reactive streams.

```csharp
using Onity.DI;
using Onity.Unity.Messaging;

public readonly struct WaveStarted
{
    public readonly int Wave;

    public WaveStarted(int wave)
    {
        Wave = wave;
    }
}

public interface IWaveService
{
    int CurrentWave { get; }
}

public sealed class WaveService :
    IWaveService,
    IOnityInitializable,
    IOnityTickable
{
    private readonly OnityEventHub m_events;

    public int CurrentWave { get; private set; }

    public WaveService(OnityEventHub events)
    {
        m_events = events;
    }

    public void Initialize()
    {
        CurrentWave = 1;
        m_events.Publish(new WaveStarted(CurrentWave));
    }

    public void Tick()
    {
        // Wave progression.
    }
}
```

```csharp
using Onity.DI;
using Onity.Unity.Installers;

public sealed class CombatInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.BindInterfacesAndSelfTo<WaveService>().AsSingle().NonLazy();
    }
}
```

If a plain service needs only one direction of a message channel, use typed
subscriber injection:

```csharp
using System;
using Onity.DI;
using Onity.Messaging;
using Onity.Unity.Installers;
using Onity.Unity.Messaging;

public sealed class CombatInstaller : MonoInstaller
{
    public override void InstallBindings(OnityContainer container)
    {
        container.BindMessageChannel<WaveStarted>();
        container.BindInterfacesAndSelfTo<WaveService>().AsSingle().NonLazy();
        container.Bind<WaveHudModel>().AsSingle();
    }
}

public sealed class WaveHudModel : IDisposable
{
    private readonly IDisposable m_subscription;

    public WaveHudModel(ISubscriber<WaveStarted> waves)
    {
        m_subscription = waves.Subscribe(OnWaveStarted);
    }

    public void Dispose()
    {
        m_subscription.Dispose();
    }

    private void OnWaveStarted(WaveStarted message)
    {
        // Update HUD model state.
    }
}
```

## Example 5: From static events or `UnityEvent`

### Before

Static C# events and inspector-wired `UnityEvent` callbacks both decouple the
sender from the receiver syntactically, but they often hide lifetime and scope.
A missed unsubscribe can leak, and a static event ignores `ProjectContext`,
`SceneContext`, and `GameObjectContext` ownership.

```csharp
using System;
using UnityEngine;

public static class GameEvents
{
    public static event Action<int> PlayerDamaged;

    public static void RaisePlayerDamaged(int amount)
    {
        PlayerDamaged?.Invoke(amount);
    }
}

public sealed class DamageButton : MonoBehaviour
{
    public void Click()
    {
        GameEvents.RaisePlayerDamaged(10);
    }
}

public sealed class DamageHud : MonoBehaviour
{
    private void OnEnable()
    {
        GameEvents.PlayerDamaged += OnPlayerDamaged;
    }

    private void OnDisable()
    {
        GameEvents.PlayerDamaged -= OnPlayerDamaged;
    }

    private void OnPlayerDamaged(int amount)
    {
        // Update UI.
    }
}
```

### After

Use a typed message. The owner overload routes through the nearest
`GameObjectContext` when one exists, then falls back to the active scene/project
context. Dispose subscriptions in `OnDisable` for enable/disable lifetime, or
use `AddTo(this)` when a subscription should live until destroy.

```csharp
using System;
using Onity.Reactive;
using Onity.Unity;
using UnityEngine;

public readonly struct PlayerDamaged
{
    public readonly int Amount;

    public PlayerDamaged(int amount)
    {
        Amount = amount;
    }
}

public sealed class DamageButton : MonoBehaviour
{
    public void Click()
    {
        OnityEvent.Publish(this, new PlayerDamaged(10));
    }
}

public sealed class DamageHud : MonoBehaviour
{
    private IDisposable m_subscription;

    private void OnEnable()
    {
        m_subscription = OnityEvent.Observe<PlayerDamaged>(this)
            .Where(message => message.Amount > 0)
            .Subscribe(OnPlayerDamaged);
    }

    private void OnDisable()
    {
        m_subscription?.Dispose();
        m_subscription = null;
    }

    private void OnPlayerDamaged(PlayerDamaged message)
    {
        // Update UI.
    }
}
```

Use direct interface injection when the receiver is a required collaborator. Use
a typed event when there can be zero, one, or many receivers and the sender
should not know them.

## Migration checklist

1. Name the responsibility before naming the class. Prefer `ScoreService`,
   `WaveService`, or `GameSessionService` over a broad `GameManager`.
2. Create small interfaces for roles other code consumes.
3. Move rules and state into constructor-injected plain C# services.
4. Keep `MonoBehaviour` scripts as input, view, collision, trigger, or prefab
   adapters.
5. Bind ScriptableObjects as config contracts, not as mutable runtime state.
6. Bind all services in one `MonoInstaller` assigned to a `ProjectContext`,
   `SceneContext`, or `GameObjectContext`.
7. Use `OnityEventHub` in services and `OnityEvent.Publish/Subscribe/Observe`
   from Unity-facing scripts when a typed event is cleaner than a direct
   dependency.
8. Replace static events gradually; convert one message at a time and dispose
   subscriptions in `OnDisable`, with `AddTo(this)` for destroy lifetime or a
   `CompositeDisposable` for plain services.
9. Delete the singleton last, after every caller receives an injected interface.

## See also

- [Dependency Injection](dependency-injection.html) — binding, injection sites,
  and documented resolve behavior.
- [Lifecycle & Scopes](lifecycle-and-scopes.html) — automatic lifecycle
  collection and context ownership.
- [Events & Messaging](events-messaging.html) — event hub and `OnityEvent`
  examples.
- [Migration: From Zenject](../Migration/From-Zenject.html) — syntax-level
  differences for existing Zenject projects.
- [Migration: From VContainer](../Migration/From-VContainer.html) — API-level
  migration details.
- [ADR 0004: Refactoring from Existing Architecture](../ADR/0004-refactoring-from-existing-architecture.html).
