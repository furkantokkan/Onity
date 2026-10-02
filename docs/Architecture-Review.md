---
title: "Architecture"
nav_order: 7
description: "How the Onity 0.8 package is put together: assembly boundaries, composition roots and scopes, DI activation paths, the reactive node design and the messaging model, the async boundary, factories and pools, Unity adapters and build-time tooling."
---

# Architecture

This page describes how the Onity package (`com.onity.framework`, 0.8.x) is put together: which
assembly owns what, where the composition roots are, how dependency injection activates types on JIT
and AOT runtimes, how the reactive, messaging and async layers share one lifetime model, and which
pieces are Unity adapters. Read it when you decide where new code belongs, when you review a dependency
direction, or when you need to know what a context does for you. The package source lives at
`Packages/com.onity.framework` in the repository; the `upm` branch mirrors that folder to the repository
root for installation.

Contents:

- [Design goals](#design-goals)
- [Runtime assemblies](#runtime-assemblies)
- [Composition roots and scopes](#composition-roots-and-scopes)
- [Dependency injection activation](#dependency-injection-activation)
- [Reactive and messaging model](#reactive-and-messaging-model)
- [Async boundary](#async-boundary)
- [Factories and pools](#factories-and-pools)
- [Unity adapters](#unity-adapters)
- [Build-time tooling](#build-time-tooling)
- [Intentional limits](#intentional-limits)
- [Verification](#verification)
- [Architecture decisions](#architecture-decisions)

## Design goals

1. Keep the dependency injection, reactive, messaging, factory and composition APIs free of
   `UnityEngine`, so domain code and EditMode tests use them without a scene.
2. Put Unity lifecycle, scenes, input, async, pooling, UI and DOTS behind explicit Unity-facing
   assemblies.
3. Keep the dependency direction acyclic and enforced by assembly definition references.
4. Give every object one owner. A scope owns the services it created, the subscriptions tied to it, the
   async work started with its token and the pools it created, and disposes them in a fixed order.
5. Keep the hot paths self-owned: no non-Unity third-party runtime dependency, no `System.Linq` in
   runtime code, and no per-call managed allocation on the resolve, publish, `OnNext`, frame-stream and
   pooled-task paths beyond the objects a call must create.

## Runtime assemblies

The assembly definitions are the source of truth for the dependency direction. "Engine-free" means the
asmdef sets `noEngineReferences`, so the assembly cannot reference `UnityEngine`.

| Assembly | References | Engine-free | Contents |
| --- | --- | --- | --- |
| `Onity.Core` | none | yes | `Unit`, `Lifetime`, `DisposableAction` |
| `Onity.Factory` | Core | yes | the `IFactory<...>` contracts |
| `Onity.DI` | Core, Factory | yes | `OnityContainer`, the binding builders, `[Inject]`, the lifecycle interfaces, `IOnityScopeLifetime`, the generated-activator registry, the DI exceptions |
| `Onity.Messaging` | Core | yes | `IMessageBroker`, `MessageBroker`, `MessageChannel<T>`, keyed and async channels |
| `Onity.Reactive` | Core | yes | `IOnityObservable<T>`, `Subject<T>`, `ReactiveProperty<T>`, the operators, time and frame providers, `CompositeDisposable`, the observable tracker, and its own copies of the IL2CPP code-generation attributes |
| `Onity.Composition` | Core, DI, Reactive, Messaging | yes | `BindReactiveProperty`, `BindSubject`, `DeclareMessage`, `DeclareAsyncMessage` |
| `Onity.Pooling` | Core, Factory | no (`PrefabComponentPool<T>` uses `UnityEngine`) | `IPool<T>`, `IParameterizedPool<T>`, `IPoolHooks`, `OnityObjectPool<T>`, `PrefabComponentPool<T>`, the `PooledFactory` adapters, pool diagnostics |
| `Onity.DOTS` | Core, Messaging, `Unity.Burst`, `Unity.Collections`, `Unity.Entities`, `Unity.Mathematics` | no | ECS system groups, the integer event bridge, the session bridge, entity pooling helpers; compiled under `ONITY_ENTITIES`, a version define on `com.unity.entities` 1.0.0 or newer |
| `Onity.Unity` | Core, DI, DOTS, Factory, Messaging, Reactive, Pooling, `Unity.Burst`, `Unity.InputSystem`, `Unity.Collections`, `Unity.Jobs` | no | contexts, installers, `OnityEvent`, `OnityEventHub`, the Unity reactive bridges, the Input System bridge, `OnityTask` and the async layer, UI Toolkit bridges, physics helpers, scene flow; version defines `ONITY_ENTITIES`, `ONITY_PHYSICS`, `ONITY_PHYSICS2D`, `ONITY_PARTICLESYSTEM` gate the optional parts |
| `Onity.Unity.UGUI` | Core, Unity, `UnityEngine.UI` | no | uGUI async events, EventSystems triggers and `BindTo`; compiled only when `com.unity.ugui` is installed (`ONITY_UGUI`) |

Every runtime assembly is auto-referenced, so scripts in Unity's predefined assemblies use Onity without
asmdef edits; a game asmdef lists the Onity assemblies it uses. Unity assembly references are not
transitive: an assembly that calls `Onity.Unity.Async` extension methods also references
`Onity.Reactive`, and `Onity.Messaging` for the messaging bridges.

Outside the runtime: `Onity.Editor` (the Editor windows, validation and assistant-guidance commands),
`Onity.Tests.EditMode`, `Onity.Tests.PlayMode` and `Onity.Tests.UGUI.PlayMode`, the benchmark assemblies
(`Onity.Benchmarks` and its Editor assembly under `ONITY_BENCHMARKS`, `Onity.TaskBenchmarks` and its
Editor assembly under `ONITY_TASK_BENCHMARKS`, `Onity.ReactiveBenchmarks` and its Editor assembly under
`ONITY_REACTIVE_BENCHMARKS`, `Onity.MessagingBenchmarks` and its Editor assembly under
`ONITY_MESSAGING_BENCHMARKS`; they reference the comparison libraries and compile only in the
development repository or a benchmark host), the `Analyzers/` folder with `Onity.Analyzers.dll`
and `Onity.SourceGen.dll`, and the generated `Documentation~/` folder.

## Composition roots and scopes

A scope is an `OnityContainer` with its children. In a Unity project the composition roots are the
contexts (`Onity.Unity.Contexts`), each a `MonoBehaviour` that owns one container:

| Context | Scope | Execution order | Parent |
| --- | --- | --- | --- |
| `ProjectContext` | Session-wide services; `ProjectContext.Instance`, `DontDestroyOnLoad`; loaded before the first scene by `ProjectContextBootstrap` from `Resources/Onity/ProjectContext` when none exists | `-10000` | none |
| `SceneContext` | One scope per scene, rebuilt on each load | `-9800` | its Project Context field, else `ProjectContext.Instance` |
| `GameObjectContext` | One scope per prefab or object subtree | `-9700` | the nearest `OnityContext` above it, else `ProjectContext.Instance` |

A context does the same work in the same order. In `Awake` it creates the container as a child of the
discovered parent's container, binds the defaults (the container, `IResolver`, `IOnityScopeLifetime`, the
context itself, `MessageBroker` with its interfaces, and `OnityEventHub`), runs the `MonoInstaller`
components in its Installers list, calls `Build()`, and member-injects every `MonoBehaviour` under its
own GameObject that a nearer context does not own. In `Start` it runs `BuildAsync(LifetimeToken)`, which
awaits the async build callbacks and every `IOnityAsyncInitializable`; `IsReady`, `ReadyTask` and
`WaitReadyAsync` cover that phase. `Update`, `FixedUpdate` and `LateUpdate` pump `Tick`, `FixedTick` and
`LateTick` into the container. `OnDestroy` disposes the container. Each default binding and each
installer step is labeled with `PushBindingSource`, which the Editor diagnostics read. Objects created
at runtime are injected with `InjectGameObject(root)` or `container.Inject(target)`.

Installers register bindings before `Build()`. The three lifetimes are `AsSingle()` (one instance per
declaring scope, shared with its children), `AsScoped()` (one instance per resolving scope, built with
that scope's dependencies) and `AsTransient()` (a new instance per resolve). `FromSubContainerResolve`
exports one contract from a child container installed once per requesting scope; the requesting scope
forwards ticks to that child and disposes it. `Unbind` and `Rebind` change a built scope; build callbacks
cannot be registered after the build.

The scope's lifetime is one `CancellationToken`. `OnityContainer.LifetimeToken` is created on first
request and linked to the parent's token; `OnityContainer` implements `IOnityScopeLifetime` and every
context binds that contract to its container, so a service injects the scope that owns it.
`OnityContext.LifetimeToken` is the container's token, or the context's destroy token before `Awake`
has created the container. `Dispose()` cancels the token first, then disposes the child scopes installed
by sub-container exports, the scoped instances, and the singletons and owned providers in reverse
registration order. Async work started with the token and subscriptions retained with `AddTo(scope)`
therefore end before the services they use are torn down. Instances passed to `BindInstance` stay
caller-owned; primitives and pools created by the binding helpers are scope-owned.

## Dependency injection activation

Every activation strategy builds the same object graph; only the per-constructor delegate differs:

1. Generated: `Onity.SourceGen` emits a direct constructor delegate for each type marked
   `[OnityGenerateActivator]` and registers it in `Onity.DI.Internal.GeneratedActivators` before
   construction plans are built. A registered activator is used first on every runtime, including
   IL2CPP.
2. Compiled: on JIT runtimes a one-time probe compiles and invokes a representative expression, and when
   it succeeds the container compiles constructor activators and member setters with
   `Expression.Compile`, once per constructor for the life of the process.
3. Reflection: when the probe fails (IL2CPP and other AOT or restricted runtimes) or one constructor
   fails to compile, that constructor uses cached reflection metadata. `OnityContainer.IsCompiledActivationSupported`
   reports which path the probe selected; it says nothing about generated activators.

Around the activators, the resolve path keeps its steady state free of per-call allocation: construction
plans are cached per type, constructor arguments travel through `[ThreadStatic]` pooled arrays, generic
`Resolve<T>()` of an explicit local binding reads a dense type-id provider slot instead of a dictionary,
and `Build()` bakes flat lifetime and singleton slots for the explicit bindings. Dynamic `Resolve(Type)`,
misses and parent fallback use the general provider map. A transient resolve still allocates the
instance it returns.

The resolution rules are part of the model: an unbound concrete class resolves as an implicit
transient; an unbound interface, abstract class or open generic definition throws
`OnityResolveException`; the last binding of a contract wins; a single `[Inject]` constructor is
selected, otherwise the public constructor with the most parameters, and a non-public constructor only
when no public one exists; constructor and member cycles are detected at resolve time; the seven
collection shapes (`IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IList<T>`,
`ICollection<T>`, `List<T>`, `T[]`) are synthesized from every explicit binding of `T` in the scope and
its ancestors; an open generic binding is closed and cached on the first resolve of each closed
contract; `WithId` and `WhenInjectedInto` are resolved per consumer, and two matching conditional
bindings throw. Managed dependency resolution stays on the main thread and outside Burst jobs; the DOTS
bridge passes blittable data across the managed and ECS boundary instead.

## Reactive and messaging model

`Subject<T>`, `ReactiveProperty<T>`, every operator and the message bridge `Observe<T>()` return
`IOnityObservable<T>`, so state, local streams and messages share one operator chain and one disposal
rule: every `Subscribe` returns an `IDisposable` that its owner retains, with `TakeUntilDisable(this)`
or `AddTo(this)` in a `MonoBehaviour`, `AddTo(scope)` in a scope service and `AddTo(compositeDisposable)`
elsewhere. `IReadOnlyReactiveProperty<T>` is the read-only face of a property, with `Value` and
`Subscribe`; it does not implement `IOnityObservable<T>`, so operators apply to the property itself or
to a wrapper.

Since 0.7.0 the core is built on subscription nodes. A subscription is one object, an internal node
(`OnityObserverNode<T>`) that is both the entry the source keeps and the `IDisposable` handed to the
subscriber; a callback node carries the `Action<T>` or `Observer<T>` the source invokes directly, and an
operator sink is a node that forwards to its downstream node. `Subject<T>` and `ReactiveProperty<T>` keep
their nodes in an array-backed `OnityNodeList<T>`: every node knows its index, removal outside a
notification moves the last node into the freed slot, removal during a notification clears the slot and
the list compacts in order after the outermost notification ends, and nodes added during a notification
are appended so the pass in progress reaches them. While exactly one live node is registered, `OnNext`
and `SetValue` deliver to it through a direct reference the list keeps, without the general loop, and
fall back to the loop when that callback subscribes, unsubscribes or disposes. A notification pass runs
in one exception region: a
throwing observer is reported to `OnityObservableExceptionHandler.Handler`, whose default is a no-op,
and delivery resumes with the next observer. Sources and the operator observables accept nodes directly
through an internal fast-path contract, so operator sinks and `Subscribe(Action<T>)` subscribe without
creating a delegate (a lambda passed to `Subscribe(Observer<T>)` takes one extra load and test per value,
because node delivery checks the action callback first). `Where`, `Select`, `DistinctUntilChanged`,
`Skip`, `SkipWhile`, `Take`, `TakeWhile`, `StartWith`, `Scan` and `Pairwise` are sink-based operators
derived from `OnityOperatorObservable<T>`, with `Where` followed by `Select` fused into one sink; the
remaining synchronous operators, the time operators and the bridges subscribe through the public path.
The hot classes turn off IL2CPP null and array-bounds checks through the assembly's own copies of the
`Unity.IL2CPP.CompilerServices` attributes, which IL2CPP recognizes by name. Steady-state `OnNext` and
`SetValue` allocate nothing; a subscription allocates its node.

The time operators wait on an `OnityTimeProvider` (`OnityTimeProvider.System` by default, the
`OnityTimeProviders` loop providers in gameplay); `ObserveOn` re-posts onto an `OnityFrameProvider`; the
thread-pool operators move pure managed work off the main thread and `ObserveOnMainThread` brings it
back. The core operators carry no error channel; `onError` is raised only by sources that report faults,
and `onCompleted` runs when a subscription is disposed.

Messaging is typed pub/sub over the scope's broker. `MessageBroker` owns one `MessageChannel<T>` per
message type, `OnityEventHub` wraps the broker with `Publish`, `Subscribe` and `Observe` (it implements
no interfaces and is bound as itself), and `OnityEvent` is the static shorthand for Unity code: its
no-owner members use the most recently activated `SceneContext`, then `ProjectContext.Instance`, then
any other active context, and the owner members use the context on the component or its nearest parent
with that default as fallback. `BindMessageChannel<T>` binds the broker's own publisher and subscriber,
so direction-only injection and `OnityEvent` share one channel; `DeclareMessage<T>` and
`DeclareAsyncMessage<T>` create standalone channels the broker never sees. Keyed and async channels are
explicit types. Since 0.8.0 a channel subscription is one object, like a reactive node: the slot entry
the channel keeps is the `IDisposable` the subscriber receives, it knows its index, and removal is a
constant-time slot swap outside a pass or a cleared slot compacted after the outermost pass;
`AsyncMessageChannel<T>` runs synchronously completing handlers inline and copies its handler array only
when a subscription is disposed while a pass holds it. A handler that throws propagates out of `Publish`
and the remaining handlers for that message are skipped, which is the opposite of the subject's behavior
and is documented as such.

## Async boundary

`OnityTask`, `OnityTask<T>` and `OnityTaskVoid` live in `Onity.Unity.Async`, because frame waits, scene
loading, `AsyncOperation` bridges, web requests, PlayerLoop timings and main-thread continuations are
engine-facing. The engine-free APIs keep BCL contracts where they fit (`BuildAsync` returns `Task`,
`PublishAsync` returns `ValueTask`, `FirstAsync` returns `Task<T>`), and the Unity layer adds the
`OnityTask` forms.

The async layer owns its method builders and task sources. A suspended `async OnityTask` method binds a
pooled runner that holds the state machine by value and returns to its pool when the task is consumed;
cancelable waits, timed waits, Unity operations and completion sources are pooled single-consumer
sources, so a task value is awaited once or `Preserve()`d before sharing. In Play, frame waits without a
cancelable token and `Yield()` are stateless and re-awaitable. The waits run on Onity's own PlayerLoop
nodes: the three after-script timings are installed before scene load, the other sixteen on first use.
Execution-context flow is off by default, as in UniTask, and `OnityTask.FlowExecutionContext` opts in;
`RunnerPoolCapacity` and `SourcePoolCapacity` cap what the pools retain.

This is also where the pillars meet. A service injects `IOnityScopeLifetime` and passes its token to
every loop; `component.GetScopeCancellationToken()` does the same for a `MonoBehaviour`;
`IOnityAsyncInitializable` is awaited by `BuildAsync` and `OnityContext.WaitReadyAsync` waits for it; an
`IReadOnlyReactiveProperty<T>` is awaited with `WaitAsync` and `WaitUntilAsync` or read as a conflating
stream; an `ISubscriber<T>` is read with `ReceiveAsync` and `ReceiveAllAsync`, and an `IAsyncSubscriber<T>`
gets a bounded queue with `SubscribeQueued`; `DeclareAsyncMessage` and `BindAsyncReactiveProperty` bind
the async primitives to a scope that disposes them. See [Async with OnityTask](guide/onitytask.html).

## Factories and pools

`IFactory<TValue>`, `IFactory<TParam, TValue>` and `IFactory<TParam1, TParam2, TValue>` are the
engine-free contracts; `BindFactory` binds a user-authored factory with `BindInterfacesAndSelfTo<TFactory>().AsSingle()`,
and there is no `Instantiate(args)` or fluent factory body. `OnityObjectPool<T>` keeps inactive items in
its own array stack, with an optional duplicate-return check that runs in players too, and never calls
`IPoolHooks`; `PrefabComponentPool<T>` instantiates clones inactive, activates on get, deactivates on
release and calls `IPoolHooks` on the component. Both implement `IParameterizedPool<T>`, whose
`initialize` callback runs before activation and the get hooks, and the `PooledFactory` adapters turn a
pool into an `IFactory<...>`. `BindPooledFactory(prefab, ...)` creates a prefab pool the scope owns and
disposes; a pool passed to `BindPooledFactory(pool)` stays caller-owned until `pool.AddTo(container)`.

## Unity adapters

- Contexts and `MonoInstaller`, with `BindScriptableObject` for designer-authored configuration and
  `BindUiResolverBridge` for the UI Toolkit presenter bridge.
- `OnityEvent`, `OnityEventHub` and the component extensions over the scope broker.
- Frame loops, timers and lifetime helpers (`Onity.Unity.Reactive`): `OnityUnityObservable`, the
  `OnityFrameProviders` and `OnityTimeProviders`, `OnityCountdownTimer`, `OnityIntervalTimer`,
  `OnityStopwatchTimer`, and the `OnityLifetimeNotifier` component that `AddTo(this)` and
  `TakeUntilDisable(this)` attach to the owner.
- The Input System bridge (`Onity.Unity.Input`): the `InputAction` observables and
  `OnityReactiveInputPlayer`, compiled with `ENABLE_INPUT_SYSTEM`.
- The async layer: PlayerLoop timings and timers, triggers, `UnityEvent`, UI Toolkit and optional uGUI
  events, scene loading, web requests and the Unity operation adapters.
- UI Toolkit presenters (`Onity.Unity.UI`): `OnityUiResolverBridge`, `OnityUiPresenter<TView>`,
  `OnityUiPresenterFactory`, `OnityUiServiceLocator` and `[OnityUiInject]`.
- Physics helpers (`Onity.Unity.Physics`): `OnityNonAllocPhysics` and `OnityRaycastCommandBatch`.
- Scene flow (`Onity.Unity.SceneFlow`): `OnitySceneFlow`, `OnitySceneFlowProfile`,
  `OnitySceneFlowStateMachine`, `OnitySceneTransitionStore`, the scene initiators and the loading view.
- DOTS (`Onity.DOTS`): the Onity system groups, `OnityDotsIntEventBridge` and its accumulate and
  bootstrap systems, `OnityDotsSessionBridge`, and `OnityDotsPoolEntityUtils` with the pooled entity
  tags.

## Build-time tooling

- `Onity.SourceGen` generates constructor activators for `[OnityGenerateActivator]` types; the DLL ships
  in `Analyzers/`.
- `Onity.Analyzers` (`ONITY001` to `ONITY006`) flags a `Resolve` in an update method, a binding after
  `Build()`, a dropped `Subscribe` result, multiple `[Inject]` constructors, an `[Inject]` member that
  cannot be injected, and `new` on a type the same file binds or resolves; the source is under
  `tools/Onity.Analyzers`, with a code fix for `ONITY001`.
- The Editor windows `Onity/Tools/Monitor`, `Onity/Tools/Container Diagnostics`, `Onity/Tools/Task Tracker`,
  `Onity/Tools/Observable Tracker`, `Onity/Tools/Pool Monitor` and `Onity/Tools/Scene Flow Manager`; the
  commands `Onity/Validation/Validate Scene`, `Onity/Validation/Validate All Scenes`,
  `Onity/Contexts/Create ProjectContext Prefab`, `Onity/AI/Install Assistant Guidance...` and
  `Onity/AI/Check Assistant Guidance`.
- The benchmark runners under `Benchmarks/`, gated behind their defines (`ONITY_BENCHMARKS` for DI,
  `ONITY_TASK_BENCHMARKS` for OnityTask, `ONITY_REACTIVE_BENCHMARKS` for the reactive comparison with R3
  and UniRx, `ONITY_MESSAGING_BENCHMARKS` for the messaging comparison with MessagePipe), and the host
  tools under `tools/benchmark-host`.
- `tools/docs/sync-package-docs.py`, which generates `Documentation~/` from `docs/` and the `onity-use`
  skill, and whose `--check` mode runs in CI.

These are build, Editor, test and benchmark inputs, not runtime dependencies of gameplay code.

## Intentional limits

- Messaging ships no buffered or replayed messages, no handler priority and no request-response; current
  state is a `ReactiveProperty<T>`, a query is a service call.
- `IOnityObservable<T>` ships no `Window`, `Zip`, `Switch`, `Concat`, `Publish`, `Share` or `RefCount`,
  no `Never`, `Create` or `Defer` factory, and `CombineLatest` over two sources; the pull-based async
  streams carry `Zip`, `Concat`, `Publish` and `Queue`.
- The container ships no `Instantiate(args)`, no fluent factory bodies, no `Func<>` factory registration
  and no optional injection. Identified and conditional bindings, `Unbind`, `Rebind` and sub-container
  exports do ship.
- Managed dependency injection does not run inside Burst, and the runtime has no non-Unity third-party
  dependency.
- Standalone .NET and other engines are out of scope.

## Verification

Each release's CHANGELOG `Tested` section records the Editor version, the EditMode and PlayMode test
counts and the Player smoke results for that release; the GitHub workflows build the engine-free
assemblies on every push, run the Unity test jobs when the repository's Unity license secret is
configured, and check that the generated `Documentation~/` copy and the docs site are up to date. The
measured comparisons, with their conditions, are on the [Comparisons](comparisons/index.html) pages; the
v0.3.10 verification snapshot that this page used to carry is in
[Measurement history](archive/di-benchmark-history.html).

## Architecture decisions

- [ADR 0001: DOTS and DI Performance](ADR/0001-dots-and-di-performance.html)
- [ADR 0002: Reactive Thread-Pool Scheduling](ADR/0002-reactive-thread-pool-scheduling.html)
- [ADR 0003: OnityEvent Shortcuts](ADR/0003-unity-event-shortcuts.html)
- [ADR 0004: Refactoring from Existing Architecture](ADR/0004-refactoring-from-existing-architecture.html)

The resulting dependency graph points downward only, the core stays testable without a scene, and
Unity-specific behavior is isolated at explicit adapters.
