# Onity.Messaging vs MessagePipe 1.8.1: Release Player comparison - 2026-10-02

## Result

Both pre-registered gates passed. On IL2CPP, the headline backend, the shipped
0.8.0 messaging core (runtime commit `4cc5763`, measured at source `52e209a`)
is faster than MessagePipe 1.8.1 in all nine rows against the MessagePipe Unity
package compiled with UniTask 2.5.10: the median Onity/MessagePipe time ratio
ranges from 0.150 (`SubscribeDispose64`) to 0.846 (`AsyncPublish8`) with a
worst process of 0.895. Against MessagePipe's .NET build (the NuGet
netstandard2.0 dll) the same runtime is faster in all nine IL2CPP rows,
medians 0.197 (`SubscribeDispose`) to 0.807 (`Publish1`), worst process 0.974.

Mono is reported, not gated. The same runtime is faster in all nine Mono rows
against both builds: medians 0.280 to 0.756 (worst process 0.862) against the
Unity package, and 0.280 to 0.762 (worst process 0.830) against the .NET
build.

The measurements ran in three stages. The released 0.7.0 runtime, measured
first as the baseline against the .NET build, was faster in seven IL2CPP rows
and slower in the two async rows (`AsyncPublish1` 1.138, `AsyncPublish8`
1.301), so gate A failed. Gate A's first attempt at the redesigned core
(`4cc5763`) passed in all nine rows on both backends. Gate B, pre-registered
afterwards against the Unity package that Unity users install, measured the
same runtime unchanged (`52e209a` adds only the harness's Unity-package
flavor) and passed at its first attempt in all nine rows on both backends. The
sections below record all three runs.

These results cover the nine workloads below at the stated settings. They are
timing results for one Windows PC; they are not a claim about allocation,
other platforms or other workloads.

## What was measured

- **Source:** `52e209a5fbe757ef3b8702b4c1724feae3e391bd` for gate B: the
  runtime commit `4cc5763dcd8512cf4336aa6e31c8baf0c4fbd40d` plus the harness's
  Unity-package flavor (no file under `Runtime/` differs between the two).
  Gate A, attempt 1, measured `4cc5763` itself, and the baseline measured
  `441c44129a2cc4becc16c2849b2b8cb5fa0f4e82` (the released 0.7.0 runtime plus
  the define-gated harness). The build sidecars and summaries of every run
  record package version `0.7.0`, because the version was raised to 0.8.0
  after the measurements; the source hash names the measured code.
- **Libraries:** MessagePipe 1.8.1 in two builds. The Unity package:
  `MessagePipe.1.8.1.unitypackage` from the official Cysharp GitHub release
  (41,788 bytes, SHA-256
  `36ff7aa0611272e22c0c596616f2b01dfb42507a206194d9da2a11d8976544b0`), its 54
  entries extracted byte-identical with the package's GUID metas and compiled
  by Unity against UniTask 2.5.10 (tree SHA-256
  `0fa45e04f49edd24b0e1981fdb8b894cd4fd660a0e2cc47c9cdb865f05d820bd`; 2.5.10
  was the local copy, 2.5.11 was not measured). The .NET build: the NuGet
  package `messagepipe.1.8.1.nupkg` (repository commit
  `e901746b927bcd84ad94d642512a7a7a7600d15b`), `lib/netstandard2.0/MessagePipe.dll`,
  SHA-256 `cf8a702ab31bbb7da9ea6de4349f6dec9e214be396234f7e527d6ebe06a94f1b`,
  with `Microsoft.Extensions.DependencyInjection.Abstractions` 6.0.0 and the
  host's shared `Microsoft.Bcl.AsyncInterfaces` 9.0.9 and
  `System.Threading.Channels` 8.0.0. The two builds share the synchronous
  source; the Unity package's async API is UniTask (`async UniTask` publish),
  the .NET build's is `ValueTask` (`async ValueTask` publish).
- **Host:** the comparison host of the reactive comparison, a copy of the
  public project with the comparison libraries, the Standalone defines
  `ONITY_BENCHMARKS;ONITY_REACTIVE_BENCHMARKS;ONITY_MESSAGING_BENCHMARKS` and,
  for gate B, `ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK`, with one
  MessagePipe build at a time in `Assets/Plugins/MessagePipe/`. The host's
  `HOST-SETUP.md` records every copied source, version, license and hash.
- **Harness:** `Packages/com.onity.framework/Benchmarks/Messaging/**`,
  `tools/benchmark-host/run-messaging-comparison.ps1` and
  `tools/benchmark-host/messaging-summary.py`, frozen at `441c441` for the
  baseline and gate A. Before any gate-B run, `52e209a` added the
  Unity-package flavor: the async handler's task type (`UniTask`) and the
  check that it completed, a build-time provenance check against the
  `.unitypackage`, and the flavor, package hash and UniTask version in the
  reports and the summary validation. The synchronous rows, Onity's side, the
  workloads, counts and rule are the same in both. The README holds the
  scenario table, the measurement procedure and the pre-registered rule.
- **Players:** Unity 2022.3.62f2, Windows x64, non-development Release
  Players; AMD Ryzen 9 5900X. Build GUIDs: gate B IL2CPP
  `da5c33248906489dae7aed44387f988a`, Mono `4c7b337f8fb04fb3aa4c3b72f8b2f081`;
  gate A, attempt 1: IL2CPP `0cb887bb53e644c1b40849bfbcfe9cfc`, Mono
  `0524d27810d248e289a02c5b18b279ac`; baseline: IL2CPP
  `372b4fe8484c4f8b95c2ceb95a01dde8`, Mono `a26d274cbbf143c4b7ce628f032dae6d`.
- **Workloads:** the message is one shared `readonly struct` with one `int`;
  keys are `int`. Each library is driven only through its public interfaces:
  Onity through `IPublisher<T>` / `ISubscriber<T>` from a `MessageBroker`,
  `IKeyedPublisher<TKey, T>` / `IKeyedSubscriber<TKey, T>` over a
  `KeyedMessageChannel<TKey, T>` and `IAsyncPublisher<T>` /
  `IAsyncSubscriber<T>` over an `AsyncMessageChannel<T>`; MessagePipe through
  `IPublisher<T>`, `ISubscriber<T>`, `IPublisher<TKey, T>`,
  `ISubscriber<TKey, T>`, `IAsyncPublisher<T>` and `IAsyncSubscriber<T>`, its
  brokers constructed from the public types with a default
  `MessagePipeOptions` and no filters, without a DI container, and its async
  publishes passing `AsyncPublishStrategy.Sequential` because Onity's channel
  is sequential and MessagePipe defaults to `Parallel`. Handlers are cached
  delegates created outside timing through each library's documented delegate
  overload; async handlers return an already completed task of their library's
  type, and the timed loop consumes it without awaiting (a publish whose task
  is not already completed successfully fails the run). The nine scenarios,
  their untimed setup and their operation counts per sample are in the harness
  README; the ids below name them.
- **Processes and samples:** three Player processes per backend, 2 discarded
  warmup samples and 15 measured samples per library and scenario, library
  order alternating per sample. Each process runs alone at High priority with
  an affinity mask that excludes cores 0 and 1.
- **Statistic and rule (pre-registered before any measurement):** a process
  ratio is Onity's process median ns/op divided by MessagePipe's process
  median ns/op, so below 1 means Onity took less time. Across the processes of
  one backend a row is `faster` when the median ratio is at most 0.95 and the
  worst (largest) process ratio is at most 1.00, `slower` when the median
  ratio is at least 1.05, and `on par` otherwise. IL2CPP is the headline; Mono
  is classified the same way and reported alongside. Gate A passes when every
  IL2CPP row is `faster` against the .NET build; gate B, pre-registered after
  gate A had passed and limited to two attempts, applies the same rule to the
  Unity package. Mono is report-only in both.
- **Correctness:** a library-independent model computes the expected checksum,
  count and probe totals for every scenario, and an untimed probe after each
  timed loop proves that the graph is still connected and that every transient
  subscription was removed. Every sample of every library matched in every
  process, and the two libraries' checksums were identical.
- **Allocation:** not measured on either backend. The counter is never called
  on IL2CPP (the DI harness observed IL2CPP crashes with it), and on Mono the
  positive control read 0 bytes for a 64 KiB array, so the harness reports
  allocation as unavailable. Gen-0 collection counts are reported instead.

## Gate B, attempt 1: the Unity package (source 52e209a, IL2CPP, 2026-10-02T19:45Z)

Values are the median over three processes of each library's per-process
median, in ns per operation. Class is the pre-registered classification.

| Scenario | Ops per sample | Onity ns | MessagePipe ns | Onity/MessagePipe per process | Median (worst) | Class |
| --- | ---: | ---: | ---: | --- | ---: | --- |
| PublishNoSubscribers | 200,000 | 2.25 | 5.32 | 0.423, 0.376, 0.416 | 0.416 (0.423) | faster |
| Publish1 | 200,000 | 6.21 | 9.16 | 0.767, 0.651, 0.726 | 0.726 (0.767) | faster |
| Publish8 | 50,000 | 20.97 | 39.40 | 0.532, 0.483, 0.526 | 0.526 (0.532) | faster |
| SubscribeDispose | 20,000 | 63.49 | 377.52 | 0.193, 0.164, 0.168 | 0.168 (0.193) | faster |
| SubscribeDispose64 | 20,000 | 60.13 | 417.30 | 0.216, 0.129, 0.150 | 0.150 (0.216) | faster |
| KeyedPublish | 200,000 | 28.35 | 67.83 | 0.418, 0.346, 0.436 | 0.418 (0.436) | faster |
| KeyedSubscribeDispose | 20,000 | 104.37 | 552.26 | 0.189, 0.172, 0.164 | 0.172 (0.189) | faster |
| AsyncPublish1 | 100,000 | 27.32 | 43.86 | 0.623, 0.740, 0.621 | 0.623 (0.740) | faster |
| AsyncPublish8 | 25,000 | 103.19 | 126.20 | 0.818, 0.846, 0.895 | 0.846 (0.895) | faster |

All nine rows are `faster`; the worst process of any row is 0.895. Gate B
passed at this first attempt.

## Gate B, attempt 1, Mono (reported, not gated)

| Scenario | Ops per sample | Onity ns | MessagePipe ns | Onity/MessagePipe per process | Median (worst) | Class |
| --- | ---: | ---: | ---: | --- | ---: | --- |
| PublishNoSubscribers | 200,000 | 5.72 | 7.95 | 0.720, 0.616, 0.706 | 0.706 (0.720) | faster |
| Publish1 | 200,000 | 8.09 | 14.37 | 0.563, 0.682, 0.559 | 0.563 (0.682) | faster |
| Publish8 | 50,000 | 25.43 | 33.92 | 0.786, 0.697, 0.756 | 0.756 (0.786) | faster |
| SubscribeDispose | 20,000 | 80.04 | 294.98 | 0.265, 0.334, 0.297 | 0.297 (0.334) | faster |
| SubscribeDispose64 | 20,000 | 82.89 | 296.37 | 0.287, 0.280, 0.253 | 0.280 (0.287) | faster |
| KeyedPublish | 200,000 | 26.04 | 44.47 | 0.586, 0.539, 0.529 | 0.539 (0.586) | faster |
| KeyedSubscribeDispose | 20,000 | 98.78 | 338.57 | 0.278, 0.292, 0.304 | 0.292 (0.304) | faster |
| AsyncPublish1 | 100,000 | 59.80 | 117.66 | 0.508, 0.514, 0.548 | 0.514 (0.548) | faster |
| AsyncPublish8 | 25,000 | 255.98 | 366.75 | 0.665, 0.662, 0.862 | 0.665 (0.862) | faster |

All nine Mono rows are `faster`; the worst process is 0.862 (`AsyncPublish8`).

## Gate A, attempt 1: the .NET build (source 4cc5763, IL2CPP, 2026-10-02T18:47Z)

| Scenario | Ops per sample | Onity ns | MessagePipe ns | Onity/MessagePipe per process | Median (worst) | Class |
| --- | ---: | ---: | ---: | --- | ---: | --- |
| PublishNoSubscribers | 200,000 | 2.80 | 7.16 | 0.391, 0.459, 0.493 | 0.459 (0.493) | faster |
| Publish1 | 200,000 | 9.22 | 9.47 | 0.974, 0.660, 0.807 | 0.807 (0.974) | faster |
| Publish8 | 50,000 | 17.98 | 35.14 | 0.513, 0.554, 0.512 | 0.513 (0.554) | faster |
| SubscribeDispose | 20,000 | 99.34 | 484.50 | 0.205, 0.197, 0.137 | 0.197 (0.205) | faster |
| SubscribeDispose64 | 20,000 | 114.17 | 495.46 | 0.230, 0.218, 0.146 | 0.218 (0.230) | faster |
| KeyedPublish | 200,000 | 26.43 | 59.43 | 0.482, 0.389, 0.427 | 0.427 (0.482) | faster |
| KeyedSubscribeDispose | 20,000 | 118.67 | 529.00 | 0.224, 0.243, 0.163 | 0.224 (0.243) | faster |
| AsyncPublish1 | 100,000 | 24.95 | 78.49 | 0.273, 0.334, 0.313 | 0.313 (0.334) | faster |
| AsyncPublish8 | 25,000 | 95.31 | 167.42 | 0.569, 0.532, 0.604 | 0.569 (0.604) | faster |

All nine rows are `faster`; the worst process is 0.974 (`Publish1`, process
1, whose other two processes measured 0.660 and 0.807). Gate A passed at this
first attempt.

## Gate A, attempt 1, Mono (reported, not gated)

| Scenario | Ops per sample | Onity ns | MessagePipe ns | Onity/MessagePipe per process | Median (worst) | Class |
| --- | ---: | ---: | ---: | --- | ---: | --- |
| PublishNoSubscribers | 200,000 | 5.67 | 8.00 | 0.709, 0.697, 0.718 | 0.709 (0.718) | faster |
| Publish1 | 200,000 | 8.11 | 14.38 | 0.561, 0.570, 0.707 | 0.570 (0.707) | faster |
| Publish8 | 50,000 | 28.41 | 37.30 | 0.722, 0.830, 0.762 | 0.762 (0.830) | faster |
| SubscribeDispose | 20,000 | 102.46 | 322.32 | 0.268, 0.334, 0.319 | 0.319 (0.334) | faster |
| SubscribeDispose64 | 20,000 | 83.58 | 297.21 | 0.271, 0.280, 0.314 | 0.280 (0.314) | faster |
| KeyedPublish | 200,000 | 23.66 | 43.16 | 0.548, 0.563, 0.529 | 0.548 (0.563) | faster |
| KeyedSubscribeDispose | 20,000 | 92.61 | 374.80 | 0.240, 0.287, 0.341 | 0.287 (0.341) | faster |
| AsyncPublish1 | 100,000 | 46.78 | 162.69 | 0.286, 0.298, 0.287 | 0.287 (0.298) | faster |
| AsyncPublish8 | 25,000 | 214.63 | 426.96 | 0.522, 0.503, 0.543 | 0.522 (0.543) | faster |

All nine Mono rows are `faster`; the worst process is 0.830 (`Publish8`).

## Baseline: the released 0.7.0 runtime against the .NET build (source 441c441, 2026-10-02T17:40Z)

IL2CPP: seven rows `faster`, the two async rows `slower`; the gate failed.

| Scenario | Ops per sample | Onity ns | MessagePipe ns | Onity/MessagePipe per process | Median (worst) | Class |
| --- | ---: | ---: | ---: | --- | ---: | --- |
| PublishNoSubscribers | 200,000 | 3.98 | 6.06 | 0.656, 0.656, 0.662 | 0.656 (0.662) | faster |
| Publish1 | 200,000 | 5.59 | 10.37 | 0.573, 0.536, 0.539 | 0.539 (0.573) | faster |
| Publish8 | 50,000 | 16.73 | 39.93 | 0.424, 0.436, 0.367 | 0.424 (0.436) | faster |
| SubscribeDispose | 20,000 | 314.05 | 416.02 | 0.839, 0.748, 0.429 | 0.748 (0.839) | faster |
| SubscribeDispose64 | 20,000 | 356.03 | 411.58 | 0.964, 0.865, 0.580 | 0.865 (0.964) | faster |
| KeyedPublish | 200,000 | 24.11 | 54.56 | 0.440, 0.442, 0.439 | 0.440 (0.442) | faster |
| KeyedSubscribeDispose | 20,000 | 336.88 | 483.35 | 0.710, 0.697, 0.447 | 0.697 (0.710) | faster |
| AsyncPublish1 | 100,000 | 85.37 | 74.71 | 1.151, 1.122, 1.138 | 1.138 (1.151) | slower |
| AsyncPublish8 | 25,000 | 210.44 | 163.26 | 1.301, 1.190, 1.305 | 1.301 (1.305) | slower |

Mono: five rows `faster`, one `on par`, three `slower`.

| Scenario | Ops per sample | Onity ns | MessagePipe ns | Onity/MessagePipe per process | Median (worst) | Class |
| --- | ---: | ---: | ---: | --- | ---: | --- |
| PublishNoSubscribers | 200,000 | 7.25 | 7.82 | 0.930, 0.920, 0.921 | 0.921 (0.930) | faster |
| Publish1 | 200,000 | 8.42 | 14.16 | 0.596, 0.608, 0.595 | 0.596 (0.608) | faster |
| Publish8 | 50,000 | 27.52 | 33.94 | 0.809, 0.824, 0.811 | 0.811 (0.824) | faster |
| SubscribeDispose | 20,000 | 256.85 | 273.05 | 0.889, 0.967, 0.959 | 0.959 (0.967) | on par |
| SubscribeDispose64 | 20,000 | 307.57 | 263.01 | 1.124, 1.243, 1.169 | 1.169 (1.243) | slower |
| KeyedPublish | 200,000 | 26.36 | 41.79 | 0.626, 0.640, 0.631 | 0.631 (0.640) | faster |
| KeyedSubscribeDispose | 20,000 | 280.64 | 324.24 | 0.798, 0.877, 0.861 | 0.861 (0.877) | faster |
| AsyncPublish1 | 100,000 | 177.15 | 152.40 | 1.173, 1.134, 1.151 | 1.151 (1.173) | slower |
| AsyncPublish8 | 25,000 | 501.12 | 402.19 | 1.276, 1.248, 1.246 | 1.248 (1.276) | slower |

## Gen-0 collections

Collections during the measured samples, summed over the three processes of a
backend, Onity / MessagePipe. Every other row counted 0 for both libraries in
every run.

| Run | Backend | SubscribeDispose | SubscribeDispose64 | KeyedSubscribeDispose |
| --- | --- | ---: | ---: | ---: |
| Baseline (0.7.0 runtime) | IL2CPP | 429 / 162 | 426 / 154 | 430 / 155 |
| Baseline (0.7.0 runtime) | Mono | 430 / 144 | 426 / 135 | 426 / 179 |
| Gate A, attempt 1 (`4cc5763`) | IL2CPP | 93 / 156 | 92 / 154 | 92 / 154 |
| Gate A, attempt 1 (`4cc5763`) | Mono | 71 / 145 | 72 / 140 | 69 / 178 |
| Gate B, attempt 1 (`52e209a`) | IL2CPP | 93 / 156 | 92 / 153 | 90 / 155 |
| Gate B, attempt 1 (`52e209a`) | Mono | 69 / 141 | 66 / 141 | 71 / 174 |

The 0.7.0 runtime allocated an unsubscribe closure, its delegate and a
`DisposableAction` per subscription; the 0.8.0 runtime allocates one object,
the subscription itself. This is a collection count, not a byte measurement.

## Fairness notes

- The user code is the same shape for both libraries: the same loops, the
  same message, the same handler bodies, one cached delegate per handler kind
  and workload. The two workload files differ in their `using` line, the
  delegate types and the broker construction; the two MessagePipe builds
  differ only in the async handler's task type and how its completion is
  checked.
- Each library wraps the handler its own way, and the wrapper is part of what
  is measured. Onity stores the `MessageHandler<T>` in its channel's slot
  array and invokes it directly; since `4cc5763` a subscription allocates one
  object, the subscription itself. MessagePipe wraps the `Action<T>` in an
  `AnonymousMessageHandler<T>` and calls it through
  `IMessageHandler<T>.Handle`; a subscription allocates that wrapper and a
  `Subscription`, and runs the handler factory (global and attribute filter
  lookups, which find nothing here).
- MessagePipe's brokers take locks where Onity's channels do not: on subscribe
  and dispose, and on every keyed publish. Onity's synchronous channel wraps
  each publish pass in a `try`/`finally` that tracks the publish depth, so a
  subscription removed during delivery is compacted afterwards. Neither is
  disabled.
- The async rows measure the publish pass with handlers that complete
  synchronously; no handler is awaited. Onity's `PublishAsync` runs that pass
  inline without a state machine; MessagePipe runs its `async UniTask` (Unity
  package) or `async ValueTask` (.NET build) method. The Unity package
  measured 43.86 ns against 78.49 ns in `AsyncPublish1` and 126.20 against
  167.42 ns in `AsyncPublish8` on IL2CPP, consistent with UniTask's builder
  running the state machine without the ExecutionContext scope the
  `ValueTask` builder pays; the measurement does not isolate that cause.
- Library defaults were left alone, except the explicit
  `AsyncPublishStrategy.Sequential`.
- The whole measurement runs synchronously inside one `AfterSceneLoad`
  callback, so no frame or PlayerLoop work interleaves with it. In the gate-B
  Players both libraries share a process that also contains UniTask, whose
  PlayerLoop runners are inserted at startup and never execute inside the
  measurement. Samples are short, so the per-process ratios, not one number,
  carry a small difference.

## Noise and limits

- Other Unity Editors and batch builds of other projects ran on the host
  during the runs. The runner's noise screen (other Unity Editor CPU at most
  0.5 s and at most 5% of the process's wall time) flagged every IL2CPP
  process of all three runs and every Mono process but one (gate B, Mono
  process 1, `unknown`). In the gate-B run the other-Editor CPU during the
  IL2CPP processes was 0.80, 0.36 and 0.78 s of 2.5, 2.4 and 2.2 s wall time,
  and during Mono process 3 it was 10.11 s of 2.4 s wall time. The ratios
  come from the same process with the library order alternating per sample,
  which is why the per-process ratios are reported and the rule reads the
  worst process.
- One Windows PC, one CPU. Other machines, other platforms (ARM64, consoles,
  mobile), other Unity versions and Development Players are not covered.
- Allocation was not measured on either backend; nothing here is an
  allocation result.
- The nine workloads are publish, subscribe and dispose, keyed publish and
  subscribe and dispose, and async publish with synchronously completing
  handlers. MessagePipe's filters, request/response, buffered publishers,
  interprocess transports and `Parallel` strategy, and Onity's `Observe<T>()`
  bridge, `OnityEvent` and `OnityTask` receives, were not measured. A pass
  that awaits a pending handler was not timed.

## Files

| File | Content |
| --- | --- |
| `messaging-surpass-messagepipe-2026-10-02/unity-package-attempt-1/messaging-summary.md`, `.json` | Gate B, attempt 1: the shipped runtime against the MessagePipe Unity package (`52e209a`): per-row medians, classes and per-process ratios for both backends, process medians, gen-0 counts, build GUIDs, source hash, package and UniTask hashes, and the process observations. Host evidence root `messaging-unitypkg-attempt-1-2026-10-02`. |
| `messaging-surpass-messagepipe-2026-10-02/attempt-1-nuget/messaging-summary.md`, `.json` | Gate A, attempt 1: the same runtime (`4cc5763`) against the .NET build, same layout. Host evidence root `messaging-attempt-1-2026-10-02`. |
| `messaging-surpass-messagepipe-2026-10-02/baseline-nuget/messaging-summary.md`, `.json` | The released 0.7.0 runtime (`441c441`) against the .NET build, same layout. Host evidence root `messaging-baseline-2026-10-02`. |

The summaries were written by `messaging-summary.py` after it validated every
report (Release non-development Players, matching build sidecars, one
MessagePipe build and the official package hash of that build across all
reports, golden checks and identical checksums passed, ns/op consistent with
the raw Stopwatch ticks). The host project's local path in them is replaced by
`<host>`. The per-process Player reports, observation files and Player logs of
all three runs are not committed; they are attached to the v0.8.0 GitHub
release as `messaging-vs-messagepipe-2026-10-02-runs.zip`, with local paths
replaced by placeholders.

## Reproduce

On a Windows host set up as the harness README describes (one MessagePipe
build in `Assets/Plugins/MessagePipe/`, UniTask as an embedded package for the
Unity-package build, the `ONITY_MESSAGING_BENCHMARKS` define and, for the
Unity package, `ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK`), build one
Release Player per backend with
`Onity.Editor.Benchmarks.OnityMessagingBenchmarkPlayerBuildRunner.BuildPlayerFromCommandLine`
(with `-onityMessagingBenchmarkMessagePipePackage` for the Unity package), run
`tools/benchmark-host/run-messaging-comparison.ps1` with three processes, and
summarize with `tools/benchmark-host/messaging-summary.py`. See
`Packages/com.onity.framework/Benchmarks/Messaging/README.md`.
