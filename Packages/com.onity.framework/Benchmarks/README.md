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
- `Assets/Onity/Benchmarks/Results/di-benchmark-latest.json`
- `Assets/Onity/Benchmarks/Results/di-benchmark-latest.csv`
- `Assets/Onity/Benchmarks/Results/di-benchmark-latest.md`

Scenarios:
- Resolve (Singleton)
- Resolve (Transient)
- Resolve (Combined)
- Resolve (Complex)
- Prepare & Register (Complex)

## 2. Render Charts

Install Python chart dependencies:

```bash
pip install matplotlib numpy
```

Generate charts:

```bash
python Assets/Onity/Benchmarks/Tools/render_di_benchmark_charts.py \
  --input Assets/Onity/Benchmarks/Results/di-benchmark-latest.json \
  --output-dir Assets/Onity/Benchmarks/Results
```

If you are currently inside `Assets/Onity/Benchmarks/Tools`, run:

```bash
python render_di_benchmark_charts.py \
  --input ../Results/di-benchmark-latest.json \
  --output-dir ../Results
```

Generated chart files:
- `Assets/Onity/Benchmarks/Results/di-runtime-comparison.png`
- `Assets/Onity/Benchmarks/Results/di-gc-alloc-comparison.png`
- `Assets/Onity/Benchmarks/Results/di-benchmark-summary.md`

## 3. Publishing Recommendations

- Run benchmark in a dedicated scene and stable environment.
- Disable domain reload changes during benchmark runs.
- Report Unity version, platform, sample count, and iteration count with every chart.
- Keep benchmark code and registration graph identical across frameworks.
