# DI IL2CPP Release Player, published 0.6.0 package (2026-10-02)

- Host: comparison host (public project copy + VContainer 1.17.0 + Zenject), Unity 2022.3.62f2, IL2CPP, Windows x64, Release.
- Package: Onity 0.6.0 runtime (source 4e92db9 = main 6d111dd + define-gated benchmark folders). Generated activators: 19.
- Per process: 512 warmups, 8 samples of 10,000 operations, mean ns/op. Three fresh processes, High priority, affinity excluding cores 0-1. GameAssembly.dll SHA-256 dcf2e30f1bf5f9afc3bff5c871de37187a62933d9f777b9e3a8b2dc8fefb47b9.
- Machine not idle: other Unity Editors open and a desktop cleaner process active; ratios come from the same process.

| Scenario | Onity Baked | Onity Reflection | VContainer | Zenject | Baked/VContainer per process | Baked/Zenject median |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| Resolve (Singleton) | 20.5 | 18.7 | 77.6 | 435 | 0.25 / 0.27 / 0.21 | 0.044 |
| Resolve (Transient) | 151.9 | 133.2 | 518.7 | 2,478 | 0.33 / 0.27 / 0.25 | 0.057 |
| Resolve (Combined) | 161.1 | 150.0 | 614.4 | 2,980 | 0.27 / 0.27 / 0.26 | 0.055 |
| Resolve (Complex) | 5,053.4 | 5,184.1 | 12,944.2 | 64,717 | 0.38 / 0.39 / 0.37 | 0.077 |
| Prepare & Register (Complex) | 31,549.8 | 26,752.5 | 58,001.7 | 91,436 | 0.59 / 0.54 / 0.52 | 0.350 |
| Resolve (Keyed Singleton) | 31.7 | 31.1 | 83.2 | 566 | 0.37 / 0.34 / 0.41 | 0.056 |
| Resolve (Scoped Singleton) | 53.5 | 233.4 | 112.9 | 565 | 0.41 / 0.47 / 0.55 | 0.094 |

Values are medians of the three process means (ns/op). Onity Baked was fastest in every scenario in all three processes.
Raw reports: di-remeasure-il2cpp-p1-2026-10-02.json, di-remeasure-il2cpp-p2-2026-10-02.json and di-remeasure-il2cpp-p3-2026-10-02.json in this directory; Player logs are in the release's di-remeasure-2026-10-02-runs.zip.
