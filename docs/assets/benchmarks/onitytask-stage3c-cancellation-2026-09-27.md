# Cancellation decorators verification — 2026-09-27

## Candidate and behavior

- Unreleased Plan 13 candidate on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`;
  package version remains 0.3.14. Unity 2022.3.62f2, CLI 1.0.0-beta.11.
- Added typed/untyped `AttachExternalCancellation` and
  `SuppressCancellationThrow` extensions, with original native wrappers and
  no new dependencies. [Usage and ownership](../../guide/onitytask.md#external-cancellation-and-cancellation-results).
- Attachment preserves original identity for noncancelable or already-complete
  inputs. Pending external cancellation competes with producer completion;
  the winning cancellation token is retained. The producer continues and its
  eventual outcome is observed, even after early output consumption.
- Suppression maps actual canceled status to a result flag. Faults retain
  identity and status, including faulted `OperationCanceledException`.
- Initialization and callback pins protect registration cleanup. Cancellation
  registration suppresses context capture; disposal and publication occur
  outside the registration gate. Pending producer ownership survives output
  consumption. No implicit Unity-thread return is introduced.

## Verification

| Run | Passed | Failed / skipped |
| --- | ---: | ---: |
| EditMode, default optimization | 777 / 777 | 0 / 0 |
| PlayMode, default optimization | 54 / 54 | 0 / 0 |
| EditMode, Release optimization | 777 / 777 | 0 / 0 |
| PlayMode, Release optimization | 54 / 54 | 0 / 0 |
| Windows Mono Release Player smoke | 20 / 20 | 0 / 0 |
| Windows IL2CPP Release Player smoke | 20 / 20 | 0 / 0 |

- The new fixtures contribute 33 EditMode and two PlayMode cases. They cover
  distinct producer/external tokens, precedence, actual cancellation versus
  faulted OCE, native/Task/preserved inputs, stale/preclaimed sources, exactly
  one consumer, derived-source existing/concurrent/future fault bridges,
  context suppression/restoration, bounded races and explicit Unity-context
  return through Task bridges.
- A dedicated test holds a cancellation continuation while another worker
  faults the producer. That worker must return before the continuation exits,
  detecting registration-disposal deadlock. Public factory/cancel races do not
  deterministically force every private callback-before-assignment interleaving;
  the initialization handshake also received code review.
- The first full PlayMode run passed 53/54. Its new Task-bridge test incorrectly
  required the exact OCE type; BCL correctly returned its `TaskCanceledException`
  subclass. Only that assertion changed to accept subclasses; token/context
  checks and runtime code remained unchanged. Both final PlayMode runs passed.
  The earlier passing default EditMode run uses identical runtime/EditMode
  sources. Initial and final manifests preserve this distinction.
- Player smoke adds external cancellation/late fault and suppression checks.
  Detailed handled-fault bookkeeping and bridge-timing proof come from EditMode;
  smoke independently checks late fault status and public outcomes.
- Players are non-development, use the internal context pair, flow enabled and
  runner capacity 128. IL2CPP uses native Release and Minimal stripping.
  PlayerSettings, build settings and Editor settings hashes were restored;
  the temporary build scene was removed.

## Limits and next step

Pending wrappers are deliberately unpooled and allocate. Completed suppression
success/cancellation returns inline, but allocation amounts and performance
against UniTask were **not measured in this stage**. There is no zero-allocation
or speed claim.

An indefinitely pending producer retains its observer. Retaining the original
native output also retains its wrapper and external cancellation source;
registration cleanup removes the reverse registration reference. The completed
Task bridge or preserved result does not itself retain the original wrapper;
a still-pending producer can continue retaining its observer/wrapper. Tighter
retention and pooling are later measured optimization work.

The functional checkpoint is complete. Proceed to the approved explicit
PlayerLoop timing stage; full UniTask parity remains unfinished. No commit,
push or release was performed.

## Evidence

- [Tests, including the corrected test failure](onitytask-stage3c-cancellation-2026-09-27/test-results-summary.json).
- [Final 482 source/assembly/metadata files](onitytask-stage3c-cancellation-2026-09-27/source-hashes.json)
  and [initial test snapshot](onitytask-stage3c-cancellation-2026-09-27/source-hashes-initial-tests.json).
- [Mono smoke](onitytask-stage3c-cancellation-2026-09-27/plan13-stage3c-mono-smoke.json),
  [IL2CPP smoke](onitytask-stage3c-cancellation-2026-09-27/plan13-stage3c-il2cpp-smoke.json).
- [Provenance and log hashes](onitytask-stage3c-cancellation-2026-09-27/provenance.json).
