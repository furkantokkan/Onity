# PERF-0 host tooling: packet note

Branch `perf/benchmark-host-tooling` (base 530c4dd). New directory `tools/benchmark-host/`; no Unity code, no package edits.

## Delivered

- `stage-host.ps1`, `verify-host-settings.ps1`, `quiet-check.ps1`, `run-paired-reports.ps1`, `validate-reports.py`, `README.md`.

## Verification (read-only on the host, no Unity launched)

- `validate-reports.py` on `onitytask-builderlifecycle-20260930`: all 12 timing reports pass; ranges reproduce the published table (Mono 128 1-susp flow on 2.006-2.053, 4-susp 2.924-2.982; IL2CPP 128 1.688-2.082 / 2.394-2.787; Mono 4096 3.222-3.750 / 3.386-3.980; IL2CPP 4096 3.025-3.813 / 2.990-4.029). New denominator check flags 7 of 48 arms above the 5%/10% limit in that archive (informational for old evidence).
- `stage-host.ps1 -DryRun -Source onity-release-038 -HostProject <host>`: 707/707 files, 705 identical, 2 different (`CHANGELOG.md`, `package.json`), zero diffs for the five frozen runtime files.
- `verify-host-settings.ps1 -Check -Baseline before-settings.json`: all four files match (both PowerShell editions). Record/Restore exercised only on a scratch fake host.
- `quiet-check.ps1`: lists running Unity processes with PIDs and project names; refuses while other Editors are active (they are).
- `run-paired-reports.ps1`: schedule matches the original fixed order; with a fake evidence root it refused at the quiet pre-gate before starting anything. No Player was run.

## Notes for the integrator

- Quiet threshold is percent of one core per process (stricter than whole-machine percent).
- Priority and affinity are applied right after process start (a short startup window runs unpinned).
- Replacement rounds are numbered 4 and 5; the old `quiet-recheck.json` path is still validated for legacy evidence.
- Follow-up: none required before PERF-1.
