using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>Measures pending typed WhenAll construction against its original Task bridge path.</summary>
    public sealed class OnityTaskWhenAllBenchmarkRunner : MonoBehaviour
    {
        private const int k_cohort = 128;
        private const int k_samples = 8;
        private const int k_runs = 2;
        private const int k_warmups = 3;

        private OnityTaskCompletionSource<int>[][] m_sources;
        private OnityTask<int>[][] m_inputs;
        private OnityTask<int[]>[] m_outputs;
        private int m_constructed;
        private OnityBenchmarkAllocationCounter m_counter;
        private string m_path;
        private Action<string, Exception> m_completed;
        private bool m_hasSettings;
        private bool m_oldFlow;
        private bool m_oldTracker;
        private bool m_oldStackTrace;
        private int m_oldCapacity;

        /// <summary>Starts the standalone construction suite and reports completion after cleanup.</summary>
        /// <param name="path">Output JSON path.</param>
        /// <param name="completed">Receives the report path and any failure.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("WhenAll benchmark output path is required.", nameof(path));
            }

            GameObject owner = new GameObject("Onity WhenAll Benchmark Runner");
            DontDestroyOnLoad(owner);
            OnityTaskWhenAllBenchmarkRunner runner = owner.AddComponent<OnityTaskWhenAllBenchmarkRunner>();
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
                OnityTask.FlowExecutionContext = true;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTask.RunnerPoolCapacity = 128;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                report.isEditor = Application.isEditor;
                report.platform = Application.platform.ToString();
                if (!report.isEditor && report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("WhenAll benchmark requires a non-development Release Player.");
                }
                if (report.environment.executionContextPath == "Unknown")
                {
                    throw new InvalidOperationException("WhenAll benchmark requires runtime context-path evidence.");
                }

                m_counter = OnityBenchmarkAllocationCounter.Create(true, !Application.isEditor);
                report.allocationCounter = m_counter.Description;
                report.allocationCounterKind = m_counter.Kind;
                report.counterCalibrated = m_counter.IsAvailable;
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
            int[] counts = { 2, 8, 16 };
            for (int size = 0; size < counts.Length; size++)
            {
                for (int library = 0; library < 2; library++)
                {
                    Metric metric = report.metrics[size * 2 + library];
                    metric.path = library == 0 ? "Native pending WhenAll" : "Original Task bridge WhenAll";
                    metric.inputCount = counts[size];
                }

                for (int warmup = 0; warmup < k_warmups; warmup++)
                {
                    for (int turn = 0; turn < 2; turn++)
                    {
                        int library = (warmup + turn) & 1;
                        ConstructCohort(counts[size], library, false, null, 0);
                        report.metrics[size * 2 + library].validatedWarmupAggregates += k_cohort;
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
                            Metric metric = report.metrics[size * 2 + library];
                            ConstructCohort(counts[size], library, true, metric, sample);
                            metric.validatedMeasuredAggregates += k_cohort;
                            yield return null;
                            yield return null;
                        }
                    }
                }

                for (int library = 0; library < 2; library++)
                {
                    Summarize(report.metrics[size * 2 + library]);
                }
            }
        }

        private void ConstructCohort(int inputCount, int library, bool measured, Metric metric, int sample)
        {
            Prepare(inputCount);
            bool sliceOpen = false;
            long bytes = 0;
            long started = 0;
            long stopped = 0;
            long allocated = -1;
            bool allocationValid = false;
            try
            {
                if (measured)
                {
                    bytes = m_counter.Begin();
                    sliceOpen = true;
                    started = Stopwatch.GetTimestamp();
                }

                for (int aggregate = 0; aggregate < k_cohort; aggregate++)
                {
                    OnityTask<int>[] inputs = m_inputs[aggregate];
                    if (library == 0)
                    {
                        m_outputs[aggregate] = OnityTask.WhenAll(inputs);
                    }
                    else
                    {
                        // Exact original typed path, including its original tracker label.
                        Task<int>[] taskArray = new Task<int>[inputs.Length];
                        for (int i = 0; i < inputs.Length; i++)
                        {
                            taskArray[i] = inputs[i].AsTask();
                        }

                        m_outputs[aggregate] = OnityTask<int[]>.FromTask(OnityAsync.WhenAll<int>(taskArray));
                    }

                    m_constructed++;
                }

                if (measured)
                {
                    stopped = Stopwatch.GetTimestamp();
                    allocated = m_counter.End(bytes, out allocationValid);
                    sliceOpen = false;
                    allocationValid &= m_counter.IsAvailable && allocated >= 0;
                }
            }
            finally
            {
                if (sliceOpen)
                {
                    m_counter.EndSlice();
                }
            }

            // Both completion paths and all result checking are outside allocation/timing.
            CompleteAndConsume(true);
            if (measured)
            {
                metric.sampleMilliseconds[sample] += (stopped - started) * 1000d / Stopwatch.Frequency;
                metric.sampleAllocatedBytes[sample] += m_counter.IsAvailable ? allocated : 0;
                metric.sampleAllocationValid[sample] &= allocationValid;
            }
        }

        private void Prepare(int inputCount)
        {
            // Clear prior work before replacing its retained output holders.
            CompleteAndConsume(false);
            m_constructed = 0;
            m_sources = new OnityTaskCompletionSource<int>[k_cohort][];
            m_inputs = new OnityTask<int>[k_cohort][];
            m_outputs = new OnityTask<int[]>[k_cohort];
            for (int aggregate = 0; aggregate < k_cohort; aggregate++)
            {
                m_sources[aggregate] = new OnityTaskCompletionSource<int>[inputCount];
                m_inputs[aggregate] = new OnityTask<int>[inputCount];
                for (int i = 0; i < inputCount; i++)
                {
                    OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                    m_sources[aggregate][i] = source;
                    m_inputs[aggregate][i] = source.Task;
                }
            }
        }

        private void CompleteAndConsume(bool validate)
        {
            if (m_sources == null)
            {
                return;
            }

            for (int aggregate = m_sources.Length - 1; aggregate >= 0; aggregate--)
            {
                OnityTaskCompletionSource<int>[] sources = m_sources[aggregate];
                if (sources == null)
                {
                    continue;
                }
                for (int i = sources.Length - 1; i >= 0; i--)
                {
                    sources[i]?.TrySetResult(aggregate * 100 + i);
                }
            }

            // Original input Task bridges dispatch framework continuations on workers.
            // No consumer/context callback is registered by this harness, so blocking
            // here cannot require a Unity dispatcher; it is outside every measured slice.
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 30;
            for (int aggregate = 0; aggregate < m_constructed; aggregate++)
            {
                while (!m_outputs[aggregate].IsCompleted)
                {
                    if (Stopwatch.GetTimestamp() >= deadline)
                    {
                        for (int pending = 0; pending < m_constructed; pending++)
                        {
                            m_outputs[pending].AsTask().ContinueWith(
                                ObserveFault, CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default);
                        }

                        ClearCohort();
                        throw new TimeoutException("WhenAll cohort did not drain within 30 seconds.");
                    }

                    Thread.Sleep(1);
                }
            }

            Exception failure = null;
            for (int aggregate = 0; aggregate < m_constructed; aggregate++)
            {
                try
                {
                    int[] results = m_outputs[aggregate].GetAwaiter().GetResult();
                    if (validate)
                    {
                        if (results.Length != m_sources[aggregate].Length)
                        {
                            throw new InvalidOperationException("WhenAll result length mismatch.");
                        }
                        for (int i = 0; i < results.Length; i++)
                        {
                            if (results[i] != aggregate * 100 + i)
                            {
                                throw new InvalidOperationException("WhenAll result order/value mismatch.");
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    failure = failure ?? exception;
                }
            }

            ClearCohort();
            if (failure != null)
            {
                throw new InvalidOperationException("WhenAll cohort validation/observation failed.", failure);
            }
        }

        private static void ObserveFault(Task<int[]> task)
        {
            _ = task.Exception;
        }

        private void ClearCohort()
        {
            m_sources = null;
            m_inputs = null;
            m_outputs = null;
            m_constructed = 0;
        }

        private void Summarize(Metric metric)
        {
            if (metric.validatedWarmupAggregates != k_cohort * k_warmups
                || metric.validatedMeasuredAggregates != k_cohort * k_samples * k_runs)
            {
                throw new InvalidOperationException("WhenAll validated cohort counts are incomplete.");
            }

            double total = 0;
            long allocated = 0;
            for (int i = 0; i < k_samples; i++)
            {
                total += metric.sampleMilliseconds[i];
                if (metric.sampleAllocationValid[i])
                {
                    metric.validAllocationSamples++;
                    allocated += metric.sampleAllocatedBytes[i];
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
            metric.nanosecondsPerAggregate = metric.meanMilliseconds * 1000000d / (k_cohort * k_runs);
            metric.allocationAvailable = m_counter.IsAvailable && metric.validAllocationSamples > 0;
            metric.allocatedBytesPerAggregate = metric.allocationAvailable
                ? (double)allocated / (metric.validAllocationSamples * k_cohort * k_runs) : -1;
            metric.allocationUnavailableReason = metric.allocationAvailable ? null
                : !m_counter.IsAvailable ? m_counter.Description : "No allocation sample survived collection/delta validity checks.";
        }

        private void Cleanup()
        {
            try
            {
                CompleteAndConsume(false);
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

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string suite = "whenall";
            public bool completed;
            public string failure;
            public string generatedAtUtc;
            public bool isEditor;
            public string platform;
            public OnityTaskBenchmarkEnvironment environment;
            public int aggregatesPerCohort = k_cohort;
            public int warmupCohortsPerPath = k_warmups;
            public int samples = k_samples;
            public int runsPerSample = k_runs;
            public string scope = "Construction only: 128 pending aggregate factories; sources, inputs and output holders prepared before slice. "
                + "Completion in reverse order, bounded drain, ordered result validation and two drain frames excluded. "
                + "New result arrays allocate during construction; old result arrays can allocate during completion. "
                + "These bytes are not full-lifecycle allocation. No runtime private-field introspection.";
            public bool counterCalibrated;
            public string allocationCounterKind;
            public string allocationCounter;
            public long calibrationBytes;
            public long emptyControlBytes;
            public string allocationRejectionReasons;
            public Metric[] metrics = { new Metric(), new Metric(), new Metric(), new Metric(), new Metric(), new Metric() };
        }

        [Serializable]
        private sealed class Metric
        {
            public string path;
            public int inputCount;
            public int validatedWarmupAggregates;
            public int validatedMeasuredAggregates;
            public double meanMilliseconds;
            public double medianMilliseconds;
            public double nanosecondsPerAggregate;
            public bool allocationAvailable;
            public string allocationUnavailableReason;
            public int validAllocationSamples;
            public double allocatedBytesPerAggregate = -1;
            public double[] sampleMilliseconds = new double[k_samples];
            public long[] sampleAllocatedBytes = new long[k_samples];
            public bool[] sampleAllocationValid = { true, true, true, true, true, true, true, true };
        }
    }
}
