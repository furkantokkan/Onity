# Finite async streams — 2026-09-27

## Candidate and behavior

- Unreleased Plan 13 candidate on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`,
  package 0.3.14, Unity 2022.3.62f2 and CLI 1.0.0-beta.11.
- Added covariant native async-enumerable/enumerator interfaces, `Empty`,
  `Return`, `Range`, `WithCancellation`, `Select`, `Where`, `Take`, `FirstAsync`
  and `ToArrayAsync`. Independent, unpooled enumerators support real
  `await foreach` consumption and cleanup. No new assembly or dependency.
- The iterative lifecycle preserves actual fault/cancellation status, tokens,
  sticky termination and Current lifetime. Native upstream disposal may settle
  a pending move. An explicit disposal winner returns false while its cleanup
  task waits for both observations; cleanup failure cannot replace that false.
- Normal-end, Take and terminal-consumer cleanup precedes ordinary results.
  Reentrant close between upstream acquisition/Current and the next user call
  prevents starting that next call. [Usage and limits](../../guide/onitytask.md#finite-async-streams).

## Verification

| Gate | Result |
| --- | ---: |
| New Release EditMode / PlayMode cases | 37/37 / 2/2 |
| Full Release EditMode / PlayMode | 851/851 / 86/86 |
| Full default EditMode / PlayMode | 851/851 / 86/86 |
| Mono / IL2CPP Release smoke | 29/29 / 29/29 |

No final gate skipped a test. Standalone semantic compilation checked 69 runtime
sources against 210 references, including C# 9 covariance and the compiler's
resolved native DisposeAsync call. Actual Unity and both Players also compiled.
Tests cover empty precancellation, range bounds, linked-token ownership,
100,000 synchronous Where rejections, overlapping moves, native/preserved/Task
inputs, both move/cleanup orders, faulted OCE, reentrant close, blocked delegates,
real await-foreach exits and worker/native versus explicit Task-bridge dispatch.

The first full Release Play run passed 85/86. An existing real AsyncOperation
test exhausted its 240-frame wait in 0.148 seconds. Only its polling helper was
changed to a five-second monotonic deadline, preserving predicates/assertions.
The focused 11-case check and both full Play runs then passed. The initial
failure remains in the [nine-run record](onitytask-stage4a-streams-2026-09-27/test-results-summary.json).
Release EditMode preceded that Play-only helper and final Player-harness fixes;
runtime and all EditMode fixtures stayed unchanged. Normal EditMode and final
Play runs use the final fixture.

## Bounded iteration measurement

One fresh non-development Player process per backend measured Range and a
Select/Where/Take chain, with three warmups and two runs of eight windows per
scenario. Each fresh enumerator first consumes one priming item outside the
bracket, materializing the lazy chain. The bracket then includes 4,096 true
moves, GetResult/Current reads, one final false and automatic Take cleanup.
Description/enumerator creation, priming and explicit final disposal are excluded.

| Backend | Scenario | Mean ms per 4,096 items | HeapDelta per window |
| --- | --- | ---: | ---: |
| Mono | Range | 1.845 | 0 in all 16 |
| Mono | Select/Where/Take | 14.421 | 0 in all 16 |
| IL2CPP | Range | 2.509 | 0 in all 16 |
| IL2CPP | Select/Where/Take | 19.791 | 0 in all 16 |

Every scenario's empty/positive controls read 0/69,632 bytes. HeapDelta measures
retained heap coarsely; `zeroAllocationProven` is false in every scenario.
Timings include traversal accounting and checks, use one process per backend,
and contain no UniTask baseline. They establish neither exact zero allocation
nor comparative speed. Descriptions, linked tokens, pending sources and array
consumers may allocate and remain outside this probe.

Source review caught lazy upstream construction inside the initial bracket.
Priming and its strict report validation were fixed before any Player measurement;
the initial harness produced no benchmark result. All raw windows and controls:
[Mono](onitytask-stage4a-streams-2026-09-27/plan13-stage4a-mono-finite.json),
[IL2CPP](onitytask-stage4a-streams-2026-09-27/plan13-stage4a-il2cpp-finite.json).

## Evidence and next stage

Each measured binary also passed its paired smoke:
[Mono](onitytask-stage4a-streams-2026-09-27/plan13-stage4a-mono-finite.smoke.json),
[IL2CPP](onitytask-stage4a-streams-2026-09-27/plan13-stage4a-il2cpp-finite.smoke.json).
Flow is enabled, tracking disabled, runner capacity 128 and the internal context
pair active. IL2CPP uses native Release and Minimal stripping.
All [540 source hashes](onitytask-stage4a-streams-2026-09-27/source-hashes.json)
match source and host after execution; the
[497 runtime/test hashes](onitytask-stage4a-streams-2026-09-27/editor-source-hashes.json),
build GUIDs, initial/final correction hashes and restored settings are retained
in [provenance](onitytask-stage4a-streams-2026-09-27/provenance.json).
The temporary build scene is absent. No commit, push or release was performed.

Next: on-demand Update streams and explicit BCL adapters under Plan 13 Stage 4b.
Channels, awaitable operators, reactive adapters and completion-order consumption
remain later stages; this checkpoint does not establish full UniTask parity.
