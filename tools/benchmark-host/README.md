# Benchmark host tooling

Scripts for running OnityTask benchmark packets on the isolated benchmark host project
(plan: `docs/Plan/16-OnityTask-Surpass-UniTask-Roadmap.md`, sections 1.4 and 2 PERF-0).
No Unity code. All PowerShell scripts run on Windows PowerShell 5.1 and PowerShell 7.

| Script | Purpose |
| --- | --- |
| `stage-host.ps1` | Mirror `Packages/com.onity.framework` from a source worktree into the host; writes `staging.json`. |
| `verify-host-settings.ps1` | Record / check / restore the four host settings files by file copy. |
| `quiet-check.ps1` | Pre-gate: other Unity CPU sample and power-plan check. |
| `run-paired-reports.ps1` | Paired baseline/candidate Release Player rounds with the noise screen. |
| `validate-reports.py` | Validate reports, ratio ranges, denominator CV, `--gate claim\|packet`, summaries. |
| `host-compile-check.py` | Roslyn check of every compiled host package assembly (`unity_compile_check.py --dependents`). |
| `run-attempt.ps1` | One end-to-end surpass-UniTask attempt or harness self-test (lease to gate). |
| `surpass-gate.py` | Surpass gate over primary/builderlifecycle/throughput reports; `--self-check`. |

`validate-framelifecycle.py` belongs to PERF-3 and is not part of this directory's PERF-0 set.

## Surpass attempts (`run-attempt.ps1`, `surpass-gate.py`)

Only two real attempts are budgeted, so validate the exact source first with a self-test, then measure the
self-tested binaries:

```text
pwsh -File tools/benchmark-host/run-attempt.ps1 -SelfTest -Label selftest-attempt-1 -Source <src>
pwsh -File tools/benchmark-host/run-attempt.ps1 -Label attempt-1 -Source <src> -PlayersFrom <host>/BenchmarkResults/surpass-selftest-attempt-1-<stamp>
```

- Steps: lease (`-LeaseOwner`, default `claude bench agent`; `-AcquireLease` takes an expired or missing
  lease; renewed before every step; `-ReleaseLease` deletes it at the end) and lock checks (no
  `Temp/UnityLockfile`, no Unity process on the host, no running Player) -> `quiet-check.ps1` (recorded;
  `-RequireQuiet` refuses) -> settings `-Record` once / `-Check` (a mismatch is restored by copy) -> stage
  (extras deleted) -> headless `UnityEditor.SyncVS.SyncSolution` when an `.asmdef`/`.asmref` changed
  (`-ForceProjectSync` always) -> `host-compile-check.py` (anything but `COMPILE_OK` aborts before a build)
  -> one Mono and one IL2CPP Release Player through
  `OnityTaskBenchmarkPlayerBuildRunner.BuildPlayerFromCommandLine` (or `-PlayersFrom <attempt dir>`, which
  copies that run's Players only when its staged sources are byte-identical and every binary matches its
  manifest) -> Player processes -> settings check/restore, temp scene absent, `Packages/manifest.json`
  unchanged (checked after every Editor run too) -> `attempt-manifest.json` + `files.sha256` ->
  `surpass-gate.py`.
- Processes per backend, one at a time, `-Priority High`, affinity excluding cores 0-1: three rounds of
  primary (`-onityTaskBenchmarkRetention matched`), builderlifecycle (matched) and throughput, with the
  backend order alternating and the suite order rotating per round, then one report-only primary and
  builderlifecycle process at default retention. `-SelfTest` runs one round plus the two default processes
  with `-onityTaskBenchmarkSelfTest`; its output is never performance evidence. A process that died, hung or
  never reached benchmark entry (neither a `benchmark-completed` nor a `benchmark-failed` startup marker)
  is moved with its stamped primary copy to `failed-runs/` and run once more (`-InfrastructureRetries`,
  default 1); a failure the Player reported itself (goldens, exceptions) is never retried.
- Archive: `BenchmarkResults/surpass-<label>-<yyyyMMdd-HHmm UTC>/` holds `attempt.log`, `staging.json`,
  `quiet-pregate.json`, Editor logs, `roslyn-host.log`, `Players/<Backend>/` with `.build.json` and
  `.buildreport.json` (build GUID), `binary-manifest-<backend>.json`, `runs/<stem>.json|.startup.log|
  .player.log|.observation.json` (noise status, priority/affinity, exit code), the gate outputs and
  `files.sha256`. Exit code: the gate's (0 PASS or self-test parse OK, 1 invalid/aborted, 3 FAIL).
- Gate rows (IL2CPP gates, Mono is reported with the same table): per process the Onity/UniTask ratio of
  primary ns/op per scenario (mean-based report value; `--primary-statistic median` uses raw sample
  medians), builderlifecycle total complete-cycle ns/op (median of samples per library) and throughput
  ns/await (median of samples, recomputed from raw ticks). Primary synchronous rows (`Completed GetResult`,
  `FromResult<int> GetResult`, `Async method completed GetResult`, `Async method completed<int> GetResult`)
  pass at median <= 1.00; every other primary row except ` (flow on)` rows, every flow-off
  builderlifecycle arm at 128 and 4096 and every throughput arm pass at median <= 0.95 and worst process
  <= 1.00. Flow-on rows, cohort-1 arms and default-retention processes are report-only.
- The gate rejects (INVALID) selfTest reports outside `--allow-self-test`, incomplete runs, failed goldens,
  mismatched throughput frame counts, missing rows, a process count other than three per group, non-Release
  or Editor reports, arithmetic that does not match the raw ticks, and mixed build GUIDs within a backend.
  It writes `surpass-gate.md` and `gate-summary.json` and never overwrites them without `--force`.
  `python surpass-gate.py --self-check` exercises every rule on synthetic data.

## Rules

- The host needs its lease (`BenchmarkResults/HOST-LEASE.json`) for every write below.
- No Unity process may run on the host during timing. `Temp/UnityLockfile` blocks staging,
  `-Record` and `-Restore`.
- Take a Unity slot (`onity-unity-slots`) before each headless Unity launch (build, tests).
- Never edit `Packages/manifest.json` or `ProjectSettings` by hand; settings are restored by copy.
- Evidence is never overwritten. Raw reports from flagged or replaced rounds are kept.

## Packet procedure

Let `$H` = host project, `$SRC` = candidate worktree, `$E` = `$H/BenchmarkResults/<packet>`.

1. Lease and quiet host: confirm the lease, no Editor on `$H`.
2. Record settings once per host state (originals under `BenchmarkResults/host-settings-original/`):
   `verify-host-settings.ps1 -HostProject $H -Record -Baseline <previous before-settings.json>`
   The current hashes must equal the baseline; otherwise stop. Later: `-Check` after every Editor run.
3. Stage the baseline source, then the candidate source, one at a time:
   `stage-host.ps1 -Source $SRC -HostProject $H -DryRun` (read the DIFF/MISSING/EXTRA lines and
   the frozen-file check), then the real run with `-ManifestPath $E/<rev>-staging.json`.
   Extras are listed first; any extra not matched by `-AllowedExtraPattern` refuses the run.
   Note: host `package.json` takes the source version, so sidecar labels change.
4. Roslyn compile check on every changed C# file (in the source worktree):
   `python C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py --project $SRC --files <files>`
5. One final focused test run per packet through the shared batch
   (`unity_test_batch.py submit ... --no-wait`, then `wait --id`); raw logs go to `$E`.
6. Build the Release Players (Mono and IL2CPP, non-development) for each revision into
   `$E/Players/<baseline|candidate>-<Mono|IL2CPP>/OnityTaskBenchmark.exe` with its
   `.build.json` sidecar; record binary manifests. Reuse frozen baseline Players when their hashes
   already match the current baseline revision.
7. Quiet gate: `quiet-check.ps1 -Seconds 10 -HostProject $H -FailOnHostEditor` (exit 0 = quiet).
   If other Editors are open, the plan allows per-packet rounds with the screen plus
   replacements; the final claim rounds need the user to close them (U-3).
8. Paired rounds:
   `run-paired-reports.ps1 -EvidenceRoot $E -Suite builderlifecycle -Backends Mono,IL2CPP -Rounds 3 -MaxReplacement 2 -Priority High`
   (`-AffinityMask` default excludes cores 0 and 1; `0` disables affinity). It plans all stems,
   refuses on existing evidence or missing Players, runs the quiet pre-gate
   (`quiet-pregate-*.json`), then one process at a time with reversed orders. Each process gets
   `<stem>.json`, `.startup.log`, `.player.log`, `.observation.json` with the noise status
   (`accepted`: other Unity CPU <= 0.5 s and <= 5% of wall; `flagged`; `unknown`).
   Extra Player arguments (for example the PERF-2 `-onityTaskBenchmarkRetention matched`) go in
   `-ExtraPlayerArgs`.
9. Validate: `python validate-reports.py --evidence-root $E --gate packet --target-arms "<regex>" --target-phase <phase>`
   or `--gate claim --revision candidate --flow off` for the final G1 evaluation; add
   `--no-timing-gate` for benchmark-only packets. Exit 0 pass, 1 invalid evidence, 3 gate failed.
   If it reports an above-limit UniTask denominator CV, replace the farthest round:
   `run-paired-reports.ps1 ... -Replacement -ReplaceBackend <Backend>` (max two per backend; a
   replacement also covers flagged/unknown pairs automatically), then validate again.
10. Settings and temp scene: `verify-host-settings.ps1 -HostProject $H -Check` must report all
    matches (use `-Restore` if not, then re-check). `Assets/OnityBenchmarkTemp/OnityTaskBenchmarkPlayer.unity`
    must be absent.
11. Archive: keep `$E` (reports, observations, staging manifests, binary manifests,
    `generated-summary.json`, `summary.md`); compute SHA256 of the archive and of each
    frozen runtime file; record them in the packet note.

Unproven packets restore the packet's runtime files to the frozen bytes (stage the baseline source
again) and keep tests and evidence.

## Gates implemented in `validate-reports.py`

- Selection: first three rounds where both revisions are `accepted`; flagged evidence is retained.
- Denominator stability: UniTask total ns/op across the six selected processes, CV <= 5% at 128 and
  <= 10% at 4096 (N=1 diagnostic).
- `--gate claim` (G1 on IL2CPP): per arm the median of the per-round Onity/UniTask ratio <= 0.95 and
  the worst round <= 1.00 at 128 and matched 4096; the default-retention 4096 arm is reported
  alongside, not gated. G2-G6 come from their own suites.
- `--gate packet`: target arms (IL2CPP) improve by more than max(2%, 1.5 x pooled repeat CV) and the
  target phase ratio is below 1; no arm on either backend regresses beyond its pooled repeat CV;
  the denominator check must pass.
- Output: `generated-summary.json` (schema 2) and `summary.md` (selection, ratio ranges in the
  published form, denominator CV, gate, phase table). Existing files are not overwritten without
  `--force`; `--output-dir` redirects, `--check-only` writes nothing.

The validator accepts flow on or off in the report environment and the optional per-metric
`retentionPolicy` (`default` or `matched`, matched only at 4096, with 3 warmups and 12 samples
allowed there).
