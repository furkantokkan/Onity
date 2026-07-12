# Onity Benchmarks

This folder contains benchmark tooling for comparing:
- `Onity`
- `VContainer`
- `Zenject`
- `OnityTask` vs `UniTask` async hot-path primitives

## 1. Run DI Benchmarks in Unity

Benchmark comparison assemblies reference VContainer and Zenject. To compile and
run them in a package-only project, install those comparison packages and add
`ONITY_BENCHMARKS` to **Project Settings > Player > Scripting Define Symbols**.
The optional OnityTask comparison also requires UniTask when that same define is
enabled; it is benchmark-only and not a runtime dependency of Onity.

From the Unity menu:
- `Onity/Benchmarks/Run DI Benchmarks (Editor)`
- `Onity/Benchmarks/Build and Run DI Benchmarks (IL2CPP Player)`
- `Onity/Benchmarks/Run OnityTask Benchmarks (Play Mode)`

Command line (batchmode) entry point:
- `Onity.Editor.Benchmarks.OnityDiBenchmarkRunner.RunBenchmarksFromCommandLine`
- `Onity.Editor.Benchmarks.OnityDiBenchmarkPlayerBuildRunner.BuildAndRunFromCommandLine`
- `Onity.Editor.Benchmarks.OnityTaskBenchmarkMenu.RunFromCommandLine`

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
- `Benchmarks/Results/onity-task-benchmark-latest.json`
- `Benchmarks/Results/onity-task-benchmark-latest.csv`
- `Benchmarks/Results/onity-task-benchmark-latest.md`

On GitHub `main`, the package copy is under
`Packages/com.onity.framework/Benchmarks`. On the `upm` branch, these paths are
at the package root.

The player build runner accepts `-onityBenchmarkOutput <absolute-path>`, which is
useful when writing IL2CPP output outside `Packages` to avoid importing benchmark
artifacts during the build session.

Use `-onityBenchmarkScenario <name>` to run one player scenario for a focused
high-sample gate. Valid names are `ResolveSingleton`, `ResolveTransient`,
`ResolveCombined`, `ResolveComplex`, and `PrepareAndRegisterComplex`. To measure
only the singleton scenario with 1000 samples, append
`-onityBenchmarkScenario ResolveSingleton -onityBenchmarkSamples 1000`. Omit
the scenario argument to run the full suite.

Scenarios:
- Resolve (Singleton)
- Resolve (Transient)
- Resolve (Combined)
- Resolve (Complex)
- Prepare & Register (Complex)

## 2. Latest Published DI Results

Latest Editor/Mono run: `2026-07-12T13:31:37Z`, Unity 2022.3.62f3, Windows
Editor/Mono, 512 warmup iterations, 8 measured samples, arithmetic mean.

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~69 ns | ~78 ns | ~217 ns | ~2,778 ns | ~68% |
| Resolve Transient | ~1,030 ns | ~1,366 ns | ~2,352 ns | ~12,561 ns | ~56% |
| Resolve Combined | ~980 ns | ~875 ns | ~1,905 ns | ~14,382 ns | ~49% |
| Resolve Complex (6-level) | ~20,874 ns | ~20,828 ns | ~40,270 ns | ~281,814 ns | ~48% |
| Prepare & Register Complex | ~40,613 ns | ~54,996 ns | ~139,246 ns | ~188,865 ns | ~71% |

Latest Windows IL2CPP Player run: `2026-07-12T13:34:55Z`, Unity 2022.3.62f3,
512 warmup iterations, 8 measured samples, 10,000 measured iterations per
sample, arithmetic mean.

| Scenario | Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~18 ns | ~18 ns | ~95 ns | ~449 ns | ~82% |
| Resolve Transient | ~159 ns | ~191 ns | ~541 ns | ~2,448 ns | ~71% |
| Resolve Combined | ~176 ns | ~196 ns | ~612 ns | ~3,080 ns | ~71% |
| Resolve Complex (6-level) | ~5,107 ns | ~5,071 ns | ~12,475 ns | ~59,327 ns | ~59% |
| Prepare & Register Complex | ~21,128 ns | ~24,490 ns | ~34,888 ns | ~59,567 ns | ~39% |

The raw reports retain the historical `Onity (Reflection)` label for the
standard lane. Generic resolves in that lane now use dense type-id provider
slots; reflection is only the activation fallback when no generated or compiled
activator exists.

Focused Windows IL2CPP singleton release gate: `2026-07-12T13:30:25Z`, 512
warmup iterations, 1000 measured samples, 10,000 resolves per sample.

| Onity Standard | Onity Baked | VContainer | Zenject | Lower ns/op vs VContainer |
| ---: | ---: | ---: | ---: | ---: |
| 18.80 ns | 17.40 ns | 94.39 ns | 435.24 ns | 80.1% |

See `Results/di-benchmark-player-singleton-1000.md` for the focused report.

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
- `Benchmarks/Results/di-benchmark-summary.md`

`di-gc-alloc-comparison.png` is generated only when the input report explicitly
marks allocation measurement available. Current Unity 2022.3 Editor and IL2CPP
reports mark it unavailable, so allocation cells are published as `n/a`.

## 4. Publishing Recommendations

- Run benchmark in a dedicated scene and stable environment.
- Disable domain reload changes during benchmark runs.
- Report Unity version, platform, sample count, and iteration count with every chart.
- Keep benchmark code and registration graph identical across frameworks.
