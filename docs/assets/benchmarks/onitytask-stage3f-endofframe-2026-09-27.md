# End-of-frame verification — 2026-09-27

## Candidate and contract

- Unreleased Plan 13 candidate on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`,
  package 0.3.14, Unity 2022.3.62f2 and CLI 1.0.0-beta.11.
- Added `OnityTask.WaitForEndOfFrame(CancellationToken = default)`. One hidden
  persistent host uses one shared coroutine and a cached Unity WaitForEndOfFrame.
  Requests are native single-consumer values; requests added while publishing
  wait for a later drain. There is no per-request coroutine.
- Main-thread, active-session and rendering checks precede pre-cancellation.
  Editor batch mode and a null graphics device reject; a supported pre-canceled
  request returns canceled without creating a host.
- Cancellation callbacks only set a flag. Update removes each canceled request,
  disposes its registration and clears fields before publication, even if the
  coroutine is stalled. The bounded source pool retains up to 128 entries.
- Manual host retirement cancels only EOF waits. Global session closure or
  failed loop repair detaches EOF, timing and timer queues before any callback.
  The captured coroutine generation prevents an old pump from touching a
  replacement. [Usage and limits](../../guide/onitytask.md#rendering-end-of-frame).

## Verification

- Exact runtime source review passed. Standalone semantic compilation checked
  62 files against 210 references; actual Unity compilation also succeeded.
- Existing timing/timeout Release fixtures passed 33/33 EditMode and 19/19
  PlayMode cases. Two new EditMode and two new PlayMode cases passed, including
  two real reload-disabled Play sessions and Awake-time validation.
- These headless runs establish thread/session/rendering rejection and
  pre-cancellation behavior. They do not establish real rendering completion.
- Full default and Release suites each pass 814/814 EditMode and 84/84
  PlayMode cases, with no skips. Fresh Mono and IL2CPP Release Players each
  pass all six graphics cases and their paired 27-case headless smoke.
- [All eight Editor runs](onitytask-stage3f-endofframe-2026-09-27/test-results-summary.json)
  and [the matching runtime/test source snapshot](onitytask-stage3f-endofframe-2026-09-27/editor-source-hashes.json)
  precede the final Player-only harness additions; their runtime and fixture
  files are frozen.

### Graphics execution

Both backends ran on Direct3D11 / NVIDIA GeForce RTX 3090 with the actual built-in
pipeline, Linear color space, a 320x240 window and a 16x16 owned render target.
All four samples registered, reached LateUpdate, observed the owned render
callback and completed on the same frame (3, 4, 5 and 6), on thread 1. Readback
alternated exact red `(1, 0, 0)` and blue `(0, 0, 1)`.

Each backend's graphics and headless processes used the same verified build
GUID. Both are non-development Release builds; IL2CPP uses native Release
and Minimal stripping. Graphics runs retain tracker enabled, while paired
smoke disables it. Both use flow enabled, the internal context pair and runner
capacity 128. These are functional checks, not matched performance samples.
All 520 source/assembly/metadata hashes still match after execution. Player,
build and Editor settings hashes are restored; the temporary scene is absent.

The six groups also pass callback-created later waits, Update cancellation of
a deliberately stopped coroutine, same-source reuse with a stale token,
independent timing/Timeout survival through manual EOF retirement, old-pump
replacement and global close/failed-repair detachment before publication.

Source review strengthened green-channel validation and best-effort cleanup,
and moved restoration of the deliberately removed Update anchor into the
trigger callback's finally block. Otherwise the harness's own Update/deadline
could stop. These corrections preceded Player execution; runtime files were
unchanged.

- [Mono graphics report](onitytask-stage3f-endofframe-2026-09-27/plan13-stage3f-mono-eof.json).
- [Same-binary Mono headless smoke](onitytask-stage3f-endofframe-2026-09-27/plan13-stage3f-mono-eof.smoke.json).
- [IL2CPP graphics report](onitytask-stage3f-endofframe-2026-09-27/plan13-stage3f-il2cpp-eof.json).
- [Same-binary IL2CPP headless smoke](onitytask-stage3f-endofframe-2026-09-27/plan13-stage3f-il2cpp-eof.smoke.json).
- [Final 520-file Player source snapshot](onitytask-stage3f-endofframe-2026-09-27/source-hashes.json).
- [Build identities, settings and log hashes](onitytask-stage3f-endofframe-2026-09-27/provenance.json).

## Evidence limits

The graphics suite observes the owned camera's render callback, alternating
pixel colors, matching LateUpdate/completion frames and main-thread consumption.
It does not measure GPU presentation latency or independently instrument every
GUI pass. Headless rejection cannot substitute for that proof. A controlled stopped
coroutine tests cancellation during a stall; it does not test actual Editor
Scene-view interaction.

Cold host/coroutine creation allocates. Pooling does not by itself prove zero
allocation. This functional stage makes no allocation-quantity or speed claim
against UniTask. No commit, push or release has been performed.

The functional checkpoint is complete. The next approved stage adds finite
native async streams and synchronous operators; full UniTask parity is still
unfinished.
