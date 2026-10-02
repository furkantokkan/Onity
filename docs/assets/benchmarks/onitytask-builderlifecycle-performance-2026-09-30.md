# OnityTask complete builder lifecycle - 2026-09-30

## Decision

The benchmark and 24 regression cases are retained. The experimental two-site
`CanBeCanceled` disposal guard is reverted to the exact frozen runtime: its
narrow benefit did not satisfy the declared consistency and repeat-spread gate.
General async superiority over UniTask remains unmet.

All three matched process pairs passed the fixed interference screen on each
backend. Correctness and evidence integrity passed independently. Accepted
Unity interference observations do not establish stable GC or an idle computer;
large-cohort results and the UniTask control still varied materially.

## Verification and identity

- Unity 2022.3.62f2, CLI 1.0.0-beta.11, Windows x64 non-development Release Mono
  and IL2CPP Players. UniTask 2.5.11 is pinned at
  `2e993ff18f28c931602a07292df0b0804eebef99`.
- Source base `5c09749b7531cdc28678153a0b4421ccc4eafdfe` plus the retained local
  packet. The isolated host sidecar's 0.3.14 package label is historical;
  staged source hashes identify the actual measured implementation.
- Lifecycle runner SHA256:
  `AF5A62CDBC42BFDFEF0E8AF3727E360CCC66FE9C4C7718943C29AEBFA7B0E6DE`.
  Baseline and final retained OnityAsync.cs:
  `3D66BDB8C0A0F27DFA75649283C75AB9E0289866F9D61DDDA80A99B74685128F`.
  Candidate snapshot:
  `01BF0C57CAC4373BA9A9228B5BD5397380881CED9E265E29CBAAC585651036C1`.
- Each staging manifest has 660 files. The candidate differs only in the
  runtime's two disposal guards, the source-state test and benchmark README.
  Each binary manifest covers 213 executable, DLL and JSON entries, not every
  Player resource. All four build-validation reports passed 48 arms and eight
  goldens; startup timings are validation only.
- Candidate Release focused tests passed 94 EditMode cases and one Mono Editor
  PlayMode pooling smoke case, without skips. After the exact revert, all 40
  source-state cases passed, including the 24 additions. These are focused
  checks; no fresh full-suite claim follows. Context isolation is covered by
  the candidate's 12 existing two-suspension EditMode cases; the single
  PlayMode case does not execute IL2CPP branches.
- Roslyn passed benchmark, candidate, test and revert checks. The saved XML and
  build timestamps precede timing; earlier Roslyn stdout/timestamps were not
  archived, so its non-overlap has only the root's execution record. Root ran
  timing after all its builds, tests and compile checks had finished.
- All host setting hashes were restored and the temporary scene was removed.
  No public API, dependency, defaults, commit, push or release changed.

## Matched measurement boundary

Actual typed and untyped async consumers use the same preallocated manual gate,
with 1 or 4 sequential suspensions and cohorts of 1, 128 or 4096. Each invocation
times scheduling, every completion, exactly one native consumption and two
common return passes before any cohort reuse. Each pass drains Onity's exact
production queue and pinned UniTask's exact LastPostLateUpdate continuation
queue. Both queues are checked before, between and after passes. On IL2CPP the
active library has exactly the cohort count queued before the first pass and
the inactive library has zero; Mono has zero in both.

Each of 48 arms has two warmups and eight measured samples. Fixed invocations
per sample are `max(1, 4096 / cohortSize)`. Setup, reflection validation, array
reset and two later real safety frames are excluded equally. Timestamp and
array-loop controls use matched brackets and are reported without subtraction.
The total is a sum of synchronous Stopwatch elapsed phases, not CPU time or
natural PlayerLoop latency. Untimed reflection can perturb later cache/GC state.

ExecutionContext flow is enabled by default, tracking is disabled, runner
retention is 128, and the harness seeds no AsyncLocal. UniTask retention is
2147483647; the 4096 workload includes this policy difference. Flow-off and
cohort 1 are diagnostics. Allocation data is unavailable (-1), not zero.

The previous readinesscycle suite omitted UniTask IL2CPP return CPU; this new
suite corrects that boundary. Ratios from the two suites are not interchangeable.

## Result and retention gate

The table reports candidate total change within each paired process, in fixed
round order. Negative means lower raw Onity elapsed time. It does not infer
causality from a ratio or subtract controls.

| Default flow, cohort 128 | Round 1 | Round 2 | Round 3 |
|---|---:|---:|---:|
| Mono untyped, 1 suspension | -1.05% | -2.72% | -2.99% |
| Mono typed, 1 suspension | -0.85% | -2.19% | +1.41% |
| Mono untyped, 4 suspensions | +0.45% | -2.59% | -2.67% |
| Mono typed, 4 suspensions | -5.86% | -1.43% | +0.35% |
| IL2CPP untyped, 1 suspension | +0.15% | -21.74% | -2.75% |
| IL2CPP typed, 1 suspension | -1.34% | -11.13% | +0.37% |
| IL2CPP untyped, 4 suspensions | +0.46% | -16.16% | +0.73% |
| IL2CPP typed, 4 suspensions | -0.55% | -7.58% | -1.01% |

Mono untyped one-suspension completion improves 3.43/5.42/6.05%, corroborating
a narrow effect. Typed and four-suspension totals overlap repeat spread.
IL2CPP's large round-2 gains rely on elevated baseline observations: untyped
one-suspension baseline totals are 301.57/381.91/298.33 ns, with consumption
83.50/130.35/82.32 ns. The isolated favorable pair cannot meet the gate.
At 4096, large favorable and adverse observations occur; no causal regression
or broad benefit is established.

Paired UniTask total changes range from -6.23% to +3.26% on Mono and -4.04% to
+5.93% on IL2CPP at 128. At 4096 they span -23.74% to +21.16% and -19.04% to
+59.77%. The runtime guard is therefore reverted; the predeclared >=2%
exploratory screen and improvement beyond repeat spread were not met across
the required workloads. No favorable-result retries were performed.

The retained baseline's Onity/UniTask total ratios, across three processes and
both output shapes, show the remaining gap:

| Backend / cohort | 1 suspension | 4 suspensions |
|---|---:|---:|
| Mono / 128 | 2.006-2.053x | 2.924-2.982x |
| IL2CPP / 128 | 1.688-2.082x | 2.394-2.787x |
| Mono / 4096 | 3.222-3.750x | 3.386-3.980x |
| IL2CPP / 4096 | 3.025-3.813x | 2.990-4.029x |

## Collection and next step

The 12 timing processes ran sequentially from 02:05:30.043 to 02:06:48.766 UTC,
in the frozen interleaved/reversed order, with unique sequences 1-12. All six
pairs select rounds 1-3. The screen requires summed other-Unity CPU <=0.5 seconds
AND <=5% of process wall, without core normalization; missing/new processes are
unknown. The highest accepted fraction was 4.958% in candidate Mono round 3.
No replacement pair was needed. Across timing runs, 4608 measured samples,
1152 warmups and 96 goldens passed.

The next bounded Plan 15 investigation is IL2CPP main-thread deferred-return
buffering. Consumption plus returns account for roughly 40-44% of retained
one-suspension totals at 128; only part of this is removable enqueue overhead.
Keep workers synchronized, snapshot both queues before callbacks, preserve
depth retries, session/shutdown clearing, context and runner retention, and
update aggregate queue diagnostics before freezing a new baseline. Mono
return passes cost approximately 1.3-1.7 ns per operation; it needs separate
completion/context work. Burst numeric acceleration cannot remove those
managed lifetime costs.

## Retained evidence

- [Raw reports, observations, source snapshots, tests and helpers](onitytask-builderlifecycle-evidence-2026-09-30/raw-evidence.zip),
  SHA256 `3371CA8ED5F1D2D6562808C268889851BC81AF32CF5EF602CA9F1776621B859E`.
- [Archive manifest](onitytask-builderlifecycle-evidence-2026-09-30/archive-manifest.json),
  [validated summary](onitytask-builderlifecycle-evidence-2026-09-30/generated-summary.json),
  [verification identity](onitytask-builderlifecycle-evidence-2026-09-30/verification.json).
- [Baseline staging](onitytask-builderlifecycle-evidence-2026-09-30/baseline-staging.json)
  and [candidate staging](onitytask-builderlifecycle-evidence-2026-09-30/candidate-staging.json);
  [baseline binaries](onitytask-builderlifecycle-evidence-2026-09-30/baseline-binary-manifest.json)
  and [candidate binaries](onitytask-builderlifecycle-evidence-2026-09-30/candidate-binary-manifest.json).
- Frozen Players and full Editor/build logs remain on the isolated host at
  `BenchmarkResults/onitytask-builderlifecycle-20260930`. The archive excludes
  those binaries and full logs; validation of their hashes requires that host.
