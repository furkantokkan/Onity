# Onity 0.4.0 release verification

Date: 2026-09-27. Unity 2022.3.62f2, Windows x64 Mono and IL2CPP.

## Released boundary

- Plan 13 through Stage 4c: thread-pool work, JobHandle/Burst integration,
  typed WhenAll and array WhenAny, cancellation decorators, PlayerLoop timing,
  timeout/end-of-frame support, finite and BCL async streams, channels and
  sequential SelectAwait/WhereAwait/ForEachAsync.
- All 574 runtime/test/benchmark files in the final
  [verified source manifest](onitytask-stage4c-await-operators-2026-09-27/source-hashes.json)
  match the release candidate. Package version and release documentation are
  packaging changes; historical execution reports retain version 0.3.14.
- No Stage 4d reactive adapters, fixtures or unfinished benchmark are included.
  Their local WIP is preserved for later, followed by WhenEach and further Unity
  adapters. The [continuation plan](../../Plan/13-OnityTask-ApiCoverageAndJobs.md)
  distinguishes these future contracts from released APIs.

## Verification

| Gate | Result |
| --- | --- |
| Full EditMode, normal / Release optimization | 947 / 947 passed in each |
| Full PlayMode, normal / Release optimization | 94 / 94 passed in each |
| Fresh Mono / IL2CPP non-development Release Players | 35 / 35 passed in each |
| Skipped tests in these Unity suites | 0 |
| Package metadata / duplicate GUIDs | No missing metadata or duplicate GUIDs |
| Core, analyzer and source-generator Release builds | Passed, zero warnings/errors |

The [final functional checkpoint](onitytask-stage4c-await-operators-2026-09-27.md)
retains initial fixture failures, corrections, XML/log hashes, source identity,
Player GUIDs and settings restoration. Exact code reassembly permits reuse of
those execution results; this release does not claim a second run of unchanged
Unity code after the version-only package edit. GitHub CI/docs results are
reported separately with the release.

## Performance and compatibility limits

- This release broadens functionality without claiming complete UniTask parity
  or general speed superiority. Windows verification does not prove every target.
- Burst accelerates the measured job computation. Managed scheduling, delegates
  and DI do not become Burst code through this bridge.
- Pending stream/decorator paths can allocate. Calibrated HeapDelta measurements
  report heap growth, not exact allocated bytes or proof of zero GC.
- Measurements are scoped to their reports. Some paths improve; typed WhenAll
  and array WhenAny include IL2CPP or allocation regressions documented in the
  [comparison](../../guide/onitytask-comparison.md). No new DI speed claim is made.
- FlowExecutionContext remains enabled; runner retention remains 128. Native
  single-consumer task ownership, Preserve/AsTask sharing and cancellation/cleanup
  rules remain documented in the [guide](../../guide/onitytask.md).
