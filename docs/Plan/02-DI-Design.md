# 02 - DI Design

This is the headline design document for `Onity.DI`. It captures the current
implementation, the performance gap, the target rewrite, and the public API
contract.

## 1. Current implementation summary

**Location:** `Assets/Onity-Packages/Onity/Runtime/DI/Scripts/`

**Files:**

- `OnityContainer.cs` (~60 KB, the entire container)
- `TypeBindingBuilder.cs` (fluent `Bind<T>().To<C>()`)
- `MultiTypeBindingBuilder.cs` (`BindInterfacesAndSelfTo<T>`,
  `BindInterfacesTo<T>`)
- `IResolver.cs` (resolve contract)
- `InjectAttribute.cs` (constructor / field / property / method marker)
- `OnityBindingException.cs`
- `OnityResolveException.cs`

**Internal types inside `OnityContainer.cs`:**

- `IProvider`, `InstanceProvider`, `TransientProvider`, `SingletonProvider`
- `TypeInjectionPlan`, `InjectedField`, `InjectedProperty`, `InjectedMethod`
- `BindingSourceRecord`, `BindingSourceScope`, `ArgumentArrayPool`

**Public API surface (already shipped):**

- `Bind<T>().To<C>().AsSingle()/AsTransient()/NonLazy()`
- `BindInterfacesAndSelfTo<T>()`, `BindInterfacesTo<T>()`
- `BindInstance<T>(T instance)`
- `BindFactory<TValue, TFactory>()` and 1/2 param overloads
- `RegisterBuildCallback(...)`, `RegisterBuildCallbackAsync(...)`
- `Resolve<T>()`, `TryResolve<T>(out T)`, `Inject(object target)`
- `Build()`, `BuildAsync(CancellationToken)`
- `GetDiagnostics()`, `GetBindingDiagnostics(List<...>)`

This surface stays. The rewrite is internal.

## 2. Performance gap

| Scenario | Onity (ns/op) | VContainer (ns/op) | Gap |
|---|---:|---:|---:|
| Resolve Singleton | 175 | 187 | Onity +6.24% |
| Resolve Transient | 2,088 | 1,801 | Onity -15.91% |
| Resolve Combined | 2,163 | 1,833 | Onity -18.02% |
| Resolve Complex | 52,386 | 40,916 | Onity -28.03% |

The single bottleneck is the activator call in `CreateInstance` at
`OnityContainer.cs:803`:

```csharp
return plan.Constructor.Invoke(arguments);
```

`ConstructorInfo.Invoke` does:

- Per-call argument validation
- Reflection metadata lookup
- A boxing pass for value-type parameters
- An internal `RuntimeMethodHandle` dispatch

VContainer pre-compiles activator delegates at build time and calls
`activator(args)` directly. This is the gap we close in Phase 1.

A secondary cost is `m_providerMap`, a
`Dictionary<Type, IProvider>` keyed by `Type`. Each resolve does at minimum
one dictionary lookup. Complex graphs do many. Replacing the dictionary with
a slot-indexed array reduces this further.

## 3. Target design - "BakedContainer"

The container has two lifecycle phases:

1. **Registration phase** (before `Build()`): user adds bindings, registration
   is dictionary-backed and ergonomic.
2. **Resolve phase** (after `Build()`): registrations are baked into flat
   arrays with compiled activators. The dictionary is kept for `TryResolve`
   diagnostics, but the hot path uses arrays.

The same `OnityContainer` instance handles both phases. There is no separate
`BakedContainer` type exposed to users.

### 3.1 Baked binding layout

```csharp
internal readonly struct BakedBinding
{
    public readonly int ContractTypeId;     // hashed type handle
    public readonly int ProviderSlot;       // index into m_activators
    public readonly Lifetime Lifetime;      // Singleton / Transient / Instance
    public readonly int FirstDependency;    // index into m_dependencyList
    public readonly int DependencyCount;
}

internal sealed class BakedGraph
{
    public BakedBinding[] m_bindings;          // sorted by ContractTypeId
    public int[] m_dependencyList;             // flattened constructor deps
    public ActivatorDelegate[] m_activators;   // compiled per provider
    public object[] m_singletonCache;          // null until first resolve
    public TypeInjectionPlan[] m_planByProviderSlot;
}
```

### 3.2 Type ID cache

```csharp
internal static class TypeIdCache<T>
{
    public static readonly int Id = TypeIdRegistry.Register(typeof(T));
}

internal static class TypeIdRegistry
{
    private static readonly Dictionary<Type, int> s_map =
        new Dictionary<Type, int>(256);
    private static int s_next;

    public static int Register(Type type)
    {
        lock (s_map)
        {
            if (s_map.TryGetValue(type, out int id))
            {
                return id;
            }

            id = s_next++;
            s_map.Add(type, id);
            return id;
        }
    }
}
```

`Resolve<T>` becomes:

```csharp
public T Resolve<T>()
{
    int id = TypeIdCache<T>.Id;
    if (TryResolveBaked(id, out object instance))
    {
        return (T)instance;
    }
    return (T)ResolveSlow(typeof(T));
}
```

`TryResolveBaked` is a binary search on `m_bindings` sorted by
`ContractTypeId`. For an N=256 graph, this is ~8 comparisons. For dense IDs
we can switch to a direct array lookup keyed by ID (sparse arrays acceptable
for production graphs of ~500 types).

### 3.3 Compiled activator

```csharp
internal delegate object ActivatorDelegate(object[] args);

internal static ActivatorDelegate Compile(ConstructorInfo ctor)
{
    ParameterInfo[] parameters = ctor.GetParameters();
    var argsParam = Expression.Parameter(typeof(object[]), "args");

    var argExprs = new Expression[parameters.Length];
    for (int i = 0; i < parameters.Length; i++)
    {
        argExprs[i] = Expression.Convert(
            Expression.ArrayIndex(argsParam, Expression.Constant(i)),
            parameters[i].ParameterType);
    }

    var newExpr = Expression.New(ctor, argExprs);
    var bodyExpr = Expression.Convert(newExpr, typeof(object));
    return Expression.Lambda<ActivatorDelegate>(bodyExpr, argsParam).Compile();
}
```

`CreateInstance` becomes:

```csharp
private object CreateInstance(int providerSlot)
{
    ActivatorDelegate activator = m_baked.m_activators[providerSlot];
    int depStart = m_baked.m_bindings[providerSlot].FirstDependency;
    int depCount = m_baked.m_bindings[providerSlot].DependencyCount;

    if (depCount == 0)
    {
        return activator(Array.Empty<object>());
    }

    object[] args = ArgumentArrayPool.Rent(depCount);
    try
    {
        for (int i = 0; i < depCount; i++)
        {
            int depTypeId = m_baked.m_dependencyList[depStart + i];
            args[i] = ResolveById(depTypeId);
        }
        return activator(args);
    }
    finally
    {
        ArgumentArrayPool.Return(args, depCount);
    }
}
```

### 3.4 AOT / IL2CPP compatibility

`Expression.Compile()` is supported on IL2CPP but emits via the interpreter
path on some platforms (slower than JIT but still faster than reflection
`Invoke`).

If profiling on iOS / WebGL shows the interpreter path is not fast enough,
Phase 2 adds source generation (`Onity.SourceGen`) that emits the same
`ActivatorDelegate` shape ahead of time. The runtime code path does not
change - the generator produces a static class with
`[ModuleInitializer]` registration of activators keyed by type.

This is **not** a Phase 1 task. Phase 1 ships `Expression.Compile()` and
measures.

### 3.5 Singleton caching

`m_singletonCache[providerSlot]` is `null` until first resolve, then holds the
instance. No dictionary lookup. No lock when `OnityContainer` is used from a
single thread (the documented use case). If we add multi-thread resolve
support later, this slot becomes a `Volatile.Read` / double-checked init.

### 3.6 Injection plan slimming

`TypeInjectionPlan` (line ~1485 in `OnityContainer.cs`) currently stores
`FieldInfo[]`, `PropertyInfo[]`, `MethodInfo[]`. Field and property
assignment via `SetValue` is reflection-bound. Compile each into a setter
delegate:

```csharp
internal delegate void FieldSetterDelegate(object target, object value);

internal static FieldSetterDelegate CompileFieldSetter(FieldInfo field)
{
    var targetParam = Expression.Parameter(typeof(object), "target");
    var valueParam = Expression.Parameter(typeof(object), "value");
    var assignExpr = Expression.Assign(
        Expression.Field(
            Expression.Convert(targetParam, field.DeclaringType),
            field),
        Expression.Convert(valueParam, field.FieldType));
    return Expression.Lambda<FieldSetterDelegate>(
        assignExpr, targetParam, valueParam).Compile();
}
```

Property setters and method invocations follow the same pattern.
`Inject(object)` becomes a sequence of compiled-delegate calls plus
resolve lookups.

## 4. Public API contract

### 4.1 Binding builders

```csharp
container.Bind<IInputService>()
    .To<KeyboardInputService>()
    .AsSingle();

container.Bind<IInputService>()
    .FromInstance(existingInstance);

container.BindInterfacesAndSelfTo<PlayerStateService>()
    .AsSingle()
    .NonLazy();

container.BindInterfacesTo<GameLoopRunner>()
    .AsSingle();

container.Bind<IPathfinder>()
    .To<AStarPathfinder>()
    .AsTransient();
```

### 4.2 Factory binding

```csharp
container.BindFactory<Enemy, EnemyFactory>();
container.BindFactory<EnemySpawnRequest, Enemy, EnemyFactory>();
container.BindFactory<Vector3, Quaternion, Bullet, BulletFactory>();
```

User implements `IFactory<T>` / `IFactory<TParam, T>` /
`IFactory<TParam1, TParam2, T>`.

### 4.3 Pooled factory (lives in `Onity.Unity` because it touches prefabs)

```csharp
container.BindPooledFactory<Bullet, BulletFactory>(
    prefab,
    initialSize: 64,
    maxSize: 256);
```

Defined in `Onity.Unity` as an extension method on `OnityContainer`.

### 4.4 Resolve

```csharp
var input = container.Resolve<IInputService>();

if (container.TryResolve<IPathfinder>(out var pathfinder))
{
    // optional dependency
}

container.Inject(monoBehaviour);
```

### 4.5 Build lifecycle

```csharp
container.RegisterBuildCallback(resolver =>
{
    var loop = resolver.Resolve<IGameLoopRunner>();
    loop.Start();
});

container.RegisterBuildCallbackAsync(async (resolver, ct) =>
{
    var saves = resolver.Resolve<ISaveLoader>();
    await saves.PrimeAsync(ct);
});

container.Build();
await container.BuildAsync(cancellationToken);
```

`Build()` finalizes the binding map. After `Build()`:

- New bindings throw `OnityBindingException`.
- Resolves take the baked fast path.

`BuildAsync` runs sync build callbacks first, then async callbacks. Result is
cached; subsequent calls return the same task.

## 5. Contexts (`Onity.Unity`)

These already exist at `Runtime/Unity/Scripts/Contexts/`:

- `ProjectContext` - boots once per session via `RuntimeInitializeOnLoadMethod`
- `SceneContext` - per scene, parent is `ProjectContext`
- `GameObjectContext` - per game object, parent is `SceneContext`

`OnityContext` base class drives the lifecycle:

1. `Awake`: create container with parent, register defaults, run installers.
2. `Build()`.
3. Auto-inject hierarchy.
4. `Start`: `BuildAsync()` for async post-build.

The rewrite does not change context APIs. It only changes what
`Resolve`/`Inject` do internally.

## 6. Migration strategy

The rewrite is internal. No public type changes. No installer code changes.

To de-risk:

1. Implement `BakedGraph` and compiled activators behind an internal feature
   flag: `OnityContainer.UseBakedResolve` (default true after CI green).
2. Run the **entire** benchmark suite and the EditMode test suite under both
   paths.
3. Leave the reflection path in for one release as a fallback, gated by the
   flag. Remove it the release after.

This avoids a "big bang" rewrite that could regress edge cases (e.g. generic
constraints, value-type dependencies, derived-type registration).

## 7. Acceptance criteria

Phase 1 ships when:

- `Resolve Transient` ns/op is **<= 1500** (current VContainer is 1801).
- `Resolve Complex` ns/op is **<= 35000** (current VContainer is 40916).
- `Resolve Singleton` ns/op is **<= 150** (current 175).
- All scenarios report **0 bytes per sample** allocation.
- All existing `Onity.Tests.EditMode` tests pass.
- Compile succeeds on IL2CPP / Mono / .NET Standard 2.1.
- A new test class `OnityBakedContainerTests` covers:
  - Transient resolve correctness vs reflection path.
  - Singleton caching identity.
  - Inject member ordering (fields before properties before methods).
  - Multi-constructor selection (attributed > most params).
  - Generic and array dependency resolution.
  - Re-entrant resolve detection still works.

## 8. Out of scope for Phase 1

- Multi-thread resolve (documented single-thread guarantee stays).
- Source-generated activators (Phase 2 if needed).
- Conditional bindings (`WhenInjectedInto<T>`).
- Signal bus (use `Onity.Messaging` instead).
- Sub-containers beyond the three documented contexts.

## 9. References

- Current code: `Assets/Onity-Packages/Onity/Runtime/DI/Scripts/`
- Benchmark code: `Packages/com.onity.framework/Benchmarks/Editor/Scripts/OnityDiBenchmarkRunner.cs`
- Benchmark results: `Packages/com.onity.framework/Benchmarks/Results/di-benchmark-summary.md`
- Engineering doc: `Assets/Onity-Packages/Onity/ENGINEERING.md` (sections 4-6)
- Agent rules: root `AGENTS.md`
- Style guide: root `codex-code-style.md`
