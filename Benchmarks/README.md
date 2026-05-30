# Onity Benchmarks

This folder contains benchmark tooling for comparing:
- `Onity`
- `VContainer`
- `Zenject`

## 1. Run DI Benchmarks in Unity

From the Unity menu:
- `Onity/Benchmarks/Run DI Benchmarks (Editor)`
- `Onity/Benchmarks/Build and Run DI Benchmarks (IL2CPP Player)`

Command line (batchmode) entry point:
- `Onity.Editor.Benchmarks.OnityDiBenchmarkRunner.RunBenchmarksFromCommandLine`
- `Onity.Editor.Benchmarks.OnityDiBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine`

Example:

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "C:\Users\e-fur\Documents\Repos\Onity" `
  -executeMethod Onity.Editor.Benchmarks.OnityDiBenchmarkRunner.RunBenchmarksFromCommandLine `
  -logFile "C:\Users\e-fur\Documents\Repos\Onity\Temp\onity-di-benchmark.log"
```

Precondition:
- Close all other Unity instances that currently have this same project open.

Output files are generated at:
- `Benchmarks/Results/di-benchmark-latest.json`
- `Benchmarks/Results/di-benchmark-latest.csv`
- `Benchmarks/Results/di-benchmark-latest.md`
- `Benchmarks/Results/di-benchmark-summary.md`
- `Benchmarks/Results/di-benchmark-player-latest.json`
- `Benchmarks/Results/di-benchmark-player-latest.csv`
- `Benchmarks/Results/di-benchmark-player-latest.md`

On GitHub `main`, the package copy is under
`Packages/com.onity.framework/Benchmarks`. On the `upm` branch, these paths are
at the package root.

The player build runner accepts `-onityBenchmarkOutput <absolute-path>`, which is
useful when writing IL2CPP output outside `Packages` to avoid importing benchmark
artifacts during the build session.

Scenarios:
- Resolve (Singleton)
- Resolve (Transient)
- Resolve (Combined)
- Resolve (Complex)
- Prepare & Register (Complex)

## 2. Latest Published DI Results

Latest Editor/Mono run: `2026-05-30T19:38:06Z`, Unity 2022.3.62f3, Windows
Editor/Mono, 512 warmup iterations, 8 measured samples, arithmetic mean.

| Scenario | Onity Baked | Onity Reflection | VContainer | Zenject | Onity Baked vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~63 ns | ~164 ns | ~214 ns | ~2,866 ns | ~+71% |
| Resolve Transient | ~1,083 ns | ~943 ns | ~1,879 ns | ~12,356 ns | ~+42% |
| Resolve Combined | ~972 ns | ~1,233 ns | ~2,079 ns | ~17,248 ns | ~+53% |
| Resolve Complex (6-level) | ~22,905 ns | ~25,940 ns | ~42,158 ns | ~289,823 ns | ~+46% |
| Prepare & Register Complex | ~61,044 ns | ~42,929 ns | ~150,730 ns | ~215,537 ns | ~+60% |

Latest Windows IL2CPP Player run: `2026-05-30T20:09:24Z`, Unity 2022.3.62f3,
512 warmup iterations, 8 measured samples, arithmetic mean.

| Scenario | Onity Baked | Onity Reflection | VContainer | Zenject | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Resolve Singleton | ~17 ns | ~102 ns | ~86 ns | ~469 ns | Onity faster |
| Resolve Transient | ~1,431 ns | ~1,581 ns | ~580 ns | ~2,458 ns | VContainer faster |
| Resolve Combined | ~1,263 ns | ~1,505 ns | ~602 ns | ~3,525 ns | VContainer faster |
| Resolve Complex (6-level) | ~34,729 ns | ~37,379 ns | ~12,918 ns | ~62,689 ns | VContainer faster |
| Prepare & Register Complex | ~23,872 ns | ~20,939 ns | ~38,465 ns | ~61,060 ns | Onity faster |

These timings are indicative, not a guarantee. They were captured in the Editor
and in a Windows IL2CPP player on one machine; hardware, Unity version, scripting
backend, and graph shape will change the absolute numbers. The allocation
columns currently emitted by the Editor runner are not reliable and should not
be used for public claims until the allocation harness is corrected. The player
runner marks allocation measurement unavailable because Unity 2022 IL2CPP crashed
when reading managed allocation counters from the benchmark loop.

IL2CPP note: Onity runs correctly in the player benchmark, but the current
IL2CPP timing order differs from Editor/Mono. VContainer is faster on transient,
combined, and complex resolve until Onity has a source-generated/AOT-specialized
activation path.

## 3. Render Charts

Install Python chart dependencies:

```bash
pip install matplotlib numpy
```

Generate charts:

```bash
python Packages/com.onity.framework/Benchmarks/Tools/render_di_benchmark_charts.py \
  --input Packages/com.onity.framework/Benchmarks/Results/di-benchmark-latest.json \
  --output-dir Packages/com.onity.framework/Benchmarks/Results
```

If you are currently inside `Benchmarks/Tools`, run:

```bash
python render_di_benchmark_charts.py \
  --input ../Results/di-benchmark-latest.json \
  --output-dir ../Results
```

Generated chart files:
- `Benchmarks/Results/di-runtime-comparison.png`
- `Benchmarks/Results/di-gc-alloc-comparison.png`
- `Benchmarks/Results/di-benchmark-summary.md`

## 4. Publishing Recommendations

- Run benchmark in a dedicated scene and stable environment.
- Disable domain reload changes during benchmark runs.
- Report Unity version, platform, sample count, and iteration count with every chart.
- Keep benchmark code and registration graph identical across frameworks.
