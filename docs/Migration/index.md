---
title: "Migration"
nav_order: 4
has_children: true
description: "Mapping tables for moving a project from Zenject, VContainer, R3, UniRx, UniTask or MessagePipe to Onity."
---

# Migration

Each guide maps one library's API to Onity, names the behaviors that differ, and lists what Onity does
not ship so you do not look for it.

- [From Zenject](From-Zenject.html): bindings, injection, factories, lifecycle, scopes, signals.
- [From VContainer](From-VContainer.html): registration, lifetimes, entry points, async startup.
- [From R3 and UniRx](From-R3.html): primitives, operators, lifetime, what is not shipped.
- [From UniTask](From-UniTask.html): name mapping, behavior differences, 0.5 to 0.6 notes.
- From MessagePipe: the [Migrating from MessagePipe](../guide/events-messaging.html#migrating-from-messagepipe)
  section of the Events and Messaging guide, and the
  [Messaging vs MessagePipe](../comparisons/messaging-vs-messagepipe.html) comparison.

## Method

- Replace one library at a time. Finish the DI move before the reactive move, and the reactive move
  before the async move; the mapping tables assume the earlier pillar is already on Onity.
- Keep the old library only inside the bounded context that still depends on it. Do not add adapters
  that bridge the two libraries across the whole project; convert one scene or feature, verify it, then
  move to the next.
- Verify every name against the installed source under `Packages/com.onity.framework/Runtime/`. The
  tables are checked against Onity 0.8.0; an API that is not in a table does not exist in Onity, and a
  Zenject, VContainer, R3, UniRx, UniTask or MessagePipe analogy is not evidence that it does.
- Read [Lifecycle and Scopes](../guide/lifecycle-and-scopes.html) before you move async startup code.
  The scope token and `IOnityAsyncInitializable` replace most hand-written startup ordering.
