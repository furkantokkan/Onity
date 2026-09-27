# Legacy task runner retirement — 2026-09-27

## Correction

- Unreleased Plan 13 candidate on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`;
  package version remains 0.3.14. Unity 2022.3.62f2, CLI 1.0.0-beta.11.
- Destroying the legacy runner previously abandoned its three wait queues.
  Token callbacks only flag cancellation, so abandoned entries could never
  finish. Explicit PlayerLoop timing exposed this preexisting limitation.
- Retirement now closes the exact instance, detaches all queues before
  publication and cancels accepted waits with their original tokens. It does
  not cancel caller token sources or underlying Unity operations.
- Queue entries retain source versions. An active predicate/progress callback
  unwinds before forced cancellation; a completed source rented again cannot
  be canceled through its old entry. Reentrant manual replacement survives
  old cleanup, while session closure rejects replacement.
- Production changes are confined to `OnityAsync.cs`. Source retirement uses
  existing locks/version checks and value-type queue entries. No new public
  API, dependency or serialized asset was introduced.

## Verification

| Run | Passed | Failed / skipped |
| --- | ---: | ---: |
| EditMode, default optimization | 782 / 782 | 0 / 0 |
| PlayMode, default optimization | 73 / 73 | 0 / 0 |
| EditMode, Release optimization | 782 / 782 | 0 / 0 |
| PlayMode, Release optimization | 73 / 73 | 0 / 0 |
| Windows Mono Release Player smoke | 24 / 24 | 0 / 0 |
| Windows IL2CPP Release Player smoke | 24 / 24 | 0 / 0 |

Two new EditMode and nine grouped PlayMode cases cover all legacy queues,
unrequested-token preservation, predicate/progress reentrancy, throwing
consumers, real source reuse, worker cancellation, replacement ownership,
dispatcher/explicit timing independence and actual reload-disabled sessions.
Real AsyncOperation tests separately exercise pending and completed Tick
progress branches, and confirm the engine operation continues independently.
Two Player smoke cases cover retirement, replacement and token/operation
ownership on both backends.

The initial existing EditMode run passed 770/780. Ten failures came from
reflection helpers still invoking the old two-argument internal Tick method.
Eleven calls now pass a null owner for deliberately standalone frame sources;
their assertions are unchanged. The subsequent existing suite and all final
suites above passed. Initial and final result identities are retained.

All 500 runtime, test and task-benchmark source/assembly/metadata files matched
the verification host before builds. The host's three saved settings hashes
were restored, and the temporary benchmark scene was removed. Release Player
GUIDs: Mono `6a2ab14d465f4f1789941955c3cfa147`,
IL2CPP `1c552d2819634612a7b98fa012c88fd6`.

## Focused before/after measurement

The unchanged primary harness ran once per backend before and after the fix,
with eight samples per row. Only plain NextFrame scheduling and GetResult
slices are interpreted here. PlayerLoop execution, active Tick callbacks and
complete lifecycles are outside these measurements. This is a correctness fix
with additional lifecycle/version work, not a performance improvement.

| Backend | Concurrent waits | Scheduling ns, before → after | GetResult ns, before → after | Scheduling B, before → after |
| --- | ---: | ---: | ---: | ---: |
| Mono | 128 | 133.36 → 157.47 | 58.44 → 59.90 | 0 → 0 |
| Mono | 4,096 | 157.48 → 177.75 | 49.73 → 50.01 | 119.1875 → 119.1445 |
| IL2CPP | 128 | 149.07 → 162.43 | 107.10 → 102.56 | 0 → 0 |
| IL2CPP | 4,096 | 164.02 → 180.95 | 99.73 → 97.88 | 118.7383 → 118.3633 |

All GetResult allocation readings were zero. Calibrated HeapDelta selected
eight valid samples per row, with a 69,632-byte positive control and zero empty
control. The warmed 128-wait slices show no measurable heap growth. The
4,096-wait burst exceeds the unchanged source retention cap and still allocates.
HeapDelta is coarse and process-wide; zero does not prove zero GC allocation,
and small burst-byte differences do not establish a memory improvement.

Scheduling means increased approximately 9–18%; this observation does not
prove an exact regression magnitude from one process per revision/backend.
The added acceptance check and saved-version entry plausibly contribute;
their individual costs were not isolated.
UniTask control timings also varied and remain in the raw reports. Neither
full-cycle speed nor general parity with UniTask is established. No owned
build/test ran during measurement; an unrelated Editor remained open and
consumed 0.0625/0.2344 seconds of CPU during the two post-fix windows.

## Evidence

- [Tests, including the initial reflection failures](onitytask-legacy-retirement-2026-09-27/test-results-summary.json).
- [500 source hashes](onitytask-legacy-retirement-2026-09-27/source-hashes.json)
  and [settings/log provenance](onitytask-legacy-retirement-2026-09-27/provenance.json).
- [Pre-fix runtime and unchanged harness identity](onitytask-legacy-retirement-2026-09-27/plan13-legacy-before-provenance.json).
- [Mono smoke](onitytask-legacy-retirement-2026-09-27/plan13-legacy-mono-smoke.json)
  and [IL2CPP smoke](onitytask-legacy-retirement-2026-09-27/plan13-legacy-il2cpp-smoke.json).
- Mono primary: [before](onitytask-legacy-retirement-2026-09-27/plan13-legacy-before-mono-primary.json),
  [after](onitytask-legacy-retirement-2026-09-27/plan13-legacy-after-mono-primary.json).
- IL2CPP primary: [before](onitytask-legacy-retirement-2026-09-27/plan13-legacy-before-il2cpp-primary.json),
  [after](onitytask-legacy-retirement-2026-09-27/plan13-legacy-after-il2cpp-primary.json).
- Environment sidecars are retained beside every primary report.

No commit, push or release was performed. Timeout is the next bounded step.
