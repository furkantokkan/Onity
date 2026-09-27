# OnityTask thread-pool support verification

## Candidate and correctness

- Base: `3f098937e9198a17edcf72cd75e73bd5bc76e2fd` (0.3.14), plus the uncommitted Stage 1 additions recorded in [source provenance](onitytask-stage1-threadpool-2026-09-27/provenance.json). This is not a new published version.
- Unity 2022.3.62f2, Windows x64, UniTask 2.5.11 at `2e993ff18f28c931602a07292df0b0804eebef99`.
- Normal and Release Editor suites each passed **682/682 EditMode + 46/46 PlayMode** tests. [Counts and XML hashes](onitytask-stage1-threadpool-2026-09-27/test-results-summary.json); original XML/logs remain under the verification host's TestResults directory.
- Non-development Release [Mono](onitytask-stage1-threadpool-2026-09-27/plan13-stage1-mono-smoke.json) and [IL2CPP](onitytask-stage1-threadpool-2026-09-27/plan13-stage1-il2cpp-smoke.json) Players each passed **16/16** smoke cases, with InternalPair execution-context support, flow enabled, tracking disabled and runner retention 128.
- New cases cover raw switching, typed/untyped delegates, success/cancel/fault outcomes, completion threads, context flow/suppression and session boundaries. The full-suite test cleanup correction restored test-owned AsyncLocal worker values; it did not change runtime context semantics.
- Unity Roslyn 4.3 compiled the WebGL branch and IL inspection confirmed a prompt unsupported-platform guard with no queue call. [Compilation check](onitytask-stage1-threadpool-2026-09-27/webgl-compilation-check.json). No WebGL browser execution was performed.
- Both project/build setting hashes matched their pre-build values; the temporary benchmark scene directory was removed by the build runner.

## Thread-pool measurements

Two independent processes per backend; ten rows per process, eight samples per row. Each sample contains two 128-operation cohorts. Two warmups and two Unity frames between cohorts occur outside measurements. Library order alternates. The shared worker gate keeps native consumers registered before completion.

Ratios below are **Onity / UniTask**; lower is faster within that measured interval. Flow-off is the matched context contract. Completion is normalized cohort wall time through the last native result consumption, not individual task latency or isolated scheduler CPU time.

| Matched flow-off workload | Mono submission ratio | IL2CPP submission ratio | Mono completion ratio | IL2CPP completion ratio |
| --- | --- | --- | --- | --- |
| Raw switch (flow setting irrelevant) | 1.14–1.17 | 1.01–1.04 | 1.45–2.14 | 1.00–1.05 |
| Action | 1.16–1.21 | 1.07–1.14 | 1.06–1.18 | 0.67–0.81 |
| Func<int> | 1.17–1.23 | 0.98–1.07 | 1.16–1.17 | 0.62–0.75 |

The IL2CPP Action/Func cohorts completed sooner for Onity in these runs, while submission generally remained slower. Mono favored UniTask. These different results do not establish an overall async performance winner. Raw Mono completion was especially noisy (Onity sample standard deviation around 74–80% of the mean); do not infer a stable ratio from that row.

Flow-on rows are retained separately in the raw reports. They test enabled capture with no installed AsyncLocal payload; UniTask does not provide the same context-flow contract. They are not matched superiority evidence. Keep the existing FlowExecutionContext=true default.

### Allocation and timing limits

- Both runtimes rejected their per-thread allocation counter. The fallback HeapDelta counter cannot isolate submission from concurrent workers, so submission allocation is **unavailable**, as is full cross-worker lifecycle allocation: `-1`, zero valid samples. This is not zero allocation.
- Timing includes the harness gate and thread-pool scheduling. It ends at native GetResult, before producer unwind and deferred pool return. Inter-cohort frames permit deferred work but do not measure it.
- An unrelated Unity Editor remained open throughout; execution sidecars record its CPU and memory snapshots. No concurrent verification build/test was running during these measurements. These are local comparison runs, not a controlled hardware certification.
- The earlier primary scheduling benchmark and IL2CPP source-lock profile remain separate workloads. These new results do not supersede them or establish full UniTask parity.

Raw reports and all sample distributions:

- [Mono run 1](onitytask-stage1-threadpool-2026-09-27/plan13-stage1-mono-threadpool-1.json), [run 2](onitytask-stage1-threadpool-2026-09-27/plan13-stage1-mono-threadpool-2.json).
- [IL2CPP run 1](onitytask-stage1-threadpool-2026-09-27/plan13-stage1-il2cpp-threadpool-1.json), [run 2](onitytask-stage1-threadpool-2026-09-27/plan13-stage1-il2cpp-threadpool-2.json).
- [Runtime source hashes](onitytask-stage1-threadpool-2026-09-27/plan13-stage1-runtime-hashes.json).

## Completion note

Stage 1 adds original SwitchToThreadPool and synchronous Action/Func RunOnThreadPool APIs with cancellation and optional main-thread return. Runtime, regression fixtures, Player smoke, benchmark dispatch and usage docs are updated. Editor and both Player backend correctness gates passed. No runtime defaults, external dependencies, commits, pushes or releases changed. Next: the approved safe JobHandle bridge and a separately measured real Burst compute workload.
