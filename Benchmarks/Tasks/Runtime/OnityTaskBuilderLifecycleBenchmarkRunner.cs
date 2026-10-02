using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>Measures controlled synchronous elapsed builder phases with sequential manual suspensions.</summary>
    public sealed class OnityTaskBuilderLifecycleBenchmarkRunner : MonoBehaviour
    {
        private const int k_capacity = 4096;
        private const int k_gateStride = 4;
        private const int k_warmups = 2;
        private const int k_samples = 8;
        private const int k_largeWarmups = 3;
        private const int k_largeSamples = 12;
        private const int k_selfTestWarmups = 1;
        private const int k_selfTestSamples = 2;
        private const int k_defaultRunnerPoolCapacity = 128;
        private readonly Gate[] m_gates = new Gate[k_capacity * k_gateStride];
        private readonly OnityTask[] m_onity = new OnityTask[k_capacity];
        private readonly OnityTask<int>[] m_onityTyped = new OnityTask<int>[k_capacity];
        private readonly UniTask[] m_uni = new UniTask[k_capacity];
        private readonly UniTask<int>[] m_uniTyped = new UniTask<int>[k_capacity];
        private readonly bool[] m_attempted = new bool[k_capacity];
        private readonly int[] m_results = new int[k_capacity];
        private readonly int[] m_controlValues = new int[k_capacity];
        private Action m_schedule;
        private Action m_consume;
        private Action m_completeStep;
        private Action m_drain;
        private Action m_uniDrain;
        private Action m_returnPass;
        private Action m_empty;
        private Action m_controlSchedule;
        private Action m_controlComplete;
        private Action m_controlConsume;
        private OnityTaskBenchmarkInternals.DeferredReturnQueue m_onityQueue;
        private FieldInfo m_yieldersField;
        private FieldInfo m_uniActionCountField;
        private FieldInfo m_uniWaitingCountField;
        private FieldInfo m_uniDequingField;
        private object m_uniQueue;
        private int m_count;
        private int m_steps;
        private int m_step;
        private int m_scheduled;
        private int m_consumed;
        private long m_controlChecksum;
        private bool m_onityLibrary;
        private bool m_typed;
        private bool m_hasSettings;
        private bool m_oldFlow;
        private bool m_oldTracker;
        private bool m_oldStackTrace;
        private bool m_matchedRetention;
        private bool m_selfTest;
        private int m_warmups = k_warmups;
        private int m_samples = k_samples;
        private int m_largeWarmups = k_largeWarmups;
        private int m_largeSamples = k_largeSamples;
        private bool m_listening;
        private string m_loggedError;
        private string m_path;
        private Action<string, Exception> m_completed;

        /// <summary>Runs the Release Player builder-lifecycle suite and writes its versioned JSON report.</summary>
        /// <param name="path">Output JSON path.</param>
        /// <param name="completed">Receives the report path and any failure after cleanup.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Builder-lifecycle output path is required.", nameof(path));
            }

            GameObject owner = new GameObject("Onity Builder Lifecycle Benchmark");
            DontDestroyOnLoad(owner);
            OnityTaskBuilderLifecycleBenchmarkRunner runner = owner.AddComponent<OnityTaskBuilderLifecycleBenchmarkRunner>();
            runner.m_path = Path.GetFullPath(path);
            runner.m_completed = completed;
        }

        private IEnumerator Start()
        {
            Report report = new Report();
            Exception failure = null;
            try
            {
                Application.logMessageReceived += OnLog;
                m_listening = true;
                m_oldFlow = OnityTask.FlowExecutionContext;
                m_oldTracker = OnityTaskTracker.IsEnabled;
                m_oldStackTrace = OnityTaskTracker.EnableStackTrace;
                m_hasSettings = true;
                // The library default (off) is the primary arm; Measure sets each arm's flag explicitly and
                // RestoreSettings restores the previous value.
                OnityTask.FlowExecutionContext = false;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                if (OnityTask.RunnerPoolCapacity != k_defaultRunnerPoolCapacity)
                {
                    throw new InvalidOperationException("Builder lifecycle requires unchanged default Onity runner retention 128.");
                }

                m_selfTest = OnityTaskBenchmarkOptions.IsSelfTest();
                if (m_selfTest)
                {
                    // Harness self-test: same arms and goldens, tiny sample counts; never used for ratios.
                    m_warmups = k_selfTestWarmups;
                    m_samples = k_selfTestSamples;
                    m_largeWarmups = k_selfTestWarmups;
                    m_largeSamples = k_selfTestSamples;
                }
                report.selfTest = m_selfTest;
                report.warmupsPerLibrary = m_warmups;
                report.samplesPerLibrary = m_samples;
                report.largeCohortWarmupsPerLibrary = m_largeWarmups;
                report.largeCohortSamplesPerLibrary = m_largeSamples;
                m_matchedRetention = OnityTaskBenchmarkOptions.ReadMatchedRetention();
                report.retentionArgument = m_matchedRetention ? "matched" : "default";
                if (m_matchedRetention)
                {
                    report.retentionPolicy = "matched: Onity RunnerPoolCapacity is raised to max(128, cohort) for each 4096 arm (retention=matched) "
                        + "and restored to 128 after it; UniTask is unbounded. The Onity pool is not drained between arms, so "
                        + "cohort 1 and 128 arms keep capacity 128 and are labelled retention=default before the first 4096 arm "
                        + "and retention=default-after-matched once a 4096 arm has run.";
                }

                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                // The execution-context path is recorded, not required: the primary arm runs with flow off.
                if (Application.isEditor || report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Builder lifecycle requires a non-development Release Player.");
                }

                m_empty = Empty;
                BindDrain(report);
                for (int i = 0; i < m_gates.Length; i++)
                {
                    m_gates[i] = new Gate();
                }
                m_completeStep = CompleteStep;
                m_returnPass = ReturnPass;
                m_controlSchedule = ControlSchedule;
                m_controlComplete = ControlComplete;
                m_controlConsume = ControlConsume;
                CheckGoldens(report);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            IEnumerator measurements = Measure(report);
            try
            {
                while (failure == null)
                {
                    bool next;
                    try
                    {
                        next = measurements.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        break;
                    }
                    if (!next)
                    {
                        break;
                    }
                    yield return null;
                }
            }
            finally
            {
                try
                {
                    (measurements as IDisposable)?.Dispose();
                }
                finally
                {
                    try
                    {
                        CleanupCohort();
                        CheckLog();
                    }
                    catch (Exception exception)
                    {
                        failure = failure ?? exception;
                    }
                    finally
                    {
                        RestoreSettings();
                    }
                }
            }

            report.completed = failure == null;
            report.failure = failure?.ToString();
            report.loggedError = m_loggedError;
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_path));
                File.WriteAllText(m_path, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            try
            {
                m_completed?.Invoke(m_path, failure);
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        private void BindDrain(Report report)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            // A runtime without a deferred-return queue keeps the Onity pass in the protocol as an empty call.
            m_onityQueue = OnityTaskBenchmarkInternals.BindDeferredReturnQueue();
            m_drain = m_onityQueue.Drain ?? m_empty;
            report.drainBinding = m_onityQueue.Description;
            report.onityDeferredReturnQueue = m_onityQueue.Drain != null ? "present" : "absent";
            m_yieldersField = typeof(PlayerLoopHelper).GetField("yielders", flags);
            Array yielders = m_yieldersField?.GetValue(null) as Array;
            int index = (int)PlayerLoopTiming.LastPostLateUpdate;
            if (yielders == null || yielders.Length <= index || yielders.GetValue(index) == null)
            {
                throw new InvalidOperationException("Pinned UniTask LastPostLateUpdate continuation queue is unavailable.");
            }
            m_uniQueue = yielders.GetValue(index);
            Type queueType = m_uniQueue.GetType();
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo run = queueType.GetMethod("Run", instanceFlags, null, Type.EmptyTypes, null);
            m_uniActionCountField = queueType.GetField("actionListCount", instanceFlags);
            m_uniWaitingCountField = queueType.GetField("waitingListCount", instanceFlags);
            m_uniDequingField = queueType.GetField("dequing", instanceFlags);
            if (queueType.FullName != "Cysharp.Threading.Tasks.Internal.ContinuationQueue"
                || run == null || run.ReturnType != typeof(void) || m_uniActionCountField == null
                || m_uniWaitingCountField == null || m_uniDequingField == null)
            {
                throw new InvalidOperationException("Exact pinned UniTask continuation queue binding unavailable or AOT stripped.");
            }
            m_uniDrain = (Action)Delegate.CreateDelegate(typeof(Action), m_uniQueue, run, true);
            report.uniDrainBinding = queueType.FullName + ".Run; PlayerLoopHelper.yielders[LastPostLateUpdate]; cached target Action."
                + " actionListCount/waitingListCount/dequing and current queue identity checked outside timing.";
            RuntimeHelpers.RunClassConstructor(typeof(TaskPool).TypeHandle);
            FieldInfo pool = typeof(TaskPool).GetField("MaxPoolSize", flags);
            report.uniTaskMaxPoolSize = pool == null ? -1 : (int)pool.GetValue(null);
            CheckQueue();
        }

        private IEnumerator Measure(Report report)
        {
            int[] counts = { 1, 128, 4096 };
            int[] suspensions = { 1, 4 };
            int metricIndex = 0;
            bool matchedPoolTouched = false;
            // Arm 0 is the library default (flow off, primary). Arm 1 is flow on, the explicitly labelled
            // diagnostic arm.
            for (int flow = 0; flow < 2; flow++)
            {
                bool flowOn = flow == 1;
                OnityTask.FlowExecutionContext = flowOn;
                for (int shape = 0; shape < 2; shape++)
                {
                    for (int size = 0; size < counts.Length; size++)
                    {
                        for (int depth = 0; depth < suspensions.Length; depth++)
                        {
                            int count = counts[size];
                            int steps = suspensions[depth];
                            // Fixed before seeing measurements, and identical for every library and flow arm.
                            int invocations = Math.Max(1, k_capacity / count);
                            bool large = count == k_capacity;
                            int warmups = large ? m_largeWarmups : m_warmups;
                            int samples = large ? m_largeSamples : m_samples;
                            int runnerCapacity = m_matchedRetention
                                ? Math.Max(k_defaultRunnerPoolCapacity, count) : k_defaultRunnerPoolCapacity;
                            // Matched mode: only 4096 arms are matched. Smaller arms keep capacity 128 and are
                            // labelled by whether a matched 4096 arm already ran (the pool is not drained).
                            string retention = !m_matchedRetention ? "default"
                                : large ? "matched"
                                : matchedPoolTouched ? "default-after-matched" : "default";
                            if (m_matchedRetention && large)
                            {
                                matchedPoolTouched = true;
                            }
                            Metric first = CreateMetric(true, shape == 1, flowOn, count, steps, invocations,
                                warmups, samples, retention, runnerCapacity);
                            Metric second = CreateMetric(false, shape == 1, flowOn, count, steps, invocations,
                                warmups, samples, retention, runnerCapacity);
                            report.metrics[metricIndex++] = first;
                            report.metrics[metricIndex++] = second;
                            // Matched retention applies to this arm only and is restored on every exit path.
                            OnityTask.RunnerPoolCapacity = runnerCapacity;
                            try
                            {
                                for (int warmup = 0; warmup < warmups; warmup++)
                                {
                                    for (int turn = 0; turn < 2; turn++)
                                    {
                                        bool onity = ((warmup + turn) & 1) == 0;
                                        Sample sample = MeasureBatch(onity, shape == 1, count, steps, invocations, warmup, turn);
                                        (onity ? first : second).warmups[warmup] = sample;
                                        yield return null;
                                        yield return null;
                                        CheckFrames(sample);
                                    }
                                }
                                for (int index = 0; index < samples; index++)
                                {
                                    for (int turn = 0; turn < 2; turn++)
                                    {
                                        bool onity = ((index + turn) & 1) == 0;
                                        Sample sample = MeasureBatch(onity, shape == 1, count, steps, invocations, index, turn);
                                        (onity ? first : second).samples[index] = sample;
                                        yield return null;
                                        yield return null;
                                        CheckFrames(sample);
                                    }
                                }
                            }
                            finally
                            {
                                OnityTask.RunnerPoolCapacity = k_defaultRunnerPoolCapacity;
                            }
                        }
                    }
                }
            }
        }

        private static Metric CreateMetric(bool onity, bool typed, bool flow, int count, int steps, int invocations,
            int warmups, int samples, string retention, int runnerCapacity)
        {
            return new Metric
            {
                library = onity ? "OnityTask" : "UniTask",
                shape = typed ? "Typed int" : "Untyped",
                onityFlowExecutionContext = flow,
                flowSemantics = onity ? "Onity setting applied per suspension" : "UniTask no-flow control",
                cohortSize = count,
                sequentialSuspensions = steps,
                invocations = invocations,
                operations = count * invocations,
                diagnosticOnly = count == 1 || flow,
                retentionPolicy = retention,
                retentionLabel = "retention=" + retention,
                runnerPoolCapacity = runnerCapacity,
                environment = OnityTaskBenchmarkEnvironment.Capture(),
                warmups = new Sample[warmups],
                samples = new Sample[samples]
            };
        }

        private Sample MeasureBatch(bool onity, bool typed, int count, int steps, int invocations, int index, int order)
        {
            Select(onity, typed, count, steps);
            Sample sample = new Sample
            {
                index = index,
                libraryOrder = order,
                completionTicks = new long[steps],
                operations = count * invocations,
                phaseBracketCount = (steps + 4) * invocations
            };
            // Onity's pending count before the first pass is recorded, never asserted: a runtime that returns
            // inline (return-on-unwind) leaves it at zero and a redesign may legitimately change it. -1 = not asserted.
            sample.expectedOnityBeforeFirstPassPerInvocation = -1;
            sample.expectedUniBeforeFirstPassPerInvocation = IsIl2Cpp && !onity ? count : 0;
            sample.timestampControl = MeasureControl(invocations, false);
            sample.arrayLoopControl = MeasureControl(invocations, true);
            bool succeeded = false;
            try
            {
                for (int invocation = 0; invocation < invocations; invocation++)
                {
                    Prepare();
                    CheckQueue();
                    sample.zeroBeforeInvocationCount++;
                    sample.scheduleTicks += TimePhase(m_schedule);
                    CheckStep(-1);
                    for (int step = 0; step < steps; step++)
                    {
                        m_step = step;
                        sample.completionTicks[step] += TimePhase(m_completeStep);
                        CheckStep(step);
                    }
                    sample.consumptionTicks += TimePhase(m_consume);
                    CheckBeforeReturnPass(sample);
                    sample.drainFirstTicks += TimePhase(m_returnPass);
                    CheckBetweenReturnPasses(sample);
                    sample.zeroBetweenDrainsCount++;
                    sample.drainSecondTicks += TimePhase(m_returnPass);
                    CheckQueue();
                    sample.zeroAfterDrainsCount++;
                    Validate(sample);
                    ClearCohort();
                }
                sample.totalTicks = sample.scheduleTicks + sample.consumptionTicks
                    + sample.drainFirstTicks + sample.drainSecondTicks;
                for (int step = 0; step < steps; step++)
                {
                    sample.totalTicks += sample.completionTicks[step];
                }
                double factor = 1000000000d / Stopwatch.Frequency / sample.operations;
                sample.scheduleNanosecondsPerOperation = sample.scheduleTicks * factor;
                sample.completionNanosecondsPerOperation = new double[steps];
                for (int step = 0; step < steps; step++)
                {
                    sample.completionNanosecondsPerOperation[step] = sample.completionTicks[step] * factor;
                }
                sample.consumptionNanosecondsPerOperation = sample.consumptionTicks * factor;
                sample.drainFirstNanosecondsPerOperation = sample.drainFirstTicks * factor;
                sample.drainSecondNanosecondsPerOperation = sample.drainSecondTicks * factor;
                sample.totalNanosecondsPerOperation = sample.totalTicks * factor;
                sample.validated = true;
                sample.safetyStartFrame = Time.frameCount;
                succeeded = true;
                return sample;
            }
            finally
            {
                if (!succeeded)
                {
                    CleanupCohort();
                }
                ClearCohort();
            }
        }

        private static long TimePhase(Action work)
        {
            long start = Stopwatch.GetTimestamp();
            work();
            return Stopwatch.GetTimestamp() - start;
        }

        private void Select(bool onity, bool typed, int count, int steps)
        {
            m_onityLibrary = onity;
            m_typed = typed;
            m_count = count;
            m_steps = steps;
            if (onity)
            {
                m_schedule = typed ? (Action)ScheduleOnityTyped : ScheduleOnity;
                m_consume = typed ? (Action)ConsumeOnityTyped : ConsumeOnity;
            }
            else
            {
                m_schedule = typed ? (Action)ScheduleUniTyped : ScheduleUni;
                m_consume = typed ? (Action)ConsumeUniTyped : ConsumeUni;
            }
        }

        private void Prepare()
        {
            if (m_scheduled != 0)
            {
                throw new InvalidOperationException("A lifecycle cohort retained native task handles.");
            }
            m_consumed = 0;
            for (int operation = 0; operation < m_count; operation++)
            {
                m_attempted[operation] = false;
                m_results[operation] = -1;
                for (int step = 0; step < m_steps; step++)
                {
                    m_gates[operation * k_gateStride + step].Reset();
                }
            }
        }

        private void ScheduleOnity()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_onity[i] = AwaitOnity(m_gates, i * k_gateStride, m_steps);
                m_scheduled++;
            }
        }

        private void ScheduleOnityTyped()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_onityTyped[i] = AwaitOnityTyped(m_gates, i * k_gateStride, m_steps);
                m_scheduled++;
            }
        }

        private void ScheduleUni()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_uni[i] = AwaitUni(m_gates, i * k_gateStride, m_steps);
                m_scheduled++;
            }
        }

        private void ScheduleUniTyped()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_uniTyped[i] = AwaitUniTyped(m_gates, i * k_gateStride, m_steps);
                m_scheduled++;
            }
        }

        private void CompleteStep()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_gates[i * k_gateStride + m_step].Complete();
            }
        }

        private void ConsumeOnity()
        {
            for (int i = 0; i < m_scheduled; i++)
            {
                m_attempted[i] = true;
                m_onity[i].GetAwaiter().GetResult();
                m_results[i] = 1;
                m_consumed++;
            }
        }

        private void ConsumeOnityTyped()
        {
            for (int i = 0; i < m_scheduled; i++)
            {
                m_attempted[i] = true;
                m_results[i] = m_onityTyped[i].GetAwaiter().GetResult();
                m_consumed++;
            }
        }

        private void ConsumeUni()
        {
            for (int i = 0; i < m_scheduled; i++)
            {
                m_attempted[i] = true;
                m_uni[i].GetAwaiter().GetResult();
                m_results[i] = 1;
                m_consumed++;
            }
        }

        private void ConsumeUniTyped()
        {
            for (int i = 0; i < m_scheduled; i++)
            {
                m_attempted[i] = true;
                m_results[i] = m_uniTyped[i].GetAwaiter().GetResult();
                m_consumed++;
            }
        }

        private static async OnityTask AwaitOnity(Gate[] gates, int offset, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await gates[offset + step];
            }
        }

        private static async OnityTask<int> AwaitOnityTyped(Gate[] gates, int offset, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await gates[offset + step];
            }
            return 42;
        }

        private static async UniTask AwaitUni(Gate[] gates, int offset, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await gates[offset + step];
            }
        }

        private static async UniTask<int> AwaitUniTyped(Gate[] gates, int offset, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await gates[offset + step];
            }
            return 42;
        }

        private void CheckStep(int completedStep)
        {
            if (m_scheduled != m_count)
            {
                throw new InvalidOperationException("Lifecycle schedule count mismatch.");
            }
            for (int operation = 0; operation < m_count; operation++)
            {
                for (int step = 0; step < m_steps; step++)
                {
                    Gate gate = m_gates[operation * k_gateStride + step];
                    int registered = step <= completedStep + 1 ? 1 : 0;
                    int completed = step <= completedStep ? 1 : 0;
                    if (gate.Registrations != registered || gate.Callbacks != completed
                        || gate.Completions != completed || gate.ResultReads != completed)
                    {
                        throw new InvalidOperationException("Sequential suspension registration/completion forwarding failed.");
                    }
                }
            }
            CheckLog();
        }

        private void Validate(Sample sample)
        {
            if (m_consumed != m_count)
            {
                throw new InvalidOperationException("Native lifecycle outputs were not consumed exactly once.");
            }
            for (int operation = 0; operation < m_count; operation++)
            {
                if (!m_attempted[operation] || m_results[operation] != (m_typed ? 42 : 1))
                {
                    throw new InvalidOperationException("Native lifecycle result/checksum mismatch.");
                }
                sample.nativeConsumptions++;
                sample.resultChecksum += m_results[operation];
                for (int step = 0; step < m_steps; step++)
                {
                    Gate gate = m_gates[operation * k_gateStride + step];
                    gate.CheckCompleted();
                    sample.registrations += gate.Registrations;
                    sample.callbacks += gate.Callbacks;
                    sample.completions += gate.Completions;
                    sample.gateResults += gate.ResultReads;
                }
            }
            CheckLog();
        }

        private void CheckGoldens(Report report)
        {
            for (int flow = 0; flow < 2; flow++)
            {
                OnityTask.FlowExecutionContext = flow == 1;
                for (int shape = 0; shape < 2; shape++)
                {
                    for (int library = 0; library < 2; library++)
                    {
                        Select(library == 0, shape == 1, 2, 4);
                        try
                        {
                            Prepare();
                            CheckQueue();
                            m_schedule();
                            CheckStep(-1);
                            for (int step = 0; step < 4; step++)
                            {
                                m_step = step;
                                CompleteStep();
                                CheckStep(step);
                            }
                            m_consume();
                            CheckBeforeReturnPass(new Sample());
                            m_returnPass();
                            CheckBetweenReturnPasses(new Sample());
                            m_returnPass();
                            CheckQueue();
                            Sample golden = new Sample();
                            Validate(golden);
                            if (golden.registrations != 8 || golden.callbacks != 8 || golden.gateResults != 8
                                || golden.completions != 8 || golden.nativeConsumptions != 2
                                || golden.resultChecksum != (shape == 1 ? 84 : 2))
                            {
                                throw new InvalidOperationException("Fixed four-suspension golden count/result failed.");
                            }
                            report.multiSuspensionGoldensPassed++;
                        }
                        finally
                        {
                            CleanupCohort();
                        }
                    }
                }
            }
            report.goldensPassed = report.multiSuspensionGoldensPassed == 8;
        }

        private Control MeasureControl(int invocations, bool arrayLoops)
        {
            Control result = new Control { completionTicks = new long[m_steps] };
            m_controlChecksum = 0;
            Action schedule = arrayLoops ? m_controlSchedule : m_empty;
            Action completion = arrayLoops ? m_controlComplete : m_empty;
            Action consume = arrayLoops ? m_controlConsume : m_empty;
            for (int invocation = 0; invocation < invocations; invocation++)
            {
                result.scheduleTicks += TimePhase(schedule);
                for (int step = 0; step < m_steps; step++)
                {
                    result.completionTicks[step] += TimePhase(completion);
                }
                result.consumptionTicks += TimePhase(consume);
                result.drainFirstTicks += TimePhase(m_empty);
                result.drainSecondTicks += TimePhase(m_empty);
            }
            result.totalTicks = result.scheduleTicks + result.consumptionTicks
                + result.drainFirstTicks + result.drainSecondTicks;
            for (int step = 0; step < m_steps; step++)
            {
                result.totalTicks += result.completionTicks[step];
            }
            result.checksum = m_controlChecksum;
            result.phaseBracketCount = (m_steps + 4) * invocations;
            if (arrayLoops && result.checksum != (long)m_count * invocations * (1 + m_steps))
            {
                throw new InvalidOperationException("Unsubtracted array-loop control checksum failed.");
            }
            return result;
        }

        private static void Empty()
        {
        }

        private void ControlSchedule()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_controlValues[i] = 1;
            }
        }

        private void ControlComplete()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_controlValues[i]++;
            }
        }

        private void ControlConsume()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_controlChecksum += m_controlValues[i];
            }
        }

        private static bool IsIl2Cpp
        {
            get
            {
#if ENABLE_IL2CPP
                return true;
#else
                return false;
#endif
            }
        }

        private void ReturnPass()
        {
            m_drain();
            m_uniDrain();
        }

        private void CheckUniQueueIdentity()
        {
            Array current = m_yieldersField.GetValue(null) as Array;
            int index = (int)PlayerLoopTiming.LastPostLateUpdate;
            if (current == null || current.Length <= index || !ReferenceEquals(current.GetValue(index), m_uniQueue))
            {
                throw new InvalidOperationException("Pinned UniTask return queue changed after binding.");
            }
        }

        // Pinned UniTask: IL2CPP queues exactly one return per consumed task, Mono none. Onity: observed only.
        private void CheckBeforeReturnPass(Sample sample)
        {
            CheckLog();
            CheckUniQueueIdentity();
            int uniActions = (int)m_uniActionCountField.GetValue(m_uniQueue);
            int uniWaiting = (int)m_uniWaitingCountField.GetValue(m_uniQueue);
            bool uniDequing = (bool)m_uniDequingField.GetValue(m_uniQueue);
            int expectedUni = IsIl2Cpp && !m_onityLibrary ? m_count : 0;
            if (uniActions != expectedUni || uniWaiting != 0 || uniDequing)
            {
                throw new InvalidOperationException("Pinned UniTask runner-return queue count did not match before the first pass.");
            }
            sample.onityBeforeFirstPassSum += Math.Max(0, m_onityQueue.ReadPending());
            sample.uniBeforeFirstPassSum += uniActions;
            sample.expectedBeforeFirstPassChecks++;
        }

        // Between the two passes the pinned UniTask queue must be empty; Onity's count is recorded only.
        private void CheckBetweenReturnPasses(Sample sample)
        {
            CheckLog();
            CheckUniQueueIdentity();
            if ((int)m_uniActionCountField.GetValue(m_uniQueue) != 0
                || (int)m_uniWaitingCountField.GetValue(m_uniQueue) != 0
                || (bool)m_uniDequingField.GetValue(m_uniQueue))
            {
                throw new InvalidOperationException("Pinned UniTask return queue was not empty and idle after the first pass.");
            }
            sample.onityBetweenPassesSum += Math.Max(0, m_onityQueue.ReadPending());
        }

        // After both passes and before every invocation both libraries must be idle.
        private void CheckQueue()
        {
            CheckLog();
            CheckUniQueueIdentity();
            if (!m_onityQueue.IsIdle()
                || (int)m_uniActionCountField.GetValue(m_uniQueue) != 0
                || (int)m_uniWaitingCountField.GetValue(m_uniQueue) != 0
                || (bool)m_uniDequingField.GetValue(m_uniQueue))
            {
                throw new InvalidOperationException("A production return queue was not empty and idle (Onity pending="
                    + m_onityQueue.ReadPending() + ", drainState=" + m_onityQueue.ReadDrainState() + ").");
            }
        }

        private void CheckFrames(Sample sample)
        {
            sample.safetyEndFrame = Time.frameCount;
            sample.safetyFrames = sample.safetyEndFrame - sample.safetyStartFrame;
            if (sample.safetyFrames < 2)
            {
                throw new InvalidOperationException("Lifecycle batch did not advance two real Unity safety frames.");
            }
            CheckQueue();
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (m_loggedError == null && (type == LogType.Error || type == LogType.Exception))
            {
                m_loggedError = condition + "\n" + stackTrace;
            }
        }

        private void CheckLog()
        {
            if (m_loggedError != null)
            {
                throw new InvalidOperationException("Logged lifecycle callback/drain error: " + m_loggedError);
            }
        }

        private void CleanupCohort()
        {
            try
            {
                // Finish every remaining registered step without replacing any gate or native output.
                for (int step = 0; step < m_steps; step++)
                {
                    for (int operation = 0; operation < m_scheduled; operation++)
                    {
                        Gate gate = m_gates[operation * k_gateStride + step];
                        if (gate.Registrations == 1 && gate.Completions == 0)
                        {
                            try
                            {
                                gate.Complete();
                            }
                            catch (Exception)
                            {
                                // Preserve the original failure and continue settling accepted outputs.
                            }
                        }
                    }
                }
                for (int operation = 0; operation < m_scheduled; operation++)
                {
                    if (m_attempted[operation])
                    {
                        continue;
                    }
                    m_attempted[operation] = true;
                    try
                    {
                        if (m_onityLibrary)
                        {
                            if (m_typed)
                            {
                                m_onityTyped[operation].GetAwaiter().GetResult();
                            }
                            else
                            {
                                m_onity[operation].GetAwaiter().GetResult();
                            }
                        }
                        else if (m_typed)
                        {
                            m_uniTyped[operation].GetAwaiter().GetResult();
                        }
                        else
                        {
                            m_uni[operation].GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception)
                    {
                        // Marked before GetResult: an exception may already have retired the source.
                    }
                }
            }
            finally
            {
                try
                {
                    if (m_drain != null && m_uniDrain != null && m_scheduled != 0)
                    {
                        try
                        {
                            ReturnPass();
                        }
                        finally
                        {
                            ReturnPass();
                        }
                    }
                }
                finally
                {
                    ClearCohort();
                }
            }
        }

        private void ClearCohort()
        {
            Array.Clear(m_onity, 0, m_scheduled);
            Array.Clear(m_onityTyped, 0, m_scheduled);
            Array.Clear(m_uni, 0, m_scheduled);
            Array.Clear(m_uniTyped, 0, m_scheduled);
            m_scheduled = 0;
        }

        private void RestoreSettings()
        {
            if (m_hasSettings)
            {
                OnityTask.FlowExecutionContext = m_oldFlow;
                OnityTaskTracker.IsEnabled = m_oldTracker;
                OnityTaskTracker.EnableStackTrace = m_oldStackTrace;
                OnityTask.RunnerPoolCapacity = k_defaultRunnerPoolCapacity;
                m_hasSettings = false;
            }
            if (m_listening)
            {
                Application.logMessageReceived -= OnLog;
                m_listening = false;
            }
        }

        private void OnDestroy()
        {
            try
            {
                CleanupCohort();
            }
            finally
            {
                RestoreSettings();
            }
        }

        private sealed class Gate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            private bool m_completed;
            public int Registrations { get; private set; }
            public int Completions { get; private set; }
            public int Callbacks { get; private set; }
            public int ResultReads { get; private set; }
            public bool IsCompleted => m_completed;
            public Gate GetAwaiter() => this;
            public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

            public void UnsafeOnCompleted(Action continuation)
            {
                if (continuation == null || m_continuation != null || m_completed || Registrations != 0)
                {
                    throw new InvalidOperationException("Invalid lifecycle manual-gate registration.");
                }
                m_continuation = continuation;
                Registrations++;
            }

            public void GetResult()
            {
                if (!m_completed || ResultReads != 0)
                {
                    throw new InvalidOperationException("Lifecycle gate result was pending or already read.");
                }
                ResultReads++;
            }

            public void Complete()
            {
                if (m_completed || m_continuation == null)
                {
                    throw new InvalidOperationException("Lifecycle gate has no pending consumer.");
                }
                m_completed = true;
                Completions++;
                Action continuation = m_continuation;
                m_continuation = null;
                Callbacks++;
                continuation();
            }

            public void Reset()
            {
                if (m_continuation != null || (Registrations != 0 && ResultReads != 1))
                {
                    throw new InvalidOperationException("Lifecycle gate retained an old consumer.");
                }
                m_completed = false;
                Registrations = Completions = Callbacks = ResultReads = 0;
            }

            public void CheckCompleted()
            {
                if (Registrations != 1 || Completions != 1 || Callbacks != 1 || ResultReads != 1
                    || m_continuation != null || !m_completed)
                {
                    throw new InvalidOperationException("Lifecycle gate completion/count/reference validation failed.");
                }
            }
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string suite = "builderlifecycle";
            public string title = "Manual-awaitable builder lifecycle elapsed phases";
            public string measurementScope = "Controlled synchronous ELAPSED phase sum: schedule actual native async cohort; "
                + "resume each sequential suspension; single native GetResult consumption; exactly two common return passes per invocation "
                + "in both libraries, each calling the production Onity dispatcher Drain (an empty measured call when the runtime has no "
                + "such queue) then pinned UniTask LastPostLateUpdate ContinuationQueue.Run. "
                + "Real builder/source reset, per-arm execution-context flow setting, allocation, continuations, inline returns and both libraries' "
                + "deferred returns are included. Gate/task-array preparation, validation/reflection, controls and two real safety frames after the "
                + "whole batch are excluded. Timed GC and OS scheduling interruptions are included; this is not CPU time or natural PlayerLoop latency.";
            public string returnBoundary = "Both return passes run after all producing MoveNext/GetResult stacks unwind, before the next "
                + "invocation. No PlayerLoopRunner, timers, workers or natural PlayerLoop latency are driven. Pinned UniTask must hold N queued "
                + "returns before the first pass on IL2CPP when it is the active library and zero otherwise (Mono: zero), and zero after the "
                + "first pass. Onity's queue count is recorded before and between the passes, never asserted there (inline return-on-unwind "
                + "leaves it empty; a runtime without the queue runs an empty pass). Both libraries must be empty and idle after the two "
                + "passes and before every invocation. Common inactive-queue/harness overhead is unsubtracted.";
            public string onityDeferredReturnQueue;
            public bool selfTest;
            public string flowPolicy = "Flow=false (library default) primary; flow=true is an explicitly labelled secondary arm, diagnostic only; "
                + "no AsyncLocal seeded. UniTask no-flow controls match the primary arm's execution-context semantics.";
            public string retentionPolicy = "Onity default runner retention 128 is required and unchanged. UniTask is not capped to match; "
                + "installed MaxPoolSize reported when reflected. N=4096 rental pressure and allocations remain included.";
            public string normalization = "invocations=max(1,4096/N), fixed before results; ticks aggregated by phase over invocations; "
                + "nanoseconds per operation normalized by N*invocations. N=1 is harness-sensitive diagnostic only.";
            public string controls = "Unsubtracted cached-empty-action timestamp and preallocated array-loop controls use identical "
                + "phase bracket counts; control return-pass brackets are empty and do not remove production return costs.";
            public string order = "Library order alternates by sample/warmup index; raw samples retain libraryOrder.";
            public string allocation = "UNAVAILABLE: no isolated allocation counter; no zero-allocation claim.";
            public string queueProof = "Require UniTask actionListCount=0/waitingListCount=0/dequing=false before each invocation, between "
                + "its two timed return passes and after both, and Onity pendingCount=0/drainState=0 (where observable) before each invocation "
                + "and after both passes; Onity counts before and between the passes are reported (onityBeforeFirstPassSum, "
                + "onityBetweenPassesSum), not asserted. Validate bound UniTask queue identity. Reject logged Error/Exception. No uncounted "
                + "cleanup can validate a failed sample.";
            public int onityRunnerPoolCapacity = 128;
            public int uniTaskMaxPoolSize;
            public string drainBinding;
            public string uniDrainBinding;
            public bool isIl2Cpp = IsIl2Cpp;
            public int preallocatedGateCapacity = k_capacity * k_gateStride;
            public int nativeTaskArrayCapacity = k_capacity;
            public int warmupsPerLibrary = k_warmups;
            public int samplesPerLibrary = k_samples;
            public int largeCohortWarmupsPerLibrary = k_largeWarmups;
            public int largeCohortSamplesPerLibrary = k_largeSamples;
            public string retentionArgument = "default";
            public long stopwatchFrequency = Stopwatch.Frequency;
            public OnityTaskBenchmarkEnvironment environment;
            public bool goldensPassed;
            public int multiSuspensionGoldensPassed;
            public Metric[] metrics = new Metric[48];
            public bool completed;
            public string failure;
            public string loggedError;
            public string generatedAtUtc;
        }

        [Serializable]
        private sealed class Metric
        {
            public string library;
            public string shape;
            public bool onityFlowExecutionContext;
            public string flowSemantics;
            public int cohortSize;
            public int sequentialSuspensions;
            public int invocations;
            public int operations;
            public bool diagnosticOnly;
            public string retentionPolicy;
            public string retentionLabel;
            public int runnerPoolCapacity;
            public OnityTaskBenchmarkEnvironment environment;
            public Sample[] warmups;
            public Sample[] samples;
        }

        [Serializable]
        private sealed class Sample
        {
            public int index;
            public int libraryOrder;
            public int operations;
            public int phaseBracketCount;
            public long scheduleTicks;
            public long[] completionTicks;
            public long consumptionTicks;
            public long drainFirstTicks;
            public long drainSecondTicks;
            public long totalTicks;
            public double scheduleNanosecondsPerOperation;
            public double[] completionNanosecondsPerOperation;
            public double consumptionNanosecondsPerOperation;
            public double drainFirstNanosecondsPerOperation;
            public double drainSecondNanosecondsPerOperation;
            public double totalNanosecondsPerOperation;
            public Control timestampControl;
            public Control arrayLoopControl;
            public int expectedOnityBeforeFirstPassPerInvocation;
            public int expectedUniBeforeFirstPassPerInvocation;
            public long onityBeforeFirstPassSum;
            public long onityBetweenPassesSum;
            public long uniBeforeFirstPassSum;
            public int expectedBeforeFirstPassChecks;
            public int zeroBeforeInvocationCount;
            public int zeroBetweenDrainsCount;
            public int zeroAfterDrainsCount;
            public int registrations;
            public int callbacks;
            public int completions;
            public int gateResults;
            public int nativeConsumptions;
            public long resultChecksum;
            public int safetyStartFrame;
            public int safetyEndFrame;
            public int safetyFrames;
            public bool validated;
        }

        [Serializable]
        private sealed class Control
        {
            public long scheduleTicks;
            public long[] completionTicks;
            public long consumptionTicks;
            public long drainFirstTicks;
            public long drainSecondTicks;
            public long totalTicks;
            public int phaseBracketCount;
            public long checksum;
        }
    }
}
