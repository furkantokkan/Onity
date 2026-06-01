---
title: "ADR 0004: Refactoring from Existing Architecture"
parent: "Architecture Decisions"
nav_order: 4
---

# ADR 0004: Refactoring from Existing Architecture

## Status

Accepted.

## Date

2026-06-01.

## Context

Onity documentation already explains the API surface, but users migrating from
Unity singleton managers or from another DI container still need a concrete
refactoring reference. Without one, examples can accidentally promote the wrong
shape: global `GameManager.Instance` access, service-location from `Update`, or
large manager classes that mix UI, scene flow, state, and game rules.

The framework's architecture goal is stricter than "use a container":
domain/gameplay rules should live in plain C# services with constructor
dependencies, while `MonoBehaviour` scripts should stay as thin adapters for
Unity callbacks, views, physics, input, and prefab boundaries.

## Decision

Add a guide named **Refactoring from Existing Architecture** that shows two before/after
migrations:

- a `GameManager.Instance` singleton converted into a small `IScoreService`,
  reactive state, and thin MonoBehaviour adapters
- a VContainer lifetime-scope/entry-point manager converted into an Onity
  `MonoInstaller` plus `IOnityInitializable` / `IOnityTickable` service

The guide uses real Onity concepts:

- constructor injection for services
- `[Inject]` only for Unity-created MonoBehaviours
- `OnityEventHub` inside services
- `OnityEvent` shortcuts for Unity-facing event code when needed
- lifecycle interfaces collected automatically by the Onity context

## Alternatives Considered

### Keep only API documentation

Rejected.

API references explain what calls exist, but they do not teach how to move a
manager-heavy Unity project toward testable responsibilities.

### Add a static Onity service-locator facade

Rejected.

That would make migration look easy, but it would recreate the hidden
dependency problem Onity is meant to remove.

### Provide only a VContainer syntax mapping

Rejected as the only documentation.

Syntax mapping helps migration, but it does not explain the architectural
target: smaller services, explicit roles, and thin Unity adapters.

## Consequences

- The docs have one canonical reference for converting singleton managers and
  VContainer managers into Onity-style services.
- The examples reinforce the same dependency model as the runtime architecture:
  services depend on abstractions; Unity scripts adapt scene objects to those
  services.
- The guide is documentation-only and does not change runtime behavior.
