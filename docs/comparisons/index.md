---
title: "Comparisons"
nav_order: 5
has_children: true
description: "What Onity was measured against, how to read the ratios, and where the evidence pages are."
---

# Comparisons

Use these pages to see how Onity's pillars compare with the libraries they replace, feature by feature
and with measured results. Each page keeps every number next to its conditions (date, Unity version,
scripting backend, process count, settings) and states what the measurement does not cover. This hub
has no numbers; the pages do.

## What is compared

| Page | Onity pillar | Compared with | What it measures |
| --- | --- | --- | --- |
| [DI vs VContainer and Zenject](../Onity-vs-VContainer-Zenject.html) | `OnityContainer`, `OnityObjectPool<T>`, `PooledFactory` | VContainer, Zenject | Feature breadth, resolve and build timing, allocation per operation, pooled factory timing |
| [Reactive vs R3 and UniRx](reactive-vs-r3-unirx.html) | `Onity.Reactive` | R3, UniRx | Feature breadth and the measured operator workloads |
| [OnityTask vs UniTask](../guide/onitytask-comparison.html) | `OnityTask` | UniTask | The Release Player gate, API coverage and the remaining gaps |
| [Messaging vs MessagePipe](messaging-vs-messagepipe.html) | `Onity.Messaging` | MessagePipe | Feature breadth and the measured publish, subscribe and dispose, keyed and async workloads |

## How to read a ratio

- A ratio is Onity's time divided by the other library's time for the same workload in the same
  process. Below 1 means Onity took less time; above 1 means it took more.
- A page reports the median across the measured processes and names the worst process, so a single
  fast or slow run cannot carry a claim on its own.
- Timing results are timing results. Allocation, other platforms and other workloads are separate
  measurements, and a page says when it has none.
- The conditions travel with the number: the Unity version, the backend (Mono or IL2CPP), the number of
  processes, the date and the settings that differ from the defaults. The known slower cases are
  stated on the same page, not elsewhere.
- Every result was produced by the runners in this repository on one Windows machine. They are
  reproducible with those runners; they are not a guarantee for other hardware, Unity versions or
  object graphs.

## Evidence pages

The comparison pages cite these records, which are kept unchanged as measured:

- [DI re-measurement at the published 0.6.0 package (2026-10-02)](../benchmarks/di-remeasure-2026-10-02.md),
  the [re-measurement at 7266dd8 (2026-10-01)](../benchmarks/remeasure-2026-10-01.md), the
  [September 2026 DI summary](../benchmarks/advanced-di-summary-2026-09-23.md) and the
  [September 24 follow-up](../benchmarks/di-speed-followup-2026-09-24.md).
- [Onity.Reactive vs R3 and UniRx Release Player comparison (2026-10-02)](../assets/benchmarks/reactive-surpass-r3-2026-10-02.md),
  with the five run summaries beside it.
- [OnityTask vs UniTask Release Player gate (2026-10-02)](../assets/benchmarks/onitytask-surpass-2026-10-02.md).
- [Onity.Messaging vs MessagePipe 1.8.1 Release Player comparison (2026-10-02)](../assets/benchmarks/messaging-surpass-messagepipe-2026-10-02.md),
  with the three run summaries beside it.
- [Pooling: own-stack pool measurements (2026-10-01)](../benchmarks/pool-own-stack-2026-10-01.md) and
  the [factory and pooling verification](../benchmarks/factory-pooling-2026-09-23.md).

Older measurements that the current pages no longer carry are in
[Measurement history](../archive/index.html), unchanged and dated.
