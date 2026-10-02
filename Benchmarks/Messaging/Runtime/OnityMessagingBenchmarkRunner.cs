using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting;
using Debug = UnityEngine.Debug;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Measures every selected scenario on Onity.Messaging and MessagePipe. Per scenario: one full GC, two
    /// discarded warmup samples per library, then the measured samples; sample k (warmups included) runs
    /// Onity first when k is even and MessagePipe first when k is odd. Every sample (warmup, measured and
    /// allocation pass) is checked against the library-independent golden model, including an untimed
    /// probe published after the timed loop.
    /// </summary>
    internal static class OnityMessagingBenchmarkRunner
    {
        internal const int WarmupSamples = 2;
        internal const int LibraryCount = 2;
        internal const int OnityIndex = 0;
        internal const int MessagePipeIndex = 1;

        private const string k_unknown = "Unknown (no build sidecar)";

        private static readonly string[] s_libraryNames =
        {
            OnityMessagingBenchmarkOnityWorkloads.LibraryName,
            OnityMessagingBenchmarkMessagePipeWorkloads.LibraryName
        };

        /// <summary>Fills <paramref name="report"/>; exceptions propagate to the caller.</summary>
        internal static void Run(OnityMessagingBenchmarkOptions options, OnityMessagingBenchmarkReport report)
        {
            report.selfTest = options.SelfTest;
            report.environment = CaptureEnvironment(options.SelfTest);
            ReadBuildMetadata(options, report);
            report.messagePipeSetup = OnityMessagingBenchmarkPlayerRunner.MessagePipeSetup;
            report.messagePipeFlavor = OnityMessagingBenchmarkPlayerRunner.MessagePipeFlavor;
            report.libraries = DescribeLibraries(report.build);
            report.settings = new OnityMessagingBenchmarkSettings
            {
                warmupSamples = WarmupSamples,
                measuredSamples = options.Samples,
                scenarioFilter = options.ScenarioFilter,
                order = "Sample k (warmups included) runs Onity first when k is even and MessagePipe first when k is odd.",
                timing = "Stopwatch.GetTimestamp around the timed loop only; ns/op = ticks * 1e9 / Stopwatch.Frequency / operations."
            };

            OnityMessagingBenchmarkAllocationCounter counter = OnityMessagingBenchmarkAllocationCounter.Calibrate();
            report.allocation = counter.ToReport();

            report.scenarios = new OnityMessagingBenchmarkScenarioResult[options.Scenarios.Length];
            bool goldenPassed = true;
            for (int i = 0; i < options.Scenarios.Length; i++)
            {
                OnityMessagingBenchmarkScenario scenario = options.Scenarios[i];
                OnityMessagingBenchmarkScenarioResult result = MeasureScenario(
                    scenario, options.OperationsFor(scenario), options.Samples, counter);
                report.scenarios[i] = result;
                goldenPassed &= result.goldenPassed;
                Debug.Log("Onity messaging benchmark " + scenario.Id + ": golden " + (result.goldenPassed ? "passed" : "FAILED")
                    + ", median Onity/MessagePipe " + FormatRatio(result.medianRatioOnityToMessagePipe));
            }

            report.goldenPassed = goldenPassed;
            report.completed = true;
        }

        private static OnityMessagingBenchmarkScenarioResult MeasureScenario(
            OnityMessagingBenchmarkScenario scenario,
            int operations,
            int samples,
            OnityMessagingBenchmarkAllocationCounter counter)
        {
            OnityMessagingBenchmarkExpectation expected = OnityMessagingBenchmarkGolden.Compute(scenario.Id, operations);
            OnityMessagingBenchmarkScenarioResult result = new OnityMessagingBenchmarkScenarioResult
            {
                id = scenario.Id,
                description = scenario.Description,
                operationsPerSample = operations,
                expectedChecksum = expected.Checksum,
                expectedNotifications = expected.Notifications,
                expectedProbeChecksum = expected.ProbeChecksum,
                expectedProbeNotifications = expected.ProbeNotifications,
                libraries = new OnityMessagingBenchmarkLibraryResult[LibraryCount]
            };

            for (int library = 0; library < LibraryCount; library++)
            {
                result.libraries[library] = new OnityMessagingBenchmarkLibraryResult
                {
                    library = s_libraryNames[library],
                    samplesNsPerOp = new double[samples],
                    sampleTicks = new long[samples],
                    sampleOrderPositions = new int[samples]
                };
            }

            try
            {
                OnityMessagingBenchmarkWorkload[] workloads =
                {
                    OnityMessagingBenchmarkOnityWorkloads.Create(scenario.Id),
                    OnityMessagingBenchmarkMessagePipeWorkloads.Create(scenario.Id)
                };

                ForceFullGc();
                for (int warmup = 0; warmup < WarmupSamples; warmup++)
                {
                    for (int position = 0; position < LibraryCount; position++)
                    {
                        int library = (position + warmup) % LibraryCount;
                        RunSample(workloads[library], operations, expected, result.libraries[library], "warmup", warmup);
                    }
                }

                double nanosecondsPerTick = 1e9 / Stopwatch.Frequency;
                for (int sample = 0; sample < samples; sample++)
                {
                    for (int position = 0; position < LibraryCount; position++)
                    {
                        int library = (position + sample) % LibraryCount;
                        OnityMessagingBenchmarkLibraryResult libraryResult = result.libraries[library];
                        int collections = GC.CollectionCount(0);
                        long ticks = RunSample(workloads[library], operations, expected, libraryResult, "sample", sample);
                        libraryResult.gcCollectionsDuringSamples += GC.CollectionCount(0) - collections;
                        libraryResult.sampleTicks[sample] = ticks;
                        libraryResult.samplesNsPerOp[sample] = ticks * nanosecondsPerTick / operations;
                        libraryResult.sampleOrderPositions[sample] = position;
                    }
                }

                if (counter.IsAvailable)
                {
                    for (int library = 0; library < LibraryCount; library++)
                    {
                        MeasureAllocation(workloads[library], operations, expected, result.libraries[library], counter);
                    }
                }
            }
            catch (Exception exception)
            {
                result.failure = exception.ToString();
                Debug.LogException(exception);
            }

            Summarize(result);
            return result;
        }

        private static long RunSample(
            OnityMessagingBenchmarkWorkload workload,
            int operations,
            OnityMessagingBenchmarkExpectation expected,
            OnityMessagingBenchmarkLibraryResult result,
            string phase,
            int index)
        {
            OnityMessagingBenchmarkSink sink = workload.Sink;
            workload.Setup();
            sink.Reset();

            long start = Stopwatch.GetTimestamp();
            workload.Run(operations);
            long ticks = Stopwatch.GetTimestamp() - start;

            Finish(workload, expected, result, phase, index);
            return ticks;
        }

        private static void MeasureAllocation(
            OnityMessagingBenchmarkWorkload workload,
            int operations,
            OnityMessagingBenchmarkExpectation expected,
            OnityMessagingBenchmarkLibraryResult result,
            OnityMessagingBenchmarkAllocationCounter counter)
        {
            workload.Setup();
            workload.Sink.Reset();

            long before = counter.Read();
            workload.Run(operations);
            long after = counter.Read();

            Finish(workload, expected, result, "allocation", 0);
            result.allocationMeasured = true;
            result.allocatedBytes = after - before;
            result.allocatedBytesPerOp = (double)(after - before) / operations;
        }

        // Reads the timed totals, publishes the untimed probe, tears down and checks against the golden model.
        private static void Finish(
            OnityMessagingBenchmarkWorkload workload,
            OnityMessagingBenchmarkExpectation expected,
            OnityMessagingBenchmarkLibraryResult result,
            string phase,
            int index)
        {
            OnityMessagingBenchmarkSink sink = workload.Sink;
            long checksum = sink.Sum;
            long notifications = sink.Count;
            workload.Probe(OnityMessagingBenchmarkGolden.ProbeValue);
            long probeChecksum = sink.Sum - checksum;
            long probeNotifications = sink.Count - notifications;
            workload.Teardown();

            if (result.checkedRuns == 0)
            {
                result.checksum = checksum;
                result.notifications = notifications;
                result.probeChecksum = probeChecksum;
                result.probeNotifications = probeNotifications;
            }

            result.checkedRuns++;
            if (checksum == expected.Checksum && notifications == expected.Notifications
                && probeChecksum == expected.ProbeChecksum && probeNotifications == expected.ProbeNotifications)
            {
                return;
            }

            result.mismatchedRuns++;
            if (string.IsNullOrEmpty(result.firstMismatch))
            {
                result.firstMismatch = phase + " " + index.ToString(CultureInfo.InvariantCulture)
                    + ": checksum " + Format(checksum) + " (expected " + Format(expected.Checksum) + "), notifications "
                    + Format(notifications) + " (expected " + Format(expected.Notifications) + "), probe checksum "
                    + Format(probeChecksum) + " (expected " + Format(expected.ProbeChecksum) + "), probe notifications "
                    + Format(probeNotifications) + " (expected " + Format(expected.ProbeNotifications) + ")";
            }
        }

        private static void Summarize(OnityMessagingBenchmarkScenarioResult result)
        {
            bool passed = string.IsNullOrEmpty(result.failure);
            for (int library = 0; library < LibraryCount; library++)
            {
                OnityMessagingBenchmarkLibraryResult libraryResult = result.libraries[library];
                double[] samples = libraryResult.samplesNsPerOp;
                libraryResult.medianNsPerOp = Median(samples);
                double sum = 0;
                double min = samples.Length > 0 ? samples[0] : 0;
                double max = min;
                for (int i = 0; i < samples.Length; i++)
                {
                    sum += samples[i];
                    min = Math.Min(min, samples[i]);
                    max = Math.Max(max, samples[i]);
                }

                libraryResult.meanNsPerOp = samples.Length > 0 ? sum / samples.Length : 0;
                libraryResult.minNsPerOp = min;
                libraryResult.maxNsPerOp = max;
                passed &= libraryResult.checkedRuns > 0 && libraryResult.mismatchedRuns == 0;
            }

            result.goldenPassed = passed;
            result.medianRatioOnityToMessagePipe = Ratio(
                result.libraries[OnityIndex].medianNsPerOp, result.libraries[MessagePipeIndex].medianNsPerOp);
        }

        private static double Median(double[] values)
        {
            if (values.Length == 0)
            {
                return 0;
            }

            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            return (sorted.Length & 1) == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        private static double Ratio(double numerator, double denominator)
        {
            return denominator > 0 ? numerator / denominator : 0;
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static OnityMessagingBenchmarkEnvironment CaptureEnvironment(bool selfTest)
        {
            return new OnityMessagingBenchmarkEnvironment
            {
                unityVersion = Application.unityVersion,
#if ENABLE_IL2CPP
                scriptingBackend = "IL2CPP",
#else
                scriptingBackend = "Mono",
#endif
                isDevelopment = Debug.isDebugBuild,
                isEditor = Application.isEditor,
                platform = Application.platform.ToString(),
                operatingSystem = SystemInfo.operatingSystem,
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                processorFrequencyMHz = SystemInfo.processorFrequency,
                buildGuid = Application.buildGUID,
                utcTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                commandLine = Environment.CommandLine,
                selfTest = selfTest,
                stopwatchFrequency = Stopwatch.Frequency,
                stopwatchIsHighResolution = Stopwatch.IsHighResolution,
                gcMode = GarbageCollector.GCMode.ToString(),
                gcIncremental = GarbageCollector.isIncremental
            };
        }

        private static void ReadBuildMetadata(OnityMessagingBenchmarkOptions options, OnityMessagingBenchmarkReport report)
        {
            report.buildMetadataPath = options.BuildMetadataPath ?? string.Empty;
            if (options.BuildMetadataPath != null && File.Exists(options.BuildMetadataPath))
            {
                report.build = JsonUtility.FromJson<OnityMessagingBenchmarkBuildMetadata>(File.ReadAllText(options.BuildMetadataPath));
                report.buildMetadataFound = report.build != null;
            }

            report.buildMetadataMatchesPlayer = report.buildMetadataFound
                && SameGuid(report.build.buildGuid, report.environment.buildGuid)
                && string.Equals(report.build.backend, report.environment.scriptingBackend, StringComparison.Ordinal)
                && report.build.development == report.environment.isDevelopment;
        }

        private static OnityMessagingBenchmarkLibraryInfo[] DescribeLibraries(OnityMessagingBenchmarkBuildMetadata build)
        {
            return new[]
            {
                new OnityMessagingBenchmarkLibraryInfo
                {
                    name = OnityMessagingBenchmarkOnityWorkloads.LibraryName,
                    version = build != null && !string.IsNullOrEmpty(build.onityVersion) ? build.onityVersion : k_unknown,
                    runtimeAssembly = OnityMessagingBenchmarkOnityWorkloads.RuntimeAssembly,
                    source = "com.onity.framework package compiled into the Player (Onity.Messaging)"
                },
                new OnityMessagingBenchmarkLibraryInfo
                {
                    name = OnityMessagingBenchmarkMessagePipeWorkloads.LibraryName,
                    version = OnityMessagingBenchmarkPlayerRunner.MessagePipeVersion,
                    runtimeAssembly = OnityMessagingBenchmarkMessagePipeWorkloads.RuntimeAssembly,
                    source = OnityMessagingBenchmarkPlayerRunner.MessagePipeSource
                }
            };
        }

        private static bool SameGuid(string first, string second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
            {
                return false;
            }

            return string.Equals(first.Replace("-", string.Empty), second.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        private static string Format(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatRatio(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
