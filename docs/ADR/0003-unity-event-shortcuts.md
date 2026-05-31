---
title: "ADR 0003: Unity Event Shortcuts"
nav_order: 3
---

# ADR 0003: Unity Event Shortcuts

## Status

Accepted.

## Date

2026-05-31.

## Context

`MessageBroker` and `OnityEventHub` are already auto-bound in every
`OnityContext`, but using them from Unity-created objects still required field
or constructor injection. That made the common gameplay case too verbose:
publish a small event from a MonoBehaviour, subscribe from a HUD component, or
observe an event stream.

Onity still needs scoped event behavior. A global singleton event bus would be
shorter, but it would blur `ProjectContext`, `SceneContext`, and
`GameObjectContext` ownership and make isolated object graphs harder to reason
about.

## Decision

Add Unity-facing shortcuts:

- `Onity.Publish(message)`
- `Onity.Subscribe<T>(handler)`
- `Onity.Observe<T>()`
- owner overloads such as `Onity.Publish(owner, message)` and
  `Onity.Subscribe<T>(owner, handler)`

The no-owner overloads resolve the default active context in this order:

1. most recently initialized active `SceneContext`
2. active `ProjectContext`
3. any remaining active context as a fallback

The owner overloads resolve the nearest `OnityContext` in the owner's parent
hierarchy, then fall back to the default active context. Owner subscriptions are
also tied to owner destroy through the existing `AddTo(Component)` lifetime
helper.

## Alternatives Considered

### Keep only constructor or field injection

Rejected.

It is still the right model for plain services, but it is unnecessarily noisy
for common MonoBehaviour event triggers.

### Add one process-wide static event bus

Rejected.

That would be easy to call but would break Onity's scoped context model.

### Only add extension methods such as `this.Publish(...)`

Rejected as the only API.

The owner-based extension is useful for exact `GameObjectContext` routing, but
the preferred simple scene/project case should not require writing `this`.

## Consequences

- MonoBehaviour event code can use `Onity.Publish(...)` directly.
- Exact object-scope routing remains available through owner overloads.
- The shortcut does not search the scene on every call; active contexts register
  themselves during context `Awake`.
- Plain services should still use constructor injection for testability and
  explicit dependencies.
