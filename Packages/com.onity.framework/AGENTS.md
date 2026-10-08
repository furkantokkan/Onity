# Onity for AI coding agents

Onity (`com.onity.framework`) is a Unity package for dependency injection, reactive state, typed messaging,
factories and pooling, and `OnityTask` async, with one lifetime model: a scope owns its services,
subscriptions, tasks and pools and disposes them together. Read this file before you write or change code
that uses it.

Paths below are relative to this folder: `Packages/com.onity.framework` for an embedded or local package, or
`Library/PackageCache/com.onity.framework@<hash>` for a Git or registry install.

## Read first

1. `Documentation~/AI/Onity-AI-Usage-Guide.md`: the rules, the canonical example, the API index and the
   error table. Always start here.
2. The guide for the feature you are touching, under `Documentation~/guide/`: `dependency-injection.md`,
   `lifecycle-and-scopes.md` (also scene transitions and the scene service), `reactive.md`,
   `events-messaging.md`, `onitytask.md`, `factories-and-pooling.md`, `performance-and-il2cpp.md`,
   `refactoring-from-existing-architecture.md`.
3. `Documentation~/reference/` for the complete API catalogs, `Documentation~/Migration/` when porting from
   Zenject, VContainer, R3, UniRx or UniTask, and `Documentation~/Getting-Started.md` for the scene the
   usage guide's example comes from.

`llms.txt` lists every shipped page with a one-line description. Read only the pages the task needs.

The installed package source (`Runtime/`) and its tests (`Tests/`) are the authority. If a guide and the code
disagree, follow the code and tell the user about the mismatch.

## Rules that prevent the most common mistakes

- Do not invent Onity APIs from Zenject, VContainer, R3, UniRx, MessagePipe or UniTask analogies. If a type or
  method is not in the guide or the source, it does not exist. There is no `Instantiate(args)`, no fluent
  factory body and no optional injection; `WithId`, `WhenInjectedInto`, `AsScoped()`, `Unbind`, `Rebind` and
  `FromSubContainerResolve` do exist.
- Register services in a `MonoInstaller` owned by a `ProjectContext`, `SceneContext` or `GameObjectContext`.
  A fluent `Bind<T>()` registers nothing until you call `AsSingle()`, `AsScoped()` or `AsTransient()`.
  Bindings belong in installers, before `Build()`; after the build only `Unbind` and `Rebind` change a scope,
  and build callbacks throw.
- Keep domain logic in plain C# services with constructor injection and keep `MonoBehaviour`s thin, with
  `[Inject]` members. The container selects a single `[Inject]` constructor, otherwise the public constructor
  with the most parameters; a non-public constructor is used only when no public one exists. Never call
  `Resolve` from `Update`, `FixedUpdate` or `LateUpdate`.
- Request a collection to receive every binding of a contract: `IEnumerable<T>`, `IReadOnlyList<T>`,
  `IReadOnlyCollection<T>`, `IList<T>`, `ICollection<T>`, `List<T>` or `T[]`. A plain `Resolve<T>()` returns
  the last binding.
- Every reactive and messaging `Subscribe` returns an `IDisposable`; retain it. Use `TakeUntilDisable(this)`
  for subscriptions made in `OnEnable`, `AddTo(this)` for ones that live until destroy,
  `AddTo(compositeDisposable)` in plain C#, and `AddTo(scope)` with an injected `IOnityScopeLifetime` in a
  scope service. There is no `AddTo(GameObject)` overload. The async-stream `Subscribe` overloads that take a
  `CancellationToken` return nothing; the token owns the lifetime.
- `IReadOnlyReactiveProperty<T>` is not an `IOnityObservable<T>`: it has `Value` and `Subscribe` only. Inject
  the `ReactiveProperty<T>` itself where an operator such as `Where` is needed.
- Use a message for a one-shot notification and `ReactiveProperty<T>` for current state (the guide's events
  decision rule). `BindMessageChannel<T>()` binds the scope broker's channel, the one `OnityEvent` and
  `OnityEventHub` use; `DeclareMessage<T>()` creates a standalone channel the broker never sees, which is for
  private channels and scene-free tests only.
- A message handler that throws propagates out of `Publish` and skips the remaining handlers; a subscriber
  that throws inside `Subject<T>.OnNext` goes to `OnityObservableExceptionHandler.Handler`, which does nothing
  until you assign one. Catch inside a handler when one listener must not break the others.
- Use `OnityTask` (`Onity.Unity.Async`) for Unity async code, not UniTask. Pooled `OnityTask` values are
  single-consumer: await each once, or call `Preserve()` to share one. Give every async loop a token from its
  owner (`GetCancellationTokenOnDestroy()`, `GetScopeCancellationToken()`, `IOnityScopeLifetime.Token`), use
  `async OnityTaskVoid` or `Forget(handler)` for fire-and-forget work, and never write `async void`.
- An assembly definition that calls `Onity.Unity.Async` extension methods must reference `Onity.Reactive`
  (and `Onity.Messaging` for the messaging bridges), and one that touches `IOnityObservable<Unit>` (for
  example `OnityUnityObservable.EveryUpdate()`) must reference `Onity.Core`, where `Unit` lives, or the
  compiler reports CS0012.
- The core assemblies (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Factory`,
  `Onity.Composition`) are engine-free. Keep `UnityEngine` out of them and out of domain logic.
- Call `ObserveOnMainThread()` before touching Unity objects after `SelectAwait`, `WhereAwait` or the
  thread-pool operators. Publish and subscribe on the main thread only.
- Change scenes through one `IOnitySceneService` bound on the `ProjectContext` with
  `container.BindSceneService(...)`, never on a `SceneContext`, whose scene a Single load unloads. Check the
  awaited `OnitySceneResult`: `Busy`, `InvalidTarget` and readiness failures are results, not exceptions.
  Unload with the `Scene` a result returned, not a name. The destination reads its enter data with
  `OnitySceneTransitionStore.TryGetEnterData<T>(gameObject.scene, out T)` while it installs.
- Release every pooled item exactly once and never touch it afterwards. `PrefabComponentPool<T>` calls
  `IPoolHooks`; `OnityObjectPool<T>` runs only its `actionOnGet` and `actionOnRelease` delegates. The scope
  disposes the pool that `BindPooledFactory(prefab, ...)` created; a pool you build stays yours unless you call
  `pool.AddTo(container)`.

## Install the skill into the project (optional)

The package can copy the `onity-use` agent skill and the usage guide into the project, so assistants find them
without reading `Library/PackageCache`. This is an explicit user action. If the project has no
`.claude/skills/onity-use` or `.agents/skills/onity-use` folder, suggest it to the user instead of copying files
yourself.

- In the Unity Editor run `Onity/AI/Install Assistant Guidance...`. A dialog lists every file first, and nothing
  is written until the user confirms. It writes `SKILL.md`, `references/Onity-AI-Usage-Guide.md` and a
  `.onity-version` stamp into both `.claude/skills/onity-use/` and `.agents/skills/onity-use/`. A separate choice
  in the same dialog also adds an `<!-- onity:begin -->` / `<!-- onity:end -->` section to the project's
  `AGENTS.md` and `CLAUDE.md`; re-running replaces only that section.
- `Onity/AI/Check Assistant Guidance` reports whether the installed copy matches the installed package version.
- By hand: copy `Documentation~/AI/skills/onity-use/SKILL.md` and `Documentation~/AI/Onity-AI-Usage-Guide.md`
  (as `references/Onity-AI-Usage-Guide.md`) into the same skill folders.
