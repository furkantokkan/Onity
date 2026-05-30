---
title: "ADR 0001: DOTS and DI Performance"
nav_order: 1
---

# ADR 0001: DOTS and DI Performance

## Status

Accepted.

## Date

2026-05-30.

## Context

Onity's DI benchmark currently has two measured managed paths:

- `Onity (Baked)`: dense-id lookup plus the same managed providers used by the
  reflection path.
- `Onity (Reflection)`: managed dictionary/provider path with cached reflection
  metadata and AOT-safe activation.

The latest Editor/Mono benchmark shows `Onity (Baked)` ahead of VContainer in
the measured resolve and prepare/register scenarios, including
`Prepare & Register (Complex)`. The remaining strategic question is whether DOTS,
Jobs, or Burst should be used to make the DI system faster.

Unity DOTS/Burst is designed for blittable data-oriented workloads. Onity DI
constructs and injects managed objects, stores `Type` and reflection metadata,
uses managed providers/delegates, and returns object references. Those operations
cannot run inside Burst jobs.

## Decision

Do not move `Onity.DI` container build or resolve into DOTS, Jobs, or Burst.

Keep managed DI as a pure C# module with the current baked graph and cached
metadata strategy. Use DOTS only at explicit bridge boundaries where data is
already blittable or can be copied into blittable event buffers.

For IL2CPP/AOT speed, prioritize source-generated or IL-postprocessed activators
over DOTS. A generated activator path can emit direct `new T(...)` and member
assignment calls for known bindings, preserving the existing DI API while
removing runtime expression compilation and much of the reflection fallback cost.

## Alternatives Considered

### Move DI resolve into Burst jobs

Rejected.

Burst cannot construct arbitrary managed service graphs, call reflection APIs,
hold managed `Type` objects, invoke managed provider delegates, or return managed
object references from a job. Emulating DI with entity data would become a
different product and would break the Zenject-familiar API goal.

### Use jobs during `Build()` to prepare baked tables

Rejected for now.

Small pieces of `Build()` could theoretically be transformed into native arrays
of integer ids and sorted in jobs, but the managed work still dominates: binding
registration, provider creation, reflection metadata, lifecycle inspection, and
callback collection. The extra marshaling would add complexity with little
expected benefit for the measured graph sizes.

### Use DOTS for messaging/reactive/event workloads

Accepted as a separate boundary.

DOTS can help when the workload is already data-oriented: large batches of
blittable events, ECS skill-stat processing, physics/raycast batches, or
simulation systems that do not need managed DI during execution. Managed Onity
services may enqueue data into a DOTS bridge, then Burst systems process the
blittable data and publish results back on the main thread.

### Generate AOT-safe DI code

Accepted as the preferred future optimization.

Source-generated activators are aligned with the current architecture: the user
keeps the same DI API, the container remains managed and testable, IL2CPP gets a
fast path without `Expression.Compile`, and the generated code can be measured
against VContainer's source-generated path.

## Consequences

- `Onity.DI` remains engine-free and keeps `noEngineReferences: true`.
- `Onity.DOTS` stays a bridge module, not a dependency of DI.
- No managed container resolve is allowed inside Burst jobs.
- Performance work for DI should focus on:
  - source-generated activators for IL2CPP/AOT;
  - reducing managed provider indirection where measurement proves it matters;
  - keeping the baked graph lean;
  - preserving parity tests between baked and reflection paths.
- Performance work for DOTS should focus on modules where data is naturally
  blittable and batchable, especially event bridges, physics, skill stats, and
  simulation samples.

## Verification

No runtime behavior changes are introduced by this ADR. The decision is based on
Unity DOTS/Burst constraints and the current benchmark evidence in
`Packages/com.onity.framework/Benchmarks/Results/di-benchmark-latest.json`.
