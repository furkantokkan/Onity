# TASK-20260729-165627507-cc9f1efd-fix-benchmark-meta — Fix immutable-package benchmark metadata

## Identity

- Contract status: ready
- Task ID: TASK-20260729-165627507-cc9f1efd-fix-benchmark-meta
- Supersedes: none
- Title: Fix immutable-package benchmark metadata
- Repository (absolute path): C:\Users\e-fur\Documents\Repos\Onity
- Branch/worktree or Plastic workspace: codex/release-pr6-integration at C:\Users\e-fur\AppData\Local\Temp\onity-pr6-fix-20260729-019faebb
- Base revision: 4870a0430fe2fd346403e2e272f797a1ad3115be
- Task kind: bug
- Area: UPM package benchmark results and release validation
- Workflow: direct

## Objective and baseline

- Observable objective: Installing or importing Onity 0.3.7 must not emit repeated immutable-package errors for benchmark result files that lack Unity metadata.
- Current behavior/evidence: GitHub Issue #5 reports repeated `Asset Packages/com.onity.framework/Benchmarks/Results/*.json has no meta file, but it's in an immutable folder` errors on v0.3.6; a clean Unity 2022.3.62f3 import of the exact 0.3.7 release tree generated the untracked file `Packages/com.onity.framework/Benchmarks/Results/di-benchmark-player-20260531-004827.json.meta`, and a full package scan found that JSON as the only non-hidden package file without a matching `.meta`.
- Expected behavior: The benchmark JSON has stable committed metadata, a release check prevents future package files from missing metadata, and clean package import produces no matching Console error or new package metadata file.
- Behavior that must remain unchanged: Benchmark JSON contents and history, benchmark execution/output behavior, Onity runtime APIs, package dependency versions, scene-flow fixes from PR #6, and the user's existing dirty root worktree remain unchanged.

## Ownership

- Owner/writer role: unity-bugfixer with sequential independent verification
- Allowed paths (exact or narrow globs):
  - Packages/com.onity.framework/Benchmarks/Results/di-benchmark-player-20260531-004827.json.meta
  - .github/workflows/onity-ci.yml
  - CHANGELOG.md
  - Packages/com.onity.framework/CHANGELOG.md
  - production/tasks/TASK-20260729-165627507-cc9f1efd-fix-benchmark-meta/contract.md
- Forbidden paths/actions:
  - Packages/com.onity.framework/Benchmarks/Results/di-benchmark-player-20260531-004827.json
  - Packages/** except the two exact allowed package paths
  - ProjectSettings/**
  - Existing benchmark result contents or filenames
  - The dirty root worktree at C:\Users\e-fur\Documents\Repos\Onity
  - Git tag or GitHub release publication before every acceptance row passes
- Serialized assets: exact paths below
  - Asset: Packages/com.onity.framework/Benchmarks/Results/di-benchmark-player-20260531-004827.json
    Sole writer: none; content is read-only
    Matching `.meta` owner: unity-bugfixer
- Other active writers/worktrees checked: Root worktree has unrelated user changes and is forbidden; isolated task worktree is the sole writer for allowed paths.

## Acceptance scenarios

1. Given the Onity package tree, when all non-hidden package files are scanned, then every file has a matching committed `.meta` and CI fails if this invariant regresses.
2. Given a clean Unity 2022.3.62f3 package import, when the fixed tree is imported, then no missing-meta or immutable-folder Console error appears and no new metadata file is generated under the package.
3. Given the fixed release candidate, when required .NET, EditMode, PlayMode, diff, changelog-mirror, and GitHub CI checks run, then they pass without changing benchmark JSON contents or unrelated files.
4. Given the 0.3.7 changelog copies, when compared as Git blobs, then they are identical and document the immutable-package metadata fix with current verification evidence.

## References and constraints

- Story/GDD/ADR/design/log/image: https://github.com/furkantokkan/Onity/issues/5; local Unity import evidence from release tree `17edadecbc1485dc56a869fcc4ffca29891f421e`
- Unity version/platform: Unity 2022.3.62f3 on Windows; reported failure also affects Unity 6000.3.9f1
- Package/architecture constraints: Keep Onity runtime dependency-free; preserve existing package layout; use a stable Unity `.meta` GUID; add only a narrow CI metadata-completeness guard.
- Explicit out of scope: Benchmark refactoring, deleting or renaming result history, dependency changes, runtime behavior changes, Unity 6 installation, and unrelated open issues.

## Verification and risk

- Focused test path/command: Scan `Packages/com.onity.framework` for non-hidden files without matching `.meta`; run the same invariant in Onity CI.
- Required execution preflight: `unity-preflight`
- Required Unity domain workflow: `unity-game-dev`
- Unity compilation/Console evidence: Clean Unity 2022.3.62f3 batchmode import log has no compilation error and no missing-meta/immutable-folder error.
- EditMode: Full Onity EditMode suite must pass.
- PlayMode: Full Onity PlayMode suite must pass.
- Manual/built-player/profiler/device evidence: Not required; exact import and Console evidence replaces manual inspection.
- Risk: high
- Approval already granted (exact target only): none; the next explicit `$implement-task` invocation authorizes only this contract.
- Commit/checkin constraint: allowed-on-explicit-invocation
- Current invocation permission: none
- Handoff destination: `$implement-task TASK-20260729-165627507-cc9f1efd-fix-benchmark-meta`

## Stop conditions

- Stop if any second missing package metadata file appears, the benchmark JSON would need modification, package import cannot be verified, unrelated paths change, or any required test/CI channel fails.

<!-- TASK-LIFECYCLE:START -->
## Lifecycle (managed)

- Task state: in_progress
- Current phase: implementation
- Last cycle verdict: CONTINUE_SAME_TASK
- Contract fingerprint: sha256:eeecf6348d715968ef81ca02faf5f57f42c95670cd24d0f39fac31b568ae6e76
- Evidence fingerprint: none
- Reviewed revision: none
- Attempt count: 1
- Last feedback: User requested the active Onity package error be fixed and pushed without another approval prompt.
- Blocker: none
- Next action: Add stable benchmark metadata, add the CI invariant, and run the required verification matrix.
- Closed revision: none
- Closed at: none
- Closure mode: none
- Closed by: none
- Superseded by: none

### Acceptance evidence

| Criterion | Result | Evidence | Freshness |
|---|---|---|---|
| AC-1 | UNPROVEN | none | none |
| AC-2 | UNPROVEN | none | none |
| AC-3 | UNPROVEN | none | none |
| AC-4 | UNPROVEN | none | none |

### Defect ledger

| ID | Dedupe key | Status | Relation | Maps to | Symptom / repro | Latest evidence | Verified revision |
|---|---|---|---|---|---|---|---|

### Transition history

- Created — planning — contract authored from Issue #5 and reproduced release-tree import evidence
- Attempt 1 — implementation — direct execution authorized for the package fix and push
<!-- TASK-LIFECYCLE:END -->
