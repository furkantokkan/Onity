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

Use this page as the reference when converting a manager-heavy Unity script or a
VContainer lifetime scope into Onity.

## Refactoring rules

| Smell | Onity target |
| --- | --- |
| `GameManager.Instance` from unrelated scripts | Inject a small role interface such as `IScoreService` |
| One manager owns score, UI, scene loading, and spawn rules | Split state/rules into services; keep views in MonoBehaviours |
| `Update` does manual resolve or scene search | Resolve once through the context, then call plain methods |
| Event sender references every receiver | Publish a typed message through `OnityEventHub` or `OnityEvent` |
| VContainer entry point registration for each manager loop | Bind an `IOnityTickable` / `IOnityInitializable` singleton; Onity collects it automatically |

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
using Onity.DI;
using Onity.Reactive;
using Onity.Unity.Reactive;
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

    private void OnEnable()
    {
        m_score.Score
            .Subscribe(SetScore)
            .AddTo(this);
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

## Migration checklist

1. Name the responsibility before naming the class. Prefer `ScoreService`,
   `WaveService`, or `GameSessionService` over a broad `GameManager`.
2. Create small interfaces for roles other code consumes.
3. Move rules and state into constructor-injected plain C# services.
4. Keep `MonoBehaviour` scripts as input, view, collision, trigger, or prefab
   adapters.
5. Bind all services in one `MonoInstaller` assigned to a `ProjectContext`,
   `SceneContext`, or `GameObjectContext`.
6. Use `OnityEventHub` in services and `OnityEvent.Publish/Subscribe/Observe`
   from Unity-facing scripts when a typed event is cleaner than a direct
   dependency.
7. Delete the singleton last, after every caller receives an injected interface.

## See also

- [Dependency Injection](dependency-injection.html) — binding, injection sites,
  and documented resolve behavior.
- [Lifecycle & Scopes](lifecycle-and-scopes.html) — automatic lifecycle
  collection and context ownership.
- [Events & Messaging](events-messaging.html) — event hub and `OnityEvent`
  examples.
- [Migration: From VContainer](../Migration/From-VContainer.html) — API-level
  migration details.
- [ADR 0004: Refactoring from Existing Architecture](../ADR/0004-refactoring-from-existing-architecture.html).
