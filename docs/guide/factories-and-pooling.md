---
title: "Factories & Pooling"
parent: "Guides"
nav_order: 5
---

# Factories & Pooling

Onity has two factory styles:

- `BindFactory<...>()` for explicit runtime-argument construction.
- `BindPooledFactory(...)` for the common Unity prefab pool case.

The factory contracts live in the engine-free `Onity.Factory` assembly. Pooling
helpers live in `Onity.Pooling`; Unity prefab pooling is wired through
`Onity.Unity.Installers`.

## Runtime-argument factory

Use an explicit `IFactory<...>` when a created object needs a runtime value, such
as an enemy id, spawn config, or level seed.

```csharp
using Onity.DI;
using Onity.Factory;

public readonly struct EnemySpawnRequest
{
    public readonly string Id;
    public readonly int Level;

    public EnemySpawnRequest(string id, int level)
    {
        Id = id;
        Level = level;
    }
}

public sealed class Enemy
{
    public Enemy(string id, int level, IClock clock)
    {
        Id = id;
        Level = level;
        Clock = clock;
    }

    public string Id { get; }
    public int Level { get; }
    public IClock Clock { get; }
}

public sealed class EnemyFactory : IFactory<EnemySpawnRequest, Enemy>
{
    private readonly IResolver m_resolver;

    public EnemyFactory(IResolver resolver)
    {
        m_resolver = resolver;
    }

    public Enemy Create(EnemySpawnRequest request)
    {
        return new Enemy(request.Id, request.Level, m_resolver.Resolve<IClock>());
    }
}
```

Register and use it:

```csharp
container.Bind<IClock>().To<GameClock>().AsSingle();
container.BindFactory<EnemySpawnRequest, Enemy, EnemyFactory>();

IFactory<EnemySpawnRequest, Enemy> factory = container.Resolve<IFactory<EnemySpawnRequest, Enemy>>();
Enemy enemy = factory.Create(new EnemySpawnRequest("elite", 5));
```

`BindFactory` has zero-, one-, and two-parameter overloads:

```csharp
container.BindFactory<Enemy, EnemyFactory>();                         // IFactory<Enemy>
container.BindFactory<EnemySpawnRequest, Enemy, EnemyFactory>();      // IFactory<EnemySpawnRequest, Enemy>
container.BindFactory<string, int, Enemy, EnemyFactory>();            // IFactory<string, int, Enemy>
```

## Prefab pooled factory

For normal projectile, VFX, enemy, pickup, or UI item pooling, bind the prefab
once from a `MonoInstaller`. One call registers both:

- `IPool<TComponent>`
- `IFactory<TComponent>`

```csharp
using Onity.DI;
using Onity.Unity.Installers;
using UnityEngine;

public sealed class CombatInstaller : MonoInstaller
{
    [SerializeField] private Projectile m_projectilePrefab;
    [SerializeField] private Transform m_projectileRoot;

    public override void InstallBindings(OnityContainer container)
    {
        container.BindPooledFactory(
            m_projectilePrefab,
            m_projectileRoot,
            defaultCapacity: 32,
            maxSize: 256);

        container.Bind<ProjectileSpawner>().AsSingle();
    }
}
```

Inject the factory to spawn and the pool to release:

```csharp
using Onity.Factory;
using Onity.Pooling;
using UnityEngine;

public sealed class ProjectileSpawner
{
    private readonly IFactory<Projectile> m_projectileFactory;
    private readonly IPool<Projectile> m_projectilePool;

    public ProjectileSpawner(
        IFactory<Projectile> projectileFactory,
        IPool<Projectile> projectilePool)
    {
        m_projectileFactory = projectileFactory;
        m_projectilePool = projectilePool;
    }

    public Projectile Spawn(Vector3 position, Vector3 velocity)
    {
        Projectile projectile = m_projectileFactory.Create();
        projectile.transform.position = position;
        projectile.Launch(velocity, Release);
        return projectile;
    }

    private void Release(Projectile projectile)
    {
        m_projectilePool.Release(projectile);
    }
}
```

Use `IPoolHooks` on the pooled component for reset logic. `PrefabComponentPool<T>`
activates the GameObject on get and deactivates it on release.

```csharp
using System;
using Onity.Pooling;
using UnityEngine;

public sealed class Projectile : MonoBehaviour, IPoolHooks
{
    private Action<Projectile> m_release;
    private Vector3 m_velocity;

    public void Launch(Vector3 velocity, Action<Projectile> release)
    {
        m_velocity = velocity;
        m_release = release;
    }

    public void Despawn()
    {
        m_release?.Invoke(this);
    }

    public void OnPoolGet()
    {
        m_velocity = Vector3.zero;
        m_release = null;
    }

    public void OnPoolRelease()
    {
        m_velocity = Vector3.zero;
        m_release = null;
    }
}
```

## Parameterized pooled spawn

`BindPooledFactory(prefab)` binds a zero-parameter `IFactory<TComponent>`. If you
want `Create(position)` or `Create(position, velocity)`, wrap the pool in your
own factory:

```csharp
using Onity.Factory;
using Onity.Pooling;
using UnityEngine;

public sealed class ProjectileAtPositionFactory : IFactory<Vector3, Projectile>
{
    private readonly IPool<Projectile> m_pool;

    public ProjectileAtPositionFactory(IPool<Projectile> pool)
    {
        m_pool = pool;
    }

    public Projectile Create(Vector3 position)
    {
        Projectile projectile = m_pool.Get();
        projectile.transform.position = position;
        return projectile;
    }
}
```

Register the prefab pool first, then the parameterized wrapper factory:

```csharp
container.BindPooledFactory(m_projectilePrefab, m_projectileRoot);
container.BindFactory<Vector3, Projectile, ProjectileAtPositionFactory>();
```

Now gameplay code can ask for the parameterized factory:

```csharp
IFactory<Vector3, Projectile> factory = container.Resolve<IFactory<Vector3, Projectile>>();
Projectile projectile = factory.Create(spawnPosition);
```

## Plain C# object pool

For non-Unity objects, use `OnityObjectPool<T>` directly. It wraps
`UnityEngine.Pool.ObjectPool<T>` and still exposes the common `IPool<T>`
contract.

```csharp
using Onity.Factory;
using Onity.Pooling;
using Onity.Unity.Installers;

using OnityObjectPool<PathNode> pool = new OnityObjectPool<PathNode>(
    createFunc: () => new PathNode(),
    actionOnGet: node => node.Reset(),
    actionOnRelease: node => node.Clear(),
    defaultCapacity: 64,
    maxSize: 1024,
    diagnosticsName: "PathNodePool");

PathNode node = pool.Get();
pool.Release(node);
```

You can also bind an existing pool as a factory:

```csharp
using Onity.Factory;
using Onity.Pooling;
using Onity.Unity.Installers;

IPool<PathNode> pool = new OnityObjectPool<PathNode>(() => new PathNode());
container.BindPooledFactory(pool);

IFactory<PathNode> factory = container.Resolve<IFactory<PathNode>>();
PathNode node = factory.Create();
```

## Is it easier than Zenject or VContainer?

For common Unity pooled prefab spawning, yes: Onity is intentionally simpler.
`container.BindPooledFactory(prefab)` binds the pool and factory in one line, and
gameplay code receives plain `IFactory<T>` / `IPool<T>` contracts.

| Use case | Onity | Zenject | VContainer |
| --- | --- | --- | --- |
| Plain runtime-argument factory | Explicit `IFactory<...>` + `BindFactory` | Powerful `BindFactory` / `PlaceholderFactory` chains | Register a factory delegate or factory type |
| Prefab pooled factory | `BindPooledFactory(prefab)` binds `IFactory<T>` and `IPool<T>` | Usually `MemoryPool` / `MonoMemoryPool` plus installer wiring | DI-only; usually combine registration with Unity/ObjectPool code |
| Reset lifecycle | Optional `IPoolHooks` on the component | Pool callbacks / `OnSpawned` / `OnDespawned` patterns | Custom pool lifecycle |
| Advanced fluent factory bodies | Explicit C# factory class | Broadest feature set (`FromMethod`, `FromIFactory`, prefab helpers) | Concise delegates, but less built-in pooling surface |

Onity's tradeoff is deliberate: fewer factory concepts, less fluent magic, and a
clear C# factory class when construction needs custom logic. Zenject still has
the broadest advanced factory feature set; VContainer stays very lean and
DI-focused. Onity is easiest when you want one package to handle DI, factories,
pooling, messaging, reactive state, diagnostics, and Unity context wiring
together.

## See also

- [Dependency Injection](dependency-injection.html) — binding, resolving, and constructor injection.
- [Lifecycle & Scopes](lifecycle-and-scopes.html) — where installers and contexts build the container.
- [Performance & IL2CPP](performance-and-il2cpp.html) — generated activators and hot-path notes.
