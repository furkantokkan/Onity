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
