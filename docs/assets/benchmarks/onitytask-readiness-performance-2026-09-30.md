# OnityTask synchronous Burst readiness feasibility - 2026-09-30

## Decision

The benchmark-only probe passed correctness in Mono and IL2CPP Release Players.
The recorded advance gate passes narrowly: 4096 simultaneously ready delays
save about 11-14% in the combined representation-copy/scan/synthetic-dispatch
window on both backends. Proceed to one controlled consumer-lifecycle probe.
Production adoption and general async superiority are not established.

Many cases regress after representation copies, especially IL2CPP pending and
sparse-ready cohorts. The timer-free manual-await builder gap remains unaffected.
Do not install this as a general scheduler or infer a UniTask win: UniTask is
not a measured arm of this numerical probe.

## Verification and identity

- Unity 2022.3.62f2, CLI 1.0.0-beta.11, Burst 1.8.29, existing Collections.
  Two non-development Players use BuildOptions.None; IL2CPP compiler Release.
- Source branch `codex/onity-0.5-async-performance`, base
  `5c09749b7531cdc28678153a0b4421ccc4eafdfe` plus the retained local packet.
  The verification host's old checkout/version label is not source identity.
  Sidecars still say Onity 0.3.14; exact staging hashes identify measured code.
- All 656 Runtime/Tests/Benchmarks files matched source and host before builds.
  Measured readiness runner SHA256:
  `4B6440B21E190551674C9DD2B91424E70DDCBA47B2B1B6690C448D654B86F5AC`.
- Mono build GUID `eafaffd9055a43b6a1ae1b5d2ed0a0b1`; IL2CPP build GUID
  `a6519d4289a64df985482641bf7e6709`. Separate binary manifests include executable,
  managed/native DLL and build-sidecar hashes.
- Roslyn passed the affected benchmark assemblies. Both startup runs and all
  six process repeats passed: 24 cases x 3 variants x 3 windows, two warmups
  and eight samples per metric. Independent boundary/proof checks include
  post-scan generation rejection. The report validator checked 13,824 measured
  batches, raw timing conversions, order, counts, checksums and environments.
- Production runtime hashes remain unchanged. All four host setting hashes
  restored after each build; the temporary scene folder was removed. No full
  EditMode/PlayMode suite repeat was needed for this benchmark-only packet.

## Measurement boundary

The same reverse frame/delay scan runs over ordinary C# managed arrays,
non-Burst synchronous IJob.Run NativeArrays and Burst IJob.Run NativeArrays.
Actual execution is proved by a poisoned BurstDiscard sentinel, not merely an
enabled flag. FloatMode.Strict preserves relative countdown arithmetic.

Inputs remain fixed during repeated one-step invocations; updated states and
generation-tagged ready tokens are overwritten. The combined native window
copies input, runs the kernel, transfers every updated record and ready token,
then writes synthetic preallocated managed sinks. It does not create real
tasks, run state machines, preserve contexts, retire sources or drain pools.
Setup/validation/compilation are outside timing; shared loop/delegate/timestamp
costs remain included, and controls are not subtracted.

Retained native payload is 311,304 bytes (4096 input/output records, ready
tokens, count/proof header); allocator overhead is excluded. Both Players
lacked an accepted per-thread allocation counter, so managed allocation is
explicitly unavailable (-1), never zero. No native-lifetime claim is made.

## Accepted process repeats

The noise screen was fixed before results: other Unity CPU <=5% of report wall
time AND <=0.5 CPU-seconds, without core normalization. Missing/new process
observations are unknown. Only known Unity interference is screened; a pass
does not prove an idle host or remove other operating-system activity.

| Process | Other Unity CPU | Wall time | Screen |
|---|---:|---:|---|
| Mono r1 | 0.078 s | 2.69 s | Accepted |
| Mono r2 | 0.641 s | 2.61 s | Flagged, retained |
| Mono r3 | 0.062 s | 2.83 s | Accepted |
| IL2CPP r1 | 0.062 s | 1.00 s | Flagged, retained |
| IL2CPP r2 | 0.047 s | 0.96 s | Accepted |
| IL2CPP r3 | 0.016 s | 1.02 s | Accepted |

A bounded ten-second recheck recorded 0.297 CPU-seconds (2.94% wall), permitting
one extra repeat per backend. No task-owned compilation, build or test overlapped
retained measurements. Startup/build-validation timings are excluded.

## Combined-window results

Each value is Burst/C# elapsed-time ratio: lower is better; 1 is parity.
Ranges span the two accepted process median values, not confidence intervals.
The managed denominator needs no representation copies. These are feasibility
data, not complete async or Onity/UniTask ratios.

| Count | Kind | Ready density | Mono ratio | IL2CPP ratio |
|---:|---|---|---:|---:|
| 128 | Frame | All pending | 1.100-1.106 | 1.528-2.125 |
| 128 | Frame | Every eighth ready | 1.057-1.070 | 1.559-1.832 |
| 128 | Frame | All ready | 1.017-1.025 | 0.924-0.965 |
| 128 | Delay | All pending | 0.697-0.701 | 1.558-1.576 |
| 128 | Delay | Every eighth ready | 0.754-0.757 | 1.532-1.581 |
| 128 | Delay | All ready | 0.911-0.938 | 0.961-0.984 |
| 4096 | Frame | All pending | 1.055-1.078 | 1.365-1.373 |
| 4096 | Frame | Every eighth ready | 1.023-1.054 | 1.362-1.363 |
| 4096 | Frame | All ready | 1.015-1.039 | 0.848-0.848 |
| 4096 | Delay | All pending | 0.650-0.681 | 1.378-1.382 |
| 4096 | Delay | Every eighth ready | 0.722-0.727 | 1.358-1.358 |
| 4096 | Delay | All ready | 0.862-0.894 | 0.865-0.888 |

At one slot the combined path is approximately 3.1-7.5x C# in Mono and
7.2-16.4x in IL2CPP. All 32-slot IL2CPP cohorts regress (1.11-2.35x).
Raw summaries retain every size, density, kernel-only and combined timing.

## Next bounded experiment and retained evidence

Compare managed/Burst scans with actual OnityTask/UniTask consumers over eight
pending steps and a terminal step, forwarding updated countdown state. Include
creation/registration, all representation copies, completion, single result
consumption and exact deferred-return drain CPU. Use 4096 delays with a 128
control, default Onity context flow and unchanged retention policy. Stop if
complete-lifecycle costs remove the gain or Onity loses to either its managed
baseline or UniTask using the identical engine.

[Evidence folder](onitytask-readiness-evidence-2026-09-30/) includes the exact
measured runner snapshot, source/binary manifests, sidecars, setting hashes and
summary. `raw-evidence.zip` retains all eight reports, six CPU observations,
both quiet rechecks and the validation/orchestration scripts. Original logs
and Player binaries remain in the isolated verification host.

Burst's managed-object restrictions and execution-proof technique follow the
official [type support](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html)
and [BurstDiscard](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/compilation-burstdiscard.html)
documentation. The measured package is 1.8.29; online 1.8 documentation currently
resolves to 1.8.30.
