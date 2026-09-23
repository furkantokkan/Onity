# Native OnityTask sharing lifecycle

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: 6a15305edfc0cf8e5cd1bba8afac4dc00fb5a8bc
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 7.025146484375 ns/op; 0 B/op

One pending native WhenAny<int> task per operation, built from two fresh completion-source inputs. Full sharing cases include input and native source construction, Preserve or AsTask conversion, pending callback registration, winner completion and callback dispatch, one GetResult per observer, two late result reads, and loser completion. The unshared native await case includes the same input and native source construction, one pending callback, winner completion and dispatch, one GetResult, and loser completion; it does not convert or read the result again. One observer compares Preserve with Preserve; four observers compare OnityTask.Preserve with UniTask.AsTask (.NET Task). Preallocated runner arrays, source verification, and cleanup are outside the measured operation; UniTask's two-input WhenAny params array is inside. Callback wait is inside. Profiler allocation samples cover the marked main-thread work; .NET Task callbacks may run on another thread outside that allocation marker. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveFullLifecycle | typed | 1 | OnityTask.Preserve | 2726.5106201171875 | 792 |
| PreserveFullLifecycle | typed | 1 | UniTask.Preserve | 2075.933837890625 | 344 |
| PreserveFullLifecycle | typed | 4 | OnityTask.Preserve | 3954.72412109375 | 896 |
| PreserveFullLifecycle | typed | 4 | UniTask.AsTask | 21780.5419921875 | 665.125 |
| NativeAwaitLifecycle | typed | 1 | OnityTask native await | 2415.3472900390625 | 672 |
| NativeAwaitLifecycle | typed | 1 | UniTask native await | 2141.7388916015625 | 304 |
