using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
    /// Measures matched typed WhenAll inputs and lifecycle outcomes in Unity Play Mode.
    /// Input sources and arrays are prepared before each measured slice.
    /// </summary>
    public sealed class OnityTypedWhenAllBenchmarkRunner : MonoBehaviour
    {
        private const int k_operations = 128;
        private const int k_saturationOperations = 384;
        private const int k_samples = 8;
        private const int k_prebridgeDiagnosticSamples = 32;
        private const int k_prebridgeScenarioIndex = 10;
        private const int k_timingBatches = 16;
        private const int k_warmupBatches = 10;
        private const int k_profilerStartupFrames = 120;
        private const int k_profilerReadFrames = 60;
        private const int k_harnessVersion = 7;
        private const int k_prebridgeDiagnosticVersion = 8;
        private const int k_prebridgeAttributionVersion = 1;
        private const string k_uniTaskCommit = "2e993ff18f28c931602a07292df0b0804eebef99";
        private const string k_scopePrefix = "Onity.TypedWhenAll.Allocation.";
        private const string k_windowPrefix = "Onity.TypedWhenAll.AllThreadWindow.";
        private const string k_runtimePath =
            "Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityAsync.cs";
        private const string k_completionSourcePath =
            "Packages/com.onity.framework/Runtime/Unity/Scripts/Async/OnityTaskCompletionSource.cs";
        private const string k_runnerPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Runtime/OnityTaskBenchmarkRunner.cs";
        private const string k_menuPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Editor/OnityTaskBenchmarkMenu.cs";
        private const string k_runtimeAsmdefPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Runtime/Onity.TaskBenchmarks.asmdef";
        private const string k_editorAsmdefPath =
            "Packages/com.onity.framework/Benchmarks/Tasks/Editor/Onity.TaskBenchmarks.Editor.asmdef";
        private const string k_manifestPath = "Packages/manifest.json";
        private const string k_lockPath = "Packages/packages-lock.json";

        private static bool s_isRunning;
        private static readonly WaitCallback s_workerPositiveControl =
            RunWorkerPositiveControl;

        private readonly OnityTaskCompletionSource<int>[] m_onityFirstSources =
            new OnityTaskCompletionSource<int>[k_operations];
        private readonly OnityTaskCompletionSource<int>[] m_onitySecondSources =
            new OnityTaskCompletionSource<int>[k_operations];
        private readonly OnityTask<int>[] m_onityFirstTasks = new OnityTask<int>[k_operations];
        private readonly OnityTask<int>[] m_onitySecondTasks = new OnityTask<int>[k_operations];
        private readonly OnityTask<int[]>[] m_onityResults = new OnityTask<int[]>[k_operations];
        private readonly OnityTask<int>[][] m_onityInputs = new OnityTask<int>[k_operations][];
        private readonly System.Threading.Tasks.Task<int>[] m_onityFirstBridges =
            new System.Threading.Tasks.Task<int>[k_operations];
        private readonly System.Threading.Tasks.Task<int>[] m_onitySecondBridges =
            new System.Threading.Tasks.Task<int>[k_operations];
        private readonly UniTaskCompletionSource<int>[] m_uniTaskFirstSources =
            new UniTaskCompletionSource<int>[k_operations];
        private readonly UniTaskCompletionSource<int>[] m_uniTaskSecondSources =
            new UniTaskCompletionSource<int>[k_operations];
        private readonly UniTask<int>[] m_uniTaskFirstTasks = new UniTask<int>[k_operations];
        private readonly UniTask<int>[] m_uniTaskSecondTasks = new UniTask<int>[k_operations];
        private readonly UniTask<int[]>[] m_uniTaskResults = new UniTask<int[]>[k_operations];
        private readonly UniTask<int>[][] m_uniTaskInputs = new UniTask<int>[k_operations][];
        private readonly OnityTaskCompletionSource<int>[] m_burstOnityFirstSources =
            new OnityTaskCompletionSource<int>[k_saturationOperations];
        private readonly OnityTaskCompletionSource<int>[] m_burstOnitySecondSources =
            new OnityTaskCompletionSource<int>[k_saturationOperations];
        private readonly OnityTask<int>[] m_burstOnityFirstTasks =
            new OnityTask<int>[k_saturationOperations];
        private readonly OnityTask<int>[] m_burstOnitySecondTasks =
            new OnityTask<int>[k_saturationOperations];
        private readonly OnityTask<int[]>[] m_burstOnityResults =
            new OnityTask<int[]>[k_saturationOperations];
        private readonly OnityTask<int>[][] m_burstOnityInputs =
            new OnityTask<int>[k_saturationOperations][];
        private readonly UniTaskCompletionSource<int>[] m_burstUniTaskFirstSources =
            new UniTaskCompletionSource<int>[k_saturationOperations];
        private readonly UniTaskCompletionSource<int>[] m_burstUniTaskSecondSources =
            new UniTaskCompletionSource<int>[k_saturationOperations];
        private readonly UniTask<int>[] m_burstUniTaskFirstTasks =
            new UniTask<int>[k_saturationOperations];
        private readonly UniTask<int>[] m_burstUniTaskSecondTasks =
            new UniTask<int>[k_saturationOperations];
        private readonly UniTask<int[]>[] m_burstUniTaskResults =
            new UniTask<int[]>[k_saturationOperations];
        private readonly UniTask<int>[][] m_burstUniTaskInputs =
            new UniTask<int>[k_saturationOperations][];
        private readonly InvalidOperationException[] m_onityFailures =
            new InvalidOperationException[k_operations];
        private readonly InvalidOperationException[] m_uniTaskFailures =
            new InvalidOperationException[k_operations];
        private readonly List<OnityTrackedTaskInfo> m_trackedTasks =
            new List<OnityTrackedTaskInfo>(1024);
        private readonly int[] m_trackedTaskIds = new int[k_operations];
        private readonly ManualResetEventSlim m_workerControlDone =
            new ManualResetEventSlim(false);
        private readonly CancellationToken m_canceledToken = new CancellationToken(true);

        private string m_outputPath;
        private Action<string, Exception> m_completed;
        private bool m_allocationOnly;
        private bool m_prebridgeDiagnostic;
        private bool m_prebridgeAttribution;
        private bool m_originalTrackerEnabled;
        private TypedWhenAllScenario m_activeScenario;
        private int m_activeLibrary;
        private Action m_scheduleBatch;
        private Action m_lifecycleBatch;
        private Action m_emptyBatch;
        private Action m_positiveControl;
        private Action m_scheduleFirst;
        private Action m_scheduleBurst;
        private Action m_trackedLifecycleBatch;
        private Action m_workerPositiveBatch;
        private bool m_trackerStateCaptured;
#if UNITY_EDITOR
        private bool m_profilerStateCaptured;
        private bool m_profilerWasEnabled;
        private bool m_profilerDriverWasEnabled;
        private bool m_profileEditorWasEnabled;
        private bool m_allocationCallstacksWereEnabled;
        private int m_lastCapturedFrame = -1;
        private int m_markerSequence;
        private bool m_lastAllocationValid;
        private long m_lastAllocationBytes;
        private string m_lastAllocationMarker;
        private int m_lastAllocationThread;
        private int m_lastAllocationMarkerSample;
        private bool m_lastWindowValid;
        private long m_lastWindowMainBytes;
        private long m_lastWindowOtherThreadBytes;
#endif

        private void Awake()
        {
            m_scheduleBatch = ScheduleBatch;
            m_lifecycleBatch = RunLifecycleBatch;
            m_emptyBatch = EmptyBatch;
            m_positiveControl = AllocatePositiveControl;
            m_scheduleFirst = ScheduleFirst;
#if UNITY_EDITOR
            m_scheduleBurst = ScheduleBurst;
            m_trackedLifecycleBatch = RunTrackedLifecycleBatch;
            m_workerPositiveBatch = RunWorkerPositiveBatch;
#endif
        }

        /// <summary>Queues the isolated two-input WhenAll comparison in Play Mode.</summary>
        /// <param name="outputPath">Absolute report JSON path.</param>
        /// <param name="completed">Receives the report path and any failure.</param>
        /// <param name="allocationOnly">Adds calibrated Profiler samples to a timing report.</param>
        /// <param name="prebridgeDiagnostic">Measures only prebridged pending faults.</param>
        /// <param name="prebridgeAttribution">Records allocation callstacks for that case.</param>
        public static void Run(string outputPath, Action<string, Exception> completed,
            bool allocationOnly, bool prebridgeDiagnostic = false,
            bool prebridgeAttribution = false)
        {
            if (s_isRunning)
            {
                throw new InvalidOperationException("A WhenAll benchmark is already running.");
            }
            if ((allocationOnly && prebridgeDiagnostic) ||
                (prebridgeAttribution && (allocationOnly || prebridgeDiagnostic)))
            {
                throw new ArgumentException("Benchmark modes are mutually exclusive.");
            }

            GameObject runnerObject = new GameObject("Typed WhenAll Benchmark Runner");
            DontDestroyOnLoad(runnerObject);
            OnityTypedWhenAllBenchmarkRunner runner =
                runnerObject.AddComponent<OnityTypedWhenAllBenchmarkRunner>();
            runner.m_outputPath = Path.GetFullPath(outputPath);
            runner.m_completed = completed;
            runner.m_allocationOnly = allocationOnly;
            runner.m_prebridgeDiagnostic = prebridgeDiagnostic;
            runner.m_prebridgeAttribution = prebridgeAttribution;
            s_isRunning = true;
        }

        private IEnumerator Start()
        {
            yield return null;
            m_originalTrackerEnabled = OnityTaskTracker.IsEnabled;
            m_trackerStateCaptured = true;
#if UNITY_EDITOR
            m_profilerWasEnabled = Profiler.enabled;
            m_profilerDriverWasEnabled = ProfilerDriver.enabled;
            m_profileEditorWasEnabled = ProfilerDriver.profileEditor;
            m_allocationCallstacksWereEnabled = Profiler.enableAllocationCallstacks;
            m_profilerStateCaptured = true;
            if (!m_allocationOnly && !m_prebridgeDiagnostic && !m_prebridgeAttribution)
            {
                Profiler.enabled = false;
                ProfilerDriver.enabled = false;
            }
#endif

            Exception failure = null;
            TypedWhenAllReport report = null;
            PrebridgeAttributionReport attributionReport = null;
            try
            {
                if (OnityTaskTracker.EnableStackTrace)
                {
                    throw new InvalidOperationException(
                        "Disable OnityTask tracker stack traces before benchmarking.");
                }

                TypedWhenAllReport expected = CreateReport();
                if (m_prebridgeAttribution)
                {
                    report = CreatePrebridgeDiagnosticReport(expected);
                    attributionReport = CreatePrebridgeAttributionReport(report);
                }
                else if (m_prebridgeDiagnostic)
                {
                    report = CreatePrebridgeDiagnosticReport(expected);
                }
                else if (m_allocationOnly)
                {
                    report = JsonUtility.FromJson<TypedWhenAllReport>(File.ReadAllText(m_outputPath));
                    ValidateAllocationInput(report, expected);
                    ClearAllocations(report);
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
            if (failure == null &&
                (m_allocationOnly || m_prebridgeDiagnostic || m_prebridgeAttribution))
            {
                IEnumerator allocation = m_prebridgeAttribution
                    ? RunPrebridgeAttribution(attributionReport, report.scenarios[0])
                    : RunAllocations(report);
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

                if (failure == null && !m_prebridgeDiagnostic && !m_prebridgeAttribution)
                {
                    try
                    {
                        Profiler.enabled = false;
                        ProfilerDriver.enabled = false;
                        RunWorkerAllocations(report);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }
            }
#endif

            if (failure == null)
            {
                try
                {
                    if (m_prebridgeAttribution)
                    {
                        SaveAttributionReport(attributionReport);
                    }
                    else
                    {
                        SaveReport(report);
                    }
                    Debug.Log("Typed WhenAll benchmark completed: " + m_outputPath, this);
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
            m_workerControlDone.Dispose();
            s_isRunning = false;
        }

        private static TypedWhenAllReport CreateReport()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return new TypedWhenAllReport
            {
                schemaVersion = 1,
                harnessVersion = k_harnessVersion,
                suite = "Typed int two-input WhenAll pending lifecycle and completed controls",
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
#if ENABLE_IL2CPP
                scriptingBackend = "IL2CPP",
#else
                scriptingBackend = "Mono",
#endif
                onityAsyncSha256 = HashFile(root, k_runtimePath),
                completionSourceSha256 = HashFile(root, k_completionSourcePath),
                runnerSha256 = HashFile(root, k_runnerPath),
                menuSha256 = HashFile(root, k_menuPath),
                runtimeAsmdefSha256 = HashFile(root, k_runtimeAsmdefPath),
                editorAsmdefSha256 = HashFile(root, k_editorAsmdefPath),
                manifestSha256 = HashFile(root, k_manifestPath),
                lockSha256 = HashFile(root, k_lockPath),
                uniTaskCommit = k_uniTaskCommit,
                operationsPerBatch = k_operations,
                timingBatchesPerSample = k_timingBatches,
                samplesPerCase = k_samples,
                warmupBatches = k_warmupBatches,
                stopwatchFrequency = Stopwatch.Frequency,
                scope = "Same prebuilt typed input array passed to WhenAll<int> per operation. Pending cases prepare "
                    + "fresh unresolved inputs outside each slice; completed controls use two or four completed "
                    + "inputs. Scheduling scenarios include WhenAll and "
                    + "storage; lifecycle scenarios include scheduling, input completion, output "
                    + "GetResult, and cleanup. Input construction is outside both slices. "
                    + "Fault cases use a fresh exception per operation, prepared outside the "
                    + "slice; the Onity-only bridge diagnostic also creates input AsTask "
                    + "bridges before the slice and leaves their exceptions unobserved. "
                    + "Cancel cases use a precreated token. Tracker-on matches "
                    + "Onity default; tracker-off changes only Onity. UniTask stays at its default. "
                    + "Profiler lifecycle bytes cover only the main-thread marker; tracker "
                    + "continuations may allocate later on worker threads. Samples include "
                    + "harness cost without subtraction. Editor/Mono only; no "
                    + "Player, frame-time, or IL2CPP result.",
                allocationCounter = "Unavailable: separate Profiler pass not run.",
                coldScheduling = new[]
                {
                    new TypedWhenAllExtraMetric { name = "First pending schedule; tracker off",
                        library = "OnityTask", operations = 1 },
                    new TypedWhenAllExtraMetric { name = "First pending schedule; tracker off",
                        library = "UniTask", operations = 1 }
                },
                burstScheduling = new[]
                {
                    new TypedWhenAllExtraMetric { name = "384 outstanding pending schedule; tracker off",
                        library = "OnityTask", operations = k_saturationOperations },
                    new TypedWhenAllExtraMetric { name = "384 outstanding pending schedule; tracker off",
                        library = "UniTask", operations = k_saturationOperations }
                },
                scenarios = new[]
                {
                    NewScenario("Pending success lifecycle; tracker on", true, true, true, 0),
                    NewScenario("Pending success lifecycle; tracker off", true, false, true, 0),
                    NewScenario("Pending success schedule; tracker on", true, true, false, 0),
                    NewScenario("Pending success schedule; tracker off", true, false, false, 0),
                    NewScenario("Pending fault lifecycle; tracker on", true, true, true, 1),
                    NewScenario("Pending fault lifecycle; tracker off", true, false, true, 1),
                    NewScenario("Pending cancel lifecycle; tracker on", true, true, true, 2),
                    NewScenario("Pending cancel lifecycle; tracker off", true, false, true, 2),
                    NewScenario("Both completed schedule; tracker on", false, true, false, 0),
                    NewScenario("Four completed schedule; tracker on", false, true, false, 0,
                        false, 4),
                    NewScenario("Pending fault lifecycle, input bridges present; tracker off",
                        true, false, true, 1, true)
                }
            };
        }

        private static TypedWhenAllReport CreatePrebridgeDiagnosticReport(
            TypedWhenAllReport report)
        {
            report.harnessVersion = k_prebridgeDiagnosticVersion;
            report.suite = "Typed int prebridged pending-fault allocation diagnostic";
            report.samplesPerCase = k_prebridgeDiagnosticSamples;
            report.timingBatchesPerSample = 0;
            report.scope = "Only the Onity tracker-off, prebridged pending-fault lifecycle. "
                + "Each sample includes 128 WhenAll<int> schedules, input completion, output "
                + "GetResult, and cleanup. Fresh sources, typed arrays, input AsTask bridges, "
                + "and per-operation exceptions are prepared outside the Profiler marker. "
                + "Input bridge exceptions remain unobserved. This is a main-thread Editor/Mono "
                + "allocation diagnostic, not a timing or all-thread comparison.";
            report.coldScheduling = null;
            report.burstScheduling = null;
            TypedWhenAllScenario scenario = report.scenarios[k_prebridgeScenarioIndex];
            if (!scenario.pending || scenario.onityTrackerEnabled || !scenario.fullLifecycle ||
                scenario.outcome != 1 || !scenario.bridgePresent ||
                scenario.results.Length != 1)
            {
                throw new InvalidDataException("Prebridge scenario definition changed.");
            }

            report.scenarios = new[] { scenario };
            return report;
        }

        private static PrebridgeAttributionReport CreatePrebridgeAttributionReport(
            TypedWhenAllReport source)
        {
            return new PrebridgeAttributionReport
            {
                schemaVersion = 1,
                attributionVersion = k_prebridgeAttributionVersion,
                suite = "Typed int prebridged pending-fault callstack attribution",
                generatedAtUtc = source.generatedAtUtc,
                unityVersion = source.unityVersion,
                scriptingBackend = source.scriptingBackend,
                onityAsyncSha256 = source.onityAsyncSha256,
                completionSourceSha256 = source.completionSourceSha256,
                runnerSha256 = source.runnerSha256,
                menuSha256 = source.menuSha256,
                runtimeAsmdefSha256 = source.runtimeAsmdefSha256,
                editorAsmdefSha256 = source.editorAsmdefSha256,
                manifestSha256 = source.manifestSha256,
                lockSha256 = source.lockSha256,
                uniTaskCommit = source.uniTaskCommit,
                scenario = source.scenarios[0].name,
                operationsPerSample = k_operations,
                samplesPerCase = k_prebridgeDiagnosticSamples,
                warmupBatches = k_warmupBatches,
                scope = source.scope,
                emptyHarnessSamples = new PrebridgeAttributionSample[k_prebridgeDiagnosticSamples],
                samples = new PrebridgeAttributionSample[k_prebridgeDiagnosticSamples]
            };
        }

        private static TypedWhenAllScenario NewScenario(string name, bool pending,
            bool onityTrackerEnabled, bool fullLifecycle, int outcome,
            bool bridgePresent = false, int inputCount = 2)
        {
            return new TypedWhenAllScenario
            {
                name = name,
                pending = pending,
                onityTrackerEnabled = onityTrackerEnabled,
                fullLifecycle = fullLifecycle,
                outcome = outcome,
                bridgePresent = bridgePresent,
                inputCount = inputCount,
                timingBatches = outcome == 0 ? k_timingBatches : 2,
                results = bridgePresent
                    ? new[] { NewMetric("OnityTask") }
                    : new[] { NewMetric("OnityTask"), NewMetric("UniTask") }
            };
        }

        private static TypedWhenAllMetric NewMetric(string library)
        {
            return new TypedWhenAllMetric
            {
                library = library, bytesPerOperation = -1,
                workerBytesPerOperation = -1
            };
        }

        private static string HashFile(string root, string relativePath)
        {
            string path = Path.Combine(root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void ValidateAllocationInput(TypedWhenAllReport report,
            TypedWhenAllReport expected)
        {
            if (report == null || report.schemaVersion != expected.schemaVersion ||
                report.harnessVersion != expected.harnessVersion ||
                report.unityVersion != expected.unityVersion ||
                report.scriptingBackend != expected.scriptingBackend ||
                report.onityAsyncSha256 != expected.onityAsyncSha256 ||
                report.completionSourceSha256 != expected.completionSourceSha256 ||
                report.runnerSha256 != expected.runnerSha256 ||
                report.menuSha256 != expected.menuSha256 ||
                report.runtimeAsmdefSha256 != expected.runtimeAsmdefSha256 ||
                report.editorAsmdefSha256 != expected.editorAsmdefSha256 ||
                report.manifestSha256 != expected.manifestSha256 ||
                report.lockSha256 != expected.lockSha256 ||
                report.uniTaskCommit != expected.uniTaskCommit ||
                report.operationsPerBatch != expected.operationsPerBatch ||
                report.timingBatchesPerSample != expected.timingBatchesPerSample ||
                report.samplesPerCase != expected.samplesPerCase ||
                report.scenarios == null ||
                report.scenarios.Length != expected.scenarios.Length)
            {
                throw new InvalidDataException(
                    "Allocation pass requires timing data from this exact source and harness.");
            }

            for (int i = 0; i < expected.scenarios.Length; i++)
            {
                if (report.scenarios[i] == null ||
                    report.scenarios[i].name != expected.scenarios[i].name ||
                    report.scenarios[i].pending != expected.scenarios[i].pending ||
                    report.scenarios[i].onityTrackerEnabled !=
                    expected.scenarios[i].onityTrackerEnabled ||
                    report.scenarios[i].fullLifecycle != expected.scenarios[i].fullLifecycle ||
                    report.scenarios[i].outcome != expected.scenarios[i].outcome ||
                    report.scenarios[i].bridgePresent != expected.scenarios[i].bridgePresent ||
                    report.scenarios[i].inputCount != expected.scenarios[i].inputCount ||
                    report.scenarios[i].timingBatches != expected.scenarios[i].timingBatches ||
                    report.scenarios[i].results == null ||
                    report.scenarios[i].results.Length != expected.scenarios[i].results.Length)
                {
                    throw new InvalidDataException("WhenAll scenario definitions changed.");
                }
            }
        }

        private void RunTiming(TypedWhenAllReport report)
        {
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int warmup = 0; warmup < k_warmupBatches; warmup++)
                {
                    for (int library = 0; library < m_activeScenario.results.Length; library++)
                    {
                        m_activeLibrary = library;
                        PrepareBatch();
                        ScheduleBatch();
                        FinishBatch();
                    }
                }
            }

            report.emptyHarnessSampleNanosecondsPerOperation = new double[k_samples];
            for (int sample = 0; sample < k_samples; sample++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int batch = 0; batch < k_timingBatches; batch++)
                {
                    m_emptyBatch();
                }

                report.emptyHarnessSampleNanosecondsPerOperation[sample] =
                    NanosecondsPerOperation(Stopwatch.GetTimestamp() - start,
                        k_operations * k_timingBatches);
            }

            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                TypedWhenAllMetric[] metrics = m_activeScenario.results;
                for (int library = 0; library < metrics.Length; library++)
                {
                    metrics[library].sampleNanosecondsPerOperation = new double[k_samples];
                }
                for (int sample = 0; sample < k_samples; sample++)
                {
                    for (int turn = 0; turn < metrics.Length; turn++)
                    {
                        m_activeLibrary = (sample + turn) % metrics.Length;
                        ForceFullGc();
                        long elapsedTicks = 0;
                        for (int batch = 0; batch < m_activeScenario.timingBatches; batch++)
                        {
                            PrepareBatch();
                            long start = Stopwatch.GetTimestamp();
                            if (m_activeScenario.fullLifecycle)
                            {
                                m_lifecycleBatch();
                            }
                            else
                            {
                                m_scheduleBatch();
                            }
                            elapsedTicks += Stopwatch.GetTimestamp() - start;
                            if (!m_activeScenario.fullLifecycle)
                            {
                                FinishBatch();
                            }
                        }

                        metrics[m_activeLibrary].sampleNanosecondsPerOperation[sample] =
                            NanosecondsPerOperation(elapsedTicks,
                                k_operations * m_activeScenario.timingBatches);
                    }
                }

                for (int library = 0; library < metrics.Length; library++)
                {
                    SummarizeTiming(metrics[library]);
                }
            }
        }

        private static double NanosecondsPerOperation(long ticks, int operations)
        {
            return (double)ticks * 1000000000d / (Stopwatch.Frequency * operations);
        }

        private static void SummarizeTiming(TypedWhenAllMetric metric)
        {
            double[] samples = metric.sampleNanosecondsPerOperation;
            double total = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                total += samples[i];
            }

            metric.meanNanosecondsPerOperation = total / samples.Length;
            double[] sorted = (double[])samples.Clone();
            Array.Sort(sorted);
            metric.medianNanosecondsPerOperation =
                (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2d;
            metric.minNanosecondsPerOperation = sorted[0];
            metric.maxNanosecondsPerOperation = sorted[sorted.Length - 1];
            double squared = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double delta = samples[i] - metric.meanNanosecondsPerOperation;
                squared += delta * delta;
            }

            metric.standardDeviationNanosecondsPerOperation =
                Math.Sqrt(squared / samples.Length);
        }

        private void PrepareBatch()
        {
            OnityTaskTracker.IsEnabled = m_activeScenario.onityTrackerEnabled;
            if (m_activeLibrary == 0)
            {
                for (int i = 0; i < k_operations; i++)
                {
                    if (m_activeScenario.pending)
                    {
                        m_onityFirstSources[i] = new OnityTaskCompletionSource<int>();
                        m_onitySecondSources[i] = new OnityTaskCompletionSource<int>();
                        m_onityFirstTasks[i] = m_onityFirstSources[i].Task;
                        m_onitySecondTasks[i] = m_onitySecondSources[i].Task;
                        if (m_activeScenario.bridgePresent)
                        {
                            m_onityFirstBridges[i] = m_onityFirstTasks[i].AsTask();
                            m_onitySecondBridges[i] = m_onitySecondTasks[i].AsTask();
                        }
                        m_onityFailures[i] = m_activeScenario.outcome == 1
                            ? new InvalidOperationException("Expected benchmark fault") : null;
                    }
                    else
                    {
                        m_onityFirstTasks[i] = OnityTask.FromResult(41);
                        m_onitySecondTasks[i] = OnityTask.FromResult(42);
                    }
                    m_onityInputs[i] = m_activeScenario.inputCount == 4
                        ? new[] { m_onityFirstTasks[i], m_onitySecondTasks[i],
                            OnityTask.FromResult(43), OnityTask.FromResult(44) }
                        : new[] { m_onityFirstTasks[i], m_onitySecondTasks[i] };
                }
            }
            else
            {
                for (int i = 0; i < k_operations; i++)
                {
                    if (m_activeScenario.pending)
                    {
                        m_uniTaskFirstSources[i] = new UniTaskCompletionSource<int>();
                        m_uniTaskSecondSources[i] = new UniTaskCompletionSource<int>();
                        m_uniTaskFirstTasks[i] = m_uniTaskFirstSources[i].Task;
                        m_uniTaskSecondTasks[i] = m_uniTaskSecondSources[i].Task;
                        m_uniTaskFailures[i] = m_activeScenario.outcome == 1
                            ? new InvalidOperationException("Expected benchmark fault") : null;
                    }
                    else
                    {
                        m_uniTaskFirstTasks[i] = UniTask.FromResult(41);
                        m_uniTaskSecondTasks[i] = UniTask.FromResult(42);
                    }
                    m_uniTaskInputs[i] = m_activeScenario.inputCount == 4
                        ? new[] { m_uniTaskFirstTasks[i], m_uniTaskSecondTasks[i],
                            UniTask.FromResult(43), UniTask.FromResult(44) }
                        : new[] { m_uniTaskFirstTasks[i], m_uniTaskSecondTasks[i] };
                }
            }
        }

        private void ScheduleBatch()
        {
            if (m_activeLibrary == 0)
            {
                for (int i = 0; i < k_operations; i++)
                {
                    m_onityResults[i] = OnityTask.WhenAll(m_onityInputs[i]);
                }
            }
            else
            {
                for (int i = 0; i < k_operations; i++)
                {
                    m_uniTaskResults[i] = UniTask.WhenAll(m_uniTaskInputs[i]);
                }
            }
        }

        private void ScheduleFirst()
        {
            if (m_activeLibrary == 0)
            {
                m_onityResults[0] = OnityTask.WhenAll(m_onityInputs[0]);
            }
            else
            {
                m_uniTaskResults[0] = UniTask.WhenAll(m_uniTaskInputs[0]);
            }
        }

        private void ScheduleRemaining()
        {
            if (m_activeLibrary == 0)
            {
                for (int i = 1; i < k_operations; i++)
                {
                    m_onityResults[i] = OnityTask.WhenAll(m_onityInputs[i]);
                }
            }
            else
            {
                for (int i = 1; i < k_operations; i++)
                {
                    m_uniTaskResults[i] = UniTask.WhenAll(m_uniTaskInputs[i]);
                }
            }
        }

        private void RunLifecycleBatch()
        {
            ScheduleBatch();
            FinishBatch();
        }

#if UNITY_EDITOR
        private void RunTrackedLifecycleBatch()
        {
            ScheduleBatch();
            if (m_activeLibrary == 0 && m_activeScenario.onityTrackerEnabled)
            {
                for (int i = 0; i < k_operations; i++)
                {
                    m_trackedTaskIds[i] = m_onityResults[i].AsTask().Id;
                }
            }

            FinishBatch();
            if (m_activeLibrary == 0 && m_activeScenario.onityTrackerEnabled)
            {
                WaitForTrackedBatchCompletion();
            }
        }

        private void WaitForTrackedBatchCompletion()
        {
            long deadline = Stopwatch.GetTimestamp() + 10L * Stopwatch.Frequency;
            while (true)
            {
                OnityTaskTracker.GetSnapshot(m_trackedTasks);
                int completed = 0;
                for (int i = 0; i < m_trackedTasks.Count; i++)
                {
                    OnityTrackedTaskInfo entry = m_trackedTasks[i];
                    for (int task = 0; task < k_operations; task++)
                    {
                        if (entry.TaskId == m_trackedTaskIds[task])
                        {
                            if (entry.IsCompleted)
                            {
                                completed++;
                            }

                            break;
                        }
                    }
                }

                if (completed == k_operations)
                {
                    return;
                }

                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    throw new TimeoutException(
                        "Tracked WhenAll batch did not settle within 10 seconds.");
                }

                Thread.Sleep(1);
            }
        }
#endif

        private void FinishBatch()
        {
            if (m_activeLibrary == 0)
            {
                for (int i = 0; i < k_operations; i++)
                {
                    OnityTaskAwaiter<int[]> awaiter = m_onityResults[i].GetAwaiter();
                    if (m_activeScenario.pending)
                    {
                        bool wasPending = !awaiter.IsCompleted;
                        bool firstCompleted = m_onityFirstSources[i].TrySetResult(41);
                        bool secondCompleted = m_activeScenario.outcome == 1
                            ? m_onitySecondSources[i].TrySetException(m_onityFailures[i])
                            : m_activeScenario.outcome == 2
                                ? m_onitySecondSources[i].TrySetCanceled(m_canceledToken)
                                : m_onitySecondSources[i].TrySetResult(42);
                        if (!wasPending || !firstCompleted || !secondCompleted)
                        {
                            throw new InvalidOperationException(
                                "Onity pending WhenAll input did not complete exactly once.");
                        }
                    }

                    if (!m_activeScenario.pending && !awaiter.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "Onity completed-input WhenAll returned a pending task.");
                    }
                }

                WaitForBatchCompletion();
                for (int i = 0; i < k_operations; i++)
                {
                    OnityTaskAwaiter<int[]> awaiter = m_onityResults[i].GetAwaiter();

                    if (!awaiter.IsCompleted)
                    {
                        throw new InvalidOperationException("Onity WhenAll did not complete.");
                    }

                    if (m_activeScenario.outcome == 0)
                    {
                        ValidateOrderedResults(awaiter.GetResult(), m_activeScenario.inputCount);
                    }
                    else
                    {
                        bool failed = false;
                        try
                        {
                            awaiter.GetResult();
                        }
                        catch (Exception exception)
                        {
                            failed = IsExpectedFailure(exception, m_activeScenario.outcome);
                        }

                        if (!failed)
                        {
                            throw new InvalidOperationException(
                                "Onity WhenAll had the wrong failure category.");
                        }
                    }
                    m_onityFirstSources[i] = null;
                    m_onitySecondSources[i] = null;
                    m_onityFirstTasks[i] = default;
                    m_onitySecondTasks[i] = default;
                    m_onityResults[i] = default;
                    m_onityInputs[i] = null;
                    m_onityFailures[i] = null;
                    m_onityFirstBridges[i] = null;
                    m_onitySecondBridges[i] = null;
                }
            }
            else
            {
                for (int i = 0; i < k_operations; i++)
                {
                    UniTask<int[]>.Awaiter awaiter = m_uniTaskResults[i].GetAwaiter();
                    if (m_activeScenario.pending)
                    {
                        bool wasPending = !awaiter.IsCompleted;
                        bool firstCompleted = m_uniTaskFirstSources[i].TrySetResult(41);
                        bool secondCompleted = m_activeScenario.outcome == 1
                            ? m_uniTaskSecondSources[i].TrySetException(m_uniTaskFailures[i])
                            : m_activeScenario.outcome == 2
                                ? m_uniTaskSecondSources[i].TrySetCanceled(m_canceledToken)
                                : m_uniTaskSecondSources[i].TrySetResult(42);
                        if (!wasPending || !firstCompleted || !secondCompleted)
                        {
                            throw new InvalidOperationException(
                                "UniTask pending WhenAll input did not complete exactly once.");
                        }
                    }

                    if (!m_activeScenario.pending && !awaiter.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "UniTask completed-input WhenAll returned a pending task.");
                    }
                }

                WaitForBatchCompletion();
                for (int i = 0; i < k_operations; i++)
                {
                    UniTask<int[]>.Awaiter awaiter = m_uniTaskResults[i].GetAwaiter();

                    if (!awaiter.IsCompleted)
                    {
                        throw new InvalidOperationException("UniTask WhenAll did not complete.");
                    }

                    if (m_activeScenario.outcome == 0)
                    {
                        ValidateOrderedResults(awaiter.GetResult(), m_activeScenario.inputCount);
                    }
                    else
                    {
                        bool failed = false;
                        try
                        {
                            awaiter.GetResult();
                        }
                        catch (Exception exception)
                        {
                            failed = IsExpectedFailure(exception, m_activeScenario.outcome);
                        }

                        if (!failed)
                        {
                            throw new InvalidOperationException(
                                "UniTask WhenAll had the wrong failure category.");
                        }
                    }
                    m_uniTaskFirstSources[i] = null;
                    m_uniTaskSecondSources[i] = null;
                    m_uniTaskFirstTasks[i] = default;
                    m_uniTaskSecondTasks[i] = default;
                    m_uniTaskResults[i] = default;
                    m_uniTaskInputs[i] = null;
                    m_uniTaskFailures[i] = null;
                }
            }
        }

        private void WaitForBatchCompletion()
        {
            long deadline = Stopwatch.GetTimestamp() + 10L * Stopwatch.Frequency;
            while (true)
            {
                bool allCompleted = true;
                if (m_activeLibrary == 0)
                {
                    for (int i = 0; i < k_operations; i++)
                    {
                        if (!m_onityResults[i].IsCompleted)
                        {
                            allCompleted = false;
                            break;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < k_operations; i++)
                    {
                        if (!m_uniTaskResults[i].GetAwaiter().IsCompleted)
                        {
                            allCompleted = false;
                            break;
                        }
                    }
                }

                if (allCompleted)
                {
                    return;
                }

                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    throw new TimeoutException("WhenAll output did not complete within 10 seconds.");
                }

                Thread.Sleep(1);
            }
        }

        private static bool IsExpectedFailure(Exception exception, int outcome)
        {
            return outcome == 2
                ? exception is OperationCanceledException
                : !(exception is OperationCanceledException);
        }

        private static void ValidateOrderedResults(int[] results, int expectedCount)
        {
            if (results == null || results.Length != expectedCount)
            {
                throw new InvalidOperationException("Typed WhenAll returned the wrong result count.");
            }

            for (int i = 0; i < expectedCount; i++)
            {
                if (results[i] != 41 + i)
                {
                    throw new InvalidOperationException("Typed WhenAll changed result order.");
                }
            }
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void EmptyBatch()
        {
            for (int i = 0; i < k_operations; i++)
            {
            }
        }

        private static void AllocatePositiveControl()
        {
            byte[] bytes = new byte[65536];
            GC.KeepAlive(bytes);
        }

        private static void RunWorkerPositiveControl(object state)
        {
            OnityTypedWhenAllBenchmarkRunner runner = (OnityTypedWhenAllBenchmarkRunner)state;
            try
            {
                AllocatePositiveControl();
            }
            finally
            {
                runner.m_workerControlDone.Set();
            }
        }

#if UNITY_EDITOR
        private void RunWorkerPositiveBatch()
        {
            m_workerControlDone.Reset();
            ThreadPool.QueueUserWorkItem(s_workerPositiveControl, this);
            if (!m_workerControlDone.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Worker positive control did not finish.");
            }
        }

        private IEnumerator RunPrebridgeAttribution(PrebridgeAttributionReport report,
            TypedWhenAllScenario scenario)
        {
            if (ProfilerDriver.deepProfiling)
            {
                throw new InvalidOperationException(
                    "Disable Deep Profiling before allocation attribution.");
            }

            Profiler.enableAllocationCallstacks = true;
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;
            for (int frame = 0; frame < k_profilerStartupFrames; frame++)
            {
                yield return null;
            }

            ValidateAttributionProfilerState();

            yield return CaptureAllocation(m_positiveControl);
            if (!m_lastAllocationValid || m_lastAllocationBytes != 65568)
            {
                throw new InvalidDataException(
                    "Attribution positive control did not capture exactly 65,568 bytes.");
            }

            report.positiveControl = ReadLastAttribution(-1);
            report.positiveControlBytes = m_lastAllocationBytes;
            if (report.positiveControl.attributedBytes != 65568 ||
                report.positiveControl.unresolvedBytes != 0 ||
                !HasResolvedPositiveCallsite(report.positiveControl))
            {
                throw new InvalidDataException(
                    "Attribution positive control callstack was unresolved or missing its callsite.");
            }

            yield return CaptureAllocation(m_emptyBatch);
            if (!m_lastAllocationValid || m_lastAllocationBytes != 0)
            {
                throw new InvalidDataException("Attribution empty control was not zero bytes.");
            }

            report.emptyControl = ReadLastAttribution(-1);
            report.emptyControlBytes = m_lastAllocationBytes;

            m_activeScenario = scenario;
            m_activeLibrary = 0;
            for (int warmup = 0; warmup < k_warmupBatches; warmup++)
            {
                PrepareBatch();
                ScheduleBatch();
                FinishBatch();
            }

            for (int sample = 0; sample < k_prebridgeDiagnosticSamples; sample++)
            {
                yield return CaptureAllocation(m_emptyBatch);
                if (!m_lastAllocationValid || m_lastAllocationBytes != 0)
                {
                    throw new InvalidDataException(
                        "Attribution empty harness sample was missing or nonzero.");
                }

                report.emptyHarnessSamples[sample] = ReadLastAttribution(sample);
            }

            for (int sample = 0; sample < k_prebridgeDiagnosticSamples; sample++)
            {
                ForceFullGc();
                PrepareBatch();
                yield return CaptureAllocation(m_lifecycleBatch);
                if (!m_lastAllocationValid)
                {
                    throw new InvalidDataException(
                        "Attribution lifecycle allocation sample was missing.");
                }

                report.samples[sample] = ReadLastAttribution(sample);
            }

            ValidateAttributionProfilerState();
            report.allocationCallstacksEnabled = Profiler.enableAllocationCallstacks;
            report.deepProfilingEnabled = ProfilerDriver.deepProfiling;
            report.completedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        private static bool HasResolvedPositiveCallsite(PrebridgeAttributionSample sample)
        {
            for (int allocation = 0; allocation < sample.events.Length; allocation++)
            {
                PrebridgeAllocationEvent item = sample.events[allocation];
                if (item.bytes <= 0 || item.resolvedAddressCount == 0)
                {
                    continue;
                }

                for (int frame = 0; frame < item.resolvedMethods.Length; frame++)
                {
                    if (item.resolvedMethods[frame].IndexOf(
                        "AllocatePositiveControl", StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ValidateAttributionProfilerState()
        {
            if (!Profiler.enableAllocationCallstacks || ProfilerDriver.deepProfiling)
            {
                throw new InvalidOperationException(
                    "Allocation callstacks must stay enabled and Deep Profiling must stay off.");
            }
        }

        private IEnumerator RunAllocations(TypedWhenAllReport report)
        {
            report.allThreadAllocationError = null;
            Profiler.enabled = true;
            ProfilerDriver.enabled = true;
            ProfilerDriver.profileEditor = false;
            for (int frame = 0; frame < k_profilerStartupFrames; frame++)
            {
                yield return null;
            }

            yield return CaptureAllocation(m_positiveControl);
            report.positiveControlBytes = m_lastAllocationBytes;
            if (!m_lastAllocationValid || report.positiveControlBytes != 65568)
            {
                throw new InvalidDataException(
                    "Profiler positive control did not capture exactly 65,568 bytes.");
            }

            yield return CaptureAllocation(m_emptyBatch);
            report.emptyControlBytes = m_lastAllocationBytes;
            if (!m_lastAllocationValid || report.emptyControlBytes != 0)
            {
                throw new InvalidDataException(
                    "Profiler empty control was not zero bytes.");
            }

            if (m_prebridgeDiagnostic)
            {
                m_activeScenario = report.scenarios[0];
                m_activeLibrary = 0;
                for (int warmup = 0; warmup < k_warmupBatches; warmup++)
                {
                    PrepareBatch();
                    ScheduleBatch();
                    FinishBatch();
                }

                report.emptyHarnessSampleAllocatedBytes =
                    new long[k_prebridgeDiagnosticSamples];
                for (int sample = 0; sample < k_prebridgeDiagnosticSamples; sample++)
                {
                    yield return CaptureAllocation(m_emptyBatch);
                    if (!m_lastAllocationValid || m_lastAllocationBytes != 0)
                    {
                        throw new InvalidDataException(
                            "Prebridge empty harness sample was missing or nonzero.");
                    }

                    report.emptyHarnessSampleAllocatedBytes[sample] = m_lastAllocationBytes;
                }

                TypedWhenAllMetric metric = m_activeScenario.results[0];
                metric.sampleAllocatedBytes = new long[k_prebridgeDiagnosticSamples];
                long total = 0;
                for (int sample = 0; sample < k_prebridgeDiagnosticSamples; sample++)
                {
                    ForceFullGc();
                    PrepareBatch();
                    yield return CaptureAllocation(m_lifecycleBatch);
                    if (!m_lastAllocationValid)
                    {
                        throw new InvalidDataException(
                            "Prebridge lifecycle allocation sample was missing.");
                    }

                    metric.sampleAllocatedBytes[sample] = m_lastAllocationBytes;
                    total += m_lastAllocationBytes;
                }

                metric.bytesPerOperation = (double)total /
                    (k_prebridgeDiagnosticSamples * k_operations);
                report.allocationsAvailable = true;
                report.allThreadAllocationError =
                    "Not collected in the main-thread prebridge diagnostic.";
                report.workerAllocationError =
                    "Not collected in the main-thread prebridge diagnostic.";
                report.allocationCounter =
                    "Unity Profiler main-thread GC.Alloc metadata; 65,568/0-byte controls passed.";
                report.allocationGeneratedAtUtc =
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                yield break;
            }

            // Scenario 1 remains pending success with the Onity tracker disabled.
            m_activeScenario = report.scenarios[1];
            for (int library = 0; library < 2; library++)
            {
                m_activeLibrary = library;
                PrepareBatch();
                yield return CaptureAllocation(m_scheduleFirst);
                if (!m_lastAllocationValid)
                {
                    throw new InvalidDataException("First-call scheduling marker was missing.");
                }

                report.coldScheduling[library].sampleAllocatedBytes =
                    new[] { m_lastAllocationBytes };
                ScheduleRemaining();
                FinishBatch();
            }

            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int warmup = 0; warmup < k_warmupBatches; warmup++)
                {
                    for (int library = 0; library < m_activeScenario.results.Length; library++)
                    {
                        m_activeLibrary = library;
                        PrepareBatch();
                        ScheduleBatch();
                        FinishBatch();
                    }
                }
            }

            report.emptyHarnessSampleAllocatedBytes = new long[k_samples];
            for (int sample = 0; sample < k_samples; sample++)
            {
                yield return CaptureAllocation(m_emptyBatch);
                if (!m_lastAllocationValid)
                {
                    throw new InvalidDataException("Empty harness allocation sample was missing.");
                }

                report.emptyHarnessSampleAllocatedBytes[sample] = m_lastAllocationBytes;
                if (m_lastAllocationBytes != 0)
                {
                    throw new InvalidDataException(
                        "Empty harness allocation sample was not zero bytes.");
                }
            }

            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int sample = 0; sample < k_samples; sample++)
                {
                    for (int turn = 0; turn < m_activeScenario.results.Length; turn++)
                    {
                        m_activeLibrary = (sample + turn) % m_activeScenario.results.Length;
                        ForceFullGc();
                        PrepareBatch();
                        yield return CaptureAllocation(m_activeScenario.fullLifecycle
                            ? m_lifecycleBatch : m_scheduleBatch);
                        if (!m_lastAllocationValid)
                        {
                            throw new InvalidDataException(
                                "WhenAll allocation sample was missing.");
                        }

                        if (!m_activeScenario.fullLifecycle)
                        {
                            FinishBatch();
                        }
                        TypedWhenAllMetric metric = m_activeScenario.results[m_activeLibrary];
                        if (metric.sampleAllocatedBytes == null)
                        {
                            metric.sampleAllocatedBytes = new long[k_samples];
                        }

                        metric.sampleAllocatedBytes[sample] = m_lastAllocationBytes;
                    }
                }

                for (int library = 0; library < m_activeScenario.results.Length; library++)
                {
                    TypedWhenAllMetric metric = m_activeScenario.results[library];
                    long total = 0;
                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        total += metric.sampleAllocatedBytes[sample];
                    }

                    metric.bytesPerOperation = (double)total / (k_samples * k_operations);
                }
            }

            for (int sample = 0; sample < k_samples; sample++)
            {
                for (int turn = 0; turn < 2; turn++)
                {
                    m_activeLibrary = (sample + turn) & 1;
                    PrepareBurst();
                    yield return CaptureAllocation(m_scheduleBurst);
                    if (!m_lastAllocationValid)
                    {
                        throw new InvalidDataException(
                            "Outstanding-burst scheduling marker was missing.");
                    }

                    TypedWhenAllExtraMetric metric = report.burstScheduling[m_activeLibrary];
                    if (metric.sampleAllocatedBytes == null)
                    {
                        metric.sampleAllocatedBytes = new long[k_samples];
                    }

                    metric.sampleAllocatedBytes[sample] = m_lastAllocationBytes;
                    FinishBurst();
                }
            }

            RunWorkerPositiveBatch();
            yield return CaptureAllThreadWindow(m_positiveControl);
            report.allThreadMainPositiveBytes = m_lastWindowMainBytes;
            report.allThreadOtherPositiveBytes = m_lastWindowOtherThreadBytes;
            if (!m_lastWindowValid || m_lastWindowMainBytes != 65568 ||
                m_lastWindowOtherThreadBytes != 0)
            {
                report.allThreadAllocationError =
                    "Main all-thread window positive control failed.";
            }

            if (report.allThreadAllocationError == null)
            {
                yield return CaptureAllThreadWindow(m_emptyBatch);
                report.allThreadMainEmptyBytes = m_lastWindowMainBytes;
                report.allThreadOtherEmptyBytes = m_lastWindowOtherThreadBytes;
                if (!m_lastWindowValid || m_lastWindowMainBytes != 0 ||
                    m_lastWindowOtherThreadBytes != 0)
                {
                    report.allThreadAllocationError =
                        "All-thread window empty control failed.";
                }
            }

            if (report.allThreadAllocationError == null)
            {
                yield return CaptureAllThreadWindow(m_workerPositiveBatch);
                report.allThreadWorkerPositiveMainBytes = m_lastWindowMainBytes;
                report.allThreadWorkerPositiveOtherBytes = m_lastWindowOtherThreadBytes;
                if (!m_lastWindowValid || m_lastWindowOtherThreadBytes != 65568)
                {
                    report.allThreadAllocationError =
                        "ThreadPool positive control was not 65,568 worker bytes.";
                }
            }

            if (report.allThreadAllocationError == null)
            {
                for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
                {
                    m_activeScenario = report.scenarios[scenario];
                    if (!m_activeScenario.fullLifecycle ||
                        !m_activeScenario.onityTrackerEnabled)
                    {
                        continue;
                    }

                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        for (int turn = 0; turn < 2; turn++)
                        {
                            m_activeLibrary = (sample + turn) & 1;
                            PrepareBatch();
                            yield return CaptureAllThreadWindow(m_trackedLifecycleBatch);
                            if (!m_lastWindowValid)
                            {
                                report.allThreadAllocationError =
                                    "Tracked lifecycle all-thread marker was missing.";
                                break;
                            }

                            TypedWhenAllMetric metric =
                                m_activeScenario.results[m_activeLibrary];
                            if (metric.allThreadSampleAllocatedBytes == null)
                            {
                                metric.allThreadSampleAllocatedBytes = new long[k_samples];
                                metric.otherThreadSampleAllocatedBytes = new long[k_samples];
                            }

                            metric.allThreadSampleAllocatedBytes[sample] =
                                m_lastWindowMainBytes + m_lastWindowOtherThreadBytes;
                            metric.otherThreadSampleAllocatedBytes[sample] =
                                m_lastWindowOtherThreadBytes;
                        }

                        if (report.allThreadAllocationError != null)
                        {
                            break;
                        }
                    }

                    if (report.allThreadAllocationError != null)
                    {
                        break;
                    }
                }
            }

            report.allThreadAllocationsAvailable = report.allThreadAllocationError == null;
            if (!report.allThreadAllocationsAvailable)
            {
                ClearAllThreadSamples(report);
            }

            report.allocationsAvailable = true;
            report.allocationCounter =
                "Unity Profiler GC.Alloc metadata; 65,568/0-byte controls passed.";
            report.allocationGeneratedAtUtc =
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        private void PrepareBurst()
        {
            OnityTaskTracker.IsEnabled = false;
            for (int i = 0; i < k_saturationOperations; i++)
            {
                if (m_activeLibrary == 0)
                {
                    m_burstOnityFirstSources[i] = new OnityTaskCompletionSource<int>();
                    m_burstOnitySecondSources[i] = new OnityTaskCompletionSource<int>();
                    m_burstOnityFirstTasks[i] = m_burstOnityFirstSources[i].Task;
                    m_burstOnitySecondTasks[i] = m_burstOnitySecondSources[i].Task;
                    m_burstOnityInputs[i] = new[]
                    {
                        m_burstOnityFirstTasks[i], m_burstOnitySecondTasks[i]
                    };
                }
                else
                {
                    m_burstUniTaskFirstSources[i] = new UniTaskCompletionSource<int>();
                    m_burstUniTaskSecondSources[i] = new UniTaskCompletionSource<int>();
                    m_burstUniTaskFirstTasks[i] = m_burstUniTaskFirstSources[i].Task;
                    m_burstUniTaskSecondTasks[i] = m_burstUniTaskSecondSources[i].Task;
                    m_burstUniTaskInputs[i] = new[]
                    {
                        m_burstUniTaskFirstTasks[i], m_burstUniTaskSecondTasks[i]
                    };
                }
            }
        }

        private void ScheduleBurst()
        {
            for (int i = 0; i < k_saturationOperations; i++)
            {
                if (m_activeLibrary == 0)
                {
                    m_burstOnityResults[i] = OnityTask.WhenAll(m_burstOnityInputs[i]);
                }
                else
                {
                    m_burstUniTaskResults[i] = UniTask.WhenAll(m_burstUniTaskInputs[i]);
                }
            }
        }

        private void FinishBurst()
        {
            for (int i = 0; i < k_saturationOperations; i++)
            {
                if (m_activeLibrary == 0)
                {
                    if (m_burstOnityResults[i].IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "Onity outstanding-burst output completed before its inputs.");
                    }

                    m_burstOnityFirstSources[i].TrySetResult(41);
                    m_burstOnitySecondSources[i].TrySetResult(42);
                }
                else
                {
                    if (m_burstUniTaskResults[i].GetAwaiter().IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "UniTask outstanding-burst output completed before its inputs.");
                    }

                    m_burstUniTaskFirstSources[i].TrySetResult(41);
                    m_burstUniTaskSecondSources[i].TrySetResult(42);
                }
            }

            long deadline = Stopwatch.GetTimestamp() + 10L * Stopwatch.Frequency;
            for (int i = 0; i < k_saturationOperations; i++)
            {
                while (m_activeLibrary == 0
                    ? !m_burstOnityResults[i].IsCompleted
                    : !m_burstUniTaskResults[i].GetAwaiter().IsCompleted)
                {
                    if (Stopwatch.GetTimestamp() >= deadline)
                    {
                        throw new TimeoutException(
                            "Outstanding-burst output did not complete within 10 seconds.");
                    }

                    Thread.Sleep(1);
                }

                if (m_activeLibrary == 0)
                {
                    ValidateOrderedResults(m_burstOnityResults[i].GetAwaiter().GetResult(), 2);
                    m_burstOnityFirstSources[i] = null;
                    m_burstOnitySecondSources[i] = null;
                    m_burstOnityFirstTasks[i] = default;
                    m_burstOnitySecondTasks[i] = default;
                    m_burstOnityResults[i] = default;
                    m_burstOnityInputs[i] = null;
                }
                else
                {
                    ValidateOrderedResults(m_burstUniTaskResults[i].GetAwaiter().GetResult(), 2);
                    m_burstUniTaskFirstSources[i] = null;
                    m_burstUniTaskSecondSources[i] = null;
                    m_burstUniTaskFirstTasks[i] = default;
                    m_burstUniTaskSecondTasks[i] = default;
                    m_burstUniTaskResults[i] = default;
                    m_burstUniTaskInputs[i] = null;
                }
            }
        }

        private void RunWorkerAllocations(TypedWhenAllReport report)
        {
            Exception workerFailure = null;
            Thread worker = new Thread(() =>
            {
                try
                {
                    RunWorkerAllocationsCore(report);
                }
                catch (Exception exception)
                {
                    workerFailure = exception;
                }
            });
            worker.Start();
            worker.Join();
            if (workerFailure != null)
            {
                report.workerAllocationError = workerFailure.GetType().Name
                    + ": " + workerFailure.Message;
                report.workerAllocationsAvailable = false;
                report.saturationSampleAllocatedBytes = null;
                for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
                {
                    for (int library = 0;
                        library < report.scenarios[scenario].results.Length; library++)
                    {
                        TypedWhenAllMetric metric = report.scenarios[scenario].results[library];
                        metric.workerBytesPerOperation = -1;
                        metric.workerSampleAllocatedBytes = null;
                    }
                }
            }
        }

        private void RunWorkerAllocationsCore(TypedWhenAllReport report)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] positive = new byte[65536];
            GC.KeepAlive(positive);
            report.workerPositiveControlBytes =
                GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            report.workerEmptyControlBytes =
                GC.GetAllocatedBytesForCurrentThread() - before;
            if (report.workerPositiveControlBytes != 65568 ||
                report.workerEmptyControlBytes != 0)
            {
                throw new InvalidDataException("Worker allocation controls failed.");
            }

            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int library = 0; library < m_activeScenario.results.Length; library++)
                {
                    m_activeLibrary = library;
                    for (int warmup = 0; warmup < k_warmupBatches; warmup++)
                    {
                        PrepareBatch();
                        RunLifecycleBatch();
                    }

                    TypedWhenAllMetric metric = m_activeScenario.results[library];
                    metric.workerSampleAllocatedBytes = new long[k_samples];
                    long total = 0;
                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        PrepareBatch();
                        before = GC.GetAllocatedBytesForCurrentThread();
                        if (m_activeScenario.fullLifecycle)
                        {
                            m_lifecycleBatch();
                        }
                        else
                        {
                            m_scheduleBatch();
                        }

                        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                        if (!m_activeScenario.fullLifecycle)
                        {
                            FinishBatch();
                        }

                        metric.workerSampleAllocatedBytes[sample] = bytes;
                        total += bytes;
                    }

                    metric.workerBytesPerOperation = (double)total / (k_samples * k_operations);
                }
            }

            report.saturationSampleAllocatedBytes = new long[k_samples];
            for (int sample = 0; sample < k_samples; sample++)
            {
                OnityTaskCompletionSource<int>[] first =
                    new OnityTaskCompletionSource<int>[k_saturationOperations];
                OnityTaskCompletionSource<int>[] second =
                    new OnityTaskCompletionSource<int>[k_saturationOperations];
                OnityTask<int>[] firstTasks = new OnityTask<int>[k_saturationOperations];
                OnityTask<int>[] secondTasks = new OnityTask<int>[k_saturationOperations];
                OnityTask<int[]>[] results = new OnityTask<int[]>[k_saturationOperations];
                OnityTask<int>[][] inputs = new OnityTask<int>[k_saturationOperations][];
                OnityTaskTracker.IsEnabled = false;
                for (int i = 0; i < k_saturationOperations; i++)
                {
                    first[i] = new OnityTaskCompletionSource<int>();
                    second[i] = new OnityTaskCompletionSource<int>();
                    firstTasks[i] = first[i].Task;
                    secondTasks[i] = second[i].Task;
                    inputs[i] = new[] { firstTasks[i], secondTasks[i] };
                }

                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < k_saturationOperations; i++)
                {
                    results[i] = OnityTask.WhenAll(inputs[i]);
                }

                report.saturationSampleAllocatedBytes[sample] =
                    GC.GetAllocatedBytesForCurrentThread() - before;
                for (int i = 0; i < k_saturationOperations; i++)
                {
                    first[i].TrySetResult(41);
                    second[i].TrySetResult(42);
                    ValidateOrderedResults(results[i].GetAwaiter().GetResult(), 2);
                }
            }

            report.workerAllocationsAvailable = true;
        }

        private IEnumerator CaptureAllocation(Action operation)
        {
            if (m_prebridgeAttribution)
            {
                ValidateAttributionProfilerState();
            }

            m_lastAllocationValid = false;
            m_lastAllocationBytes = 0;
            m_lastAllocationThread = -1;
            m_lastAllocationMarkerSample = -1;
            string marker = k_scopePrefix
                + (m_markerSequence++).ToString(CultureInfo.InvariantCulture);
            m_lastAllocationMarker = marker;
            Profiler.BeginSample(marker);
            try
            {
                operation();
            }
            finally
            {
                Profiler.EndSample();
            }

            if (m_prebridgeAttribution)
            {
                ValidateAttributionProfilerState();
            }

            for (int wait = 0; wait < k_profilerReadFrames; wait++)
            {
                yield return null;
                if (m_prebridgeAttribution)
                {
                    ValidateAttributionProfilerState();
                }

                if (TryReadAllocation(
                    Math.Max(m_lastCapturedFrame, ProfilerDriver.firstFrameIndex),
                    ProfilerDriver.lastFrameIndex, marker,
                    out long bytes, out int foundFrame,
                    out int foundThread, out int markerSample))
                {
                    m_lastAllocationBytes = bytes;
                    m_lastCapturedFrame = foundFrame;
                    m_lastAllocationThread = foundThread;
                    m_lastAllocationMarkerSample = markerSample;
                    m_lastAllocationValid = true;
                    yield break;
                }
            }
        }

        private PrebridgeAttributionSample ReadLastAttribution(int sampleIndex)
        {
            ValidateAttributionProfilerState();
            if (!m_lastAllocationValid || m_lastCapturedFrame < 0 ||
                m_lastAllocationThread < 0 || m_lastAllocationMarkerSample < 0)
            {
                throw new InvalidDataException("Attribution marker identity was missing.");
            }

            using (RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(
                m_lastCapturedFrame, m_lastAllocationThread))
            {
                if (!data.valid || m_lastAllocationMarkerSample >= data.sampleCount ||
                    data.GetSampleMarkerId(m_lastAllocationMarkerSample) !=
                    data.GetMarkerId(m_lastAllocationMarker))
                {
                    throw new InvalidDataException("Attribution marker frame or thread changed.");
                }

                int allocationId = data.GetMarkerId("GC.Alloc");
                int markerEnd = m_lastAllocationMarkerSample +
                    data.GetSampleChildrenCountRecursive(m_lastAllocationMarkerSample);
                if (markerEnd >= data.sampleCount)
                {
                    throw new InvalidDataException("Attribution marker sample tree was truncated.");
                }
                List<int> parents = new List<int> { m_lastAllocationMarkerSample };
                List<int> parentEnds = new List<int> { markerEnd };
                List<PrebridgeAllocationEvent> events = new List<PrebridgeAllocationEvent>();
                List<ulong> addresses = new List<ulong>(32);
                long attributedBytes = 0;
                long unresolvedBytes = 0;
                for (int child = m_lastAllocationMarkerSample + 1; child <= markerEnd; child++)
                {
                    while (parentEnds.Count > 0 && parentEnds[parentEnds.Count - 1] < child)
                    {
                        int last = parentEnds.Count - 1;
                        parentEnds.RemoveAt(last);
                        parents.RemoveAt(last);
                    }

                    if (parents.Count == 0)
                    {
                        throw new InvalidDataException("Attribution sample tree was incomplete.");
                    }

                    int parent = parents[parents.Count - 1];
                    if (data.GetSampleMarkerId(child) == allocationId)
                    {
                        long bytes = data.GetSampleMetadataAsLong(child, 0);
                        addresses.Clear();
                        data.GetSampleCallstack(child, addresses);
                        string[] rawAddresses = new string[addresses.Count];
                        string[] resolvedMethods = new string[addresses.Count];
                        string[] resolutionErrors = new string[addresses.Count];
                        int resolvedCount = 0;
                        for (int address = 0; address < addresses.Count; address++)
                        {
                            rawAddresses[address] = addresses[address].ToString(
                                "X16", CultureInfo.InvariantCulture);
                            try
                            {
                                FrameDataView.MethodInfo method =
                                    data.ResolveMethodInfo(addresses[address]);
                                resolvedMethods[address] = method.methodName ?? string.Empty;
                            }
                            catch (Exception exception)
                            {
                                resolvedMethods[address] = string.Empty;
                                resolutionErrors[address] = exception.GetType().Name;
                            }

                            if (resolvedMethods[address].Length > 0)
                            {
                                resolvedCount++;
                            }
                        }

                        events.Add(new PrebridgeAllocationEvent
                        {
                            profilerSampleIndex = child,
                            parentSampleIndex = parent,
                            parentSampleName = data.GetSampleName(parent),
                            bytes = bytes,
                            rawAddresses = rawAddresses,
                            resolvedMethods = resolvedMethods,
                            resolutionErrors = resolutionErrors,
                            resolvedAddressCount = resolvedCount,
                            unresolvedAddressCount = addresses.Count - resolvedCount
                        });
                        if (resolvedCount > 0)
                        {
                            attributedBytes += bytes;
                        }
                        else
                        {
                            unresolvedBytes += bytes;
                        }
                    }

                    int childEnd = child + data.GetSampleChildrenCountRecursive(child);
                    if (childEnd > child)
                    {
                        parents.Add(child);
                        parentEnds.Add(childEnd);
                    }
                }

                if (attributedBytes + unresolvedBytes != m_lastAllocationBytes)
                {
                    throw new InvalidDataException(
                        "Attributed and unresolved bytes did not match the marker total.");
                }

                return new PrebridgeAttributionSample
                {
                    sampleIndex = sampleIndex,
                    frameIndex = m_lastCapturedFrame,
                    threadIndex = m_lastAllocationThread,
                    markerSampleIndex = m_lastAllocationMarkerSample,
                    markerName = m_lastAllocationMarker,
                    markerTotalBytes = m_lastAllocationBytes,
                    attributedBytes = attributedBytes,
                    unresolvedBytes = unresolvedBytes,
                    events = events.ToArray()
                };
            }
        }

        private static bool TryReadAllocation(int firstFrame, int lastFrame,
            string marker, out long bytes, out int foundFrame,
            out int foundThread, out int markerSample)
        {
            bytes = 0;
            foundFrame = -1;
            foundThread = -1;
            markerSample = -1;
            for (int frame = firstFrame; frame <= lastFrame; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using (RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread))
                    {
                        if (!data.valid)
                        {
                            break;
                        }

                        int scopeId = data.GetMarkerId(marker);
                        int allocationId = data.GetMarkerId("GC.Alloc");
                        if (scopeId == FrameDataView.invalidMarkerId)
                        {
                            continue;
                        }

                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            if (data.GetSampleMarkerId(sample) != scopeId)
                            {
                                continue;
                            }

                            int end = sample + data.GetSampleChildrenCountRecursive(sample);
                            for (int child = sample + 1; child <= end; child++)
                            {
                                if (data.GetSampleMarkerId(child) == allocationId)
                                {
                                    bytes += data.GetSampleMetadataAsLong(child, 0);
                                }
                            }

                            foundFrame = frame;
                            foundThread = thread;
                            markerSample = sample;
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        private IEnumerator CaptureAllThreadWindow(Action operation)
        {
            m_lastWindowValid = false;
            m_lastWindowMainBytes = 0;
            m_lastWindowOtherThreadBytes = 0;
            string marker = k_windowPrefix
                + (m_markerSequence++).ToString(CultureInfo.InvariantCulture);
            Profiler.BeginSample(marker);
            try
            {
                operation();
            }
            finally
            {
                Profiler.EndSample();
            }

            for (int wait = 0; wait < k_profilerReadFrames; wait++)
            {
                yield return null;
                if (TryReadAllThreadWindow(
                    Math.Max(m_lastCapturedFrame, ProfilerDriver.firstFrameIndex),
                    ProfilerDriver.lastFrameIndex, marker,
                    out long mainBytes, out long otherBytes, out int foundFrame))
                {
                    m_lastWindowMainBytes = mainBytes;
                    m_lastWindowOtherThreadBytes = otherBytes;
                    m_lastCapturedFrame = foundFrame;
                    m_lastWindowValid = true;
                    yield break;
                }
            }
        }

        private static bool TryReadAllThreadWindow(int firstFrame, int lastFrame,
            string marker, out long mainBytes, out long otherBytes, out int foundFrame)
        {
            mainBytes = 0;
            otherBytes = 0;
            foundFrame = -1;
            ulong startTime = 0;
            ulong endTime = 0;
            int mainThread = -1;
            for (int frame = firstFrame; frame <= lastFrame; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using (RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread))
                    {
                        if (!data.valid)
                        {
                            break;
                        }

                        int markerId = data.GetMarkerId(marker);
                        if (markerId == FrameDataView.invalidMarkerId)
                        {
                            continue;
                        }

                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            if (data.GetSampleMarkerId(sample) != markerId)
                            {
                                continue;
                            }

                            startTime = data.GetSampleStartTimeNs(sample);
                            endTime = startTime + data.GetSampleTimeNs(sample);
                            foundFrame = frame;
                            mainThread = thread;
                            break;
                        }
                    }

                    if (foundFrame >= 0)
                    {
                        break;
                    }
                }

                if (foundFrame >= 0)
                {
                    break;
                }
            }

            if (foundFrame < 0 || endTime <= startTime)
            {
                return false;
            }

            int firstWindowFrame = Math.Max(ProfilerDriver.firstFrameIndex, foundFrame - 1);
            int lastWindowFrame = Math.Min(ProfilerDriver.lastFrameIndex, foundFrame + 1);
            for (int frame = firstWindowFrame; frame <= lastWindowFrame; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using (RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread))
                    {
                        if (!data.valid)
                        {
                            break;
                        }

                        int allocationId = data.GetMarkerId("GC.Alloc");
                        if (allocationId == FrameDataView.invalidMarkerId)
                        {
                            continue;
                        }

                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            if (data.GetSampleMarkerId(sample) != allocationId)
                            {
                                continue;
                            }

                            ulong time = data.GetSampleStartTimeNs(sample);
                            if (time < startTime || time > endTime)
                            {
                                continue;
                            }

                            long bytes = data.GetSampleMetadataAsLong(sample, 0);
                            if (frame == foundFrame && thread == mainThread)
                            {
                                mainBytes += bytes;
                            }
                            else
                            {
                                otherBytes += bytes;
                            }
                        }
                    }
                }
            }

            return true;
        }

#endif

        private static void ClearAllocations(TypedWhenAllReport report)
        {
            report.allocationsAvailable = false;
            report.allThreadAllocationsAvailable = false;
            report.allThreadAllocationError = "Unavailable: separate Profiler pass not run.";
            report.workerAllocationsAvailable = false;
            report.workerPositiveControlBytes = 0;
            report.workerEmptyControlBytes = 0;
            report.workerAllocationError = null;
            report.saturationSampleAllocatedBytes = null;
            for (int extra = 0; extra < 2; extra++)
            {
                report.coldScheduling[extra].sampleAllocatedBytes = null;
                report.burstScheduling[extra].sampleAllocatedBytes = null;
            }
            report.allocationCounter = "Unavailable: separate Profiler pass not run.";
            report.allocationGeneratedAtUtc = null;
            report.positiveControlBytes = 0;
            report.emptyControlBytes = 0;
            report.emptyHarnessSampleAllocatedBytes = null;
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                for (int library = 0;
                    library < report.scenarios[scenario].results.Length; library++)
                {
                    report.scenarios[scenario].results[library].bytesPerOperation = -1;
                    report.scenarios[scenario].results[library].sampleAllocatedBytes = null;
                    report.scenarios[scenario].results[library].workerBytesPerOperation = -1;
                    report.scenarios[scenario].results[library].workerSampleAllocatedBytes = null;
                    report.scenarios[scenario].results[library].allThreadSampleAllocatedBytes = null;
                    report.scenarios[scenario].results[library].otherThreadSampleAllocatedBytes = null;
                }
            }
        }

        private static void ClearAllThreadSamples(TypedWhenAllReport report)
        {
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                for (int library = 0;
                    library < report.scenarios[scenario].results.Length; library++)
                {
                    report.scenarios[scenario].results[library].allThreadSampleAllocatedBytes = null;
                    report.scenarios[scenario].results[library].otherThreadSampleAllocatedBytes = null;
                }
            }
        }

        private void SaveReport(TypedWhenAllReport report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_outputPath));
            File.WriteAllText(m_outputPath, JsonUtility.ToJson(report, true));
            if (m_prebridgeDiagnostic)
            {
                return;
            }

            File.WriteAllText(Path.ChangeExtension(m_outputPath, ".csv"), BuildCsv(report));
            File.WriteAllText(Path.ChangeExtension(m_outputPath, ".md"), BuildMarkdown(report));
        }

        private void SaveAttributionReport(PrebridgeAttributionReport report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_outputPath));
            File.WriteAllText(m_outputPath, JsonUtility.ToJson(report, true));
        }

        private static string BuildCsv(TypedWhenAllReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("scenario,library,timing_operations_per_sample,allocation_operations_per_sample,"
                + "mean_ns_per_op,median_ns_per_op,min_ns_per_op,max_ns_per_op,"
                + "standard_deviation_ns_per_op,bytes_per_op,worker_bytes_per_op,"
                + "allocations_available");
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                for (int library = 0;
                    library < report.scenarios[scenario].results.Length; library++)
                {
                    TypedWhenAllMetric metric = report.scenarios[scenario].results[library];
                    builder.Append(report.scenarios[scenario].name).Append(',');
                    builder.Append(metric.library).Append(',');
                    builder.Append(k_operations * report.scenarios[scenario].timingBatches)
                        .Append(',');
                    builder.Append(k_operations).Append(',');
                    builder.Append(Number(metric.meanNanosecondsPerOperation)).Append(',');
                    builder.Append(Number(metric.medianNanosecondsPerOperation)).Append(',');
                    builder.Append(Number(metric.minNanosecondsPerOperation)).Append(',');
                    builder.Append(Number(metric.maxNanosecondsPerOperation)).Append(',');
                    builder.Append(Number(metric.standardDeviationNanosecondsPerOperation))
                        .Append(',');
                    builder.Append(Number(metric.bytesPerOperation)).Append(',');
                    builder.Append(Number(metric.workerBytesPerOperation)).Append(',');
                    builder.AppendLine(report.allocationsAvailable ? "true" : "false");
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(TypedWhenAllReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# " + report.suite);
            builder.AppendLine();
            builder.AppendLine("- Unity: " + report.unityVersion + "; " + report.scriptingBackend);
            builder.AppendLine("- UniTask: " + report.uniTaskCommit);
            builder.AppendLine("- OnityAsync SHA-256: " + report.onityAsyncSha256);
            builder.AppendLine("- OnityTaskCompletionSource SHA-256: "
                + report.completionSourceSha256);
            builder.AppendLine("- Runner SHA-256: " + report.runnerSha256);
            builder.AppendLine("- Editor menu SHA-256: " + report.menuSha256);
            builder.AppendLine("- Runtime/Editor asmdef SHA-256: "
                + report.runtimeAsmdefSha256 + " / " + report.editorAsmdefSha256);
            builder.AppendLine("- Manifest/lock SHA-256: "
                + report.manifestSha256 + " / " + report.lockSha256);
            builder.AppendLine("- Samples: " + report.samplesPerCase + "; operations/sample: "
                + (k_operations * k_timingBatches) + " timing for success/completed, "
                + (k_operations * 2) + " timing for fault/cancel, " + k_operations
                + " allocation; warmup batches: " + report.warmupBatches);
            builder.AppendLine("- Allocation counter: " + report.allocationCounter);
            builder.AppendLine("- Controls: " + report.positiveControlBytes
                + " B positive; " + report.emptyControlBytes + " B empty.");
            builder.AppendLine("- Worker controls: " + report.workerPositiveControlBytes
                + " B positive; " + report.workerEmptyControlBytes + " B empty.");
            if (!report.workerAllocationsAvailable)
            {
                builder.AppendLine("- Worker allocation unavailable: "
                    + report.workerAllocationError);
            }
            builder.AppendLine("- All-thread window controls: main positive "
                + report.allThreadMainPositiveBytes + "/"
                + report.allThreadOtherPositiveBytes + " B (main/other), empty "
                + report.allThreadMainEmptyBytes + "/"
                + report.allThreadOtherEmptyBytes + " B, ThreadPool positive "
                + report.allThreadWorkerPositiveMainBytes + "/"
                + report.allThreadWorkerPositiveOtherBytes + " B.");
            if (!report.allThreadAllocationsAvailable)
            {
                builder.AppendLine("- All-thread lifecycle allocation unavailable: "
                    + report.allThreadAllocationError);
            }
            for (int extra = 0; extra < 2; extra++)
            {
                TypedWhenAllExtraMetric cold = report.coldScheduling[extra];
                builder.AppendLine("- " + cold.name + ", " + cold.library
                    + ": " + (cold.sampleAllocatedBytes == null
                        ? "unavailable" : cold.sampleAllocatedBytes[0] + " raw B")
                    + "; first call in a warmed Editor process, one sample per process.");
                TypedWhenAllExtraMetric burst = report.burstScheduling[extra];
                builder.AppendLine("- " + burst.name + ", " + burst.library
                    + ": " + (burst.sampleAllocatedBytes == null
                        ? "unavailable" : string.Join(",", burst.sampleAllocatedBytes))
                    + " raw B for " + burst.operations + " simultaneous outputs per sample.");
            }
            if (report.saturationSampleAllocatedBytes != null)
            {
                builder.AppendLine("- Worker saturation: 384 pending Onity outputs, tracker off; "
                    + "raw bytes per sample: "
                    + string.Join(",", report.saturationSampleAllocatedBytes) + ".");
            }
            builder.AppendLine();
            if (report.allThreadAllocationsAvailable)
            {
                builder.AppendLine("All-thread lifecycle samples include a tracked-batch "
                    + "completion barrier. Raw totals include benchmark barrier overhead; "
                    + "they are reported separately from the main-thread marker slices.");
                for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
                {
                    for (int library = 0;
                        library < report.scenarios[scenario].results.Length; library++)
                    {
                        TypedWhenAllMetric metric = report.scenarios[scenario].results[library];
                        if (metric.allThreadSampleAllocatedBytes == null)
                        {
                            continue;
                        }

                        builder.AppendLine("- " + report.scenarios[scenario].name
                            + ", " + metric.library + ": all-thread raw B "
                            + string.Join(",", metric.allThreadSampleAllocatedBytes)
                            + "; other-thread raw B "
                            + string.Join(",", metric.otherThreadSampleAllocatedBytes) + ".");
                    }
                }
            }

            builder.AppendLine();
            builder.AppendLine(report.scope);
            builder.AppendLine();
            builder.AppendLine("| Scenario | Library | Mean ns/op | Median ns/op | Range ns/op | SD ns/op | Main-marker B/op | Worker B/op |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                for (int library = 0;
                    library < report.scenarios[scenario].results.Length; library++)
                {
                    TypedWhenAllMetric metric = report.scenarios[scenario].results[library];
                    builder.Append("| ").Append(report.scenarios[scenario].name)
                        .Append(" | ").Append(metric.library)
                        .Append(" | ").Append(Number(metric.meanNanosecondsPerOperation))
                        .Append(" | ").Append(Number(metric.medianNanosecondsPerOperation))
                        .Append(" | ").Append(Number(metric.minNanosecondsPerOperation))
                        .Append("–").Append(Number(metric.maxNanosecondsPerOperation))
                        .Append(" | ").Append(Number(metric.standardDeviationNanosecondsPerOperation))
                        .Append(" | ").Append(Number(metric.bytesPerOperation))
                        .Append(" | ").Append(Number(metric.workerBytesPerOperation)).AppendLine(" |");
                }
            }

            return builder.ToString();
        }

        private static string Number(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private void RestoreState()
        {
            if (m_trackerStateCaptured)
            {
                OnityTaskTracker.IsEnabled = m_originalTrackerEnabled;
                m_trackerStateCaptured = false;
            }
#if UNITY_EDITOR
            if (!m_profilerStateCaptured)
            {
                return;
            }

            m_profilerStateCaptured = false;
            Profiler.enabled = m_profilerWasEnabled;
            ProfilerDriver.enabled = m_profilerDriverWasEnabled;
            ProfilerDriver.profileEditor = m_profileEditorWasEnabled;
            Profiler.enableAllocationCallstacks = m_allocationCallstacksWereEnabled;
#endif
        }

        [Serializable]
        private sealed class PrebridgeAttributionReport
        {
            public int schemaVersion;
            public int attributionVersion;
            public string suite;
            public string generatedAtUtc;
            public string completedAtUtc;
            public string unityVersion;
            public string scriptingBackend;
            public string onityAsyncSha256;
            public string completionSourceSha256;
            public string runnerSha256;
            public string menuSha256;
            public string runtimeAsmdefSha256;
            public string editorAsmdefSha256;
            public string manifestSha256;
            public string lockSha256;
            public string uniTaskCommit;
            public string scenario;
            public int operationsPerSample;
            public int samplesPerCase;
            public int warmupBatches;
            public string scope;
            public bool allocationCallstacksEnabled;
            public bool deepProfilingEnabled;
            public long positiveControlBytes;
            public long emptyControlBytes;
            public PrebridgeAttributionSample positiveControl;
            public PrebridgeAttributionSample emptyControl;
            public PrebridgeAttributionSample[] emptyHarnessSamples;
            public PrebridgeAttributionSample[] samples;
        }

        [Serializable]
        private sealed class PrebridgeAttributionSample
        {
            public int sampleIndex;
            public int frameIndex;
            public int threadIndex;
            public int markerSampleIndex;
            public string markerName;
            public long markerTotalBytes;
            public long attributedBytes;
            public long unresolvedBytes;
            public PrebridgeAllocationEvent[] events;
        }

        [Serializable]
        private sealed class PrebridgeAllocationEvent
        {
            public int profilerSampleIndex;
            public int parentSampleIndex;
            public string parentSampleName;
            public long bytes;
            public string[] rawAddresses;
            public string[] resolvedMethods;
            public string[] resolutionErrors;
            public int resolvedAddressCount;
            public int unresolvedAddressCount;
        }

        [Serializable]
        private sealed class TypedWhenAllReport
        {
            public int schemaVersion;
            public int harnessVersion;
            public string suite;
            public string generatedAtUtc;
            public string allocationGeneratedAtUtc;
            public string unityVersion;
            public string scriptingBackend;
            public string onityAsyncSha256;
            public string completionSourceSha256;
            public string runnerSha256;
            public string menuSha256;
            public string runtimeAsmdefSha256;
            public string editorAsmdefSha256;
            public string manifestSha256;
            public string lockSha256;
            public string uniTaskCommit;
            public int operationsPerBatch;
            public int timingBatchesPerSample;
            public int samplesPerCase;
            public int warmupBatches;
            public long stopwatchFrequency;
            public string scope;
            public double[] emptyHarnessSampleNanosecondsPerOperation;
            public long[] emptyHarnessSampleAllocatedBytes;
            public bool allocationsAvailable;
            public string allocationCounter;
            public long positiveControlBytes;
            public long emptyControlBytes;
            public bool workerAllocationsAvailable;
            public bool allThreadAllocationsAvailable;
            public string allThreadAllocationError;
            public long allThreadMainPositiveBytes;
            public long allThreadOtherPositiveBytes;
            public long allThreadMainEmptyBytes;
            public long allThreadOtherEmptyBytes;
            public long allThreadWorkerPositiveMainBytes;
            public long allThreadWorkerPositiveOtherBytes;
            public long workerPositiveControlBytes;
            public long workerEmptyControlBytes;
            public string workerAllocationError;
            public long[] saturationSampleAllocatedBytes;
            public TypedWhenAllExtraMetric[] coldScheduling;
            public TypedWhenAllExtraMetric[] burstScheduling;
            public TypedWhenAllScenario[] scenarios;
        }

        [Serializable]
        private sealed class TypedWhenAllExtraMetric
        {
            public string name;
            public string library;
            public int operations;
            public long[] sampleAllocatedBytes;
        }

        [Serializable]
        private sealed class TypedWhenAllScenario
        {
            public string name;
            public bool pending;
            public bool onityTrackerEnabled;
            public bool fullLifecycle;
            public int outcome;
            public bool bridgePresent;
            public int inputCount;
            public int timingBatches;
            public TypedWhenAllMetric[] results;
        }

        [Serializable]
        private sealed class TypedWhenAllMetric
        {
            public string library;
            public double meanNanosecondsPerOperation;
            public double medianNanosecondsPerOperation;
            public double minNanosecondsPerOperation;
            public double maxNanosecondsPerOperation;
            public double standardDeviationNanosecondsPerOperation;
            public double bytesPerOperation;
            public double[] sampleNanosecondsPerOperation;
            public long[] sampleAllocatedBytes;
            public double workerBytesPerOperation;
            public long[] workerSampleAllocatedBytes;
            public long[] allThreadSampleAllocatedBytes;
            public long[] otherThreadSampleAllocatedBytes;
        }
    }
}
