# 08 - Surpass VContainer in Every Aspect

Onity now beats VContainer on measured Editor/Mono resolve speed, measured
Windows IL2CPP player resolve speed with generated AOT activators, Editor/Mono
and IL2CPP prepare/register speed, scope (DI+Reactive+Events), and
AI-friendliness.

Honest framing: one gap — *real production battle-testing* — cannot be fully
engineered; it accrues with adoption. Everything else below is concrete work.

## Gap scorecard (today)

| Axis | Winner today | Track to close |
|---|---|---|
| Resolve / build speed | **Onity** in the current Editor/Mono and Windows IL2CPP player benchmark runs | keep benchmark gates green |
| Steady-state allocation | pending corrected harness | allocation re-measure |
| Scope (DI+Reactive+Events) | **Onity** | n/a |
| AI-friendliness / analyzer | **Onity** | n/a |
| IL2CPP / AOT / console proven | **Onity** on current Windows IL2CPP benchmark; VContainer still has broader production/device history | device coverage + maturity |
| Collection / `IEnumerable<T>` injection | **Onity** (P1-1 shipped) | — |
| Open-generic registration | **Onity** (P1-2 shipped; IL2CPP caveat) | — |
| Entry-point lifecycle (auto-run) | **Onity** (P1-3 shipped; pump needs PlayMode test) | — |
| Production maturity / trust | VContainer | **P2** (partial) |
| Docs / ecosystem | VContainer | **P3** |

---

## P0 - Unblock "production-grade" (must do first)

### P0-1 Validate (and guarantee) IL2CPP / AOT
**Closes:** "VContainer is proven on device; Onity's `Expression.Compile` is unverified on IL2CPP."
> **Status 2026-05-30 — Windows IL2CPP benchmark now runs with generated AOT activators.**
> `RuntimeCompileSupport` still probes `Expression.Compile` (compile + invoke) once per
> process and both compilers fall back to reflection when no generated activator exists.
> The current player benchmark registers 19 generated activators and beats VContainer on
> singleton, transient, combined, complex, and prepare/register in that run. **Remaining:**
> Android/WebGL/device coverage, broader generated-member coverage, and corrected allocation data.
- Keep the Windows IL2CPP player benchmark green and add Android/device coverage
  for the same singleton/transient/complex/child/inject/factory smoke surface.
- Confirm compiled activators + member-setters do not throw under IL2CPP; use the
  benchmark result as the source-gen gate instead of assuming Editor/Mono ordering.
- Continue hardening **`Onity.SourceGen`**: broaden discovery beyond explicitly
  marked types, add generated member setters where measurement justifies it, and
  keep the runtime registry compatible with the existing resolve path.
- **Verify:** IL2CPP player runs the EditMode-equivalent checks + a benchmark without
  throwing; numbers recorded in `Benchmarks/Results/` for the IL2CPP variant.
- Effort: L. Risk: med (source-gen if needed). **Highest priority** - it is the real
  production blocker.

### P0-2 Measure + activate the BakedGraph fast path
**Closes:** widens the speed lead and meets the internal Complex<=35k / Combined<=1550 gates.
- Teach `OnityDiBenchmarkRunner` to run each scenario under `UseBakedResolve` = false
  AND true; publish baked vs reflection vs VContainer numbers.
- If baked is >= reflection everywhere and meets the gates: flip the default to true,
  update `OnityBakedGraphParityTests.FlagDefault_*`, and re-run the full EditMode suite
  on the baked path (all 203 green on baked, not just the 15 parity scenarios).
- **Verify:** benchmark table + full suite green with the flag on.
- Effort: S-M. Risk: low (parity-tested, reversible).

---

## P1 - Close the DI feature-breadth gap

### P1-1 Collection / `IEnumerable<T>` injection
**Closes:** "VContainer resolves all registrations of a type; Onity binds last-wins only."
> **Status 2026-05-30 — shipped.** Multiple binds per contract accumulate in a parallel
> `m_multiProviderMap`; resolving `IEnumerable<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>`/
> `IList<T>`/`ICollection<T>`/`List<T>`/`T[]` returns all (registration order, ancestors
> first), single resolve stays last-wins, the single-resolve hot path is untouched. Unbound
> element → unresolvable (no silent empty). 11 EditMode tests + an 11-assertion net8 runtime
> harness through the real container — all pass.
- Allow multiple bindings per contract; resolving `IEnumerable<T>` / `IReadOnlyList<T>`
  / `T[]` returns every registration (registration order). Single-resolve keeps
  last-wins for back-compat. Add `BindMany`/append semantics.
- 0-alloc where possible (cache the resolved array per contract after Build).
- **Verify:** port VContainer's collection-resolution tests; baked + reflection parity.
- Effort: M. Risk: med (touches resolve + binding map).

### P1-2 Open-generic registration
**Closes:** "`Bind(typeof(IRepo<>)).To(typeof(Repo<>))`."
> **Status 2026-05-30 — shipped (Editor/Mono; IL2CPP caveat).** Non-generic `Bind(Type)` →
> `RuntimeTypeBindingBuilder`; open registrations in `m_openGenericMap`; first resolve of a
> closed contract builds the impl via `MakeGenericType`, caches it as a normal binding, and
> resolves (singleton lifetime owned by the registering scope). Validated fail-fast at bind.
> 11 EditMode tests + an 11-assertion net8 runtime harness — all pass. Runtime `MakeGenericType`
> needs the closed type to survive IL2CPP stripping; `Onity.SourceGen` (P0-1) would remove
> the caveat. **With P1-1 + P1-3, DI feature parity vs VContainer is complete.**
- Register an open generic; on `Resolve<IRepo<Foo>>()` build/cache the closed activator
  for `Repo<Foo>` lazily. Must coordinate with P0-1 (AOT: closed types need source-gen
  hints or `[Preserve]`/explicit usage to survive IL2CPP stripping).
- **Verify:** open-generic tests + an IL2CPP smoke for one closed instantiation.
- Effort: L. Risk: med-high (AOT). Do **after** P0-1.

### P1-3 Entry-point lifecycle (`IOnityInitializable` / `IOnityTickable`)
**Closes:** "VContainer/Zenject auto-run `IStartable`/`ITickable`/`IInitializable`."
> **Status 2026-05-30 — shipped (engine-free core + Unity pump).** `IOnityInitializable` /
> `IOnityTickable` / `IOnityFixedTickable` / `IOnityLateTickable` in `Onity.DI`. `Build()`
> auto-collects singleton/instance entry points (no manual registration), runs `Initialize()`
> in order, exposes `Tick/FixedTick/LateTick`; `OnityContext` pumps them from
> Update/FixedUpdate/LateUpdate. 9 EditMode tests; headless net8 compile clean. **Remaining:**
> a PlayMode test that the context pump actually drives ticks in play mode.
- In the Unity layer (not engine-free DI): after `Build()`, call `Initialize()` on all
  bound `IOnityInitializable`; pump `IOnityTickable.Tick()` once per frame via the
  existing `EveryUpdate` PlayerLoop hook (indexed dispatch, 0 per-frame alloc).
- Register via the new `Onity.Composition` DSL.
- **Verify:** EditMode (init order) + a PlayMode tick test.
- Effort: M. Risk: low (additive, Unity-layer; keeps DI core engine-free).

---

## P2 - Earn production trust (the maturity gap)

### P2-1 PlayMode tests + CI
- Add a `Onity.Tests.PlayMode` assembly (lifecycle, EveryUpdate, contexts, ticking).
- GitHub Actions via **GameCI** (`game-ci/unity-test-runner`) running EditMode +
  PlayMode on every push; badge in the README.
### P2-2 Stress / soak
- A benchmark scene that spawns/despawns 5-10k DI-resolved objects + reactive
  subscriptions; assert 0 steady-state GC over N minutes and stable frame time.
### P2-3 Ship the sample as a real, playable build
- Wire the Coin Rush sample scene; produce a runnable build; this is the first
  "real game uses Onity" evidence.
- **Verify:** green CI, soak holds frame/GC, sample build runs.
- Effort: L (ongoing). Risk: low. *Real-world adoption still accrues over time.*

---

## P3 - Ecosystem parity

- Generate an API reference site from the XML docs (DocFX) and link from the README.
- 2-3 more focused samples (RollABall-style, a HUD/reactive sample, an events sample).
- Publish the public GitHub repo + UPM git-URL + a `v0.1.0` release.
- Effort: M. Risk: low. (AI usage guide + migration guides already done.)

---

## Sequencing

1. **P0-1 (IL2CPP)** + **P0-2 (baked benchmark/activate)** - keep the measured
   speed lead green across more platforms and graph shapes.
2. **P1-1 (collection)** + **P1-3 (lifecycle)** - the two most-requested DI features
   VContainer has and Onity lacks; both medium effort.
3. **P1-2 (open-generic)** - after P0-1 (AOT-coupled).
4. **P2 (CI/PlayMode/soak/sample)** - converts "unproven" into "verified + demonstrated."
5. **P3 (docs site / samples / release)** - ecosystem polish.

After P0+P1: Onity is faster, broader, AI-friendly, AND feature-complete + IL2CPP-proven
vs VContainer - i.e. **>= VContainer on every engineering axis**. Only lived-in
production maturity (P2 + time) remains, and P2 makes that demonstrable.
