# Onity Benchmarks

This folder contains benchmark tooling for comparing:
- `Onity`
- `VContainer`
- `Zenject`

## 1. Run DI Benchmarks in Unity

From the Unity menu:
- `Onity/Benchmarks/Run DI Benchmarks (Editor)`

Command line (batchmode) entry point:
- `Onity.Editor.Benchmarks.OnityDiBenchmarkRunner.RunBenchmarksFromCommandLine`

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

When this package is opened from the development project, the full local path is
`Assets/Onity-Packages/Onity/Benchmarks/Results/...`. On GitHub `main`, the
published package copy is under `Packages/com.onity.framework/Benchmarks`.

Scenarios:
- Resolve (Singleton)
- Resolve (Transient)
- Resolve (Combined)
- Resolve (Complex)
- Prepare & Register (Complex)

## 2. Latest Published DI Result

Latest published run: `2026-05-30T18:32:48Z`, Unity 2022.3.62f3, Windows
Editor/Mono, 512 warmup iterations, 8 measured samples, arithmetic mean.

| Scenario | Onity Baked | Onity Reflection | VContainer | Zenject | Onity Baked vs VContainer |
| --- | ---: | ---: | ---: | ---: | ---: |
| Resolve Singleton | ~94 ns | ~215 ns | ~202 ns | ~3,137 ns | ~+53% |
| Resolve Transient | ~775 ns | ~1,725 ns | ~1,697 ns | ~11,681 ns | ~+54% |
| Resolve Combined | ~896 ns | ~1,059 ns | ~1,712 ns | ~15,400 ns | ~+48% |
| Resolve Complex (6-level) | ~22,787 ns | ~26,502 ns | ~57,995 ns | ~285,394 ns | ~+61% |
| Prepare & Register Complex | ~47,243 ns | ~35,837 ns | ~135,140 ns | ~197,132 ns | ~+65% |

These timings are indicative, not a guarantee. They were captured in the Editor
on one machine; hardware, Unity version, scripting backend, and graph shape will
change the absolute numbers. The allocation columns currently emitted by the
runner are not reliable and should not be used for public claims until the
allocation harness is corrected.

IL2CPP note: Onity's Mono/JIT speed path uses compiled activators. On AOT/IL2CPP
that path falls back to reflection so the container runs safely, but IL2CPP
player timings still need a separate benchmark run.

## 3. Render Charts

Install Python chart dependencies:

```bash
pip install matplotlib numpy
```

Generate charts:

```bash
python Assets/Onity-Packages/Onity/Benchmarks/Tools/render_di_benchmark_charts.py \
  --input Assets/Onity-Packages/Onity/Benchmarks/Results/di-benchmark-latest.json \
  --output-dir Assets/Onity-Packages/Onity/Benchmarks/Results
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
