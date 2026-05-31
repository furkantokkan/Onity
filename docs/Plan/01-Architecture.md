# 01 - Architecture

## Layer diagram

```
|  PLUGINS (split-ready, opt-in)                                       |
|  Onity.Physics  |  Onity.SkillStats  |  Onity.Input                  |
|  Onity.UI       |  Onity.SceneFlow                                   |
+----------------------------------------------------------------------+
|  ENGINE BRIDGES                                                      |
|  Onity.Unity (contexts, lifecycle, MonoInstaller)                    |
|  Onity.DOTS  (managed -> ECS bridges, Burst-safe queues)             |
|  Onity.Editor (diagnostics, validation, wizards)                     |
+----------------------------------------------------------------------+
|  CORE (pure C# where possible, no engine refs)                       |
|  Onity.DI      |  Onity.Reactive  |  Onity.Async                     |
|  Onity.Messaging |  Onity.Factory  |  Onity.Pooling                  |
|  Onity.Core (shared primitives)                                      |
+----------------------------------------------------------------------+
```

Dependencies flow downward. Higher layers may reference lower layers. A lower
layer never references a higher layer.

## Module table

| Module | Location | Status | Engine refs | Notes |
|---|---|---|---|---|
| `Onity.Core` | `Runtime/Core` | Stable | No | Lifetime enum, Unit, DisposableAction |
| `Onity.DI` | `Runtime/DI` | Headline rewrite target | No | See `02-DI-Design.md` |
| `Onity.Reactive` | `Runtime/Reactive` | Headline expansion target | No | See `03-Reactive-Design.md` |
| `Onity.Async` | **NEW** `Runtime/Async` | To extract | No | Move from `Onity.Unity` |
| `Onity.Messaging` | `Runtime/Messaging` | Working | No | EventHub expansion per `EVENT_HUB_PLAN.md` |
| `Onity.Factory` | `Runtime/Factory` | Working | No | Minimal contracts |
| `Onity.Pooling` | `Runtime/Pooling` | Working | Yes (UnityEngine.Pool) | Diagnostics registry |
| `Onity.Unity` | `Runtime/Unity` | Working, shrink scope | Yes | Becomes thinner once Async/Input/UI/SceneFlow extract |
| `Onity.DOTS` | `Runtime/DOTS` | Limited | Yes (Entities) | Phase 4 expansion |
| `Onity.Editor` | `Editor` | Working | Yes | Diagnostics, validation |
| `Onity.Tests.EditMode` | `Tests/EditMode` | Working | Yes | NUnit edit-mode |
| `Onity.Benchmarks.Editor` | `Benchmarks/Editor` | Working | Yes | DI benchmark runner |

Locations are relative to `Assets/Onity-Packages/Onity/`.

### Future modules (not present yet)

| Module | Location | Phase |
|---|---|---|
| `Onity.Input` | `Assets/Onity-Packages/Onity.Input/Runtime` | Phase 0 |
| `Onity.UI` | `Assets/Onity-Packages/Onity.UI/Runtime` | Phase 0 |
| `Onity.SceneFlow` | `Assets/Onity-Packages/Onity.SceneFlow/Runtime` | Phase 0 |
| `Onity.Physics` (existing) | `Assets/Onity-Packages/Onity.Physics/Runtime` | Phase 5 polish |
| `Onity.SkillStats` (existing) | `Assets/Onity-Packages/Onity.SkillStats/Runtime` | Phase 5 polish |

## Dependency rules

These rules are enforced by the asmdef `references` field. A PR that breaks
them must update this document with an explicit reason.

```
Onity.Core           depends on: nothing
Onity.Factory        depends on: Onity.Core
Onity.DI             depends on: Onity.Core, Onity.Factory
Onity.Reactive       depends on: Onity.Core
Onity.Async          depends on: Onity.Core, Onity.Reactive
Onity.Messaging      depends on: Onity.Core, Onity.Reactive
Onity.Pooling        depends on: Onity.Core
Onity.Unity          depends on: Onity.Core, Onity.DI, Onity.Reactive,
                                  Onity.Async, Onity.Messaging, Onity.Pooling,
                                  Onity.Factory
Onity.DOTS           depends on: Onity.Core, Onity.Messaging
Onity.Editor         depends on: Onity.Core, Onity.DI, Onity.Reactive,
                                  Onity.Async, Onity.Messaging, Onity.Unity
Onity.Input          depends on: Onity.Core, Onity.Reactive
Onity.UI             depends on: Onity.Core, Onity.DI, Onity.Unity
Onity.SceneFlow      depends on: Onity.Core, Onity.Async, Onity.Reactive
Onity.Physics        depends on: Onity.Core, Onity.Async
Onity.SkillStats     depends on: Onity.Core, Onity.DOTS
```

## Engine-reference policy

Each core module sets `noEngineReferences: true` in its asmdef when possible.
This keeps the core testable without spinning up Unity and prevents accidental
coupling to `UnityEngine` types.

| Module | `noEngineReferences` | Reason |
|---|---|---|
| `Onity.Core` | true | Pure primitives |
| `Onity.DI` | true | Pure container logic |
| `Onity.Factory` | true | Pure contracts |
| `Onity.Reactive` | true | Pure observable algebra |
| `Onity.Async` | true | Pure Task / CancellationToken |
| `Onity.Messaging` | true | Pure broker / channels |
| `Onity.Pooling` | false | Uses `UnityEngine.Pool.ObjectPool<T>` |
| `Onity.Unity` | false | MonoBehaviour, GameObject, PlayerLoop |
| `Onity.DOTS` | false | Entities, Burst, Mathematics |

`noEngineReferences: true` does **not** mean the module can be used outside
Unity. Onity is Unity-only. The flag exists purely to keep build/test surfaces
clean.

## Naming and file layout

### Asmdef naming

- `Onity.<Module>` for runtime: `Onity.DI`, `Onity.Reactive`.
- `Onity.<Module>.Editor` for editor: `Onity.DI.Editor`.
- `Onity.Tests.<Module>` for edit-mode tests: `Onity.Tests.DI.EditMode`.
- `Onity.Tests.<Module>.PlayMode` for play-mode tests.
- Existing `Onity.Tests.EditMode` umbrella will be split into per-module test
  asmdefs in Phase 0 to keep test compilation surface small.

### Namespace and `rootNamespace`

The asmdef `rootNamespace` matches the asmdef name. Example: `Onity.DI`.

Scripts that bridge multiple namespaces live in `Onity.<Outer>.<Inner>`. For
example: `Onity.Unity.Messaging` for messaging helpers that need Unity bridges.

### Folder layout per module

```
Onity-Packages/
  Onity/
    Runtime/
      <Module>/
        Onity.<Module>.asmdef
        Scripts/
          *.cs
    Editor/
      Onity.Editor.asmdef
      Scripts/
        <Module>/
          *.cs
    Tests/
      EditMode/
        Onity.Tests.EditMode.asmdef
        Scripts/
          <Module>/
            *.cs
```

A module may add subfolders under `Scripts/` for organization (e.g.
`Scripts/Builders`, `Scripts/Internal`). Subfolders do not affect namespaces.

## Public API rules

- A type is public only if a user needs to call, implement, or subclass it.
  Everything else is `internal`.
- Public types ship with `///` XML documentation on all members.
- `internal` types may live in the same file as their related public type if
  the file stays under ~400 lines, else they go in a sibling file.
- No `public static` mutable state unless it is a diagnostic counter or a
  documented opt-in (see `OnityContainer.DiagnosticsCollectionEnabled` for
  precedent).

## Style anchors

- Style guide: root `codex-code-style.md` is the source of truth.
- Brace style: Allman.
- Private instance fields: `m_camelCase`.
- Private static fields: `s_camelCase`.
- Constants: `k_camelCase`.
- `[SerializeField] private` over `public` fields.
- Subscribe in `OnEnable`, unsubscribe in `OnDisable`.
- `Update` and `FixedUpdate` allocation-free.

These rules are enforced by code review and (eventually) an analyzer pack.
