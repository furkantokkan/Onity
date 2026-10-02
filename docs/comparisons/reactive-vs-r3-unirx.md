---
title: "Reactive vs R3 and UniRx"
parent: "Comparisons"
nav_order: 2
description: "Onity.Reactive compared with R3 1.3.0 and UniRx 7.1.0: feature shape, the measured Release Player results for 0.7.0, the conditions, the fairness notes and the 0.6.0 baseline."
---

# Reactive vs R3 and UniRx

This page compares `Onity.Reactive` with the two reactive libraries it replaces in a Unity project, R3
1.3.0 and UniRx 7.1.0: first the feature shape, then the measured results of the 0.7.0 core with their
conditions and caveats. The numbers come from one evidence record,
[reactive-surpass-r3-2026-10-02.md](../assets/benchmarks/reactive-surpass-r3-2026-10-02.md), whose
summaries were produced by the comparison harness in this repository; the mapping for moving code over
is [From R3 and UniRx](../Migration/From-R3.html).

Contents: [Scope](#scope), [Feature comparison](#feature-comparison),
[Measured results for 0.7.0](#measured-results-for-070), [What changed in 0.7.0](#what-changed-in-070),
[Conditions](#conditions), [Fairness notes](#fairness-notes), [Caveats](#caveats),
[The 0.6.0 baseline](#the-060-baseline).

## Scope

The comparison is a timing comparison of nine synchronous main-thread workloads: publish to one and to
eight subscribers, a property set that changes and one that does not, subscribe and dispose on a
subject, a property and a `Where` plus `Select` chain, a fused `Where` and `Select` publish, and a
two-source `CombineLatest` publish. Each library runs the same workload through its idiomatic API, and a
library-independent model checks that all three produced the same results. It is not a feature-parity
test, it measured no allocation, and it covers neither time operators, thread-pool operators and frame
streams nor the async bridges.

## Feature comparison

| Area | Onity.Reactive (0.7.0) | R3 1.3.0 | UniRx 7.1.0 |
| --- | --- | --- | --- |
| Stream contract | `IOnityObservable<T>`, hot by default | `Observable<T>` with `OnNext`, `OnErrorResume` and `OnCompleted(Result)` | `IObservable<T>` (`System`) |
| Primitives | `Subject<T>`, `ReactiveProperty<T>` (equal values skipped, `SetValue` returns whether it changed), `IReadOnlyReactiveProperty<T>` | `Subject<T>`, `ReactiveProperty<T>`, `BehaviorSubject<T>`, `ReplaySubject<T>` and others | `Subject<T>`, `ReactiveProperty<T>`, `BehaviorSubject<T>`, `ReplaySubject<T>` and others |
| Operators | the synchronous set (`Where`, `Select`, `DistinctUntilChanged`, `Skip`, `SkipWhile`, `Take`, `TakeWhile`, `StartWith`, `Scan`, `Pairwise`, `Buffer`, `Merge`, `CombineLatest` over two sources, `Sample`), the time set (`Debounce`, `Throttle`, `ThrottleLast`, `Buffer`), the async set (`SelectAwait`, `WhereAwait`, `TakeUntil`) and the scheduling set (`ObserveOn`, `ObserveOnMainThread`, thread pool) | a large operator set including `Zip`, `Switch`, `Concat` and the multicast operators | a large operator set including `Window`, `Zip`, `Switch`, `Concat` and the multicast operators |
| Error channel | none in the core operators; a throwing subscriber goes to `OnityObservableExceptionHandler` and delivery continues | `OnErrorResume` on every observer | `OnError` ends the subscription |
| Time and frames | `OnityTimeProvider` for the time operators, `OnityFrameProvider` for `ObserveOn`; Unity providers in `Onity.Unity.Reactive` | `TimeProvider` and `FrameProvider`; Unity providers in `R3.Unity` | `MainThreadDispatcher` and `Observable.EveryUpdate` |
| Subscription lifetime | `AddTo(this)`, `TakeUntilDisable(this)`, `AddTo(scope)`, `AddTo(bag)`; no `AddTo(GameObject)` | `AddTo` on a `GameObject` or `Component`, `CompositeDisposable` | `AddTo(GameObject)`, `CompositeDisposable` |
| Threading | main thread only; no locks | thread-safe subjects and operators | thread-safe subjects and operators |
| Integration with the rest of the stack | the same `IOnityObservable<T>` for messages (`Observe<T>()`), the DI scope token, `OnityTask` waits on properties | a reactive library; DI, messaging and async come from other packages | a reactive library with its own `MessageBroker` |
| Runtime dependencies | none beyond Unity | the netstandard2.1 DLL plus `Microsoft.Bcl.TimeProvider`, `Microsoft.Bcl.AsyncInterfaces`, `System.ComponentModel.Annotations`, `System.Threading.Channels`, `System.Runtime.CompilerServices.Unsafe` | none beyond Unity |
| Maintenance | this repository | maintained | archived upstream; 7.1.0 is the last release |

Onity ships fewer operators by design. The missing ones and their replacements are listed in
[What is not shipped](../guide/reactive.html#what-is-not-shipped).

## Measured results for 0.7.0

IL2CPP is the headline backend. A ratio is Onity's time divided by the other library's time for the
same workload in the same process; below 1 means Onity took less time. A row is faster when the median
ratio over the processes is at most 0.95 and the worst process is at most 1.00, slower when the median is
at least 1.05, and on par otherwise; the rule was fixed before any measurement.

In the run that measured the shipped 0.7.0 core (source `36c68c5`, Unity 2022.3.62f2, Windows x64,
IL2CPP Release Players, three processes), Onity.Reactive was faster than R3 1.3.0 in all nine rows,
with medians from 0.145 to 0.805 and no process above 0.811, and faster than UniRx 7.1.0 in all nine
rows, with medians from 0.129 to 0.872 and no process above 0.874:

| Scenario | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 3.23 | 4.39 | 3.70 | 0.737 (0.739) | faster | 0.872 (0.874) | faster |
| SubjectOnNext8 | 15.45 | 23.84 | 29.68 | 0.634 (0.666) | faster | 0.521 (0.524) | faster |
| SubjectSubscribeDispose | 90.34 | 265.85 | 513.75 | 0.340 (0.365) | faster | 0.183 (0.196) | faster |
| PropertySetChanged | 6.56 | 11.76 | 12.26 | 0.558 (0.558) | faster | 0.535 (0.541) | faster |
| PropertySetSame | 4.69 | 6.59 | 7.10 | 0.713 (0.716) | faster | 0.659 (0.660) | faster |
| PropertySubscribeDispose | 103.69 | 289.68 | 180.90 | 0.368 (0.370) | faster | 0.566 (0.589) | faster |
| WhereSelectOnNext | 7.33 | 9.10 | 9.86 | 0.805 (0.811) | faster | 0.758 (0.812) | faster |
| ChainSubscribeDispose | 307.18 | 541.54 | 871.02 | 0.567 (0.593) | faster | 0.353 (0.396) | faster |
| CombineLatestOnNext | 6.28 | 43.47 | 48.51 | 0.145 (0.146) | faster | 0.129 (0.131) | faster |

Mono is reported, not gated. The same Players measured Onity faster than R3 in seven rows, on par in
`PropertySetSame` (1.040) and slower in `CombineLatestOnNext` (1.378). Against UniRx on Mono, Onity was
faster in five rows, on par in `PropertySetSame` (1.001) and `WhereSelectOnNext` (1.015), and slower in
`SubjectOnNext1` (1.098) and `CombineLatestOnNext` (1.527):

| Scenario | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 5.26 | 8.73 | 4.79 | 0.603 (0.605) | faster | 1.098 (1.099) | slower |
| SubjectOnNext8 | 24.85 | 52.85 | 33.68 | 0.470 (0.471) | faster | 0.738 (0.744) | faster |
| SubjectSubscribeDispose | 94.94 | 284.32 | 471.77 | 0.330 (0.357) | faster | 0.201 (0.215) | faster |
| PropertySetChanged | 8.62 | 19.05 | 13.23 | 0.452 (0.452) | faster | 0.644 (0.654) | faster |
| PropertySetSame | 5.37 | 5.15 | 5.32 | 1.040 (1.043) | on par | 1.001 (1.016) | on par |
| PropertySubscribeDispose | 104.77 | 258.94 | 186.87 | 0.405 (0.419) | faster | 0.561 (0.568) | faster |
| WhereSelectOnNext | 8.83 | 12.16 | 8.51 | 0.719 (0.744) | faster | 1.015 (1.104) | on par |
| ChainSubscribeDispose | 329.70 | 539.45 | 708.26 | 0.614 (0.615) | faster | 0.466 (0.483) | faster |
| CombineLatestOnNext | 45.70 | 33.50 | 29.92 | 1.378 (1.390) | slower | 1.527 (1.551) | slower |

The per-process ratios behind every median are in the evidence record.

## What changed in 0.7.0

The results above come from a redesign of the subscription and delivery core, with no change to the
public API or behavior:

- A subscription is one object, an internal node that is both the entry the source keeps and the
  `IDisposable` returned to the subscriber. Unsubscribing outside a notification is a constant-time slot
  swap; during a notification it clears the slot, and the list compacts after the outermost
  notification ends.
- A notification pass runs in one exception region. A throwing observer is reported to
  `OnityObservableExceptionHandler` and delivery continues with the next observer, as before.
- `Subject<T>`, `ReactiveProperty<T>` and the operator observables accept nodes directly, so operator
  sinks and `Subscribe(Action<T>)` subscribe without creating a delegate; `ReactiveProperty<T>` keeps its
  own node list.
- `Where`, `Select`, `DistinctUntilChanged`, `Skip`, `SkipWhile`, `Take`, `TakeWhile`, `StartWith`, `Scan`
  and `Pairwise` are sink-based operators, and `Where` followed by `Select` is fused into one sink.
- While exactly one live subscriber is registered, `Subject<T>` and `ReactiveProperty<T>` deliver to it
  through a direct reference without the general loop; a subscribe, unsubscribe or dispose inside that
  callback falls back to the general pass, so nothing observable changes.
- On IL2CPP, null checks and array bounds checks are off on the hot reactive classes.

The measurements ran as two pre-registered gates. The R3 gate required every IL2CPP row to be faster
than R3: the first attempt at this core (`b73c20e`) missed it on one row, `SubjectOnNext1` at 1.061;
the second (`9563453`) trimmed the per-call work in `Subject.OnNext` and `ReactiveProperty.SetValue`
and passed, with single-subscriber publish on par with UniRx (1.044). The follow-up gate, limited to two
attempts, required every IL2CPP row to be faster than both libraries; its first attempt (`36c68c5`, the
lone-subscriber delivery) passed and is the 0.7.0 result above. `CombineLatest` was not redesigned.

## Conditions

- Unity 2022.3.62f2, Windows x64, non-development Release Players, IL2CPP with managed stripping Minimal,
  AMD Ryzen 9 5900X; three processes per backend, 2 warmup and 15 measured samples per library and
  scenario, library order rotated per sample.
- R3 1.3.0 as the NuGet netstandard2.1 DLL without `R3.Unity`; UniRx 7.1.0 from the official release
  unitypackage, unmodified. Library defaults were left alone.
- The harness (`Packages/com.onity.framework/Benchmarks/Reactive/**` and the two host scripts) was frozen
  before the first measurement, and the classification rule was pre-registered.
- Checksums matched across all three libraries in every process.

## Fairness notes

- All three libraries subscribe through `Subscribe(Action<int>)` with one cached action. Each library
  wraps that action its own way, and the wrapper is part of what is measured: Onity stores the action in
  the subscription node and invokes it directly; R3 wraps it in an `AnonymousObserver`; UniRx wraps it in
  a subscribe observer.
- Onity's node delivery checks an `Action<T>` callback first, then an `Observer<T>` callback, then the
  sink's virtual `OnNext`. A lambda passed to Onity's `Subscribe(Observer<T>)` therefore pays one extra
  load and test per value compared with the measured `Subscribe(Action<T>)` path.
- Onity.Reactive is main-thread only and takes no locks, while R3's and UniRx's `CombineLatest` lock per
  value. That explains most of the `CombineLatest` gap on IL2CPP, and it is a design difference, not a
  benchmark artifact.
- All three libraries fuse `Where(...).Select(...)` into one operator.

## Caveats

- Timing on one Windows PC. Other machines, platforms, Unity versions and Development Players are not
  covered.
- Allocation was not measured on either backend. The counter is never called on IL2CPP, and on Mono its
  positive control failed, so no allocation claim follows from this comparison.
- Other Unity Editors were open and compiling during the R3 gate run, and the noise screen flagged every
  process in both gate runs (the follow-up run did get its quiet window). The ratios come from the same
  process with rotated library order, and the rule reads the worst process.
- On Mono, `CombineLatest` stays about 1.4x slower than R3 and 1.5x slower than UniRx, single-subscriber
  publish is slower than UniRx (1.098), and the same-value property set is on par with both libraries.
- The nine rows are the synchronous core. They say nothing about the time operators, the thread-pool
  operators, the frame streams or the async bridges.

## The 0.6.0 baseline

The published 0.6.0 runtime was measured first with the same harness (source `4e92db9`, three processes,
then a five-process confirmation). On IL2CPP it was faster than R3 in `CombineLatestOnNext` (0.190),
`PropertySetSame` (0.773), `PropertySetChanged` (0.869) and `SubjectOnNext8` (0.680), on par in
`WhereSelectOnNext` (0.956), and slower in `SubjectOnNext1` (1.162), `SubjectSubscribeDispose` (1.658),
`PropertySubscribeDispose` (1.523) and `ChainSubscribeDispose` (2.570). The five-process run moved
`SubjectOnNext8` and `PropertySetChanged` to on par and `WhereSelectOnNext` to faster, and left the four
slower rows slower. On Mono the 0.6.0 runtime was slower than R3 in every row, the publish rows by 2.7x
to 5.0x. The full baseline tables are in the evidence record; the redesign above is what changed
between the baseline and the 0.7.0 results.
