# Pending typed WhenAll verification — 2026-09-27

## Candidate and scope

- Unreleased Plan 13 changes on `3f098937e9198a17edcf72cd75e73bd5bc76e2fd`;
  package version remains 0.3.14. [All 468 source/metadata hashes](onitytask-stage3a-whenall-2026-09-27/source-hashes.json)
  match the source checkout and warm verification host. The host's Git HEAD
  does not identify its staged package.
- Unity 2022.3.62f2, Windows x64, Ryzen 9 5900X, non-development Release
  Mono and IL2CPP Players. IL2CPP native compiler: Release; stripping: Minimal.
- The new coordinator handles 1–16 unique exact built-in typed completion
  sources without existing bridges, mixed with inline/default values, with
  at least one pending source. Other inputs keep the previous fallback.
  Output remains Task-backed and shareable; its ordered result array escapes.
- Existing async/completion-source/builder EditMode fixtures passed 200/200
  in Release. The new independent fixture passed 25/25 in both normal and
  Release optimization; the PlayMode builder matrix passed 1/1 in Release.
  These are focused regressions, not a new full-suite run. Both Players
  passed 16/16 smoke cases. [Test identities](onitytask-stage3a-whenall-2026-09-27/test-results-summary.json).

## Method

Compare construction against the exact previous Onity Task bridge path at
2, 8 and 16 inputs. This is not a UniTask comparison. Prepare 128 fresh pending
aggregates outside the measurement, construct all outputs inside it, then
complete inputs in reverse order and validate every ordered result outside it.
Three warmups and eight samples of two cohorts give 384 validated warmup and
2,048 validated measured aggregates per row. Alternate path order and drain
between cohorts; run each backend twice in independent processes.

Flow is enabled, tracker disabled, runner capacity 128. All four reports
completed, all six rows retained eight timing/allocation samples, and every
result passed validation. Build settings were restored and no temporary scene
remained. No Onity test, build or second benchmark ran concurrently. An unrelated
Unity Editor remained open but accumulated only 0–0.047 CPU seconds during
each 1.80–4.65 second process window.

## Construction results

Mean nanoseconds per aggregate; heap bytes are the calibrated fallback estimate.

| Backend/run | Inputs | New ns | Previous ns | New heap B | Previous heap B |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mono 1 | 2 | 1,244 | 1,188 | 194 | 514 |
| Mono 2 | 2 | 823 | 873 | 200 | 506 |
| Mono 1 | 8 | 1,787 | 3,135 | 232 | 1,774 |
| Mono 2 | 8 | 1,460 | 2,651 | 232 | 1,706 |
| Mono 1 | 16 | 3,452 | 6,355 | 256 | 3,370 |
| Mono 2 | 16 | 3,627 | 7,432 | 232 | 3,378 |
| IL2CPP 1 | 2 | 1,232 | 780 | 204 | 576 |
| IL2CPP 2 | 2 | 1,440 | 835 | 214 | 608 |
| IL2CPP 1 | 8 | 3,345 | 2,839 | 256 | 1,972 |
| IL2CPP 2 | 8 | 3,885 | 3,126 | 262 | 1,960 |
| IL2CPP 1 | 16 | 5,286 | 4,681 | 280 | 3,630 |
| IL2CPP 2 | 16 | 5,787 | 5,628 | 278 | 3,718 |

Mono construction is 1.75–1.82× faster at eight inputs and 1.84–2.05× faster
at sixteen; two-input results are mixed. IL2CPP construction is **1.03–1.73×
slower**. Heap-delta estimates fall by 60–93% across these rows. This is an
allocation-versus-construction-time tradeoff, not a universal speedup.

The per-thread counter failed Mono's control and is deliberately skipped on
IL2CPP after earlier crashes. The profiler counter was unavailable. HeapDelta
passed a 64 KiB positive control (69,632 bytes) and an empty control (0 bytes),
with GC disabled during measurement. It is process-wide and page-granular;
these values are estimates, not exact per-object allocation counts.

Only construction is measured. In particular, the new result array is created
inside this slice while the old implementation can allocate it on completion.
Completion, result consumption, worker activity during completion and total
lifecycle allocation are excluded. No zero-allocation, end-to-end speed or
UniTask superiority conclusion follows from this experiment.

## Evidence and follow-up

- [Mono run 1](onitytask-stage3a-whenall-2026-09-27/plan13-stage3a-mono-whenall-1.json),
  [run 2](onitytask-stage3a-whenall-2026-09-27/plan13-stage3a-mono-whenall-2.json).
- [IL2CPP run 1](onitytask-stage3a-whenall-2026-09-27/plan13-stage3a-il2cpp-whenall-1.json),
  [run 2](onitytask-stage3a-whenall-2026-09-27/plan13-stage3a-il2cpp-whenall-2.json).
- [Derived measurements](onitytask-stage3a-whenall-2026-09-27/measurement-summary.json),
  [provenance and settings](onitytask-stage3a-whenall-2026-09-27/provenance.json).
- [Mono smoke](onitytask-stage3a-whenall-2026-09-27/plan13-stage3a-mono-smoke.json),
  [IL2CPP smoke](onitytask-stage3a-whenall-2026-09-27/plan13-stage3a-il2cpp-smoke.json).
- Reproduce using `-onityTaskBenchmarkSuite whenall` with the existing Player
  build entry, then run the built executable twice after its build Editor exits.
  Profile the complete IL2CPP lifecycle before claiming a scheduling improvement.
