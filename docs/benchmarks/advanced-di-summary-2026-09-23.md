# Advanced DI comparison — 2026-09-23

Unity 2022.3.62f3 on one Windows machine. Onity, VContainer, and Zenject use
the same benchmark contracts. Resolve timings exclude registration, first
activation, scope creation, and disposal. Each timing case has 512 warmups and
eight samples of 10,000 operations. Values below are arithmetic means in ns/op.
The Onity column is the baked resolve path.

| Scenario | Editor Onity | Editor VContainer | Editor Zenject | IL2CPP Onity | IL2CPP VContainer | IL2CPP Zenject |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Singleton | 106 | 210 | 2,773 | 21 | 91 | 463 |
| Transient | 689 | 1,872 | 11,785 | 129 | 529 | 2,333 |
| Combined | 547 | 1,886 | 13,900 | 126 | 588 | 2,901 |
| Complex graph | 12,465 | 37,633 | 272,418 | 3,936 | 13,078 | 61,883 |
| Prepare and register complex graph | 53,284 | 153,133 | 183,996 | 22,575 | 40,919 | 63,623 |
| Keyed singleton | 140 | 252 | 2,951 | 31 | 86 | 574 |
| Scoped singleton | 166 | 300 | 2,819 | 47 | 109 | 563 |

For the keyed case, each framework resolves the same string-identified singleton.
For the scoped case, Onity and VContainer resolve a parent-declared scoped
binding through a warmed child. Zenject resolves an equivalent child-local
singleton because it has no native scoped lifetime. The runner checks singleton
identity and separation between root and child before timing. The prepare case
includes graph registration and build instead of a warmed resolve.

Allocation used a separate raw Unity Profiler pass in Editor/Mono and in a
Windows IL2CPP Development player. Each case used 512 warmups and three samples
of 64 operations. Empty controls recorded 0 B; 1 MiB and 2 MiB payload controls
recorded 1,053,728 B and 2,102,304 B in both environments.

| Scenario | Editor Onity / VContainer / Zenject (B/op) | IL2CPP Onity / VContainer / Zenject (B/op) |
| --- | ---: | ---: |
| Singleton | 0 / 0 / 0 | 0 / 0 / 0 |
| Transient | 16 / 16 / 272 | 16 / 16 / 200 |
| Combined | 16 / 16 / 272 | 16 / 16 / 200 |
| Complex graph | 384 / 384 / 5,780 | 384 / 384 / 4,800 |
| Prepare and register complex graph | 10,364 / 15,296 / 23,166 | 10,424 / 16,136 / 23,405 |
| Keyed singleton | 0 / 0 / 0 | 0 / 0 / 0 |
| Scoped singleton | 0 / 0 / 0 | 0 / 0 / 0 |

Raw evidence: [Editor timing](advanced-di-editor-mono-direct-keyed/di-benchmark-latest.json),
[Editor allocation](advanced-di-editor-allocation/di-allocation-bytes-latest.json),
[IL2CPP release timing](advanced-di-il2cpp-release.json),
[IL2CPP Development allocation](advanced-di-il2cpp-allocation.json), and
[EditMode results](advanced-di-final-full-editmode-results.xml) (464 passed,
0 failed, 0 skipped). The IL2CPP release run registered 19 generated activators.
The temporary `ONITY_BENCHMARKS` Standalone define was removed afterward;
`ProjectSettings.asset` matches its original SHA-256 backup exactly.

These are self-run results on one machine, not a universal speed guarantee.
Conditional injection, rebinding, sub-container exports, and pool operations
have correctness tests; they do not have separate competitor timing cases here.
VContainer has no built-in pool equivalent in this comparison. The competitors
also have longer production and platform histories than Onity.
