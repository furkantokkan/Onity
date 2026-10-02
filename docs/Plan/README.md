# Onity Framework — Planning Index

This folder contains the working plan for Onity, a Unity-first, high-performance
DI + reactive framework. The docs are written for AI agents and human
contributors who will implement the framework piece by piece.

## Reading order

Read these in order on first contact:

1. [`00-Overview.md`](00-Overview.md) - vision, goals, non-goals, current state.
2. [`01-Architecture.md`](01-Architecture.md) - layer map, modules, dependency
   rules.
3. [`02-DI-Design.md`](02-DI-Design.md) - DI core design (headline document).
4. [`03-Reactive-Design.md`](03-Reactive-Design.md) - reactive core design
   (headline document).
5. [`04-Performance-Targets.md`](04-Performance-Targets.md) - benchmark gates.
6. [`05-Implementation-Phases.md`](05-Implementation-Phases.md) - phase-by-phase
   ticket list.
7. [`06-Agent-Playbook.md`](06-Agent-Playbook.md) - rules of engagement for AI
   agents working on the framework.
8. [`07-Competitive-And-AI-Roadmap.md`](07-Competitive-And-AI-Roadmap.md) -
   competitive positioning and AI-friendliness roadmap.
9. [`08-Surpass-VContainer.md`](08-Surpass-VContainer.md) - remaining work to
   keep Onity ahead of VContainer across speed, scope, and proof.
10. [`09-OnityTask-Integration-Plan.md`](09-OnityTask-Integration-Plan.md) -
    target and phased plan for replacing UniTask usage with OnityTask.
11. [`10-OnityTask-MainThreadSwitch.md`](10-OnityTask-MainThreadSwitch.md) -
    Editor lifecycle, hook ownership, and verification contract for
    `OnityTask.SwitchToMainThread`.
12. [`11-OnityTask-AsyncBuilderGap.md`](11-OnityTask-AsyncBuilderGap.md) -
    measured `async OnityTask` builder overhead, its causes, the
    execution-context decision, and the pooled runner implementation status.
13. [`12-OnityTask-PlayerVerification.md`](12-OnityTask-PlayerVerification.md) -
    Player startup diagnosis, the reproducible Release Player baseline and the
    dated core-optimization checkpoints.
14. [`13-OnityTask-ApiCoverageAndJobs.md`](13-OnityTask-ApiCoverageAndJobs.md) -
    staged UniTask API coverage and Jobs integration plan; the 0.4.0 release
    boundary.
15. [`14-OnityTask-BurstReadiness.md`](14-OnityTask-BurstReadiness.md) -
    accepted benchmark-only Burst readiness probe, measurement boundaries and
    conditional gate for a later full async integration.
16. [`15-OnityTask-GeneralAsyncPerformance.md`](15-OnityTask-GeneralAsyncPerformance.md) -
    accepted staged general async investigation, full consumer-cycle benchmark
    and measured completion/return-path optimizations.
17. [`16-OnityTask-Surpass-UniTask-Roadmap.md`](16-OnityTask-Surpass-UniTask-Roadmap.md) -
    the 0.6.0 roadmap to surpass UniTask 2.5.11. Its performance lane was
    superseded by the executed core redesign
    ([`notes/perf-7-core-redesign.md`](notes/perf-7-core-redesign.md)); the
    per-packet notes are under [`notes/`](notes/).

## Scope discipline

The user's stated priority is the **main system: DI + Reactive**. Everything
else (plugins, samples, extra DOTS bridges) is deferred.

If a doc starts drifting outside DI/Reactive without explicit user direction,
revisit `00-Overview.md` Section "Non-Goals".

## Status

| Doc | Status | Owner |
|---|---|---|
| 00-Overview | Draft v1 | - |
| 01-Architecture | Draft v1 | - |
| 02-DI-Design | Draft v1 | - |
| 03-Reactive-Design | Draft v1 | - |
| 04-Performance-Targets | Draft v1 | - |
| 05-Implementation-Phases | Draft v1 | - |
| 06-Agent-Playbook | Draft v1 | - |
| 07-Competitive-And-AI-Roadmap | Draft v1 | - |
| 08-Surpass-VContainer | Draft v1 | - |
| 09-OnityTask-Integration-Plan | Draft v1 | - |
| 10-OnityTask-MainThreadSwitch | Draft v1, Unity verification pending | - |
| 11-OnityTask-AsyncBuilderGap | Draft v2, option C implemented, Unity verification pending | - |
| 12-OnityTask-PlayerVerification | Checkpoints through 2026-09-30 | - |
| 13-OnityTask-ApiCoverageAndJobs | Stages 1-4c shipped in 0.4.0; Stage 4d continued as plan 16 PAR-C1 | - |
| 14-OnityTask-BurstReadiness | Release correctness verified; return boundary limited, no general integration | - |
| 15-OnityTask-GeneralAsyncPerformance | Completion guard reverted; return-buffer packet retired by plan 16 (R1) | - |
| 16-OnityTask-Surpass-UniTask-Roadmap | Performance lane superseded by PERF-7; 2026-10-02 IL2CPP gate passed (29 of 29 rows) | - |

Draft v1 = first complete pass, not yet validated against running benchmarks.

## Relationship to other docs

- Root `AGENTS.md` is the canonical agent rule sheet. These planning docs
  refine the design but do not override `AGENTS.md`.
- Root `codex-code-style.md` is the canonical style guide. These planning docs
  defer to it for naming, formatting, and member ordering.
- `Assets/Onity-Packages/Onity/ENGINEERING.md` describes the current
  implementation. When this plan and `ENGINEERING.md` disagree, the plan
  describes the **target** state and `ENGINEERING.md` should be updated as
  work lands.
- Root `EVENT_HUB_PLAN.md` is a focused plan for the messaging EventHub. It
  remains the source of truth for that subsystem and is referenced from
  `02-DI-Design.md` and `03-Reactive-Design.md` where relevant.
