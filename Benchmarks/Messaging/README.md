# Messaging comparison benchmark (Onity.Messaging vs MessagePipe)

This benchmark measures Onity.Messaging against MessagePipe 1.8.1 in Windows x64 Release Players. IL2CPP
is gated and Mono is reported the same way. Both libraries run the same workloads with the same message
sequence, and every run checks that they produced the same results.

The gate is fixed in advance: Onity must be `faster` in every IL2CPP row (see
[Pre-registered classification and gate](#pre-registered-classification-and-gate)). The results are
reported whatever they are.

MessagePipe comes in two flavors, and the harness measures either one:

| Flavor | What it is | Async API | Gate |
| --- | --- | --- | --- |
| `nuget-netstandard2.0` | The NuGet build: the precompiled `lib/netstandard2.0/MessagePipe.dll` | `ValueTask` | A |
| `unity-package` | The Unity package: the sources of `MessagePipe.1.8.1.unitypackage`, compiled by Unity against UniTask | `UniTask` | B |

The Unity package is what Unity users install, so the public claim must hold against it (gate B). The host
define `ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK` selects the Unity package flavor; without it the
harness compiles against the NuGet build. Only the async rows differ in code between the flavors.

The code is gated behind `ONITY_MESSAGING_BENCHMARKS`, and its assembly compiles against MessagePipe. A
normal project that uses the package never compiles it. Only the comparison host defines the symbol and
holds the third-party code.

## Contents

| Path | Purpose |
| --- | --- |
| `Runtime/Onity.MessagingBenchmarks.asmdef` | Runtime assembly. References `Onity.Core`, `Onity.Messaging`, and by name `MessagePipe` and `UniTask`, the Unity package's assemblies. The NuGet flavor has neither assembly definition, so Unity skips those two names and MessagePipe comes from the auto-referenced `MessagePipe.dll` (`overrideReferences: false`). Carries `[assembly: AlwaysLinkAssembly]`. |
| `Runtime/OnityMessagingBenchmarkOnityWorkloads.cs`, `...MessagePipeWorkloads.cs` | One file per library. Each imports only its own namespace, because both libraries define `IPublisher<T>` and `ISubscriber<T>`. |
| `Runtime/OnityMessagingBenchmarkWorkload.cs` | The shared message type, the workload base and the sink whose methods are every handler's body. |
| `Runtime/OnityMessagingBenchmarkScenarios.cs` | Scenario table and the golden model, which computes the expected totals without any messaging library. |
| `Runtime/OnityMessagingBenchmarkRunner.cs` | Warmups, rotation, timing, the allocation pass, statistics and ratios. |
| `Runtime/OnityMessagingBenchmarkPlayerRunner.cs` | Player entry point, command-line arguments, the compiled MessagePipe flavor, and the recorded MessagePipe source, setup and async-API notes of that flavor. |
| `Editor/OnityMessagingBenchmarkPlayerBuildRunner.cs` | Release Player build entry, the MessagePipe provenance check and the `.build.json` sidecar. |
| `tools/benchmark-host/run-messaging-comparison.ps1` (repository) | Runs the Player processes and keeps the evidence. |
| `tools/benchmark-host/messaging-summary.py` (repository) | Validates the reports, writes the summary and evaluates the gate. |

## Host setup

The comparison host is the scratch Unity 2022.3.62f2 project of the reactive comparison, a local project
outside this repository. Its `HOST-SETUP.md` records every copied source, version, license and hash. For
this benchmark it adds the following:

- The package, staged with `tools/benchmark-host/stage-host.ps1 -Source <worktree> -HostProject <host>
  -ManifestPath <file>`.
- One MessagePipe flavor at a time in `Assets/Plugins/MessagePipe/`. Both flavors build an assembly named
  `MessagePipe`, so they cannot coexist; the host keeps the other one in a backup folder outside `Assets/`.
- Unity package flavor (gate B):
  - `MessagePipe.1.8.1.unitypackage` from the official Cysharp GitHub release 1.8.1 (41,788 bytes, SHA-256
    `36ff7aa0611272e22c0c596616f2b01dfb42507a206194d9da2a11d8976544b0`). Its 54 entries (`package.json`,
    `Runtime/**` with `MessagePipe.asmdef`, and the Editor diagnostics window in `Editor/**`) are extracted
    byte-identical with the package's GUID metas. The package has no samples or tests.
  - UniTask as the embedded package `Packages/com.cysharp.unitask/` (MessagePipe's assembly definition
    references `UniTask` and `UniTask.Linq`). On the reference host this is UniTask 2.5.10.
  - The Standalone define `ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK`.
- NuGet flavor (gate A), taken from the local NuGet cache, never downloaded:
  - `MessagePipe.dll` from the official package `messagepipe.1.8.1.nupkg` (repository commit
    `e901746b927bcd84ad94d642512a7a7a7600d15b`, MIT). The package has no netstandard2.1 build, so this is
    `lib/netstandard2.0/MessagePipe.dll`. The package ships no license file; `MessagePipe.LICENSE.txt` holds
    the MIT text with the nuspec copyright.
  - `Microsoft.Extensions.DependencyInjection.Abstractions.dll` 6.0.0 (`lib/netstandard2.1`), which
    `MessagePipe.dll` references, with its license.
  - The other references are reused, never copied twice: `Microsoft.Bcl.AsyncInterfaces` 9.0.9 and
    `System.Threading.Channels` 8.0.0 from the host's R3 folder. Unity rejects duplicate assembly names.
  - `MessagePipe.dll` takes `ValueTask` from `System.Threading.Tasks.Extensions` 4.2.0.1. That name resolves
    to Unity's own type-forwarding facade, which forwards to the one `ValueTask` of the runtime: the
    .NET Standard 2.1 compat shim when compiling (the benchmark assembly then references only
    `[netstandard]System.Threading.Tasks.ValueTask`), the `unityjit` profile facade in the Mono Player,
    and the IL2CPP linker resolves the forwarders at build time. No second `ValueTask` definition is
    shipped, and both async rows pass their goldens on both backends.
- Standalone scripting defines `ONITY_BENCHMARKS;ONITY_REACTIVE_BENCHMARKS;ONITY_MESSAGING_BENCHMARKS`, plus
  `ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK` for the Unity package flavor. These are host only.

To switch flavors, close Unity on the host, move `Assets/Plugins/MessagePipe/` and its folder meta out to
the other flavor's backup, move the other flavor in, and add or remove
`ONITY_MESSAGING_BENCHMARKS_MESSAGEPIPE_UNITASK` in the Standalone define line. The Unity package flavor
also needs UniTask in `Packages/`; the NuGet flavor ignores it. The host's `HOST-SETUP.md` records the
exact folders.

MessagePipe's synchronous code is the same source in both flavors. The Unity package replaces `ValueTask`
with UniTask in the async API: `AsyncMessageBrokerCore<T>.PublishAsync` is an `async UniTask` method there,
and UniTask's builder calls `MoveNext` directly, with no ExecutionContext scope. The async rows therefore
measure the flavor's own async path. The report and the build sidecar record the flavor and this note.

## How the MessagePipe brokers are created

No DI framework is used. The Unity package has the same public constructors as the NuGet build, so both
flavors construct the public broker types directly, the same way (the netstandard2.0 `MessagePipe.dll` has
no `BuiltinContainerBuilder`, and the Unity package's is not needed):

- Once per workload, like the singletons MessagePipe's `AddMessagePipe` registers: `new MessagePipeOptions()`
  with its defaults (no global filters, `EnableCaptureStackTrace` false, `HandlingSubscribeDisposedPolicy`
  `Ignore`), `new MessagePipeDiagnosticsInfo(options)`, and
  `new FilterAttachedMessageHandlerFactory(options, new AttributeFilterProvider<MessageHandlerFilterAttribute>(), provider)`
  plus the matching `FilterAttachedAsyncMessageHandlerFactory`. `provider` is an `IServiceProvider` that
  throws if asked; with no filter registered, MessagePipe never asks.
- Per sample: a new `MessageBrokerCore<T>`, `MessageBrokerCore<TKey, T>` or `AsyncMessageBrokerCore<T>`, with
  one `MessageBroker` / `AsyncMessageBroker` wrapper for the publisher interface and one for the subscriber
  interface. These are the types `AddMessagePipe` registers for `IPublisher<T>`, `ISubscriber<T>`,
  `IPublisher<TKey, T>`, `ISubscriber<TKey, T>`, `IAsyncPublisher<T>` and `IAsyncSubscriber<T>`.
- Handlers use the `Subscribe(Action<T>)` extension and the async `Subscribe` extension of the flavor,
  `Subscribe(Func<T, CancellationToken, ValueTask>)` (NuGet) or `Subscribe(Func<T, CancellationToken, UniTask>)`
  (Unity package), with no filters.
- Async publishes pass `AsyncPublishStrategy.Sequential` explicitly. MessagePipe's default is `Parallel`;
  Onity's async channel is sequential.

The report and the build sidecar record this setup word for word.

## Build

Run one Editor at a time, in batch mode, on the host only:

```text
Unity.exe -batchmode -nographics -quit -projectPath <host> -logFile <log>
  -executeMethod Onity.Editor.Benchmarks.OnityMessagingBenchmarkPlayerBuildRunner.BuildPlayerFromCommandLine
  -onityMessagingBenchmarkBackend IL2CPP|Mono
  -onityMessagingBenchmarkBuildPath <dir>/<IL2CPP|Mono>/OnityMessagingBenchmark.exe
  [-onityMessagingBenchmarkSourceHead <git sha>]
  [-onityMessagingBenchmarkMessagePipePackage <MessagePipe.1.8.1.unitypackage>]   (Unity package flavor: required)
```

The build is Windows x64 and Release (`BuildOptions.None`, no Development flag). It uses a temporary empty
scene (`Assets/OnityBenchmarkTemp/OnityMessagingBenchmarkPlayer.unity`), which is deleted afterwards. The
scripting backend and the active build target are restored. The build refuses to start when the
Standalone defines lack `ONITY_MESSAGING_BENCHMARKS`, and it checks MessagePipe's provenance first:

- NuGet flavor: the project must hold exactly one `MessagePipe.dll`.
- Unity package flavor: the build reads the `.unitypackage` named by
  `-onityMessagingBenchmarkMessagePipePackage` (a gzip tar; only plain files and folders are accepted) and
  refuses unless every package entry is in the project byte for byte with a meta of the same GUID, the
  package's folder holds no other file (folder metas, which the package lacks, aside), the assembly
  definition that compiles `MessagePipe` is one of the entries, no `MessagePipe.dll` exists, and the
  `UniTask` assembly comes from the registered `com.cysharp.unitask` package.

Next to the exe it writes `<exe>.build.json`. The sidecar records the backend, Unity version, build GUID,
time, build options and Development flag, Standalone defines, IL2CPP configuration, stripping level, Onity
package id and version, the MessagePipe source, setup and async-API notes, the source HEAD, and the
MessagePipe provenance:

| Field | NuGet flavor | Unity package flavor |
| --- | --- | --- |
| `messagePipeFlavor` | `nuget-netstandard2.0` | `unity-package` |
| `messagePipeVersion`, `messagePipeAssemblyVersion` | the dll's informational and assembly versions | the package's `package.json` version; the compiled assembly's version |
| `messagePipeAssemblyPath` | the dll | the package's `MessagePipe.asmdef` |
| `messagePipePackagePath`, `messagePipePackageSha256` | the dll and its SHA-256 (also in `messagePipeDllPath`, `messagePipeDllSha256`) | the `.unitypackage` and its SHA-256 |
| `messagePipePackageEntries` | 0 | the entries verified in the project |
| `uniTaskVersion`, `uniTaskPackageId`, `uniTaskTreeSha256` | not used | the UniTask package version and id, and SHA-256 over the sorted `relative/path<TAB>sha256<LF>` lines of its non-meta files |

## Run

Player arguments:

| Argument | Meaning |
| --- | --- |
| `-onityRunMessagingBenchmark` | Run the benchmark, then quit. |
| `-onityMessagingBenchmarkOutput <json>` | Report path. The default is the Player's persistent data folder. |
| `-onityMessagingBenchmarkSelfTest` | Tiny counts (default ops / 1000, 3 samples). The report is marked `selfTest`. Never evidence. |
| `-onityMessagingBenchmarkSamples <n>` | Measured samples per library and scenario (default 15). |
| `-onityMessagingBenchmarkScenario <id>` | Run one scenario only. |
| `-onityMessagingBenchmarkBuildMetadata <path>` | Build sidecar to embed. The default is `<exe>.build.json`. |

The Player writes the report and exits with 0. It exits with 1 on any failure, such as a golden mismatch,
an async publish that did not complete synchronously, an exception or an invalid argument. It writes the
report in every case where the output path is known.

Measurement, with 3 processes per backend:

```text
pwsh -File tools/benchmark-host/run-messaging-comparison.ps1 -HostProject <host>
  -PlayersFrom <dir with IL2CPP/ and Mono/> -EvidenceRoot <host>/BenchmarkResults/messaging-<label>
python tools/benchmark-host/messaging-summary.py --evidence-root <host>/BenchmarkResults/messaging-<label>
```

The runner runs one Player process at a time and alternates the backend order per round. Each process
runs at `-Priority High` with an affinity mask that excludes cores 0 and 1, under a timeout. It refuses
to start a process while a Unity Editor runs on the host, the host's `Temp/UnityLockfile` is held, or any
benchmark Player (messaging, reactive, task or DI) is running. It also refuses Players whose sidecars do
not record the MessagePipe flavor, package hash and UniTask version, or whose two backends differ in the
flavor or package hash.

For each process it keeps `runs/<stem>.json`, `runs/<stem>.player.log` and
`runs/<stem>.observation.json`. The observation file holds the exit code, wall time, applied priority and
affinity, binary hashes, and the CPU used by other Unity Editors during the process. The runner also
writes `run-manifest.json`. It refuses a non-empty evidence root.

For a self-test, add `-SelfTest` to the runner and `--allow-self-test` to the summary.

## Scenarios

The message is one shared `readonly struct` with one `int Value`; keys are `int`. Each library is driven
only through its public interfaces, as a user receives them:

- Onity: `IPublisher<T>` / `ISubscriber<T>` from a `MessageBroker`; `IKeyedPublisher<TKey, T>` /
  `IKeyedSubscriber<TKey, T>` over a `KeyedMessageChannel<TKey, T>`; `IAsyncPublisher<T>` /
  `IAsyncSubscriber<T>` over an `AsyncMessageChannel<T>`.
- MessagePipe: `IPublisher<T>`, `ISubscriber<T>`, `IPublisher<TKey, T>`, `ISubscriber<TKey, T>`,
  `IAsyncPublisher<T>`, `IAsyncSubscriber<T>`.

Handlers are cached delegates created once per workload, outside timing, through each library's
documented delegate overload: Onity's `MessageHandler<T>` and `Func<T, CancellationToken, ValueTask>`;
MessagePipe's `Subscribe(Action<T>)` extension and its async `Subscribe` extension, which takes
`Func<T, CancellationToken, ValueTask>` (NuGet flavor) or `Func<T, CancellationToken, UniTask>` (Unity
package flavor). Library-side wrappers and allocations count; user delegate creation does not. Every
handler body is the same: add `message.Value` to a `long` sum and increment a count. Async handlers return
an already completed task of their library's type: `default(ValueTask)`, or `default(UniTask)` for the
Unity package.

| Id | Untimed setup | Timed operation (per op) | Ops/sample |
| --- | --- | --- | ---: |
| `PublishNoSubscribers` | channel or broker that never had a subscriber | `Publish(new Msg(i))` | 200,000 |
| `Publish1` | 1 subscriber | `Publish(new Msg(i))` | 200,000 |
| `Publish8` | 8 subscribers | `Publish(new Msg(i))` | 50,000 |
| `SubscribeDispose` | 1 resident subscriber | `Subscribe(cached)`, then `Dispose()` of that subscription | 20,000 |
| `SubscribeDispose64` | 64 resident subscribers | `Subscribe(cached)`, then `Dispose()` of that subscription | 20,000 |
| `KeyedPublish` | keys 0..15, 1 subscriber each | `Publish(i & 15, new Msg(i))` | 200,000 |
| `KeyedSubscribeDispose` | keys 0..15, 1 resident subscriber each | `Subscribe(i & 15, cached)`, then `Dispose()` | 20,000 |
| `AsyncPublish1` | 1 async handler | `PublishAsync(new Msg(i), CancellationToken.None)`, consumed synchronously | 100,000 |
| `AsyncPublish8` | 8 async handlers | same | 25,000 |

- An async publish whose task is not already completed successfully fails the run. The timed loop checks
  `ValueTask.IsCompletedSuccessfully` (for the Unity package, `UniTask.Status == UniTaskStatus.Succeeded`) and
  consumes the task with `GetAwaiter().GetResult()`; it never awaits.
- After the timed loop, outside timing, the runner publishes one probe message (`Value = -2`; keyed rows
  publish it to key 0). The probe proves that the graph is still connected. After a subscribe/dispose
  loop it proves that every transient subscription was really removed: only residents receive it.
- A library-independent model (`OnityMessagingBenchmarkGolden`) computes the expected checksum, count and
  probe totals. Every sample of every library must match them: warmups, measured samples and the
  allocation pass. Otherwise the scenario fails, the run exits with 1, and the report names the first
  mismatch.

## Measurement

- Per scenario: a full GC (`Collect`, `WaitForPendingFinalizers`, `Collect`), then 2 discarded warmup
  samples per library, then S measured samples (default 15).
- Sample k runs Onity first when k is even and MessagePipe first when k is odd, warmups included.
- Each sample creates its brokers and subscriptions untimed, times only the loop with
  `Stopwatch.GetTimestamp`, and checks and disposes untimed. ns/op = ticks x 1e9 / `Stopwatch.Frequency` /
  ops.
- The report lists per library all samples (ns/op and raw ticks), each sample's order position, gen-0
  collections during the measured samples, the median, mean, min and max, the checksums, and the
  per-scenario median ratio Onity/MessagePipe.
- Allocation is a separate untimed pass per library and scenario. It reads
  `GC.GetAllocatedBytesForCurrentThread()` around the timed loop and reports bytes/op. The counter is used
  only when a 64 KiB array reads at least 64 KiB (positive control) and two consecutive reads differ by 0
  (empty control). Otherwise allocation is reported unavailable for that backend. It is never called on
  IL2CPP, because the DI harness observed IL2CPP crashes with it. Allocation is reported, not gated.
- In the 2026-10-02 self-test on the reference host, the Mono Release Player's positive control read 0
  bytes for the 64 KiB array, as in the reactive harness. As it stands, this harness reports allocation
  as unavailable on both backends.
- The report also records the Unity version, backend, Development flag, OS, CPU, processor count, UTC
  time, command line, self-test flag, GC mode, the embedded build sidecar and the MessagePipe setup method.

## Fairness notes

- The user code is the same shape for both libraries: the same loops, the same message, the same sink
  method bodies, one cached delegate per handler kind and workload. The two workload files differ in their
  `using` line, the delegate types and the broker construction. MessagePipe's two flavors differ only in
  the async workload: the handler's task type and how the returned task is checked.
- Each library wraps the handler its own way, and that is measured:
  - Onity stores the `MessageHandler<T>` in its channel's slot array and invokes it directly. Since
    `4cc5763` a subscription allocates one object, the subscription itself; the 0.7.0 runtime measured as
    the baseline allocated an unsubscribe closure, its delegate and a `DisposableAction`.
  - MessagePipe wraps the `Action<T>` in an `AnonymousMessageHandler<T>` and calls it through
    `IMessageHandler<T>.Handle`. A subscription allocates that wrapper and a `Subscription`, and runs the
    handler factory (global and attribute filter lookups).
- MessagePipe's brokers take locks where Onity's channels do not: on subscribe and dispose, and on every
  keyed publish. Onity's channel wraps each publish pass in a `try`/`finally` that tracks the publish
  depth, so a subscription removed during delivery is compacted afterwards. Neither is disabled.
- Library defaults are left alone, except the explicit `AsyncPublishStrategy.Sequential` (above).
- The whole measurement runs synchronously inside one `AfterSceneLoad` callback, so no frame or
  PlayerLoop work interleaves with it. The Mono Player has no stripping and loads every host assembly,
  including Onity.Unity's PlayerLoop hooks and the other benchmark assemblies, whose entry points return
  at once without their own arguments. The IL2CPP linker removes unreferenced assemblies, so
  `Onity.MessagingBenchmarks` carries `[assembly: AlwaysLinkAssembly]`.
- In the Unity package flavor both Players also contain UniTask. Its `PlayerLoopHelper.Init` runs at
  `AfterAssembliesLoaded`, before the benchmark, and inserts UniTask's PlayerLoop runners. No frame runs
  during the measurement, so they never execute inside it, and both libraries share the same process.
- Samples are short (milliseconds at the default counts), so they are sensitive to scheduling noise. Use
  the raw samples and the per-process ratios, not one number, when a difference is small.

## Pre-registered classification and gate

This rule was fixed before any measurement. A process ratio is the Onity process median ns/op divided by
the MessagePipe process median ns/op for one scenario, so a ratio below 1 means Onity was faster. Across
the processes of one backend:

- `faster` when the median ratio <= 0.95 and the worst (largest) process ratio <= 1.00;
- `slower` when the median ratio >= 1.05;
- `on par` otherwise.

Gate: IL2CPP, 3 processes, every row `faster` (9 rows, or 7 if the async rows had to be dropped). Mono is
classified the same way and reported, not gated. Workloads, counts and harness code are frozen once the
first timing run starts. Gate A applied this rule to the NuGet flavor; gate B applies the same rule,
unchanged, to the Unity package flavor.

## Reading the summary

`messaging-summary.py` validates the evidence before it summarizes. It checks the following:

- no self-test report, unless `--allow-self-test` is given;
- every golden check passed, and checksums are identical for both libraries;
- every report is from a Release, non-development Player whose build sidecar matches it, and records the
  MessagePipe setup;
- there are exactly N processes per backend (`--processes`, default 3) and one build per backend;
- all reports have the same scenarios, operation counts and sample counts;
- all reports share one MessagePipe flavor, and each Player was compiled for the flavor its sidecar
  records; a Unity package build recorded its verified package entries and a UniTask version (reports
  from before the flavor field are the NuGet flavor);
- all reports share one source HEAD, Onity version, MessagePipe version, package hash and UniTask version,
  and the package hash is the official MessagePipe 1.8.1 artifact of the flavor: the netstandard2.0 dll
  (`cf8a702ab31bbb7da9ea6de4349f6dec9e214be396234f7e527d6ebe06a94f1b`) or `MessagePipe.1.8.1.unitypackage`
  (`36ff7aa0611272e22c0c596616f2b01dfb42507a206194d9da2a11d8976544b0`); `--messagepipe-sha256` overrides;
- the ns/op values and medians agree with the raw Stopwatch ticks.

It then writes `messaging-summary.md` and `messaging-summary.json`, and never overwrites them without
`--force`.

Per backend and scenario, the summary lists the median over processes of the process medians for each
library, the per-process ratios, the median and worst ratio, and the class. It also lists the process
medians, gen-0 collections, allocation bytes/op when the counter passed its controls, the gate verdict
for IL2CPP, and each process's observation (exit code, wall time, priority, affinity and other Unity
Editor CPU) for information.

A self-test summary carries a banner, never evaluates the gate, and is never performance evidence.
