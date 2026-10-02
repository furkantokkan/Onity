using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Onity.DI;
using Onity.DI.Internal;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using VContainer;
using Zenject;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Player-side DI benchmark entry point. Runs only when the player receives
    /// <c>-onityRunDiBenchmark</c>.
    /// </summary>
    public static class OnityDiBenchmarkPlayerRunner
    {
        private const int k_warmupIterations = 512;
        private const int k_samplesPerCase = 8;
        private const int k_hotPathWarmupIterations = 10000;
        private const int k_hotPathSamplesPerCase = 16;
        private const int k_profilerWarmupIterations = 64;
        private const int k_profilerSamplesPerCase = 1;
        private const int k_profilerIterationsPerSample = 10000;
        private const int k_allocationCaptureSamplesPerCase = 3;
        private const int k_allocationCaptureIterations = 64;
        private const int k_allocationProbeCount = 128;
        private const int k_allocationProbeBytes = 8192;
        private const int k_metricsPerScenario = 4;
        private const string k_runArgument = "-onityRunDiBenchmark";
        private const string k_outputArgument = "-onityBenchmarkOutput";
        private const string k_allocationCaptureArgument = "-onityCaptureDiAllocationBytes";
        private const string k_hotPathsOnlyArgument = "-onityBenchmarkHotPathsOnly";
        private const string k_iterationsArgument = "-onityBenchmarkIterations";
        private const string k_samplesArgument = "-onityBenchmarkSamples";
        private const string k_warmupArgument = "-onityBenchmarkWarmup";
        private const string k_scenarioArgument = "-onityBenchmarkScenario";
        private const string k_latestJsonFileName = "di-benchmark-player-latest.json";
        private const string k_latestHotPathJsonFileName = "di-benchmark-player-hot-paths-latest.json";
        private const string k_emptyProbeMarkerName = "Onity.DI.BytesProbe.Empty";
        private const string k_positiveProbeMarkerName = "Onity.DI.BytesProbe.Positive";
        private const string k_doubleProbeMarkerName = "Onity.DI.BytesProbe.Double";
        private const string k_keyedServiceId = "benchmark-service";

        private static readonly ProfilerMarker s_emptyProbeMarker = new ProfilerMarker(k_emptyProbeMarkerName);
        private static readonly ProfilerMarker s_positiveProbeMarker = new ProfilerMarker(k_positiveProbeMarkerName);
        private static readonly ProfilerMarker s_doubleProbeMarker = new ProfilerMarker(k_doubleProbeMarkerName);
        private static bool s_isRunning;
        private static string s_allocationProfilePath;
        private static BenchmarkOperation[] s_allocationOperations;

        internal static int AllocationCaptureStepCount =>
            1 + s_scenarios.Length * k_metricsPerScenario * k_allocationCaptureSamplesPerCase;

        private static readonly BenchmarkContainerKind[] s_containers =
        {
            BenchmarkContainerKind.Onity,
            BenchmarkContainerKind.VContainer,
            BenchmarkContainerKind.Zenject
        };

        private static readonly OnityResolveMode[] s_onityResolveModes =
        {
            OnityResolveMode.Reflection,
            OnityResolveMode.Baked
        };

        private static readonly PropertyInfo s_useBakedResolveProperty =
            typeof(OnityContainer).GetProperty(
                "UseBakedResolve",
                BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly ScenarioConfig[] s_scenarios =
        {
            new ScenarioConfig(BenchmarkScenario.ResolveSingleton, "Resolve (Singleton)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveTransient, "Resolve (Transient)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveCombined, "Resolve (Combined)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveComplex, "Resolve (Complex)", 10000),
            new ScenarioConfig(BenchmarkScenario.PrepareAndRegisterComplex, "Prepare & Register (Complex)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveKeyedSingleton, "Resolve (Keyed Singleton)", 10000),
            new ScenarioConfig(BenchmarkScenario.ResolveScopedSingleton, "Resolve (Scoped Singleton)", 10000)
        };

        private static readonly MethodInfo s_getTotalAllocatedBytesMethod =
            typeof(GC).GetMethod(
                "GetTotalAllocatedBytes",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);

        private static readonly object[] s_preciseArgs = { true };
        private static bool s_generatedOnityActivatorsRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunFromCommandLine()
        {
            RunBenchmarkAndQuit();
        }

        /// <summary>
        /// Runs the player benchmark selected by the command line and quits. Called once at load
        /// and by <see cref="OnityDiBenchmarkPlayerBootstrap" /> in a generated benchmark scene.
        /// </summary>
        public static void RunBenchmarkAndQuit()
        {
            string[] args = Environment.GetCommandLineArgs();

            if (s_isRunning || !HasArgument(args, k_runArgument))
            {
                return;
            }

            s_isRunning = true;

            bool hotPathsOnly = HasArgument(args, k_hotPathsOnlyArgument);
            if (HasArgument(args, k_allocationCaptureArgument))
            {
                if (hotPathsOnly)
                {
                    UnityEngine.Debug.LogError("The hot-path timing option cannot be combined with allocation capture.");
                    Application.Quit(1);
                    return;
                }

                try
                {
                    OnityDiBenchmarkPlayModeDriver.RunAllocationCapture(GetOutputPath(args));
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                    Application.Quit(1);
                }

                return;
            }

            int exitCode = 0;

            try
            {
                string jsonPath = GetOutputPath(args, hotPathsOnly);
                if (hotPathsOnly)
                {
                    SaveReport(RunHotPathBenchmarks(), jsonPath);
                }
                else
                {
                    RunBenchmarksAndSave(jsonPath);
                }
                UnityEngine.Debug.Log($"Onity DI player benchmark completed. Latest report: {jsonPath}");
            }
            catch (Exception exception)
            {
                exitCode = 1;
                UnityEngine.Debug.LogException(exception);
            }
            finally
            {
                Application.Quit(exitCode);
            }
        }

        public static void RunBenchmarksAndSave(string latestJson)
        {
            if (string.IsNullOrEmpty(latestJson))
            {
                throw new ArgumentException("Benchmark output path cannot be null or empty.", nameof(latestJson));
            }

            BenchmarkReport report = RunBenchmarks();
            SaveReport(report, latestJson);
        }

        internal static void StartAllocationCapture(string rawProfilePath)
        {
#if !ENABLE_IL2CPP
            throw new InvalidOperationException("Allocation capture requires an IL2CPP player.");
#endif
            if (!UnityEngine.Debug.isDebugBuild)
            {
                throw new InvalidOperationException("Allocation capture requires a Development Build.");
            }

            if (s_useBakedResolveProperty == null)
            {
                throw new InvalidOperationException("Internal OnityContainer.UseBakedResolve flag was not found.");
            }

            if (string.IsNullOrEmpty(rawProfilePath) ||
                !string.Equals(Path.GetExtension(rawProfilePath), ".raw", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A .raw profiler output path is required.", nameof(rawProfilePath));
            }

            if (s_allocationOperations != null)
            {
                throw new InvalidOperationException("An allocation capture is already active.");
            }

            BenchmarkOperation[] operations = null;

            try
            {
                Profiler.enabled = false;
                RegisterGeneratedOnityActivators();
                operations = WarmupAllocationCases();
                ForceFullGc();
                Directory.CreateDirectory(Path.GetDirectoryName(rawProfilePath));
                s_allocationProfilePath = rawProfilePath;
                Profiler.logFile = rawProfilePath;
                Profiler.maxUsedMemory = 256 * 1024 * 1024;
                Profiler.enableBinaryLog = true;
                Profiler.enabled = true;

                if (!Profiler.enabled || !Profiler.enableBinaryLog)
                {
                    throw new InvalidOperationException("The player profiler did not start binary capture.");
                }

                s_allocationOperations = operations;
            }
            catch
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                Profiler.logFile = string.Empty;
                s_allocationProfilePath = null;
                DisposeAllocationOperations(operations);
                throw;
            }
        }

        internal static void RunAllocationCaptureStep(int step)
        {
            if (step < 0 || step >= AllocationCaptureStepCount)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            BenchmarkOperation[] operations = s_allocationOperations;

            if (operations == null)
            {
                throw new InvalidOperationException("Allocation capture has not started.");
            }

            if (step == 0)
            {
                RunAllocationControls();
                return;
            }

            int caseIndex = (step - 1) / k_allocationCaptureSamplesPerCase;
            ScenarioConfig config = s_scenarios[caseIndex / k_metricsPerScenario];
            int metricIndex = caseIndex % k_metricsPerScenario;
            BenchmarkContainerKind containerKind = metricIndex < 2
                ? BenchmarkContainerKind.Onity
                : metricIndex == 2 ? BenchmarkContainerKind.VContainer : BenchmarkContainerKind.Zenject;
            OnityResolveMode resolveMode = metricIndex == 1
                ? OnityResolveMode.Baked
                : OnityResolveMode.Reflection;
            bool isOnity = containerKind == BenchmarkContainerKind.Onity;
            object originalFlag = isOnity ? s_useBakedResolveProperty.GetValue(null) : null;

            try
            {
                if (isOnity)
                {
                    s_useBakedResolveProperty.SetValue(null, resolveMode == OnityResolveMode.Baked);
                }

                BenchmarkOperation operation = operations[caseIndex];
                ForceFullGc();
                ProfilerMarker marker = new ProfilerMarker(
                    BuildProfilerMarkerName(config, containerKind, resolveMode));
                using (marker.Auto())
                {
                    for (int i = 0; i < k_allocationCaptureIterations; i++)
                    {
                        operation.Invoke();
                    }
                }
            }
            finally
            {
                if (isOnity)
                {
                    s_useBakedResolveProperty.SetValue(null, originalFlag);
                }
            }
        }

        internal static void StopAllocationCapture()
        {
            BenchmarkOperation[] operations = s_allocationOperations;
            s_allocationOperations = null;

            try
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                Profiler.logFile = string.Empty;

                if (string.IsNullOrEmpty(s_allocationProfilePath) ||
                    !File.Exists(s_allocationProfilePath) ||
                    new FileInfo(s_allocationProfilePath).Length == 0)
                {
                    throw new FileNotFoundException(
                        "The player profiler did not write a nonempty .raw capture.",
                        s_allocationProfilePath);
                }

                UnityEngine.Debug.Log($"Onity DI IL2CPP Development allocation capture completed: {s_allocationProfilePath}");
            }
            finally
            {
                s_allocationProfilePath = null;
                DisposeAllocationOperations(operations);
            }
        }

        private static void RunAllocationControls()
        {
            using (s_emptyProbeMarker.Auto())
            {
            }

            using (s_positiveProbeMarker.Auto())
            {
                byte[][] probes = new byte[k_allocationProbeCount][];
                for (int i = 0; i < probes.Length; i++)
                {
                    probes[i] = new byte[k_allocationProbeBytes];
                }

                GC.KeepAlive(probes);
            }

            using (s_doubleProbeMarker.Auto())
            {
                byte[][] probes = new byte[k_allocationProbeCount][];
                for (int i = 0; i < probes.Length; i++)
                {
                    probes[i] = new byte[k_allocationProbeBytes * 2];
                }

                GC.KeepAlive(probes);
            }
        }

        private static BenchmarkOperation[] WarmupAllocationCases()
        {
            BenchmarkOperation[] operations = new BenchmarkOperation[s_scenarios.Length * k_metricsPerScenario];

            try
            {
                for (int scenarioIndex = 0; scenarioIndex < s_scenarios.Length; scenarioIndex++)
                {
                    ScenarioConfig config = s_scenarios[scenarioIndex];
                    for (int metricIndex = 0; metricIndex < k_metricsPerScenario; metricIndex++)
                    {
                        BenchmarkContainerKind containerKind = metricIndex < 2
                            ? BenchmarkContainerKind.Onity
                            : metricIndex == 2 ? BenchmarkContainerKind.VContainer : BenchmarkContainerKind.Zenject;
                        OnityResolveMode resolveMode = metricIndex == 1
                            ? OnityResolveMode.Baked
                            : OnityResolveMode.Reflection;
                        bool isOnity = containerKind == BenchmarkContainerKind.Onity;
                        object originalFlag = isOnity ? s_useBakedResolveProperty.GetValue(null) : null;

                        try
                        {
                            if (isOnity)
                            {
                                s_useBakedResolveProperty.SetValue(null, resolveMode == OnityResolveMode.Baked);
                            }

                            int caseIndex = scenarioIndex * k_metricsPerScenario + metricIndex;
                            BenchmarkOperation operation = CreateOperation(containerKind, config.scenario);
                            operations[caseIndex] = operation;

                            for (int i = 0; i < k_warmupIterations; i++)
                            {
                                operation.Invoke();
                            }
                        }
                        finally
                        {
                            if (isOnity)
                            {
                                s_useBakedResolveProperty.SetValue(null, originalFlag);
                            }
                        }
                    }
                }

                return operations;
            }
            catch
            {
                DisposeAllocationOperations(operations);
                throw;
            }
        }

        private static void DisposeAllocationOperations(BenchmarkOperation[] operations)
        {
            if (operations == null)
            {
                return;
            }

            for (int i = operations.Length - 1; i >= 0; i--)
            {
                operations[i]?.Dispose();
            }
        }

        public static void RunProfilerCaptureAndSave(string latestJson)
        {
            if (string.IsNullOrEmpty(latestJson))
            {
                throw new ArgumentException("Benchmark output path cannot be null or empty.", nameof(latestJson));
            }

            BenchmarkReport report = RunBenchmarks(
                s_scenarios,
                k_profilerSamplesPerCase,
                k_profilerWarmupIterations,
                k_profilerIterationsPerSample);

            SaveReport(report, latestJson);
        }

        private static BenchmarkReport RunHotPathBenchmarks()
        {
            if (s_useBakedResolveProperty == null)
            {
                throw new InvalidOperationException("Internal OnityContainer.UseBakedResolve flag was not found.");
            }

            RegisterGeneratedOnityActivators();
            ScenarioConfig[] configs =
            {
                new ScenarioConfig(BenchmarkScenario.ResolveSingleton, "Resolve (Singleton)", 1000000),
                new ScenarioConfig(BenchmarkScenario.ResolveTransient, "Resolve (Transient)", 100000),
                new ScenarioConfig(BenchmarkScenario.ResolveCombined, "Resolve (Combined)", 100000),
                new ScenarioConfig(BenchmarkScenario.ResolveComplex, "Resolve (Complex)", 10000)
            };
            BenchmarkReport report = new BenchmarkReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                scriptingBackend = GetScriptingBackendLabel(),
                compiledActivationSupported = OnityContainer.IsCompiledActivationSupported,
                generatedActivatorsRegistered = GeneratedActivators.RegisteredCount,
                allocationMeasurementAvailable = false,
                scope = "Onity Baked hot paths, interleaved",
                samplesPerCase = k_hotPathSamplesPerCase,
                warmupIterations = k_hotPathWarmupIterations,
                scenarios = new ScenarioReport[configs.Length]
            };
            BenchmarkOperation[] operations = new BenchmarkOperation[configs.Length];
            double[][] samples = new double[configs.Length][];
            int[][] gen0Collections = new int[configs.Length][];
            int[][] gen1Collections = new int[configs.Length][];
            int[][] gen2Collections = new int[configs.Length][];
            ProfilerMarker[] markers = new ProfilerMarker[configs.Length];
            bool originalBakedResolve = (bool)s_useBakedResolveProperty.GetValue(null);
            bool originalDiagnostics = OnityContainer.DiagnosticsCollectionEnabled;

            try
            {
                s_useBakedResolveProperty.SetValue(null, true);
                OnityContainer.DiagnosticsCollectionEnabled = false;

                for (int caseIndex = 0; caseIndex < configs.Length; caseIndex++)
                {
                    operations[caseIndex] = CreateOperation(BenchmarkContainerKind.Onity, configs[caseIndex].scenario);
                    samples[caseIndex] = new double[k_hotPathSamplesPerCase];
                    gen0Collections[caseIndex] = new int[k_hotPathSamplesPerCase];
                    gen1Collections[caseIndex] = new int[k_hotPathSamplesPerCase];
                    gen2Collections[caseIndex] = new int[k_hotPathSamplesPerCase];
                    markers[caseIndex] = new ProfilerMarker(
                        BuildProfilerMarkerName(configs[caseIndex], BenchmarkContainerKind.Onity, OnityResolveMode.Baked));

                    Action invoke = operations[caseIndex].Invoke;
                    for (int i = 0; i < k_hotPathWarmupIterations; i++)
                    {
                        invoke();
                    }
                }

                ForceFullGc();

                for (int sample = 0; sample < k_hotPathSamplesPerCase; sample++)
                {
                    for (int slot = 0; slot < configs.Length; slot++)
                    {
                        int caseIndex = (slot + sample) % configs.Length;
                        Action invoke = operations[caseIndex].Invoke;
                        int iterations = configs[caseIndex].iterationsPerSample;
                        int gen0Before = GC.CollectionCount(0);
                        int gen1Before = GC.CollectionCount(1);
                        int gen2Before = GC.CollectionCount(2);
                        long elapsedTicks;

                        using (markers[caseIndex].Auto())
                        {
                            long start = Stopwatch.GetTimestamp();
                            for (int i = 0; i < iterations; i++)
                            {
                                invoke();
                            }
                            elapsedTicks = Stopwatch.GetTimestamp() - start;
                        }

                        samples[caseIndex][sample] = elapsedTicks * 1000000000d /
                            (Stopwatch.Frequency * iterations);
                        gen0Collections[caseIndex][sample] = GC.CollectionCount(0) - gen0Before;
                        gen1Collections[caseIndex][sample] = GC.CollectionCount(1) - gen1Before;
                        gen2Collections[caseIndex][sample] = GC.CollectionCount(2) - gen2Before;
                    }
                }

                for (int caseIndex = 0; caseIndex < configs.Length; caseIndex++)
                {
                    double[] sorted = (double[])samples[caseIndex].Clone();
                    Array.Sort(sorted);
                    Stats stats = CalculateStats(samples[caseIndex]);
                    int iterations = configs[caseIndex].iterationsPerSample;
                    MetricReport metric = new MetricReport
                    {
                        container = BuildContainerLabel(BenchmarkContainerKind.Onity, OnityResolveMode.Baked),
                        meanMilliseconds = stats.mean * iterations / 1000000d,
                        minMilliseconds = stats.min * iterations / 1000000d,
                        maxMilliseconds = stats.max * iterations / 1000000d,
                        standardDeviationMilliseconds = stats.standardDeviation * iterations / 1000000d,
                        nanosecondsPerOperation = stats.mean,
                        allocBytesPerSampleMean = -1d,
                        allocBytesPerOperationMean = -1d,
                        medianNanosecondsPerOperation = MedianOfSortedRange(sorted, 0, sorted.Length),
                        q1NanosecondsPerOperation = MedianOfSortedRange(sorted, 0, sorted.Length / 2),
                        q3NanosecondsPerOperation = MedianOfSortedRange(sorted, sorted.Length / 2, sorted.Length / 2),
                        sampleNanosecondsPerOperation = samples[caseIndex],
                        gen0CollectionsPerSample = gen0Collections[caseIndex],
                        gen1CollectionsPerSample = gen1Collections[caseIndex],
                        gen2CollectionsPerSample = gen2Collections[caseIndex]
                    };
                    report.scenarios[caseIndex] = new ScenarioReport
                    {
                        scenario = configs[caseIndex].scenario.ToString(),
                        displayName = configs[caseIndex].displayName,
                        iterationsPerSample = iterations,
                        results = new[] { metric }
                    };
                }

                return report;
            }
            finally
            {
                try
                {
                    for (int i = 0; i < operations.Length; i++)
                    {
                        operations[i]?.Dispose();
                    }
                }
                finally
                {
                    OnityContainer.DiagnosticsCollectionEnabled = originalDiagnostics;
                    s_useBakedResolveProperty.SetValue(null, originalBakedResolve);
                }
            }
        }

        private static double MedianOfSortedRange(double[] sorted, int start, int length)
        {
            int middle = start + length / 2;
            return (sorted[middle - 1] + sorted[middle]) / 2d;
        }

        private static BenchmarkReport RunBenchmarks()
        {
            // Optional player arguments narrow or resize the full comparison; without
            // them (and in the Editor) the default protocol runs unchanged.
            string[] args = Environment.GetCommandLineArgs();
            return RunBenchmarks(
                SelectScenarios(
                    GetPositiveIntArgument(args, k_iterationsArgument, 0),
                    GetArgumentValue(args, k_scenarioArgument)),
                GetPositiveIntArgument(args, k_samplesArgument, k_samplesPerCase),
                GetPositiveIntArgument(args, k_warmupArgument, k_warmupIterations),
                int.MaxValue);
        }

        private static ScenarioConfig[] SelectScenarios(int iterationsPerSample, string scenarioFilter)
        {
            ScenarioConfig[] scenarios = new ScenarioConfig[s_scenarios.Length];
            for (int i = 0; i < scenarios.Length; i++)
            {
                ScenarioConfig config = s_scenarios[i];
                scenarios[i] = iterationsPerSample > 0
                    ? new ScenarioConfig(config.scenario, config.displayName, iterationsPerSample)
                    : config;
            }

            if (string.IsNullOrWhiteSpace(scenarioFilter))
            {
                return scenarios;
            }

            if (Enum.TryParse(scenarioFilter, true, out BenchmarkScenario selectedScenario) == false)
            {
                throw new ArgumentException(
                    $"Unknown benchmark scenario '{scenarioFilter}'. Use a {nameof(BenchmarkScenario)} enum name.",
                    nameof(scenarioFilter));
            }

            for (int i = 0; i < scenarios.Length; i++)
            {
                if (scenarios[i].scenario == selectedScenario)
                {
                    return new[] { scenarios[i] };
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(scenarioFilter), scenarioFilter, "Benchmark scenario is not configured.");
        }

        private static int GetPositiveIntArgument(string[] args, string argument, int fallback)
        {
            string value = GetArgumentValue(args, argument);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed
                : fallback;
        }

        private static BenchmarkReport RunBenchmarks(
            ScenarioConfig[] scenarios,
            int samplesPerCase,
            int warmupIterations,
            int maxIterationsPerSample)
        {
            if (s_useBakedResolveProperty == null)
            {
                throw new InvalidOperationException(
                    "Internal OnityContainer.UseBakedResolve flag was not found; the baked-vs-reflection player benchmark cannot toggle the resolve path.");
            }

            RegisterGeneratedOnityActivators();

            BenchmarkReport report = new BenchmarkReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                scriptingBackend = GetScriptingBackendLabel(),
                compiledActivationSupported = OnityContainer.IsCompiledActivationSupported,
                generatedActivatorsRegistered = GeneratedActivators.RegisteredCount,
                allocationMeasurementAvailable = IsAllocationMeasurementAvailable(),
                scope = "Full comparison",
                samplesPerCase = samplesPerCase,
                warmupIterations = warmupIterations,
                scenarios = new ScenarioReport[scenarios.Length]
            };

            MetricReport[] metrics = new MetricReport[s_containers.Length + 1];

            for (int scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
            {
                ScenarioConfig config = ClampScenarioIterations(scenarios[scenarioIndex], maxIterationsPerSample);
                int metricCount = 0;

                for (int containerIndex = 0; containerIndex < s_containers.Length; containerIndex++)
                {
                    BenchmarkContainerKind containerKind = s_containers[containerIndex];

                    if (containerKind == BenchmarkContainerKind.Onity)
                    {
                        for (int modeIndex = 0; modeIndex < s_onityResolveModes.Length; modeIndex++)
                        {
                            metrics[metricCount] = MeasureScenario(
                                containerKind,
                                config,
                                s_onityResolveModes[modeIndex],
                                samplesPerCase,
                                warmupIterations);
                            metricCount++;
                        }
                    }
                    else
                    {
                        metrics[metricCount] = MeasureScenario(
                            containerKind,
                            config,
                            OnityResolveMode.Reflection,
                            samplesPerCase,
                            warmupIterations);
                        metricCount++;
                    }
                }

                MetricReport[] scenarioMetrics = new MetricReport[metricCount];
                Array.Copy(metrics, scenarioMetrics, metricCount);

                report.scenarios[scenarioIndex] = new ScenarioReport
                {
                    scenario = config.scenario.ToString(),
                    displayName = config.displayName,
                    iterationsPerSample = config.iterationsPerSample,
                    results = scenarioMetrics
                };
            }

            return report;
        }

        private static ScenarioConfig ClampScenarioIterations(ScenarioConfig config, int maxIterationsPerSample)
        {
            if (maxIterationsPerSample <= 0 || maxIterationsPerSample >= config.iterationsPerSample)
            {
                return config;
            }

            return new ScenarioConfig(config.scenario, config.displayName, maxIterationsPerSample);
        }

        private static MetricReport MeasureScenario(
            BenchmarkContainerKind containerKind,
            ScenarioConfig config,
            OnityResolveMode resolveMode,
            int samplesPerCase,
            int warmupIterations)
        {
            bool isOnity = containerKind == BenchmarkContainerKind.Onity;
            object originalFlag = isOnity ? s_useBakedResolveProperty.GetValue(null) : null;

            try
            {
                if (isOnity)
                {
                    s_useBakedResolveProperty.SetValue(null, resolveMode == OnityResolveMode.Baked);
                }

                double[] elapsedMsSamples = new double[samplesPerCase];
                long[] allocSamples = new long[samplesPerCase];
                ProfilerMarker measuredLoopMarker = new ProfilerMarker(
                    BuildProfilerMarkerName(config, containerKind, resolveMode));

                for (int sampleIndex = 0; sampleIndex < samplesPerCase; sampleIndex++)
                {
                    using BenchmarkOperation operation = CreateOperation(containerKind, config.scenario);
                    int warmup = Math.Min(warmupIterations, config.iterationsPerSample);

                    for (int i = 0; i < warmup; i++)
                    {
                        operation.Invoke();
                    }

                    ForceFullGc();

                    Stopwatch stopwatch = new Stopwatch();
                    long allocBefore = ReadGrossAllocatedBytes();

                    using (measuredLoopMarker.Auto())
                    {
                        stopwatch.Restart();

                        for (int i = 0; i < config.iterationsPerSample; i++)
                        {
                            operation.Invoke();
                        }

                        stopwatch.Stop();
                    }

                    long allocAfter = ReadGrossAllocatedBytes();

                    elapsedMsSamples[sampleIndex] = stopwatch.Elapsed.TotalMilliseconds;
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
                        () => BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkSingletonService>()),
                        container.Dispose);
                }
                case BenchmarkScenario.ResolveKeyedSingleton:
                {
                    OnityContainer container = new OnityContainer();
                    container.Bind<IBenchmarkSingletonService>()
                        .To<BenchmarkSingletonService>().WithId(k_keyedServiceId).AsSingle();
                    container.Build();
                    ValidateSingleton(container.Resolve<IBenchmarkSingletonService>(k_keyedServiceId),
                        container.Resolve<IBenchmarkSingletonService>(k_keyedServiceId));
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(
                            container.Resolve<IBenchmarkSingletonService>(k_keyedServiceId)),
                        container.Dispose);
                }
                case BenchmarkScenario.ResolveScopedSingleton:
                {
                    OnityContainer root = new OnityContainer();
                    root.Bind<IBenchmarkSingletonService>()
                        .To<BenchmarkSingletonService>().AsScoped();
                    root.Build();
                    OnityContainer scope = new OnityContainer(root);
                    scope.Build();
                    ValidateSingleton(scope.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    ValidateSeparateScopes(root.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(scope.Resolve<IBenchmarkSingletonService>()),
                        () => { scope.Dispose(); root.Dispose(); });
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkTransientService>()),
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterSimpleOnity(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkSingletonService>());
                            BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkTransientService>());
                        },
                        container.Dispose);
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    OnityContainer container = new OnityContainer();
                    RegisterComplexOnity(container);
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(container.Resolve<IComplexRoot>()),
                        container.Dispose);
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                    return new BenchmarkOperation(
                        () =>
                        {
                            using OnityContainer container = new OnityContainer();
                            RegisterComplexOnity(container);
                        });

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
                        () => BenchmarkBlackhole.Consume(resolver.Resolve<IBenchmarkSingletonService>()),
                        resolver.Dispose);
                }
                case BenchmarkScenario.ResolveKeyedSingleton:
                {
                    ContainerBuilder builder = new ContainerBuilder();
                    builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(
                        VContainer.Lifetime.Singleton).Keyed(k_keyedServiceId);
                    IObjectResolver resolver = builder.Build();
                    ValidateSingleton(resolver.Resolve<IBenchmarkSingletonService>(k_keyedServiceId),
                        resolver.Resolve<IBenchmarkSingletonService>(k_keyedServiceId));
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(
                            resolver.Resolve<IBenchmarkSingletonService>(k_keyedServiceId)),
                        resolver.Dispose);
                }
                case BenchmarkScenario.ResolveScopedSingleton:
                {
                    ContainerBuilder builder = new ContainerBuilder();
                    builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(
                        VContainer.Lifetime.Scoped);
                    IObjectResolver root = builder.Build();
                    IObjectResolver scope = root.CreateScope();
                    ValidateSingleton(scope.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    ValidateSeparateScopes(root.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(scope.Resolve<IBenchmarkSingletonService>()),
                        () => { scope.Dispose(); root.Dispose(); });
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(resolver.Resolve<IBenchmarkTransientService>()),
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    IObjectResolver resolver = BuildSimpleVContainer();
                    return new BenchmarkOperation(
                        () =>
                        {
                            BenchmarkBlackhole.Consume(resolver.Resolve<IBenchmarkSingletonService>());
                            BenchmarkBlackhole.Consume(resolver.Resolve<IBenchmarkTransientService>());
                        },
                        resolver.Dispose);
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    IObjectResolver resolver = BuildComplexVContainer();
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(resolver.Resolve<IComplexRoot>()),
                        resolver.Dispose);
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                    return new BenchmarkOperation(
                        () =>
                        {
                            ContainerBuilder builder = new ContainerBuilder();
                            RegisterComplexVContainer(builder);
                            IObjectResolver resolver = builder.Build();
                            resolver.Dispose();
                        });

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
                        () => BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkSingletonService>()));
                }
                case BenchmarkScenario.ResolveKeyedSingleton:
                {
                    DiContainer container = new DiContainer();
                    container.Bind<IBenchmarkSingletonService>().WithId(k_keyedServiceId)
                        .To<BenchmarkSingletonService>().AsSingle();
                    ValidateSingleton(container.ResolveId<IBenchmarkSingletonService>(k_keyedServiceId),
                        container.ResolveId<IBenchmarkSingletonService>(k_keyedServiceId));
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(
                            container.ResolveId<IBenchmarkSingletonService>(k_keyedServiceId)));
                }
                case BenchmarkScenario.ResolveScopedSingleton:
                {
                    DiContainer root = new DiContainer();
                    root.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
                    DiContainer scope = root.CreateSubContainer();
                    scope.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
                    ValidateSingleton(scope.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    ValidateSeparateScopes(root.Resolve<IBenchmarkSingletonService>(),
                        scope.Resolve<IBenchmarkSingletonService>());
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(scope.Resolve<IBenchmarkSingletonService>()));
                }

                case BenchmarkScenario.ResolveTransient:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkTransientService>()));
                }

                case BenchmarkScenario.ResolveCombined:
                {
                    DiContainer container = new DiContainer();
                    RegisterSimpleZenject(container);
                    return new BenchmarkOperation(
                        () =>
                        {
                            BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkSingletonService>());
                            BenchmarkBlackhole.Consume(container.Resolve<IBenchmarkTransientService>());
                        });
                }

                case BenchmarkScenario.ResolveComplex:
                {
                    DiContainer container = new DiContainer();
                    RegisterComplexZenject(container);
                    return new BenchmarkOperation(
                        () => BenchmarkBlackhole.Consume(container.Resolve<IComplexRoot>()));
                }

                case BenchmarkScenario.PrepareAndRegisterComplex:
                    return new BenchmarkOperation(
                        () =>
                        {
                            DiContainer container = new DiContainer();
                            RegisterComplexZenject(container);
                            BenchmarkBlackhole.Consume(container);
                        });

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown benchmark scenario.");
            }
        }

        private static void ValidateSingleton(object first, object second)
        {
            if (!ReferenceEquals(first, second))
            {
                throw new InvalidOperationException("Benchmark registration did not reuse the singleton.");
            }
        }

        private static void ValidateSeparateScopes(object first, object second)
        {
            if (ReferenceEquals(first, second))
            {
                throw new InvalidOperationException("Benchmark scopes shared an instance.");
            }
        }

        private static void RegisterSimpleOnity(OnityContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
            container.Build();
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
            container.Build();
        }

        private static void RegisterGeneratedOnityActivators()
        {
            if (s_generatedOnityActivatorsRegistered)
            {
                return;
            }

            GeneratedActivators.Register(
                typeof(BenchmarkSingletonService),
                Type.EmptyTypes,
                args => new BenchmarkSingletonService());
            GeneratedActivators.Register(
                typeof(BenchmarkTransientService),
                new[] { typeof(IBenchmarkSingletonService) },
                args => new BenchmarkTransientService((IBenchmarkSingletonService)args[0]));

            GeneratedActivators.Register(
                typeof(SharedSettings),
                Type.EmptyTypes,
                args => new SharedSettings());
            GeneratedActivators.Register(
                typeof(SharedClock),
                Type.EmptyTypes,
                args => new SharedClock());
            GeneratedActivators.Register(
                typeof(SharedRandom),
                Type.EmptyTypes,
                args => new SharedRandom());

            GeneratedActivators.Register(
                typeof(LeafA),
                new[] { typeof(ISharedSettings) },
                args => new LeafA((ISharedSettings)args[0]));
            GeneratedActivators.Register(
                typeof(LeafB),
                new[] { typeof(ISharedClock) },
                args => new LeafB((ISharedClock)args[0]));
            GeneratedActivators.Register(
                typeof(LeafC),
                new[] { typeof(ISharedRandom) },
                args => new LeafC((ISharedRandom)args[0]));
            GeneratedActivators.Register(
                typeof(LeafD),
                new[] { typeof(ISharedSettings) },
                args => new LeafD((ISharedSettings)args[0]));
            GeneratedActivators.Register(
                typeof(LeafE),
                new[] { typeof(ISharedClock) },
                args => new LeafE((ISharedClock)args[0]));
            GeneratedActivators.Register(
                typeof(LeafF),
                new[] { typeof(ISharedRandom) },
                args => new LeafF((ISharedRandom)args[0]));
            GeneratedActivators.Register(
                typeof(LeafG),
                new[] { typeof(ISharedSettings) },
                args => new LeafG((ISharedSettings)args[0]));
            GeneratedActivators.Register(
                typeof(LeafH),
                new[] { typeof(ISharedClock) },
                args => new LeafH((ISharedClock)args[0]));

            GeneratedActivators.Register(
                typeof(ComplexServiceA),
                new[] { typeof(ILeafA), typeof(ILeafB), typeof(ISharedSettings) },
                args => new ComplexServiceA((ILeafA)args[0], (ILeafB)args[1], (ISharedSettings)args[2]));
            GeneratedActivators.Register(
                typeof(ComplexServiceB),
                new[] { typeof(ILeafC), typeof(ILeafD), typeof(ISharedClock) },
                args => new ComplexServiceB((ILeafC)args[0], (ILeafD)args[1], (ISharedClock)args[2]));
            GeneratedActivators.Register(
                typeof(ComplexServiceC),
                new[] { typeof(ILeafE), typeof(ILeafF), typeof(ISharedRandom) },
                args => new ComplexServiceC((ILeafE)args[0], (ILeafF)args[1], (ISharedRandom)args[2]));
            GeneratedActivators.Register(
                typeof(ComplexServiceD),
                new[] { typeof(ILeafG), typeof(ILeafH), typeof(IComplexServiceA) },
                args => new ComplexServiceD((ILeafG)args[0], (ILeafH)args[1], (IComplexServiceA)args[2]));
            GeneratedActivators.Register(
                typeof(ComplexServiceE),
                new[] { typeof(IComplexServiceB), typeof(IComplexServiceC), typeof(ISharedSettings) },
                args => new ComplexServiceE((IComplexServiceB)args[0], (IComplexServiceC)args[1], (ISharedSettings)args[2]));
            GeneratedActivators.Register(
                typeof(ComplexRoot),
                new[]
                {
                    typeof(IComplexServiceA),
                    typeof(IComplexServiceB),
                    typeof(IComplexServiceC),
                    typeof(IComplexServiceD),
                    typeof(IComplexServiceE),
                    typeof(IBenchmarkTransientService)
                },
                args => new ComplexRoot(
                    (IComplexServiceA)args[0],
                    (IComplexServiceB)args[1],
                    (IComplexServiceC)args[2],
                    (IComplexServiceD)args[3],
                    (IComplexServiceE)args[4],
                    (IBenchmarkTransientService)args[5]));

            s_generatedOnityActivatorsRegistered = true;
        }

        private static IObjectResolver BuildSimpleVContainer()
        {
            ContainerBuilder builder = new ContainerBuilder();
            builder.Register<IBenchmarkSingletonService, BenchmarkSingletonService>(VContainer.Lifetime.Singleton);
            builder.Register<IBenchmarkTransientService, BenchmarkTransientService>(VContainer.Lifetime.Transient);
            return builder.Build();
        }

        private static IObjectResolver BuildComplexVContainer()
        {
            ContainerBuilder builder = new ContainerBuilder();
            RegisterComplexVContainer(builder);
            return builder.Build();
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

        private static void RegisterSimpleZenject(DiContainer container)
        {
            container.Bind<IBenchmarkSingletonService>().To<BenchmarkSingletonService>().AsSingle();
            container.Bind<IBenchmarkTransientService>().To<BenchmarkTransientService>().AsTransient();
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

        private static void SaveReport(BenchmarkReport report, string latestJson)
        {
            string directory = Path.GetDirectoryName(latestJson);
            Directory.CreateDirectory(directory);

            string fileStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string versionedJson = Path.Combine(directory,
                report.scope == "Onity Baked hot paths, interleaved"
                    ? $"di-benchmark-player-hot-paths-{fileStamp}.json"
                    : $"di-benchmark-player-{fileStamp}.json");
            string latestCsv = Path.ChangeExtension(latestJson, ".csv");
            string latestMarkdown = Path.ChangeExtension(latestJson, ".md");

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(versionedJson, json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            File.WriteAllText(latestCsv, BuildCsv(report), Encoding.UTF8);
            File.WriteAllText(latestMarkdown, BuildMarkdown(report), Encoding.UTF8);
        }

        private static string BuildCsv(BenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine("scenario,container,iterations,mean_ms,min_ms,max_ms,stddev_ms,ns_per_op,alloc_bytes_per_sample_mean,alloc_bytes_per_op_mean");

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
                    builder.Append(FormatAllocationMetric(report, metric.allocBytesPerSampleMean, "G17")).Append(',');
                    builder.Append(FormatAllocationMetric(report, metric.allocBytesPerOperationMean, "G17")).AppendLine();
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(BenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.AppendLine("# Onity DI Player Benchmark");
            builder.AppendLine();
            builder.AppendLine($"- Generated (UTC): `{report.generatedAtUtc}`");
            builder.AppendLine($"- Unity: `{report.unityVersion}`");
            builder.AppendLine($"- Platform: `{report.platform}`");
            builder.AppendLine($"- Scripting backend: `{report.scriptingBackend}`");
            builder.AppendLine($"- Scope: `{report.scope}`");
            builder.AppendLine($"- Compiled activation supported: `{report.compiledActivationSupported}`");
            builder.AppendLine($"- Generated activators registered: `{report.generatedActivatorsRegistered}`");
            builder.AppendLine($"- Allocation measurement available: `{report.allocationMeasurementAvailable}`");
            builder.AppendLine($"- Samples per case: `{report.samplesPerCase}`");
            builder.AppendLine($"- Warmup iterations: `{report.warmupIterations}`");
            builder.AppendLine();
            builder.AppendLine("| Scenario | Container | Mean (ms) | ns/op | Alloc/sample (B) | Alloc/op (B) |");
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
                    builder.Append(FormatAllocationMetric(report, metric.allocBytesPerSampleMean, "F2")).Append(" | ");
                    builder.Append(FormatAllocationMetric(report, metric.allocBytesPerOperationMean, "F6")).AppendLine(" |");
                }
            }

            if (report.scope == "Onity Baked hot paths, interleaved")
            {
                AppendHotPathMarkdown(builder, report);
            }

            return builder.ToString();
        }

        private static void AppendHotPathMarkdown(StringBuilder builder, BenchmarkReport report)
        {
            builder.AppendLine();
            builder.AppendLine("| Scenario | Median (ns/op) | Q1–Q3 (ns/op) | Range (ns/op) | Samples with GC |");
            builder.AppendLine("|---|---:|---:|---:|---:|");

            for (int i = 0; i < report.scenarios.Length; i++)
            {
                ScenarioReport scenario = report.scenarios[i];
                MetricReport metric = scenario.results[0];
                int gcSamples = 0;
                for (int sample = 0; sample < metric.sampleNanosecondsPerOperation.Length; sample++)
                {
                    if (metric.gen0CollectionsPerSample[sample] > 0 ||
                        metric.gen1CollectionsPerSample[sample] > 0 ||
                        metric.gen2CollectionsPerSample[sample] > 0)
                    {
                        gcSamples++;
                    }
                }

                builder.Append("| ").Append(scenario.displayName).Append(" | ");
                builder.Append(metric.medianNanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" | ");
                builder.Append(metric.q1NanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture))
                    .Append("–")
                    .Append(metric.q3NanosecondsPerOperation.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" | ");
                builder.Append((metric.minMilliseconds * 1000000d / scenario.iterationsPerSample)
                    .ToString("F2", CultureInfo.InvariantCulture)).Append("–");
                builder.Append((metric.maxMilliseconds * 1000000d / scenario.iterationsPerSample)
                    .ToString("F2", CultureInfo.InvariantCulture)).Append(" | ");
                builder.Append(gcSamples).Append("/").Append(report.samplesPerCase).AppendLine(" |");
            }
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

        private static string GetOutputPath(string[] args, bool hotPathsOnly = false)
        {
            string explicitOutput = GetArgumentValue(args, k_outputArgument);

            if (!string.IsNullOrEmpty(explicitOutput))
            {
                return Path.GetFullPath(explicitOutput);
            }

            return Path.Combine(Application.persistentDataPath,
                hotPathsOnly ? k_latestHotPathJsonFileName : k_latestJsonFileName);
        }

        private static bool HasArgument(string[] args, string argument)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], argument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetArgumentValue(string[] args, string argument)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argument, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static string BuildContainerLabel(BenchmarkContainerKind containerKind, OnityResolveMode resolveMode)
        {
            if (containerKind != BenchmarkContainerKind.Onity)
            {
                return containerKind.ToString();
            }

            return resolveMode == OnityResolveMode.Baked ? "Onity (Baked)" : "Onity (Reflection)";
        }

        private static string BuildProfilerMarkerName(
            ScenarioConfig config,
            BenchmarkContainerKind containerKind,
            OnityResolveMode resolveMode)
        {
            string containerLabel = BuildContainerLabel(containerKind, resolveMode)
                .Replace(" ", string.Empty)
                .Replace("(", string.Empty)
                .Replace(")", string.Empty);

            return $"Onity.DI.Benchmark/{config.scenario}/{containerLabel}";
        }

        private static string GetScriptingBackendLabel()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }

        private static bool IsAllocationMeasurementAvailable()
        {
#if ENABLE_IL2CPP
            return false;
#else
            return true;
#endif
        }

        private static long ReadGrossAllocatedBytes()
        {
#if ENABLE_IL2CPP
            return 0L;
#else
            if (s_getTotalAllocatedBytesMethod != null)
            {
                return (long)s_getTotalAllocatedBytesMethod.Invoke(null, s_preciseArgs);
            }

            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

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
                min = Math.Min(min, value);
                max = Math.Max(max, value);
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

        private static string ToInvariant(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string FormatAllocationMetric(BenchmarkReport report, double value, string format)
        {
            if (!report.allocationMeasurementAvailable)
            {
                return "n/a";
            }

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public string scriptingBackend;
            public bool compiledActivationSupported;
            public int generatedActivatorsRegistered;
            public bool allocationMeasurementAvailable;
            public string scope;
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
            public double medianNanosecondsPerOperation;
            public double q1NanosecondsPerOperation;
            public double q3NanosecondsPerOperation;
            public double[] sampleNanosecondsPerOperation;
            public int[] gen0CollectionsPerSample;
            public int[] gen1CollectionsPerSample;
            public int[] gen2CollectionsPerSample;
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
            PrepareAndRegisterComplex = 4,
            ResolveKeyedSingleton = 5,
            ResolveScopedSingleton = 6
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

        [UnityEngine.Scripting.Preserve]
        internal static object[] CreateZenjectIntArrayPool(
            Func<int[]> create,
            Action<int[], int, int> initialize,
            Action<int[]> reset)
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            if (reset == null)
            {
                throw new ArgumentNullException(nameof(reset));
            }

            DiContainer container = new DiContainer();
            container.BindMemoryPool<int[], IntArrayMemoryPool>()
                .WithFixedSize(32)
                .FromMethod(_ => create());
            IntArrayMemoryPool pool = container.Resolve<IntArrayMemoryPool>();
            pool.SetCallbacks(initialize, reset);

            Func<int, int, int[]> acquire = pool.Spawn;
            Action<int[]> release = pool.Despawn;
            Func<int> active = () => pool.NumActive;
            Func<int> inactive = () => pool.NumInactive;
            Func<int> total = () => pool.NumTotal;
#if ZEN_STRIP_ASSERTS_IN_BUILDS
            const bool stripAssertsInBuilds = true;
#else
            const bool stripAssertsInBuilds = false;
#endif
#if ZEN_INTERNAL_PROFILING
            const bool internalProfiling = true;
#else
            const bool internalProfiling = false;
#endif
            return new object[]
            {
                "onity-zenject-int-array-v1", acquire, release, active, inactive, total,
                pool, stripAssertsInBuilds, internalProfiling
            };
        }

        [UnityEngine.Scripting.Preserve]
        internal sealed class IntArrayMemoryPool : MemoryPool<int, int, int[]>
        {
            private Action<int[], int, int> m_initialize;
            private Action<int[]> m_reset;

            [UnityEngine.Scripting.Preserve]
            public IntArrayMemoryPool()
            {
            }

            public void SetCallbacks(Action<int[], int, int> initialize, Action<int[]> reset)
            {
                m_initialize = initialize;
                m_reset = reset;
            }

            protected override void Reinitialize(int damage, int ownerId, int[] item)
            {
                m_initialize(item, damage, ownerId);
            }

            protected override void OnDespawned(int[] item)
            {
                m_reset(item);
            }
        }
    }
}
