using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Onity.DI;
using UnityEditor;
using UnityEngine;
using VContainer;
using Zenject;

namespace Onity.Editor.Benchmarks
{
    /// <summary>
    /// Runs DI benchmark scenarios for Onity, VContainer, and Zenject.
    /// </summary>
    public static class OnityDiBenchmarkRunner
    {
        private const int k_warmupIterations = 512;
        private const int k_samplesPerCase = 8;
        private const string k_resultsDirectory = "Assets/Onity-Packages/Onity/Benchmarks/Results";
        private const string k_latestJsonFileName = "di-benchmark-latest.json";
        private const string k_latestCsvFileName = "di-benchmark-latest.csv";
        private const string k_latestMarkdownFileName = "di-benchmark-latest.md";

        private static readonly BenchmarkContainerKind[] k_containers =
        {
            BenchmarkContainerKind.Onity,
            BenchmarkContainerKind.VContainer,
            BenchmarkContainerKind.Zenject
        };

        // Onity is measured on both resolve paths so the baked fast path can be
        // compared side by side against the proven reflection path. The other
        // containers expose no such toggle and are measured once.
        private static readonly OnityResolveMode[] k_onityResolveModes =
        {
            OnityResolveMode.Reflection,
            OnityResolveMode.Baked
        };

        // OnityContainer.UseBakedResolve is internal; it is reflected exactly as
        // the parity test suite does so the benchmark can flip the path without a
        // public API. Captured per pass and restored in a finally.
        private static readonly PropertyInfo s_useBakedResolveProperty =
            typeof(OnityContainer).GetProperty(
                "UseBakedResolve",
                BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly ScenarioConfig[] k_scenarios =
        {
            new ScenarioConfig(BenchmarkScenario.ResolveSingleton, "Resolve (Singleton)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveTransient, "Resolve (Transient)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveCombined, "Resolve (Combined)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveComplex, "Resolve (Complex)", 10000),
            new ScenarioConfig(BenchmarkScenario.PrepareAndRegisterComplex, "Prepare & Register (Complex)", 10000)
        };

        [MenuItem("Onity/Benchmarks/Run DI Benchmarks (Editor)")]
        private static void RunBenchmarksFromMenu()
        {
            BenchmarkReport report = RunBenchmarks();
            SaveReport(report);
            AssetDatabase.Refresh();

            string jsonPath = Path.Combine(k_resultsDirectory, k_latestJsonFileName);
            UnityEngine.Debug.Log($"Onity DI benchmark completed. Latest report: {jsonPath}");
        }

        /// <summary>
        /// Command-line entry point for Unity batchmode benchmark generation.
        /// </summary>
        public static void RunBenchmarksFromCommandLine()
        {
            BenchmarkReport report = RunBenchmarks();
            SaveReport(report);
            AssetDatabase.Refresh();

            string jsonPath = Path.Combine(k_resultsDirectory, k_latestJsonFileName);
            UnityEngine.Debug.Log($"Onity DI benchmark completed in batchmode. Latest report: {jsonPath}");
        }

        /// <summary>
        /// Runs all benchmark scenarios and returns report data.
        /// </summary>
        /// <returns>Benchmark report.</returns>
        private static BenchmarkReport RunBenchmarks()
        {
            if (s_useBakedResolveProperty == null)
            {
                throw new InvalidOperationException(
                    "Internal OnityContainer.UseBakedResolve flag was not found; the baked-vs-reflection benchmark cannot toggle the resolve path.");
            }

            BenchmarkReport report = new BenchmarkReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                samplesPerCase = k_samplesPerCase,
                warmupIterations = k_warmupIterations,
                scenarios = new ScenarioReport[k_scenarios.Length]
            };

            List<MetricReport> metrics = new List<MetricReport>(k_containers.Length + 1);

            for (int scenarioIndex = 0; scenarioIndex < k_scenarios.Length; scenarioIndex++)
            {
                ScenarioConfig config = k_scenarios[scenarioIndex];
                metrics.Clear();

                for (int containerIndex = 0; containerIndex < k_containers.Length; containerIndex++)
                {
                    BenchmarkContainerKind containerKind = k_containers[containerIndex];

                    if (containerKind == BenchmarkContainerKind.Onity)
                    {
                        // Onity runs once per resolve mode so the reflection and
                        // baked paths appear as separate, clearly labeled rows.
                        for (int modeIndex = 0; modeIndex < k_onityResolveModes.Length; modeIndex++)
                        {
                            OnityResolveMode resolveMode = k_onityResolveModes[modeIndex];
                            metrics.Add(MeasureScenario(containerKind, config, resolveMode));
                        }
                    }
                    else
                    {
                        metrics.Add(MeasureScenario(containerKind, config, OnityResolveMode.Reflection));
                    }
                }

                report.scenarios[scenarioIndex] = new ScenarioReport
                {
                    scenario = config.scenario.ToString(),
                    displayName = config.displayName,
                    iterationsPerSample = config.iterationsPerSample,
                    results = metrics.ToArray()
                };
            }

            return report;
        }

        private static MetricReport MeasureScenario(
            BenchmarkContainerKind containerKind,
            ScenarioConfig config,
            OnityResolveMode resolveMode)
        {
            bool isOnity = containerKind == BenchmarkContainerKind.Onity;

            // Only Onity flips the internal baked-resolve flag. The flag wraps the
            // entire measurement because CreateOperation builds the container (and
            // its baked graph) inside the sample loop, so Build() must observe it.
            // The original value is restored in the finally below.
            object originalFlag = isOnity ? s_useBakedResolveProperty.GetValue(null) : null;

            try
            {
                if (isOnity)
                {
                    SetUseBakedResolve(resolveMode == OnityResolveMode.Baked);
                }

                double[] elapsedMsSamples = new double[k_samplesPerCase];
                long[] allocSamples = new long[k_samplesPerCase];

                for (int sampleIndex = 0; sampleIndex < k_samplesPerCase; sampleIndex++)
                {
                    using BenchmarkOperation operation = CreateOperation(containerKind, config.scenario);

                    int warmup = Math.Min(k_warmupIterations, config.iterationsPerSample);

                    for (int i = 0; i < warmup; i++)
                    {
                        operation.Invoke();
                    }

                    ForceFullGc();

                    long allocBefore = ReadGrossAllocatedBytes();
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    for (int i = 0; i < config.iterationsPerSample; i++)
                    {
                        operation.Invoke();
                    }

                    stopwatch.Stop();
                    long allocAfter = ReadGrossAllocatedBytes();

                    elapsedMsSamples[sampleIndex] = stopwatch.Elapsed.TotalMilliseconds;

                    // Process-wide gross managed-allocation delta for the measured loop.
                    allocSamples[sampleIndex] = Math.Max(0, allocAfter - allocBefore);
                }

                Stats stats = CalculateStats(elapsedMsSamples);
                Stats allocStats = CalculateStats(allocSamples);

                return new MetricReport
                {
                    container = BuildContainerLabel(containerKind, resolveMode),
                    meanMilliseconds = stats.mean,
                    minMilliseconds = stats.min,
                    maxMilliseconds = stats.max,
                    standardDeviationMilliseconds = stats.standardDeviation,
                    nanosecondsPerOperation = stats.mean * 1000000d / config.iterationsPerSample,
                    allocBytesPerSampleMean = allocStats.mean,
                    allocBytesPerOperationMean = allocStats.mean / config.iterationsPerSample
                };
            }
            finally
            {
                if (isOnity)
                {
                    s_useBakedResolveProperty.SetValue(null, originalFlag);
                }
            }
        }

        private static void SetUseBakedResolve(bool value)
        {
            s_useBakedResolveProperty.SetValue(null, value);
        }

        private static string BuildContainerLabel(BenchmarkContainerKind containerKind, OnityResolveMode resolveMode)
        {
            if (containerKind != BenchmarkContainerKind.Onity)
            {
                return containerKind.ToString();
            }

            return resolveMode == OnityResolveMode.Baked ? "Onity (Baked)" : "Onity (Reflection)";
        }

        private static BenchmarkOperation CreateOperation(BenchmarkContainerKind containerKind, BenchmarkScenario scenario)
        {
            switch (containerKind)
            {
                case BenchmarkContainerKind.Onity:
                    return CreateOnityOperation(scenario);

                case BenchmarkContainerKind.VContainer:
                    return CreateVContainerOperation(scenario);

                case BenchmarkContainerKind.Zenject:
                    return CreateZenjectOperation(scenario);

                default:
                    throw new ArgumentOutOfRangeException(nameof(containerKind), containerKind, "Unknown benchmark container.");
            }
        }

        private static BenchmarkOperation CreateOnityOperation(BenchmarkScenario scenario)
        {
            switch (scenario)
            {
                case BenchmarkScenario.ResolveSingleton:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService service = container.Resolve<IBenchmarkSingletonService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkTransientService service = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService singleton = container.Resolve<IBenchmarkSingletonService>();
                            IBenchmarkTransientService transient = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(singleton);
                            BenchmarkBlackhole.Consume(transient);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterComplexOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IComplexRoot root = container.Resolve<IComplexRoot>();
                            BenchmarkBlackhole.Consume(root);
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                {
                    return new BenchmarkOperation(
                        () =>
                        {
                            using OnityContainer container = new OnityContainer();
                            RegisterComplexOnity(container);
                        });
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown benchmark scenario.");
            }
        }

        private static BenchmarkOperation CreateVContainerOperation(BenchmarkScenario scenario)
        {
            switch (scenario)
            {
                case BenchmarkScenario.ResolveSingleton:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService service = resolver.Resolve<IBenchmarkSingletonService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkTransientService service = resolver.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(service);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService singleton = resolver.Resolve<IBenchmarkSingletonService>();
                            IBenchmarkTransientService transient = resolver.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(singleton);
                            BenchmarkBlackhole.Consume(transient);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    IObjectResolver resolver = BuildComplexVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            IComplexRoot root = resolver.Resolve<IComplexRoot>();
                            BenchmarkBlackhole.Consume(root);
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                {
                    return new BenchmarkOperation(
                        () =>
                        {
                            ContainerBuilder builder = new ContainerBuilder();
                            RegisterComplexVContainer(builder);
                            IObjectResolver resolver = builder.Build();
                            resolver.Dispose();
                        });
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown benchmark scenario.");
            }
        }

        private static BenchmarkOperation CreateZenjectOperation(BenchmarkScenario scenario)
        {
            switch (scenario)
            {
                case BenchmarkScenario.ResolveSingleton:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService service = container.Resolve<IBenchmarkSingletonService>();
                            BenchmarkBlackhole.Consume(service);
                        });
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkTransientService service = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(service);
                        });
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IBenchmarkSingletonService singleton = container.Resolve<IBenchmarkSingletonService>();
                            IBenchmarkTransientService transient = container.Resolve<IBenchmarkTransientService>();
                            BenchmarkBlackhole.Consume(singleton);
                            BenchmarkBlackhole.Consume(transient);
                        });
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    DiContainer container = new DiContainer();
                    RegisterComplexZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            IComplexRoot root = container.Resolve<IComplexRoot>();
                            BenchmarkBlackhole.Consume(root);
                        });
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                {
                    return new BenchmarkOperation(
                        () =>
                        {
                            DiContainer container = new DiContainer();
                            RegisterComplexZenject(container);
                            BenchmarkBlackhole.Consume(container);
                        });
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown benchmark scenario.");
            }
        }

        private static void RegisterSimpleOnity(OnityContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
            // Build commits the registrations and, when UseBakedResolve is true,
            // compiles the baked graph the resolve hot path reads. Required so the
            // baked pass actually exercises the fast path instead of staying on
            // the reflection fallback.
            container.Build();
        }

        private static IObjectResolver BuildSimpleVContainer()
        {
            ContainerBuilder builder = new ContainerBuilder();
            builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(VContainer.Lifetime.Singleton);
            builder.Register<IBenchmarkTransientService, BenchmarkTransientService>(VContainer.Lifetime.Transient);
            return builder.Build();
        }

        private static void RegisterSimpleZenject(DiContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
        }

        private static void RegisterComplexOnity(OnityContainer container)
        {
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();

            container.Bind<ISharedSettings>().To<SharedSettings>().AsSingle();
            container.Bind<ISharedClock>().To<SharedClock>().AsSingle();
            container.Bind<ISharedRandom>().To<SharedRandom>().AsSingle();

            container.Bind<ILeafA>().To<LeafA>().AsTransient();
            container.Bind<ILeafB>().To<LeafB>().AsTransient();
            container.Bind<ILeafC>().To<LeafC>().AsTransient();
            container.Bind<ILeafD>().To<LeafD>().AsTransient();
            container.Bind<ILeafE>().To<LeafE>().AsTransient();
            container.Bind<ILeafF>().To<LeafF>().AsTransient();
            container.Bind<ILeafG>().To<LeafG>().AsTransient();
            container.Bind<ILeafH>().To<LeafH>().AsTransient();

            container.Bind<IComplexServiceA>().To<ComplexServiceA>().AsTransient();
            container.Bind<IComplexServiceB>().To<ComplexServiceB>().AsTransient();
            container.Bind<IComplexServiceC>().To<ComplexServiceC>().AsTransient();
            container.Bind<IComplexServiceD>().To<ComplexServiceD>().AsTransient();
            container.Bind<IComplexServiceE>().To<ComplexServiceE>().AsTransient();
            container.Bind<IComplexRoot>().To<ComplexRoot>().AsTransient();
            // Build commits the registrations and, when UseBakedResolve is true,
            // compiles the baked graph the resolve hot path reads. For the
            // prepare/register scenario this also makes baked-graph construction
            // part of the measured build cost, matching VContainer's builder.Build.
            container.Build();
        }

        private static void RegisterComplexVContainer(IContainerBuilder builder)
        {
            builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(VContainer.Lifetime.Singleton);
            builder.Register<IBenchmarkTransientService, BenchmarkTransientService>(VContainer.Lifetime.Transient);

            builder.Register<ISharedSettings, SharedSettings>(VContainer.Lifetime.Singleton);
            builder.Register<ISharedClock, SharedClock>(VContainer.Lifetime.Singleton);
            builder.Register<ISharedRandom, SharedRandom>(VContainer.Lifetime.Singleton);

            builder.Register<ILeafA, LeafA>(VContainer.Lifetime.Transient);
            builder.Register<ILeafB, LeafB>(VContainer.Lifetime.Transient);
            builder.Register<ILeafC, LeafC>(VContainer.Lifetime.Transient);
            builder.Register<ILeafD, LeafD>(VContainer.Lifetime.Transient);
            builder.Register<ILeafE, LeafE>(VContainer.Lifetime.Transient);
            builder.Register<ILeafF, LeafF>(VContainer.Lifetime.Transient);
            builder.Register<ILeafG, LeafG>(VContainer.Lifetime.Transient);
            builder.Register<ILeafH, LeafH>(VContainer.Lifetime.Transient);

            builder.Register<IComplexServiceA, ComplexServiceA>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceB, ComplexServiceB>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceC, ComplexServiceC>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceD, ComplexServiceD>(VContainer.Lifetime.Transient);
            builder.Register<IComplexServiceE, ComplexServiceE>(VContainer.Lifetime.Transient);
            builder.Register<IComplexRoot, ComplexRoot>(VContainer.Lifetime.Transient);
        }

        private static IObjectResolver BuildComplexVContainer()
        {
            ContainerBuilder builder = new ContainerBuilder();
            RegisterComplexVContainer(builder);
            return builder.Build();
        }

        private static void RegisterComplexZenject(DiContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();

            container.Bind<ISharedSettings>().To<SharedSettings>().AsSingle();
            container.Bind<ISharedClock>().To<SharedClock>().AsSingle();
            container.Bind<ISharedRandom>().To<SharedRandom>().AsSingle();

            container.Bind<ILeafA>().To<LeafA>().AsTransient();
            container.Bind<ILeafB>().To<LeafB>().AsTransient();
            container.Bind<ILeafC>().To<LeafC>().AsTransient();
            container.Bind<ILeafD>().To<LeafD>().AsTransient();
            container.Bind<ILeafE>().To<LeafE>().AsTransient();
            container.Bind<ILeafF>().To<LeafF>().AsTransient();
            container.Bind<ILeafG>().To<LeafG>().AsTransient();
            container.Bind<ILeafH>().To<LeafH>().AsTransient();

            container.Bind<IComplexServiceA>().To<ComplexServiceA>().AsTransient();
            container.Bind<IComplexServiceB>().To<ComplexServiceB>().AsTransient();
            container.Bind<IComplexServiceC>().To<ComplexServiceC>().AsTransient();
            container.Bind<IComplexServiceD>().To<ComplexServiceD>().AsTransient();
            container.Bind<IComplexServiceE>().To<ComplexServiceE>().AsTransient();
            container.Bind<IComplexRoot>().To<ComplexRoot>().AsTransient();
        }

        private static void SaveReport(BenchmarkReport report)
        {
            string absoluteResultsDirectory = Path.Combine(Directory.GetCurrentDirectory(), k_resultsDirectory);
            Directory.CreateDirectory(absoluteResultsDirectory);

            string fileStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string versionedJson = Path.Combine(absoluteResultsDirectory, $"di-benchmark-{fileStamp}.json");
            string latestJson = Path.Combine(absoluteResultsDirectory, k_latestJsonFileName);
            string latestCsv = Path.Combine(absoluteResultsDirectory, k_latestCsvFileName);
            string latestMarkdown = Path.Combine(absoluteResultsDirectory, k_latestMarkdownFileName);

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(versionedJson, json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            File.WriteAllText(latestCsv, BuildCsv(report), Encoding.UTF8);
            File.WriteAllText(latestMarkdown, BuildMarkdown(report), Encoding.UTF8);
        }

        private static string BuildCsv(BenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine(
                "scenario,container,iterations,mean_ms,min_ms,max_ms,stddev_ms,ns_per_op,alloc_bytes_per_sample_mean,alloc_bytes_per_op_mean");

            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                ScenarioReport scenario = report.scenarios[scenarioIndex];

                for (int metricIndex = 0; metricIndex < scenario.results.Length; metricIndex++)
                {
                    MetricReport metric = scenario.results[metricIndex];
                    builder.Append(EscapeCsv(scenario.displayName)).Append(',');
                    builder.Append(EscapeCsv(metric.container)).Append(',');
                    builder.Append(scenario.iterationsPerSample).Append(',');
                    builder.Append(ToInvariant(metric.meanMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.minMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.maxMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.standardDeviationMilliseconds)).Append(',');
                    builder.Append(ToInvariant(metric.nanosecondsPerOperation)).Append(',');
                    builder.Append(ToInvariant(metric.allocBytesPerSampleMean)).Append(',');
                    builder.Append(ToInvariant(metric.allocBytesPerOperationMean)).AppendLine();
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(BenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.AppendLine("# Onity DI Benchmark");
            builder.AppendLine();
            builder.AppendLine($"- Generated (UTC): `{report.generatedAtUtc}`");
            builder.AppendLine($"- Unity: `{report.unityVersion}`");
            builder.AppendLine($"- Platform: `{report.platform}`");
            builder.AppendLine($"- Samples per case: `{report.samplesPerCase}`");
            builder.AppendLine($"- Warmup iterations: `{report.warmupIterations}`");
            builder.AppendLine();
            builder.AppendLine(
                "| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");

            for (int scenarioIndex = 0; scenarioIndex < report.scenarios.Length; scenarioIndex++)
            {
                ScenarioReport scenario = report.scenarios[scenarioIndex];

                for (int metricIndex = 0; metricIndex < scenario.results.Length; metricIndex++)
                {
                    MetricReport metric = scenario.results[metricIndex];
                    builder.Append("| ").Append(scenario.displayName).Append(" | ");
                    builder.Append(metric.container).Append(" | ");
                    builder.Append(metric.meanMilliseconds.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(metric.nanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(metric.allocBytesPerSampleMean.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(metric.allocBytesPerOperationMean.ToString("F6", CultureInfo.InvariantCulture)).AppendLine(" |");
                }
            }

            return builder.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static string ToInvariant(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------------
        // Allocation measurement source
        //
        // Per-op allocation is derived from a GROSS, cumulative managed-allocation
        // counter sampled tightly around the timed loop, divided by the iteration
        // count. The gross counter only ever grows; a full GC before the loop does
        // not reset it, so the delta across the loop is the bytes allocated by the
        // measured work (including objects later collected), which is exactly what
        // a per-op allocation figure should report.
        //
        // Primary source: System.GC.GetTotalAllocatedBytes(precise: true). This is
        // process-wide and cumulative, and is reliable on Unity 2022.3 / .NET
        // Standard 2.1 Mono. It is read on a quiet benchmark thread immediately
        // around the loop, so other-thread noise is negligible at the 10k-iteration
        // scale used here.
        //
        // Why the previous source was wrong: the runner used
        // GC.GetAllocatedBytesForCurrentThread(). On Unity 2022.3 Editor-Mono that
        // API does not return a reliable per-thread allocation total — the
        // committed run read 0 B for EVERY container, including the known
        // allocation-heavy Zenject and the transient-resolve paths that must
        // allocate the instance they return. A flat 0 B across the board means the
        // counter was not tracking, not that the paths allocate nothing, so those
        // figures were withdrawn.
        //
        // GetTotalAllocatedBytes(precise: true) shipped with the .NET Standard 2.1
        // surface Unity 2022.3 targets, but it is invoked through a guarded reflected
        // MethodInfo so that if a given Mono build does not expose it the runner
        // falls back to GetAllocatedBytesForCurrentThread() (documented as unreliable
        // on this backend) rather than failing to compile or throwing.
        // ---------------------------------------------------------------------

        // Cached reflected accessor for GC.GetTotalAllocatedBytes(bool). Resolved
        // once; null when the running Mono build does not expose the method, which
        // routes ReadGrossAllocatedBytes to the documented per-thread fallback.
        private static readonly MethodInfo s_getTotalAllocatedBytesMethod =
            typeof(GC).GetMethod(
                "GetTotalAllocatedBytes",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);

        /// <summary>
        /// Reads the process-wide cumulative gross managed-allocation counter.
        /// Prefers <c>GC.GetTotalAllocatedBytes(precise: true)</c>; falls back to
        /// the per-thread counter (unreliable on Unity 2022.3 Editor-Mono) only
        /// when the preferred method is unavailable on the current runtime.
        /// </summary>
        private static long ReadGrossAllocatedBytes()
        {
            if (s_getTotalAllocatedBytesMethod != null)
            {
                return (long)s_getTotalAllocatedBytesMethod.Invoke(null, s_preciseArgs);
            }

            return GC.GetAllocatedBytesForCurrentThread();
        }

        // Boxed argument reused for the precise gross-allocation read so the
        // reflected call adds no per-sample allocation of its own.
        private static readonly object[] s_preciseArgs = { true };

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static Stats CalculateStats(double[] values)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            double sum = 0d;

            for (int i = 0; i < values.Length; i++)
            {
                double value = values[i];

                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }

                sum += value;
            }

            double mean = values.Length > 0 ? sum / values.Length : 0d;
            double varianceSum = 0d;

            for (int i = 0; i < values.Length; i++)
            {
                double delta = values[i] - mean;
                varianceSum += delta * delta;
            }

            double standardDeviation = values.Length > 1 ? Math.Sqrt(varianceSum / (values.Length - 1)) : 0d;
            return new Stats(mean, min, max, standardDeviation);
        }

        private static Stats CalculateStats(long[] values)
        {
            double[] converted = new double[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                converted[i] = values[i];
            }

            return CalculateStats(converted);
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public int samplesPerCase;
            public int warmupIterations;
            public ScenarioReport[] scenarios;
        }

        [Serializable]
        private sealed class ScenarioReport
        {
            public string scenario;
            public string displayName;
            public int iterationsPerSample;
            public MetricReport[] results;
        }

        [Serializable]
        private sealed class MetricReport
        {
            public string container;
            public double meanMilliseconds;
            public double minMilliseconds;
            public double maxMilliseconds;
            public double standardDeviationMilliseconds;
            public double nanosecondsPerOperation;
            public double allocBytesPerSampleMean;
            public double allocBytesPerOperationMean;
        }

        private readonly struct ScenarioConfig
        {
            public readonly BenchmarkScenario scenario;
            public readonly string displayName;
            public readonly int iterationsPerSample;

            public ScenarioConfig(BenchmarkScenario scenario, string displayName, int iterationsPerSample)
            {
                this.scenario = scenario;
                this.displayName = displayName;
                this.iterationsPerSample = iterationsPerSample;
            }
        }

        private readonly struct Stats
        {
            public readonly double mean;
            public readonly double min;
            public readonly double max;
            public readonly double standardDeviation;

            public Stats(double mean, double min, double max, double standardDeviation)
            {
                this.mean = mean;
                this.min = min;
                this.max = max;
                this.standardDeviation = standardDeviation;
            }
        }

        private enum BenchmarkContainerKind
        {
            Onity = 0,
            VContainer = 1,
            Zenject = 2
        }

        private enum OnityResolveMode
        {
            Reflection = 0,
            Baked = 1
        }

        private enum BenchmarkScenario
        {
            ResolveSingleton = 0,
            ResolveTransient = 1,
            ResolveCombined = 2,
            ResolveComplex = 3,
            PrepareAndRegisterComplex = 4
        }

        private sealed class BenchmarkOperation : IDisposable
        {
            private readonly Action m_disposeAction;

            public Action Invoke { get; }

            public BenchmarkOperation(Action invoke, Action disposeAction = null)
            {
                Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
                m_disposeAction = disposeAction;
            }

            public void Dispose()
            {
                m_disposeAction?.Invoke();
            }
        }

        private static class BenchmarkBlackhole
        {
            private static object s_lastValue;

            public static void Consume(object value)
            {
                s_lastValue = value;
            }
        }

        private interface IBenchmarkSingletonService
        {
        }

        private interface IBenchmarkTransientService
        {
        }

        private sealed class BenchmarkSingletonService : IBenchmarkSingletonService
        {
        }

        private sealed class BenchmarkTransientService : IBenchmarkTransientService
        {
            public BenchmarkTransientService(IBenchmarkSingletonService singletonService)
            {
                BenchmarkBlackhole.Consume(singletonService);
            }
        }

        private interface ISharedSettings
        {
        }

        private interface ISharedClock
        {
        }

        private interface ISharedRandom
        {
        }

        private sealed class SharedSettings : ISharedSettings
        {
        }

        private sealed class SharedClock : ISharedClock
        {
        }

        private sealed class SharedRandom : ISharedRandom
        {
        }

        private interface ILeafA
        {
        }

        private interface ILeafB
        {
        }

        private interface ILeafC
        {
        }

        private interface ILeafD
        {
        }

        private interface ILeafE
        {
        }

        private interface ILeafF
        {
        }

        private interface ILeafG
        {
        }

        private interface ILeafH
        {
        }

        private sealed class LeafA : ILeafA
        {
            public LeafA(ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class LeafB : ILeafB
        {
            public LeafB(ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private sealed class LeafC : ILeafC
        {
            public LeafC(ISharedRandom sharedRandom)
            {
                BenchmarkBlackhole.Consume(sharedRandom);
            }
        }

        private sealed class LeafD : ILeafD
        {
            public LeafD(ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class LeafE : ILeafE
        {
            public LeafE(ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private sealed class LeafF : ILeafF
        {
            public LeafF(ISharedRandom sharedRandom)
            {
                BenchmarkBlackhole.Consume(sharedRandom);
            }
        }

        private sealed class LeafG : ILeafG
        {
            public LeafG(ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class LeafH : ILeafH
        {
            public LeafH(ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private interface IComplexServiceA
        {
        }

        private interface IComplexServiceB
        {
        }

        private interface IComplexServiceC
        {
        }

        private interface IComplexServiceD
        {
        }

        private interface IComplexServiceE
        {
        }

        private interface IComplexRoot
        {
        }

        private sealed class ComplexServiceA : IComplexServiceA
        {
            public ComplexServiceA(ILeafA leafA, ILeafB leafB, ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(leafA);
                BenchmarkBlackhole.Consume(leafB);
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class ComplexServiceB : IComplexServiceB
        {
            public ComplexServiceB(ILeafC leafC, ILeafD leafD, ISharedClock sharedClock)
            {
                BenchmarkBlackhole.Consume(leafC);
                BenchmarkBlackhole.Consume(leafD);
                BenchmarkBlackhole.Consume(sharedClock);
            }
        }

        private sealed class ComplexServiceC : IComplexServiceC
        {
            public ComplexServiceC(ILeafE leafE, ILeafF leafF, ISharedRandom sharedRandom)
            {
                BenchmarkBlackhole.Consume(leafE);
                BenchmarkBlackhole.Consume(leafF);
                BenchmarkBlackhole.Consume(sharedRandom);
            }
        }

        private sealed class ComplexServiceD : IComplexServiceD
        {
            public ComplexServiceD(ILeafG leafG, ILeafH leafH, IComplexServiceA complexServiceA)
            {
                BenchmarkBlackhole.Consume(leafG);
                BenchmarkBlackhole.Consume(leafH);
                BenchmarkBlackhole.Consume(complexServiceA);
            }
        }

        private sealed class ComplexServiceE : IComplexServiceE
        {
            public ComplexServiceE(IComplexServiceB complexServiceB, IComplexServiceC complexServiceC, ISharedSettings sharedSettings)
            {
                BenchmarkBlackhole.Consume(complexServiceB);
                BenchmarkBlackhole.Consume(complexServiceC);
                BenchmarkBlackhole.Consume(sharedSettings);
            }
        }

        private sealed class ComplexRoot : IComplexRoot
        {
            public ComplexRoot(
                IComplexServiceA complexServiceA,
                IComplexServiceB complexServiceB,
                IComplexServiceC complexServiceC,
                IComplexServiceD complexServiceD,
                IComplexServiceE complexServiceE,
                IBenchmarkTransientService benchmarkTransientService)
            {
                BenchmarkBlackhole.Consume(complexServiceA);
                BenchmarkBlackhole.Consume(complexServiceB);
                BenchmarkBlackhole.Consume(complexServiceC);
                BenchmarkBlackhole.Consume(complexServiceD);
                BenchmarkBlackhole.Consume(complexServiceE);
                BenchmarkBlackhole.Consume(benchmarkTransientService);
            }
        }
    }
}
