# Contributing to Onity

Thanks for your interest in Onity. It is one Unity package for dependency
injection, reactive state, typed messaging, `OnityTask` async, and factories
and pooling, with one lifetime model and an engine-free core. This guide
explains how to build and test the project, how the documentation is published,
the coding conventions, what the analyzer enforces, and what a pull request
should include.

Onity is MIT-licensed. By contributing you agree that your contribution is
licensed under the same terms.

---

## Build and test

Onity supports **Unity 2022.3 LTS or newer**. The package lives at
`Packages/com.onity.framework`, and the repository root is a minimal Unity
project (`ProjectSettings/ProjectVersion.txt` pins `2022.3.62f2`, the version
`.github/workflows/onity-ci.yml` tests with), so you can clone and open it
directly.

### 1. Open as a Unity project

1. Install Unity 2022.3 LTS or newer.
2. Open the repository root as a Unity project.

### 2. Run the tests

Run the suite through Unity's **Test Runner** (`Window > General > Test
Runner`). The test assemblies are under `Packages/com.onity.framework/Tests`:

- **EditMode** (`Onity.Tests.EditMode`): the bulk of the suite; the engine-free
  core is exercised here with no scene.
- **PlayMode** (`Onity.Tests.PlayMode`): Unity integration, such as contexts,
  lifecycle pumping, PlayerLoop timings and the Unity reactive bridges.
- **PlayMode UGUI** (`Onity.Tests.UGUI.PlayMode`): compiled only when
  `com.unity.ugui` is installed (the `ONITY_UGUI` define).

Keep EditMode and PlayMode green.

### 3. What CI runs

`.github/workflows/onity-ci.yml` runs on every push and pull request to `main`
and `master`:

- **Build engine-free core** always runs and needs no Unity license. It checks
  that every file under the package has its `.meta` file, compiles the
  engine-free assemblies with the .NET 8 SDK
  (`dotnet build onity-core-ci.csproj -c Release -nologo`), and builds the
  Roslyn analyzer and source generator
  (`dotnet build tools/Onity.Analyzers/Onity.Analyzers.csproj -c Release` and
  `dotnet build tools/Onity.SourceGen/Onity.SourceGen.csproj -c Release`).
- **Test (editmode)** and **Test (playmode)** run through
  `game-ci/unity-test-runner` on Unity `2022.3.62f2`, but only when the
  repository has the `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD`
  secrets. On a fork without them the jobs are skipped, not failed, so run the
  Test Runner locally before you open a pull request.

### 4. Quick engine-free checks with dotnet

Six runtime assemblies carry no `UnityEngine` reference (`noEngineReferences`
in their `.asmdef`): `Onity.Core`, `Onity.DI`, `Onity.Reactive`,
`Onity.Messaging`, `Onity.Factory` and `Onity.Composition`.
`onity-core-ci.csproj` at the repository root compiles the first five with a
plain .NET SDK (`netstandard2.1`, C# 9, the Unity 2022.3 language level), so
you get a fast compile check without the Editor:

```text
dotnet build onity-core-ci.csproj -c Release -nologo
```

This catches core compile errors in seconds, but it is not a substitute for
the Test Runner. The Unity layer, `Onity.Composition` and the test suite still
need the Editor.

---

## Documentation

The docs site at `docs/` is built with Jekyll and the `just-the-docs` theme
(see `Gemfile`) and deployed by `.github/workflows/pages.yml`. The UPM package
ships a generated copy of Getting Started, the guides, references, migration
pages, the AI usage guide and the `onity-use` agent skill under
`Packages/com.onity.framework/Documentation~`.

- `docs/` and `.agents/skills/onity-use/SKILL.md` are the source of truth.
  Never edit `Documentation~` by hand. After changing them, run
  `python tools/docs/sync-package-docs.py` and commit the regenerated
  `Documentation~/`; `.github/workflows/docs-check.yml` runs the script with
  `--check` and fails when the committed copy is stale.
- Every site page starts with Jekyll front matter (`title`, `nav_order`, and
  `parent` on a child page) at the first byte of the file, saved as UTF-8
  without a byte order mark. CI builds with `--strict_front_matter` and
  validates the generated links with `htmlproofer`.
- Link rules: inside `docs/`, link to other pages with relative `.html` links
  and the exact anchor (`guide/reactive.html#primitives`,
  `../reference/di-api.html`); anchors are kramdown ids (lowercase, spaces to
  hyphens, punctuation dropped), so keep headings plain ASCII. Link to files
  without front matter (`.json`, benchmark records) by their real extension.
  Every link target must exist when the sync script runs; it refuses to
  generate when one does not resolve. Do not write Liquid in prose; only
  `{{ site.baseurl }}`, `{% link %}` and `{% raw %}` are accepted inside link
  destinations, and relative links are enough.
- Do not rename or move a shipped file. `Packages/com.onity.framework/README.md`,
  `AGENTS.md`, `CLAUDE.md` and `llms.txt` name `Documentation~/...` paths that
  must exist, and `llms.txt` must list every shipped file.
- To preview locally: `bundle install`, then
  `bundle exec jekyll build --source ./docs --destination ./_site --strict_front_matter`
  and `bundle exec htmlproofer ./_site --disable-external`.
- Record user-visible changes under `[Unreleased]` in `CHANGELOG.md` and in its
  package copy `Packages/com.onity.framework/CHANGELOG.md`.

CI mirrors `Packages/com.onity.framework` to the `upm` branch on every push to
`main`. Do not commit to that branch; pull requests target `main`.

---

## Coding conventions

Onity uses Unity C# conventions. Match the surrounding code.

- **Naming**
  - private instance fields: `m_camelCase`
  - private static fields: `s_camelCase`
  - constants: `k_camelCase`
  - Clear English names and simple, direct verbs (`Get`, `Set`, `Add`, `Bind`,
    `Resolve`, `Publish`, `Subscribe`).
- **Braces**: Allman style (the opening brace on its own line).
- **XML docs**: public types and members carry XML documentation comments.
- **No `System.Linq`**: never add `using System.Linq;` in runtime code. Onity
  has no third-party runtime dependencies, so write a plain loop where a query
  pipeline would otherwise help.
- **Engine-free core**: do not pull `UnityEngine` into `Onity.Core`,
  `Onity.DI`, `Onity.Reactive`, `Onity.Messaging`, `Onity.Factory` or
  `Onity.Composition`. Unity-specific code belongs in `Onity.Unity` or the
  other engine-facing assemblies.
- **Allocation-conscious hot paths**: resolve, `Publish`, `OnNext`, the
  per-frame Unity observables, `OnityTask` awaits and subscription steady state
  are designed to avoid per-call managed allocation. Keep them that way: no
  LINQ, closures, boxing or string allocation on these paths. Subscribe-time
  wrapper allocations are acceptable; per-emit allocations are not.
- **Ownership and lifetime**: every subscription, timer, async loop and pooled
  task has one owner and a defined stop point (a disposable, a scope token or
  a destroy token). Do not add `async void`.

---

## Analyzer

The [Onity analyzer pack](tools/Onity.Analyzers) turns common misuse into
compiler diagnostics `ONITY001` to `ONITY006` (all `Warning`, enabled by
default). When you change a runtime API or a usage pattern, keep your code
clean against these rules and update the analyzer and its tests if you change
the behavior they describe.

| Rule | What it flags |
| --- | --- |
| `ONITY001` | A `Resolve` call inside `Update`, `FixedUpdate` or `LateUpdate`. Resolve once and cache. Has a code fix. |
| `ONITY002` | `Bind`, `BindInstance`, `BindFactory` or `Resolve` on a container local after `Build()`. |
| `ONITY003` | A `Subscribe` or `SubscribeAwait` result (`IDisposable`) that is discarded instead of stored, returned or passed to `AddTo`. Has a code fix. |
| `ONITY004` | A type with two or more `[Inject]` constructors. |
| `ONITY005` | An `[Inject]` member that cannot be injected: a property without a setter, an indexer, a generic method, or a static member. |
| `ONITY006` | `new TService()` on a type the same file binds or resolves through Onity. |

The analyzer and the source generator (`tools/Onity.SourceGen`) are plain
`dotnet` projects, not Unity assemblies. Run the analyzer tests with
`dotnet test tools/Onity.Analyzers/Tests/Onity.Analyzers.Tests.csproj` when you
touch either.

---

## Pull requests

- **Tests for new behavior.** Add focused EditMode tests for the success path
  and the important failure paths of any new or changed behavior. Prefer the
  engine-free core so tests run without a scene; add PlayMode tests when the
  change is Unity-integration specific.
- **Keep both test modes green.** Do not merge with failing EditMode or
  PlayMode tests.
- **Honest claims.** Documentation, commit messages and code comments must be
  accurate:
  - Onity has no third-party runtime dependencies; do not reintroduce one, and
    keep `System.Linq` out of the runtime.
  - Performance numbers come only from the evidence pages under
    `docs/benchmarks/` and `docs/assets/benchmarks/`. A number sits in the same
    sentence as its date, Unity version, backend and process count and links
    its evidence page, and the next sentence states the known limit. Do not
    claim a verified zero-allocation ("0 B/op") resolve: the resolve machinery
    and the reactive, messaging and `OnityTask` paths are designed to avoid
    per-call managed allocation, while a transient resolve still allocates the
    instance it returns.
  - Keep the docs aligned with the shipped API: every type, member, namespace
    and menu path in an example must exist in `Packages/com.onity.framework`.
- **Surgical changes.** Touch only what the change requires. Do not reformat or
  rename adjacent code without reason.
- **Describe the change.** Fill in the pull request template: what changed,
  why, the affected pillars, and how you verified it.

---

## Reporting bugs and requesting features

- **Bugs**: open a [bug report](.github/ISSUE_TEMPLATE/bug_report.md). Include
  your Unity version, repro steps, expected and actual behavior, and whether
  it reproduces in EditMode.
- **Features**: open a
  [feature request](.github/ISSUE_TEMPLATE/feature_request.md). Describe the
  use case and the pillar it touches (DI, Reactive, Events, Async, Pooling).
- **Security**: do not open a public issue. See [SECURITY.md](SECURITY.md).
