# Onity.Reactive vs R3 1.3.0 and UniRx 7.1.0: Release Player comparison - 2026-10-02

## Result

Both pre-registered gates passed. On IL2CPP, the headline backend, the shipped
0.7.0 core (source `36c68c5`) is faster than R3 1.3.0 and faster than UniRx
7.1.0 in all nine rows: against R3 the median Onity/R3 time ratio ranges from
0.145 (`CombineLatestOnNext`) to 0.805 (`WhereSelectOnNext`) with a worst
process of 0.811, and against UniRx from 0.129 (`CombineLatestOnNext`) to 0.872
(`SubjectOnNext1`) with a worst process of 0.874.

Mono is reported, not gated. Against R3 on Mono, Onity is faster in seven rows,
on par in `PropertySetSame` (1.040) and slower in `CombineLatestOnNext`
(1.378). Against UniRx on Mono, Onity is faster in five rows, on par in
`PropertySetSame` (1.001) and `WhereSelectOnNext` (1.015), and slower in
`SubjectOnNext1` (1.098) and `CombineLatestOnNext` (1.527).

The measurements ran in three stages. The published 0.6.0 runtime, measured
first as the baseline, gave mixed results on IL2CPP and was slower in most rows
on Mono. The R3 gate (every IL2CPP row faster than R3) failed at the first
attempt at the redesigned core (`b73c20e`) on one row and passed at the second
(`9563453`), which left single-subscriber publish on par with UniRx. The UniRx
follow-up gate (every IL2CPP row faster than both libraries, at most two
attempts) passed at its first attempt, `36c68c5`, which delivers to a lone
subscriber without the general loop. 0.7.0 also carries a small OnityTask fix
in the worker-thread registration path of stateless frame waits, which the
reactive benchmark never exercises, so these numbers stand for the shipped
code. The sections below record all five runs.

These results cover the nine workloads below at the stated settings. They are
timing results for one Windows PC; they are not a claim about allocation,
other platforms or other workloads.

## What was measured

- **Source:** `36c68c580b7ce622a0132cf952336b6aa93c64c7` on
  `perf/reactive-surpass-r3`, the UniRx follow-up attempt and the shipped 0.7.0
  reactive core. The build sidecars of every run record package version
  `0.6.0`, because the version was raised to 0.7.0 after the measurements; the
  source hash names the measured code. The R3 gate measured
  `b73c20ecce1aa7290e35a32a6d7de9cce2e0228b` (attempt 1, failed) and
  `9563453f5f46bd79dad9f7d4204a9d4f6f6be7eb` (attempt 2, passed), and the
  baseline runs measured `4e92db959023180fa6530e5d0867153b9ff6047e` (main
  `6d111dd` plus the define-gated benchmark folders, so the published 0.6.0
  runtime).
- **Libraries:** R3 1.3.0 (the NuGet `netstandard2.1` DLL with
  `Microsoft.Bcl.TimeProvider` 8.0.0, `Microsoft.Bcl.AsyncInterfaces` 9.0.9,
  `System.ComponentModel.Annotations` 5.0.0, `System.Threading.Channels` 8.0.0
  and `System.Runtime.CompilerServices.Unsafe` 6.1.2; R3 1.3.1 has no runtime
  change from 1.3.0, only CI, packaging and a Unity 6.2 editor fix) and UniRx
  7.1.0 (the official release unitypackage, SHA-256
  `2e6cae5ba4a3ad68791fdc763027c524258244607a45e31096af59d1f568c073`; 193 files
  imported unmodified; the 16 Examples entries and the 35 bridge files that need
  `UnityEngine.UI` or EventSystems, or depend on those, were not imported, and
  none of the benchmarked UniRx code is among them). R3 runs without `R3.Unity`.
- **Host:** a copy of the public project with the two libraries, the host-only
  Standalone defines `ONITY_BENCHMARKS;ONITY_REACTIVE_BENCHMARKS`, and the
  built-in module `com.unity.modules.unitywebrequestwww`, which UniRx's
  `MainThreadDispatcher` needs for `WWW` in its Editor path. The host's
  `HOST-SETUP.md` records every copied source, version, license and hash.
- **Harness:** `Packages/com.onity.framework/Benchmarks/Reactive/**`,
  `tools/benchmark-host/run-reactive-comparison.ps1` and
  `tools/benchmark-host/reactive-summary.py`, frozen at `4e92db9` for every run
  (the harness README is the only file that changed). The README holds the
  scenario table, the measurement procedure and the pre-registered rule.
- **Players:** Unity 2022.3.62f2, Windows x64, non-development Release Players;
  IL2CPP with managed stripping Minimal; AMD Ryzen 9 5900X. Follow-up gate
  build GUIDs: IL2CPP `ac2bdd1c782842fc8d27e90da330fd26`, Mono
  `a75b0ac2272f4f6584fc5535fefb8a38`. R3 gate, attempt 2: IL2CPP
  `76bffacfc72746dbad4a05b3890997b5`, Mono `57eb3ef763c74b598735d77694b0093b`.
  R3 gate, attempt 1: IL2CPP `d3ced29dbec449d98d1a120372dd55d8`, Mono
  `d0c5043e8e6242bd818bd80d01d06d36`. Baseline: IL2CPP
  `9ebeccd9b06a406eb4a00c27287f2146`, Mono `b2689d46c2bf4c8ebeacf8db9cb58bc9`.
- **Workloads:** all payloads are `int`; every library subscribes through its
  `Subscribe(Action<int>)` with one cached action per workload. The nine
  scenarios, their untimed setup and their operation counts per sample are in
  the harness README; the ids below name them.
- **Processes and samples:** three Player processes per backend (five per
  backend in the baseline confirmation run), 2 discarded warmup samples and 15
  measured samples per library and scenario, library order rotated per sample.
  Each process runs alone at High priority with an affinity mask that excludes
  cores 0 and 1.
- **Statistic and rule (pre-registered before any measurement):** a process
  ratio is Onity's process median ns/op divided by the other library's process
  median ns/op, so below 1 means Onity took less time. Across the processes of
  one backend a row is `faster` when the median ratio is at most 0.95 and the
  worst (largest) process ratio is at most 1.00, `slower` when the median ratio
  is at least 1.05, and `on par` otherwise. IL2CPP is the headline; Mono is
  classified the same way and reported alongside. The R3 gate passes when every
  IL2CPP row is `faster` against R3, with UniRx ratios report-only. The UniRx
  follow-up gate, pre-registered after the R3 gate had passed and limited to
  two attempts, passes when every IL2CPP row is `faster` against R3 and against
  UniRx. Mono is report-only in both.
- **Correctness:** a library-independent model computes the expected checksum,
  notification count and probe totals for every scenario; every sample of
  every library matched them in every process.
- **Allocation:** not measured on either backend. The counter is never called on
  IL2CPP (the DI harness observed IL2CPP crashes with it), and on Mono the
  positive control read 0 bytes for a 64 KiB array, so the harness reports
  allocation as unavailable.

## UniRx follow-up, attempt 1: the shipped 0.7.0 core (source 36c68c5, IL2CPP, 2026-10-02T15:44Z)

Values are the median over three processes of each library's per-process
median, in ns per operation. Class is the pre-registered classification.

| Scenario | Ops per sample | Onity ns | R3 ns | UniRx ns | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200,000 | 3.23 | 4.39 | 3.70 | 0.737 (0.739) | faster | 0.872 (0.874) | faster |
| SubjectOnNext8 | 50,000 | 15.45 | 23.84 | 29.68 | 0.634 (0.666) | faster | 0.521 (0.524) | faster |
| SubjectSubscribeDispose | 20,000 | 90.34 | 265.85 | 513.75 | 0.340 (0.365) | faster | 0.183 (0.196) | faster |
| PropertySetChanged | 200,000 | 6.56 | 11.76 | 12.26 | 0.558 (0.558) | faster | 0.535 (0.541) | faster |
| PropertySetSame | 200,000 | 4.69 | 6.59 | 7.10 | 0.713 (0.716) | faster | 0.659 (0.660) | faster |
| PropertySubscribeDispose | 20,000 | 103.69 | 289.68 | 180.90 | 0.368 (0.370) | faster | 0.566 (0.589) | faster |
| WhereSelectOnNext | 200,000 | 7.33 | 9.10 | 9.86 | 0.805 (0.811) | faster | 0.758 (0.812) | faster |
| ChainSubscribeDispose | 10,000 | 307.18 | 541.54 | 871.02 | 0.567 (0.593) | faster | 0.353 (0.396) | faster |
| CombineLatestOnNext | 100,000 | 6.28 | 43.47 | 48.51 | 0.145 (0.146) | faster | 0.129 (0.131) | faster |

Per-process ratios (p1, p2, p3):

| Scenario | Onity/R3 | Onity/UniRx |
| --- | --- | --- |
| SubjectOnNext1 | 0.737, 0.736, 0.739 | 0.872, 0.872, 0.874 |
| SubjectOnNext8 | 0.634, 0.630, 0.666 | 0.521, 0.515, 0.524 |
| SubjectSubscribeDispose | 0.365, 0.340, 0.340 | 0.196, 0.183, 0.176 |
| PropertySetChanged | 0.558, 0.558, 0.556 | 0.535, 0.532, 0.541 |
| PropertySetSame | 0.713, 0.711, 0.716 | 0.655, 0.660, 0.659 |
| PropertySubscribeDispose | 0.350, 0.370, 0.368 | 0.566, 0.565, 0.589 |
| WhereSelectOnNext | 0.805, 0.811, 0.772 | 0.720, 0.812, 0.758 |
| ChainSubscribeDispose | 0.593, 0.567, 0.561 | 0.396, 0.353, 0.338 |
| CombineLatestOnNext | 0.145, 0.143, 0.146 | 0.129, 0.128, 0.131 |

All nine rows are `faster` against both libraries; the worst process of any
row is 0.811 against R3 and 0.874 against UniRx. The follow-up gate passed at
this first attempt.

## UniRx follow-up, attempt 1, Mono (reported, not gated)

| Scenario | Ops per sample | Onity ns | R3 ns | UniRx ns | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200,000 | 5.26 | 8.73 | 4.79 | 0.603 (0.605) | faster | 1.098 (1.099) | slower |
| SubjectOnNext8 | 50,000 | 24.85 | 52.85 | 33.68 | 0.470 (0.471) | faster | 0.738 (0.744) | faster |
| SubjectSubscribeDispose | 20,000 | 94.94 | 284.32 | 471.77 | 0.330 (0.357) | faster | 0.201 (0.215) | faster |
| PropertySetChanged | 200,000 | 8.62 | 19.05 | 13.23 | 0.452 (0.452) | faster | 0.644 (0.654) | faster |
| PropertySetSame | 200,000 | 5.37 | 5.15 | 5.32 | 1.040 (1.043) | on par | 1.001 (1.016) | on par |
| PropertySubscribeDispose | 20,000 | 104.77 | 258.94 | 186.87 | 0.405 (0.419) | faster | 0.561 (0.568) | faster |
| WhereSelectOnNext | 200,000 | 8.83 | 12.16 | 8.51 | 0.719 (0.744) | faster | 1.015 (1.104) | on par |
| ChainSubscribeDispose | 10,000 | 329.70 | 539.45 | 708.26 | 0.614 (0.615) | faster | 0.466 (0.483) | faster |
| CombineLatestOnNext | 100,000 | 45.70 | 33.50 | 29.92 | 1.378 (1.390) | slower | 1.527 (1.551) | slower |

Per-process ratios (p1, p2, p3):

| Scenario | Onity/R3 | Onity/UniRx |
| --- | --- | --- |
| SubjectOnNext1 | 0.605, 0.597, 0.603 | 1.099, 1.097, 1.098 |
| SubjectOnNext8 | 0.471, 0.470, 0.464 | 0.733, 0.738, 0.744 |
| SubjectSubscribeDispose | 0.330, 0.312, 0.357 | 0.201, 0.183, 0.215 |
| PropertySetChanged | 0.447, 0.452, 0.452 | 0.644, 0.654, 0.597 |
| PropertySetSame | 1.043, 1.040, 0.828 | 1.000, 1.001, 1.016 |
| PropertySubscribeDispose | 0.371, 0.405, 0.419 | 0.534, 0.561, 0.568 |
| WhereSelectOnNext | 0.719, 0.713, 0.744 | 1.015, 0.999, 1.104 |
| ChainSubscribeDispose | 0.614, 0.610, 0.615 | 0.466, 0.458, 0.483 |
| CombineLatestOnNext | 1.390, 1.378, 1.350 | 1.551, 1.527, 1.527 |

On Mono, `PropertySetSame` is on par with both libraries (1.040 against R3,
1.001 against UniRx), `WhereSelectOnNext` is on par with UniRx (1.015), and
`SubjectOnNext1` against UniRx (1.098) and `CombineLatestOnNext` (1.378 against
R3, 1.527 against UniRx) are slower; every other row is faster. `CombineLatest`
was not redesigned.

## R3 gate, attempt 2 (source 9563453, IL2CPP, 2026-10-02T14:06Z)

Values are the median over three processes of each library's per-process
median, in ns per operation. Class is the pre-registered classification.

| Scenario | Ops per sample | Onity ns | R3 ns | UniRx ns | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200,000 | 4.25 | 4.57 | 4.07 | 0.930 (0.941) | faster | 1.044 (1.075) | on par |
| SubjectOnNext8 | 50,000 | 15.97 | 24.71 | 29.58 | 0.634 (0.646) | faster | 0.540 (0.543) | faster |
| SubjectSubscribeDispose | 20,000 | 118.44 | 335.62 | 607.36 | 0.353 (0.354) | faster | 0.178 (0.195) | faster |
| PropertySetChanged | 200,000 | 7.01 | 12.91 | 12.89 | 0.552 (0.572) | faster | 0.543 (0.543) | faster |
| PropertySetSame | 200,000 | 4.94 | 6.90 | 7.55 | 0.717 (0.724) | faster | 0.652 (0.667) | faster |
| PropertySubscribeDispose | 20,000 | 112.46 | 333.62 | 202.38 | 0.337 (0.359) | faster | 0.559 (0.582) | faster |
| WhereSelectOnNext | 200,000 | 7.96 | 9.70 | 10.14 | 0.801 (0.824) | faster | 0.785 (0.799) | faster |
| ChainSubscribeDispose | 10,000 | 365.18 | 625.06 | 1023.87 | 0.584 (0.598) | faster | 0.365 (0.370) | faster |
| CombineLatestOnNext | 100,000 | 7.06 | 45.22 | 52.56 | 0.153 (0.158) | faster | 0.135 (0.143) | faster |

Per-process ratios (p1, p2, p3):

| Scenario | Onity/R3 | Onity/UniRx |
| --- | --- | --- |
| SubjectOnNext1 | 0.917, 0.930, 0.941 | 1.075, 1.044, 1.016 |
| SubjectOnNext8 | 0.646, 0.575, 0.634 | 0.540, 0.539, 0.543 |
| SubjectSubscribeDispose | 0.353, 0.354, 0.339 | 0.195, 0.165, 0.178 |
| PropertySetChanged | 0.539, 0.572, 0.552 | 0.543, 0.533, 0.543 |
| PropertySetSame | 0.716, 0.724, 0.717 | 0.667, 0.537, 0.652 |
| PropertySubscribeDispose | 0.359, 0.337, 0.333 | 0.559, 0.556, 0.582 |
| WhereSelectOnNext | 0.777, 0.824, 0.801 | 0.775, 0.785, 0.799 |
| ChainSubscribeDispose | 0.556, 0.584, 0.598 | 0.365, 0.357, 0.370 |
| CombineLatestOnNext | 0.146, 0.158, 0.153 | 0.135, 0.133, 0.143 |

All nine rows are `faster` against R3; no process measured Onity slower than R3
in any row. `SubjectOnNext1` against UniRx was `on par` (median 1.044, worst
1.075), which motivated the follow-up gate.

## R3 gate, attempt 2, Mono (reported, not gated)

| Scenario | Ops per sample | Onity ns | R3 ns | UniRx ns | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 200,000 | 9.18 | 9.94 | 5.14 | 0.926 (1.112) | on par | 1.793 (1.952) | slower |
| SubjectOnNext8 | 50,000 | 25.98 | 54.42 | 34.89 | 0.472 (0.477) | faster | 0.736 (0.748) | faster |
| SubjectSubscribeDispose | 20,000 | 90.38 | 318.39 | 526.15 | 0.282 (0.307) | faster | 0.172 (0.179) | faster |
| PropertySetChanged | 200,000 | 11.21 | 19.54 | 13.56 | 0.580 (0.603) | faster | 0.834 (0.869) | faster |
| PropertySetSame | 200,000 | 5.21 | 5.32 | 5.63 | 0.961 (0.994) | on par | 0.936 (0.938) | faster |
| PropertySubscribeDispose | 20,000 | 99.06 | 284.69 | 199.91 | 0.338 (0.352) | faster | 0.492 (0.501) | faster |
| WhereSelectOnNext | 200,000 | 11.89 | 12.54 | 9.02 | 0.948 (0.955) | faster | 1.318 (1.382) | slower |
| ChainSubscribeDispose | 10,000 | 342.06 | 593.95 | 749.41 | 0.581 (0.610) | faster | 0.461 (0.462) | faster |
| CombineLatestOnNext | 100,000 | 56.80 | 38.14 | 32.63 | 1.489 (1.543) | slower | 1.741 (1.781) | slower |

Per-process ratios (p1, p2, p3):

| Scenario | Onity/R3 | Onity/UniRx |
| --- | --- | --- |
| SubjectOnNext1 | 1.112, 0.922, 0.926 | 1.952, 1.785, 1.793 |
| SubjectOnNext8 | 0.477, 0.472, 0.466 | 0.748, 0.732, 0.736 |
| SubjectSubscribeDispose | 0.282, 0.273, 0.307 | 0.172, 0.165, 0.179 |
| PropertySetChanged | 0.580, 0.603, 0.563 | 0.834, 0.869, 0.799 |
| PropertySetSame | 0.905, 0.994, 0.961 | 0.936, 0.833, 0.938 |
| PropertySubscribeDispose | 0.352, 0.316, 0.338 | 0.501, 0.463, 0.492 |
| WhereSelectOnNext | 0.933, 0.955, 0.948 | 1.382, 1.299, 1.318 |
| ChainSubscribeDispose | 0.610, 0.581, 0.560 | 0.462, 0.461, 0.451 |
| CombineLatestOnNext | 1.543, 1.448, 1.489 | 1.781, 1.715, 1.741 |

Had the IL2CPP rule applied to Mono against R3, three rows would miss it:
`SubjectOnNext1` (one process at 1.112), `PropertySetSame` (median 0.961) and
`CombineLatestOnNext` (slower). `CombineLatest` was not redesigned; on Mono it
stays about 1.5x slower than R3.

## R3 gate, attempt 1 (source b73c20e, 2026-10-02T13:35Z, failed)

The first attempt at the redesigned core failed the rule on one IL2CPP row:
`SubjectOnNext1` measured 4.68 ns against R3's 4.39 ns, median ratio 1.061
(per process 1.059, 1.082, 1.061), classified `slower`. The other eight IL2CPP
rows were `faster` (medians 0.178 to 0.812). On Mono, eight rows were `faster`
against R3 (`SubjectOnNext1` 0.944 with worst 0.952) and `CombineLatestOnNext`
was `slower` (1.607). The second attempt then trimmed per-call work from
`Subject.OnNext` and `ReactiveProperty.SetValue` (`9563453`).

IL2CPP, attempt 1:

| Scenario | Onity ns | R3 ns | UniRx ns | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 4.68 | 4.39 | 3.71 | 1.061 (1.082) | slower | 1.262 (1.286) | slower |
| SubjectOnNext8 | 17.53 | 23.79 | 28.46 | 0.732 (0.738) | faster | 0.614 (0.616) | faster |
| SubjectSubscribeDispose | 90.30 | 271.52 | 504.83 | 0.361 (0.382) | faster | 0.181 (0.204) | faster |
| PropertySetChanged | 6.95 | 12.14 | 14.33 | 0.578 (0.580) | faster | 0.488 (0.490) | faster |
| PropertySetSame | 4.81 | 6.67 | 7.59 | 0.722 (0.723) | faster | 0.633 (0.635) | faster |
| PropertySubscribeDispose | 100.01 | 282.89 | 176.86 | 0.349 (0.385) | faster | 0.571 (0.617) | faster |
| WhereSelectOnNext | 7.54 | 9.30 | 9.95 | 0.812 (0.814) | faster | 0.753 (0.762) | faster |
| ChainSubscribeDispose | 317.63 | 576.68 | 863.69 | 0.553 (0.598) | faster | 0.368 (0.385) | faster |
| CombineLatestOnNext | 7.68 | 43.07 | 50.03 | 0.178 (0.182) | faster | 0.153 (0.153) | faster |

## Baseline: the published 0.6.0 runtime (source 4e92db9)

Run 1 (2026-10-02T12:04Z, three processes per backend), IL2CPP:

| Scenario | Onity ns | R3 ns | UniRx ns | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |
| --- | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SubjectOnNext1 | 5.38 | 4.74 | 3.83 | 1.162 (1.253) | slower | 1.405 (1.500) | slower |
| SubjectOnNext8 | 22.24 | 37.85 | 30.16 | 0.680 (0.824) | faster | 0.680 (0.737) | faster |
| SubjectSubscribeDispose | 490.74 | 352.85 | 637.44 | 1.658 (1.755) | slower | 0.840 (0.908) | faster |
| PropertySetChanged | 10.95 | 13.00 | 12.71 | 0.869 (0.957) | faster | 0.877 (0.966) | faster |
| PropertySetSame | 5.33 | 6.89 | 8.18 | 0.773 (0.781) | faster | 0.671 (0.732) | faster |
| PropertySubscribeDispose | 525.26 | 330.00 | 207.41 | 1.523 (1.645) | slower | 2.497 (2.617) | slower |
| WhereSelectOnNext | 10.25 | 10.73 | 10.36 | 0.956 (0.984) | on par | 0.969 (0.990) | on par |
| ChainSubscribeDispose | 1737.04 | 675.80 | 1001.46 | 2.570 (2.582) | slower | 1.700 (1.735) | slower |
| CombineLatestOnNext | 8.17 | 44.17 | 51.13 | 0.190 (0.199) | faster | 0.167 (0.185) | faster |

Run 2 (2026-10-02T12:06Z, five processes per backend, same Players) confirmed
the picture: against R3 on IL2CPP, `SubjectOnNext8` (median 0.817, worst
1.492) and `PropertySetChanged` (0.894, worst 1.012) dropped to `on par`,
`WhereSelectOnNext` moved to `faster` (0.938, worst 0.941), `PropertySetSame`
(0.778) and `CombineLatestOnNext` (0.178) stayed `faster`, and the four
`slower` rows stayed `slower` (`SubjectOnNext1` 1.198, `SubjectSubscribeDispose`
1.664, `PropertySubscribeDispose` 1.644, `ChainSubscribeDispose` 2.521).

On Mono the 0.6.0 runtime was slower than R3 in every row of both runs: the
publish rows by a wide margin (run 1 medians `SubjectOnNext1` 4.503,
`SubjectOnNext8` 4.993, `PropertySetChanged` 3.351, `WhereSelectOnNext` 2.673),
the subscribe and dispose rows by 1.362 to 2.087, and `CombineLatestOnNext` by
1.484; `PropertySetSame` measured 1.476. Against UniRx on Mono it was slower in
eight rows and faster only in `SubjectSubscribeDispose` (0.777). The redesign
removed the Mono publish-path slowdown: at `9563453` the same four rows measure
0.926, 0.472, 0.580 and 0.948 against R3, and at `36c68c5` 0.603, 0.470, 0.452
and 0.719.

## Fairness notes

- The user code is the same shape for all three libraries: the same loops,
  the same cached predicate, selector and combiner, the same sink. The R3 and
  UniRx workload files differ from the Onity file only in their `using` line.
- `Subscribe(Action<int>)` is each library's idiomatic overload, and each wraps
  the action differently; those wrappers are part of what is measured. Onity's
  extension stores the action in the subscription node and the source invokes
  it directly. R3 wraps it in an `AnonymousObserver`, UniRx in a subscribe
  observer.
- Onity's node delivery checks an `Action<T>` callback first, then an
  `Observer<T>` callback, then the sink's virtual `OnNext`. A lambda passed to
  Onity's `Subscribe(Observer<T>)` therefore takes one extra load and test per
  value compared with the measured `Subscribe(Action<T>)` path.
- Since `36c68c5`, `Subject<T>` and `ReactiveProperty<T>` deliver to a lone
  live subscriber through a direct reference the node list keeps, without the
  general loop; a subscribe, unsubscribe or dispose inside that callback falls
  back to the general pass. The publish rows whose sources have one subscriber
  (`SubjectOnNext1`, `PropertySetChanged`, `PropertySetSame`,
  `WhereSelectOnNext`, `CombineLatestOnNext`) measure that path;
  `SubjectOnNext8` measures the general pass.
- All three libraries fuse `Where(...).Select(...)` into one operator; the
  0.6.0 runtime composed two delegate-backed operators.
- Onity.Reactive is main-thread only and takes no locks. R3's `CombineLatest`
  locks per value, as does UniRx's; that explains most of the `CombineLatest`
  gap on IL2CPP.
- Onity isolates each observer's exception with one exception region per
  notification pass (the 0.6.0 runtime caught around each call). Nothing was
  disabled in any library; defaults were left alone.
- The whole measurement runs synchronously inside one `AfterSceneLoad`
  callback, so no frame or PlayerLoop work interleaves with it. Samples are
  short, so the per-process ratios, not one number, carry a small difference.

## Noise and limits

- Other Unity Editors were open and compiling during the R3 gate's attempt 2.
  The runner's 5-minute wait for a quiet window expired and every process was
  flagged noisy. In the follow-up run the quiet-window wait succeeded, but the
  noise screen still flagged every process. The ratios come from the same
  process with the library order rotated per sample, which is why the
  per-process ratios are reported and the rule reads the worst process.
- One Windows PC, one CPU. Other machines, other platforms (ARM64, consoles,
  mobile), other Unity versions and Development Players are not covered.
- Allocation was not measured on either backend; nothing here is an allocation
  result.
- The nine workloads are synchronous main-thread paths: publish, property set,
  subscribe and dispose, a fused `Where` and `Select` chain, and a two-source
  `CombineLatest`. Time operators, thread-pool operators, frame streams and the
  async bridges were not measured.

## Files

| File | Content |
| --- | --- |
| `reactive-surpass-r3-2026-10-02/unirx-attempt-1-summary.md`, `.json` | The UniRx follow-up gate run, the shipped 0.7.0 core (`36c68c5`): per-row medians, classes and per-process ratios for both backends, with the build GUIDs and source hash. |
| `reactive-surpass-r3-2026-10-02/attempt-2-summary.md`, `.json` | The R3 gate run (`9563453`), same layout. |
| `reactive-surpass-r3-2026-10-02/attempt-1-summary.md`, `.json` | The failed first R3 gate attempt (`b73c20e`). |
| `reactive-surpass-r3-2026-10-02/baseline-run1-summary.md`, `.json` | The published 0.6.0 runtime, three processes per backend. |
| `reactive-surpass-r3-2026-10-02/baseline-run2-summary.md`, `.json` | The 0.6.0 confirmation run, five processes per backend. |

The summaries were written by `reactive-summary.py` after it validated every
report (Release non-development Players, matching build sidecars, golden
checks and checksums passed, ns/op consistent with the raw Stopwatch ticks).
Local paths in them are replaced by `<host>`. The per-process Player reports,
observation files and Player logs of all five runs are not committed; they are
attached to the v0.7.0 GitHub release as
`reactive-surpass-r3-2026-10-02-runs.zip`.

## Reproduce

On a Windows host set up as the harness README describes (the two libraries,
the `ONITY_REACTIVE_BENCHMARKS` define, the `WWW` module), build one Release
Player per backend with
`Onity.Editor.Benchmarks.OnityReactiveBenchmarkPlayerBuildRunner.BuildPlayerFromCommandLine`,
run `tools/benchmark-host/run-reactive-comparison.ps1` with three processes,
and summarize with `tools/benchmark-host/reactive-summary.py`. See
`Packages/com.onity.framework/Benchmarks/Reactive/README.md`.
