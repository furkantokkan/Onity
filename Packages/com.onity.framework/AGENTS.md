# Onity for AI coding agents

Onity (`com.onity.framework`) is a Unity package for dependency injection, reactive state, typed messaging,
factories and pooling, and `OnityTask` async. Read this file before you write or change code that uses it.

Paths below are relative to this folder: `Packages/com.onity.framework` for an embedded or local package, or
`Library/PackageCache/com.onity.framework@<hash>` for a Git or registry install.

## Read first

1. `Documentation~/AI/Onity-AI-Usage-Guide.md`: the index and the rules. Always start here.
2. The guide for the feature you are touching, under `Documentation~/guide/`: `dependency-injection.md`,
   `reactive.md`, `events-messaging.md`, `onitytask.md`, `lifecycle-and-scopes.md`,
   `factories-and-pooling.md`, `performance-and-il2cpp.md`.
3. `Documentation~/reference/` for complete API catalogs, `Documentation~/Migration/` when porting from Zenject,
   VContainer, R3 or UniRx, or UniTask, and `Documentation~/Getting-Started.md` for a first-scene walkthrough.

`llms.txt` lists every shipped page with a one-line description. Read only the pages the task needs.

The installed package source (`Runtime/`) and its tests (`Tests/`) are the authority. If a guide and the code
disagree, follow the code and tell the user about the mismatch.

## Rules that prevent the most common mistakes

- Do not invent Onity APIs from Zenject, VContainer, R3, UniRx, MessagePipe or UniTask analogies. If a type or
  method is not in the guide or the source, it does not exist (there is no `Instantiate(args)`; `WithId`,
  `WhenInjectedInto` and `AsScoped()` do exist). The guide's DO / DON'T section lists the known traps.
- Register services in a `MonoInstaller` owned by a `ProjectContext`, `SceneContext` or `GameObjectContext`.
  A fluent `Bind<T>()` registers nothing until you call `AsSingle()`, `AsScoped()` or `AsTransient()`, and
  bindings cannot be added after `Build()`.
- Keep domain logic in plain C# services with constructor injection and `MonoBehaviour`s thin. Never call
  `Resolve` from `Update`, `FixedUpdate` or `LateUpdate`.
- Every reactive and messaging `Subscribe` returns an `IDisposable`; retain it. Use `AddTo(this)` in a
  `Component`, `AddTo(compositeDisposable)` in plain C#, and `AddTo(scope)` with an injected
  `IOnityScopeLifetime` in a scope service. There is no `AddTo(GameObject)` overload. The async-stream
  `Subscribe` overloads that take a `CancellationToken` return nothing; the token owns the lifetime.
- Use `OnityTask` (`Onity.Unity.Async`) for Unity async code, not UniTask. Pooled `OnityTask` values are
  single-consumer: await each once, or call `Preserve()` to share one. Give every async loop a token from its
  owner (`GetCancellationTokenOnDestroy()`, `IOnityScopeLifetime.Token`), and never write `async void`.
- An assembly definition that calls `Onity.Unity.Async` extension methods must reference `Onity.Reactive`
  (and `Onity.Messaging` for the messaging bridges), or the compiler reports CS0012.
- The core assemblies (`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Factory`,
  `Onity.Composition`) are engine-free. Keep `UnityEngine` out of them and out of domain logic.
- Call `ObserveOnMainThread()` before touching Unity objects after `SelectAwait` or `WhereAwait`. Publish and
  subscribe on the main thread only.
- Use a message for a one-shot notification and `ReactiveProperty<T>` for current state (the guide's events
  decision rule).

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
