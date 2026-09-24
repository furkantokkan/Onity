# Typed prebridge allocation diagnostic

The v7 reports remain unchanged. This separate harness mode measures only the
Onity `WhenAll<int>` pending-fault lifecycle with the tracker off and both input
`AsTask` bridges created before each marker. It collects 32 raw main-thread
`GC.Alloc` samples of 128 operations each in one fresh Unity 2022.3.62f3 Editor
process per revision. Sources, typed input arrays, and fresh exceptions are
prepared outside the marker. Scheduling, input completion, output `GetResult`,
and cleanup use the same runner methods as v7. Input bridge exceptions remain
unobserved, as in v7.

Use the installed Unity CLI for preflight, then run the pinned Editor without
`-quit` because the Play Mode coroutine exits after writing the report:

```text
Unity.exe -batchmode -nographics -profiler-enable -projectPath <absolute-project> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTypedPrebridgeDiagnostic -onityTaskBenchmarkOutput <new-absolute-report.json> -logFile <absolute-log.log>
```

The menu rejects an existing output path. The JSON records all 32 raw sample
totals, mean B/op, both product-source hashes, harness/menu/config hashes, and
the exact 65,568-byte positive and zero-byte empty controls. Its empty-harness
samples must also be zero. Worker and all-thread allocation data and timing are
outside this diagnostic. Samples include harness cost without subtraction.

The isolated prebridge mode has its own warmup. Its 32 samples can show whether
the extra 376-byte event seen in v7 repeats, but they do not replace v7's
11-scenario promotion evidence or prove the allocation's source.
