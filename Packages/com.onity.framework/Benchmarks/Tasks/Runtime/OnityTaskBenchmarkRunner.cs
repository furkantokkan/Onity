using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
#if UNITY_EDITOR
        private const int k_allocationIterations = 1024;
        private const int k_profilerReadTimeoutFrames = 60;
        private const int k_profilerWarmupFrames = 120;
        private const string k_allocationScope = "OnityTask.Benchmark.AllocationScope";
#endif

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
        private bool m_allocationOnly;
        private bool m_allocationCounterAvailable;
#if UNITY_EDITOR
        private bool m_lastProfiledAllocationValid;
        private long m_lastProfiledAllocationBytes;
        private int m_lastProfiledFirstFrame;
        private int m_lastProfiledLastFrame;
        private int m_lastProfiledFoundFrame;
        private int m_profilerSampleId;
        private bool m_profilerStateCaptured;
        private bool m_profilerWasEnabled;
        private bool m_profilerDriverWasEnabled;
        private bool m_profileEditorWasEnabled;
#endif

        /// <summary>
        /// Queues a benchmark run. The optional callback receives a report path or failure.
        /// </summary>
        /// <param name="latestJson">Output JSON path.</param>
        /// <param name="completed">Optional completion callback.</param>
        /// <param name="allocationOnly">Reads an existing timing report and adds profiled allocation samples.</param>
        public static void Run(string latestJson, Action<string, Exception> completed = null,
            bool allocationOnly = false)
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
            runner.m_allocationOnly = allocationOnly;
            s_isRunning = true;
        }

        private IEnumerator Start()
        {
            yield return null;
#if UNITY_EDITOR
            m_profilerWasEnabled = Profiler.enabled;
            m_profilerDriverWasEnabled = ProfilerDriver.enabled;
            m_profileEditorWasEnabled = ProfilerDriver.profileEditor;
            m_profilerStateCaptured = true;
            if (!m_allocationOnly)
            {
                Profiler.enabled = false;
                ProfilerDriver.enabled = false;
            }
#endif
            Exception failure = null;
            TaskBenchmarkReport report = null;
            try
            {
                if (m_allocationOnly)
                {
                    report = JsonUtility.FromJson<TaskBenchmarkReport>(File.ReadAllText(m_latestJson));
                    if (report?.scenarios == null || report.scenarios.Length != 16)
                    {
                        throw new InvalidDataException("Allocation pass requires a complete 16-scenario timing report.");
                    }

                    ClearAllocationResults(report);
                }
                else
                {
                    report = CreateReport();
                    RunSynchronousBenchmarks(report);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure == null && !m_allocationOnly)
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

            if (failure == null && m_allocationOnly)
            {
#if UNITY_EDITOR
                IEnumerator allocationBenchmarks = RunProfilerAllocationBenchmarks(report);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = allocationBenchmarks.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        report.allocationCounter = "Unavailable: Profiler allocation pass failed ("
                            + exception.GetType().Name + ").";
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return allocationBenchmarks.Current;
                }
#endif
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
#if UNITY_EDITOR
                RestoreProfilerState();
#endif
                s_isRunning = false;
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            RestoreProfilerState();
#endif
            s_isRunning = false;
        }

        private TaskBenchmarkReport CreateReport()
        {
            TaskBenchmarkReport report = new TaskBenchmarkReport
            {
                schemaVersion = 3,
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
                    + "Raw times include harness overhead; no baseline subtraction or overall winner is inferred. "
                    + "Editor allocation samples use a separate Profiler pass over the same synchronous slices.",
                scenarios = new TaskBenchmarkScenarioReport[16]
            };

            CalibrateAllocationCounter(report);
            return report;
        }

        private void CalibrateAllocationCounter(TaskBenchmarkReport report)
        {
#if UNITY_EDITOR
            report.allocationCounter = "Unavailable: Unity Profiler allocation pass has not passed calibration.";
#elif ENABLE_IL2CPP
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
#if UNITY_EDITOR || ENABLE_IL2CPP
            return 0;
#else
            return m_allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif
        }

#if UNITY_EDITOR
        private IEnumerator RunProfilerAllocationBenchmarks(TaskBenchmarkReport report)
        {
            m_lastProfiledFoundFrame = -1;
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;
            try
            {
                // In batch mode the Profiler history can lag Play Mode startup.
                int warmup = k_profilerWarmupFrames;
                while (ProfilerDriver.lastFrameIndex < 2 && warmup-- > 0)
                {
                    yield return null;
                }

                yield return CaptureProfiledAllocation(AllocatePositiveControl, 1);
                report.allocationCalibrationBytes = m_lastProfiledAllocationBytes;
                if (!m_lastProfiledAllocationValid || report.allocationCalibrationBytes < 65536)
                {
                    report.allocationCounter = "Unavailable: Profiler GC.Alloc positive control failed "
                        + "(captured=" + m_lastProfiledAllocationValid + ", bytes="
                        + report.allocationCalibrationBytes + ", frames=" + m_lastProfiledFirstFrame
                        + ".." + m_lastProfiledLastFrame + ").";
                    yield break;
                }

                yield return CaptureProfiledAllocation(EmptyOperation, 1);
                report.emptyAllocationDeltaBytes = m_lastProfiledAllocationBytes;
                if (!m_lastProfiledAllocationValid || report.emptyAllocationDeltaBytes != 0)
                {
                    report.allocationCounter = "Unavailable: Profiler GC.Alloc empty control failed.";
                    yield break;
                }

                Debug.Log("OnityTask allocation profiler controls passed: "
                    + report.allocationCalibrationBytes + " bytes positive; zero empty.", this);

                long[,,] samples = new long[report.scenarios.Length, 2, k_samplesPerCase];
                long[] baseline = new long[k_samplesPerCase];
                for (int sample = 0; sample < k_samplesPerCase; sample++)
                {
                    yield return CaptureProfiledAllocation(EmptyOperation, k_allocationIterations);
                    if (!m_lastProfiledAllocationValid)
                    {
                        report.allocationCounter = "Unavailable: empty delegate sample was not captured.";
                        yield break;
                    }

                    baseline[sample] = m_lastProfiledAllocationBytes;
                }

                int[] synchronousScenarios = { 0, 1, 6, 7 };
                Action[,] synchronousOperations =
                {
                    { MeasureOnityCompleted, MeasureUniTaskCompleted },
                    { MeasureOnityResult, MeasureUniTaskResult },
                    { MeasureOnityAsyncCompleted, MeasureUniTaskAsyncCompleted },
                    { MeasureOnityAsyncResult, MeasureUniTaskAsyncResult }
                };

                for (int kind = 0; kind < synchronousScenarios.Length; kind++)
                {
                    for (int library = 0; library < 2; library++)
                    {
                        for (int warmupCall = 0; warmupCall < k_warmupIterations; warmupCall++)
                        {
                            synchronousOperations[kind, library]();
                        }
                    }

                    Debug.Log("OnityTask allocation profiler synchronous scenario " + synchronousScenarios[kind], this);
                    for (int sample = 0; sample < k_samplesPerCase; sample++)
                    {
                        ForceFullGc();
                        for (int turn = 0; turn < 2; turn++)
                        {
                            int library = (sample + turn) & 1;
                            yield return CaptureProfiledAllocation(
                                synchronousOperations[kind, library], k_allocationIterations);
                            if (!m_lastProfiledAllocationValid)
                            {
                                report.allocationCounter = "Unavailable: synchronous operation sample was not captured.";
                                yield break;
                            }

                            samples[synchronousScenarios[kind], library, sample] = m_lastProfiledAllocationBytes;
                        }
                    }
                }

                for (int kind = 0; kind < 3; kind++)
                {
                    bool asyncMethod = kind != 0;
                    bool typed = kind == 2;
                    for (int cohort = 0; cohort < 2; cohort++)
                    {
                        int concurrency = cohort == 0 ? k_steadyConcurrency : k_burstConcurrency;
                        int schedulingScenario = kind == 0 ? 2 + cohort * 2 : 8 + (kind - 1) * 4 + cohort * 2;
                        for (int warmupBatch = 0; warmupBatch < 2; warmupBatch++)
                        {
                            for (int library = 0; library < 2; library++)
                            {
                                if (asyncMethod)
                                {
                                    ScheduleAsyncMethods(library, concurrency, typed);
                                }
                                else
                                {
                                    Schedule(library, concurrency);
                                }

                                int remaining = k_completionTimeoutFrames;
                                while (asyncMethod
                                    ? !AreAsyncMethodsCompleted(library, concurrency, typed)
                                    : !IsCompleted(library, concurrency))
                                {
                                    if (--remaining == 0)
                                    {
                                        report.allocationCounter = "Unavailable: profiled warmup timed out.";
                                        yield break;
                                    }

                                    yield return null;
                                }

                                if (asyncMethod)
                                {
                                    ConsumeAsyncMethods(library, concurrency, typed);
                                }
                                else
                                {
                                    Consume(library, concurrency);
                                }
                            }
                        }

                        Debug.Log("OnityTask allocation profiler frame scenario " + schedulingScenario, this);
                        for (int sample = 0; sample < k_samplesPerCase; sample++)
                        {
                            ForceFullGc();
                            for (int turn = 0; turn < 2; turn++)
                            {
                                int library = (sample + turn) & 1;
                                Action schedule = asyncMethod
                                    ? (Action)(() => ScheduleAsyncMethods(library, concurrency, typed))
                                    : () => Schedule(library, concurrency);
                                Action consume = asyncMethod
                                    ? (Action)(() => ConsumeAsyncMethods(library, concurrency, typed))
                                    : () => Consume(library, concurrency);

                                yield return CaptureProfiledAllocation(schedule, 1);
                                if (!m_lastProfiledAllocationValid)
                                {
                                    report.allocationCounter = "Unavailable: frame scheduling sample was not captured "
                                        + "(scenario=" + schedulingScenario + ", sample=" + sample
                                        + ", library=" + library + ", frames=" + m_lastProfiledFirstFrame
                                        + ".." + m_lastProfiledLastFrame + ", available="
                                        + ProfilerDriver.firstFrameIndex + ".." + ProfilerDriver.lastFrameIndex + ").";
                                    yield break;
                                }

                                samples[schedulingScenario, library, sample] = m_lastProfiledAllocationBytes;
                                int remaining = k_completionTimeoutFrames;
                                while (asyncMethod
                                    ? !AreAsyncMethodsCompleted(library, concurrency, typed)
                                    : !IsCompleted(library, concurrency))
                                {
                                    if (--remaining == 0)
                                    {
                                        report.allocationCounter = "Unavailable: profiled frame operation timed out.";
                                        yield break;
                                    }

                                    yield return null;
                                }

                                yield return CaptureProfiledAllocation(consume, 1);
                                if (!m_lastProfiledAllocationValid)
                                {
                                    report.allocationCounter = "Unavailable: frame consumption sample was not captured "
                                        + "(scenario=" + (schedulingScenario + 1) + ", sample=" + sample
                                        + ", library=" + library + ", frames=" + m_lastProfiledFirstFrame
                                        + ".." + m_lastProfiledLastFrame + ", available="
                                        + ProfilerDriver.firstFrameIndex + ".." + ProfilerDriver.lastFrameIndex + ").";
                                    yield break;
                                }

                                samples[schedulingScenario + 1, library, sample] = m_lastProfiledAllocationBytes;
                            }
                        }
                    }
                }

                SetProfiledAllocation(report.synchronousHarnessBaseline, baseline, k_allocationIterations);
                for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
                {
                    for (int library = 0; library < 2; library++)
                    {
                        long[] scenarioSamples = new long[k_samplesPerCase];
                        for (int sample = 0; sample < k_samplesPerCase; sample++)
                        {
                            scenarioSamples[sample] = samples[scenario, library, sample];
                        }

                        int operations = scenario == 0 || scenario == 1 || scenario == 6 || scenario == 7
                            ? k_allocationIterations : report.scenarios[scenario].concurrency;
                        SetProfiledAllocation(report.scenarios[scenario].results[library], scenarioSamples, operations);
                    }
                }

                m_allocationCounterAvailable = true;
                report.allocationsAvailable = true;
                report.allocationCounter = "Unity Profiler GC.Alloc sample metadata; separate marked operation pass; "
                    + "64 KiB positive and empty controls passed.";
                report.allocationIterationsPerSample = k_allocationIterations;
                report.allocationGeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            }
            finally
            {
                Profiler.enabled = m_profilerWasEnabled;
                ProfilerDriver.enabled = m_profilerDriverWasEnabled;
            }
        }

        private void RestoreProfilerState()
        {
            if (!m_profilerStateCaptured)
            {
                return;
            }

            m_profilerStateCaptured = false;
            Profiler.enabled = m_profilerWasEnabled;
            ProfilerDriver.enabled = m_profilerDriverWasEnabled;
            ProfilerDriver.profileEditor = m_profileEditorWasEnabled;
        }

        private IEnumerator CaptureProfiledAllocation(Action operation, int iterations)
        {
            m_lastProfiledAllocationValid = false;
            m_lastProfiledAllocationBytes = 0;
            int firstFrame = ProfilerDriver.lastFrameIndex;
            m_lastProfiledFirstFrame = firstFrame;
            int sampleId = m_profilerSampleId++;
            string markerName = k_allocationScope + "." + sampleId.ToString(CultureInfo.InvariantCulture);
            bool failed = false;
            try
            {
                Profiler.BeginSample(markerName);
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        operation();
                    }
                }
                finally
                {
                    Profiler.EndSample();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                failed = true;
            }

            if (failed)
            {
                yield break;
            }

            for (int attempt = 0; attempt < k_profilerReadTimeoutFrames; attempt++)
            {
                yield return null;
                m_lastProfiledLastFrame = ProfilerDriver.lastFrameIndex;
                bool captured = false;
                int foundFrame = -1;
                try
                {
                    captured = TryReadProfiledAllocation(
                        Math.Max(m_lastProfiledFoundFrame, ProfilerDriver.firstFrameIndex),
                        ProfilerDriver.lastFrameIndex, markerName,
                        out m_lastProfiledAllocationBytes, out foundFrame);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    failed = true;
                }

                if (failed)
                {
                    yield break;
                }

                if (captured)
                {
                    m_lastProfiledFoundFrame = foundFrame;
                    m_lastProfiledAllocationValid = true;
                    yield break;
                }
            }
        }

        private static bool TryReadProfiledAllocation(int firstFrame, int lastFrame,
            string markerName, out long bytes, out int foundFrame)
        {
            bytes = 0;
            foundFrame = -1;
            for (int frame = firstFrame;
                frame <= lastFrame; frame++)
            {
                for (int thread = 0; ; thread++)
                {
                    using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(frame, thread))
                    {
                        if (!frameData.valid)
                        {
                            break;
                        }

                        int scopeId = frameData.GetMarkerId(markerName);
                        int allocationId = frameData.GetMarkerId("GC.Alloc");
                        if (scopeId == FrameDataView.invalidMarkerId)
                        {
                            continue;
                        }

                        for (int sample = 0; sample < frameData.sampleCount; sample++)
                        {
                            if (frameData.GetSampleMarkerId(sample) != scopeId)
                            {
                                continue;
                            }

                            int end = sample + frameData.GetSampleChildrenCountRecursive(sample);
                            for (int child = sample + 1; child <= end; child++)
                            {
                                if (frameData.GetSampleMarkerId(child) == allocationId)
                                {
                                    bytes += frameData.GetSampleMetadataAsLong(child, 0);
                                }
                            }

                            foundFrame = frame;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void AllocatePositiveControl()
        {
            byte[] bytes = new byte[65536];
            GC.KeepAlive(bytes);
        }

        private static void SetProfiledAllocation(TaskBenchmarkMetricReport metric, long[] samples, int operations)
        {
            long total = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                total += samples[i];
            }

            metric.sampleAllocatedBytes = samples;
            metric.allocatedBytesPerOperation = (double)total / (samples.Length * (long)operations);
        }
#endif

        private static void ClearAllocationResults(TaskBenchmarkReport report)
        {
            report.schemaVersion = 3;
            report.allocationsAvailable = false;
            report.allocationCounter = "Unavailable: Unity Profiler allocation pass has not passed calibration.";
            report.allocationCalibrationBytes = 0;
            report.emptyAllocationDeltaBytes = 0;
            report.allocationIterationsPerSample = 0;
            report.allocationGeneratedAtUtc = null;
            report.synchronousHarnessBaseline.allocatedBytesPerOperation = -1;
            report.synchronousHarnessBaseline.sampleAllocatedBytes = null;
            foreach (TaskBenchmarkScenarioReport scenario in report.scenarios)
            {
                foreach (TaskBenchmarkMetricReport metric in scenario.results)
                {
                    metric.allocatedBytesPerOperation = -1;
                    metric.sampleAllocatedBytes = null;
                }
            }
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
                + "min_ms,max_ms,stddev_ms,ns_per_op,bytes_per_op,allocations_available,allocation_operations_per_sample");
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
                    builder.Append(report.allocationsAvailable ? "true" : "false").Append(',');
                    builder.AppendLine(report.allocationsAvailable
                        ? (scenario.concurrency == 1
                            ? report.allocationIterationsPerSample : scenario.concurrency).ToString()
                        : "-1");
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
            if (report.allocationIterationsPerSample > 0)
            {
                builder.AppendLine($"- Separate allocation pass: {report.allocationIterationsPerSample} "
                    + "synchronous iterations/sample; one cohort per frame-operation sample.");
            }
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
            public int allocationIterationsPerSample;
            public string allocationGeneratedAtUtc;
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
    /// Compares fresh, multi-consumer completion sources with a pinned UniTask build.
    /// Each stage is measured separately; setup and cleanup stay outside its timed slice.
    /// </summary>
    public sealed class OnityCompletionSourceBenchmarkRunner : MonoBehaviour
    {
        private const int k_operations = 256;
        private const int k_samples = 8;
        private const int k_timingBatchesPerSample = 16;
        private const int k_warmupBatches = 2;
        private const int k_profilerWaitFrames = 60;
        private const int k_profilerStartupFrames = 120;
        private const string k_scopePrefix = "Onity.CompletionSource.Allocation.";
        private const string k_pinnedUniTaskCommit = "2e993ff18f28c931602a07292df0b0804eebef99";
        private const string k_onityRuntimeCommit = "a7c7c9c07867a0f2a20a0b817ca0482e75b77795";
        private const string k_statusRuntimeCommit = "25c8e202214a1b7b5fa258feb87a0f5d91750ad4";
        private const string k_preserveRuntimeCommit = "c2f9358994b8198c12ab48e1569014787f65325e";

        private static bool s_isRunning;
        private static int s_callbacks;
        private static int s_completedStatusReads;
        private static readonly Action s_callback = CountCallback;
        private static int s_preserveCallbacks;
        private static int s_preserveResultSink;
        private static readonly Action s_preserveCallback = CountPreserveCallback;

        private readonly OnityTaskCompletionSource[] m_onityUntyped =
            new OnityTaskCompletionSource[k_operations];
        private readonly OnityTaskCompletionSource<int>[] m_onityTyped =
            new OnityTaskCompletionSource<int>[k_operations];
        private readonly UniTaskCompletionSource[] m_uniTaskUntyped =
            new UniTaskCompletionSource[k_operations];
        private readonly UniTaskCompletionSource<int>[] m_uniTaskTyped =
            new UniTaskCompletionSource<int>[k_operations];
        private readonly System.Threading.Tasks.Task[] m_bridges =
            new System.Threading.Tasks.Task[k_operations];
        private readonly OnityTaskCompletionSource[] m_onityPreserveInputs =
            new OnityTaskCompletionSource[k_operations];
        private readonly OnityTaskCompletionSource[] m_onitySecondPreserveInputs =
            new OnityTaskCompletionSource[k_operations];
        private readonly UniTaskCompletionSource[] m_uniTaskPreserveInputs =
            new UniTaskCompletionSource[k_operations];
        private readonly UniTaskCompletionSource[] m_uniTaskSecondPreserveInputs =
            new UniTaskCompletionSource[k_operations];
        private readonly OnityTask<int>[] m_onityTypedNativeTasks =
            new OnityTask<int>[k_operations];
        private readonly UniTask<int>[] m_uniTaskTypedNativeTasks =
            new UniTask<int>[k_operations];
        private readonly OnityTask<int>[] m_onityTypedPreservedTasks =
            new OnityTask<int>[k_operations];
        private readonly UniTask<int>[] m_uniTaskTypedPreservedTasks =
            new UniTask<int>[k_operations];
        private readonly System.Threading.Tasks.Task<int>[] m_uniTaskTypedFanoutTasks =
            new System.Threading.Tasks.Task<int>[k_operations];

        private string m_outputPath;
        private Action<string, Exception> m_completed;
        private bool m_allocationOnly;
        private bool m_attributionOnly;
        private bool m_statusProbe;
        private bool m_preserveBenchmark;
        private bool m_lifecycleBenchmark;
        private bool m_preserveSourceVerified;
        private CompletionScenario m_activeScenario;
        private int m_activeLibrary;
#if UNITY_EDITOR
        private bool m_profilerStateCaptured;
        private bool m_profilerWasEnabled;
        private bool m_profilerDriverWasEnabled;
        private bool m_profileEditorWasEnabled;
        private bool m_allocationCallstacksWereEnabled;
        private int m_lastCapturedFrame = -1;
        private string m_lastMarkerName;
        private int m_markerSequence;
        private bool m_lastAllocationValid;
        private long m_lastAllocationBytes;
#endif

        /// <summary>Starts the isolated completion-source comparison in Play Mode.</summary>
        public static void Run(string outputPath, Action<string, Exception> completed,
            bool allocationOnly, bool attributionOnly = false, bool statusProbe = false,
            bool preserveBenchmark = false, bool lifecycleBenchmark = false)
        {
            if (s_isRunning)
            {
                throw new InvalidOperationException("A completion-source benchmark is already running.");
            }

            GameObject runnerObject = new GameObject("Completion Source Benchmark Runner");
            DontDestroyOnLoad(runnerObject);
            OnityCompletionSourceBenchmarkRunner runner =
                runnerObject.AddComponent<OnityCompletionSourceBenchmarkRunner>();
            runner.m_outputPath = Path.GetFullPath(outputPath);
            runner.m_completed = completed;
            runner.m_allocationOnly = allocationOnly;
            runner.m_attributionOnly = attributionOnly;
            runner.m_statusProbe = statusProbe;
            runner.m_preserveBenchmark = preserveBenchmark || lifecycleBenchmark;
            runner.m_lifecycleBenchmark = lifecycleBenchmark;
            s_isRunning = true;
        }

        private IEnumerator Start()
        {
            yield return null;
#if UNITY_EDITOR
            m_profilerWasEnabled = Profiler.enabled;
            m_profilerDriverWasEnabled = ProfilerDriver.enabled;
            m_profileEditorWasEnabled = ProfilerDriver.profileEditor;
            m_allocationCallstacksWereEnabled = Profiler.enableAllocationCallstacks;
            m_profilerStateCaptured = true;
            if (!m_allocationOnly && !m_attributionOnly)
            {
                Profiler.enabled = false;
                ProfilerDriver.enabled = false;
            }
#endif

            Exception failure = null;
            CompletionReport report = null;
            CompletionAttributionReport attributionReport = null;
            try
            {
                if (m_attributionOnly)
                {
                    attributionReport = new CompletionAttributionReport
                    {
                        suite = m_preserveBenchmark
                            ? "Native OnityTask Preserve conversion allocation callstacks"
                            : "Completion-source allocation callstacks",
                        onityRuntimeCommit = m_preserveBenchmark
                            ? k_preserveRuntimeCommit : k_onityRuntimeCommit,
                        uniTaskCommit = k_pinnedUniTaskCommit,
                        unityVersion = Application.unityVersion,
                        operations = k_operations,
                        cases = new CompletionAttributionCase[4]
                    };
                }
                else if (m_allocationOnly)
                {
                    report = JsonUtility.FromJson<CompletionReport>(File.ReadAllText(m_outputPath));
                    if (report == null || report.schemaVersion != 1 ||
                        report.scenarios == null ||
                        report.scenarios.Length != (m_lifecycleBenchmark ? 3 :
                            m_statusProbe || m_preserveBenchmark ? 4 : 16) ||
                        (m_statusProbe && report.suite != "Completion-source IsCompleted probe") ||
                        (m_lifecycleBenchmark && report.suite != "Native OnityTask sharing lifecycle") ||
                        (m_preserveBenchmark && !m_lifecycleBenchmark &&
                            report.suite != "OnityTask Preserve vs UniTask sharing"))
                    {
                        throw new InvalidDataException("A complete completion-source timing report is required.");
                    }

                    if (m_preserveBenchmark || m_statusProbe)
                    {
                        CompletionReport expected = m_statusProbe ? CreateStatusReport() :
                            m_lifecycleBenchmark ? CreateLifecycleReport() : CreatePreserveReport();
                        ValidateAllocationInput(report, expected);
                    }

                    ClearAllocations(report);
                }
                else
                {
                    report = m_lifecycleBenchmark ? CreateLifecycleReport() :
                        m_preserveBenchmark ? CreatePreserveReport() :
                        m_statusProbe ? CreateStatusReport() : CreateReport();
                    RunTiming(report);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

#if UNITY_EDITOR
            if (failure == null && m_attributionOnly)
            {
                IEnumerator attribution = RunAttribution(attributionReport);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = attribution.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        attributionReport.reason = exception.ToString();
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return attribution.Current;
                }
            }

            if (failure == null && m_allocationOnly)
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
                        report.allocationCounter = "Unavailable: " + exception.GetType().Name;
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
                    if (m_attributionOnly)
                    {
                        File.WriteAllText(m_outputPath,
                            JsonUtility.ToJson(attributionReport, true));
                    }
                    else
                    {
                        SaveReport(report);
                    }
                    Debug.Log("Completion-source benchmark completed: " + m_outputPath, this);
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
#if UNITY_EDITOR
                RestoreProfilerState();
#endif
                s_isRunning = false;
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            RestoreProfilerState();
#endif
            s_isRunning = false;
        }

        private static CompletionReport CreateReport()
        {
            CompletionReport report = new CompletionReport
            {
                schemaVersion = 1,
                suite = "OnityTaskCompletionSource vs UniTaskCompletionSource",
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scriptingBackend = "Editor Mono",
                uniTaskCommit = k_pinnedUniTaskCommit,
                onityRuntimeCommit = k_onityRuntimeCommit,
                operationsPerSample = k_operations,
                timingBatchesPerSample = k_timingBatchesPerSample,
                timingOperationsPerSample = k_operations * k_timingBatchesPerSample,
                allocationOperationsPerSample = k_operations,
                samplesPerCase = k_samples,
                warmupBatches = k_warmupBatches,
                scope = "Synchronous main-thread slices only. Construction, pending registration, "
                    + "completion with callback dispatch, late registration, and pending AsTask conversion "
                    + "are isolated. Source preparation and cleanup are outside each slice. "
                    + "Samples retain harness overhead; no baseline subtraction or overall winner is inferred.",
                allocationCounter = "Unavailable: separate Unity Profiler pass has not passed calibration.",
                scenarios = new CompletionScenario[16]
            };

            int index = 0;
            for (int typed = 0; typed < 2; typed++)
            {
                for (int stage = 0; stage < 5; stage++)
                {
                    int consumerVariants = stage == 0 || stage == 4 ? 1 : 2;
                    for (int variant = 0; variant < consumerVariants; variant++)
                    {
                        report.scenarios[index++] = new CompletionScenario
                        {
                            typed = typed != 0,
                            stage = (CompletionStage)stage,
                            stageName = ((CompletionStage)stage).ToString(),
                            consumers = variant == 0 ? 1 : 4,
                            results = new[]
                            {
                                new CompletionMetric { library = "OnityTask", bytesPerOperation = -1 },
                                new CompletionMetric { library = "UniTask", bytesPerOperation = -1 }
                            }
                        };
                    }
                }
            }

            return report;
        }

        private static CompletionReport CreateStatusReport()
        {
            CompletionReport report = CreateReport();
            report.suite = "Completion-source IsCompleted probe";
            report.onityRuntimeCommit = k_statusRuntimeCommit;
            report.scope = "Pending and completed-success GetAwaiter().IsCompleted reads on fresh "
                + "typed and untyped sources. Setup and completion stay outside each measured slice. "
                + "Samples retain harness overhead; no baseline subtraction or overall winner is inferred.";
            report.scenarios = new CompletionScenario[4];
            int index = 0;
            for (int typed = 0; typed < 2; typed++)
            {
                for (int terminal = 0; terminal < 2; terminal++)
                {
                    CompletionStage stage = terminal == 0
                        ? CompletionStage.PendingStatus : CompletionStage.TerminalStatus;
                    report.scenarios[index++] = new CompletionScenario
                    {
                        typed = typed != 0,
                        stage = stage,
                        stageName = stage.ToString(),
                        consumers = 1,
                        results = new[]
                        {
                            new CompletionMetric { library = "OnityTask", bytesPerOperation = -1 },
                            new CompletionMetric { library = "UniTask", bytesPerOperation = -1 }
                        }
                    };
                }
            }

            return report;
        }

        private static CompletionReport CreatePreserveReport()
        {
            CompletionReport report = CreateReport();
            report.suite = "OnityTask Preserve vs UniTask sharing";
            report.onityRuntimeCommit = k_preserveRuntimeCommit;
            report.scope = "Pending native WhenAny<int> sources with two unresolved completion-source inputs. "
                + "Only conversion or two late result reads are measured; input creation, pending "
                + "callback registration, source completion, and first consumption are outside each slice. "
                + "One-consumer cases compare Preserve with Preserve. Four-consumer cases compare "
                + "OnityTask.Preserve with UniTask.AsTask, which returns a .NET Task. "
                + "The pending callbacks and repeated results are checked after each batch. "
                + "Samples retain harness overhead; no baseline subtraction or overall winner is inferred.";
            report.scenarios = new CompletionScenario[4];
            int index = 0;
            for (int consumers = 1; consumers <= 4; consumers += 3)
            {
                for (int stage = 0; stage < 2; stage++)
                {
                    CompletionStage preserveStage = stage == 0
                        ? CompletionStage.PreserveConversion : CompletionStage.PreserveTwoLateReads;
                    report.scenarios[index++] = new CompletionScenario
                    {
                        typed = true,
                        stage = preserveStage,
                        stageName = preserveStage.ToString(),
                        consumers = consumers,
                        results = new[]
                        {
                            new CompletionMetric
                            {
                                library = "OnityTask.Preserve",
                                bytesPerOperation = -1
                            },
                            new CompletionMetric
                            {
                                library = consumers == 1 ? "UniTask.Preserve" : "UniTask.AsTask",
                                bytesPerOperation = -1
                            }
                        }
                    };
                }
            }

            return report;
        }

        private static CompletionReport CreateLifecycleReport()
        {
            CompletionReport report = CreateReport();
            report.suite = "Native OnityTask sharing lifecycle";
            report.onityRuntimeCommit = k_preserveRuntimeCommit;
            report.scope = "One pending native WhenAny<int> task per operation, built from two fresh "
                + "completion-source inputs. Full sharing cases include input and native source construction, "
                + "Preserve or AsTask conversion, pending callback registration, winner completion and "
                + "callback dispatch, one GetResult per observer, two late result reads, and loser completion. "
                + "The unshared native await case includes the same input and native source construction, "
                + "one pending callback, winner completion and dispatch, one GetResult, and loser completion; "
                + "it does not convert or read the result again. One observer compares Preserve with Preserve; "
                + "four observers compare OnityTask.Preserve with UniTask.AsTask (.NET Task). "
                + "Preallocated runner arrays, source verification, and cleanup are outside the measured "
                + "operation; UniTask's two-input WhenAny params array is inside. Callback wait is inside. "
                + "Profiler allocation samples cover the marked main-thread work; .NET Task callbacks may "
                + "run on another thread outside that allocation marker. Samples retain harness overhead; "
                + "no baseline subtraction or overall winner is inferred.";
            report.scenarios = new CompletionScenario[3];
            for (int index = 0; index < report.scenarios.Length; index++)
            {
                bool nativeAwait = index == 2;
                int consumers = index == 1 ? 4 : 1;
                CompletionStage stage = nativeAwait
                    ? CompletionStage.NativeAwaitLifecycle
                    : CompletionStage.PreserveFullLifecycle;
                report.scenarios[index] = new CompletionScenario
                {
                    typed = true,
                    stage = stage,
                    stageName = stage.ToString(),
                    consumers = consumers,
                    results = new[]
                    {
                        new CompletionMetric
                        {
                            library = nativeAwait ? "OnityTask native await" : "OnityTask.Preserve",
                            bytesPerOperation = -1
                        },
                        new CompletionMetric
                        {
                            library = nativeAwait ? "UniTask native await" :
                                consumers == 1 ? "UniTask.Preserve" : "UniTask.AsTask",
                            bytesPerOperation = -1
                        }
                    }
                };
            }

            return report;
        }

        private static void ValidateAllocationInput(
            CompletionReport report, CompletionReport expected)
        {
            if (report.schemaVersion != expected.schemaVersion ||
                report.suite != expected.suite ||
                report.onityRuntimeCommit != expected.onityRuntimeCommit ||
                report.uniTaskCommit != expected.uniTaskCommit ||
                report.unityVersion != expected.unityVersion ||
                report.scriptingBackend != expected.scriptingBackend ||
                report.operationsPerSample != expected.operationsPerSample ||
                report.timingBatchesPerSample != expected.timingBatchesPerSample ||
                report.timingOperationsPerSample != expected.timingOperationsPerSample ||
                report.allocationOperationsPerSample != expected.allocationOperationsPerSample ||
                report.samplesPerCase != expected.samplesPerCase ||
                report.warmupBatches != expected.warmupBatches ||
                report.scope != expected.scope)
            {
                throw new InvalidDataException(
                    "Allocation pass requires a timing report from this exact runtime and benchmark configuration.");
            }

            for (int i = 0; i < expected.scenarios.Length; i++)
            {
                CompletionScenario actualScenario = report.scenarios[i];
                CompletionScenario expectedScenario = expected.scenarios[i];
                if (actualScenario == null ||
                    actualScenario.stage != expectedScenario.stage ||
                    actualScenario.stageName != expectedScenario.stageName ||
                    actualScenario.typed != expectedScenario.typed ||
                    actualScenario.consumers != expectedScenario.consumers ||
                    actualScenario.results == null ||
                    actualScenario.results.Length != expectedScenario.results.Length)
                {
                    throw new InvalidDataException("Timing scenario definitions do not match this benchmark.");
                }

                for (int result = 0; result < expectedScenario.results.Length; result++)
                {
                    CompletionMetric actualMetric = actualScenario.results[result];
                    if (actualMetric == null ||
                        actualMetric.library != expectedScenario.results[result].library ||
                        actualMetric.sampleNanosecondsPerOperation == null ||
                        actualMetric.sampleNanosecondsPerOperation.Length != k_samples)
                    {
                        throw new InvalidDataException("Timing samples are incomplete or use another comparator.");
                    }
                }
            }
        }

        private void PrepareBatch()
        {
            if (m_preserveBenchmark)
            {
                PreparePreserveBatch();
                return;
            }

            s_callbacks = 0;
            s_completedStatusReads = -1;
            if (m_activeScenario.stage == CompletionStage.Construction)
            {
                return;
            }

            for (int i = 0; i < k_operations; i++)
            {
                CreateSource(i);
                if (m_activeScenario.stage == CompletionStage.CompletionDispatch)
                {
                    RegisterConsumers(i);
                }
                else if (m_activeScenario.stage == CompletionStage.LateRegistration ||
                    m_activeScenario.stage == CompletionStage.TerminalStatus)
                {
                    CompleteSource(i);
                }
            }
        }

        private void MeasureActiveBatch()
        {
            if (m_preserveBenchmark)
            {
                MeasurePreserveBatch();
                return;
            }

            if (m_activeScenario.stage == CompletionStage.PendingStatus ||
                m_activeScenario.stage == CompletionStage.TerminalStatus)
            {
                int completed = 0;
                for (int i = 0; i < k_operations; i++)
                {
                    bool isCompleted = m_activeScenario.typed
                        ? m_activeLibrary == 0
                            ? m_onityTyped[i].Task.GetAwaiter().IsCompleted
                            : m_uniTaskTyped[i].Task.GetAwaiter().IsCompleted
                        : m_activeLibrary == 0
                            ? m_onityUntyped[i].Task.GetAwaiter().IsCompleted
                            : m_uniTaskUntyped[i].Task.GetAwaiter().IsCompleted;
                    if (isCompleted)
                    {
                        completed++;
                    }
                }

                s_completedStatusReads = completed;
                return;
            }

            for (int i = 0; i < k_operations; i++)
            {
                switch (m_activeScenario.stage)
                {
                    case CompletionStage.Construction:
                        CreateSource(i);
                        break;
                    case CompletionStage.PendingRegistration:
                    case CompletionStage.LateRegistration:
                        RegisterConsumers(i);
                        break;
                    case CompletionStage.CompletionDispatch:
                        CompleteSource(i);
                        break;
                    case CompletionStage.AsTask:
                        m_bridges[i] = m_activeScenario.typed
                            ? m_activeLibrary == 0
                                ? m_onityTyped[i].Task.AsTask()
                                : m_uniTaskTyped[i].Task.AsTask()
                            : m_activeLibrary == 0
                                ? m_onityUntyped[i].Task.AsTask()
                                : m_uniTaskUntyped[i].Task.AsTask();
                        break;
                }
            }
        }

        private void FinishBatch()
        {
            if (m_preserveBenchmark)
            {
                FinishPreserveBatch();
                return;
            }

            CompletionStage stage = m_activeScenario.stage;
            if ((stage == CompletionStage.PendingStatus || stage == CompletionStage.TerminalStatus) &&
                s_completedStatusReads != (stage == CompletionStage.TerminalStatus ? k_operations : 0))
            {
                throw new InvalidOperationException("IsCompleted did not match the prepared source state.");
            }

            int expectedCallbacks = (stage == CompletionStage.PendingRegistration ||
                stage == CompletionStage.CompletionDispatch ||
                stage == CompletionStage.LateRegistration)
                ? k_operations * m_activeScenario.consumers : 0;

            if (stage == CompletionStage.Construction ||
                stage == CompletionStage.PendingRegistration || stage == CompletionStage.AsTask ||
                stage == CompletionStage.PendingStatus)
            {
                for (int i = 0; i < k_operations; i++)
                {
                    CompleteSource(i);
                }
            }

            if (s_callbacks != expectedCallbacks)
            {
                throw new InvalidOperationException("Completion callbacks did not match the consumer count.");
            }

            for (int i = 0; i < k_operations; i++)
            {
                if (stage == CompletionStage.AsTask && !m_bridges[i].IsCompleted)
                {
                    throw new InvalidOperationException("AsTask bridge did not complete.");
                }

                if (m_activeScenario.typed)
                {
                    int value = m_activeLibrary == 0
                        ? m_onityTyped[i].Task.GetAwaiter().GetResult()
                        : m_uniTaskTyped[i].Task.GetAwaiter().GetResult();
                    if (value != 42)
                    {
                        throw new InvalidOperationException("Typed completion returned the wrong value.");
                    }
                }
                else if (m_activeLibrary == 0)
                {
                    m_onityUntyped[i].Task.GetAwaiter().GetResult();
                }
                else
                {
                    m_uniTaskUntyped[i].Task.GetAwaiter().GetResult();
                }

                m_bridges[i] = null;
            }
        }

        private void CreateSource(int index)
        {
            if (m_activeScenario.typed)
            {
                if (m_activeLibrary == 0)
                {
                    m_onityTyped[index] = new OnityTaskCompletionSource<int>();
                }
                else
                {
                    m_uniTaskTyped[index] = new UniTaskCompletionSource<int>();
                }
            }
            else if (m_activeLibrary == 0)
            {
                m_onityUntyped[index] = new OnityTaskCompletionSource();
            }
            else
            {
                m_uniTaskUntyped[index] = new UniTaskCompletionSource();
            }
        }

        private void RegisterConsumers(int index)
        {
            for (int consumer = 0; consumer < m_activeScenario.consumers; consumer++)
            {
                if (m_activeScenario.typed)
                {
                    if (m_activeLibrary == 0)
                    {
                        m_onityTyped[index].Task.GetAwaiter().OnCompleted(s_callback);
                    }
                    else
                    {
                        m_uniTaskTyped[index].Task.GetAwaiter().OnCompleted(s_callback);
                    }
                }
                else if (m_activeLibrary == 0)
                {
                    m_onityUntyped[index].Task.GetAwaiter().OnCompleted(s_callback);
                }
                else
                {
                    m_uniTaskUntyped[index].Task.GetAwaiter().OnCompleted(s_callback);
                }
            }
        }

        private void CompleteSource(int index)
        {
            bool completed = m_activeScenario.typed
                ? m_activeLibrary == 0
                    ? m_onityTyped[index].TrySetResult(42)
                    : m_uniTaskTyped[index].TrySetResult(42)
                : m_activeLibrary == 0
                    ? m_onityUntyped[index].TrySetResult()
                    : m_uniTaskUntyped[index].TrySetResult();
            if (!completed)
            {
                throw new InvalidOperationException("A fresh source failed to complete.");
            }
        }

        private void PreparePreserveBatch()
        {
            System.Threading.Volatile.Write(ref s_preserveCallbacks, 0);
            if (m_lifecycleBenchmark)
            {
                if (!m_preserveSourceVerified)
                {
                    VerifyNativePreserveSource();
                }

                return;
            }

            for (int i = 0; i < k_operations; i++)
            {
                if (m_activeLibrary == 0)
                {
                    m_onityPreserveInputs[i] = new OnityTaskCompletionSource();
                    m_onitySecondPreserveInputs[i] = new OnityTaskCompletionSource();
                    m_onityTypedNativeTasks[i] = OnityTask.WhenAny(
                        m_onityPreserveInputs[i].Task,
                        m_onitySecondPreserveInputs[i].Task);
                    if (m_onityTypedNativeTasks[i].IsCompleted)
                    {
                        throw new InvalidOperationException("Onity WhenAny source was not pending.");
                    }
                    if (!m_preserveSourceVerified)
                    {
                        System.Reflection.FieldInfo stateField = typeof(OnityTask<int>).GetField(
                            "m_state",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic);
                        object state = stateField?.GetValue(m_onityTypedNativeTasks[i]);
                        if (state == null || state.GetType().Name != "OnityWhenAnyTaskSource")
                        {
                            throw new InvalidOperationException(
                                "Preserve benchmark requires a native OnityWhenAnyTaskSource.");
                        }

                        m_preserveSourceVerified = true;
                    }
                }
                else
                {
                    m_uniTaskPreserveInputs[i] = new UniTaskCompletionSource();
                    m_uniTaskSecondPreserveInputs[i] = new UniTaskCompletionSource();
                    m_uniTaskTypedNativeTasks[i] = UniTask.WhenAny(new[]
                    {
                        m_uniTaskPreserveInputs[i].Task,
                        m_uniTaskSecondPreserveInputs[i].Task
                    });
                    if (m_uniTaskTypedNativeTasks[i].Status != UniTaskStatus.Pending)
                    {
                        throw new InvalidOperationException("UniTask WhenAny source was not pending.");
                    }
                }
            }

            if (m_activeScenario.stage == CompletionStage.PreserveTwoLateReads)
            {
                ConvertPreserveBatch();
                RegisterPreserveCallbacks();
                CompletePreserveInputs();
                ConsumePreserveOnce();
            }
        }

        private void VerifyNativePreserveSource()
        {
            OnityTaskCompletionSource first = new OnityTaskCompletionSource();
            OnityTaskCompletionSource second = new OnityTaskCompletionSource();
            OnityTask<int> task = OnityTask.WhenAny(first.Task, second.Task);
            System.Reflection.FieldInfo stateField = typeof(OnityTask<int>).GetField(
                "m_state",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            object state = stateField?.GetValue(task);
            if (state == null || state.GetType().Name != "OnityWhenAnyTaskSource")
            {
                throw new InvalidOperationException(
                    "Lifecycle benchmark requires a native OnityWhenAnyTaskSource.");
            }

            first.TrySetResult();
            if (task.GetAwaiter().GetResult() != 0 || !second.TrySetResult())
            {
                throw new InvalidOperationException("Native source verification failed.");
            }

            m_preserveSourceVerified = true;
        }

        private void CreatePreserveSources()
        {
            for (int i = 0; i < k_operations; i++)
            {
                if (m_activeLibrary == 0)
                {
                    m_onityPreserveInputs[i] = new OnityTaskCompletionSource();
                    m_onitySecondPreserveInputs[i] = new OnityTaskCompletionSource();
                    m_onityTypedNativeTasks[i] = OnityTask.WhenAny(
                        m_onityPreserveInputs[i].Task,
                        m_onitySecondPreserveInputs[i].Task);
                }
                else
                {
                    m_uniTaskPreserveInputs[i] = new UniTaskCompletionSource();
                    m_uniTaskSecondPreserveInputs[i] = new UniTaskCompletionSource();
                    m_uniTaskTypedNativeTasks[i] = UniTask.WhenAny(new[]
                    {
                        m_uniTaskPreserveInputs[i].Task,
                        m_uniTaskSecondPreserveInputs[i].Task
                    });
                }
            }
        }

        private void MeasurePreserveBatch()
        {
            if (m_lifecycleBenchmark)
            {
                CreatePreserveSources();
                if (m_activeScenario.stage == CompletionStage.NativeAwaitLifecycle)
                {
                    RegisterNativeCallbacks();
                    CompletePreserveInputs();
                    ConsumeNativeOnce();
                }
                else
                {
                    ConvertPreserveBatch();
                    RegisterPreserveCallbacks();
                    CompletePreserveInputs();
                    ConsumePreserveAll();
                    ReadPreserveLateResults();
                }

                return;
            }

            if (m_activeScenario.stage == CompletionStage.PreserveConversion)
            {
                ConvertPreserveBatch();
                return;
            }

            ReadPreserveLateResults();
        }

        private void ConvertPreserveBatch()
        {
            for (int i = 0; i < k_operations; i++)
            {
                if (m_activeLibrary == 0)
                {
                    m_onityTypedPreservedTasks[i] = m_onityTypedNativeTasks[i].Preserve();
                }
                else if (m_activeScenario.consumers == 1)
                {
                    m_uniTaskTypedPreservedTasks[i] = m_uniTaskTypedNativeTasks[i].Preserve();
                }
                else
                {
                    m_uniTaskTypedFanoutTasks[i] = m_uniTaskTypedNativeTasks[i].AsTask();
                }
            }
        }

        private void RegisterPreserveCallbacks()
        {
            for (int i = 0; i < k_operations; i++)
            {
                for (int consumer = 0; consumer < m_activeScenario.consumers; consumer++)
                {
                    if (m_activeLibrary == 0)
                    {
                        m_onityTypedPreservedTasks[i].GetAwaiter().OnCompleted(s_preserveCallback);
                    }
                    else if (m_activeScenario.consumers == 1)
                    {
                        m_uniTaskTypedPreservedTasks[i].GetAwaiter().OnCompleted(s_preserveCallback);
                    }
                    else
                    {
                        m_uniTaskTypedFanoutTasks[i].ConfigureAwait(false)
                            .GetAwaiter().OnCompleted(s_preserveCallback);
                    }
                }
            }
        }

        private void RegisterNativeCallbacks()
        {
            for (int i = 0; i < k_operations; i++)
            {
                if (m_activeLibrary == 0)
                {
                    m_onityTypedNativeTasks[i].GetAwaiter().OnCompleted(s_preserveCallback);
                }
                else
                {
                    m_uniTaskTypedNativeTasks[i].GetAwaiter().OnCompleted(s_preserveCallback);
                }
            }
        }

        private void CompletePreserveInputs()
        {
            for (int i = 0; i < k_operations; i++)
            {
                bool completed = m_activeLibrary == 0
                    ? m_onityPreserveInputs[i].TrySetResult()
                    : m_uniTaskPreserveInputs[i].TrySetResult();
                if (!completed)
                {
                    throw new InvalidOperationException("A preserve input failed to complete.");
                }
            }

            int expectedCallbacks = k_operations * m_activeScenario.consumers;
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (System.Threading.Volatile.Read(ref s_preserveCallbacks) < expectedCallbacks)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("Pending preserve callbacks did not complete.");
                }

                System.Threading.Thread.Yield();
            }

            if (System.Threading.Volatile.Read(ref s_preserveCallbacks) != expectedCallbacks)
            {
                throw new InvalidOperationException("Preserve callback count was incorrect.");
            }
        }

        private void ConsumePreserveOnce()
        {
            for (int i = 0; i < k_operations; i++)
            {
                int result = m_activeLibrary == 0
                    ? m_onityTypedPreservedTasks[i].GetAwaiter().GetResult()
                    : m_activeScenario.consumers == 1
                        ? m_uniTaskTypedPreservedTasks[i].GetAwaiter().GetResult()
                        : m_uniTaskTypedFanoutTasks[i].GetAwaiter().GetResult();
                if (result != 0)
                {
                    throw new InvalidOperationException("WhenAny returned the wrong winner.");
                }

                bool loserCompleted = m_activeLibrary == 0
                    ? m_onitySecondPreserveInputs[i].TrySetResult()
                    : m_uniTaskSecondPreserveInputs[i].TrySetResult();
                if (!loserCompleted)
                {
                    throw new InvalidOperationException("The losing input failed to complete.");
                }
            }
        }

        private void ConsumePreserveAll()
        {
            for (int i = 0; i < k_operations; i++)
            {
                for (int consumer = 0; consumer < m_activeScenario.consumers; consumer++)
                {
                    int result = m_activeLibrary == 0
                        ? m_onityTypedPreservedTasks[i].GetAwaiter().GetResult()
                        : m_activeScenario.consumers == 1
                            ? m_uniTaskTypedPreservedTasks[i].GetAwaiter().GetResult()
                            : m_uniTaskTypedFanoutTasks[i].GetAwaiter().GetResult();
                    if (result != 0)
                    {
                        throw new InvalidOperationException("Shared task returned the wrong winner.");
                    }
                }

                bool loserCompleted = m_activeLibrary == 0
                    ? m_onitySecondPreserveInputs[i].TrySetResult()
                    : m_uniTaskSecondPreserveInputs[i].TrySetResult();
                if (!loserCompleted)
                {
                    throw new InvalidOperationException("The losing input failed to complete.");
                }
            }
        }

        private void ConsumeNativeOnce()
        {
            for (int i = 0; i < k_operations; i++)
            {
                int result = m_activeLibrary == 0
                    ? m_onityTypedNativeTasks[i].GetAwaiter().GetResult()
                    : m_uniTaskTypedNativeTasks[i].GetAwaiter().GetResult();
                if (result != 0)
                {
                    throw new InvalidOperationException("Native task returned the wrong winner.");
                }

                bool loserCompleted = m_activeLibrary == 0
                    ? m_onitySecondPreserveInputs[i].TrySetResult()
                    : m_uniTaskSecondPreserveInputs[i].TrySetResult();
                if (!loserCompleted)
                {
                    throw new InvalidOperationException("The losing input failed to complete.");
                }
            }
        }

        private void ReadPreserveLateResults()
        {
            int sum = 0;
            for (int i = 0; i < k_operations; i++)
            {
                for (int read = 0; read < 2; read++)
                {
                    int result = m_activeLibrary == 0
                        ? m_onityTypedPreservedTasks[i].GetAwaiter().GetResult()
                        : m_activeScenario.consumers == 1
                            ? m_uniTaskTypedPreservedTasks[i].GetAwaiter().GetResult()
                            : m_uniTaskTypedFanoutTasks[i].GetAwaiter().GetResult();
                    if (result != 0)
                    {
                        throw new InvalidOperationException("A late preserve read changed the winner.");
                    }

                    sum += result;
                }
            }

            s_preserveResultSink = sum;
        }

        private void FinishPreserveBatch()
        {
            if (m_activeScenario.stage == CompletionStage.PreserveConversion)
            {
                RegisterPreserveCallbacks();
                CompletePreserveInputs();
                ConsumePreserveOnce();
                ReadPreserveLateResults();
            }

            for (int i = 0; i < k_operations; i++)
            {
                m_onityPreserveInputs[i] = null;
                m_onitySecondPreserveInputs[i] = null;
                m_uniTaskPreserveInputs[i] = null;
                m_uniTaskSecondPreserveInputs[i] = null;
                m_onityTypedNativeTasks[i] = default;
                m_uniTaskTypedNativeTasks[i] = default;
                m_onityTypedPreservedTasks[i] = default;
                m_uniTaskTypedPreservedTasks[i] = default;
                m_uniTaskTypedFanoutTasks[i] = null;
            }
        }

        private void RunTiming(CompletionReport report)
        {
            Action operation = MeasureActiveBatch;
            Action emptyOperation = EmptyOperation;
            for (int warmup = 0; warmup < k_warmupBatches; warmup++)
            {
                for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
                {
                    for (int library = 0; library < 2; library++)
                    {
                        m_activeScenario = report.scenarios[scenario];
                        m_activeLibrary = library;
                        PrepareBatch();
                        operation();
                        FinishBatch();
                    }
                }
            }

            for (int warmup = 0; warmup < k_warmupBatches * k_timingBatchesPerSample; warmup++)
            {
                for (int i = 0; i < k_operations; i++)
                {
                    emptyOperation();
                }
            }

            Stopwatch.GetTimestamp();
            double baseline = 0;
            report.emptyHarnessSampleNanosecondsPerOperation = new double[k_samples];
            for (int sample = 0; sample < k_samples; sample++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int batch = 0; batch < k_timingBatchesPerSample; batch++)
                {
                    for (int operationIndex = 0; operationIndex < k_operations; operationIndex++)
                    {
                        emptyOperation();
                    }
                }

                double elapsed = TicksToNanoseconds(Stopwatch.GetTimestamp() - start)
                    / (k_operations * k_timingBatchesPerSample);
                report.emptyHarnessSampleNanosecondsPerOperation[sample] = elapsed;
                baseline += elapsed;
            }

            report.emptyHarnessNanosecondsPerOperation = baseline / k_samples;
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int sample = 0; sample < k_samples; sample++)
                {
                    ForceFullGc();
                    long[] ticks = new long[2];
                    for (int batch = 0; batch < k_timingBatchesPerSample; batch++)
                    {
                        for (int turn = 0; turn < 2; turn++)
                        {
                            m_activeLibrary = (sample + batch + turn) & 1;
                            PrepareBatch();
                            long start = Stopwatch.GetTimestamp();
                            operation();
                            ticks[m_activeLibrary] += Stopwatch.GetTimestamp() - start;
                            FinishBatch();
                        }
                    }

                    for (int library = 0; library < 2; library++)
                    {
                        CompletionMetric metric = m_activeScenario.results[library];
                        if (metric.sampleNanosecondsPerOperation == null)
                        {
                            metric.sampleNanosecondsPerOperation = new double[k_samples];
                        }

                        metric.sampleNanosecondsPerOperation[sample] =
                            TicksToNanoseconds(ticks[library])
                            / (k_operations * k_timingBatchesPerSample);
                    }
                }

                for (int library = 0; library < 2; library++)
                {
                    CompletionMetric metric = m_activeScenario.results[library];
                    double total = 0;
                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        total += metric.sampleNanosecondsPerOperation[sample];
                    }

                    metric.meanNanosecondsPerOperation = total / k_samples;
                }
            }
        }

        private static double TicksToNanoseconds(long ticks)
        {
            return ticks * (1000000000d / Stopwatch.Frequency);
        }

#if UNITY_EDITOR
        private IEnumerator RunAttribution(CompletionAttributionReport result)
        {
            ProfilerDriver.profileEditor = false;
            Profiler.enableAllocationCallstacks = true;
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;

            int startup = k_profilerStartupFrames;
            while (ProfilerDriver.lastFrameIndex < 2 && startup-- > 0)
            {
                yield return null;
            }

            yield return CaptureAllocation(AllocatePositiveControl);
            result.positiveControlBytes = m_lastAllocationBytes;
            if (!m_lastAllocationValid || result.positiveControlBytes < 65536)
            {
                result.reason = "Positive allocation control failed.";
                yield break;
            }

            CompletionAllocationStack[] positiveStacks = ReadAllocationCallstacks(
                m_lastCapturedFrame, m_lastMarkerName, out long positiveStackBytes);
            if (positiveStackBytes != result.positiveControlBytes || positiveStacks.Length == 0)
            {
                result.reason = "Positive allocation control or its callstack failed.";
                yield break;
            }

            yield return CaptureAllocation(EmptyOperation);
            result.emptyControlBytes = m_lastAllocationBytes;
            if (!m_lastAllocationValid || result.emptyControlBytes != 0)
            {
                result.reason = "Empty allocation control failed.";
                yield break;
            }

            int index = 0;
            for (int stageIndex = 0; stageIndex < 2; stageIndex++)
            {
                CompletionStage stage = m_preserveBenchmark
                    ? CompletionStage.PreserveConversion
                    : stageIndex == 0 ? CompletionStage.Construction : CompletionStage.AsTask;
                int consumers = m_preserveBenchmark && stageIndex != 0 ? 4 : 1;
                for (int library = 0; library < 2; library++)
                {
                    m_activeScenario = new CompletionScenario
                    {
                        stage = stage,
                        typed = true,
                        consumers = consumers
                    };
                    m_activeLibrary = library;
                    for (int warmup = 0; warmup < k_warmupBatches; warmup++)
                    {
                        PrepareBatch();
                        MeasureActiveBatch();
                        FinishBatch();
                    }

                    ForceFullGc();
                    PrepareBatch();
                    yield return CaptureAllocation(MeasureActiveBatch);
                    long sampleBytes = m_lastAllocationBytes;
                    bool captured = m_lastAllocationValid;
                    CompletionAllocationStack[] stacks = captured
                        ? ReadAllocationCallstacks(m_lastCapturedFrame,
                            m_lastMarkerName, out _)
                        : new CompletionAllocationStack[0];
                    FinishBatch();
                    if (!captured)
                    {
                        result.reason = stage + " allocation sample was not captured.";
                        yield break;
                    }

                    long attributedBytes = 0;
                    for (int stack = 0; stack < stacks.Length; stack++)
                    {
                        attributedBytes += stacks[stack].bytes;
                    }

                    result.cases[index++] = new CompletionAttributionCase
                    {
                        stage = stage.ToString(),
                        consumers = consumers,
                        library = m_preserveBenchmark
                            ? library == 0 ? "OnityTask.Preserve"
                                : consumers == 1 ? "UniTask.Preserve" : "UniTask.AsTask"
                            : library == 0 ? "OnityTask" : "UniTask",
                        sampleBytes = sampleBytes,
                        bytesPerOperation = (double)sampleBytes / k_operations,
                        attributedBytes = attributedBytes,
                        callstacks = stacks
                    };
                    if (attributedBytes != sampleBytes || stacks.Length == 0)
                    {
                        result.reason = stage + " " + result.cases[index - 1].library
                            + " callstack bytes did not match the calibrated sample.";
                        yield break;
                    }
                }
            }

            result.available = true;
            result.reason = "Calibrated GC.Alloc samples and fully attributed callstacks captured.";
        }

        private static CompletionAllocationStack[] ReadAllocationCallstacks(
            int frame, string marker, out long totalBytes)
        {
            totalBytes = 0;
            Dictionary<string, CompletionAllocationStack> groups =
                new Dictionary<string, CompletionAllocationStack>();
            List<ulong> addresses = new List<ulong>(32);
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
                            if (data.GetSampleMarkerId(child) != allocationId)
                            {
                                continue;
                            }

                            addresses.Clear();
                            data.GetSampleCallstack(child, addresses);
                            StringBuilder stack = new StringBuilder();
                            for (int address = 0; address < addresses.Count; address++)
                            {
                                FrameDataView.MethodInfo method =
                                    data.ResolveMethodInfo(addresses[address]);
                                if (string.IsNullOrEmpty(method.methodName))
                                {
                                    continue;
                                }

                                if (stack.Length > 0)
                                {
                                    stack.Append(" <- ");
                                }

                                stack.Append(method.methodName);
                            }

                            if (stack.Length == 0)
                            {
                                continue;
                            }

                            long bytes = data.GetSampleMetadataAsLong(child, 0);
                            string key = stack.ToString();
                            if (!groups.TryGetValue(key, out CompletionAllocationStack group))
                            {
                                group = new CompletionAllocationStack { stack = key };
                                groups.Add(key, group);
                            }

                            group.bytes += bytes;
                            group.allocations++;
                            totalBytes += bytes;
                        }

                        List<CompletionAllocationStack> sorted =
                            new List<CompletionAllocationStack>(groups.Values);
                        sorted.Sort((left, right) => right.bytes.CompareTo(left.bytes));
                        return sorted.ToArray();
                    }
                }
            }

            return new CompletionAllocationStack[0];
        }

        private IEnumerator RunAllocations(CompletionReport report)
        {
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;
            int startup = k_profilerStartupFrames;
            while (ProfilerDriver.lastFrameIndex < 2 && startup-- > 0)
            {
                yield return null;
            }

            yield return CaptureAllocation(AllocatePositiveControl);
            report.positiveControlBytes = m_lastAllocationBytes;
            if (!m_lastAllocationValid || report.positiveControlBytes < 65536)
            {
                report.allocationCounter = "Unavailable: Profiler positive control failed.";
                yield break;
            }

            yield return CaptureAllocation(EmptyOperation);
            report.emptyControlBytes = m_lastAllocationBytes;
            if (!m_lastAllocationValid || report.emptyControlBytes != 0)
            {
                report.allocationCounter = "Unavailable: Profiler empty control failed.";
                yield break;
            }

            Action operation = MeasureActiveBatch;
            Action baseline = MeasureEmptyBatch;
            long baselineTotal = 0;
            report.emptyHarnessSampleAllocatedBytes = new long[k_samples];
            for (int sample = 0; sample < k_samples; sample++)
            {
                yield return CaptureAllocation(baseline);
                if (!m_lastAllocationValid)
                {
                    report.allocationCounter = "Unavailable: harness baseline was not captured.";
                    yield break;
                }

                baselineTotal += m_lastAllocationBytes;
                report.emptyHarnessSampleAllocatedBytes[sample] = m_lastAllocationBytes;
            }

            report.emptyHarnessAllocatedBytes = baselineTotal / k_samples;
            for (int warmup = 0; warmup < k_warmupBatches; warmup++)
            {
                for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
                {
                    for (int library = 0; library < 2; library++)
                    {
                        m_activeScenario = report.scenarios[scenario];
                        m_activeLibrary = library;
                        PrepareBatch();
                        operation();
                        FinishBatch();
                    }
                }
            }

            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                m_activeScenario = report.scenarios[scenario];
                for (int sample = 0; sample < k_samples; sample++)
                {
                    ForceFullGc();
                    for (int turn = 0; turn < 2; turn++)
                    {
                        m_activeLibrary = (sample + turn) & 1;
                        PrepareBatch();
                        yield return CaptureAllocation(operation);
                        if (!m_lastAllocationValid)
                        {
                            report.allocationCounter = "Unavailable: scenario " + scenario
                                + " sample " + sample + " was not captured.";
                            yield break;
                        }

                        FinishBatch();
                        CompletionMetric metric = m_activeScenario.results[m_activeLibrary];
                        if (metric.sampleAllocatedBytes == null)
                        {
                            metric.sampleAllocatedBytes = new long[k_samples];
                        }

                        metric.sampleAllocatedBytes[sample] = m_lastAllocationBytes;
                    }
                }

                for (int library = 0; library < 2; library++)
                {
                    CompletionMetric metric = m_activeScenario.results[library];
                    long total = 0;
                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        total += metric.sampleAllocatedBytes[sample];
                    }

                    metric.bytesPerOperation = (double)total / (k_samples * k_operations);
                }
            }

            report.allocationsAvailable = true;
            report.allocationCounter = "Unity Profiler GC.Alloc metadata; 64 KiB and empty controls passed.";
            report.allocationGeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        private IEnumerator CaptureAllocation(Action operation)
        {
            m_lastAllocationValid = false;
            m_lastAllocationBytes = 0;
            string marker = k_scopePrefix
                + (m_markerSequence++).ToString(CultureInfo.InvariantCulture);
            m_lastMarkerName = marker;
            Profiler.BeginSample(marker);
            try
            {
                operation();
            }
            finally
            {
                Profiler.EndSample();
            }

            for (int wait = 0; wait < k_profilerWaitFrames; wait++)
            {
                yield return null;
                if (TryReadAllocation(
                    Math.Max(m_lastCapturedFrame, ProfilerDriver.firstFrameIndex),
                    ProfilerDriver.lastFrameIndex, marker,
                    out long bytes, out int foundFrame))
                {
                    m_lastAllocationBytes = bytes;
                    m_lastCapturedFrame = foundFrame;
                    m_lastAllocationValid = true;
                    yield break;
                }
            }
        }

        private static bool TryReadAllocation(int firstFrame, int lastFrame,
            string marker, out long bytes, out int foundFrame)
        {
            bytes = 0;
            foundFrame = -1;
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
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void RestoreProfilerState()
        {
            if (!m_profilerStateCaptured)
            {
                return;
            }

            m_profilerStateCaptured = false;
            Profiler.enabled = m_profilerWasEnabled;
            ProfilerDriver.enabled = m_profilerDriverWasEnabled;
            ProfilerDriver.profileEditor = m_profileEditorWasEnabled;
            Profiler.enableAllocationCallstacks = m_allocationCallstacksWereEnabled;
        }
#endif

        private static void ClearAllocations(CompletionReport report)
        {
            report.allocationsAvailable = false;
            report.allocationCounter = "Unavailable: separate Unity Profiler pass has not passed calibration.";
            report.allocationGeneratedAtUtc = null;
            report.positiveControlBytes = 0;
            report.emptyControlBytes = 0;
            report.emptyHarnessAllocatedBytes = 0;
            report.emptyHarnessSampleAllocatedBytes = null;
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                for (int library = 0; library < 2; library++)
                {
                    report.scenarios[scenario].results[library].bytesPerOperation = -1;
                    report.scenarios[scenario].results[library].sampleAllocatedBytes = null;
                }
            }
        }

        private static void MeasureEmptyBatch()
        {
            for (int i = 0; i < k_operations; i++)
            {
                EmptyOperation();
            }
        }

        private static void AllocatePositiveControl()
        {
            byte[] bytes = new byte[65536];
            GC.KeepAlive(bytes);
        }

        private void SaveReport(CompletionReport report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_outputPath));
            File.WriteAllText(m_outputPath, JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.ChangeExtension(m_outputPath, ".csv"), BuildCsv(report));
            File.WriteAllText(Path.ChangeExtension(m_outputPath, ".md"), BuildMarkdown(report));
        }

        private static string BuildCsv(CompletionReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("stage,typed,consumers,library,timing_operations_per_sample,"
                + "allocation_operations_per_sample,mean_ns_per_op,"
                + "bytes_per_op,allocations_available");
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                CompletionScenario item = report.scenarios[scenario];
                for (int library = 0; library < 2; library++)
                {
                    CompletionMetric metric = item.results[library];
                    builder.Append(item.stageName).Append(',');
                    builder.Append(item.typed ? "true" : "false").Append(',');
                    builder.Append(item.consumers).Append(',');
                    builder.Append(metric.library).Append(',');
                    builder.Append(report.timingOperationsPerSample).Append(',');
                    builder.Append(report.allocationOperationsPerSample).Append(',');
                    builder.Append(Number(metric.meanNanosecondsPerOperation)).Append(',');
                    builder.Append(Number(metric.bytesPerOperation)).Append(',');
                    builder.AppendLine(report.allocationsAvailable ? "true" : "false");
                }
            }

            return builder.ToString();
        }

        private static string BuildMarkdown(CompletionReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# " + report.suite);
            builder.AppendLine();
            builder.AppendLine("- Unity: " + report.unityVersion + "; " + report.scriptingBackend);
            builder.AppendLine("- UniTask commit: " + report.uniTaskCommit);
            builder.AppendLine("- Onity runtime commit: " + report.onityRuntimeCommit);
            builder.AppendLine("- Timing operations/sample: " + report.timingOperationsPerSample
                + "; allocation operations/sample: " + report.allocationOperationsPerSample
                + "; samples: " + report.samplesPerCase);
            builder.AppendLine("- Allocation counter: " + report.allocationCounter);
            builder.AppendLine("- Empty harness: "
                + Number(report.emptyHarnessNanosecondsPerOperation) + " ns/op; "
                + Number((double)report.emptyHarnessAllocatedBytes / k_operations) + " B/op");
            builder.AppendLine();
            builder.AppendLine(report.scope);
            builder.AppendLine();
            builder.AppendLine("| Stage | Type | Consumers | Library | ns/op | B/op |");
            builder.AppendLine("| --- | --- | ---: | --- | ---: | ---: |");
            for (int scenario = 0; scenario < report.scenarios.Length; scenario++)
            {
                CompletionScenario item = report.scenarios[scenario];
                for (int library = 0; library < 2; library++)
                {
                    CompletionMetric metric = item.results[library];
                    builder.Append("| ").Append(item.stageName).Append(" | ")
                        .Append(item.typed ? "typed" : "untyped").Append(" | ")
                        .Append(item.consumers).Append(" | ").Append(metric.library)
                        .Append(" | ").Append(Number(metric.meanNanosecondsPerOperation))
                        .Append(" | ").Append(Number(metric.bytesPerOperation))
                        .AppendLine(" |");
                }
            }

            return builder.ToString();
        }

        private static string Number(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static void CountCallback()
        {
            s_callbacks++;
        }

        private static void CountPreserveCallback()
        {
            System.Threading.Interlocked.Increment(ref s_preserveCallbacks);
        }

        private static void EmptyOperation()
        {
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private enum CompletionStage
        {
            Construction,
            PendingRegistration,
            CompletionDispatch,
            LateRegistration,
            AsTask,
            PendingStatus,
            TerminalStatus,
            PreserveConversion,
            PreserveTwoLateReads,
            PreserveFullLifecycle,
            NativeAwaitLifecycle
        }

        [Serializable]
        private sealed class CompletionAttributionReport
        {
            public string suite;
            public string onityRuntimeCommit;
            public string uniTaskCommit;
            public string unityVersion;
            public int operations;
            public bool available;
            public string reason;
            public long positiveControlBytes;
            public long emptyControlBytes;
            public CompletionAttributionCase[] cases;
        }

        [Serializable]
        private sealed class CompletionAttributionCase
        {
            public string stage;
            public int consumers;
            public string library;
            public long sampleBytes;
            public double bytesPerOperation;
            public long attributedBytes;
            public CompletionAllocationStack[] callstacks;
        }

        [Serializable]
        private sealed class CompletionAllocationStack
        {
            public string stack;
            public long bytes;
            public int allocations;
        }

        [Serializable]
        private sealed class CompletionReport
        {
            public int schemaVersion;
            public string suite;
            public string generatedAtUtc;
            public string allocationGeneratedAtUtc;
            public string unityVersion;
            public string scriptingBackend;
            public string uniTaskCommit;
            public string onityRuntimeCommit;
            public int operationsPerSample;
            public int timingBatchesPerSample;
            public int timingOperationsPerSample;
            public int allocationOperationsPerSample;
            public int samplesPerCase;
            public int warmupBatches;
            public string scope;
            public double emptyHarnessNanosecondsPerOperation;
            public double[] emptyHarnessSampleNanosecondsPerOperation;
            public long emptyHarnessAllocatedBytes;
            public long[] emptyHarnessSampleAllocatedBytes;
            public bool allocationsAvailable;
            public string allocationCounter;
            public long positiveControlBytes;
            public long emptyControlBytes;
            public CompletionScenario[] scenarios;
        }

        [Serializable]
        private sealed class CompletionScenario
        {
            public CompletionStage stage;
            public string stageName;
            public bool typed;
            public int consumers;
            public CompletionMetric[] results;
        }

        [Serializable]
        private sealed class CompletionMetric
        {
            public string library;
            public double meanNanosecondsPerOperation;
            public double bytesPerOperation;
            public double[] sampleNanosecondsPerOperation;
            public long[] sampleAllocatedBytes;
        }
    }
}
