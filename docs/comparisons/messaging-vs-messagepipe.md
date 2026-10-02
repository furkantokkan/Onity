---
title: "Messaging vs MessagePipe"
parent: "Comparisons"
nav_order: 4
description: "Onity.Messaging compared with MessagePipe 1.8.1: feature shape, the measured Release Player results for 0.8.0 against the Unity package and the .NET build, the conditions, the fairness notes and the 0.7.0 baseline."
---

# Messaging vs MessagePipe

This page compares `Onity.Messaging` with MessagePipe 1.8.1, the typed pub/sub library it replaces in a
Unity project: first the feature shape, then the measured results of the 0.8.0 core with their
conditions and caveats. The numbers come from one evidence record,
[messaging-surpass-messagepipe-2026-10-02.md](../assets/benchmarks/messaging-surpass-messagepipe-2026-10-02.md),
whose summaries were produced by the comparison harness in this repository. The summaries print
"Onity 0.7.0" because the package version was not yet raised when they ran; the measured runtime is
source `4cc5763`, as the evidence record explains. The mapping for moving code over is
[Migrating from MessagePipe](../guide/events-messaging.html#migrating-from-messagepipe).

Contents: [Scope](#scope), [Feature comparison](#feature-comparison),
[Measured results for 0.8.0](#measured-results-for-080), [The .NET build](#the-net-build),
[What changed in 0.8.0](#what-changed-in-080), [Conditions](#conditions),
[Fairness notes](#fairness-notes), [Caveats](#caveats), [The 0.7.0 baseline](#the-070-baseline).

## Scope

The comparison is a timing comparison of nine main-thread workloads: publish to no, one and eight
subscribers; subscribe and dispose with one and with 64 resident subscribers; keyed publish and keyed
subscribe and dispose over 16 keys; and async publish to one and to eight handlers that complete
synchronously, consumed without awaiting. Each library runs the same workload through its public
interfaces with its documented delegate `Subscribe` overloads, and a library-independent model checks
that both produced the same results. It is not a feature-parity test, it measured no allocation, and it
covers neither MessagePipe's filters, request/response, buffered publishers, interprocess transports and
`Parallel` async strategy nor Onity's `Observe<T>()` bridge and `OnityTask` receives.

## Feature comparison

| Area | Onity.Messaging (0.8.0) | MessagePipe 1.8.1 (Unity package) |
| --- | --- | --- |
| Setup | Every context binds `MessageBroker` (`IMessageBroker`) and `OnityEventHub`; no registration per message type. `BindMessageChannel<T>()` injects `IPublisher<T>` or `ISubscriber<T>` directly; `DeclareMessage<T>()` and `DeclareAsyncMessage<T>()` create standalone channels | `AddMessagePipe()` plus `AddMessageBroker<T>()` per message type on a DI builder (`BuiltinContainerBuilder` in the package; the VContainer and Zenject integrations are not part of it), or `GlobalMessagePipe` over a service provider |
| Publisher and subscriber | `IPublisher<T>`, `ISubscriber<T>`; `Subscribe(MessageHandler<T>)` returns `IDisposable` | `IPublisher<T>`, `ISubscriber<T>` with singleton and scoped variants; `Subscribe(IMessageHandler<T>, filters)` and the `Subscribe(Action<T>, ...)` extension with an optional predicate |
| Keyed | `KeyedMessageChannel<TKey, T>` (`IKeyedPublisher`, `IKeyedSubscriber`), one inner channel per key, bound with `BindInstance`; synchronous only | `IPublisher<TKey, T>`, `ISubscriber<TKey, T>` and the async keyed pair |
| Async delivery | `AsyncMessageChannel<T>`: `PublishAsync` returns `ValueTask`, delivers sequentially, checks the token before each handler and never throws synchronously; `PublishOnityTask`, `SubscribeOnityTask`; `SubscribeQueued` puts a bounded queue in front of one consumer | `IAsyncPublisher<T>`, `IAsyncSubscriber<T>` over UniTask; `PublishAsync` with `AsyncPublishStrategy.Parallel` (the default) or `Sequential`, and a non-awaited `Publish(message, ct)` |
| Filters | None on the publish path; a per-consumer rule lives in the stream: `Observe<T>().Where(...)` | `MessageHandlerFilter<T>` and its async form as a pipeline per subscription, by attribute, globally through `MessagePipeOptions`, or as a predicate overload |
| Request and response | Not shipped; a query is a service call | `IRequestHandler<TRequest, TResponse>`, `IRequestAllHandler` and their async forms, with their own filters |
| Latest-value replay | Not shipped; current state is a `ReactiveProperty<T>` bound through DI | `IBufferedPublisher<T>` and `IBufferedSubscriber<T>` (sync and async), which deliver the latest message to a new subscriber; `EventFactory` for owner-created events |
| Streams and awaits | `Observe<T>()` returns the same `IOnityObservable<T>` as `Subject<T>`, so a message takes the operator chain without an adapter; `ReceiveAsync`, `ReceiveAllAsync` with an overflow policy | `AsObservable()` (`System.IObservable<T>`; operators need a reactive library), `AsAsyncEnumerable()` (UniTask async LINQ), `FirstAsync` |
| Subscription lifetime | `IDisposable` retained with `AddTo(this)`, `TakeUntilDisable(this)`, `AddTo(scope)` or `AddTo(bag)`; `OnityEvent.Subscribe(owner, handler)` also disposes when `owner` is destroyed | `IDisposable` collected with `DisposableBag`; `HandlingSubscribeDisposedPolicy` decides whether a subscribe on a disposed broker is ignored (the default) or throws |
| Threading | Main thread only; a channel takes no lock, and the broker locks its channel table (creation, count, diagnostics, dispose), never the publish path | Thread-safe brokers: subscribe and dispose take a lock, and so does every keyed publish |
| Error handling | A throwing handler propagates out of `Publish` and the remaining handlers for that message do not run; `PublishAsync` faults its task | The broker loop has no exception handling either (`MessageBrokerCore<T>.Publish`), so a throwing handler stops that publish the same way |
| Diagnostics | `MessageBroker.GetDiagnostics` (channels and subscriber counts), shown in `Onity/Tools/Monitor` | `MessagePipeDiagnosticsInfo` (subscribe count, optional stack-trace capture) and the `Window/MessagePipe Diagnostics` Editor window |
| Interprocess and distributed | Not shipped | `IDistributedPublisher` and `IDistributedSubscriber` with an in-memory implementation; the Redis and interprocess transports are separate packages |
| Runtime dependencies | none beyond Unity (`Onity.Messaging` is engine-free) | UniTask (the package's assembly references `UniTask` and `UniTask.Linq`); engine-free |
| Maintenance | this repository | Cysharp; 1.8.1 is the measured release |

Onity ships a smaller surface by design: no filters, no request/response, no buffered or replayed
messages and no interprocess transport.
[Migrating from MessagePipe](../guide/events-messaging.html#migrating-from-messagepipe) maps each of
these to the Onity shape that replaces it.

## Measured results for 0.8.0

IL2CPP is the headline backend. A ratio is Onity's time divided by MessagePipe's time for the same
workload in the same process; below 1 means Onity took less time. A row is faster when the median ratio
over the processes is at most 0.95 and the worst process is at most 1.00, slower when the median is at
least 1.05, and on par otherwise; the rule was fixed before any measurement.

MessagePipe comes in two builds with the same synchronous source and a different async API: the Unity
package, which Unity users install, compiles its async publish as an `async UniTask` method, and the
NuGet .NET build as an `async ValueTask` method. The claim stands on the Unity package. In the run that
measured the shipped 0.8.0 runtime against it (source `52e209a`, which is the runtime commit `4cc5763`
plus the harness's Unity-package flavor; MessagePipe 1.8.1 as `MessagePipe.1.8.1.unitypackage` compiled
against UniTask 2.5.10; Unity 2022.3.62f2, Windows x64, IL2CPP Release Players, three processes),
Onity.Messaging was faster than MessagePipe in all nine rows, with medians from 0.150 to 0.846 and no
process above 0.895:

| Scenario | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe median (worst) | Class |
| --- | ---: | ---: | ---: | --- |
| PublishNoSubscribers | 2.25 | 5.32 | 0.416 (0.423) | faster |
| Publish1 | 6.21 | 9.16 | 0.726 (0.767) | faster |
| Publish8 | 20.97 | 39.40 | 0.526 (0.532) | faster |
| SubscribeDispose | 63.49 | 377.52 | 0.168 (0.193) | faster |
| SubscribeDispose64 | 60.13 | 417.30 | 0.150 (0.216) | faster |
| KeyedPublish | 28.35 | 67.83 | 0.418 (0.436) | faster |
| KeyedSubscribeDispose | 104.37 | 552.26 | 0.172 (0.189) | faster |
| AsyncPublish1 | 27.32 | 43.86 | 0.623 (0.740) | faster |
| AsyncPublish8 | 103.19 | 126.20 | 0.846 (0.895) | faster |

Mono is reported, not gated. The same Players measured Onity faster in all nine rows, with medians from
0.280 to 0.756 and no process above 0.862:

| Scenario | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe median (worst) | Class |
| --- | ---: | ---: | ---: | --- |
| PublishNoSubscribers | 5.72 | 7.95 | 0.706 (0.720) | faster |
| Publish1 | 8.09 | 14.37 | 0.563 (0.682) | faster |
| Publish8 | 25.43 | 33.92 | 0.756 (0.786) | faster |
| SubscribeDispose | 80.04 | 294.98 | 0.297 (0.334) | faster |
| SubscribeDispose64 | 82.89 | 296.37 | 0.280 (0.287) | faster |
| KeyedPublish | 26.04 | 44.47 | 0.539 (0.586) | faster |
| KeyedSubscribeDispose | 98.78 | 338.57 | 0.292 (0.304) | faster |
| AsyncPublish1 | 59.80 | 117.66 | 0.514 (0.548) | faster |
| AsyncPublish8 | 255.98 | 366.75 | 0.665 (0.862) | faster |

The per-process ratios behind every median, the process medians and the gen-0 collection counts are in
the evidence record.

## The .NET build

The NuGet build (`lib/netstandard2.0/MessagePipe.dll` of `messagepipe.1.8.1.nupkg`) was measured
first, with the same workloads and the same rule. Against it the same runtime (`4cc5763`, IL2CPP, three
processes) was faster in all nine rows, with medians from 0.197 to 0.807 and a worst process of 0.974
(one `Publish1` process); on Mono it was faster in all nine rows, medians 0.280 to 0.762, worst
process 0.830:

| Scenario | Onity ns/op | MessagePipe ns/op | IL2CPP median (worst) | Class | Mono median (worst) | Class |
| --- | ---: | ---: | ---: | --- | ---: | --- |
| PublishNoSubscribers | 2.80 | 7.16 | 0.459 (0.493) | faster | 0.709 (0.718) | faster |
| Publish1 | 9.22 | 9.47 | 0.807 (0.974) | faster | 0.570 (0.707) | faster |
| Publish8 | 17.98 | 35.14 | 0.513 (0.554) | faster | 0.762 (0.830) | faster |
| SubscribeDispose | 99.34 | 484.50 | 0.197 (0.205) | faster | 0.319 (0.334) | faster |
| SubscribeDispose64 | 114.17 | 495.46 | 0.218 (0.230) | faster | 0.280 (0.314) | faster |
| KeyedPublish | 26.43 | 59.43 | 0.427 (0.482) | faster | 0.548 (0.563) | faster |
| KeyedSubscribeDispose | 118.67 | 529.00 | 0.224 (0.243) | faster | 0.287 (0.341) | faster |
| AsyncPublish1 | 24.95 | 78.49 | 0.313 (0.334) | faster | 0.287 (0.298) | faster |
| AsyncPublish8 | 95.31 | 167.42 | 0.569 (0.604) | faster | 0.522 (0.543) | faster |

The ns/op columns are the IL2CPP values. The two builds share the synchronous source and differ in
the async API, and the async rows show it: on IL2CPP MessagePipe's `AsyncPublish1` measured 43.86 ns
in the Unity package against 78.49 ns in the .NET build, and `AsyncPublish8` 126.20 against 167.42 ns.
The likely reason is a code fact consistent with these numbers, not one the measurement isolates:
UniTask's method builder runs the state machine without an ExecutionContext scope, which the
`async ValueTask` builder pays on every publish. The Unity package is therefore the harder target in
the async rows and the one the claim stands on; the .NET build is reported because it was the first
gate.

## What changed in 0.8.0

The results above come from a redesign of the channel subscriptions and of the async publish pass, with
no change to the public API:

- A subscription is one object: the entry the channel keeps and the `IDisposable` returned to the
  subscriber, which knows its slot. Disposing it runs at most once and removes its slot in constant
  time: outside a publish pass the last subscriber moves into the freed slot; during a pass the slot is
  cleared and the slots are compacted in order after the outermost pass, after the async channel has
  copied its handler array once so the passes in flight keep the array they hold. The 0.7.0 runtime
  allocated an unsubscribe closure, its delegate and a `DisposableAction` per subscription and searched
  for the entry on dispose.
- In `MessageChannel<T>`, delivery order, delivery to a handler added during a pass, skipping of a
  handler removed during a pass, nested publish and exception propagation are unchanged; the async
  channel still delivers each pass to the handlers registered when it started. `Publish` and
  `PublishAsync` return at once when the channel has no subscriber.
- `AsyncMessageChannel<T>.PublishAsync` is no longer an `async` method. Handlers whose `ValueTask`
  completes synchronously run inline; the first pending one hands the rest of the pass to an awaiting
  continuation. Failures complete the returned `ValueTask` exactly as an `async` method would: canceled
  for `OperationCanceledException`, faulted otherwise, never thrown synchronously.
- The async channel copies its handler array only when a subscription is disposed while a pass holds
  it, instead of a snapshot copy on every publish; every pass still delivers to the handlers registered
  when it started. In-flight passes are counted, where a bool flag used to be cleared early by a nested
  publish.
- On IL2CPP, null checks and array-bounds checks are off on the hot messaging members, through
  `Onity.Messaging`'s own internal copies of the `Unity.IL2CPP.CompilerServices` attributes.
- Measured effect beyond the ratios: gen-0 collections during the three subscribe-and-dispose rows fell
  from 426 to 430 per row in the baseline to 90 to 93 in the Unity-package run and 92 to 93 in the
  NuGet run (IL2CPP, sum over three processes), while MessagePipe's stayed at 153 to 162.

One behavior changed, and it is listed under Changed in the changelog. Because `PublishAsync` is no
longer an `async` method, a handler that is not itself an `async` method runs its synchronous work in
the caller's context, whether it returns a completed `ValueTask` or is the first to return a pending
one: an `AsyncLocal<T>` value or a `SynchronizationContext` it sets stays visible to the caller after
`PublishAsync` returns, as with the synchronous `Publish`, OnityTask at its default
(`FlowExecutionContext` off) and MessagePipe's Unity build for a handler of that shape. An `async`
handler keeps its own scope, as in 0.7.0: its builder restores the context, so the caller does not see
its change. Handlers after the first pending one run inside the continuation, and the caller does not
see their changes either.

The measurements ran as two pre-registered gates with the same rule. Gate A, against the .NET build:
the released 0.7.0 runtime, measured first as the baseline, was faster in seven IL2CPP rows and slower
in `AsyncPublish1` (1.138) and `AsyncPublish8` (1.301); the first attempt at the new core (`4cc5763`)
passed in all nine rows on both backends. Gate B, against the Unity package: its first attempt measured
that runtime unchanged (`52e209a` adds only the harness flavor) and passed in all nine rows on both
backends; it is the 0.8.0 result above.

## Conditions

- Unity 2022.3.62f2, Windows x64, non-development Release Players, AMD Ryzen 9 5900X; three processes
  per backend, 2 warmup and 15 measured samples per library and scenario, library order alternating per
  sample; each process at High priority with an affinity mask that excludes cores 0 and 1.
- MessagePipe 1.8.1 as the Unity package for the headline (`MessagePipe.1.8.1.unitypackage` from the
  official Cysharp release, SHA-256 `36ff7aa0611272e22c0c596616f2b01dfb42507a206194d9da2a11d8976544b0`,
  its 54 entries verified byte for byte in the host, compiled by Unity against UniTask 2.5.10) and as the
  NuGet `netstandard2.0` dll for the first gate (SHA-256
  `cf8a702ab31bbb7da9ea6de4349f6dec9e214be396234f7e527d6ebe06a94f1b`). Both unmodified.
- The MessagePipe brokers are built from the package's public types with a default `MessagePipeOptions`
  and no filters, the way `AddMessagePipe` registers them, without a DI container. The async rows pass
  `AsyncPublishStrategy.Sequential` explicitly, because Onity's async channel is sequential and
  MessagePipe defaults to `Parallel`. Every other library default was left alone.
- The message is a `readonly struct` with one `int`; keys are `int`. Handlers are cached delegates
  created outside timing through each library's documented delegate overload; async handlers return an
  already completed task of their library's type.
- The harness (`Packages/com.onity.framework/Benchmarks/Messaging/**` and the two host scripts) was
  frozen before the first measurement, and the classification rule was pre-registered. The only later
  change, before any gate-B run, added the Unity-package flavor: the async handler's task type and the
  check that it completed, a provenance check against the `.unitypackage`, and the flavor fields in the
  reports.
- A library-independent model checked every sample's checksum, count and probe totals in every process,
  and the checksums of the two libraries were identical.

## Fairness notes

- The user code is the same shape for both libraries: the same loops, the same message, the same
  handler bodies, one cached delegate per handler kind and workload. The two workload files differ in
  their `using` line, the delegate types and the broker construction.
- Each library wraps the handler its own way, and the wrapper is part of what is measured. Onity stores
  the `MessageHandler<T>` in its channel's slot array and invokes it directly; a subscription allocates
  one object, the subscription itself. MessagePipe wraps the `Action<T>` in an
  `AnonymousMessageHandler<T>` and calls it through `IMessageHandler<T>.Handle`; a subscription
  allocates that wrapper and a `Subscription`, and runs the handler factory (global and attribute filter
  lookups, which find nothing here).
- MessagePipe's brokers take locks where Onity's channels do not: on subscribe and dispose, and on every
  keyed publish. Onity is main-thread only. That is a design difference, not a benchmark artifact, and
  nothing was disabled in either library.
- The async rows measure the publish pass with handlers that complete synchronously; no handler is
  awaited. Onity runs that pass inline; MessagePipe runs its `async UniTask` (Unity package) or
  `async ValueTask` (.NET build) method, the likely reason the two builds measure differently in those
  rows.
- The whole measurement runs synchronously inside one `AfterSceneLoad` callback, so no frame or
  PlayerLoop work interleaves with it. In the Unity-package run both Players also contain UniTask, whose
  PlayerLoop runners never execute inside the measurement.
- Samples are short, so the per-process ratios, not one number, carry a small difference.

## Caveats

- Timing on one Windows PC. Other machines, platforms, Unity versions and Development Players are not
  covered.
- Allocation was not measured on either backend. The counter is never called on IL2CPP, and on Mono its
  positive control read 0 bytes, so no allocation claim follows from this comparison; the evidence
  record lists gen-0 collection counts instead.
- Other Unity Editors and batch builds of other projects ran during the runs. The noise screen flagged
  every IL2CPP process of all three runs and every Mono process but one. Both libraries run in every
  process with alternating order, so the ratios carry the comparison, and the rule reads the worst
  process.
- The nine rows do not measure MessagePipe's filters, request/response, buffered publishers,
  interprocess transports or `Parallel` strategy, nor Onity's `Observe<T>()` bridge, `OnityEvent` or
  the `OnityTask` receives. UniTask 2.5.10 was the local copy; 2.5.11 was not measured.
- The async rows time handlers that complete synchronously. A pass that awaits a pending handler, and
  the continuation that finishes it, were not timed.

## The 0.7.0 baseline

The released 0.7.0 runtime was measured first against the .NET build with the same harness (harness
commit `441c441`, three processes per backend). On IL2CPP it was faster in seven rows, from
`Publish8` (0.424) to `SubscribeDispose64` (0.865, worst process 0.964), and slower in `AsyncPublish1`
(1.138) and `AsyncPublish8` (1.301), so the gate failed:

| Scenario | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe median (worst) | Class |
| --- | ---: | ---: | ---: | --- |
| PublishNoSubscribers | 3.98 | 6.06 | 0.656 (0.662) | faster |
| Publish1 | 5.59 | 10.37 | 0.539 (0.573) | faster |
| Publish8 | 16.73 | 39.93 | 0.424 (0.436) | faster |
| SubscribeDispose | 314.05 | 416.02 | 0.748 (0.839) | faster |
| SubscribeDispose64 | 356.03 | 411.58 | 0.865 (0.964) | faster |
| KeyedPublish | 24.11 | 54.56 | 0.440 (0.442) | faster |
| KeyedSubscribeDispose | 336.88 | 483.35 | 0.697 (0.710) | faster |
| AsyncPublish1 | 85.37 | 74.71 | 1.138 (1.151) | slower |
| AsyncPublish8 | 210.44 | 163.26 | 1.301 (1.305) | slower |

On Mono the 0.7.0 runtime was faster in five rows, on par in `SubscribeDispose` (0.959) and slower in
`SubscribeDispose64` (1.169), `AsyncPublish1` (1.151) and `AsyncPublish8` (1.248). Its subscribe and
dispose rows ran 426 to 430 gen-0 collections per row against MessagePipe's 135 to 179. The full
baseline tables are in the evidence record; the redesign above is what changed between the baseline and
the 0.8.0 results.
