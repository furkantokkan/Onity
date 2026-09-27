# Timeout verification — 2026-09-27

## Candidate and contract

- Unreleased Plan 13 candidate on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`,
  package 0.3.14, Unity 2022.3.62f2 and CLI 1.0.0-beta.11.
- Added typed/untyped `Timeout` and `TimeoutWithoutException` extensions.
  Finite nonnegative seconds are validated before inspecting or claiming input.
  Completed input wins even at zero; pending zero reserves timeout immediately.
  Positive pending waits require the main thread in an accepting Play session.
- A private timer lane uses scaled/unscaled double-precision Unity clocks,
  after public Update waits. Callback-created timers first run in a later pass.
  Session retirement cancels; failed PlayerLoop repair faults. Producer
  completion can publish on a worker without an implicit context return.
- Only the wrapper's timeout becomes `TimeoutException` or a timeout flag.
  Producer faults, faulted OCE and original cancellation tokens retain their
  status. The producer is never canceled and late outcomes remain observed,
  including after the wrapper result has been consumed.
- Native outputs are single-consumer unless preserved or bridged. Plain Timeout
  returns an already-completed input unchanged; it does not add observer or
  bridge semantics to that identity path. [Usage](../../guide/onitytask.md#timeouts).

## Verification

| Run | Passed | Failed / skipped |
| --- | ---: | ---: |
| EditMode, default optimization | 812 / 812 | 0 / 0 |
| PlayMode, default optimization | 82 / 82 | 0 / 0 |
| EditMode, Release optimization | 812 / 812 | 0 / 0 |
| PlayMode, Release optimization | 82 / 82 | 0 / 0 |
| Windows Mono Release Player smoke | 26 / 26 | 0 / 0 |
| Windows IL2CPP Release Player smoke | 26 / 26 | 0 / 0 |

- The new fixtures add 30 EditMode cases and nine PlayMode groups. They cover
  validation before input consumption, all result shapes and source families,
  zero/completed precedence, worker rejection and completion, original tokens,
  faulty/stale/preclaimed inputs, sharing, derived completion-source late faults
  and bridge timing, bounded races and context restoration.
- Actual Unity tests also check paused scaled time with `float.Epsilon`,
  unscaled expiration, resumed scaled time, `float.MaxValue`, public Update
  before private timeout, timers created from callbacks, session/repair
  reentrancy and two real reload-disabled Play sessions with Awake-created work.
  Callback cleanup and unused producers are observed even when an assertion fails.
- Before Unity verification, source review corrected deadline rounding for tiny
  positive scaled durations: elapsed-time comparison preserves a paused clock.
  Empty timer passes avoid reading clocks; winner paths stop their timer before
  publishing output. These changes are part of the single tested runtime.
- The first focused Edit run passed 26/30. Four assertions incorrectly tried
  a second AsTask conversion of an already-consumed native output. Tests now
  share the first bridge and still test repeated conversion through Preserve.
  A second run passed 29/30: the race test incorrectly required decorator fault
  bookkeeping when plain Timeout returned the completed source unchanged.
  The corrected assertion distinguishes this identity path; actual pending
  decorators retain the stronger late-fault observation checks. Test cleanup
  also consumes the unused terminal source. Runtime files did not change.
- Both final full suites include these corrections. Focused Release PlayMode
  passed 9/9 beforehand. The final Player-only smoke expansion followed the
  full Editor suites; runtime and Edit/Play fixtures remained unchanged.
- Player smokes add two cases for four shapes, cancellation/fault identity,
  scaled/unscaled clocks, a preserved native worker producer and reentrant
  timers. Their late-fault assertions prove the producer's public outcome;
  automatic observation bookkeeping is independently established by EditMode.
- Both Players are non-development Release builds with context flow enabled,
  the internal context pair active, tracker disabled and runner capacity 128.
  IL2CPP uses native Release and Minimal stripping. All 508 source, assembly
  and metadata files match the host. The three build/Editor settings hashes
  were restored and the temporary scene removed.

## Limits and follow-up

Wrappers and timer entries are intentionally unpooled and allocate. Allocation
quantities and performance against UniTask were not measured in this stage.
No zero-allocation or speed-equivalence claim follows from the functional tests.
An indefinitely pending producer retains its observer after early timeout.

The functional checkpoint is complete. Next is the approved real end-of-frame
stage. Full UniTask parity remains unfinished. No commit, push or release was
performed.

## Evidence

- [Test summaries, including both earlier failures](onitytask-stage3e-timeout-2026-09-27/test-results-summary.json).
- [508 source/assembly/metadata hashes](onitytask-stage3e-timeout-2026-09-27/source-hashes.json).
- [Mono smoke](onitytask-stage3e-timeout-2026-09-27/plan13-stage3e-mono-smoke.json),
  [IL2CPP smoke](onitytask-stage3e-timeout-2026-09-27/plan13-stage3e-il2cpp-smoke.json).
- [Build identities, settings restoration and log hashes](onitytask-stage3e-timeout-2026-09-27/provenance.json).
