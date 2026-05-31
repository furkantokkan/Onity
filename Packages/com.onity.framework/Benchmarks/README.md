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

Latest Windows IL2CPP Player run: `2026-05-31T00:48:27Z`, Unity 2022.3.62f3,
512 warmup iterations, 8 measured samples, 10,000 measured iterations per
sample, arithmetic mean.

| Scenario | Onity Baked | Onity Reflection | VContainer | Zenject | Onity Baked vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~17 ns | ~157 ns | ~79 ns | ~547 ns | ~+79% |
| Resolve Transient | ~191 ns | ~348 ns | ~576 ns | ~2,742 ns | ~+67% |
| Resolve Combined | ~232 ns | ~634 ns | ~794 ns | ~3,531 ns | ~+71% |
| Resolve Complex (6-level) | ~5,399 ns | ~6,095 ns | ~12,740 ns | ~61,072 ns | ~+58% |
| Prepare & Register Complex | ~31,084 ns | ~24,958 ns | ~42,446 ns | ~66,386 ns | ~+27% |

These timings are indicative, not a guarantee. They were captured in the Editor
and in a Windows IL2CPP player on one Windows PC; Unity version, scripting
backend, and graph shape can change the absolute numbers. The allocation
columns currently emitted by the Editor runner are not reliable and should not
be used for public claims until the allocation harness is corrected. The player
runner marks allocation measurement unavailable because Unity 2022 IL2CPP crashed
when reading managed allocation counters from the benchmark loop.

IL2CPP note: the current player benchmark uses Onity's generated AOT activator
registry for the benchmark graph, so hot implementation construction avoids
`ConstructorInfo.Invoke` on AOT builds. The current 10,000-iteration Windows
player timing order matches the Editor/Mono headline: Onity baked is ahead of
VContainer and Zenject on every measured scenario.

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
