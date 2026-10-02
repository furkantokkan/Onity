using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>Measures one manual-awaitable suspension and logical builder completion per operation.</summary>
    public sealed class OnityTaskBuilderCycleBenchmarkRunner : MonoBehaviour
    {
        private const int k_concurrency = 128;
        private const int k_warmups = 2;
        private const int k_samples = 8;
        private string m_path;
        private Action<string, Exception> m_completed;
        private bool m_hasSettings;
        private bool m_oldFlow;
        private bool m_oldTracker;
        private bool m_oldStackTrace;
        private int m_oldCapacity;

        /// <summary>Runs the Release Player logical builder-cycle suite and writes its JSON report.</summary>
        /// <param name="path">Output JSON path.</param>
        /// <param name="completed">Receives the report path and any failure after settings restoration.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Builder-cycle output path is required.", nameof(path));
            }

            GameObject owner = new GameObject("Onity Logical Builder Cycle Benchmark");
            DontDestroyOnLoad(owner);
            OnityTaskBuilderCycleBenchmarkRunner runner = owner.AddComponent<OnityTaskBuilderCycleBenchmarkRunner>();
            runner.m_path = Path.GetFullPath(path);
            runner.m_completed = completed;
        }

        private IEnumerator Start()
        {
            Report report = new Report();
            Exception failure = null;
            m_oldFlow = OnityTask.FlowExecutionContext;
            m_oldTracker = OnityTaskTracker.IsEnabled;
            m_oldStackTrace = OnityTaskTracker.EnableStackTrace;
            m_oldCapacity = OnityTask.RunnerPoolCapacity;
            m_hasSettings = true;
            try
            {
                // Library default (off) is the primary arm; the flow loop below sets each arm's flag explicitly.
                OnityTask.FlowExecutionContext = false;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTask.RunnerPoolCapacity = k_concurrency;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                report.isEditor = Application.isEditor;
                report.platform = Application.platform.ToString();
                if (report.isEditor || report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Builder-cycle benchmark requires a non-development Release Player.");
                }
                if (report.environment.executionContextPath == "Unknown")
                {
                    throw new InvalidOperationException("Builder-cycle benchmark requires runtime context-path evidence.");
                }
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
                    RestoreSettings();
                }
            }

            report.completed = failure == null;
            report.failure = failure?.ToString();
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

        private IEnumerator Measure(Report report)
        {
            Batch batch = new Batch();
            int[] controlValues = new int[k_concurrency];
            // flow 0 is the library default (off, primary); flow 1 is the explicitly labelled flow-on arm.
            for (int flow = 0; flow < 2; flow++)
            {
                OnityTask.FlowExecutionContext = flow == 1;
                for (int shape = 0; shape < 2; shape++)
                {
                    int row = (flow * 2 + shape) * 2;
                    for (int library = 0; library < 2; library++)
                    {
                        Metric metric = new Metric
                        {
                            library = library == 0 ? "OnityTask" : "UniTask",
                            shape = shape == 0 ? "Untyped" : "Typed int",
                            onityFlowExecutionContext = flow == 1,
                            libraryFlowSemantics = library == 0 ? "Onity setting applied" : "UniTask no-flow control",
                            environment = OnityTaskBenchmarkEnvironment.Capture(),
                            warmups = new Sample[k_warmups],
                            samples = new Sample[k_samples]
                        };
                        report.metrics[row + library] = metric;
                    }

                    for (int warmup = 0; warmup < k_warmups; warmup++)
                    {
                        for (int turn = 0; turn < 2; turn++)
                        {
                            int library = (warmup + turn) & 1;
                            Sample sample = new Sample { index = warmup, libraryOrder = turn };
                            report.metrics[row + library].warmups[warmup] = sample;
                            batch.Measure(library == 0, shape == 1, sample);
                            sample.drainStartFrame = Time.frameCount;
                            yield return null;
                            yield return null;
                            CheckDrain(sample);
                        }
                    }

                    Control[] controls = new Control[k_samples];
                    report.controls[flow * 2 + shape] = new ControlSet
                    {
                        onityFlowExecutionContext = flow == 1,
                        shape = shape == 0 ? "Untyped" : "Typed int",
                        samples = controls
                    };
                    for (int index = 0; index < k_samples; index++)
                    {
                        controls[index] = MeasureControl(controlValues, index);
                        for (int turn = 0; turn < 2; turn++)
                        {
                            int library = (index + turn) & 1;
                            Sample sample = new Sample { index = index, libraryOrder = turn };
                            report.metrics[row + library].samples[index] = sample;
                            batch.Measure(library == 0, shape == 1, sample);
                            sample.drainStartFrame = Time.frameCount;
                            yield return null;
                            yield return null;
                            CheckDrain(sample);
                        }
                    }
                    Summarize(report.metrics[row]);
                    Summarize(report.metrics[row + 1]);
                }
            }
        }

        private static void CheckDrain(Sample sample)
        {
            sample.drainEndFrame = Time.frameCount;
            sample.drainFrames = sample.drainEndFrame - sample.drainStartFrame;
            if (sample.drainFrames < 2)
            {
                throw new InvalidOperationException("Builder-cycle pool drain did not advance two real Unity frames.");
            }
        }

        private static Control MeasureControl(int[] values, int index)
        {
            Array.Clear(values, 0, values.Length);
            Control sample = new Control { index = index };
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < k_concurrency; i++)
            {
                values[i] = 1;
            }
            long stop = Stopwatch.GetTimestamp();
            sample.scheduleTicks = stop - start;
            start = Stopwatch.GetTimestamp();
            for (int i = 0; i < k_concurrency; i++)
            {
                values[i]++;
            }
            stop = Stopwatch.GetTimestamp();
            sample.completionTicks = stop - start;
            long checksum = 0;
            start = Stopwatch.GetTimestamp();
            for (int i = 0; i < k_concurrency; i++)
            {
                checksum += values[i];
            }
            stop = Stopwatch.GetTimestamp();
            sample.consumptionTicks = stop - start;
            sample.checksum = checksum;
            if (checksum != k_concurrency * 2)
            {
                throw new InvalidOperationException("Builder-cycle common loop control checksum failed.");
            }
            SetNanoseconds(sample);
            return sample;
        }

        private static void SetNanoseconds(Timing timing)
        {
            double scale = 1000000000d / Stopwatch.Frequency / k_concurrency;
            timing.totalTicks = timing.scheduleTicks + timing.completionTicks + timing.consumptionTicks;
            timing.scheduleNanosecondsPerOperation = timing.scheduleTicks * scale;
            timing.completionNanosecondsPerOperation = timing.completionTicks * scale;
            timing.consumptionNanosecondsPerOperation = timing.consumptionTicks * scale;
            timing.totalNanosecondsPerOperation = timing.totalTicks * scale;
        }

        private static void Summarize(Metric metric)
        {
            for (int i = 0; i < metric.samples.Length; i++)
            {
                Sample sample = metric.samples[i];
                metric.meanScheduleNanosecondsPerOperation += sample.scheduleNanosecondsPerOperation / k_samples;
                metric.meanCompletionNanosecondsPerOperation += sample.completionNanosecondsPerOperation / k_samples;
                metric.meanConsumptionNanosecondsPerOperation += sample.consumptionNanosecondsPerOperation / k_samples;
                metric.meanTotalNanosecondsPerOperation += sample.totalNanosecondsPerOperation / k_samples;
            }
        }

        private void OnDestroy()
        {
            RestoreSettings();
        }

        private void RestoreSettings()
        {
            if (!m_hasSettings)
            {
                return;
            }
            OnityTask.FlowExecutionContext = m_oldFlow;
            OnityTaskTracker.IsEnabled = m_oldTracker;
            OnityTaskTracker.EnableStackTrace = m_oldStackTrace;
            OnityTask.RunnerPoolCapacity = m_oldCapacity;
            m_hasSettings = false;
        }

        private sealed class Batch
        {
            private readonly ManualGate[] m_gates = new ManualGate[k_concurrency];
            private readonly OnityTask[] m_onity = new OnityTask[k_concurrency];
            private readonly OnityTask<int>[] m_onityTyped = new OnityTask<int>[k_concurrency];
            private readonly UniTask[] m_uni = new UniTask[k_concurrency];
            private readonly UniTask<int>[] m_uniTyped = new UniTask<int>[k_concurrency];
            private readonly int[] m_results = new int[k_concurrency];
            private readonly bool[] m_consumptionAttempted = new bool[k_concurrency];
            private readonly Action[] m_schedulers;
            private readonly Action[] m_consumers;
            private int m_scheduled;

            internal Batch()
            {
                m_schedulers = new Action[] { ScheduleOnityUntyped, ScheduleOnityTyped, ScheduleUniUntyped, ScheduleUniTyped };
                m_consumers = new Action[] { ConsumeOnityUntyped, ConsumeOnityTyped, ConsumeUniUntyped, ConsumeUniTyped };
                for (int i = 0; i < k_concurrency; i++)
                {
                    m_gates[i] = new ManualGate();
                }
            }

            internal void Measure(bool onity, bool typed, Sample sample)
            {
                int workload = (onity ? 0 : 2) + (typed ? 1 : 0);
                Action schedule = m_schedulers[workload];
                Action consume = m_consumers[workload];
                m_scheduled = 0;
                for (int i = 0; i < k_concurrency; i++)
                {
                    m_gates[i].Reset();
                    m_results[i] = 0;
                    m_consumptionAttempted[i] = false;
                }
                Exception failure = null;
                try
                {
                    // Workload dispatch is selected before timing; both libraries pay the same delegate and bookkeeping costs.
                    long start = Stopwatch.GetTimestamp();
                    schedule();
                    long stop = Stopwatch.GetTimestamp();
                    sample.scheduleTicks = stop - start;
                    for (int i = 0; i < k_concurrency; i++)
                    {
                        if (m_gates[i].Registrations != 1 || IsCompleted(i, onity, typed))
                        {
                            throw new InvalidOperationException("Builder cycle did not suspend exactly once.");
                        }
                    }

                    start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < k_concurrency; i++)
                    {
                        m_gates[i].Complete();
                    }
                    stop = Stopwatch.GetTimestamp();
                    sample.completionTicks = stop - start;
                    for (int i = 0; i < k_concurrency; i++)
                    {
                        if (!IsCompleted(i, onity, typed))
                        {
                            throw new InvalidOperationException("Builder output did not complete with its manual gate.");
                        }
                    }

                    start = Stopwatch.GetTimestamp();
                    consume();
                    stop = Stopwatch.GetTimestamp();
                    sample.consumptionTicks = stop - start;
                    for (int i = 0; i < k_concurrency; i++)
                    {
                        ManualGate gate = m_gates[i];
                        if (gate.Registrations != 1 || gate.Callbacks != 1 || gate.Completions != 1
                            || gate.ResultReads != 1 || !m_consumptionAttempted[i] || m_results[i] != (typed ? 42 : 1))
                        {
                            throw new InvalidOperationException("Builder-cycle callback/result/count validation failed.");
                        }
                        sample.registrations += gate.Registrations;
                        sample.callbacks += gate.Callbacks;
                        sample.completions += gate.Completions;
                        sample.gateResultReads += gate.ResultReads;
                        sample.outputConsumptions++;
                        sample.checksum += m_results[i];
                    }
                    sample.expectedChecksum = k_concurrency * (typed ? 42 : 1);
                    sample.validated = sample.checksum == sample.expectedChecksum && m_scheduled == k_concurrency;
                    if (!sample.validated)
                    {
                        throw new InvalidOperationException("Builder-cycle checksum or scheduled count failed.");
                    }
                    SetNanoseconds(sample);
                }
                catch (Exception exception)
                {
                    failure = exception;
                    throw;
                }
                finally
                {
                    Exception cleanupFailure = Cleanup(onity, typed);
                    if (failure == null && cleanupFailure != null)
                    {
                        throw cleanupFailure;
                    }
                }
            }

            private void ScheduleOnityUntyped()
            {
                for (int i = 0; i < k_concurrency; i++)
                {
                    m_onity[i] = OnityUntyped(m_gates[i]);
                    m_scheduled++;
                }
            }

            private void ScheduleOnityTyped()
            {
                for (int i = 0; i < k_concurrency; i++)
                {
                    m_onityTyped[i] = OnityTyped(m_gates[i]);
                    m_scheduled++;
                }
            }

            private void ScheduleUniUntyped()
            {
                for (int i = 0; i < k_concurrency; i++)
                {
                    m_uni[i] = UniUntyped(m_gates[i]);
                    m_scheduled++;
                }
            }

            private void ScheduleUniTyped()
            {
                for (int i = 0; i < k_concurrency; i++)
                {
                    m_uniTyped[i] = UniTyped(m_gates[i]);
                    m_scheduled++;
                }
            }

            private void ConsumeOnityUntyped()
            {
                for (int i = 0; i < m_scheduled; i++)
                {
                    m_consumptionAttempted[i] = true;
                    m_onity[i].GetAwaiter().GetResult();
                    m_results[i] = 1;
                }
            }

            private void ConsumeOnityTyped()
            {
                for (int i = 0; i < m_scheduled; i++)
                {
                    m_consumptionAttempted[i] = true;
                    m_results[i] = m_onityTyped[i].GetAwaiter().GetResult();
                }
            }

            private void ConsumeUniUntyped()
            {
                for (int i = 0; i < m_scheduled; i++)
                {
                    m_consumptionAttempted[i] = true;
                    m_uni[i].GetAwaiter().GetResult();
                    m_results[i] = 1;
                }
            }

            private void ConsumeUniTyped()
            {
                for (int i = 0; i < m_scheduled; i++)
                {
                    m_consumptionAttempted[i] = true;
                    m_results[i] = m_uniTyped[i].GetAwaiter().GetResult();
                }
            }

            private Exception Cleanup(bool onity, bool typed)
            {
                Exception failure = null;
                try
                {
                    // Finish all registered gates even if scheduling or a previous continuation failed.
                    for (int i = 0; i < k_concurrency; i++)
                    {
                        try
                        {
                            if (m_gates[i].Registrations == 1 && m_gates[i].Completions == 0)
                            {
                                m_gates[i].Complete();
                            }
                        }
                        catch (Exception exception)
                        {
                            failure = failure ?? exception;
                        }
                    }
                    for (int i = 0; i < m_scheduled; i++)
                    {
                        if (m_consumptionAttempted[i])
                        {
                            continue;
                        }
                        // GetResult may retire a faulted source before throwing; never retry the same handle.
                        m_consumptionAttempted[i] = true;
                        try
                        {
                            if (onity && typed)
                            {
                                m_onityTyped[i].GetAwaiter().GetResult();
                            }
                            else if (onity)
                            {
                                m_onity[i].GetAwaiter().GetResult();
                            }
                            else if (typed)
                            {
                                m_uniTyped[i].GetAwaiter().GetResult();
                            }
                            else
                            {
                                m_uni[i].GetAwaiter().GetResult();
                            }
                        }
                        catch (Exception exception)
                        {
                            failure = failure ?? exception;
                        }
                    }
                }
                finally
                {
                    Array.Clear(m_onity, 0, k_concurrency);
                    Array.Clear(m_onityTyped, 0, k_concurrency);
                    Array.Clear(m_uni, 0, k_concurrency);
                    Array.Clear(m_uniTyped, 0, k_concurrency);
                }
                return failure;
            }

            private bool IsCompleted(int index, bool onity, bool typed)
            {
                return onity
                    ? typed ? m_onityTyped[index].IsCompleted : m_onity[index].IsCompleted
                    : typed ? m_uniTyped[index].Status.IsCompleted() : m_uni[index].Status.IsCompleted();
            }

            private static async OnityTask OnityUntyped(ManualGate gate)
            {
                await gate;
            }

            private static async OnityTask<int> OnityTyped(ManualGate gate)
            {
                await gate;
                return 42;
            }

            private static async UniTask UniUntyped(ManualGate gate)
            {
                await gate;
            }

            private static async UniTask<int> UniTyped(ManualGate gate)
            {
                await gate;
                return 42;
            }
        }

        private sealed class ManualGate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            private bool m_isCompleted;
            internal int Registrations { get; private set; }
            internal int Callbacks { get; private set; }
            internal int Completions { get; private set; }
            internal int ResultReads { get; private set; }
            public bool IsCompleted => m_isCompleted;

            public ManualGate GetAwaiter() => this;

            public void GetResult()
            {
                if (!m_isCompleted)
                {
                    throw new InvalidOperationException("Manual gate was consumed before completion.");
                }
                ResultReads++;
            }

            public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

            public void UnsafeOnCompleted(Action continuation)
            {
                if (m_continuation != null || m_isCompleted || continuation == null)
                {
                    throw new InvalidOperationException("Invalid manual-gate continuation registration.");
                }
                m_continuation = continuation;
                Registrations++;
            }

            internal void Reset()
            {
                m_continuation = null;
                m_isCompleted = false;
                Registrations = 0;
                Callbacks = 0;
                Completions = 0;
                ResultReads = 0;
            }

            internal void Complete()
            {
                if (m_isCompleted || m_continuation == null)
                {
                    throw new InvalidOperationException("Manual gate has no pending continuation.");
                }
                m_isCompleted = true;
                Completions++;
                Action continuation = m_continuation;
                m_continuation = null;
                Callbacks++;
                continuation();
            }
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string title = "Manual-awaitable logical builder cycle";
            public string measurementScope = "Logical builder cycle: schedule, manual-gate completion, and single native output consumption. "
                + "Excludes Unity PlayerLoop waits and IL2CPP deferred pool-return/drain CPU; common harness overhead included. "
                + "Total is the sum of three measured windows, not the full physical lifecycle.";
            public string controlScope = "Common preallocated array write/increment/checksum loops with identical Stopwatch brackets; unsubtracted.";
            public string drainEvidence = "Two real Unity frames after every warmup and measured library batch; frameCount delta checked >= 2.";
            public string order = "Library order alternates by measured sample index and warmup index.";
            public int concurrency = k_concurrency;
            public int operationsPerBatch = k_concurrency;
            public int suspensionsPerOperation = 1;
            public int warmupBatchesPerLibraryPerScenario = k_warmups;
            public int measuredBatchesPerSample = 1;
            public int samplesPerLibraryPerScenario = k_samples;
            public long stopwatchFrequency = Stopwatch.Frequency;
            public string generatedAtUtc;
            public bool completed;
            public string failure;
            public bool isEditor;
            public string platform;
            public OnityTaskBenchmarkEnvironment environment;
            public Metric[] metrics = new Metric[8];
            public ControlSet[] controls = new ControlSet[4];
        }

        [Serializable]
        private sealed class Metric
        {
            public string library;
            public string shape;
            public bool onityFlowExecutionContext;
            public string libraryFlowSemantics;
            public OnityTaskBenchmarkEnvironment environment;
            public double meanScheduleNanosecondsPerOperation;
            public double meanCompletionNanosecondsPerOperation;
            public double meanConsumptionNanosecondsPerOperation;
            public double meanTotalNanosecondsPerOperation;
            public Sample[] warmups;
            public Sample[] samples;
        }

        [Serializable]
        private class Timing
        {
            public int index;
            public long scheduleTicks;
            public long completionTicks;
            public long consumptionTicks;
            public long totalTicks;
            public double scheduleNanosecondsPerOperation;
            public double completionNanosecondsPerOperation;
            public double consumptionNanosecondsPerOperation;
            public double totalNanosecondsPerOperation;
            public long checksum;
        }

        [Serializable]
        private sealed class Sample : Timing
        {
            public int libraryOrder;
            public int registrations;
            public int callbacks;
            public int completions;
            public int gateResultReads;
            public int outputConsumptions;
            public long expectedChecksum;
            public bool validated;
            public int drainStartFrame;
            public int drainEndFrame;
            public int drainFrames;
        }

        [Serializable]
        private sealed class Control : Timing
        {
        }

        [Serializable]
        private sealed class ControlSet
        {
            public bool onityFlowExecutionContext;
            public string shape;
            public Control[] samples;
        }
    }
}
