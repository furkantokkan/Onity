using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>Measures array WhenAny with fresh shareable-success producer groups.</summary>
    public sealed class OnityTaskWhenAnyBenchmarkRunner : MonoBehaviour
    {
        private const int k_cohortSize = 128;
        private const int k_samples = 8;
        private const int k_runs = 2;
        private const int k_warmups = 3;
        private Cohort m_cohort;
        private OnityBenchmarkAllocationCounter m_counter;
        private string m_path;
        private Action<string, Exception> m_completed;
        private bool m_hasSettings;
        private bool m_oldFlow;
        private bool m_oldTracker;
        private bool m_oldStackTrace;
        private int m_oldCapacity;

        /// <summary>Runs the standalone synthetic composition suite and writes its JSON report.</summary>
        /// <param name="path">Output JSON path.</param>
        /// <param name="completed">Receives the report path and any failure after cleanup.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("WhenAny benchmark output path is required.", nameof(path));
            }

            GameObject owner = new GameObject("Onity WhenAny Benchmark Runner");
            DontDestroyOnLoad(owner);
            OnityTaskWhenAnyBenchmarkRunner runner = owner.AddComponent<OnityTaskWhenAnyBenchmarkRunner>();
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
                OnityTask.FlowExecutionContext = false; // library default; restored with the other settings
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTask.RunnerPoolCapacity = 128;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                report.isEditor = Application.isEditor;
                report.platform = Application.platform.ToString();
                if (report.isEditor || report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("WhenAny benchmark requires a non-development Release Player.");
                }
                if (report.environment.executionContextPath == "Unknown")
                {
                    throw new InvalidOperationException("WhenAny benchmark requires runtime context-path evidence.");
                }

                m_counter = OnityBenchmarkAllocationCounter.Create(true, true);
                report.counterCalibrated = m_counter.IsAvailable;
                report.allocationCounterKind = m_counter.Kind;
                report.allocationCounter = m_counter.Description;
                report.calibrationBytes = m_counter.CalibrationBytes;
                report.emptyControlBytes = m_counter.EmptyDeltaBytes;
                report.allocationRejectionReasons = m_counter.RejectedCandidates;
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
                    Cleanup();
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
            int[] counts = { 2, 16, 32 };
            for (int shape = 0; shape < 2; shape++)
            {
                for (int size = 0; size < counts.Length; size++)
                {
                    int row = (shape * counts.Length + size) * 2;
                    for (int library = 0; library < 2; library++)
                    {
                        Metric metric = report.metrics[row + library];
                        metric.library = library == 0 ? "OnityTask" : "UniTask";
                        metric.shape = shape == 0 ? "Untyped" : "Typed int";
                        metric.inputCount = counts[size];
                    }

                    for (int warmup = 0; warmup < k_warmups; warmup++)
                    {
                        for (int turn = 0; turn < 2; turn++)
                        {
                            int library = (warmup + turn) & 1;
                            MeasureCohort(counts[size], shape == 1, library == 0, null, 0);
                            report.metrics[row + library].validatedWarmupGroups += k_cohortSize;
                            yield return null;
                            yield return null;
                        }
                    }

                    for (int sample = 0; sample < k_samples; sample++)
                    {
                        for (int run = 0; run < k_runs; run++)
                        {
                            for (int turn = 0; turn < 2; turn++)
                            {
                                int library = (sample + run + turn) & 1;
                                Metric metric = report.metrics[row + library];
                                MeasureCohort(counts[size], shape == 1, library == 0, metric, sample);
                                metric.validatedMeasuredGroups += k_cohortSize;
                                yield return null;
                                yield return null;
                            }
                        }
                    }

                    Summarize(report.metrics[row]);
                    Summarize(report.metrics[row + 1]);
                }
            }
        }

        private void MeasureCohort(int count, bool typed, bool onity, Metric metric, int sample)
        {
            // Assign before allocation so partial preparation remains reachable for cleanup.
            m_cohort = new Cohort(count, typed, onity);
            m_cohort.Prepare();
            bool measured = metric != null;
            bool sliceOpen = false;
            long bytes = 0;
            long started = 0;
            long stopped = 0;
            long allocated = -1;
            bool valid = false;
            try
            {
                if (measured)
                {
                    bytes = m_counter.Begin();
                    sliceOpen = true;
                    started = Stopwatch.GetTimestamp();
                }

                m_cohort.Construct();
                m_cohort.CompleteProducers();
                m_cohort.Consume();

                if (measured)
                {
                    stopped = Stopwatch.GetTimestamp();
                    allocated = m_counter.End(bytes, out valid);
                    sliceOpen = false;
                    valid &= m_counter.IsAvailable && allocated >= 0;
                }
            }
            finally
            {
                if (sliceOpen)
                {
                    m_counter.EndSlice();
                }
            }

            m_cohort.Validate();
            m_cohort.Cleanup();
            m_cohort = null;
            if (measured)
            {
                metric.sampleMilliseconds[sample] += (stopped - started) * 1000d / Stopwatch.Frequency;
                metric.sampleAllocatedBytes[sample] += m_counter.IsAvailable ? allocated : 0;
                metric.sampleAllocationValid[sample] &= valid;
            }
        }

        private void Summarize(Metric metric)
        {
            if (metric.validatedWarmupGroups != k_cohortSize * k_warmups
                || metric.validatedMeasuredGroups != k_cohortSize * k_samples * k_runs)
            {
                throw new InvalidOperationException("WhenAny validated cohort counts are incomplete.");
            }

            double total = 0;
            long allocated = 0;
            for (int i = 0; i < k_samples; i++)
            {
                total += metric.sampleMilliseconds[i];
                if (metric.sampleAllocationValid[i])
                {
                    allocated += metric.sampleAllocatedBytes[i];
                    metric.validAllocationSamples++;
                }
                if (!m_counter.IsAvailable)
                {
                    metric.sampleAllocatedBytes[i] = -1;
                }
            }

            metric.meanMilliseconds = total / k_samples;
            double[] sorted = (double[])metric.sampleMilliseconds.Clone();
            Array.Sort(sorted);
            metric.medianMilliseconds = (sorted[3] + sorted[4]) / 2;
            metric.nanosecondsPerGroup = metric.meanMilliseconds * 1000000d / (k_cohortSize * k_runs);
            metric.allocationAvailable = m_counter.IsAvailable && metric.validAllocationSamples > 0;
            metric.allocatedBytesPerGroup = metric.allocationAvailable
                ? (double)allocated / (metric.validAllocationSamples * k_cohortSize * k_runs) : -1;
            metric.allocationUnavailableReason = metric.allocationAvailable ? null
                : !m_counter.IsAvailable ? m_counter.Description : "No allocation sample survived collection/delta validity checks.";
        }

        private void Cleanup()
        {
            try
            {
                m_cohort?.Cleanup();
                m_cohort = null;
            }
            finally
            {
                m_counter?.Dispose();
                m_counter = null;
            }
        }

        private void OnDestroy()
        {
            try
            {
                Cleanup();
            }
            finally
            {
                RestoreSettings();
            }
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

        private sealed class Cohort
        {
            private static readonly Action<Exception> s_observeFault = exception => Console.Error.WriteLine(exception);
            private readonly int m_count;
            private readonly bool m_typed;
            private readonly bool m_onity;
            private OnityTaskCompletionSource[][] m_onitySources;
            private OnityTaskCompletionSource<int>[][] m_onityTypedSources;
            private UniTaskCompletionSource[][] m_uniSources;
            private UniTaskCompletionSource<int>[][] m_uniTypedSources;
            private OnityTask[][] m_onityInputs;
            private OnityTask<int>[][] m_onityTypedInputs;
            private UniTask[][] m_uniInputs;
            private UniTask<int>[][] m_uniTypedInputs;
            private OnityTask<int>[] m_onityOutputs;
            private OnityTask<(int winnerIndex, int result)>[] m_onityTypedOutputs;
            private UniTask<int>[] m_uniOutputs;
            private UniTask<(int winnerIndex, int result)>[] m_uniTypedOutputs;
            private bool[] m_consumed;
            private int[] m_winners;
            private int[] m_values;
            private int m_constructed;
            private bool m_producersCompleted;

            internal Cohort(int count, bool typed, bool onity)
            {
                m_count = count;
                m_typed = typed;
                m_onity = onity;
            }

            internal void Prepare()
            {
                m_consumed = new bool[k_cohortSize];
                m_winners = new int[k_cohortSize];
                m_values = new int[k_cohortSize];
                if (m_onity && m_typed)
                {
                    m_onityTypedSources = new OnityTaskCompletionSource<int>[k_cohortSize][];
                    m_onityTypedInputs = new OnityTask<int>[k_cohortSize][];
                    m_onityTypedOutputs = new OnityTask<(int, int)>[k_cohortSize];
                }
                else if (m_onity)
                {
                    m_onitySources = new OnityTaskCompletionSource[k_cohortSize][];
                    m_onityInputs = new OnityTask[k_cohortSize][];
                    m_onityOutputs = new OnityTask<int>[k_cohortSize];
                }
                else if (m_typed)
                {
                    m_uniTypedSources = new UniTaskCompletionSource<int>[k_cohortSize][];
                    m_uniTypedInputs = new UniTask<int>[k_cohortSize][];
                    m_uniTypedOutputs = new UniTask<(int, int)>[k_cohortSize];
                }
                else
                {
                    m_uniSources = new UniTaskCompletionSource[k_cohortSize][];
                    m_uniInputs = new UniTask[k_cohortSize][];
                    m_uniOutputs = new UniTask<int>[k_cohortSize];
                }

                for (int group = 0; group < k_cohortSize; group++)
                {
                    if (m_onity && m_typed)
                    {
                        m_onityTypedSources[group] = new OnityTaskCompletionSource<int>[m_count];
                        m_onityTypedInputs[group] = new OnityTask<int>[m_count];
                    }
                    else if (m_onity)
                    {
                        m_onitySources[group] = new OnityTaskCompletionSource[m_count];
                        m_onityInputs[group] = new OnityTask[m_count];
                    }
                    else if (m_typed)
                    {
                        m_uniTypedSources[group] = new UniTaskCompletionSource<int>[m_count];
                        m_uniTypedInputs[group] = new UniTask<int>[m_count];
                    }
                    else
                    {
                        m_uniSources[group] = new UniTaskCompletionSource[m_count];
                        m_uniInputs[group] = new UniTask[m_count];
                    }

                    for (int i = 0; i < m_count; i++)
                    {
                        if (m_onity && m_typed)
                        {
                            var source = new OnityTaskCompletionSource<int>();
                            m_onityTypedSources[group][i] = source;
                            m_onityTypedInputs[group][i] = source.Task;
                        }
                        else if (m_onity)
                        {
                            var source = new OnityTaskCompletionSource();
                            m_onitySources[group][i] = source;
                            m_onityInputs[group][i] = source.Task;
                        }
                        else if (m_typed)
                        {
                            var source = new UniTaskCompletionSource<int>();
                            m_uniTypedSources[group][i] = source;
                            m_uniTypedInputs[group][i] = source.Task;
                        }
                        else
                        {
                            var source = new UniTaskCompletionSource();
                            m_uniSources[group][i] = source;
                            m_uniInputs[group][i] = source.Task;
                        }
                    }
                }
            }

            internal void Construct()
            {
                for (int group = 0; group < k_cohortSize; group++)
                {
                    if (m_onity && m_typed)
                    {
                        m_onityTypedOutputs[group] = OnityTask.WhenAny<int>(m_onityTypedInputs[group]);
                    }
                    else if (m_onity)
                    {
                        m_onityOutputs[group] = OnityTask.WhenAny(m_onityInputs[group]);
                    }
                    else if (m_typed)
                    {
                        m_uniTypedOutputs[group] = UniTask.WhenAny<int>(m_uniTypedInputs[group]);
                    }
                    else
                    {
                        m_uniOutputs[group] = UniTask.WhenAny(m_uniInputs[group]);
                    }

                    m_constructed++;
                }
            }

            internal void CompleteProducers()
            {
                if (m_producersCompleted)
                {
                    return;
                }

                for (int group = k_cohortSize - 1; group >= 0; group--)
                {
                    for (int i = m_count - 1; i >= 0; i--)
                    {
                        if (m_onity && m_typed)
                        {
                            m_onityTypedSources?[group]?[i]?.TrySetResult(group * 100 + i);
                        }
                        else if (m_onity)
                        {
                            m_onitySources?[group]?[i]?.TrySetResult();
                        }
                        else if (m_typed)
                        {
                            m_uniTypedSources?[group]?[i]?.TrySetResult(group * 100 + i);
                        }
                        else
                        {
                            m_uniSources?[group]?[i]?.TrySetResult();
                        }
                    }
                }

                m_producersCompleted = true;
            }

            private bool IsCompleted(int group)
            {
                return m_onity
                    ? m_typed ? m_onityTypedOutputs[group].IsCompleted : m_onityOutputs[group].IsCompleted
                    : m_typed ? m_uniTypedOutputs[group].Status.IsCompleted() : m_uniOutputs[group].Status.IsCompleted();
            }

            internal void Consume()
            {
                for (int group = 0; group < m_constructed; group++)
                {
                    if (!IsCompleted(group))
                    {
                        throw new InvalidOperationException("WhenAny did not complete synchronously with its producers.");
                    }

                    ConsumeOne(group);
                }
            }

            private void ConsumeOne(int group)
            {
                if (m_consumed[group])
                {
                    return;
                }

                m_consumed[group] = true;
                if (m_onity && m_typed)
                {
                    (m_winners[group], m_values[group]) = m_onityTypedOutputs[group].GetAwaiter().GetResult();
                }
                else if (m_onity)
                {
                    m_winners[group] = m_onityOutputs[group].GetAwaiter().GetResult();
                }
                else if (m_typed)
                {
                    (m_winners[group], m_values[group]) = m_uniTypedOutputs[group].GetAwaiter().GetResult();
                }
                else
                {
                    m_winners[group] = m_uniOutputs[group].GetAwaiter().GetResult();
                }
            }

            internal void Validate()
            {
                if (m_constructed != k_cohortSize)
                {
                    throw new InvalidOperationException("WhenAny construction count is incomplete.");
                }
                for (int group = 0; group < m_constructed; group++)
                {
                    if (!m_consumed[group] || m_winners[group] != m_count - 1
                        || (m_typed && m_values[group] != group * 100 + m_count - 1))
                    {
                        throw new InvalidOperationException("WhenAny winner/result mismatch at group " + group + ".");
                    }
                }
            }

            internal void Cleanup()
            {
                CompleteProducers();
                Exception failure = null;
                for (int group = 0; group < m_constructed; group++)
                {
                    if (m_consumed[group])
                    {
                        continue;
                    }

                    try
                    {
                        if (IsCompleted(group))
                        {
                            ConsumeOne(group);
                        }
                        else
                        {
                            // Unexpected pending output: retain exactly one fault observer,
                            // without waiting for a Unity context during failure shutdown.
                            m_consumed[group] = true;
                            if (m_onity && m_typed)
                            {
                                m_onityTypedOutputs[group].Forget(s_observeFault);
                            }
                            else if (m_onity)
                            {
                                m_onityOutputs[group].Forget(s_observeFault);
                            }
                            else if (m_typed)
                            {
                                m_uniTypedOutputs[group].Forget(s_observeFault, false);
                            }
                            else
                            {
                                m_uniOutputs[group].Forget(s_observeFault, false);
                            }

                            failure = failure ?? new InvalidOperationException("WhenAny cleanup retained a pending output observer.");
                        }
                    }
                    catch (Exception exception)
                    {
                        failure = failure ?? exception;
                    }
                }

                if (failure != null)
                {
                    throw new InvalidOperationException("WhenAny output cleanup failed.", failure);
                }
            }
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string suite = "whenany";
            public bool completed;
            public string failure;
            public string generatedAtUtc;
            public bool isEditor;
            public string platform;
            public OnityTaskBenchmarkEnvironment environment;
            public int groupsPerCohort = k_cohortSize;
            public int warmupCohortsPerLibrary = k_warmups;
            public int samples = k_samples;
            public int cohortsPerSample = k_runs;
            public string scope = "128 independent pending shareable-success groups: construct all array WhenAny outputs, "
                + "complete ALL producers synchronously in reverse order, then GetResult/store each winner once inside the slice. "
                + "Source/input/output/winner holders prepared before the slice; result assertions and two drain frames excluded. "
                + "Includes public source completion and loser observation, not isolated scheduler CPU or caller input allocation. "
                + "Does not establish native single-consumer, cancellation or general performance superiority.";
            public string uniTaskTracking = "Pinned UniTask tracker calls are conditional UNITY_EDITOR and absent in this Player.";
            public bool counterCalibrated;
            public string allocationCounterKind;
            public string allocationCounter;
            public long calibrationBytes;
            public long emptyControlBytes;
            public string allocationRejectionReasons;
            public Metric[] metrics =
            {
                new Metric(), new Metric(), new Metric(), new Metric(), new Metric(), new Metric(),
                new Metric(), new Metric(), new Metric(), new Metric(), new Metric(), new Metric()
            };
        }

        [Serializable]
        private sealed class Metric
        {
            public string library;
            public string shape;
            public int inputCount;
            public int validatedWarmupGroups;
            public int validatedMeasuredGroups;
            public double meanMilliseconds;
            public double medianMilliseconds;
            public double nanosecondsPerGroup;
            public bool allocationAvailable;
            public string allocationUnavailableReason;
            public int validAllocationSamples;
            public double allocatedBytesPerGroup = -1;
            public double[] sampleMilliseconds = new double[k_samples];
            public long[] sampleAllocatedBytes = new long[k_samples];
            public bool[] sampleAllocationValid = { true, true, true, true, true, true, true, true };
        }
    }
}
