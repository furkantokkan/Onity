# OnityTask Stage 4c: channels

Date: 2026-09-27. Uncommitted candidate based on `3f098937`, package version
0.3.14, Unity 2022.3.62f2. Functional verification passed. Awaitable stream
operators are a separate follow-up packet.

## Change and ownership

- Added bounded and unbounded OnityChannel factories, Reader/Writer handles,
  a closed exception and private unpooled waiters in five new runtime files.
  Existing runtime, assemblies and dependencies remain unchanged.
- One channel gate commits FIFO admission, buffered delivery, writer promotion
  and reader ownership. ReadAll uses a dedicated enumerator so disposal cannot
  replace a committed delivery and silently lose its item.
- Cancellation uses direct token registrations, with assignment and callback
  pins. ReadAll can observe two caller tokens without owning a child CTS.
  Direct reads release their lease at commitment; ReadAll retains it while idle
  and until terminal cleanup. Late callbacks carry operation/lease identities.
- Source review corrected move/disposal publication ordering before execution.
  Cleanup can detach before publication, but shared disposal waits for the
  actual pending move source to become terminal. Exact source identity protects
  a reentrant replacement move from an older completion callback.

## Editor verification

| Check | Result |
| --- | --- |
| Roslyn runtime semantic compilation | 77 sources, 210 references, zero errors |
| Existing builder and stream tests, Release EditMode | 107/107 passed; zero skipped; CLI exit 0 |
| Focused channel tests, Release | 34/34 EditMode and 2/2 PlayMode; zero skipped; CLI exit 0 |
| Full suites, normal and Release | 913/913 EditMode and 92/92 PlayMode per optimization; zero skipped; CLI exit 0 |
| Strengthened disposal fixture, normal and Release | 34/34 EditMode per optimization; zero skipped; CLI exit 0 |
| Release Mono / IL2CPP smoke | 33/33 per backend, CLI exit 0; same binary as its measurement |
| Release Mono / IL2CPP allocation slices | 16 valid windows per capacity/backend, all HeapDelta zero; controls passed |

The first Unity run is `TestResults/plan13-stage4c-channels-existing-editmode-release.xml`
on `C:/Users/e-fur/.codex/worktrees/onity-builder-benchmark-4be50dc/Onity`.
All ten new runtime/metadata files matched the frozen implementation before
staging. No runtime change followed that initial compilation.

The full suites used 523 matching runtime/test files. A later source review
strengthened three parameter variants of one disposal test: the initial test
attached its continuation after synchronous disposal returned and could miss
premature disposal publication. The revised test holds the real waiter's
initialization pin, attaches the continuation while disposal is pending and
releases the pin in finally. The continuation checks that the original move
source is terminal and can reentrantly request false. Escaped Preserve/Task
results are consumed after their separate observers propagate completion.
The unchanged 34-case fixture then passed both optimizations. This was test
strengthening, with no observed runtime failure or runtime change.

The initial fixture SHA256 is `72A97BF02CF4AE98F9BF546AF7D07B11445EC0803135D0CC9FF575E852DFD9D4`;
the final fixture is `32C400325B15A6544A2B554ECB01A6DBD5DCB6BAED6806A2C8E4E32E395009D9`.
The initial source snapshot remains in the host's
`TestResults/plan13-stage4c-channels-initial-fixture/` directory.

[Nine test runs and retained log/XML hashes](onitytask-stage4c-channels-2026-09-27/test-results-summary.json),
[full-suite source manifest](onitytask-stage4c-channels-2026-09-27/editor-source-hashes.json)
and [final 568-file Player manifest](onitytask-stage4c-channels-2026-09-27/source-hashes.json)
record the chronology. The final manifest includes the strengthened fixture and
five benchmark/integration files staged after the full suites.

## Player results and measurement boundary

The slice measures only warmed bounded TryWrite/TryRead fill/drain
operations without pending waiters, at capacities 1 and 128. Construction,
priming, metadata and final validation stay outside the bracket. Pending waits,
token registration, descriptions and unbounded buffer growth may allocate.

Each fresh channel is primed with a full ring outside measurement. Three
warmups precede two internal runs of eight windows per capacity in one Player
process per backend. Every window performs 4,096 fill/drain cycles; capacity
128 therefore processes 524,288 items, not 4,096. Values, write/read counts and
long checksums are verified. Windows include loop/scalar/FIFO bookkeeping.

| Backend | Capacity | Items per window | Mean window time | Valid raw windows | HeapDelta per window |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mono | 1 | 4,096 | 0.20749 ms | 16/16 | 0 B |
| Mono | 128 | 524,288 | 27.40586 ms | 16/16 | 0 B |
| IL2CPP | 1 | 4,096 | 0.34907 ms | 16/16 | 0 B |
| IL2CPP | 128 | 524,288 | 44.36654 ms | 16/16 | 0 B |

All four empty controls read 0 B and held 64 KiB positive controls read
69,632 B. The selected counter is calibrated HeapDelta; all reports set
`zeroAllocationProven` to false. This is no measured retained-heap increase
in these windows, not exact zero allocated bytes. There is no UniTask baseline,
independent process repetition, pending-operation or unbounded-growth measure.

Both Players are non-development Release, flow enabled, runner capacity 128,
tracking disabled and ExecutionContext InternalPair. IL2CPP uses Release code
generation with Minimal stripping. Build GUIDs match each paired smoke report:
Mono `40871a99802b442990065ba8120d784a`,
IL2CPP `145b61b28dda4a81b8e70f664b52f2a7`.

Raw [Mono measurement](onitytask-stage4c-channels-2026-09-27/mono-channels.json),
[Mono smoke](onitytask-stage4c-channels-2026-09-27/mono-smoke.json),
[IL2CPP measurement](onitytask-stage4c-channels-2026-09-27/il2cpp-channels.json)
and [IL2CPP smoke](onitytask-stage4c-channels-2026-09-27/il2cpp-smoke.json)
are retained with [provenance and restoration checks](onitytask-stage4c-channels-2026-09-27/provenance.json).
All 568 final source/host hashes match after execution. The three settings
hashes are restored and the temporary scene directory is absent.

No commit, push or release was performed. Sequential awaitable operators are
the next independently verified packet.
