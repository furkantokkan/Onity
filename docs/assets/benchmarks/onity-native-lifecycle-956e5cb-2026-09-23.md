# Native OnityTask sharing lifecycle

- Unity: 2022.3.62f3; Editor Mono
- UniTask commit: 2e993ff18f28c931602a07292df0b0804eebef99
- Onity runtime commit: 956e5cb356cdce0b324bfdad5242ac3c2b537a97
- Timing operations/sample: 4096; allocation operations/sample: 256; samples: 8
- Allocation counter: Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.
- Empty harness: 5.2459716796875 ns/op; 0 B/op

One pending native WhenAny<int> task per operation, built from two fresh completion-source inputs. Full sharing cases include input and native source construction, Preserve or AsTask conversion, pending callback registration, winner completion and callback dispatch, one GetResult per observer, two late result reads, and loser completion. The unshared native await case includes the same input and native source construction, one pending callback, winner completion and dispatch, one GetResult, and loser completion; it does not convert or read the result again. One observer compares Preserve with Preserve; four observers compare OnityTask.Preserve with UniTask.AsTask (.NET Task). Array storage, runner setup, and cleanup are outside the measured operation. Samples retain harness overhead; no baseline subtraction or overall winner is inferred.

| Stage | Type | Consumers | Library | ns/op | B/op |
| --- | --- | ---: | --- | ---: | ---: |
| PreserveFullLifecycle | typed | 1 | OnityTask.Preserve | 2755.4168701171875 | 904 |
| PreserveFullLifecycle | typed | 1 | UniTask.Preserve | 2075.433349609375 | 344 |
| PreserveFullLifecycle | typed | 4 | OnityTask.Preserve | 3899.969482421875 | 1008 |
| PreserveFullLifecycle | typed | 4 | UniTask.AsTask | 20086.270141601563 | 665.125 |
| NativeAwaitLifecycle | typed | 1 | OnityTask native await | 2204.9591064453125 | 656 |
| NativeAwaitLifecycle | typed | 1 | UniTask native await | 1931.689453125 | 304 |
