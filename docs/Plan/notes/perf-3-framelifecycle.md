# PERF-3 packet note: `framelifecycle` suite (G2, G3)

Branch `perf/frame-lifecycle-suite` (base `530c4dd`). Benchmark-only packet with no runtime edits.
Status: **PARTIAL**. The code compiles against Unity's exact (non-transitive) asmdef references
after fix round 1. The development Player dry run has still not run; it is blocked on an
environment decision (see Open questions).

## Changed files

- `Packages/com.onity.framework/Benchmarks/Tasks/Runtime/OnityTaskFrameLifecycleBenchmarkRunner.cs`
  (new) and its `.meta` (new GUID `2f2bced408fb4db080ba802242648302`).
- `Packages/com.onity.framework/Benchmarks/Tasks/Runtime/OnityTaskBenchmarkPlayerRunner.cs`: adds
  the `framelifecycle` suite, its default report name and the doc line.
- `Packages/com.onity.framework/Benchmarks/Tasks/Editor/OnityTaskBenchmarkPlayerBuildRunner.cs`:
  - accepts `framelifecycle` and sets its default report name;
  - forwards an optional `-onityTaskFrameLifecycleArms` to the Player.
- `Packages/com.onity.framework/Benchmarks/Tasks/README.md`: new section "Frame lifecycle under the
  real PlayerLoop". The PERF-2 paragraph is not included because this job allowed the
  framelifecycle section only.
- `tools/benchmark-host/validate-framelifecycle.py` (new).
- `docs/Plan/notes/perf-3-framelifecycle.md` (this note).

No existing suite's arms or output formats changed. `OnityTaskBuilderLifecycleBenchmarkRunner.cs`
was not touched.

Do not commit: the untracked setup by-products `.vsconfig`, `Assets/NuGet.config.meta` and
`Assets/packages.config.meta` (created before this packet's first edit). The
`tools/benchmark-host/__pycache__/` directory from round 0's `py_compile` was deleted in round 1.

## Fix round 1 (verifier findings)

- **Blocker, CS0012 (fixed).** `OnityTask.Delay(k_delaySeconds, token)` pulled the
  `Delay(TimeSpan, OnityTimeProvider, ...)` overload into resolution. `OnityTimeProvider` lives in
  `Onity.Reactive`, which `Onity.TaskBenchmarks.asmdef` does not reference, and Unity references are
  not transitive. The call is now `OnityTask.Delay(delaySeconds: k_delaySeconds, cancellationToken: token)`.
  Round 0's scratch compile reported a false COMPILE_OK because it used a wider reference set.
- **Major, packet note (fixed).** The compile claim is now backed by an exact-reference compile (below).
- **Minor, checklist items asserted (fixed).**
  - `returnPassesInsideBrackets` is now measured. Every sample's queues must be empty at cohort end,
    the sanity bracket must hold, and for each arm/library pair whose warmups left returns pending
    after consumption (Onity IL2CPP Drain, UniTask LastPostLateUpdate queue), the median
    control-subtracted return-frame cost in that library's bracketed systems must be positive.
  - `harnessInertOutsideBrackets` is now checked at install. The harness MonoBehaviour must declare
    no `Update`/`FixedUpdate`/`LateUpdate`, and the installed loop must contain exactly
    3 + 2 x brackets harness systems. It also feeds `passed`. The report records
    `harnessSystemsInstalled`, `harnessSystemsExpected` and `harnessDeclaresUnityUpdate`.
- **Minor, control heap slice (fixed).** `bytesPerOperation` = (cohort bytes - frames x control
  bytes / 8) / ops when both slices are valid (`bytesValid`), else -1. `rawBytesPerOperation` keeps
  the uncorrected cohort bytes / ops. The report's `allocation` string documents both.
- **Minor, validator and uncalibrated counter (fixed).** `counterCalibrated == false` is accepted
  when every sample has no valid heap slice and reports -1. The PASS line prints
  `allocation unavailable`. A calibrated report still gets strict raw and control-subtracted
  bytes/op checks. A report with valid slices but no calibrated counter is rejected.
- **Minor, install-frame bracket counters (mitigated, unverified).** `SetPlayerLoop` runs from
  `Start()`, mid-frame. At the first frame-begin after install, the runner now resets the
  unresolved/anomaly counters, open flags and call totals. It records what it discarded in
  `installFrameDiscardedEvents`. Whether the install frame actually produces partial markers is
  still unverified until the dry run.

## What and why

- **Mechanism.** The suite extends the `InsertBracket` Before/After pattern.
  - Timestamp markers wrap every system either library runs.
  - Onity: `Update.ScriptRunBehaviourUpdate`, which hosts `OnityTaskRunner.Update` (sources, job
    registry, IL2CPP `Drain`), plus the FixedUpdate and LateUpdate behaviour systems and the three
    `OnityTaskPlayerLoop` markers.
  - UniTask: every system from the UniTask assembly. That is 32 runner and yield systems plus
    `UniTaskSynchronizationContext`.
  - The run fails at start unless 14 required systems are each bracketed exactly once: the six Onity
    systems above, plus UniTask Update, LastUpdate, PostLateUpdate and LastPostLateUpdate (yield and
    runner each).
- **Harness placement.**
  - A timed schedule system is the first system in Update. It also performs the frame-1 cancel for
    the 50% arms.
  - A timed consume system is the last system in PostLateUpdate, after UniTask's
    LastPostLateUpdate pass.
  - A frame-begin system is the first system of the loop. The whole-loop sanity bracket runs from
    frame-begin to the end of consume.
  - Bookkeeping runs after the sanity bracket closes and does not allocate inside windows.
  - The harness MonoBehaviour has no Update, FixedUpdate or LateUpdate method. This is checked at
    install.
  - Teardown runs from a coroutine after the last window.
- **Accounting.**
  - Library ticks per frame = the active library's brackets + harness schedule + harness consume.
  - Each cohort has 8 empty control frames before it.
  - Cost per op = (sum over the cohort's frames - frames x median control) / ops.
  - Each sample breaks this into the schedule frame, completion frames, return frame
    (consume + 1) and trailing frames.
- **Frame window.** The window length is fixed per arm from warmups 2 and 3 of both libraries:
  max(consume frame) + 2. Every sample of both libraries spans exactly that many frames, so both
  return passes fall inside it.
- **Workers.** `async OnityTask Worker(n) { for (i < n) await X; }` and the UniTask twin, using each
  library's default API.
  - The only harness work inside a worker is one counter increment.
  - Waits: NextFrame and Yield(Update) at 1 and 3 suspensions × 128/4096; one typed NextFrame-1 arm;
    DelayFrames(3), Delay(0.05 s) and WaitUntil(counter).
  - Token modes: none, a live registered token, or 50% canceled in frame 1. All 18 arms are
    predeclared.
  - Gate labels: G2 = 4 arms, G3 = 9 arms. The typed arm and the four 4096 arms are labelled
    "reported".
  - 4096 arms are labelled `policy: default`. They switch to `matched` (both caps raised to the
    cohort, then restored) once `OnityTask.SourcePoolCapacity` exists.
- **Protocol.**
  - 3 warmup + 8 sample cohorts (6 at 4096), in A,B,B,A order.
  - Flow off (set explicitly; PERF-1 makes it the default), tracker off, runner retention 128
    required. All settings are restored after the run.
  - `Time.captureDeltaTime = 0.02` keeps Delay(0.05 s) at a deterministic frame count in both
    libraries while frames stay uncapped. It is restored after the run.
  - The calibrated allocation slice per control block and per cohort gives control-subtracted
    bytes/op, or -1 when unavailable or invalid. Raw bytes/op is reported alongside.
- **Goldens.**
  - Completions = ops; the canceled count matches; no other exception; the typed checksum matches.
  - Every Onity queue (dispatcher, runner lists, phase pending lists, timers) and every UniTask queue
    (all runners, all yield queues) is empty and idle at each cohort end.
- **Boundary checklist (in the report):**
  - every bracket resolved once per frame, counting from the first full frame after install;
  - return passes measured inside the brackets (see fix round 1);
  - whole-loop sanity bracket within 5% of the bracket sum per arm and library;
  - same frame count per library;
  - registrations disposed inside the measured frames. To check this, the live token is canceled
    after each window, and Onity's cancellation-request counter must not move.
  - harness inert outside the brackets (checked at install).
  - Any failed item fails the Player (exit 1).

## Verification

| Check | Command | Result | Artifact |
| --- | --- | --- | --- |
| Unity readiness | `unity --version`; `ProjectSettings/ProjectVersion.txt` | CLI 1.0.0-beta.11; 2022.3.62f2 (pre-Unity 6, headless Unity.exe only) | - |
| Official Roslyn check (round 1) | `python ~/.claude/skills/unity-cli/scripts/unity_compile_check.py --project <wt> --files Packages/.../OnityTaskFrameLifecycleBenchmarkRunner.cs` | `COMPILE_UNVERIFIED`: "Assembly Onity.TaskBenchmarks has no generated .csproj yet". Unity has never compiled the assembly in this worktree (no `ONITY_TASK_BENCHMARKS` define, no UniTask package) | - |
| Exact-reference Roslyn compile (round 1, no project writes) | `python <scratchpad>/exact_ref_compile.py`, adapted from the verifier's `Logs/PERF-3-verify/verify_compile.py` with output redirected. References are exactly the csproj HintPaths plus the asmdef/ProjectReference assemblies (`Onity.Unity`, `Onity.Core`; Editor: + `Onity.TaskBenchmarks`). UniTask is built from the pinned read-only 2.5.11 source. `Onity.Reactive` is not referenced. | UniTask **COMPILE_OK**; `Onity.TaskBenchmarks` **COMPILE_OK** (8 warnings, none in changed files); `Onity.TaskBenchmarks.Editor` **COMPILE_OK** (107 pre-existing CS0649 DTO warnings, none on changed lines) | `Logs/PERF-3/roslyn-r2.txt`, `Logs/PERF-3/roslyn-r2/` |
| Round 0 scratch compile | - | **Superseded and invalid**: its COMPILE_OK used a wider reference set and hid CS0012 | `Logs/PERF-3/scratch-roslyn.txt` (do not cite) |
| Validator syntax and self-test (round 1) | `ast.parse`; synthetic reports in the scratchpad (not evidence), run with `PYTHONDONTWRITEBYTECODE=1` | Calibrated subset report passes with `--allow-subset` and fails without it. Uncalibrated report passes as `allocation unavailable`. Rejected: a wrong control-subtracted bytes/op, a valid heap slice without a calibrated counter, a harness-system count mismatch | scratchpad only |
| Development Player dry run | not run | **BLOCKED**, see Open questions | - |

No Unity process was started and no slot was taken in either round. ProjectSettings, Packages and
packages-lock are unmodified, and `Assets/OnityBenchmarkTemp` is absent.

## Dry-run command (for when the environment is approved)

Dry-run arms: `nextframe-1x128,yield-3x128,nextframe-typed-1x128,delayframes3-cancel50,delay50ms-registered,waituntil-none`.

```text
"C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe" -batchmode -nographics -quit -projectPath <wt>
  -executeMethod Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine
  -onityTaskBenchmarkBackend Mono -onityTaskBenchmarkSuite framelifecycle
  -onityTaskFrameLifecycleArms <dry-run arms>
  -onityTaskBenchmarkOutput <wt>/Logs/PERF-3/framelifecycle-dryrun-mono.json -logFile <wt>/Logs/PERF-3/dryrun-editor.log
python tools/benchmark-host/validate-framelifecycle.py Logs/PERF-3/framelifecycle-dryrun-mono.json --allow-subset
```

Afterwards: `git restore` ProjectSettings/* and Packages/packages-lock.json if they changed, and
confirm that `Assets/OnityBenchmarkTemp` is absent. In the report, check `installFrameDiscardedEvents`
(expected small and harmless), `boundaryChecklist.returnPassesEvidence` and `worstSanityDeviation`.

## Integrator lines

- CHANGELOG (Added, benchmarks): "`framelifecycle` Player suite: Onity vs UniTask 2.5.11 async
  lifecycles under the real PlayerLoop. Covers NextFrame, Yield, DelayFrames, Delay and WaitUntil
  with token none, registered and 50%-canceled. Every library PlayerLoop system is bracketed;
  control frames are subtracted; a whole-loop sanity bracket checks for work outside the brackets.
  Allocation is control-subtracted bytes/op, or -1 when no calibrated counter exists. Validator:
  `tools/benchmark-host/validate-framelifecycle.py`."
- `docs/guide/performance-and-il2cpp.md` / the comparison guide: cite G2/G3 only from the host
  baseline report (three processes per backend). No ratios exist yet.

## Open questions

1. **Dry run blocked (needs an orchestrator or user decision).** This worktree cannot build the
   benchmark assemblies as it stands. `Packages/manifest.json` has no UniTask package, and the
   Standalone `scriptingDefineSymbols` in `ProjectSettings/ProjectSettings.asset` lack
   `ONITY_TASK_BENCHMARKS`. Both files are outside this packet's allowed writes. In round 0 the
   permission classifier denied a temporary embed-and-restore of these. Round 1 did not retry that
   change without explicit approval.
   - Option a: approve temporary scaffolding in this worktree: an untracked
     `Packages/com.cysharp.unitask` copy of the pinned 2.5.11 package and the define, both restored
     by `git restore` and removal right after the run.
   - Option b: run the dry run on the benchmark host under its lease after staging.

   Either way, these acceptance items remain unverified: goldens pass, every bracket resolved,
   sanity within 5%, the validator on a real report, and install-frame behaviour.
2. **Risk to watch in the first run: sanity bracket noise.** Whole-loop control frames carry
   engine noise. The 128 Yield-1 and NextFrame-1 arms have the smallest signal. If the check
   exceeds 5%, report the attribution as the plan requires. Do not loosen it. The measured
   return-frame check could also flag on a very small IL2CPP Drain; treat that the same way.
3. **API asymmetries (measured as is, not corrected).**
   - UniTask `Yield(PlayerLoopTiming)` returns its pooled-free `YieldAwaitable`, while Onity
     `Yield(timing)` rents a source.
   - With a token, UniTask (default `cancelImmediately=false`) polls the token, while Onity
     registers a callback.
4. **IL2CPP reflection targets.** The run reads private queue fields through reflection, outside
   the timed windows: the Onity runner lists, phase pending lists and timers, and UniTask's
   `PlayerLoopRunner.tail/running/waitQueue.size`. If stripping removes them, the run fails fast
   with "unavailable or AOT stripped". Expect to confirm this on the first IL2CPP host build.
