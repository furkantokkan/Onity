# PERF-2 Matched-policy 4096 arm

Status: implemented, NOT compile-verified and NOT run (see Verification).

## Changed files
- `Packages/com.onity.framework/Benchmarks/Tasks/Runtime/OnityTaskBuilderLifecycleBenchmarkRunner.cs`
- `docs/Plan/notes/perf-2-matched-retention.md` (this note)

## What / why
- Player argument `-onityTaskBenchmarkRetention default|matched` is read by the runner itself
  (`ReadMatchedRetention`, case-insensitive, invalid value throws; absent = default).
- Matched: `OnityTask.RunnerPoolCapacity = max(128, cohort)` is set before each arm's warmups
  and restored to 128 in a `finally` around the warmup+sample loops. `RestoreSettings` (also
  run from `OnDestroy`) restores 128 as a second line of defence. Default mode still sets 128,
  so default behavior is unchanged. The initial "must be 128" guard is kept.
- 4096 arms use 3 warmups + 12 samples in both retention modes (1 and 128 arms stay 2 + 8).
  Metric arrays are sized per arm; the metrics array stays 48 (no extra arms; the retention
  mode is a run-level switch, so the matched Player run produces the matched arms).
- New JSON fields (additive): Metric `retentionPolicy` ("default"|"matched"), `retentionLabel`
  ("retention=default"|"retention=matched"), `runnerPoolCapacity`; Report `retentionArgument`,
  `largeCohortWarmupsPerLibrary`, `largeCohortSamplesPerLibrary`.
- Name clash: the Report already had a prose string field `retentionPolicy`. It was left
  unchanged for byte-compatibility, so the machine-readable value lives on each Metric. The
  Report prose still says "Onity default runner retention 128 ... unchanged"; in matched mode
  read `retentionArgument` / Metric fields instead.

## Verification
- Roslyn: `unity_compile_check.py ... OnityTaskBuilderLifecycleBenchmarkRunner.cs` returned
  COMPILE_UNVERIFIED ("Assembly Onity.TaskBenchmarks has no generated .csproj yet"). Cause: the
  checker only indexes assemblies present in Library/ScriptAssemblies, and
  `Onity.TaskBenchmarks` is not compiled in this worktree because `ONITY_TASK_BENCHMARKS` is
  not in the Standalone scripting defines. The copied gitignored `Onity.TaskBenchmarks.csproj`
  was also stale (lacked 4 runner files); I added them to that ignored file (backup in %TEMP%).
- Headless Unity attempt (slot-1, released): `Unity.exe -batchmode -nographics -quit -projectPath
  <wt> -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine
  -onityTaskBenchmarkBackend Mono -onityTaskBenchmarkSuite builderlifecycle ...` failed with
  "executeMethod class 'OnityTaskBenchmarkPlayerBuildRunner' could not be found" because the
  Editor benchmark assembly is define-gated. Log: `Logs/PERF-2/build.log`.
- Making it compile requires adding `ONITY_TASK_BENCHMARKS` to the Standalone entry of
  `ProjectSettings/ProjectSettings.asset` (then `git restore` it). The auto-mode classifier
  denied my sed edit of that tracked file, so I stopped. No ProjectSettings or packages-lock
  changes exist (git status clean apart from the runner and untracked Unity files).
- Build-runner pass-through: `OnityTaskBenchmarkPlayerBuildRunner.RunPlayer` builds the Player
  argument list explicitly and does NOT forward `-onityTaskBenchmarkRetention`; and
  `OnityTaskBenchmarkPlayerRunner` needs no change (the lifecycle runner reads the argument
  itself from `Environment.GetCommandLineArgs`). Open question for PERF-1/PERF-3: either launch
  the built Player exe by hand with the extra argument, or add a one-line forward in the
  BuildRunner (PERF-3 owns that file).

## Remaining proof (needs approval or a define)
1. Add `ONITY_TASK_BENCHMARKS` to Standalone defines temporarily (or run in the benchmark host).
2. Roslyn check should then report COMPILE_OK for Onity.TaskBenchmarks.
3. Mono Release build+run of `builderlifecycle` (default), then run the built exe by hand with
   `-onityTaskBenchmarkRetention matched`; expect Metric `retentionLabel` = `retention=matched`,
   `runnerPoolCapacity` 4096 for 4096 arms, 4096-arm `samples.Length` 12 / `warmups.Length` 3,
   and a final `OnityTask.RunnerPoolCapacity` of 128 (runner throws at start if not 128, so a
   second launch is the check; there is no post-run capacity field).

## Integrator lines
README (belongs to PERF-3): "builderlifecycle accepts `-onityTaskBenchmarkRetention
default|matched`. `matched` raises `OnityTask.RunnerPoolCapacity` to max(128, cohort) for each
arm (4096 for the 4096 cohort) so the pooled-runner retention matches UniTask's unbounded pool;
the capacity is restored to 128 afterwards. Reports carry `retentionPolicy` and
`retentionLabel` per metric. 4096 arms run 3 warmups and 12 samples."
CHANGELOG: "Benchmarks: builderlifecycle matched-retention arm (-onityTaskBenchmarkRetention)."

## Fix round (verifier findings)
- Report.retentionPolicy prose is now replaced in matched mode with a matched-specific string; default string unchanged.
- A trailing `-onityTaskBenchmarkRetention` with no value now throws ArgumentException.
- Matched labels the WHOLE run (`retention=matched` on every metric, including 1 and 128 cohorts whose capacity stays 128).
  The Onity runner pool is not drained between arms, so later arms in a matched run can start with runners retained by
  earlier 4096 arms. The new Report prose states this. Compare matched vs default at the 4096 arms.
- Default-mode 4096 output now has 3 warmups and 12 samples (plan 1.4).
- Removed the Unity-generated untracked files .vsconfig, Assets/NuGet.config.meta, Assets/packages.config.meta.
- Verification: official Roslyn gate still COMPILE_UNVERIFIED (Onity.TaskBenchmarks define-gated, no csproj). Proxy csc
  (Roslyn 2022.3.62f2, ONITY_TASK_BENCHMARKS defined, args in Logs/PERF-2-verify/args.rsp) gives zero errors in
  OnityTaskBuilderLifecycleBenchmarkRunner.cs; only 3 pre-existing CS0246 UniTask-stub errors in other files
  (Logs/PERF-2/csc3.txt). No Unity launch this round; Player run still needs ONITY_TASK_BENCHMARKS (orchestrator).
