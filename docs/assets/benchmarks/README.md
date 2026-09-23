# Pooled typed builder experiment

`onitytask-builder-value-8292166-2026-09-23.json` is the raw Unity 2022.3.62f3
Editor/Mono comparison for commit `8292166`. The adjacent provenance file pins
the source, harness, UniTask revision, calibration controls, and report hash.

The focused builder test suite passed 29/29. In the warm 128-operation cohort,
typed async-method `NextFrame` scheduling allocated **752 B/operation** in the
experimental Onity builder and **72 B/operation** in pinned UniTask. The
current Onity product baseline measured **312 B/operation** with the same
scenario in a separate Editor run (see
`benchmark/onitytask-builder-attribution`, source commit `25c8e20`). The
experiment therefore regressed this key allocation path and was not merged.

The allocation marker covers main-thread scheduling only; frame waiting,
continuation dispatch, result consumption, and the full async lifecycle are
outside it. Timing numbers include harness overhead and vary between Editor
runs. These results do not establish an overall winner or IL2CPP behavior.
