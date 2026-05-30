# Contributing to Onity

Thanks for your interest in Onity. It is one Unity package for dependency
injection, reactive programming, and events, with an engine-free core. This
guide explains how to build and test the project, the coding conventions, how
the analyzer enforces correct usage, and what a pull request should include.

Onity is MIT-licensed. By contributing you agree your contribution is licensed
under the same terms.

---

## Build and test

Onity targets **Unity 2022.3 LTS or newer**. The package lives at
`Packages/com.onity.framework`, and the repository root is itself a minimal
Unity project, so you can clone and open it directly.

### 1. Open as a Unity project

1. Install **Unity 2022.3 LTS or newer** (CI runs on `2022.3.62f3`).
2. Open the repository root as a Unity project.

### 2. Install ZLinq (required)

**ZLinq is the only third-party runtime dependency.** Onity's Unity layer
(`Onity.Unity`) uses it; the engine-free core uses no `System.Linq` at all.
Install [ZLinq](https://github.com/Cysharp/ZLinq) via
[NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) (package id
`ZLinq`) before the Unity layer will compile. The repository already vendors a
copy under `Assets/Packages/` for local development.

### 3. Run the tests

Run the test suite through Unity's **Test Runner** (`Window > General > Test
Runner`):

- **EditMode** — the bulk of the suite; the engine-free core is exercised here
  with no scene.
- **PlayMode** — Unity-integration coverage (contexts, lifecycle pumping, Unity
  reactive bridges).

CI runs both EditMode and PlayMode on every push and pull request to `main` and
`master` via [`game-ci/unity-test-runner`](.github/workflows/onity-ci.yml). Keep
both modes green.

### 4. Quick engine-free checks with `dotnet`

The core assemblies carry no `UnityEngine` reference
(`Onity.Core`, `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`,
`Onity.Factory`), so you can compile them outside the Editor for a fast feedback
loop:

```
dotnet build Onity.Core.csproj -nologo
dotnet build Onity.DI.csproj -nologo
dotnet build Onity.Reactive.csproj -nologo
dotnet build Onity.Messaging.csproj -nologo
dotnet build Onity.Factory.csproj -nologo
```

This catches core compile errors quickly, but it is **not** a substitute for the
Test Runner. The Unity layer and the full test suite still need the Editor.

---

## Coding conventions

Onity uses Unity C# conventions. Match the surrounding code.

- **Naming**
  - private instance fields: `m_camelCase`
  - private static fields: `s_camelCase`
  - constants: `k_camelCase`
  - Use clear English names and simple, direct verbs (`Get`, `Set`, `Add`,
    `Bind`, `Resolve`, `Publish`, `Subscribe`).
- **Braces** — Allman style (opening brace on its own line).
- **XML docs** — public types and members carry XML documentation comments.
- **No `System.Linq`** — never add `using System.Linq;` in runtime code. Use
  ZLinq (`AsValueEnumerable` chains) where a query pipeline helps; otherwise
  write a plain loop.
- **Engine-free core** — the core asmdefs are `noEngineReferences`. Do not pull
  `UnityEngine` into `Onity.Core`, `Onity.DI`, `Onity.Reactive`,
  `Onity.Messaging`, or `Onity.Factory`. Unity-specific code belongs in
  `Onity.Unity` (or the other engine-facing assemblies).
- **Allocation-conscious hot paths** — resolve, `Publish`, `OnNext`, the
  per-frame Unity observables, and subscription steady state are designed to
  avoid per-call managed allocation. Keep them that way: avoid LINQ, closures,
  boxing, and string allocation on these paths. Subscribe-time wrapper
  allocations are acceptable; per-emit allocations are not.

---

## Analyzer (`ONITY001`–`ONITY006`)

The [Onity analyzer pack](tools/Onity.Analyzers) turns common misuse into inline
diagnostics. When you change runtime API or usage patterns, keep your code clean
against these rules and update the analyzer and its tests if you change the
behavior they describe.

| Rule | What it flags |
| --- | --- |
| `ONITY001` | A `Resolve` call inside `Update` / `FixedUpdate` / `LateUpdate`. Resolve once and cache instead. |
| `ONITY002` | `Bind` / `Resolve` / `BindInstance` / `BindFactory` on a container after `Build()`. |
| `ONITY003` | A `Subscribe` result discarded without `AddTo` or storing the `IDisposable`. |
| `ONITY004` | A type declaring two or more constructors marked `[Inject]`. |
| `ONITY005` | An `[Inject]` member that cannot be injected (no setter, indexer, generic method, or static field/property/method). |
| `ONITY006` | `new TService()` on a type the same file also binds or resolves through Onity. |

The analyzer has its own test project under
[`tools/Onity.Analyzers/Tests`](tools/Onity.Analyzers/Tests); run it with
`dotnet test` when you touch the analyzer.

---

## Pull requests

- **Tests for new behavior.** Add focused EditMode tests for the success path
  and the important failure paths of any new or changed behavior. Prefer the
  engine-free core so tests run without a scene. Add PlayMode tests when the
  change is Unity-integration specific.
- **Keep both test modes green.** Do not merge with failing EditMode or PlayMode
  CI.
- **Honest claims.** Documentation, commit messages, and code comments must be
  accurate. In particular:
  - ZLinq **is** a third-party runtime dependency — do not describe Onity as
    having no third-party dependencies.
  - Do not claim a verified zero-allocation / "0 B/op" resolve. The accurate
    framing is that the resolve machinery and the reactive/messaging emit paths
    are **designed** to avoid per-call managed allocation, but a transient
    resolve still allocates the instance it returns. Cite timing numbers only
    with the "indicative, Editor/Mono, one machine, not guaranteed" caveat.
- **Surgical changes.** Touch only what the change requires. Do not reformat or
  rename adjacent code without reason.
- **Describe the change.** Write a clear PR description: what changed, why, the
  affected systems, and how you verified it. Fill in the pull request template.

---

## Reporting bugs and requesting features

- **Bugs** — open a [bug report](.github/ISSUE_TEMPLATE/bug_report.md). Include
  your Unity version, repro steps, expected vs. actual behavior, and whether it
  reproduces in EditMode.
- **Features** — open a
  [feature request](.github/ISSUE_TEMPLATE/feature_request.md). Describe the use
  case and which pillar it touches (DI / Reactive / Events).
- **Security** — do not open a public issue. See [SECURITY.md](SECURITY.md).
