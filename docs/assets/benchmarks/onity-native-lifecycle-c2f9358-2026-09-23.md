# Native OnityTask sharing lifecycle

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: c2f9358994b8198c12ab48e1569014787f65325e
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 6.268310546875 ns/op; 0 B/op

One pending native WhenAny<int> task per operation, built from two fresh completion-source inputs. Full sharing cases include input and native source construction, Preserve or AsTask conversion, pending callback registration, winner completion and callback dispatch, one GetResult per observer, two late result reads, and loser completion. The unshared native await case includes the same input and native source construction, one pending callback, winner completion and dispatch, one GetResult, and loser completion; it does not convert or read the result again. One observer compares Preserve with Preserve; four observers compare OnityTask.Preserve with UniTask.AsTask (.NET Task). Preallocated runner arrays, source verification, and cleanup are outside the measured operation; UniTask's two-input WhenAny params array is inside. Callback wait is inside. Profiler allocation samples cover the marked main-thread work; .NET Task callbacks may run on another thread outside that allocation marker. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveFullLifecycle | typed | 1 | OnityTask.Preserve | 3044.927978515625 | 776 |
| PreserveFullLifecycle | typed | 1 | UniTask.Preserve | 2235.8642578125 | 344 |
| PreserveFullLifecycle | typed | 4 | OnityTask.Preserve | 4029.40673828125 | 880 |
| PreserveFullLifecycle | typed | 4 | UniTask.AsTask | 20714.535522460938 | 665.30859375 |
| NativeAwaitLifecycle | typed | 1 | OnityTask native await | 2012.59765625 | 656 |
| NativeAwaitLifecycle | typed | 1 | UniTask native await | 1695.8526611328125 | 304 |
