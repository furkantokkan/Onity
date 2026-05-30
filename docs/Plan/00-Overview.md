# 00 - Onity Overview

## Vision

Onity is a **Unity-only, high-performance** framework that replaces the common
"download four assets to build a game" pattern:

| Slot | Replaced asset | Onity module |
|---|---|---|
| DI container | Zenject / VContainer | `Onity.DI` |
| Reactive primitives | UniRx / R3 | `Onity.Reactive` |
| Messaging bus | MessagePipe | `Onity.Messaging` |
| Async helpers | UniTask | `Onity.Async` |
| Pooling | UnityEngine.Pool wrappers | `Onity.Pooling` |
| Factory | Zenject factories | `Onity.Factory` |

One install gives a user the full stack with a single coherent API. The user
should not need to learn five mental models.

## Primary goals (priority order)

1. **Settle the main system first.** DI and Reactive must be production-ready
   before plugins receive attention. Everything else is deferred.
2. **Beat VContainer on DI resolve hot paths.** Current benchmark shows Onity
   is faster at registration (+91%) but slower at transient/complex resolve
   (-15% to -28%). Closing this gap is the headline performance task.
3. **Zenject-familiar API.** A developer who knows
   `Bind<T>().To<C>().AsSingle()` should be productive on day one without
   reading a manual.
4. **Allocation-free hot paths.** Resolve, Publish, EveryUpdate, and
   subscription steady state must allocate zero bytes per call.
5. **AI-implementable plan.** The framework is built by AI agents reading this
   plan plus `AGENTS.md`. Every design decision and acceptance criterion is
   spelled out in MD files so agents do not have to guess.

## Secondary goals (after main system is stable)

1. DOTS bridges beyond the current `int` event queue.
2. Plugins: `Onity.Physics`, `Onity.SkillStats`, `Onity.Input`, `Onity.UI`,
   `Onity.SceneFlow`.
3. Sample projects covering common Unity game patterns.
4. Asset Store and GitHub UPM release packaging.

## Non-goals

- **No non-Unity runtimes.** Onity targets Unity 2022.3 LTS and later.
  Standalone .NET / Godot / cross-engine portability is explicitly out of
  scope.
- **No third-party runtime dependencies.** R3, UniTask, Zenject, VContainer,
  Autofac, MicroResolver, MessagePipe, etc. live in `Assets/Packages/` and
  `Assets/ThirdParty/` for **reference and benchmark comparison only**. They
  must not be referenced by any Onity runtime asmdef.
- **No managed DI inside Burst jobs.** DI resolves managed objects. Burst
  cannot allocate managed memory. Resolve on main thread, pass blittable data
  into jobs.
- **No advanced Zenject feature parity.** Conditional bindings, signal bus,
  sub-containers beyond `ProjectContext/SceneContext/GameObjectContext`,
  facade pattern, etc. are not goals unless a sample needs them.
- **No source-generated DI in phase 1.** `Expression.Compile` is the activator
  baseline; source-gen is a phase 2+ optimization.

## Current state snapshot

As of 2026-02-12 the runtime modules under
`Assets/Onity-Packages/Onity/Runtime/` are:

| Module | Files | Maturity |
|---|---:|---|
| `Onity.Core` | 3 | Stable, minimal primitives |
| `Onity.DI` | 7 | Working, reflection-based, slower than VContainer on transient/complex |
| `Onity.Reactive` | 17 | Working, primitive operators present, missing R3-parity operators |
| `Onity.Messaging` | 7 | Working, see `EVENT_HUB_PLAN.md` for next steps |
| `Onity.Factory` | 1 | Minimal contracts |
| `Onity.Pooling` | 4 | Working with diagnostics registry |
| `Onity.Unity` | ~30 | Contexts, async helpers, scene flow, UI bridge, input wrappers |
| `Onity.DOTS` | 6 | Only int-event queue + session bridge |

Editor tooling (`Onity/Diagnostics/*`, scene validation) and benchmark runner
already work. Samples scenes (RollABall, GameObjectContextScope,
BasicGameplay) generate from a menu.

### Latest DI benchmark

From `Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-summary.md`,
Unity 2022.3.62f3, Windows Editor/Mono, generated 2026-05-30:

| Scenario | Onity (ns/op) | VContainer (ns/op) | Onity vs VContainer |
|---|---:|---:|---:|
| Resolve Singleton | 94 | 202 | +53.44% |
| Resolve Transient | 775 | 1,697 | +54.32% |
| Resolve Combined | 896 | 1,712 | +47.64% |
| Resolve Complex | 22,787 | 57,995 | +60.71% |
| Prepare and Register Complex | 47,243 | 135,140 | +65.04% |

Allocation numbers from this runner are withdrawn pending a corrected harness;
the timing numbers above are the trustworthy benchmark output.

Onity Baked is faster than VContainer on every measured Editor/Mono timing path.
The remaining performance tasks are IL2CPP player timing, source-generated or
IL-postprocessed activators for AOT, and a corrected allocation harness.

## Where work begins

After reading the rest of the plan, the next action is to close the remaining
0.2.x performance proof gaps: IL2CPP player benchmark, corrected allocation
measurement, and source-generated or IL-postprocessed activators.
