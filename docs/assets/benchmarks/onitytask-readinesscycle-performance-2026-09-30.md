# OnityTask controlled Burst consumer lifecycle - 2026-09-30

## Decision

Correctness and evidence integrity passed in Mono and IL2CPP Release Players.
**Timing does not establish a replicated benefit:** the initial four independent
repeats failed the interference screen. The permitted later extra gives one
accepted process per backend, not replicated evidence. Production integration
remains NO-GO pending valid complete-cycle evidence. A subsequent boundary audit
also found that this suite excludes UniTask's IL2CPP deferred-return CPU; it is
not a matched complete-lifecycle comparison.
General async superiority over UniTask remains unverified.

The earlier [numerical readiness probe](onitytask-readiness-performance-2026-09-30.md)
retains its narrow 4096 all-ready delay result. That synthetic-dispatch result
does not establish a consumer-lifecycle win. Production runtime was unchanged
through both benchmark packets.

## Verification and identity

- Unity 2022.3.62f2, CLI 1.0.0-beta.11, Burst 1.8.29, pinned UniTask 2.5.11
  (`2e993ff18f28c931602a07292df0b0804eebef99`). Non-development Windows x64
  Players use BuildOptions.None; the IL2CPP compiler configuration is Release.
- Source base `5c09749b7531cdc28678153a0b4421ccc4eafdfe` plus the retained
  local packet on `codex/onity-0.5-async-performance`. All 658 staged
  Runtime/Tests/Benchmarks files match source and verification host. The host
  sidecar's old Onity 0.3.14 label is not measured source identity.
- Consumer runner SHA256:
  `DDC0C8777B5CF64908DE58710A18FC74AB8B5BE036DD1706E03C223DF2364902`.
  Shared readiness runner SHA256:
  `F0DF12A5C14686D062FD31C7990B15460A0DD453BD2010AD794EE0FD352C627D`.
  The earlier numerical probe's exact source snapshot is archived separately.
- Mono build GUID `ec3a45b0403d447984bab68e15a5d3f1`; IL2CPP build GUID
  `8ead5893aec641da9b31837b04b3d753`. The manifest's 213 executable, DLL and
  JSON entries match hashes and sizes; it does not inventory every Player asset.
- Roslyn passed 18 affected benchmark sources across three assemblies with
  dependents. Both build-validation runs and all four independent repeats
  completed without reported errors: 384 measured samples and 96 warmups,
  plus correctness goldens. Full EditMode/PlayMode suites were not repeated
  for these benchmark-only changes.
- After a fresh quiet check, two permitted r3 extras also passed correctness:
  eight reports total, 512 measured samples and 128 warmups. The original archive
  is preserved; the supplementary raw reports retain their separate identity.
- Independent verification checked rotating arm order, all 13 phase sums,
  per-consumer conversions, proof/count vectors, checksums, one consumption,
  deferred-return queues, source/binary identity and observation hashes.
  All four host setting hashes restored and the temporary scene was removed.
  All 354 staged production runtime file hashes remain unchanged between packets.

## Measurement boundary

Four arms pair a managed or synchronous Burst scan with actual typed
OnityTask<int> or UniTask<int> consumers of the same preallocated manual gate.
Counts are 128 and 4096. Timers start at 2.25 seconds; updated records feed
forward through eight pending 0.25-second ticks and a terminal ninth tick.
Managed scans operate in place. Burst includes managed-to-native staging,
IJob.Run, transfer of every updated record and ready token, and managed dispatch.
Strict float arithmetic and an execution sentinel verify the intended path.

The total sums Stopwatch elapsed time for registration, eight pending ticks,
terminal scan/continuations, native output consumption, and two calls of the
exact production deferred-return drain. These are synchronous elapsed slices,
not thread CPU counters. Proof writes, included GC activity, timestamp costs
and shared gate work remain included. Setup/reflection, validation, final
cleanup/disposal and two later real safety frames are excluded. This is not
natural PlayerLoop timer latency or an end-to-end wall-clock delay measurement.

**Deferred-return asymmetry discovered during Plan 15:** the timed calls drain
Onity only. Pinned UniTask IL2CPP queues async-runner returns in LastPostLateUpdate,
which this suite reaches in its untimed later safety frames. UniTask return CPU
and immediate-reuse behavior are therefore outside these totals. Consumer,
generation and Onity queue correctness remain valid; IL2CPP ratios must not be
described as a matched full physical lifecycle. Plan 15's new builderlifecycle
suite times both exact queues in common passes before any cohort reuse.

Onity context flow stays enabled, tracking disabled, and runner retention 128.
The harness does not seed AsyncLocal state. Installed UniTask retention is
2147483647, a different reported policy. Native retained payload is 311,304
bytes; allocator overhead is excluded. Neither backend accepted a per-thread
managed allocation counter: bytes are unavailable (-1), with no zero-GC claim.

Goldens pass on every arm: forwarded countdown, cancellation, post-scan stale
generation rejection, callback reentry invalidating another slot, and generation
reuse only after old tasks settle and are consumed. Sample counters verify one
native result consumption, an empty idle queue before/after draining, and zero
pending work between drains. IL2CPP Onity's first drain includes all deferred
returns. Logged callback/drain errors reject a report. Native buffers were
disposed after synchronous jobs. This controlled gate does not prove arbitrary
concurrent cancellation or production native-timer ownership.

## Excluded timing repeats

The frozen screen requires summed other-Unity CPU <=5% of report wall time
AND <=0.5 CPU-seconds per report, without core normalization. Missing/new
process observations are unknown. Task-owned builds/tests/Roslyn did not
overlap these repeats. The screen addresses known Unity interference only.

| Process | Other Unity CPU | Wall time | Screen |
|---|---:|---:|---|
| Mono r1 | 1.688 s | 1.149 s | Flagged |
| Mono r2 | 1.578 s | 1.028 s | Flagged |
| IL2CPP r1 | 0.906 s | 0.630 s | Flagged |
| IL2CPP r2 | 1.062 s | 0.637 s | Flagged |

The initial 10.111-second recheck recorded 15.109 CPU-seconds (149.4% wall) and an
unknown observation because a process exited. A later complete 10.028-second
recheck recorded 0.141 CPU-seconds (1.40% wall), permitting the single extra pair.
Mono r3 had 0.031 CPU-seconds / 1.058 seconds (2.95%); IL2CPP r3 had zero /
0.709 seconds. Both are accepted by the frozen screen; r1/r2 stay flagged.

R3 Onity Burst/managed ratios were 1.308/1.171 at 128 (Mono/IL2CPP), and
0.955/0.952 at 4096. These single-process observations do not establish a
replicated effect. The deferred-return asymmetry further limits IL2CPP
Onity/UniTask comparisons. No general speed or adoption claim follows.

## Recovery and retained evidence

Keep runtime unchanged. This campaign's one-extra allowance is now exhausted.
A later campaign needs its own declared repeat policy and the corrected return
boundary. Plan 15 targets timer-free managed lifetime costs. Until valid
consumer-lifecycle evidence exists, do not infer that Burst closes that gap or
begin general scheduler integration.

- [Raw reports, observations, validator and manifests](onitytask-readinesscycle-evidence-2026-09-30/raw-evidence.zip)
  (SHA256 `1F5AE890F6A70040794DF00D7C2A544D1F569A0654EADBD7C7F433331FC80C2D`).
- [Archive manifest](onitytask-readinesscycle-evidence-2026-09-30/archive-manifest.json),
  [excluded summary](onitytask-readinesscycle-evidence-2026-09-30/summary.json),
  [source staging](onitytask-readinesscycle-evidence-2026-09-30/source-staging.json),
  [binary manifest](onitytask-readinesscycle-evidence-2026-09-30/binary-manifest.json).
- [Later clean extras and quiet observations](onitytask-readinesscycle-evidence-2026-09-30/clean-extra/raw-extra.zip)
  (SHA256 `73EB9B5D45F9983957AE4588BA6B1D8A4953110D27ECF2251DA8085700A6CAAE`),
  [supplement manifest](onitytask-readinesscycle-evidence-2026-09-30/clean-extra/manifest.json).
- Exact [consumer source snapshot](onitytask-readinesscycle-evidence-2026-09-30/OnityTaskReadinessCycleBenchmarkRunner.cs.txt)
  and [shared scan snapshot](onitytask-readinesscycle-evidence-2026-09-30/OnityTaskReadinessBenchmarkRunner.cs.txt).
- Original frozen Players and build logs remain on the isolated verification
  host under `BenchmarkResults/onitytask-readinesscycle-20260930`; the archive
  carries identity manifests, not the Player binaries or full build logs.
