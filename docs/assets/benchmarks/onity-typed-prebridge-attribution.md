# Typed prebridge allocation attribution

This mode is separate from the v7 comparison and v8 allocation diagnostic. It
profiles only the Onity `WhenAll<int>` pending-fault lifecycle with the tracker
off and both input `AsTask` bridges prepared before each marker. The same
128-operation preparation, ten warmup batches, scheduling, input completion,
output observation, and cleanup methods are used. Fresh exceptions and typed
input arrays are created outside the measured marker. The input bridge
exceptions are left unobserved, as in v7 and v8.

After the installed Unity CLI preflight, use a fresh Unity 2022.3.62f3 Editor
process per revision. The Play Mode coroutine exits after writing a new JSON
path, so omit `-quit`:

```text
Unity.exe -batchmode -nographics -profiler-enable -projectPath <absolute-project> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine -onityTypedPrebridgeAttribution -onityTaskBenchmarkOutput <new-absolute-report.json> -logFile <absolute-log.log>
```

The mode enables managed allocation callstacks before calibration, requires
Deep Profiling to be off, and restores the prior callstack setting on exit.
It stops if the main-thread positive and empty controls are not exactly
65,568 and zero bytes, an empty-harness control is nonzero, a marker is missing,
or the allocation events do not sum to their marker total. The full positive
control must have resolved callstacks containing `AllocatePositiveControl`,
with zero unresolved bytes. Callstacks must remain enabled and Deep Profiling
must remain off throughout capture; the final observed states are recorded.

The JSON records 32 raw lifecycle samples of 128 operations each, exact marker
frame/thread/sample identity, every descendant `GC.Alloc` event, its bytes and
parent sample, raw callstack addresses, resolved method names, and explicit
unresolved addresses or resolver errors. For each sample, attributed bytes plus
unresolved bytes must equal the main-thread marker total. An event with at least
one resolved method counts as attributed; an event with none counts as
unresolved. Unresolved addresses remain in either event. Product, harness,
asmdef, manifest, and lock hashes are included. Callstack capture can affect
allocations, so this report is diagnostic evidence and must not replace v7/v8
allocation gates. It makes no timing, worker, all-thread, or Player claim.
