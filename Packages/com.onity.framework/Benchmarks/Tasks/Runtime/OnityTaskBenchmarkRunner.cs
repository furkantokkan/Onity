using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
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
        private bool m_useHeapDelta;

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

                if (!m_allocationCounterAvailable)
                {
                    // Unity 2022 Mono reports zero for the per-thread counter. Fall back to the
                    // process heap delta with the collector disabled inside each measured slice.
                    m_useHeapDelta = CalibrateHeapDeltaCounter(
                        out report.allocationCalibrationBytes, out report.emptyAllocationDeltaBytes);
                    m_allocationCounterAvailable = m_useHeapDelta;
                    report.allocationCounter = m_useHeapDelta
                        ? "GC.GetTotalMemory delta with GarbageCollector.GCMode disabled inside each measured "
                          + "slice; 64 KiB positive control and empty control passed. Process-wide, block "
                          + "granularity: read averages over many operations only."
                        : "Unavailable: per-thread counter and heap-delta fallback both failed calibration.";
                }
            }
            catch (Exception exception)
            {
                m_allocationCounterAvailable = false;
                m_useHeapDelta = false;
                report.allocationCounter = "Unavailable: " + exception.GetType().Name;
            }
#endif
            report.allocationCounterKind = m_useHeapDelta
                ? "HeapDelta"
                : m_allocationCounterAvailable ? "PerThread" : "None";
            report.allocationsAvailable = m_allocationCounterAvailable;
        }

        private static bool CalibrateHeapDeltaCounter(out long calibrationBytes, out long emptyDeltaBytes)
        {
            UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
            try
            {
                long before = GC.GetTotalMemory(false);
                byte[] calibration = new byte[65536];
                long after = GC.GetTotalMemory(false);
                GC.KeepAlive(calibration);
                calibrationBytes = after - before;
                before = GC.GetTotalMemory(false);
                after = GC.GetTotalMemory(false);
                emptyDeltaBytes = after - before;
            }
            finally
            {
                UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Enabled;
            }

            return calibrationBytes >= 65536 && emptyDeltaBytes == 0;
        }

        /// <summary>
        /// Opens a measured slice and returns the starting allocation reading. With the heap-delta
        /// counter the collector is disabled until <see cref="EndAllocationSlice"/>.
        /// </summary>
        private long BeginAllocationSlice()
        {
            if (m_useHeapDelta)
            {
                UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
            }

            return ReadAllocatedBytes();
        }

        /// <summary>
        /// Closes a measured slice and returns the bytes allocated since <paramref name="startBytes"/>.
        /// </summary>
        private long EndAllocationSlice(long startBytes)
        {
            long delta = ReadAllocatedBytes() - startBytes;
            if (m_useHeapDelta)
            {
                UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Enabled;
            }

            return delta;
        }

        private long ReadAllocatedBytes()
        {
#if ENABLE_IL2CPP
            return 0;
#else
            if (m_useHeapDelta)
            {
                return GC.GetTotalMemory(false);
            }

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
            long bytes = BeginAllocationSlice();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < k_synchronousIterations; i++)
            {
                operation();
            }

            long stopped = Stopwatch.GetTimestamp();
            samples.bytes[sample] = EndAllocationSlice(bytes);
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
                            long bytes = BeginAllocationSlice();
                            long started = Stopwatch.GetTimestamp();
                            Schedule(library, concurrency);
                            long stopped = Stopwatch.GetTimestamp();
                            creation[library].bytes[sample] += EndAllocationSlice(bytes);
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

                            bytes = BeginAllocationSlice();
                            started = Stopwatch.GetTimestamp();
                            Consume(library, concurrency);
                            stopped = Stopwatch.GetTimestamp();
                            consumption[library].bytes[sample] += EndAllocationSlice(bytes);
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
                                long bytes = BeginAllocationSlice();
                                long started = Stopwatch.GetTimestamp();
                                ScheduleAsyncMethods(library, concurrency, typed);
                                long stopped = Stopwatch.GetTimestamp();
                                creation[library].bytes[sample] += EndAllocationSlice(bytes);
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

                                bytes = BeginAllocationSlice();
                                started = Stopwatch.GetTimestamp();
                                ConsumeAsyncMethods(library, concurrency, typed);
                                stopped = Stopwatch.GetTimestamp();
                                consumption[library].bytes[sample] += EndAllocationSlice(bytes);
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
            public string allocationCounterKind;
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
}
