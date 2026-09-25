using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Compares <c>OnityTask.SwitchToMainThread</c> with <c>UniTask.SwitchToMainThread</c>.
    /// It measures the main-thread synchronous path, worker-thread scheduling, main-thread dispatch
    /// inside one drain, and a matched thread-pool round trip. Requires the pinned UniTask test
    /// dependency and the <c>ONITY_TASK_BENCHMARKS</c> define.
    /// </summary>
    public sealed class OnityThreadSwitchBenchmarkRunner : MonoBehaviour
    {
        private const int k_warmupIterations = 4096;
        private const int k_samplesPerCase = 8;
        private const int k_synchronousIterations = 1000000;
        private const int k_canceledIterations = 20000;
        private const int k_batchesPerSample = 8;
        private const int k_steadyConcurrency = 128;
        private const int k_burstConcurrency = 4096;
        private const int k_roundTripConcurrency = 128;
        private const int k_completionTimeoutFrames = 600;
        private const int k_scenarioCount = 7;

        private static readonly Action s_continuation = OnContinuation;
        private static readonly CancellationTokenSource s_canceledSource = CreateCanceledSource();
        private static readonly CancellationToken s_canceledToken = s_canceledSource.Token;

        private static bool s_isRunning;
        private static int s_completedCount;
        private static int s_expectedCount;
        private static int s_canceledCount;
        private static long s_firstTimestamp;
        private static long s_lastTimestamp;
        private static long s_firstBytes;
        private static long s_lastBytes;
        private static bool s_mainThreadAllocationAvailable;

        private string m_latestJson;
        private Action<string, Exception> m_completed;
        private string m_codeOptimization;
        private bool m_workerAllocationAvailable = true;
        private string m_workerAllocationCounter = "Not measured";

        /// <summary>
        /// Queues a benchmark run. The optional callback receives a report path or failure.
        /// </summary>
        /// <param name="latestJson">Output JSON path.</param>
        /// <param name="completed">Optional completion callback.</param>
        /// <param name="codeOptimization">
        /// Editor code optimization mode recorded with the report, because the Debug mode disables
        /// JIT inlining and register allocation and changes per-item dispatch cost.
        /// </param>
        public static void Run(
            string latestJson,
            Action<string, Exception> completed = null,
            string codeOptimization = null)
        {
            if (string.IsNullOrWhiteSpace(latestJson))
            {
                throw new ArgumentException("Benchmark output path is required.", nameof(latestJson));
            }

            if (s_isRunning)
            {
                throw new InvalidOperationException("An OnityTask thread-switch benchmark is already running.");
            }

            GameObject runnerObject = new GameObject("Onity Thread Switch Benchmark Runner");
            DontDestroyOnLoad(runnerObject);
            OnityThreadSwitchBenchmarkRunner runner = runnerObject.AddComponent<OnityThreadSwitchBenchmarkRunner>();
            runner.m_latestJson = Path.GetFullPath(latestJson);
            runner.m_completed = completed;
            runner.m_codeOptimization = string.IsNullOrEmpty(codeOptimization) ? "Unknown" : codeOptimization;
            s_isRunning = true;
        }

        private IEnumerator Start()
        {
            yield return null;
            Exception failure = null;
            ThreadSwitchBenchmarkReport report = null;

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
                IEnumerator frameBenchmarks = RunQueuedBenchmarks(report);
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
                    report.workerAllocationsAvailable = m_workerAllocationAvailable;
                    report.workerAllocationCounter = m_workerAllocationCounter;
                    SaveReport(report, m_latestJson);
                    Debug.Log($"OnityTask thread-switch benchmark completed: {m_latestJson}", this);
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

        private ThreadSwitchBenchmarkReport CreateReport()
        {
            ThreadSwitchBenchmarkReport report = new ThreadSwitchBenchmarkReport
            {
                schemaVersion = 1,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                isEditor = Application.isEditor,
                scriptingBackend = GetScriptingBackendLabel(),
                codeOptimization = m_codeOptimization,
                gcIncremental = UnityEngine.Scripting.GarbageCollector.isIncremental,
                uniTaskAssembly = typeof(UniTask).Assembly.FullName,
                stopwatchFrequency = Stopwatch.Frequency,
                timerResolutionNanoseconds = 1000000000d / Stopwatch.Frequency,
                samplesPerCase = k_samplesPerCase,
                warmupIterations = k_warmupIterations,
                batchesPerSample = k_batchesPerSample,
                measurementScope = "Main-thread synchronous slices include the delegate call, awaiter creation, "
                    + "IsCompleted and GetResult. Worker scheduling times awaitable creation, IsCompleted and "
                    + "UnsafeOnCompleted on a dedicated worker thread. Main-thread dispatch spans the first to the "
                    + "last continuation of one batch inside one drain, so it covers N-1 continuations. The round "
                    + "trip spans starting N async methods on the main thread until the last one resumes after a "
                    + "thread-pool hop and a switch; thread-pool latency and frame waits are included. Raw times "
                    + "include harness overhead; no baseline subtraction or overall winner is inferred.",
                scenarios = new ThreadSwitchScenarioReport[k_scenarioCount]
            };

            CalibrateMainThreadAllocationCounter(report);
            return report;
        }

        private static void CalibrateMainThreadAllocationCounter(ThreadSwitchBenchmarkReport report)
        {
#if ENABLE_IL2CPP
            s_mainThreadAllocationAvailable = false;
            report.mainThreadAllocationCalibrationBytes = 0;
            report.mainThreadEmptyAllocationDeltaBytes = 0;
            report.mainThreadAllocationCounter = "Unavailable: Unity 2022 IL2CPP managed allocation counter is disabled "
                + "because the existing DI harness observed crashes. Use a profiler capture for allocations.";
#else
            try
            {
                s_mainThreadAllocationAvailable = CalibrateCurrentThreadAllocationCounter(
                    out long calibrationBytes, out long emptyDeltaBytes);
                report.mainThreadAllocationCalibrationBytes = calibrationBytes;
                report.mainThreadEmptyAllocationDeltaBytes = emptyDeltaBytes;
                report.mainThreadAllocationCounter = s_mainThreadAllocationAvailable
                    ? "GC.GetAllocatedBytesForCurrentThread; 64 KiB positive control and empty control passed."
                    : "Unavailable: allocation counter calibration failed.";
            }
            catch (Exception exception)
            {
                s_mainThreadAllocationAvailable = false;
                report.mainThreadAllocationCounter = "Unavailable: " + exception.GetType().Name;
            }
#endif
            report.mainThreadAllocationsAvailable = s_mainThreadAllocationAvailable;
        }

        private static bool CalibrateCurrentThreadAllocationCounter(out long calibrationBytes, out long emptyDeltaBytes)
        {
#if ENABLE_IL2CPP
            calibrationBytes = 0;
            emptyDeltaBytes = 0;
            return false;
#else
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] calibration = new byte[65536];
            long after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(calibration);
            calibrationBytes = after - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            after = GC.GetAllocatedBytesForCurrentThread();
            emptyDeltaBytes = after - before;
            return calibrationBytes >= 65536 && emptyDeltaBytes == 0;
#endif
        }

        private static long ReadMainThreadAllocatedBytes()
        {
#if ENABLE_IL2CPP
            return 0;
#else
            return s_mainThreadAllocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif
        }

        private void RunSynchronousBenchmarks(ThreadSwitchBenchmarkReport report)
        {
            Action empty = EmptyOperation;
            SampleSet baseline = new SampleSet();

            for (int i = 0; i < k_warmupIterations; i++)
            {
                empty();
                MeasureOnitySwitchOnMainThread();
                MeasureUniTaskSwitchOnMainThread();
                MeasureOnityCanceledSwitchOnMainThread();
                MeasureUniTaskCanceledSwitchOnMainThread();
            }

            for (int sample = 0; sample < k_samplesPerCase; sample++)
            {
                MeasureLoop(empty, baseline, sample, k_synchronousIterations);
            }

            report.synchronousHarnessBaseline = BuildMetric("Empty delegate loop", baseline, k_synchronousIterations, true);
            report.scenarios[0] = MeasureSynchronousScenario(
                "Main-thread switch", k_synchronousIterations,
                MeasureOnitySwitchOnMainThread, MeasureUniTaskSwitchOnMainThread);
            report.scenarios[1] = MeasureSynchronousScenario(
                "Main-thread switch with pre-canceled token", k_canceledIterations,
                MeasureOnityCanceledSwitchOnMainThread, MeasureUniTaskCanceledSwitchOnMainThread);
        }

        private ThreadSwitchScenarioReport MeasureSynchronousScenario(
            string name, int iterations, Action onity, Action uniTask)
        {
            SampleSet onitySamples = new SampleSet();
            SampleSet uniTaskSamples = new SampleSet();
            for (int sample = 0; sample < k_samplesPerCase; sample++)
            {
                ForceFullGc();
                if ((sample & 1) == 0)
                {
                    MeasureLoop(onity, onitySamples, sample, iterations);
                    MeasureLoop(uniTask, uniTaskSamples, sample, iterations);
                }
                else
                {
                    MeasureLoop(uniTask, uniTaskSamples, sample, iterations);
                    MeasureLoop(onity, onitySamples, sample, iterations);
                }
            }

            return BuildScenario(name, "Synchronous main thread; delegate invocation included", 1,
                iterations, onitySamples, uniTaskSamples, true, true);
        }

        private static void MeasureLoop(Action operation, SampleSet samples, int sample, int iterations)
        {
            long bytes = ReadMainThreadAllocatedBytes();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                operation();
            }

            long stopped = Stopwatch.GetTimestamp();
            samples.bytes[sample] += ReadMainThreadAllocatedBytes() - bytes;
            samples.ticks[sample] += stopped - started;
        }

        private IEnumerator RunQueuedBenchmarks(ThreadSwitchBenchmarkReport report)
        {
            // Make sure the Onity runner exists before any worker thread schedules a switch.
            OnityTask runnerWarmup = OnityTask.NextFrame();
            int remainingWarmupFrames = k_completionTimeoutFrames;
            while (!runnerWarmup.IsCompleted)
            {
                yield return null;
                if (--remainingWarmupFrames == 0)
                {
                    throw new TimeoutException("Onity runner warmup did not complete.");
                }
            }

            runnerWarmup.GetAwaiter().GetResult();

            for (int cohort = 0; cohort < 2; cohort++)
            {
                int concurrency = cohort == 0 ? k_steadyConcurrency : k_burstConcurrency;
                string workload = cohort == 0
                    ? "Warm steady state; 128 continuations per batch"
                    : "Repeated burst; 4096 continuations per batch";

                for (int warmup = 0; warmup < 2; warmup++)
                {
                    for (int library = 0; library < 2; library++)
                    {
                        WorkerSchedulingJob job = ScheduleFromWorker(library, concurrency);
                        IEnumerator wait = WaitForContinuations(concurrency, "Thread-switch warmup");
                        while (wait.MoveNext())
                        {
                            yield return null;
                        }

                        RecordWorkerCalibration(job);
                    }
                }

                SampleSet[] scheduling = { new SampleSet(), new SampleSet() };
                SampleSet[] dispatch = { new SampleSet(), new SampleSet() };
                for (int sample = 0; sample < k_samplesPerCase; sample++)
                {
                    ForceFullGc();
                    for (int batch = 0; batch < k_batchesPerSample; batch++)
                    {
                        for (int turn = 0; turn < 2; turn++)
                        {
                            int library = (sample + batch + turn) & 1;
                            WorkerSchedulingJob job = ScheduleFromWorker(library, concurrency);
                            scheduling[library].ticks[sample] += job.Ticks;
                            scheduling[library].bytes[sample] += job.Bytes;
                            RecordWorkerCalibration(job);

                            IEnumerator wait = WaitForContinuations(concurrency, "Thread-switch sample");
                            while (wait.MoveNext())
                            {
                                yield return null;
                            }

                            dispatch[library].ticks[sample] += s_lastTimestamp - s_firstTimestamp;
                            dispatch[library].bytes[sample] += s_lastBytes - s_firstBytes;
                        }
                    }
                }

                int index = 2 + cohort * 2;
                int operations = concurrency * k_batchesPerSample;
                report.scenarios[index] = BuildScenario("Worker scheduling", workload, concurrency,
                    operations, scheduling[0], scheduling[1], false, m_workerAllocationAvailable);
                report.scenarios[index + 1] = BuildScenario("Main-thread dispatch", workload, concurrency,
                    (concurrency - 1) * k_batchesPerSample, dispatch[0], dispatch[1], true, true);
            }

            IEnumerator roundTrip = RunRoundTripBenchmark(report);
            while (roundTrip.MoveNext())
            {
                yield return null;
            }
        }

        private IEnumerator RunRoundTripBenchmark(ThreadSwitchBenchmarkReport report)
        {
            for (int warmup = 0; warmup < 2; warmup++)
            {
                for (int library = 0; library < 2; library++)
                {
                    StartRoundTrips(library, k_roundTripConcurrency);
                    IEnumerator wait = WaitForContinuations(k_roundTripConcurrency, "Round-trip warmup");
                    while (wait.MoveNext())
                    {
                        yield return null;
                    }
                }
            }

            SampleSet[] roundTrips = { new SampleSet(), new SampleSet() };
            for (int sample = 0; sample < k_samplesPerCase; sample++)
            {
                ForceFullGc();
                for (int batch = 0; batch < k_batchesPerSample; batch++)
                {
                    for (int turn = 0; turn < 2; turn++)
                    {
                        int library = (sample + batch + turn) & 1;
                        long bytes = ReadMainThreadAllocatedBytes();
                        long started = Stopwatch.GetTimestamp();
                        StartRoundTrips(library, k_roundTripConcurrency);
                        IEnumerator wait = WaitForContinuations(k_roundTripConcurrency, "Round-trip sample");
                        while (wait.MoveNext())
                        {
                            yield return null;
                        }

                        roundTrips[library].ticks[sample] += s_lastTimestamp - started;
                        roundTrips[library].bytes[sample] += s_lastBytes - bytes;
                    }
                }
            }

            report.scenarios[6] = BuildScenario("Async round trip: thread-pool hop then switch",
                "128 concurrent async Task methods per batch; thread-pool latency and frame waits included",
                k_roundTripConcurrency, k_roundTripConcurrency * k_batchesPerSample,
                roundTrips[0], roundTrips[1], true, true);
        }

        private static WorkerSchedulingJob ScheduleFromWorker(int library, int count)
        {
            ResetCounters(count);
            WorkerSchedulingJob job = new WorkerSchedulingJob { Library = library, Count = count };
            Thread worker = new Thread(RunWorkerSchedulingJob)
            {
                IsBackground = true,
                Name = "Onity thread-switch benchmark worker"
            };
            worker.Start(job);
            worker.Join();

            if (job.Failure != null)
            {
                throw new InvalidOperationException("Worker scheduling failed.", job.Failure);
            }

            return job;
        }

        private static void RunWorkerSchedulingJob(object state)
        {
            WorkerSchedulingJob job = (WorkerSchedulingJob)state;
            try
            {
                job.AllocationAvailable = CalibrateCurrentThreadAllocationCounter(
                    out job.CalibrationBytes, out job.EmptyDeltaBytes);
                long bytes = job.AllocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0;
                long started = Stopwatch.GetTimestamp();
                if (job.Library == 0)
                {
                    ScheduleOnityFromWorker(job.Count);
                }
                else
                {
                    ScheduleUniTaskFromWorker(job.Count);
                }

                long stopped = Stopwatch.GetTimestamp();
                job.Bytes = job.AllocationAvailable ? GC.GetAllocatedBytesForCurrentThread() - bytes : 0;
                job.Ticks = stopped - started;
            }
            catch (Exception exception)
            {
                job.Failure = exception;
            }
        }

        private void RecordWorkerCalibration(WorkerSchedulingJob job)
        {
            if (!job.AllocationAvailable)
            {
                m_workerAllocationAvailable = false;
                m_workerAllocationCounter = "Unavailable: a worker thread failed the 64 KiB positive or empty control ("
                    + job.CalibrationBytes.ToString(CultureInfo.InvariantCulture) + " / "
                    + job.EmptyDeltaBytes.ToString(CultureInfo.InvariantCulture) + " bytes).";
                return;
            }

            if (m_workerAllocationAvailable && m_workerAllocationCounter == "Not measured")
            {
                m_workerAllocationCounter = "GC.GetAllocatedBytesForCurrentThread on each worker thread; "
                    + "64 KiB positive control and empty control passed on every scheduling job.";
            }
        }

        private static void ScheduleOnityFromWorker(int count)
        {
            for (int i = 0; i < count; i++)
            {
                OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    throw new InvalidOperationException("Onity worker scheduling ran on the main thread.");
                }

                awaiter.UnsafeOnCompleted(s_continuation);
            }
        }

        private static void ScheduleUniTaskFromWorker(int count)
        {
            for (int i = 0; i < count; i++)
            {
                SwitchToMainThreadAwaitable.Awaiter awaiter = UniTask.SwitchToMainThread().GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    throw new InvalidOperationException("UniTask worker scheduling ran on the main thread.");
                }

                awaiter.UnsafeOnCompleted(s_continuation);
            }
        }

        private static void StartRoundTrips(int library, int count)
        {
            ResetCounters(count);
            for (int i = 0; i < count; i++)
            {
                if (library == 0)
                {
                    RoundTripOnityAsync().Forget();
                }
                else
                {
                    RoundTripUniTaskAsync().Forget();
                }
            }
        }

        private static async Task RoundTripOnityAsync()
        {
            await new ThreadPoolHop();
            await OnityTask.SwitchToMainThread();
            OnContinuation();
        }

        private static async Task RoundTripUniTaskAsync()
        {
            await new ThreadPoolHop();
            await UniTask.SwitchToMainThread();
            OnContinuation();
        }

        private static IEnumerator WaitForContinuations(int expected, string label)
        {
            int remaining = k_completionTimeoutFrames;
            while (s_completedCount < expected)
            {
                yield return null;
                if (--remaining == 0)
                {
                    throw new TimeoutException(label + " did not complete: " + s_completedCount + "/" + expected + ".");
                }
            }

            if (s_completedCount != expected)
            {
                throw new InvalidOperationException(label + " ran " + s_completedCount + " continuations for " + expected + ".");
            }
        }

        private static void ResetCounters(int expected)
        {
            s_completedCount = 0;
            s_expectedCount = expected;
            s_firstTimestamp = 0;
            s_lastTimestamp = 0;
            s_firstBytes = 0;
            s_lastBytes = 0;
        }

        private static void OnContinuation()
        {
            long now = Stopwatch.GetTimestamp();
            int completed = ++s_completedCount;
            if (completed == 1)
            {
                s_firstTimestamp = now;
                s_firstBytes = ReadMainThreadAllocatedBytes();
            }

            if (completed == s_expectedCount)
            {
                s_lastTimestamp = now;
                s_lastBytes = ReadMainThreadAllocatedBytes();
            }
        }

        private ThreadSwitchScenarioReport BuildScenario(
            string name,
            string workload,
            int concurrency,
            int operations,
            SampleSet onity,
            SampleSet uniTask,
            bool mainThreadAllocations,
            bool workerAllocations)
        {
            bool allocationsAvailable = mainThreadAllocations ? s_mainThreadAllocationAvailable : workerAllocations;
            return new ThreadSwitchScenarioReport
            {
                displayName = name,
                workload = workload,
                concurrency = concurrency,
                iterationsPerSample = operations,
                allocationThread = mainThreadAllocations ? "Main thread" : "Worker thread",
                results = new[]
                {
                    BuildMetric("OnityTask", onity, operations, allocationsAvailable),
                    BuildMetric("UniTask", uniTask, operations, allocationsAvailable)
                }
            };
        }

        private static ThreadSwitchMetricReport BuildMetric(
            string label, SampleSet samples, int operations, bool allocationsAvailable)
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
            return new ThreadSwitchMetricReport
            {
                library = label,
                meanMilliseconds = mean,
                medianMilliseconds = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2d,
                minMilliseconds = sorted[0],
                maxMilliseconds = sorted[sorted.Length - 1],
                standardDeviationMilliseconds = Math.Sqrt(variance / milliseconds.Length),
                nanosecondsPerOperation = mean * 1000000d / operations,
                allocatedBytesPerOperation = allocationsAvailable
                    ? (double)totalBytes / (milliseconds.Length * (long)operations) : -1d,
                sampleMilliseconds = milliseconds,
                sampleAllocatedBytes = allocationsAvailable ? samples.bytes : null
            };
        }

        private static void EmptyOperation()
        {
        }

        private static void MeasureOnitySwitchOnMainThread()
        {
            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread().GetAwaiter();
            if (awaiter.IsCompleted)
            {
                awaiter.GetResult();
                return;
            }

            throw new InvalidOperationException("Onity main-thread switch did not complete synchronously.");
        }

        private static void MeasureUniTaskSwitchOnMainThread()
        {
            SwitchToMainThreadAwaitable.Awaiter awaiter = UniTask.SwitchToMainThread().GetAwaiter();
            if (awaiter.IsCompleted)
            {
                awaiter.GetResult();
                return;
            }

            throw new InvalidOperationException("UniTask main-thread switch did not complete synchronously.");
        }

        private static void MeasureOnityCanceledSwitchOnMainThread()
        {
            OnityTaskThreadSwitchAwaiter awaiter = OnityTask.SwitchToMainThread(s_canceledToken).GetAwaiter();
            if (!awaiter.IsCompleted)
            {
                throw new InvalidOperationException("Onity canceled switch did not complete synchronously.");
            }

            try
            {
                awaiter.GetResult();
            }
            catch (OperationCanceledException)
            {
                s_canceledCount++;
            }
        }

        private static void MeasureUniTaskCanceledSwitchOnMainThread()
        {
            SwitchToMainThreadAwaitable.Awaiter awaiter = UniTask.SwitchToMainThread(s_canceledToken).GetAwaiter();
            if (!awaiter.IsCompleted)
            {
                throw new InvalidOperationException("UniTask canceled switch did not complete synchronously.");
            }

            try
            {
                awaiter.GetResult();
            }
            catch (OperationCanceledException)
            {
                s_canceledCount++;
            }
        }

        private static CancellationTokenSource CreateCanceledSource()
        {
            CancellationTokenSource source = new CancellationTokenSource();
            source.Cancel();
            return source;
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

        private static void SaveReport(ThreadSwitchBenchmarkReport report, string latestJson)
        {
            string directory = Path.GetDirectoryName(latestJson);
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, $"onity-thread-switch-benchmark-{stamp}.json"), json, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            File.WriteAllText(Path.ChangeExtension(latestJson, ".csv"), BuildCsv(report), Encoding.UTF8);
            File.WriteAllText(Path.ChangeExtension(latestJson, ".md"), BuildMarkdown(report), Encoding.UTF8);
        }

        private static string BuildCsv(ThreadSwitchBenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("scenario,workload,concurrency,allocation_thread,library,operations_per_sample,mean_ms,"
                + "median_ms,min_ms,max_ms,stddev_ms,ns_per_op,bytes_per_op");
            foreach (ThreadSwitchScenarioReport scenario in report.scenarios)
            {
                foreach (ThreadSwitchMetricReport metric in scenario.results)
                {
                    builder.Append(EscapeCsv(scenario.displayName)).Append(',');
                    builder.Append(EscapeCsv(scenario.workload)).Append(',');
                    builder.Append(scenario.concurrency).Append(',');
                    builder.Append(EscapeCsv(scenario.allocationThread)).Append(',');
                    builder.Append(metric.library).Append(',');
                    builder.Append(scenario.iterationsPerSample).Append(',');
                    builder.Append(Number(metric.meanMilliseconds)).Append(',');
                    builder.Append(Number(metric.medianMilliseconds)).Append(',');
                    builder.Append(Number(metric.minMilliseconds)).Append(',');
                    builder.Append(Number(metric.maxMilliseconds)).Append(',');
                    builder.Append(Number(metric.standardDeviationMilliseconds)).Append(',');
                    builder.Append(Number(metric.nanosecondsPerOperation)).Append(',');
                    builder.Append(Number(metric.allocatedBytesPerOperation)).AppendLine();
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(ThreadSwitchBenchmarkReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# OnityTask thread-switch benchmark");
            builder.AppendLine();
            builder.AppendLine($"- Generated (UTC): {report.generatedAtUtc}");
            builder.AppendLine($"- Unity: {report.unityVersion} ({report.platform}, {report.scriptingBackend})");
            builder.AppendLine($"- Editor code optimization: {report.codeOptimization}; incremental GC: {report.gcIncremental}");
            builder.AppendLine($"- UniTask: {report.uniTaskAssembly}");
            builder.AppendLine($"- Main-thread allocation counter: {report.mainThreadAllocationCounter}");
            builder.AppendLine($"- Worker allocation counter: {report.workerAllocationCounter}");
            builder.AppendLine($"- Scope: {report.measurementScope}");
            builder.AppendLine();
            builder.AppendLine("| Scenario | Concurrency | Library | Mean ns/op | Bytes/op | Allocation thread | Stddev ms |");
            builder.AppendLine("| --- | ---: | --- | ---: | ---: | --- | ---: |");
            foreach (ThreadSwitchScenarioReport scenario in report.scenarios)
            {
                foreach (ThreadSwitchMetricReport metric in scenario.results)
                {
                    builder.Append("| ").Append(scenario.displayName).Append(" | ");
                    builder.Append(scenario.concurrency).Append(" | ").Append(metric.library).Append(" | ");
                    builder.Append(metric.nanosecondsPerOperation.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(metric.allocatedBytesPerOperation < 0
                        ? "unavailable"
                        : metric.allocatedBytesPerOperation.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ");
                    builder.Append(scenario.allocationThread).Append(" | ");
                    builder.Append(metric.standardDeviationMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
                    builder.AppendLine(" |");
                }
            }

            return builder.ToString();
        }

        private static string EscapeCsv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        private sealed class WorkerSchedulingJob
        {
            public int Library;
            public int Count;
            public long Ticks;
            public long Bytes;
            public long CalibrationBytes;
            public long EmptyDeltaBytes;
            public bool AllocationAvailable;
            public Exception Failure;
        }

        /// <summary>
        /// Moves the awaiting method onto a thread-pool thread so the following switch is queued.
        /// </summary>
        private readonly struct ThreadPoolHop : ICriticalNotifyCompletion
        {
            private static readonly WaitCallback s_invoke = state => ((Action)state)();

            public bool IsCompleted => false;

            public ThreadPoolHop GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                ThreadPool.QueueUserWorkItem(s_invoke, continuation);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                ThreadPool.UnsafeQueueUserWorkItem(s_invoke, continuation);
            }
        }

        private sealed class SampleSet
        {
            public readonly long[] ticks = new long[k_samplesPerCase];
            public readonly long[] bytes = new long[k_samplesPerCase];
        }

        [Serializable]
        private sealed class ThreadSwitchBenchmarkReport
        {
            public int schemaVersion;
            public string generatedAtUtc;
            public string unityVersion;
            public string platform;
            public bool isEditor;
            public string scriptingBackend;
            public string codeOptimization;
            public bool gcIncremental;
            public string uniTaskAssembly;
            public long stopwatchFrequency;
            public double timerResolutionNanoseconds;
            public int samplesPerCase;
            public int warmupIterations;
            public int batchesPerSample;
            public string measurementScope;
            public bool mainThreadAllocationsAvailable;
            public string mainThreadAllocationCounter;
            public long mainThreadAllocationCalibrationBytes;
            public long mainThreadEmptyAllocationDeltaBytes;
            public bool workerAllocationsAvailable;
            public string workerAllocationCounter;
            public ThreadSwitchMetricReport synchronousHarnessBaseline;
            public ThreadSwitchScenarioReport[] scenarios;
        }

        [Serializable]
        private sealed class ThreadSwitchScenarioReport
        {
            public string displayName;
            public string workload;
            public int concurrency;
            public int iterationsPerSample;
            public string allocationThread;
            public ThreadSwitchMetricReport[] results;
        }

        [Serializable]
        private sealed class ThreadSwitchMetricReport
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

    internal static class OnityThreadSwitchBenchmarkTaskExtensions
    {
        /// <summary>
        /// Observes a benchmark task's fault through the Unity log without awaiting it.
        /// </summary>
        /// <param name="task">Fire-and-forget task.</param>
        public static void Forget(this Task task)
        {
            task.ContinueWith(
                static faulted => Debug.LogException(faulted.Exception),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
