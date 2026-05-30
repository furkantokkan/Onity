# 06 - Agent Playbook

Rules of engagement for AI agents working on Onity. Read this **before**
starting any implementation task. Combined with `AGENTS.md` (root) and the
plan docs in this folder, it is enough to ship correctly.

## 1. The mental model

Onity is a Unity-only DI plus Reactive framework. Two design decisions
override most others:

1. **Performance is part of the product.** A change is not done until the
   benchmark numbers move in the right direction. See
   `04-Performance-Targets.md`.
2. **Zero third-party runtime dependencies.** R3, UniTask, Zenject,
   VContainer, MessagePipe, Autofac, MicroResolver are reference and
   benchmark only. Never `references` them from an Onity runtime asmdef.

If a task seems to require either rule to bend, stop and ask.

## 2. Where things live

```
Repo root
|-- AGENTS.md                 (Canonical agent rules - read first)
|-- README.md
|-- EVENT_HUB_PLAN.md         (Messaging expansion plan)
|-- codex-code-style.md       (Style guide - source of truth)
|-- docs/Plan/                (You are here)
|-- Assets/Onity-Packages/
|   |-- Onity/                (Core package)
|   |   |-- Runtime/
|   |   |   |-- Core/
|   |   |   |-- DI/
|   |   |   |-- Reactive/
|   |   |   |-- Messaging/
|   |   |   |-- Factory/
|   |   |   |-- Pooling/
|   |   |   |-- Unity/
|   |   |   `-- DOTS/
|   |   |-- Editor/
|   |   |-- Tests/EditMode/
|   |   |-- Benchmarks/Editor/
|   |   `-- Samples/
|   |-- Onity.Physics/        (Split-ready plugin)
|   `-- Onity.SkillStats/     (Split-ready plugin)
`-- Assets/Packages/          (Third-party reference - never reference from
                               runtime asmdefs)
```

## 3. Before you write code

Run these in order:

1. Read `AGENTS.md` end to end if you have not in this session.
2. Read the plan doc that covers your area (`02-DI-Design.md` or
   `03-Reactive-Design.md`).
3. Read the relevant existing source files. Do not paraphrase from memory.
4. Identify the exact file:line you intend to change.
5. Identify the test(s) you will add or update.
6. Identify the benchmark scenario(s) that prove your change works.

If any of these is unclear, ask. Do not start coding from a guess.

## 4. Style essentials

These are repeated from `codex-code-style.md` because they are the most
common review findings:

- **Brace style:** Allman. Open brace on its own line.
- **Field names:** `m_camelCase` (private instance), `s_camelCase`
  (private static), `k_camelCase` (constants).
- **SerializeField:** `[SerializeField] private` over `public`.
- **Auto-properties:** `public T Value { get; private set; }` only when the
  setter has to stay private. Otherwise plain fields with explicit setters.
- **Member order:** Unity execution flow first. `Awake -> OnEnable ->
  Start -> Update -> FixedUpdate -> LateUpdate -> OnDisable -> OnDestroy`.
- **`Update` / `FixedUpdate`:** zero allocation. No `foreach` over a
  `Dictionary`. No LINQ. No `string` interpolation. Cache lookups out of the
  loop.
- **Subscribe / Unsubscribe:** `OnEnable` / `OnDisable` symmetry. Never
  subscribe in `Start` and forget to unsubscribe.
- **Comments:** only when the why is non-obvious. Do not narrate what code
  is doing.
- **Naming verbs:** simple. `Set`, `Get`, `Add`, `Remove`, `Create`,
  `Update`, `Load`, `Save`, `Open`, `Close`, `Show`, `Hide`. Avoid
  `Ensure`, `Manage`, `Execute`, `Process`, `Perform` unless the verb is
  truly accurate.

## 5. The "minimum change" rule

You are writing one feature or one fix per task. Do not:

- Reformat unrelated files.
- Rename a method that is not part of your task.
- "Clean up" a class while you are there.
- Add a comment that explains the past.

Do:

- Touch only files that the task requires.
- Leave dead code alone unless your task removes it.
- File a follow-up note in the relevant plan doc if you spot a problem.

## 6. The "no speculative API" rule

The plan docs describe what the API will look like. Ship exactly that.
Do not:

- Add `BindOptional<T>` because "users might want it".
- Add a `Disposable.Empty` constant if no caller needs it.
- Add an `IObservable<T>` adapter to bridge System.Reactive.
- Add a generic `BindFactory<TParam1, TParam2, TParam3, ...>` overload
  beyond the documented arities.

If a sample or a real user need surfaces a missing API, file it in the plan
doc and ship in the next iteration.

## 7. Testing discipline

Every behavior change ships with one or more EditMode tests:

- Tests live under
  `Assets/Onity-Packages/Onity/Tests/EditMode/Scripts/<Module>/`.
- Test classes named `<TypeName>Tests`.
- Test methods named `<MethodName>_<Scenario>_<ExpectedResult>`. Example:
  `Resolve_WhenSingletonBound_ReturnsSameInstance`.
- Tests prefer constructed instances of the system under test over mocks.
  Mock only at the boundary you are exercising.
- No `Thread.Sleep`. Use deterministic time sources.
- No `Task.Delay` in tests except when the assertion is "should not
  complete before X".

## 8. Benchmark discipline

If your change touches:

- `OnityContainer.cs`, anything in `Runtime/DI/`, or providers ->
  run the DI benchmark.
- `Subject`, `ReactiveProperty`, anything in `Runtime/Reactive/`, or
  PlayerLoop runners -> run the reactive benchmark.

Include the before / after table in the PR description per
`04-Performance-Targets.md` section 6.

If the benchmark moves in the wrong direction, fix the change before
opening the PR. Do not open a PR with red benchmarks and ask for help.
Ask for help first, in conversation.

## 9. Common pitfalls

| Pitfall | Fix |
|---|---|
| Importing `R3.*` or `Cysharp.Threading.Tasks.*` in runtime code | Use Onity's own types |
| Allocating a `List<T>` inside `OnNext` or `Update` | Cache it as a field; clear before reuse |
| `foreach` over `Dictionary` in hot path | Convert to `for` over `KeyValuePair[]` or array of values |
| `string.Format` in benchmark / log statements that run per frame | Pre-build the string once or gate behind a debug flag |
| Subscribing in `Awake` without `AddTo(this)` | Always pair subscription with a disposal owner |
| Returning `Task<T>` from a hot inner loop | Hot loops stay sync. Async at the boundary |
| Adding `using Onity.Reactive` to a `Core` file | `Onity.Core` cannot reference reactive. Move the file. |

## 10. PR shape

Every PR contains:

1. **Title:** present-tense imperative. `Add baked resolve path to DI` not
   `Added baked resolve path`.
2. **Body:**
   - Why (one paragraph)
   - What changed (bulleted file list with one-line description per file)
   - Benchmark before/after if applicable
   - Tests added/updated
3. **Diff:** focused. If the diff has unrelated edits, split the PR.

Do not add AI attribution lines (no `Co-authored-by: Claude`,
`Generated with ...`, model names, etc.). The user's global rules forbid
them.

## 11. Asking questions

You are encouraged to ask when:

- The plan does not cover the case you hit.
- A test reveals existing code that disagrees with the plan.
- A benchmark gate cannot be hit even with the planned approach.

Ask in conversation, not in PR comments, not in TODOs scattered through
source.

## 12. End-of-task documentation

After finishing a task, before moving on:

- Update the relevant plan doc if the design shifted.
- Update `Assets/Onity-Packages/Onity/ENGINEERING.md` if the runtime
  architecture changed materially.
- Write a short bullet list of what changed and why in the PR description.

Do not write long narrative docs unless the user asks. Concise bullets,
real file paths, real numbers.

## 13. References

- Root: `AGENTS.md`, `README.md`, `EVENT_HUB_PLAN.md`,
  `codex-code-style.md`
- Plan docs: this folder
- Engineering doc:
  `Assets/Onity-Packages/Onity/ENGINEERING.md`
- Benchmarks: `Assets/Onity-Packages/Onity/Benchmarks/`
