# Explicit PlayerLoop timing verification — 2026-09-27

## Candidate and contract

- Unreleased Plan 13 candidate on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`;
  package version remains 0.3.14. Unity 2022.3.62f2, CLI 1.0.0-beta.11.
- Added explicit Update, FixedUpdate and LateUpdate timing, immediately after
  their script callbacks. `Yield` targets the next occurrence; `NextFrame`
  and `DelayFrames` enforce rendered-frame distance. Existing overloads and
  the legacy runner remain unchanged.
- A separate session owner installs before scene loading, repairs only its
  own nodes in the current loop and retires accepted waits on exit. Successful
  repair retains queues; failed repair faults/detaches them. No loop watchdog
  or ECS dependency was added.
- Cancellation callbacks only set flags. Main-thread publication removes a
  source and disposes its registration before invoking consumers. Phase pass
  and session epoch checks protect reentrant registration and teardown.
- [Usage and ownership](../../guide/onitytask.md#playerloop-timings).

## Editor verification

| Run | Passed | Failed / skipped |
| --- | ---: | ---: |
| EditMode, default optimization | 780 / 780 | 0 / 0 |
| PlayMode, default optimization | 64 / 64 | 0 / 0 |
| EditMode, Release optimization | 780 / 780 | 0 / 0 |
| PlayMode, Release optimization | 64 / 64 | 0 / 0 |
| Windows Mono Release Player smoke | 22 / 22 | 0 / 0 |
| Windows IL2CPP Release Player smoke | 22 / 22 | 0 / 0 |

- Three new EditMode and ten grouped PlayMode cases cover validation order,
  phase placement and reentrancy, multiple actual fixed ticks in one rendered
  frame, paused worker cancellation, native/bridge/preserved consumption,
  current-loop repair, foreign nodes, failed repair, epochs, ECS coexistence,
  legacy-runner independence and two actual reload-disabled Play sessions.
- The first lifecycle test passed its Awake and in-Play assertions, then
  incorrectly required its scene component to remain alive after ExitPlayMode.
  It now checks retained managed result fields after exit while keeping the
  live-component guard before exit. Runtime code did not change.
- Initial PlayMode compilation found two unqualified `Time.frameCount` uses
  inside a SystemBase test. Qualifying UnityEngine.Time corrected the test's
  name binding. The next run passed 63/64; its remaining setup assumption
  exposed the preexisting legacy-runner teardown gap described below.
- Final normal EditMode preceded the new PlayMode fixture and smoke staging;
  runtime and EditMode sources are identical. Final PlayMode and Release
  EditMode use the corrected fixtures. Benchmark-only dispatch additions were
  staged afterward for Player builds.

## Measurement boundary

The separate `timing` suite brackets the actual injected Update node with
cached Before/After nodes. Each work window schedules 128 tokenless native
Yield operations, registers cached callbacks, runs the real cancellation scan
and drain, consumes every result once and returns pooled sources. Readings end
before validation/reporting. Three warmup cohorts precede eight samples of two
cohorts: 384 warmup and 2,048 measured waits per process.

Holder/delegate construction, loop installation, iterator/report work and
counter calibration are outside the bracket. Empty and retained 64 KiB
positive controls pass through the same bracket layout. Failed controls or
invalid samples leave allocation unverified; raw readings remain in JSON.
HeapDelta is coarse and process-wide: zero means no measured heap growth,
not proof of zero GC allocation. No UniTask speed comparison is made.

| Backend | Run | Mean ns/wait | HeapDelta bytes/wait | Valid allocation samples |
| --- | ---: | ---: | ---: | ---: |
| Mono | 1 | 621.73 | 0 | 8 / 8 |
| Mono | 2 | 361.23 | 0 | 8 / 8 |
| IL2CPP | 1 | 400.54 | 0 | 8 / 8 |
| IL2CPP | 2 | 403.32 | 0 | 8 / 8 |

All four processes passed both counter and same-bracket controls: 69,632 bytes
for the 64 KiB allocation and zero for the empty control. Every raw work sample
read zero. This supports no measured heap growth for this warmed, tokenless
Update Yield path. Token registrations, cold installation, pool growth, other
phases and other frame-wait variants are not covered by this allocation probe.
Mono timing varied considerably; these times establish no general speed claim.

Both Players use non-development Release, context flow enabled, internal
context support and runner capacity 128 with tracking disabled. IL2CPP uses
native Release and Minimal stripping. Build GUIDs are
`d327acfd324741b9908580ac78d53860` (Mono) and
`a0d4b9d503d74724a4358d8d4b277d55` (IL2CPP). All three saved settings hashes
were restored and the temporary scene removed.

No owned build/test ran during a measurement process. An unrelated Editor
remained open; its sampled CPU increase was 0–0.03125 seconds over process
windows of 0.48–0.99 seconds. Environment sidecars retain these observations;
this is not an exclusive-machine timing experiment.

## Separate legacy limitation

Destroying the legacy OnityTaskRunner can orphan its own pending waits. Its
OnDestroy retires Jobs and the dispatcher but does not drain the three legacy
wait lists; token callbacks cannot rescue those abandoned entries. The new
injected wait completed successfully in the failing test. The final test
completes its legacy setup wait before destruction to isolate independence.

This existing limitation was corrected in the subsequent bounded
[legacy retirement step](onitytask-legacy-retirement-2026-09-27.md), with saved
source versions, callback reentrancy, replacement ownership and session closure
checks. The measurements above retain the earlier source revision. It was not
a regression introduced by explicit timing.

## Evidence

- [Test results, including initial failures](onitytask-stage3d-playerloop-2026-09-27/test-results-summary.json).
- [494 source/assembly/metadata hashes](onitytask-stage3d-playerloop-2026-09-27/source-hashes.json).
- [Mono smoke](onitytask-stage3d-playerloop-2026-09-27/plan13-stage3d-mono-smoke.json),
  [IL2CPP smoke](onitytask-stage3d-playerloop-2026-09-27/plan13-stage3d-il2cpp-smoke.json).
- Raw timing: [Mono 1](onitytask-stage3d-playerloop-2026-09-27/plan13-stage3d-mono-timing-run1.json),
  [Mono 2](onitytask-stage3d-playerloop-2026-09-27/plan13-stage3d-mono-timing-run2.json),
  [IL2CPP 1](onitytask-stage3d-playerloop-2026-09-27/plan13-stage3d-il2cpp-timing-run1.json),
  [IL2CPP 2](onitytask-stage3d-playerloop-2026-09-27/plan13-stage3d-il2cpp-timing-run2.json).
- [Provenance, initial fixture identities and log hashes](onitytask-stage3d-playerloop-2026-09-27/provenance.json).

No commit, push or release was performed. Full UniTask parity remains unfinished.
