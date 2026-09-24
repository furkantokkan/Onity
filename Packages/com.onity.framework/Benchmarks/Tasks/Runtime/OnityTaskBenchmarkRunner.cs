using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.Profiling;
#endif
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Compares matched OnityTask and UniTask primitives and async methods in Play Mode.
    /// Frame timings cover scheduling and consumption, not PlayerLoop execution or frame latency.
    /// </summary>
    public sealed class OnityTaskBenchmarkRunner : MonoBehaviour
    {
        private const int k_warmupIterations = 4096;
        private const int k_samplesPerCase = 8;
        private const int k_synchronousIterations = 1000000;
        private const int k_batchesPerSample = 32;
        private const int k_steadyConcurrency = 128;
        private const int k_burstConcurrency = 4096;
        private const int k_completionTimeoutFrames = 240;

        private static bool s_isRunning;
        private static int s_lastInt;

        private readonly OnityTaskAwaiter[] m_onityAwaiters = new OnityTaskAwaiter[k_burstConcurrency];
        private readonly UniTask.Awaiter[] m_uniTaskAwaiters = new UniTask.Awaiter[k_burstConcurrency];
        private readonly OnityTaskAwaiter<int>[] m_onityTypedAwaiters =
            new OnityTaskAwaiter<int>[k_burstConcurrency];
        private readonly UniTask<int>.Awaiter[] m_uniTaskTypedAwaiters =
            new UniTask<int>.Awaiter[k_burstConcurrency];
        private string m_latestJson;
        private Action<string, Exception> m_completed;
        private bool m_allocationCounterAvailable;

        /// <summary>
        /// Queues a benchmark run. The optional callback receives a report path or failure.
        /// </summary>
        /// <param name="latestJson">Output JSON path.</param>
        /// <param name="completed">Optional completion callback.</param>
        public static void Run(string latestJson, Action<string, Exception> completed = null)
        {
            if (string.IsNullOrWhiteSpace(latestJson))
            {
                throw new ArgumentException("Benchmark output path is required.", nameof(latestJson));
            }

            if (s_isRunning)
            {
                throw new InvalidOperationException("An OnityTask benchmark is already running.");
            }

            GameObject runnerObject = new GameObject("Onity Task Benchmark Runner");
            DontDestroyOnLoad(runnerObject);
            OnityTaskBenchmarkRunner runner = runnerObject.AddComponent<OnityTaskBenchmarkRunner>();
            runner.m_latestJson = Path.GetFullPath(latestJson);
            runner.m_completed = completed;
            s_isRunning = true;
        }

        private IEnumerator Start()
        {
            yield return null;
            Exception failure = null;
            TaskBenchmarkReport report = null;
            try
            {
                report = CreateReport();
                RunSynchronousBenchmarks(report);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure == null)
            {
                // This iterator yields only null, so every measured batch is inside this guard.
                IEnumerator frameBenchmarks = RunFrameBenchmarks(report);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = frameBenchmarks.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return null;
                }
            }

            if (failure == null)
            {
                try
                {
                    SaveReport(report, m_latestJson);
                    Debug.Log($"OnityTask benchmark completed: {m_latestJson}", this);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure != null)
            {
                Debug.LogException(failure, this);
            }

            try
            {
                m_completed?.Invoke(m_latestJson, failure);
            }
            finally
            {
                s_isRunning = false;
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            s_isRunning = false;
        }

        private TaskBenchmarkReport CreateReport()
        {
            TaskBenchmarkReport report = new TaskBenchmarkReport
            {
                schemaVersion = 2,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                isEditor = Application.isEditor,
                scriptingBackend = GetScriptingBackendLabel(),
                uniTaskAssembly = typeof(UniTask).Assembly.FullName,
                stopwatchFrequency = Stopwatch.Frequency,
                timerResolutionNanoseconds = 1000000000d / Stopwatch.Frequency,
                samplesPerCase = k_samplesPerCase,
                warmupIterations = k_warmupIterations,
                frameBatchesPerSample = k_batchesPerSample,
                measurementScope = "Main-thread synchronous slices only. Scheduling and GetResult are separate. "
                    + "Async-method cases include builder work within those slices. PlayerLoop execution, "
                    + "suspended-frame time, continuation dispatch and builder completion during resumption are excluded. "
                    + "Raw times include harness overhead; no baseline subtraction or overall winner is inferred.",
                scenarios = new TaskBenchmarkScenarioReport[16]
            };

            CalibrateAllocationCounter(report);
            return report;
        }

        private void CalibrateAllocationCounter(TaskBenchmarkReport report)
        {
#if ENABLE_IL2CPP
            report.allocationCounter = "Unavailable: Unity 2022 IL2CPP managed allocation counter is disabled "
                + "because the existing DI harness observed crashes. Use a profiler capture for allocations.";
#else
            try
            {
                GC.GetAllocatedBytesForCurrentThread();
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] calibration = new byte[65536];
                long after = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(calibration);
                report.allocationCalibrationBytes = after - before;
                before = GC.GetAllocatedBytesForCurrentThread();
                after = GC.GetAllocatedBytesForCurrentThread();
                report.emptyAllocationDeltaBytes = after - before;
                m_allocationCounterAvailable = report.allocationCalibrationBytes >= 65536
                    && report.emptyAllocationDeltaBytes == 0;
                report.allocationCounter = m_allocationCounterAvailable
                    ? "GC.GetAllocatedBytesForCurrentThread; 64 KiB positive control and empty control passed."
                    : "Unavailable: allocation counter calibration failed.";
            }
            catch (Exception exception)
            {
                report.allocationCounter = "Unavailable: " + exception.GetType().Name;
            }
#endif
            report.allocationsAvailable = m_allocationCounterAvailable;
        }

        private long ReadAllocatedBytes()
        {
#if ENABLE_IL2CPP
            return 0;
#else
            return m_allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif
        }

        private void RunSynchronousBenchmarks(TaskBenchmarkReport report)
        {
            Action empty = EmptyOperation;
            SampleSet baseline = new SampleSet();
            for (int i = 0; i < k_warmupIterations; i++)
            {
                empty();
                MeasureOnityCompleted();
                MeasureUniTaskCompleted();
                MeasureOnityResult();
                MeasureUniTaskResult();
            }

            for (int sample = 0; sample < k_samplesPerCase; sample++)
            {
                MeasureLoop(empty, baseline, sample);
            }

            report.synchronousHarnessBaseline = BuildMetric("Empty delegate loop", baseline, k_synchronousIterations);
            report.scenarios[0] = MeasureSynchronousScenario(
                "Completed GetResult", MeasureOnityCompleted, MeasureUniTaskCompleted);
            report.scenarios[1] = MeasureSynchronousScenario(
                "FromResult<int> GetResult", MeasureOnityResult, MeasureUniTaskResult);

            for (int i = 0; i < k_warmupIterations; i++)
            {
                MeasureOnityAsyncCompleted();
                MeasureUniTaskAsyncCompleted();
                MeasureOnityAsyncResult();
                MeasureUniTaskAsyncResult();
            }

            report.scenarios[6] = MeasureSynchronousScenario(
                "Async method completed GetResult", MeasureOnityAsyncCompleted, MeasureUniTaskAsyncCompleted);
            report.scenarios[7] = MeasureSynchronousScenario(
                "Async method completed<int> GetResult", MeasureOnityAsyncResult, MeasureUniTaskAsyncResult);
        }

        private TaskBenchmarkScenarioReport MeasureSynchronousScenario(
            string name, Action onity, Action uniTask)
        {
            SampleSet onitySamples = new SampleSet();
            SampleSet uniTaskSamples = new SampleSet();
            for (int sample = 0; sample < k_samplesPerCase; sample++)
            {
                ForceFullGc();
                if ((sample & 1) == 0)
                {
                    MeasureLoop(onity, onitySamples, sample);
                    MeasureLoop(uniTask, uniTaskSamples, sample);
                }
                else
                {
                    MeasureLoop(uniTask, uniTaskSamples, sample);
                    MeasureLoop(onity, onitySamples, sample);
                }
            }

            return BuildScenario(name, "Synchronous; delegate invocation included", 1,
                k_synchronousIterations, onitySamples, uniTaskSamples);
        }

        private void MeasureLoop(Action operation, SampleSet samples, int sample)
        {
            long bytes = ReadAllocatedBytes();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < k_synchronousIterations; i++)
            {
                operation();
            }

            long stopped = Stopwatch.GetTimestamp();
            samples.bytes[sample] = ReadAllocatedBytes() - bytes;
            samples.ticks[sample] = stopped - started;
        }

        private IEnumerator RunFrameBenchmarks(TaskBenchmarkReport report)
        {
            for (int cohort = 0; cohort < 2; cohort++)
            {
                int concurrency = cohort == 0 ? k_steadyConcurrency : k_burstConcurrency;
                string workload = cohort == 0
                    ? "Warm steady state; 128 concurrent operations; below Onity's 256 source retention cap"
                    : "Repeated burst; 4096 concurrent operations; exceeds Onity's 256 source retention cap";

                // Warm each library with the exact concurrency and completed consumption pattern.
                for (int warmup = 0; warmup < 2; warmup++)
                {
                    for (int library = 0; library < 2; library++)
                    {
                        Schedule(library, concurrency);
                        int remaining = k_completionTimeoutFrames;
                        do
                        {
                            yield return null;
                            if (--remaining == 0)
                            {
                                throw new TimeoutException("NextFrame warmup did not complete.");
                            }
                        }
                        while (!IsCompleted(library, concurrency));
                        Consume(library, concurrency);
                    }
                }

                SampleSet[] creation = { new SampleSet(), new SampleSet() };
                SampleSet[] consumption = { new SampleSet(), new SampleSet() };
                for (int sample = 0; sample < k_samplesPerCase; sample++)
                {
                    ForceFullGc();
                    for (int batch = 0; batch < k_batchesPerSample; batch++)
                    {
                        for (int turn = 0; turn < 2; turn++)
                        {
                            int library = (sample + batch + turn) & 1;
                            long bytes = ReadAllocatedBytes();
                            long started = Stopwatch.GetTimestamp();
                            Schedule(library, concurrency);
                            long stopped = Stopwatch.GetTimestamp();
                            creation[library].bytes[sample] += ReadAllocatedBytes() - bytes;
                            creation[library].ticks[sample] += stopped - started;

                            int remaining = k_completionTimeoutFrames;
                            do
                            {
                                yield return null;
                                if (--remaining == 0)
                                {
                                    throw new TimeoutException("NextFrame sample did not complete.");
                                }
                            }
                            while (!IsCompleted(library, concurrency));

                            bytes = ReadAllocatedBytes();
                            started = Stopwatch.GetTimestamp();
                            Consume(library, concurrency);
                            stopped = Stopwatch.GetTimestamp();
                            consumption[library].bytes[sample] += ReadAllocatedBytes() - bytes;
                            consumption[library].ticks[sample] += stopped - started;
                        }
                    }
                }

                int index = 2 + cohort * 2;
                int operations = concurrency * k_batchesPerSample;
                report.scenarios[index] = BuildScenario("NextFrame scheduling", workload,
                    concurrency, operations, creation[0], creation[1]);
                report.scenarios[index + 1] = BuildScenario("NextFrame GetResult", workload,
                    concurrency, operations, consumption[0], consumption[1]);
            }

            IEnumerator asyncMethodBenchmarks = RunAsyncMethodFrameBenchmarks(report);
            while (asyncMethodBenchmarks.MoveNext())
            {
                yield return null;
            }
        }

        private IEnumerator RunAsyncMethodFrameBenchmarks(TaskBenchmarkReport report)
        {
            for (int resultKind = 0; resultKind < 2; resultKind++)
            {
                bool typed = resultKind == 1;
                string name = typed ? "Async method NextFrame<int>" : "Async method NextFrame";
                for (int cohort = 0; cohort < 2; cohort++)
                {
                    int concurrency = cohort == 0 ? k_steadyConcurrency : k_burstConcurrency;
                    string workload = (cohort == 0
                        ? "Warm steady state; 128 concurrent operations"
                        : "Repeated burst; 4096 concurrent operations")
                        + "; one NextFrame suspension; scheduling/consumption slices only; "
                        + "suspended-frame time and resumption excluded";

                    for (int warmup = 0; warmup < 2; warmup++)
                    {
                        for (int library = 0; library < 2; library++)
                        {
                            ScheduleAsyncMethods(library, concurrency, typed);
                            int remaining = k_completionTimeoutFrames;
                            do
                            {
                                yield return null;
                                if (--remaining == 0)
                                {
                                    throw new TimeoutException(name + " warmup did not complete.");
                                }
                            }
                            while (!AreAsyncMethodsCompleted(library, concurrency, typed));
                            ConsumeAsyncMethods(library, concurrency, typed);
                        }
                    }

                    SampleSet[] creation = { new SampleSet(), new SampleSet() };
                    SampleSet[] consumption = { new SampleSet(), new SampleSet() };
                    for (int sample = 0; sample < k_samplesPerCase; sample++)
                    {
                        ForceFullGc();
                        for (int batch = 0; batch < k_batchesPerSample; batch++)
                        {
                            for (int turn = 0; turn < 2; turn++)
                            {
                                int library = (sample + batch + turn) & 1;
                                long bytes = ReadAllocatedBytes();
                                long started = Stopwatch.GetTimestamp();
                                ScheduleAsyncMethods(library, concurrency, typed);
                                long stopped = Stopwatch.GetTimestamp();
                                creation[library].bytes[sample] += ReadAllocatedBytes() - bytes;
                                creation[library].ticks[sample] += stopped - started;

                                int remaining = k_completionTimeoutFrames;
                                do
                                {
                                    yield return null;
                                    if (--remaining == 0)
                                    {
                                        throw new TimeoutException(name + " sample did not complete.");
                                    }
                                }
                                while (!AreAsyncMethodsCompleted(library, concurrency, typed));

                                bytes = ReadAllocatedBytes();
                                started = Stopwatch.GetTimestamp();
                                ConsumeAsyncMethods(library, concurrency, typed);
                                stopped = Stopwatch.GetTimestamp();
                                consumption[library].bytes[sample] += ReadAllocatedBytes() - bytes;
                                consumption[library].ticks[sample] += stopped - started;
                            }
                        }
                    }

                    int index = 8 + resultKind * 4 + cohort * 2;
                    int operations = concurrency * k_batchesPerSample;
                    report.scenarios[index] = BuildScenario(name + " scheduling", workload,
                        concurrency, operations, creation[0], creation[1]);
                    report.scenarios[index + 1] = BuildScenario(name + " GetResult", workload,
                        concurrency, operations, consumption[0], consumption[1]);
                }
            }
        }

        private void ScheduleAsyncMethods(int library, int count, bool typed)
        {
            if (typed)
            {
                if (library == 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        m_onityTypedAwaiters[i] = OnityNextFrameResultAsync().GetAwaiter();
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        m_uniTaskTypedAwaiters[i] = UniTaskNextFrameResultAsync().GetAwaiter();
                    }
                }
            }
            else if (library == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    m_onityAwaiters[i] = OnityNextFrameAsync().GetAwaiter();
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    m_uniTaskAwaiters[i] = UniTaskNextFrameAsync().GetAwaiter();
                }
            }
        }

        private bool AreAsyncMethodsCompleted(int library, int count, bool typed)
        {
            if (!typed)
            {
                return IsCompleted(library, count);
            }

            for (int i = 0; i < count; i++)
            {
                if (library == 0 ? !m_onityTypedAwaiters[i].IsCompleted : !m_uniTaskTypedAwaiters[i].IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }

        private void ConsumeAsyncMethods(int library, int count, bool typed)
        {
            if (!typed)
            {
                Consume(library, count);
                return;
            }

            int resultSum = 0;
            int invalidResult = 0;
            if (library == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    int result = m_onityTypedAwaiters[i].GetResult();
                    resultSum += result;
                    invalidResult |= result ^ 42;
                    m_onityTypedAwaiters[i] = default;
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    int result = m_uniTaskTypedAwaiters[i].GetResult();
                    resultSum += result;
                    invalidResult |= result ^ 42;
                    m_uniTaskTypedAwaiters[i] = default;
                }
            }

            s_lastInt = resultSum;
            if (invalidResult != 0)
            {
                throw new InvalidOperationException("Async method returned an unexpected result.");
            }
        }

        private void Schedule(int library, int count)
        {
            if (library == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    m_onityAwaiters[i] = OnityTask.NextFrame().GetAwaiter();
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    m_uniTaskAwaiters[i] = UniTask.NextFrame().GetAwaiter();
                }
            }
        }

        private bool IsCompleted(int library, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (library == 0 ? !m_onityAwaiters[i].IsCompleted : !m_uniTaskAwaiters[i].IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }

        private void Consume(int library, int count)
        {
            if (library == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    m_onityAwaiters[i].GetResult();
                    m_onityAwaiters[i] = default;
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    m_uniTaskAwaiters[i].GetResult();
                    m_uniTaskAwaiters[i] = default;
                }
            }
        }

        private TaskBenchmarkScenarioReport BuildScenario(
            string name, string workload, int concurrency, int operations, SampleSet onity, SampleSet uniTask)
        {
            return new TaskBenchmarkScenarioReport
            {
                displayName = name,
                workload = workload,
                concurrency = concurrency,
                iterationsPerSample = operations,
                results = new[] { BuildMetric("OnityTask", onity, operations), BuildMetric("UniTask", uniTask, operations) }
            };
        }

        private TaskBenchmarkMetricReport BuildMetric(string label, SampleSet samples, int operations)
        {
            double[] milliseconds = new double[k_samplesPerCase];
            double total = 0;
            long totalBytes = 0;
            for (int i = 0; i < milliseconds.Length; i++)
            {
                milliseconds[i] = samples.ticks[i] * 1000d / Stopwatch.Frequency;
                total += milliseconds[i];
                totalBytes += samples.bytes[i];
            }

            double mean = total / milliseconds.Length;
            double variance = 0;
            for (int i = 0; i < milliseconds.Length; i++)
            {
                double delta = milliseconds[i] - mean;
                variance += delta * delta;
            }

            double[] sorted = (double[])milliseconds.Clone();
            Array.Sort(sorted);
            return new TaskBenchmarkMetricReport
            {
                library = label,
                meanMilliseconds = mean,
                medianMilliseconds = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2d,
                minMilliseconds = sorted[0],
                maxMilliseconds = sorted[sorted.Length - 1],
                standardDeviationMilliseconds = Math.Sqrt(variance / milliseconds.Length),
                nanosecondsPerOperation = mean * 1000000d / operations,
                allocatedBytesPerOperation = m_allocationCounterAvailable
                    ? (double)totalBytes / (milliseconds.Length * (long)operations) : -1d,
                sampleMilliseconds = milliseconds,
                sampleAllocatedBytes = m_allocationCounterAvailable ? samples.bytes : null
            };
        }

        private static void EmptyOperation()
        {
        }
        private static void MeasureOnityCompleted() => OnityTask.CompletedTask.GetAwaiter().GetResult();
        private static void MeasureUniTaskCompleted() => UniTask.CompletedTask.GetAwaiter().GetResult();
        private static void MeasureOnityResult() => s_lastInt = OnityTask.FromResult(42).GetAwaiter().GetResult();
        private static void MeasureUniTaskResult() => s_lastInt = UniTask.FromResult(42).GetAwaiter().GetResult();
        private static void MeasureOnityAsyncCompleted() => OnityCompletedAsync().GetAwaiter().GetResult();
        private static void MeasureUniTaskAsyncCompleted() => UniTaskCompletedAsync().GetAwaiter().GetResult();
        private static void MeasureOnityAsyncResult() => s_lastInt = OnityCompletedResultAsync().GetAwaiter().GetResult();
        private static void MeasureUniTaskAsyncResult() => s_lastInt = UniTaskCompletedResultAsync().GetAwaiter().GetResult();

        private static async OnityTask OnityCompletedAsync()
        {
            await OnityTask.CompletedTask;
        }

        private static async UniTask UniTaskCompletedAsync()
        {
            await UniTask.CompletedTask;
        }

        private static async OnityTask<int> OnityCompletedResultAsync()
        {
            await OnityTask.CompletedTask;
            return 42;
        }

        private static async UniTask<int> UniTaskCompletedResultAsync()
        {
            await UniTask.CompletedTask;
            return 42;
        }

        private static async OnityTask OnityNextFrameAsync()
        {
            await OnityTask.NextFrame();
        }

        private static async UniTask UniTaskNextFrameAsync()
        {
            await UniTask.NextFrame();
        }

        private static async OnityTask<int> OnityNextFrameResultAsync()
        {
            await OnityTask.NextFrame();
            return 42;
        }

        private static async UniTask<int> UniTaskNextFrameResultAsync()
        {
            await UniTask.NextFrame();
            return 42;
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static string GetScriptingBackendLabel()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#elif ENABLE_MONO
            return "Mono";
#else
            return "Unknown";
#endif
        }

        private static void SaveReport(TaskBenchmarkReport report, string latestJson)
        {
            string directory = Path.GetDirectoryName(latestJson);
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, $"onity-task-benchmark-{stamp}.json"), json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            File.WriteAllText(Path.ChangeExtension(latestJson, ".csv"), BuildCsv(report), Encoding.UTF8);
            File.WriteAllText(Path.ChangeExtension(latestJson, ".md"), BuildMarkdown(report), Encoding.UTF8);
        }

        private static string BuildCsv(TaskBenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("scenario,workload,concurrency,library,operations_per_sample,mean_ms,median_ms,"
                + "min_ms,max_ms,stddev_ms,ns_per_op,bytes_per_op,allocations_available");
            foreach (TaskBenchmarkScenarioReport scenario in report.scenarios)
            {
                foreach (TaskBenchmarkMetricReport metric in scenario.results)
                {
                    builder.Append(EscapeCsv(scenario.displayName)).Append(',');
                    builder.Append(EscapeCsv(scenario.workload)).Append(',');
                    builder.Append(scenario.concurrency).Append(',').Append(metric.library).Append(',');
                    builder.Append(scenario.iterationsPerSample).Append(',');
                    builder.Append(Number(metric.meanMilliseconds)).Append(',');
                    builder.Append(Number(metric.medianMilliseconds)).Append(',');
                    builder.Append(Number(metric.minMilliseconds)).Append(',');
                    builder.Append(Number(metric.maxMilliseconds)).Append(',');
                    builder.Append(Number(metric.standardDeviationMilliseconds)).Append(',');
                    builder.Append(Number(metric.nanosecondsPerOperation)).Append(',');
                    builder.Append(Number(metric.allocatedBytesPerOperation)).Append(',');
                    builder.AppendLine(report.allocationsAvailable ? "true" : "false");
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(TaskBenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# OnityTask primitive and async-method benchmark").AppendLine();
            builder.AppendLine($"- UTC: {report.generatedAtUtc}");
            builder.AppendLine($"- Unity: {report.unityVersion}; {report.platform}; {report.scriptingBackend}");
            builder.AppendLine($"- Samples: {report.samplesPerCase}; frame batches/sample: {report.frameBatchesPerSample}");
            builder.AppendLine($"- Allocation counter: {report.allocationCounter}");
            builder.AppendLine($"- Timer resolution: {Number(report.timerResolutionNanoseconds)} ns");
            builder.AppendLine($"- Empty synchronous delegate loop: {Number(report.synchronousHarnessBaseline.nanosecondsPerOperation)} ns/op");
            builder.AppendLine().AppendLine(report.measurementScope).AppendLine();
            builder.AppendLine("Both libraries use identical concurrency and two full warmup batches per frame cohort. "
                + "Execution order alternates per sample and batch. Burst results include pool retention differences. "
                + "Unavailable allocations are written as -1, never zero.").AppendLine();
            builder.AppendLine("| Scenario | Concurrency | Library | Mean ns/op | Bytes/op | Stddev ms |");
            builder.AppendLine("|---|---:|---|---:|---:|---:|");
            foreach (TaskBenchmarkScenarioReport scenario in report.scenarios)
            {
                foreach (TaskBenchmarkMetricReport metric in scenario.results)
                {
                    builder.Append("| ").Append(scenario.displayName).Append(" | ");
                    builder.Append(scenario.concurrency).Append(" | ").Append(metric.library).Append(" | ");
                    builder.Append(Number(metric.nanosecondsPerOperation)).Append(" | ");
                    builder.Append(report.allocationsAvailable ? Number(metric.allocatedBytesPerOperation) : "unavailable");
                    builder.Append(" | ").Append(Number(metric.standardDeviationMilliseconds)).AppendLine(" |");
                }
            }

            builder.AppendLine().AppendLine("128 concurrent operations is the warm steady-state cohort; "
                + "4096 is a repeated burst exceeding Onity's 256 retained frame sources. "
                + "Primitive and async-method slice measurements do not establish overall library superiority.");
            return builder.ToString();
        }

        private static string EscapeCsv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        private sealed class SampleSet
        {
            public readonly long[] ticks = new long[k_samplesPerCase];
            public readonly long[] bytes = new long[k_samplesPerCase];
        }

        [Serializable]
        private sealed class TaskBenchmarkReport
        {
            public int schemaVersion;
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public bool isEditor;
            public string scriptingBackend;
            public string uniTaskAssembly;
            public long stopwatchFrequency;
            public double timerResolutionNanoseconds;
            public int samplesPerCase;
            public int warmupIterations;
            public int frameBatchesPerSample;
            public string measurementScope;
            public bool allocationsAvailable;
            public string allocationCounter;
            public long allocationCalibrationBytes;
            public long emptyAllocationDeltaBytes;
            public TaskBenchmarkMetricReport synchronousHarnessBaseline;
            public TaskBenchmarkScenarioReport[] scenarios;
        }

        [Serializable]
        private sealed class TaskBenchmarkScenarioReport
        {
            public string displayName;
            public string workload;
            public int concurrency;
            public int iterationsPerSample;
            public TaskBenchmarkMetricReport[] results;
        }

        [Serializable]
        private sealed class TaskBenchmarkMetricReport
        {
            public string library;
            public double meanMilliseconds;
            public double medianMilliseconds;
            public double minMilliseconds;
            public double maxMilliseconds;
            public double standardDeviationMilliseconds;
            public double nanosecondsPerOperation;
            public double allocatedBytesPerOperation;
            public double[] sampleMilliseconds;
            public long[] sampleAllocatedBytes;
        }
    }

    /// <summary>
    /// Measures the native typed two-input WhenAny route, UniTask's exact-shape
    /// params route, and Onity's older Task bridge migration route.
    /// </summary>
    public sealed class OnityTypedWhenAnyBenchmarkRunner : MonoBehaviour
    {
        private const int k_operations = 128;
        private const int k_outstandingOperations = 384;
        private const int k_samples = 8;
        private const int k_warmupBatches = 10;
        private const int k_successTimingBatches = 16;
        private const int k_failureTimingBatches = 2;
        private const int k_profilerStartupFrames = 120;
        private const int k_profilerReadFrames = 60;
        private const string k_uniTaskCommit =
            "2e993ff18f28c931602a07292df0b0804eebef99";
        private const string k_markerPrefix = "Onity.TypedWhenAny.Allocation.";
        private const string k_runtimePath =
            "Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsync.cs";
        private const string k_completionPath =
            "Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityTaskCompletionSource.cs";
        private const string k_runnerPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Runtime/OnityTaskBenchmarkRunner.cs";
        private const string k_menuPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Editor/OnityTaskBenchmarkMenu.cs";
        private const string k_runtimeAsmdefPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Runtime/Onity.TaskBenchmarks.asmdef";
        private const string k_editorAsmdefPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Editor/Onity.TaskBenchmarks.Editor.asmdef";
        private const string k_nativeSourceType =
            "Onity.Unity.Async.OnityWhenAnyTaskSource`1";
        private const string k_pooledNativeSourceType =
            "Onity.Unity.Async.OnityWhenAnyPooledTaskSource`1";
        private const string k_intSourceArgument = "<System.Int32>";

        private static bool s_isRunning;
        private static int s_emptySink;
        private static readonly CancellationToken s_canceledToken = new CancellationToken(true);

        private readonly OnityTaskCompletionSource<int>[] m_onityFirst =
            new OnityTaskCompletionSource<int>[k_outstandingOperations];
        private readonly OnityTaskCompletionSource<int>[] m_onitySecond =
            new OnityTaskCompletionSource<int>[k_outstandingOperations];
        private readonly UniTaskCompletionSource<int>[] m_uniFirst =
            new UniTaskCompletionSource<int>[k_outstandingOperations];
        private readonly UniTaskCompletionSource<int>[] m_uniSecond =
            new UniTaskCompletionSource<int>[k_outstandingOperations];
        private readonly OnityTask<(int winnerIndex, int result)>[] m_nativeResults =
            new OnityTask<(int winnerIndex, int result)>[k_outstandingOperations];
        private readonly UniTask<(int winArgumentIndex, int result)>[] m_uniResults =
            new UniTask<(int winArgumentIndex, int result)>[k_outstandingOperations];
        private readonly Task<Task<int>>[] m_legacyResults =
            new Task<Task<int>>[k_outstandingOperations];
        private readonly Task<int>[] m_legacyFirst = new Task<int>[k_outstandingOperations];
        private readonly Task<int>[] m_legacySecond = new Task<int>[k_outstandingOperations];
        private readonly Exception[] m_failures = new Exception[k_outstandingOperations];

        private string m_outputPath;
        private string m_productCommit;
        private Action<string, Exception> m_completed;
        private bool m_allocationOnly;
        private bool m_collectorDiagnostic;
        private bool m_originalTracker;
        private bool m_trackerCaptured;
        private bool m_trackerStackTraceAtStart;
        private Scenario m_activeScenario;
        private int m_activeRoute;
        private int m_activeCount;
        private Action m_measureBatch;
        private Action m_emptyBatch;
        private Action m_emptyOutstandingBatch;
        private Action m_positiveControl;
        private string m_nativePendingSourceType;
#if UNITY_EDITOR
        private readonly System.Collections.Generic.List<CaptureDiagnostic> m_captureDiagnostics =
            new System.Collections.Generic.List<CaptureDiagnostic>(16);
        private string m_capturePhase;
        private ulong m_mainProfilerThreadId;
        private int m_mainManagedThreadId;
        private bool m_mainThreadIdentified;
        private bool m_profilerCaptured;
        private bool m_profilerWasEnabled;
        private bool m_driverWasEnabled;
        private bool m_profileEditorWasEnabled;
        private bool m_callstacksWereEnabled;
        private bool m_deepProfilingAtStart;
        private int m_lastFrame = -1;
        private int m_markerSequence;
        private bool m_lastCaptureValid;
        private long m_lastCaptureBytes;
#endif

        /// <summary>
        /// Runs the typed benchmark using the supplied product commit in every report mode.
        /// </summary>
        public static void Run(string outputPath, Action<string, Exception> completed,
            bool allocationOnly, bool collectorDiagnostic, string productCommit)
        {
            if (s_isRunning)
            {
                throw new InvalidOperationException("A typed WhenAny benchmark is already running.");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            }

            ValidateProductCommit(productCommit);

            GameObject runnerObject = new GameObject("Typed WhenAny Benchmark Runner");
            DontDestroyOnLoad(runnerObject);
            OnityTypedWhenAnyBenchmarkRunner runner =
                runnerObject.AddComponent<OnityTypedWhenAnyBenchmarkRunner>();
            runner.m_outputPath = Path.GetFullPath(outputPath);
            runner.m_productCommit = productCommit.ToLowerInvariant();
            runner.m_completed = completed;
            runner.m_allocationOnly = allocationOnly;
            runner.m_collectorDiagnostic = collectorDiagnostic;
            s_isRunning = true;
        }

        private void Awake()
        {
            m_measureBatch = MeasureBatch;
            m_emptyBatch = MeasureEmptyBatch;
            m_emptyOutstandingBatch = MeasureEmptyOutstandingBatch;
            m_positiveControl = AllocatePositiveControl;
        }

        private IEnumerator Start()
        {
            yield return null;
            m_originalTracker = OnityTaskTracker.IsEnabled;
            m_trackerStackTraceAtStart = OnityTaskTracker.EnableStackTrace;
            m_trackerCaptured = true;
#if UNITY_EDITOR
            m_profilerWasEnabled = Profiler.enabled;
            m_driverWasEnabled = ProfilerDriver.enabled;
            m_profileEditorWasEnabled = ProfilerDriver.profileEditor;
            m_callstacksWereEnabled = Profiler.enableAllocationCallstacks;
            m_deepProfilingAtStart = ProfilerDriver.deepProfiling;
            m_profilerCaptured = true;
#endif
            Exception failure = null;
            Report report = null;
            try
            {
                ValidateNumericSettings();
#if UNITY_EDITOR
                if (!m_allocationOnly && !m_collectorDiagnostic)
                {
                    Profiler.enabled = false;
                    ProfilerDriver.enabled = false;
                }
#endif
                Report expected = CreateReport(m_productCommit);
                if (m_collectorDiagnostic)
                {
                    report = expected;
                    report.collectorDiagnosticOnly = true;
                    report.scope = "Collector diagnostic only. Positive/empty controls and three "
                        + "first-observed pending scheduling routes; no numerical comparison.";
                }
                else if (m_allocationOnly)
                {
                    report = JsonUtility.FromJson<Report>(File.ReadAllText(m_outputPath));
                    ValidateAllocationInput(report, expected);
                    ClearAllocations(report);
                    report.allocationTrackerStackTraceEnabled = m_trackerStackTraceAtStart;
#if UNITY_EDITOR
                    report.allocationDeepProfilingEnabled = m_deepProfilingAtStart;
                    report.allocationCallstacksEnabled = m_callstacksWereEnabled;
#endif
                }
                else
                {
                    report = expected;
                    RunTiming(report);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

#if UNITY_EDITOR
            if (failure == null && (m_allocationOnly || m_collectorDiagnostic))
            {
                IEnumerator allocation = RunAllocations(report);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = allocation.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return allocation.Current;
                }
            }
#endif
            if (failure == null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(m_outputPath));
                    File.WriteAllText(m_outputPath, JsonUtility.ToJson(report, true));
                    Debug.Log("Typed WhenAny benchmark completed: " + m_outputPath, this);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure != null)
            {
                Debug.LogException(failure, this);
            }

            try
            {
                m_completed?.Invoke(m_outputPath, failure);
            }
            finally
            {
                RestoreState();
                s_isRunning = false;
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            RestoreState();
            s_isRunning = false;
        }

        private void RestoreState()
        {
            if (m_trackerCaptured)
            {
                OnityTaskTracker.IsEnabled = m_originalTracker;
                m_trackerCaptured = false;
            }
#if UNITY_EDITOR
            if (m_profilerCaptured)
            {
                Profiler.enabled = m_profilerWasEnabled;
                ProfilerDriver.enabled = m_driverWasEnabled;
                ProfilerDriver.profileEditor = m_profileEditorWasEnabled;
                Profiler.enableAllocationCallstacks = m_callstacksWereEnabled;
                m_profilerCaptured = false;
            }
#endif
        }

        private static void ValidateNumericSettings()
        {
            if (OnityTaskTracker.EnableStackTrace)
            {
                throw new InvalidOperationException(
                    "Typed WhenAny numerical runs require OnityTaskTracker.EnableStackTrace=false.");
            }
#if UNITY_EDITOR
            if (ProfilerDriver.deepProfiling || Profiler.enableAllocationCallstacks)
            {
                throw new InvalidOperationException(
                    "Typed WhenAny numerical runs require Deep Profiling and allocation callstacks off. "
                    + "DeepProfiling=" + ProfilerDriver.deepProfiling
                    + ", AllocationCallstacks=" + Profiler.enableAllocationCallstacks);
            }
#endif
        }

        /// <summary>
        /// Requires a full hexadecimal commit ID for typed WhenAny benchmark provenance.
        /// </summary>
        public static void ValidateProductCommit(string productCommit)
        {
            if (productCommit == null || productCommit.Length != 40)
            {
                throw new ArgumentException(
                    "Typed WhenAny benchmark requires -onityTaskProductCommit with a full 40-hex SHA.",
                    nameof(productCommit));
            }

            for (int i = 0; i < productCommit.Length; i++)
            {
                char digit = productCommit[i];
                if (!((digit >= '0' && digit <= '9') ||
                    (digit >= 'a' && digit <= 'f') ||
                    (digit >= 'A' && digit <= 'F')))
                {
                    throw new ArgumentException(
                        "Typed WhenAny benchmark requires -onityTaskProductCommit with a full 40-hex SHA.",
                        nameof(productCommit));
                }
            }
        }

        private static Report CreateReport(string productCommit)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Report report = new Report
            {
                schemaVersion = 2,
                harnessVersion = 2,
                suite = "Typed int two-input WhenAny native, UniTask, and legacy migration routes",
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scriptingBackend = "Editor Mono",
                uniTaskCommit = k_uniTaskCommit,
                productCommit = productCommit,
                onityAsyncSha256 = HashFile(root, k_runtimePath),
                completionSourceSha256 = HashFile(root, k_completionPath),
                runnerSha256 = HashFile(root, k_runnerPath),
                menuSha256 = HashFile(root, k_menuPath),
                runtimeAsmdefSha256 = HashFile(root, k_runtimeAsmdefPath),
                editorAsmdefSha256 = HashFile(root, k_editorAsmdefPath),
                manifestSha256 = HashFile(root, "Packages/manifest.json"),
                lockSha256 = HashFile(root, "Packages/packages-lock.json"),
                projectSettingsSha256 = HashFile(root, "ProjectSettings/ProjectSettings.asset"),
                operationsPerBatch = k_operations,
                outstandingBurstOperations = k_outstandingOperations,
                outstandingBurstWarmupBatches = 1,
                samplesPerCase = k_samples,
                warmupBatches = k_warmupBatches,
                successTimingBatches = k_successTimingBatches,
                failureTimingBatches = k_failureTimingBatches,
                stopwatchFrequency = Stopwatch.Frequency,
                timingTrackerStackTraceEnabled = OnityTaskTracker.EnableStackTrace,
#if UNITY_EDITOR
                timingDeepProfilingEnabled = ProfilerDriver.deepProfiling,
                timingCallstacksEnabled = Profiler.enableAllocationCallstacks,
#endif
                allocationCounter = "Unavailable: independent Profiler calibration pending.",
                positiveControlBytes = -1,
                emptyControlBytes = -1,
                outstandingEmptyControlBytes = -1,
                scope = "Main-thread Editor/Mono. Native Onity versus UniTask params is the "
                    + "primary comparison. Same-revision OnityAsync Task bridge is a migration "
                    + "route, not a product baseline. UniTask's two-element params array and "
                    + "legacy AsTask bridges, Task array, index mapping, and value observation "
                    + "are inside measured operations. Sources and fresh faults are prepared "
                    + "outside markers. Pending scheduling markers exclude completion and "
                    + "consumption; those occur during cleanup outside the marker. Pending "
                    + "lifecycle markers include winner completion and observation, loser "
                    + "completion and validation, and reference cleanup. Completed-input "
                    + "controls are lifecycle measurements: inputs are completed during "
                    + "preparation when both are complete; the second-completed control "
                    + "completes its loser inside the marker. Native tracker ON/OFF is "
                    + "OnityTask tracking only, not legacy Task-tracker observability. "
                    + "Legacy Task bridge tracker callbacks may run outside the main-thread "
                    + "marker. First-observed calls precede warmups in each process "
                    + "but are sequential per route, not three process-cold measurements. "
                    + "Tracker ON/OFF changes only Onity; UniTask retains its default "
                    + "task tracking. Worker allocations and Player performance are unavailable.",
                outstandingBurstScope = "384 pending outputs are scheduled before any input "
                    + "completes. Source preparation is outside the marker; winner and loser "
                    + "completion, output observation, and cleanup follow outside the marker. "
                    + "One unmeasured 384-operation warm-saturation burst per route primes "
                    + "state before eight separate scheduling samples. A bounded pool may "
                    + "still allocate when the outstanding count exceeds its capacity. "
                    + "This is a main-thread "
                    + "Editor/Mono scheduling slice, not a complete task lifecycle.",
                scenarios = new Scenario[12],
                firstObserved = new Metric[3],
                outstandingBurst = new Scenario[2]
            };

            int index = 0;
            for (int tracker = 1; tracker >= 0; tracker--)
            {
                report.scenarios[index++] = NewScenario("Pending success schedule", tracker != 0, 0, 0);
                report.scenarios[index++] = NewScenario("Pending success lifecycle", tracker != 0, 1, 0);
                report.scenarios[index++] = NewScenario("Pending fault lifecycle", tracker != 0, 1, 1);
                report.scenarios[index++] = NewScenario("Pending cancel lifecycle", tracker != 0, 1, 2);
                report.scenarios[index++] = NewScenario("Both completed, first wins", tracker != 0, 2, 0);
                report.scenarios[index++] = NewScenario("Second completed first", tracker != 0, 3, 0);
            }

            for (int route = 0; route < 3; route++)
            {
                report.firstObserved[route] = NewMetric(RouteName(route));
            }

            report.outstandingBurst[0] = NewScenario(
                "384 outstanding pending schedule", true, 0, 0);
            report.outstandingBurst[1] = NewScenario(
                "384 outstanding pending schedule", false, 0, 0);

            return report;
        }

        private static Scenario NewScenario(string name, bool trackerEnabled, int mode, int outcome)
        {
            Scenario scenario = new Scenario
            {
                name = name,
                trackerEnabled = trackerEnabled,
                mode = mode,
                outcome = outcome,
                results = new Metric[3]
            };
            for (int route = 0; route < 3; route++)
            {
                scenario.results[route] = NewMetric(RouteName(route));
            }

            return scenario;
        }

        private static Metric NewMetric(string library)
        {
            return new Metric
            {
                library = library,
                bytesPerOperation = -1,
                totalAllocatedBytes = -1
            };
        }

        private static string RouteName(int route)
        {
            return route == 0 ? "OnityTask native" :
                route == 1 ? "UniTask params" : "OnityAsync Task bridge migration";
        }

        private static bool IsNativePendingSourceType(string sourceType)
        {
            return sourceType == k_nativeSourceType + k_intSourceArgument ||
                sourceType == k_pooledNativeSourceType + k_intSourceArgument;
        }

        private static string GetNativePendingSourceType(object state)
        {
            Type concreteType = state?.GetType();
            if (concreteType == null || !concreteType.IsGenericType ||
                concreteType.Assembly != typeof(OnityTask<>).Assembly)
            {
                return null;
            }

            Type[] arguments = concreteType.GetGenericArguments();
            if (arguments.Length != 1 || arguments[0] != typeof(int))
            {
                return null;
            }

            string definition = concreteType.GetGenericTypeDefinition().FullName;
            if (definition != k_nativeSourceType &&
                definition != k_pooledNativeSourceType)
            {
                return null;
            }

            return definition + k_intSourceArgument;
        }

        private static string HashFile(string root, string relativePath)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(Path.Combine(root, relativePath)))
            {
                return BitConverter.ToString(hash.ComputeHash(stream))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void ValidateAllocationInput(Report report, Report expected)
        {
            if (report == null || report.schemaVersion != 2 || report.harnessVersion != 2 ||
                report.suite != expected.suite ||
                report.unityVersion != expected.unityVersion ||
                report.scriptingBackend != expected.scriptingBackend ||
                report.uniTaskCommit != expected.uniTaskCommit ||
                report.productCommit != expected.productCommit ||
                report.onityAsyncSha256 != expected.onityAsyncSha256 ||
                report.completionSourceSha256 != expected.completionSourceSha256 ||
                report.runnerSha256 != expected.runnerSha256 ||
                report.menuSha256 != expected.menuSha256 ||
                report.runtimeAsmdefSha256 != expected.runtimeAsmdefSha256 ||
                report.editorAsmdefSha256 != expected.editorAsmdefSha256 ||
                report.manifestSha256 != expected.manifestSha256 ||
                report.lockSha256 != expected.lockSha256 ||
                report.projectSettingsSha256 != expected.projectSettingsSha256 ||
                report.operationsPerBatch != k_operations ||
                report.outstandingBurstOperations != k_outstandingOperations ||
                report.outstandingBurstWarmupBatches != 1 ||
                report.samplesPerCase != k_samples ||
                report.warmupBatches != k_warmupBatches ||
                report.successTimingBatches != k_successTimingBatches ||
                report.failureTimingBatches != k_failureTimingBatches ||
                report.scope != expected.scope ||
                report.outstandingBurstScope != expected.outstandingBurstScope ||
                report.timingTrackerStackTraceEnabled ||
                report.timingDeepProfilingEnabled ||
                report.timingCallstacksEnabled ||
                !report.nativePendingGatePassed ||
                !IsNativePendingSourceType(report.nativePendingSourceType) ||
                report.scenarios == null || report.scenarios.Length != 12 ||
                report.firstObserved == null || report.firstObserved.Length != 3 ||
                report.outstandingBurst == null || report.outstandingBurst.Length != 2)
            {
                throw new InvalidDataException(
                    "Allocation pass requires timing from identical product, harness, and config.");
            }

            for (int route = 0; route < 3; route++)
            {
                if (report.firstObserved[route] == null ||
                    report.firstObserved[route].library != RouteName(route) ||
                    report.firstObserved[route].sampleNanosecondsPerOperation == null ||
                    report.firstObserved[route].sampleNanosecondsPerOperation.Length != 1)
                {
                    throw new InvalidDataException("First-observed timing sample is incomplete.");
                }
            }

            for (int i = 0; i < report.scenarios.Length; i++)
            {
                Scenario actual = report.scenarios[i];
                Scenario wanted = expected.scenarios[i];
                if (actual == null || actual.name != wanted.name ||
                    actual.trackerEnabled != wanted.trackerEnabled ||
                    actual.mode != wanted.mode || actual.outcome != wanted.outcome ||
                    actual.results == null || actual.results.Length != 3)
                {
                    throw new InvalidDataException("Timing scenario definitions changed.");
                }

                for (int route = 0; route < 3; route++)
                {
                    Metric metric = actual.results[route];
                    if (metric == null || metric.library != RouteName(route) ||
                        metric.sampleNanosecondsPerOperation == null ||
                        metric.sampleNanosecondsPerOperation.Length != k_samples)
                    {
                        throw new InvalidDataException("Timing samples are incomplete.");
                    }
                }
            }

            for (int i = 0; i < report.outstandingBurst.Length; i++)
            {
                Scenario actual = report.outstandingBurst[i];
                Scenario wanted = expected.outstandingBurst[i];
                if (actual == null || actual.name != wanted.name ||
                    actual.trackerEnabled != wanted.trackerEnabled ||
                    actual.mode != wanted.mode || actual.outcome != wanted.outcome ||
                    actual.results == null || actual.results.Length != 3)
                {
                    throw new InvalidDataException("Outstanding burst definitions changed.");
                }

                for (int route = 0; route < 3; route++)
                {
                    Metric metric = actual.results[route];
                    if (metric == null || metric.library != RouteName(route) ||
                        metric.sampleNanosecondsPerOperation == null ||
                        metric.sampleNanosecondsPerOperation.Length != k_samples ||
                        metric.sampleElapsedTicks == null ||
                        metric.sampleElapsedTicks.Length != k_samples)
                    {
                        throw new InvalidDataException("Outstanding burst timing is incomplete.");
                    }
                }
            }
        }

        private static void ClearAllocations(Report report)
        {
            report.allocationsAvailable = false;
            report.allocationCounter = "Unavailable: independent Profiler calibration pending.";
            report.positiveControlBytes = -1;
            report.emptyControlBytes = -1;
            report.emptyHarnessSampleAllocatedBytes = null;
            report.outstandingEmptyControlBytes = -1;
            report.outstandingEmptyHarnessSampleAllocatedBytes = null;
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                for (int route = 0; route < 3; route++)
                {
                    Metric metric = report.scenarios[scenario].results[route];
                    metric.bytesPerOperation = -1;
                    metric.totalAllocatedBytes = -1;
                    metric.sampleAllocatedBytes = null;
                }
            }

            for (int route = 0; route < 3; route++)
            {
                report.firstObserved[route].bytesPerOperation = -1;
                report.firstObserved[route].totalAllocatedBytes = -1;
                report.firstObserved[route].sampleAllocatedBytes = null;
            }

            for (int scenario = 0; scenario < report.outstandingBurst.Length; scenario++)
            {
                for (int route = 0; route < 3; route++)
                {
                    Metric metric = report.outstandingBurst[scenario].results[route];
                    metric.bytesPerOperation = -1;
                    metric.totalAllocatedBytes = -1;
                    metric.sampleAllocatedBytes = null;
                }
            }
        }

        private void RunTiming(Report report)
        {
            ValidateNumericSettings();
            m_activeScenario = report.scenarios[0];
            for (int route = 0; route < 3; route++)
            {
                m_activeRoute = route;
                PrepareBatch(1);
                long start = Stopwatch.GetTimestamp();
                MeasureBatch();
                long elapsed = Stopwatch.GetTimestamp() - start;
                FinishBatch();
                report.firstObserved[route].sampleNanosecondsPerOperation =
                    new[] { ToNanoseconds(elapsed) };
            }

            report.nativePendingGatePassed = m_nativeGateVerified;
            report.nativePendingSourceType = m_nativePendingSourceType;
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int warmup = 0; warmup < k_warmupBatches; warmup++)
                {
                    for (int turn = 0; turn < 3; turn++)
                    {
                        m_activeRoute = (warmup + turn) % 3;
                        PrepareBatch(k_operations);
                        MeasureBatch();
                        FinishBatch();
                    }
                }

                int batches = m_activeScenario.outcome == 0
                    ? k_successTimingBatches : k_failureTimingBatches;
                for (int route = 0; route < 3; route++)
                {
                    m_activeScenario.results[route].sampleNanosecondsPerOperation =
                        new double[k_samples];
                }

                for (int sample = 0; sample < k_samples; sample++)
                {
                    ValidateNumericSettings();
                    for (int turn = 0; turn < 3; turn++)
                    {
                        m_activeRoute = (sample + turn) % 3;
                        long totalTicks = 0;
                        for (int batch = 0; batch < batches; batch++)
                        {
                            PrepareBatch(k_operations);
                            long start = Stopwatch.GetTimestamp();
                            MeasureBatch();
                            totalTicks += Stopwatch.GetTimestamp() - start;
                            FinishBatch();
                        }

                        m_activeScenario.results[m_activeRoute]
                            .sampleNanosecondsPerOperation[sample] =
                            ToNanoseconds(totalTicks) / (batches * k_operations);
                    }
                }

                for (int route = 0; route < 3; route++)
                {
                    Metric metric = m_activeScenario.results[route];
                    double[] sorted = (double[])metric.sampleNanosecondsPerOperation.Clone();
                    Array.Sort(sorted);
                    metric.medianNanosecondsPerOperation =
                        (sorted[3] + sorted[4]) * 0.5d;
                }
            }

            RunOutstandingBurstTiming(report);
        }

        private void RunOutstandingBurstTiming(Report report)
        {
            for (int scenario = 0; scenario < report.outstandingBurst.Length; scenario++)
            {
                m_activeScenario = report.outstandingBurst[scenario];
                for (int route = 0; route < 3; route++)
                {
                    m_activeRoute = route;
                    PrepareBatch(k_outstandingOperations);
                    MeasureBatch();
                    FinishBatch();
                    m_activeScenario.results[route].sampleNanosecondsPerOperation =
                        new double[k_samples];
                    m_activeScenario.results[route].sampleElapsedTicks =
                        new long[k_samples];
                }

                for (int sample = 0; sample < k_samples; sample++)
                {
                    ValidateNumericSettings();
                    for (int turn = 0; turn < 3; turn++)
                    {
                        m_activeRoute = (sample + turn) % 3;
                        PrepareBatch(k_outstandingOperations);
                        long start = Stopwatch.GetTimestamp();
                        MeasureBatch();
                        long elapsed = Stopwatch.GetTimestamp() - start;
                        FinishBatch();
                        m_activeScenario.results[m_activeRoute]
                            .sampleElapsedTicks[sample] = elapsed;
                        m_activeScenario.results[m_activeRoute]
                            .sampleNanosecondsPerOperation[sample] =
                            ToNanoseconds(elapsed) / k_outstandingOperations;
                    }
                }

                for (int route = 0; route < 3; route++)
                {
                    Metric metric = m_activeScenario.results[route];
                    double[] sorted = (double[])metric.sampleNanosecondsPerOperation.Clone();
                    Array.Sort(sorted);
                    metric.medianNanosecondsPerOperation =
                        (sorted[3] + sorted[4]) * 0.5d;
                }
            }
        }

        private static double ToNanoseconds(long ticks)
        {
            return ticks * (1000000000d / Stopwatch.Frequency);
        }

        private bool m_nativeGateVerified;

        private void PrepareBatch(int count)
        {
            m_activeCount = count;
            OnityTaskTracker.IsEnabled = m_activeScenario.trackerEnabled;
            for (int i = 0; i < count; i++)
            {
                m_failures[i] = m_activeScenario.outcome == 1
                    ? new InvalidOperationException("typed WhenAny benchmark fault") : null;
                if (m_activeRoute == 1)
                {
                    m_uniFirst[i] = new UniTaskCompletionSource<int>();
                    m_uniSecond[i] = new UniTaskCompletionSource<int>();
                    if (m_activeScenario.mode == 2)
                    {
                        m_uniFirst[i].TrySetResult(FirstValue(i));
                        m_uniSecond[i].TrySetResult(SecondValue(i));
                    }
                    else if (m_activeScenario.mode == 3)
                    {
                        m_uniSecond[i].TrySetResult(SecondValue(i));
                    }
                }
                else
                {
                    m_onityFirst[i] = new OnityTaskCompletionSource<int>();
                    m_onitySecond[i] = new OnityTaskCompletionSource<int>();
                    if (m_activeScenario.mode == 2)
                    {
                        m_onityFirst[i].TrySetResult(FirstValue(i));
                        m_onitySecond[i].TrySetResult(SecondValue(i));
                    }
                    else if (m_activeScenario.mode == 3)
                    {
                        m_onitySecond[i].TrySetResult(SecondValue(i));
                    }
                }
            }
        }

        private static int FirstValue(int index) => 1000 + index;
        private static int SecondValue(int index) => 2000 + index;

        private void MeasureBatch()
        {
            for (int i = 0; i < m_activeCount; i++)
            {
                ScheduleOne(i);
                if (m_activeScenario.mode == 0)
                {
                    continue;
                }

                int winner = m_activeScenario.mode == 2 ? 0 :
                    m_activeScenario.mode == 3 ? 1 : i & 1;
                if (m_activeScenario.mode == 1)
                {
                    CompleteInput(i, winner, true);
                }

                ObserveWinner(i, winner);
                if (m_activeScenario.mode != 2)
                {
                    CompleteInput(i, 1 - winner, false);
                }

                ObserveLoser(i, 1 - winner);
                ClearOperation(i);
            }
        }

        private void ScheduleOne(int i)
        {
            if (m_activeRoute == 0)
            {
                m_nativeResults[i] = OnityTask.WhenAny<int>(
                    m_onityFirst[i].Task, m_onitySecond[i].Task);
            }
            else if (m_activeRoute == 1)
            {
                m_uniResults[i] = UniTask.WhenAny<int>(new[]
                {
                    m_uniFirst[i].Task, m_uniSecond[i].Task
                });
            }
            else
            {
                m_legacyFirst[i] = m_onityFirst[i].Task.AsTask();
                m_legacySecond[i] = m_onitySecond[i].Task.AsTask();
                m_legacyResults[i] = OnityAsync.WhenAny<int>(new[]
                {
                    m_legacyFirst[i], m_legacySecond[i]
                });
            }
        }

        private void FinishBatch()
        {
            if (m_activeScenario.mode != 0)
            {
                return;
            }

            for (int i = 0; i < m_activeCount; i++)
            {
                if (m_activeRoute == 0)
                {
                    if (m_nativeResults[i].IsCompleted)
                    {
                        throw new InvalidOperationException("Native result was not pending.");
                    }

                    if (!m_nativeGateVerified)
                    {
                        System.Reflection.FieldInfo stateField =
                            typeof(OnityTask<(int winnerIndex, int result)>).GetField(
                                "m_state", System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic);
                        object state = stateField?.GetValue(m_nativeResults[i]);
                        string sourceType = GetNativePendingSourceType(state);
                        if (sourceType == null)
                        {
                            throw new InvalidOperationException(
                                "Expected a native typed WhenAny source, not a Task bridge.");
                        }

                        m_nativePendingSourceType = sourceType;
                        m_nativeGateVerified = true;
                    }
                }
                else if (m_activeRoute == 1)
                {
                    if (m_uniResults[i].Status != UniTaskStatus.Pending)
                    {
                        throw new InvalidOperationException("UniTask result was not pending.");
                    }
                }
                else if (m_legacyResults[i].IsCompleted)
                {
                    throw new InvalidOperationException("Legacy result was not pending.");
                }

                int winner = i & 1;
                CompleteInput(i, winner, true);
                ObserveWinner(i, winner);
                CompleteInput(i, 1 - winner, false);
                ObserveLoser(i, 1 - winner);
                ClearOperation(i);
            }
        }

        private void ClearOperation(int i)
        {
            m_onityFirst[i] = null;
            m_onitySecond[i] = null;
            m_uniFirst[i] = null;
            m_uniSecond[i] = null;
            m_nativeResults[i] = default;
            m_uniResults[i] = default;
            m_legacyResults[i] = null;
            m_legacyFirst[i] = null;
            m_legacySecond[i] = null;
            m_failures[i] = null;
        }

        private void CompleteInput(int i, int input, bool winner)
        {
            if (m_activeRoute == 1)
            {
                UniTaskCompletionSource<int> source = input == 0 ? m_uniFirst[i] : m_uniSecond[i];
                bool completed = winner && m_activeScenario.outcome == 1
                    ? source.TrySetException(m_failures[i])
                    : winner && m_activeScenario.outcome == 2
                        ? source.TrySetCanceled(s_canceledToken)
                        : source.TrySetResult(input == 0 ? FirstValue(i) : SecondValue(i));
                if (!completed)
                {
                    throw new InvalidOperationException("UniTask input completion failed.");
                }
            }
            else
            {
                OnityTaskCompletionSource<int> source = input == 0
                    ? m_onityFirst[i] : m_onitySecond[i];
                bool completed = winner && m_activeScenario.outcome == 1
                    ? source.TrySetException(m_failures[i])
                    : winner && m_activeScenario.outcome == 2
                        ? source.TrySetCanceled(s_canceledToken)
                        : source.TrySetResult(input == 0 ? FirstValue(i) : SecondValue(i));
                if (!completed)
                {
                    throw new InvalidOperationException("Onity input completion failed.");
                }
            }
        }

        private void ObserveWinner(int i, int winner)
        {
            Task<int> winningTask = null;
            if (m_activeRoute == 2)
            {
                winningTask = m_legacyResults[i].GetAwaiter().GetResult();
                if (!m_legacyResults[i].IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException("Legacy WhenAny outer task did not succeed.");
                }

                Task<int> expectedTask = winner == 0 ? m_legacyFirst[i] : m_legacySecond[i];
                if (!ReferenceEquals(winningTask, expectedTask))
                {
                    throw new InvalidOperationException("Legacy winner Task identity or index was wrong.");
                }
            }

            int outcome = m_activeScenario.outcome;
            bool statusMatches = m_activeRoute == 0
                ? outcome == 0 ? m_nativeResults[i].IsCompletedSuccessfully :
                    outcome == 1 ? m_nativeResults[i].IsFaulted : m_nativeResults[i].IsCanceled
                : m_activeRoute == 1
                    ? m_uniResults[i].Status == (outcome == 0 ? UniTaskStatus.Succeeded :
                        outcome == 1 ? UniTaskStatus.Faulted : UniTaskStatus.Canceled)
                    : outcome == 0 ? winningTask.IsCompletedSuccessfully :
                        outcome == 1 ? winningTask.IsFaulted : winningTask.IsCanceled;
            if (!statusMatches)
            {
                throw new InvalidOperationException("Winner terminal status was wrong.");
            }

            if (outcome == 0)
            {
                int actualIndex;
                int actualValue;
                if (m_activeRoute == 0)
                {
                    (actualIndex, actualValue) = m_nativeResults[i].GetAwaiter().GetResult();
                }
                else if (m_activeRoute == 1)
                {
                    (actualIndex, actualValue) = m_uniResults[i].GetAwaiter().GetResult();
                }
                else
                {
                    actualIndex = ReferenceEquals(winningTask, m_legacyFirst[i]) ? 0 : 1;
                    actualValue = winningTask.GetAwaiter().GetResult();
                }

                if (actualIndex != winner ||
                    actualValue != (winner == 0 ? FirstValue(i) : SecondValue(i)))
                {
                    throw new InvalidOperationException("Winner index or value was wrong.");
                }

                return;
            }

            Exception observed = null;
            try
            {
                if (m_activeRoute == 0)
                {
                    m_nativeResults[i].GetAwaiter().GetResult();
                }
                else if (m_activeRoute == 1)
                {
                    m_uniResults[i].GetAwaiter().GetResult();
                }
                else
                {
                    winningTask.GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            if (outcome == 1 && !ReferenceEquals(observed, m_failures[i]))
            {
                throw new InvalidOperationException("Winner did not preserve prepared fault identity.");
            }

            if (outcome == 2 && !(observed is OperationCanceledException canceled &&
                canceled.CancellationToken == s_canceledToken))
            {
                throw new InvalidOperationException("Winner cancellation token was wrong.");
            }
        }

        private void ObserveLoser(int i, int loser)
        {
            int expected = loser == 0 ? FirstValue(i) : SecondValue(i);
            int actual = m_activeRoute == 1
                ? (loser == 0 ? m_uniFirst[i] : m_uniSecond[i]).Task.GetAwaiter().GetResult()
                : m_activeRoute == 2
                    ? (loser == 0 ? m_legacyFirst[i] : m_legacySecond[i]).GetAwaiter().GetResult()
                    : (loser == 0 ? m_onityFirst[i] : m_onitySecond[i]).Task
                        .GetAwaiter().GetResult();
            if (actual != expected)
            {
                throw new InvalidOperationException("Losing input result was wrong.");
            }
        }

        private static void AllocatePositiveControl()
        {
            byte[] bytes = new byte[65536];
            GC.KeepAlive(bytes);
        }

        private void MeasureEmptyBatch()
        {
            int value = 0;
            for (int i = 0; i < k_operations; i++)
            {
                value ^= i;
            }

            s_emptySink = value;
        }

        private void MeasureEmptyOutstandingBatch()
        {
            int value = 0;
            for (int i = 0; i < k_outstandingOperations; i++)
            {
                value ^= i;
            }

            s_emptySink = value;
        }

#if UNITY_EDITOR
        private IEnumerator RunAllocations(Report report)
        {
            ValidateNumericSettings();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;
            int startup = k_profilerStartupFrames;
            while (ProfilerDriver.lastFrameIndex < 2 && startup-- > 0)
            {
                yield return null;
            }

            m_capturePhase = "positive-control";
            yield return CaptureAllocation(m_positiveControl);
            if (!m_lastCaptureValid || m_lastCaptureBytes != 65568)
            {
                throw new InvalidDataException("Profiler 64 KiB positive control was not 65,568 bytes.");
            }

            report.positiveControlBytes = m_lastCaptureBytes;
            m_capturePhase = "empty-control";
            yield return CaptureAllocation(m_emptyBatch);
            if (!m_lastCaptureValid || m_lastCaptureBytes != 0)
            {
                throw new InvalidDataException("Profiler empty control was not zero bytes.");
            }

            report.emptyControlBytes = 0;
            report.emptyHarnessSampleAllocatedBytes = new long[k_samples];
            for (int sample = 0; sample < k_samples; sample++)
            {
                m_capturePhase = "empty-harness-" + sample.ToString(CultureInfo.InvariantCulture);
                yield return CaptureAllocation(m_emptyBatch);
                if (!m_lastCaptureValid || m_lastCaptureBytes != 0)
                {
                    throw new InvalidDataException("Profiler empty-harness sample was not zero bytes.");
                }

                report.emptyHarnessSampleAllocatedBytes[sample] = 0;
            }

            m_activeScenario = report.scenarios[0];
            for (int route = 0; route < 3; route++)
            {
                m_activeRoute = route;
                PrepareBatch(1);
                m_capturePhase = "first-observed";
                yield return CaptureAllocation(m_measureBatch);
                FinishBatch();
                if (!m_collectorDiagnostic)
                {
                    RequireCapture();
                    report.firstObserved[route].sampleAllocatedBytes =
                        new[] { m_lastCaptureBytes };
                    report.firstObserved[route].bytesPerOperation = m_lastCaptureBytes;
                    report.firstObserved[route].totalAllocatedBytes = m_lastCaptureBytes;
                }
            }

            report.nativePendingGatePassed = m_nativeGateVerified;
            if (m_collectorDiagnostic)
            {
                report.nativePendingSourceType = m_nativePendingSourceType;
                report.collectorCaptures = m_captureDiagnostics.ToArray();
                report.allocationCounter = "Unavailable: collector diagnostic only.";
                report.allocationGeneratedAtUtc =
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                yield break;
            }

            if (!report.nativePendingGatePassed)
            {
                throw new InvalidDataException("Native pending source gate did not pass.");
            }

            if (report.nativePendingSourceType != m_nativePendingSourceType)
            {
                throw new InvalidDataException(
                    "Native pending source changed between timing and allocation passes.");
            }

            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int warmup = 0; warmup < k_warmupBatches; warmup++)
                {
                    for (int turn = 0; turn < 3; turn++)
                    {
                        m_activeRoute = (warmup + turn) % 3;
                        PrepareBatch(k_operations);
                        MeasureBatch();
                        FinishBatch();
                    }
                }

                for (int route = 0; route < 3; route++)
                {
                    m_activeScenario.results[route].sampleAllocatedBytes = new long[k_samples];
                }

                for (int sample = 0; sample < k_samples; sample++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    for (int turn = 0; turn < 3; turn++)
                    {
                        m_activeRoute = (sample + turn) % 3;
                        PrepareBatch(k_operations);
                        m_capturePhase = "warm-sample-" + sample.ToString(CultureInfo.InvariantCulture);
                        yield return CaptureAllocation(m_measureBatch);
                        RequireCapture();
                        FinishBatch();
                        m_activeScenario.results[m_activeRoute]
                            .sampleAllocatedBytes[sample] = m_lastCaptureBytes;
                    }
                }

                for (int route = 0; route < 3; route++)
                {
                    Metric metric = m_activeScenario.results[route];
                    long total = 0;
                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        total += metric.sampleAllocatedBytes[sample];
                    }

                    metric.totalAllocatedBytes = total;
                    metric.bytesPerOperation = (double)total / (k_samples * k_operations);
                }
            }

            IEnumerator outstanding = RunOutstandingBurstAllocations(report);
            while (outstanding.MoveNext())
            {
                yield return outstanding.Current;
            }

            report.allocationsAvailable = true;
            report.allocationCounter =
                "Unity Profiler main-thread GC.Alloc; exact 65,568/0 controls, "
                + "eight zero 128-operation samples, and eight zero 384-operation samples.";
            report.allocationGeneratedAtUtc =
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        private IEnumerator RunOutstandingBurstAllocations(Report report)
        {
            m_activeScenario = null;
            m_capturePhase = "outstanding-empty-control";
            yield return CaptureAllocation(m_emptyOutstandingBatch);
            if (!m_lastCaptureValid || m_lastCaptureBytes != 0)
            {
                throw new InvalidDataException("Profiler 384-operation empty control was not zero bytes.");
            }

            report.outstandingEmptyControlBytes = 0;
            report.outstandingEmptyHarnessSampleAllocatedBytes = new long[k_samples];
            for (int sample = 0; sample < k_samples; sample++)
            {
                m_capturePhase = "outstanding-empty-harness-"
                    + sample.ToString(CultureInfo.InvariantCulture);
                yield return CaptureAllocation(m_emptyOutstandingBatch);
                if (!m_lastCaptureValid || m_lastCaptureBytes != 0)
                {
                    throw new InvalidDataException(
                        "Profiler 384-operation empty-harness sample was not zero bytes.");
                }

                report.outstandingEmptyHarnessSampleAllocatedBytes[sample] = 0;
            }

            for (int scenario = 0; scenario < report.outstandingBurst.Length; scenario++)
            {
                m_activeScenario = report.outstandingBurst[scenario];
                for (int route = 0; route < 3; route++)
                {
                    m_activeRoute = route;
                    PrepareBatch(k_outstandingOperations);
                    MeasureBatch();
                    FinishBatch();
                    m_activeScenario.results[route].sampleAllocatedBytes =
                        new long[k_samples];
                }

                for (int sample = 0; sample < k_samples; sample++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    for (int turn = 0; turn < 3; turn++)
                    {
                        m_activeRoute = (sample + turn) % 3;
                        PrepareBatch(k_outstandingOperations);
                        m_capturePhase = "outstanding-warm-sample-"
                            + sample.ToString(CultureInfo.InvariantCulture);
                        yield return CaptureAllocation(m_measureBatch);
                        RequireCapture();
                        FinishBatch();
                        m_activeScenario.results[m_activeRoute]
                            .sampleAllocatedBytes[sample] = m_lastCaptureBytes;
                    }
                }

                for (int route = 0; route < 3; route++)
                {
                    Metric metric = m_activeScenario.results[route];
                    long total = 0;
                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        total += metric.sampleAllocatedBytes[sample];
                    }

                    metric.totalAllocatedBytes = total;
                    metric.bytesPerOperation =
                        (double)total / (k_samples * k_outstandingOperations);
                }
            }
        }

        private void RequireCapture()
        {
            if (!m_lastCaptureValid)
            {
                throw new InvalidDataException("Profiler marker or sample was unavailable.");
            }
        }

        private IEnumerator CaptureAllocation(Action operation)
        {
            ValidateNumericSettings();
            m_lastCaptureValid = false;
            m_lastCaptureBytes = 0;
            string marker = k_markerPrefix +
                (m_markerSequence++).ToString(CultureInfo.InvariantCulture);
            CaptureDiagnostic diagnostic = new CaptureDiagnostic
            {
                phase = m_capturePhase,
                scenario = m_activeScenario == null ? "control" : m_activeScenario.name,
                route = m_activeScenario == null ? "control" : RouteName(m_activeRoute),
                marker = marker,
                emittingManagedThread = Thread.CurrentThread.ManagedThreadId,
                emissionFirstFrame = ProfilerDriver.firstFrameIndex,
                emissionLastFrame = ProfilerDriver.lastFrameIndex,
                previousSuccessFrame = m_lastFrame,
                profilerEnabled = Profiler.enabled,
                driverEnabled = ProfilerDriver.enabled,
                profileEditor = ProfilerDriver.profileEditor,
                deepProfiling = ProfilerDriver.deepProfiling,
                allocationCallstacks = Profiler.enableAllocationCallstacks,
                result = "marker absent"
            };
            Profiler.BeginSample(marker);
            try
            {
                operation();
            }
            finally
            {
                Profiler.EndSample();
            }

            diagnostic.postEmissionFirstFrame = ProfilerDriver.firstFrameIndex;
            diagnostic.postEmissionLastFrame = ProfilerDriver.lastFrameIndex;
            diagnostic.profilerEnabledAfter = Profiler.enabled;
            diagnostic.driverEnabledAfter = ProfilerDriver.enabled;
            diagnostic.profileEditorAfter = ProfilerDriver.profileEditor;
            ValidateNumericSettings();

            for (int wait = 0; wait < k_profilerReadFrames; wait++)
            {
                yield return null;
                int first = Math.Max(m_lastFrame, ProfilerDriver.firstFrameIndex);
                int last = ProfilerDriver.lastFrameIndex;
                diagnostic.readFirstFrame = first;
                diagnostic.readLastFrame = last;
                MarkerProbe probe = FindMarker(first, last, marker);
                if (probe.found)
                {
                    SetProbe(diagnostic, probe);
                    if (probe.truncated)
                    {
                        diagnostic.result = "marker tree truncated";
                    }
                    else if (probe.count != 1)
                    {
                        diagnostic.result = "duplicate unique marker";
                    }
                    else if (!m_mainThreadIdentified)
                    {
                        if (m_capturePhase == "positive-control" &&
                            probe.threadName == "Main Thread")
                        {
                            m_mainProfilerThreadId = probe.threadId;
                            m_mainManagedThreadId = diagnostic.emittingManagedThread;
                            m_mainThreadIdentified = true;
                        }
                        else
                        {
                            diagnostic.result = "main-thread identity unavailable";
                        }
                    }

                    if (diagnostic.result == "marker absent" && m_mainThreadIdentified)
                    {
                        if (probe.threadName != "Main Thread" ||
                            probe.threadId != m_mainProfilerThreadId ||
                            diagnostic.emittingManagedThread != m_mainManagedThreadId)
                        {
                            diagnostic.result = "marker on other thread";
                        }
                        else
                        {
                            m_lastCaptureBytes = probe.bytes;
                            m_lastFrame = probe.frame;
                            m_lastCaptureValid = true;
                            diagnostic.result = "main-thread marker";
                        }
                    }

                    RecordCapture(diagnostic);
                    yield break;
                }
            }

            MarkerProbe retained = FindMarker(
                ProfilerDriver.firstFrameIndex, ProfilerDriver.lastFrameIndex, marker);
            if (retained.found)
            {
                SetProbe(diagnostic, retained);
                diagnostic.result = retained.truncated ? "marker tree truncated" :
                    retained.frame < diagnostic.readFirstFrame ? "marker outside cursor" :
                    "marker on other thread or unreadable in cursor";
            }
            else
            {
                diagnostic.result = retained.invalidStream
                    ? "marker absent; invalid frame/thread stream observed"
                    : "marker absent from retained frames";
            }

            RecordCapture(diagnostic);
        }

        private void RecordCapture(CaptureDiagnostic diagnostic)
        {
            if (m_collectorDiagnostic)
            {
                m_captureDiagnostics.Add(diagnostic);
            }

            if (m_collectorDiagnostic || !m_lastCaptureValid)
            {
                Debug.Log("Typed WhenAny collector: " + JsonUtility.ToJson(diagnostic), this);
            }
        }

        private static void SetProbe(CaptureDiagnostic diagnostic, MarkerProbe probe)
        {
            diagnostic.foundFrame = probe.frame;
            diagnostic.threadIndex = probe.threadIndex;
            diagnostic.threadName = probe.threadName;
            diagnostic.threadGroupName = probe.threadGroupName;
            diagnostic.threadId = probe.threadId.ToString(CultureInfo.InvariantCulture);
            diagnostic.markerSample = probe.sample;
            diagnostic.markerCount = probe.count;
            diagnostic.markerAllocatedBytes = probe.bytes;
        }

        private static MarkerProbe FindMarker(int firstFrame, int lastFrame, string marker)
        {
            MarkerProbe result = new MarkerProbe { frame = -1, threadIndex = -1, sample = -1 };
            for (int frame = firstFrame; frame <= lastFrame; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using (RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread))
                    {
                        if (!data.valid)
                        {
                            if (thread == 0)
                            {
                                result.invalidStream = true;
                            }

                            break;
                        }

                        int markerId = data.GetMarkerId(marker);
                        if (markerId == FrameDataView.invalidMarkerId)
                        {
                            continue;
                        }

                        int allocationId = data.GetMarkerId("GC.Alloc");
                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            if (data.GetSampleMarkerId(sample) != markerId)
                            {
                                continue;
                            }

                            result.count++;
                            if (result.count > 1)
                            {
                                continue;
                            }

                            result.found = true;
                            result.frame = frame;
                            result.threadIndex = thread;
                            result.threadName = data.threadName;
                            result.threadGroupName = data.threadGroupName;
                            result.threadId = data.threadId;
                            result.sample = sample;
                            int end = sample + data.GetSampleChildrenCountRecursive(sample);
                            if (end >= data.sampleCount)
                            {
                                result.truncated = true;
                                continue;
                            }

                            for (int child = sample + 1; child <= end; child++)
                            {
                                if (data.GetSampleMarkerId(child) == allocationId)
                                {
                                    result.bytes += data.GetSampleMetadataAsLong(child, 0);
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        private struct MarkerProbe
        {
            public bool found;
            public bool invalidStream;
            public bool truncated;
            public int count;
            public int frame;
            public int threadIndex;
            public int sample;
            public string threadName;
            public string threadGroupName;
            public ulong threadId;
            public long bytes;
        }
#endif

        [Serializable]
        private sealed class CaptureDiagnostic
        {
            public string phase;
            public string scenario;
            public string route;
            public string marker;
            public int emittingManagedThread;
            public int emissionFirstFrame;
            public int emissionLastFrame;
            public int postEmissionFirstFrame;
            public int postEmissionLastFrame;
            public int previousSuccessFrame;
            public int readFirstFrame;
            public int readLastFrame;
            public bool profilerEnabled;
            public bool driverEnabled;
            public bool profileEditor;
            public bool deepProfiling;
            public bool allocationCallstacks;
            public bool profilerEnabledAfter;
            public bool driverEnabledAfter;
            public bool profileEditorAfter;
            public string result;
            public int foundFrame;
            public int threadIndex;
            public string threadName;
            public string threadGroupName;
            public string threadId;
            public int markerSample;
            public int markerCount;
            public long markerAllocatedBytes;
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion;
            public int harnessVersion;
            public string suite;
            public string generatedAtUtc;
            public string allocationGeneratedAtUtc;
            public string unityVersion;
            public string scriptingBackend;
            public string uniTaskCommit;
            public string productCommit;
            public string onityAsyncSha256;
            public string completionSourceSha256;
            public string runnerSha256;
            public string menuSha256;
            public string runtimeAsmdefSha256;
            public string editorAsmdefSha256;
            public string manifestSha256;
            public string lockSha256;
            public string projectSettingsSha256;
            public int operationsPerBatch;
            public int outstandingBurstOperations;
            public int outstandingBurstWarmupBatches;
            public int samplesPerCase;
            public int warmupBatches;
            public int successTimingBatches;
            public int failureTimingBatches;
            public long stopwatchFrequency;
            public string scope;
            public string outstandingBurstScope;
            public bool collectorDiagnosticOnly;
            public CaptureDiagnostic[] collectorCaptures;
            public bool timingTrackerStackTraceEnabled;
            public bool timingDeepProfilingEnabled;
            public bool timingCallstacksEnabled;
            public bool allocationTrackerStackTraceEnabled;
            public bool allocationDeepProfilingEnabled;
            public bool allocationCallstacksEnabled;
            public bool nativePendingGatePassed;
            public string nativePendingSourceType;
            public bool allocationsAvailable;
            public string allocationCounter;
            public long positiveControlBytes;
            public long emptyControlBytes;
            public long[] emptyHarnessSampleAllocatedBytes;
            public long outstandingEmptyControlBytes;
            public long[] outstandingEmptyHarnessSampleAllocatedBytes;
            public Metric[] firstObserved;
            public Scenario[] scenarios;
            public Scenario[] outstandingBurst;
        }

        [Serializable]
        private sealed class Scenario
        {
            public string name;
            public bool trackerEnabled;
            public int mode;
            public int outcome;
            public Metric[] results;
        }

        [Serializable]
        private sealed class Metric
        {
            public string library;
            public double medianNanosecondsPerOperation;
            public double bytesPerOperation;
            public long totalAllocatedBytes;
            public double[] sampleNanosecondsPerOperation;
            public long[] sampleElapsedTicks;
            public long[] sampleAllocatedBytes;
        }
    }
}
