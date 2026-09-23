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
        private const string k_builderRuntimeCommit = "8292166d73ea5525ed2cd5ead38ba0126e08b1e2";
        private const string k_builderOnityAsyncBlob = "85f4e32ff23c9c7c0debae2b139e9ff9e6ad12ae";
        private const string k_pinnedUniTaskCommit = "2e993ff18f28c931602a07292df0b0804eebef99";
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
        private bool m_builderAttribution;
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
        private bool m_allocationCallstacksWereEnabled;
        private string m_lastProfiledMarkerName;
#endif

        /// <summary>
        /// Queues a benchmark run. The optional callback receives a report path or failure.
        /// </summary>
        /// <param name="latestJson">Output JSON path.</param>
        /// <param name="completed">Optional completion callback.</param>
        /// <param name="allocationOnly">Reads an existing timing report and adds profiled allocation samples.</param>
        /// <param name="builderAttribution">Captures warm async method scheduling allocation callstacks.</param>
        public static void Run(string latestJson, Action<string, Exception> completed = null,
            bool allocationOnly = false, bool builderAttribution = false)
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
            runner.m_builderAttribution = builderAttribution;
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
            if (!m_allocationOnly && !m_builderAttribution)
            {
                Profiler.enabled = false;
                ProfilerDriver.enabled = false;
            }
#endif
            Exception failure = null;
            TaskBenchmarkReport report = null;
            BuilderAttributionReport attributionReport = null;
            try
            {
                if (m_builderAttribution)
                {
                    attributionReport = CreateBuilderAttributionReport();
                }
                else if (m_allocationOnly)
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

            if (failure == null && !m_allocationOnly && !m_builderAttribution)
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

            if (failure == null && m_allocationOnly && !m_builderAttribution)
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

            if (failure == null && m_builderAttribution)
            {
#if UNITY_EDITOR
                IEnumerator attribution = RunBuilderAttribution(attributionReport);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = attribution.MoveNext();
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

                    yield return attribution.Current;
                }
#else
                failure = new NotSupportedException("Builder allocation attribution requires the Unity Editor.");
#endif
            }

            if (failure == null)
            {
                try
                {
                    if (m_builderAttribution)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(m_latestJson));
                        File.WriteAllText(m_latestJson,
                            JsonUtility.ToJson(attributionReport, true), Encoding.UTF8);
                    }
                    else
                    {
                        SaveReport(report, m_latestJson);
                    }
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

        private static BuilderAttributionReport CreateBuilderAttributionReport()
        {
            return new BuilderAttributionReport
            {
                schemaVersion = 1,
                suite = "Warm async method scheduling allocation callstacks",
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                onityRuntimeCommit = k_builderRuntimeCommit,
                onityAsyncGitBlob = k_builderOnityAsyncBlob,
                uniTaskCommit = k_pinnedUniTaskCommit,
                unityVersion = Application.unityVersion,
                scriptingBackend = GetScriptingBackendLabel(),
                operations = k_steadyConcurrency,
                warmupBatches = 2,
                measurementScope = "One synchronous main-thread scheduling batch of 128 warm operations. "
                    + "All native and suspended awaiters are checked pending after the marker, before a frame yield. "
                    + "Preparation, full GC, frame waiting, continuation dispatch, completion, "
                    + "and result consumption are outside the Profiler marker. Callstack capture "
                    + "is diagnostic; the separate eight-sample benchmark supplies numerical results.",
                cases = new BuilderAttributionCase[10]
            };
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
        private IEnumerator RunBuilderAttribution(BuilderAttributionReport report)
        {
            m_lastProfiledFoundFrame = -1;
            ProfilerDriver.profileEditor = false;
            Profiler.enableAllocationCallstacks = true;
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;

            int startup = k_profilerWarmupFrames;
            while (ProfilerDriver.lastFrameIndex < 2 && startup-- > 0)
            {
                yield return null;
            }

            yield return CaptureProfiledAllocation(AllocatePositiveControl, 1);
            report.positiveControlBytes = m_lastProfiledAllocationBytes;
            if (!m_lastProfiledAllocationValid || report.positiveControlBytes < 65536)
            {
                throw new InvalidDataException("Builder attribution positive allocation control failed.");
            }

            BuilderAllocationStack[] positiveStacks = ReadBuilderAllocationCallstacks(
                m_lastProfiledFoundFrame, m_lastProfiledMarkerName, out long positiveStackBytes);
            if (positiveStacks.Length == 0 || positiveStackBytes != report.positiveControlBytes)
            {
                throw new InvalidDataException("Builder attribution positive control callstack was incomplete.");
            }

            yield return CaptureProfiledAllocation(EmptyOperation, 1);
            report.emptyControlBytes = m_lastProfiledAllocationBytes;
            if (!m_lastProfiledAllocationValid || report.emptyControlBytes != 0)
            {
                throw new InvalidDataException("Builder attribution empty allocation control failed.");
            }

            int caseIndex = 0;
            for (int stageIndex = 0; stageIndex < 5; stageIndex++)
            {
                BuilderAttributionStage stage = (BuilderAttributionStage)stageIndex;
                for (int library = 0; library < 2; library++)
                {
                    for (int warmup = 0; warmup < report.warmupBatches; warmup++)
                    {
                        RunBuilderAttributionOperation(stage, library);
                        AssertBuilderAttributionPending(stage, library);
                        int remaining = k_completionTimeoutFrames;
                        while (!AreBuilderAttributionOperationsCompleted(stage, library))
                        {
                            if (--remaining == 0)
                            {
                                throw new TimeoutException(stage + " attribution warmup did not complete.");
                            }

                            yield return null;
                        }

                        ConsumeBuilderAttributionOperations(stage, library);
                    }

                    ForceFullGc();
                    Action operation = () => RunBuilderAttributionOperation(stage, library);
                    Action checkPending = () => AssertBuilderAttributionPending(stage, library);
                    yield return CaptureProfiledAllocation(operation, 1, checkPending);
                    if (!m_lastProfiledAllocationValid)
                    {
                        throw new InvalidDataException(stage + " allocation marker was not captured.");
                    }

                    long sampleBytes = m_lastProfiledAllocationBytes;
                    BuilderAllocationStack[] stacks = ReadBuilderAllocationCallstacks(
                        m_lastProfiledFoundFrame, m_lastProfiledMarkerName, out long attributedBytes);
                    int remainingFrames = k_completionTimeoutFrames;
                    while (!AreBuilderAttributionOperationsCompleted(stage, library))
                    {
                        if (--remainingFrames == 0)
                        {
                            throw new TimeoutException(stage + " attribution sample did not complete.");
                        }

                        yield return null;
                    }

                    ConsumeBuilderAttributionOperations(stage, library);
                    if (attributedBytes != sampleBytes || (sampleBytes != 0 && stacks.Length == 0))
                    {
                        throw new InvalidDataException(stage + " callstack bytes do not match the allocation marker.");
                    }

                    report.cases[caseIndex++] = new BuilderAttributionCase
                    {
                        stage = stage.ToString(),
                        library = library == 0 ? "OnityTask" : "UniTask",
                        sampleBytes = sampleBytes,
                        bytesPerOperation = (double)sampleBytes / k_steadyConcurrency,
                        attributedBytes = attributedBytes,
                        callstacks = stacks
                    };
                }
            }

            report.available = true;
            report.reason = "Calibrated GC.Alloc samples and complete scheduling callstacks captured.";
        }

        private void RunBuilderAttributionOperation(BuilderAttributionStage stage, int library)
        {
            switch (stage)
            {
                case BuilderAttributionStage.NativeFrameSchedule:
                    Schedule(library, k_steadyConcurrency);
                    return;
                case BuilderAttributionStage.CompletedAsync:
                    for (int i = 0; i < k_steadyConcurrency; i++)
                    {
                        if (library == 0)
                        {
                            MeasureOnityAsyncCompleted();
                        }
                        else
                        {
                            MeasureUniTaskAsyncCompleted();
                        }
                    }
                    return;
                case BuilderAttributionStage.CompletedTypedAsync:
                    for (int i = 0; i < k_steadyConcurrency; i++)
                    {
                        if (library == 0)
                        {
                            MeasureOnityAsyncResult();
                        }
                        else
                        {
                            MeasureUniTaskAsyncResult();
                        }
                    }
                    return;
                case BuilderAttributionStage.SuspendedAsync:
                    ScheduleAsyncMethods(library, k_steadyConcurrency, false);
                    return;
                case BuilderAttributionStage.SuspendedTypedAsync:
                    ScheduleAsyncMethods(library, k_steadyConcurrency, true);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        private bool AreBuilderAttributionOperationsCompleted(BuilderAttributionStage stage, int library)
        {
            if (stage == BuilderAttributionStage.NativeFrameSchedule)
            {
                return IsCompleted(library, k_steadyConcurrency);
            }

            if (stage == BuilderAttributionStage.SuspendedAsync
                || stage == BuilderAttributionStage.SuspendedTypedAsync)
            {
                return AreAsyncMethodsCompleted(library, k_steadyConcurrency,
                    stage == BuilderAttributionStage.SuspendedTypedAsync);
            }

            return true;
        }

        private void AssertBuilderAttributionPending(BuilderAttributionStage stage, int library)
        {
            if (stage == BuilderAttributionStage.CompletedAsync
                || stage == BuilderAttributionStage.CompletedTypedAsync)
            {
                return;
            }

            bool typed = stage == BuilderAttributionStage.SuspendedTypedAsync;
            for (int i = 0; i < k_steadyConcurrency; i++)
            {
                bool completed = typed
                    ? library == 0 ? m_onityTypedAwaiters[i].IsCompleted
                        : m_uniTaskTypedAwaiters[i].IsCompleted
                    : library == 0 ? m_onityAwaiters[i].IsCompleted
                        : m_uniTaskAwaiters[i].IsCompleted;
                if (completed)
                {
                    throw new InvalidOperationException(stage + " completed before the frame yield.");
                }
            }
        }

        private void ConsumeBuilderAttributionOperations(BuilderAttributionStage stage, int library)
        {
            if (stage == BuilderAttributionStage.NativeFrameSchedule)
            {
                Consume(library, k_steadyConcurrency);
            }
            else if (stage == BuilderAttributionStage.SuspendedAsync
                || stage == BuilderAttributionStage.SuspendedTypedAsync)
            {
                ConsumeAsyncMethods(library, k_steadyConcurrency,
                    stage == BuilderAttributionStage.SuspendedTypedAsync);
            }
        }

        private static BuilderAllocationStack[] ReadBuilderAllocationCallstacks(
            int frame, string marker, out long totalBytes)
        {
            totalBytes = 0;
            Dictionary<string, BuilderAllocationStack> groups =
                new Dictionary<string, BuilderAllocationStack>();
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
                            if (!groups.TryGetValue(key, out BuilderAllocationStack group))
                            {
                                group = new BuilderAllocationStack { stack = key };
                                groups.Add(key, group);
                            }

                            group.bytes += bytes;
                            group.allocations++;
                            totalBytes += bytes;
                        }

                        List<BuilderAllocationStack> sorted =
                            new List<BuilderAllocationStack>(groups.Values);
                        sorted.Sort((left, right) => right.bytes.CompareTo(left.bytes));
                        return sorted.ToArray();
                    }
                }
            }

            return new BuilderAllocationStack[0];
        }

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
            Profiler.enableAllocationCallstacks = m_allocationCallstacksWereEnabled;
        }

        private IEnumerator CaptureProfiledAllocation(Action operation, int iterations,
            Action afterOperation = null)
        {
            m_lastProfiledAllocationValid = false;
            m_lastProfiledAllocationBytes = 0;
            int firstFrame = ProfilerDriver.lastFrameIndex;
            m_lastProfiledFirstFrame = firstFrame;
            int sampleId = m_profilerSampleId++;
            string markerName = k_allocationScope + "." + sampleId.ToString(CultureInfo.InvariantCulture);
            m_lastProfiledMarkerName = markerName;
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

            afterOperation?.Invoke();

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
#endif

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

        private enum BuilderAttributionStage
        {
            NativeFrameSchedule,
            CompletedAsync,
            CompletedTypedAsync,
            SuspendedAsync,
            SuspendedTypedAsync
        }

        [Serializable]
        private sealed class BuilderAttributionReport
        {
            public int schemaVersion;
            public string suite;
            public string generatedAtUtc;
            public string onityRuntimeCommit;
            public string onityAsyncGitBlob;
            public string uniTaskCommit;
            public string unityVersion;
            public string scriptingBackend;
            public int operations;
            public int warmupBatches;
            public string measurementScope;
            public bool available;
            public string reason;
            public long positiveControlBytes;
            public long emptyControlBytes;
            public BuilderAttributionCase[] cases;
        }

        [Serializable]
        private sealed class BuilderAttributionCase
        {
            public string stage;
            public string library;
            public long sampleBytes;
            public double bytesPerOperation;
            public long attributedBytes;
            public BuilderAllocationStack[] callstacks;
        }

        [Serializable]
        private sealed class BuilderAllocationStack
        {
            public string stack;
            public long bytes;
            public int allocations;
        }
    }
}
