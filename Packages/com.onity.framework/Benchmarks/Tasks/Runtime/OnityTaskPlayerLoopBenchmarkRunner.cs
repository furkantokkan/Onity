using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Scripting;

namespace Onity.Benchmarks
{
    /// <summary>Brackets the actual Onity Update drain with warmed native Yield cohorts.</summary>
    public sealed class OnityTaskPlayerLoopBenchmarkRunner : MonoBehaviour
    {
        private const int k_cohortSize = 128;
        private const int k_warmups = 3;
        private const int k_samples = 8;
        private const int k_runs = 2;
        private const int k_positiveBytes = 65536;
        private const string k_updateMarker = "Onity.Unity.Async.OnityTaskPlayerLoop+UpdateMarker";
        private sealed class BeforeMarker
        {
        }

        private sealed class AfterMarker
        {
        }

        private enum WindowKind
        {
            Work,
            Empty,
            Positive
        }
        private static OnityTaskPlayerLoopBenchmarkRunner s_active;
        private static readonly PlayerLoopSystem.UpdateFunction s_before = Before;
        private static readonly PlayerLoopSystem.UpdateFunction s_after = After;

        private readonly Holder[] m_holders = new Holder[k_cohortSize];
        private OnityBenchmarkAllocationCounter m_counter;
        private string m_path;
        private Action<string, Exception> m_completed;
        private Exception m_failure;
        private WindowKind m_kind;
        private bool m_armed;
        private bool m_open;
        private bool m_done;
        private bool m_hasSettings;
        private bool m_oldFlow;
        private bool m_oldTracker;
        private bool m_oldStackTrace;
        private int m_oldCapacity;
        private GarbageCollector.Mode m_oldGcMode;
        private byte[] m_positiveControl;
        private int m_consumed;
        private int m_startFrame;
        private long m_startBytes;
        private long m_startTicks;
        private Window m_last;

        /// <summary>Runs the standalone Release Player timing allocation suite.</summary>
        /// <param name="path">JSON output path.</param>
        /// <param name="completed">Called after cleanup with the report path and any failure.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Timing benchmark output path is required.", nameof(path));
            }
            if (s_active != null)
            {
                throw new InvalidOperationException("A timing benchmark is already running.");
            }

            GameObject owner = new GameObject("Onity Timing Benchmark Runner");
            DontDestroyOnLoad(owner);
            var runner = owner.AddComponent<OnityTaskPlayerLoopBenchmarkRunner>();
            runner.m_path = Path.GetFullPath(path);
            runner.m_completed = completed;
            s_active = runner;
        }

        private IEnumerator Start()
        {
            Report report = new Report();
            m_oldFlow = OnityTask.FlowExecutionContext;
            m_oldTracker = OnityTaskTracker.IsEnabled;
            m_oldStackTrace = OnityTaskTracker.EnableStackTrace;
            m_oldCapacity = OnityTask.RunnerPoolCapacity;
            m_oldGcMode = GarbageCollector.GCMode;
            m_hasSettings = true;
            try
            {
                OnityTask.FlowExecutionContext = false; // library default; restored with the other settings
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTask.RunnerPoolCapacity = k_cohortSize;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                if (Application.isEditor || report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Timing benchmark requires a non-development Release Player.");
                }
                for (int i = 0; i < m_holders.Length; i++)
                {
                    m_holders[i] = new Holder(this);
                }
                m_counter = OnityBenchmarkAllocationCounter.Create(true, true);
                RestoreCollector();
                report.counterKind = m_counter.Kind;
                report.counterDescription = m_counter.Description;
                report.counterRejections = m_counter.RejectedCandidates;
                report.counterCalibrationBytes = m_counter.CalibrationBytes;
                report.counterEmptyBytes = m_counter.EmptyDeltaBytes;
                report.counterCalibrated = m_counter.IsAvailable;
                report.originalCollectorMode = m_oldGcMode.ToString();
                OnityTaskPlayerLoop.Initialize();
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                RemoveMarkers(ref loop);
                if (InsertBracket(ref loop) != 1)
                {
                    throw new InvalidOperationException("Timing benchmark requires exactly one actual Onity Update marker.");
                }
                PlayerLoop.SetPlayerLoop(loop);
            }
            catch (Exception exception)
            {
                m_failure = exception;
            }

            IEnumerator measurements = Measure(report);
            try
            {
                while (m_failure == null)
                {
                    bool next;
                    try
                    {
                        next = measurements.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        m_failure = exception;
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
                (measurements as IDisposable)?.Dispose();
                try
                {
                    Cleanup();
                }
                catch (Exception exception)
                {
                    m_failure = m_failure ?? exception;
                }
                finally
                {
                    RestoreSettings();
                }
            }

            report.completed = m_failure == null;
            report.failure = m_failure?.ToString();
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_path));
                File.WriteAllText(m_path, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                m_failure = m_failure ?? exception;
            }
            try
            {
                m_completed?.Invoke(m_path, m_failure);
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        private IEnumerator Measure(Report report)
        {
            for (int warmup = 0; warmup < k_warmups; warmup++)
            {
                IEnumerator window = RunWindow(WindowKind.Work);
                while (window.MoveNext())
                {
                    yield return null;
                }
                ValidateWork();
                report.validatedWarmupWaits += k_cohortSize;
            }

            IEnumerator empty = RunWindow(WindowKind.Empty);
            while (empty.MoveNext())
            {
                yield return null;
            }
            report.bracketEmptyBytes = m_last.Bytes;
            report.bracketEmptyValid = m_last.Valid;
            IEnumerator positive = RunWindow(WindowKind.Positive);
            while (positive.MoveNext())
            {
                yield return null;
            }
            report.bracketPositiveBytes = m_last.Bytes;
            report.bracketPositiveValid = m_last.Valid;
            GC.KeepAlive(m_positiveControl);
            m_positiveControl = null;
            report.bracketControlsPassed = report.counterCalibrated
                && report.bracketEmptyValid && report.bracketEmptyBytes == 0
                && report.bracketPositiveValid && report.bracketPositiveBytes >= k_positiveBytes;

            for (int sample = 0; sample < k_samples; sample++)
            {
                report.sampleAllocationValid[sample] = report.bracketControlsPassed;
                for (int run = 0; run < k_runs; run++)
                {
                    IEnumerator window = RunWindow(WindowKind.Work);
                    while (window.MoveNext())
                    {
                        yield return null;
                    }
                    ValidateWork();
                    report.validatedMeasuredWaits += k_cohortSize;
                    report.sampleMilliseconds[sample] += m_last.Milliseconds;
                    report.sampleRawBytes[sample] += m_last.Bytes;
                    report.sampleAllocationValid[sample] &= m_last.Valid;
                    report.sampleConsumed[sample] += m_last.Consumed;
                }
            }
            Summarize(report);
        }

        private IEnumerator RunWindow(WindowKind kind)
        {
            m_kind = kind;
            m_consumed = 0;
            m_done = false;
            m_last = default;
            foreach (Holder holder in m_holders)
            {
                holder.Reset();
            }
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            m_armed = true;
            while (!m_done && m_failure == null)
            {
                yield return null;
                if (Time.realtimeSinceStartupAsDouble > deadline)
                {
                    AbortWindow();
                    throw new TimeoutException("Timing benchmark bracket did not finish within 30 seconds.");
                }
            }
            if (m_failure != null)
            {
                throw new InvalidOperationException("Timing benchmark bracket failed.", m_failure);
            }
        }

        private static void Before()
        {
            if (s_active != null)
            {
                s_active.BeginWindow();
            }
        }

        private static void After()
        {
            if (s_active != null)
            {
                s_active.EndWindow();
            }
        }

        private void BeginWindow()
        {
            if (!m_armed || m_failure != null)
            {
                return;
            }
            m_armed = false;
            try
            {
                if (m_open)
                {
                    throw new InvalidOperationException("A timing bracket was left open.");
                }
                m_startFrame = Time.frameCount;
                m_open = true;
                m_startBytes = m_counter.Begin();
                m_startTicks = Stopwatch.GetTimestamp();
                if (m_kind == WindowKind.Positive)
                {
                    m_positiveControl = new byte[k_positiveBytes];
                }
                else if (m_kind == WindowKind.Work)
                {
                    for (int i = 0; i < m_holders.Length; i++)
                    {
                        Holder holder = m_holders[i];
                        holder.Task = OnityTask.Yield(OnityPlayerLoopTiming.Update, default);
                        holder.HasTask = true;
                        holder.Task.GetAwaiter().UnsafeOnCompleted(holder.Callback);
                        holder.Registered = true;
                    }
                }
            }
            catch (Exception exception)
            {
                AbortWindow();
                m_failure = exception;
                m_done = true;
            }
        }

        private void EndWindow()
        {
            if (!m_open)
            {
                return;
            }
            try
            {
                long stopped = Stopwatch.GetTimestamp();
                long bytes = m_counter.End(m_startBytes, out bool valid);
                m_open = false;
                RestoreCollector();
                m_last = new Window
                {
                    Bytes = bytes,
                    Valid = valid && m_counter.IsAvailable && bytes >= 0 && Time.frameCount == m_startFrame,
                    Milliseconds = (stopped - m_startTicks) * 1000d / Stopwatch.Frequency,
                    Consumed = m_consumed
                };
                m_done = true;
            }
            catch (Exception exception)
            {
                AbortWindow();
                m_failure = exception;
                m_done = true;
            }
        }

        private void Update()
        {
            if (m_open && Time.frameCount != m_startFrame)
            {
                AbortWindow();
                m_failure = new InvalidOperationException("The prior timing Before marker had no matching After marker.");
                m_done = true;
            }
        }

        private void AbortWindow()
        {
            m_armed = false;
            if (m_open)
            {
                m_open = false;
                try
                {
                    m_counter?.EndSlice();
                }
                finally
                {
                    RestoreCollector();
                }
            }
        }

        private void ValidateWork()
        {
            if (m_last.Consumed != k_cohortSize)
            {
                throw new InvalidOperationException("Timing window did not consume all 128 native waits.");
            }
            foreach (Holder holder in m_holders)
            {
                if (!holder.Consumed || holder.Failure != null)
                {
                    throw new InvalidOperationException("Timing native callback failed.", holder.Failure);
                }
            }
        }

        private void Summarize(Report report)
        {
            if (report.validatedWarmupWaits != k_warmups * k_cohortSize
                || report.validatedMeasuredWaits != k_samples * k_runs * k_cohortSize)
            {
                throw new InvalidOperationException("Timing benchmark validated cohort counts are incomplete.");
            }

            double time = 0;
            long bytes = 0;
            for (int i = 0; i < k_samples; i++)
            {
                time += report.sampleMilliseconds[i];
                if (report.sampleAllocationValid[i])
                {
                    report.validAllocationSamples++;
                    bytes += report.sampleRawBytes[i];
                }
            }
            report.meanMilliseconds = time / k_samples;
            double[] sorted = (double[])report.sampleMilliseconds.Clone();
            Array.Sort(sorted);
            report.medianMilliseconds = (sorted[3] + sorted[4]) / 2;
            report.nanosecondsPerWait = report.meanMilliseconds * 1000000d / (k_runs * k_cohortSize);
            report.allocationVerified = report.bracketControlsPassed && report.validAllocationSamples == k_samples;
            report.bytesPerWait = report.allocationVerified ? (double)bytes / (k_samples * k_runs * k_cohortSize) : -1;
            report.allocationUnavailableReason = report.allocationVerified ? null
                : !report.counterCalibrated ? m_counter.Description
                : !report.bracketControlsPassed ? "The same-layout empty/64 KiB bracket controls did not both pass."
                : "At least one bracket slice failed collection, nonnegative delta, or same-frame checks.";
            report.allocationInterpretation = report.allocationVerified && bytes == 0
                && m_counter.Kind == OnityBenchmarkAllocationCounter.k_kindHeapDelta
                ? "No measured heap growth; coarse process-wide HeapDelta does not prove zero allocation."
                : "Counter readings apply only to the stated main-thread bracket; no general zero-allocation or speed claim.";
        }

        private void Cleanup()
        {
            try
            {
                AbortWindow();
            }
            finally
            {
                try
                {
                    PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                    RemoveMarkers(ref loop);
                    PlayerLoop.SetPlayerLoop(loop);
                }
                finally
                {
                    foreach (Holder holder in m_holders)
                    {
                        try
                        {
                            holder?.Cleanup();
                        }
                        catch (Exception exception)
                        {
                            m_failure = m_failure ?? exception;
                        }
                    }
                    m_positiveControl = null;
                    try
                    {
                        m_counter?.Dispose();
                    }
                    finally
                    {
                        m_counter = null;
                        if (ReferenceEquals(s_active, this))
                        {
                            s_active = null;
                        }
                    }
                }
            }
        }

        private void RestoreCollector()
        {
            if (m_hasSettings && GarbageCollector.GCMode != m_oldGcMode)
            {
                GarbageCollector.GCMode = m_oldGcMode;
            }
        }

        private void RestoreSettings()
        {
            if (!m_hasSettings)
            {
                return;
            }
            RestoreCollector();
            OnityTask.FlowExecutionContext = m_oldFlow;
            OnityTaskTracker.IsEnabled = m_oldTracker;
            OnityTaskTracker.EnableStackTrace = m_oldStackTrace;
            OnityTask.RunnerPoolCapacity = m_oldCapacity;
            m_hasSettings = false;
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

        private static int InsertBracket(ref PlayerLoopSystem system)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return 0;
            }
            int found = 0;
            for (int i = 0; i < children.Length; i++)
            {
                found += InsertBracket(ref children[i]);
                if (children[i].type?.FullName == k_updateMarker
                    && children[i].type.Assembly == typeof(OnityTaskPlayerLoop).Assembly)
                {
                    var expanded = new PlayerLoopSystem[children.Length + 2];
                    Array.Copy(children, 0, expanded, 0, i);
                    expanded[i] = new PlayerLoopSystem { type = typeof(BeforeMarker), updateDelegate = s_before };
                    expanded[i + 1] = children[i];
                    expanded[i + 2] = new PlayerLoopSystem { type = typeof(AfterMarker), updateDelegate = s_after };
                    Array.Copy(children, i + 1, expanded, i + 3, children.Length - i - 1);
                    system.subSystemList = expanded;
                    found++;
                }
            }
            return found;
        }

        private static void RemoveMarkers(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
            {
                return;
            }
            var retained = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (PlayerLoopSystem childValue in system.subSystemList)
            {
                if (childValue.type == typeof(BeforeMarker) || childValue.type == typeof(AfterMarker))
                {
                    continue;
                }
                PlayerLoopSystem child = childValue;
                RemoveMarkers(ref child);
                retained.Add(child);
            }
            system.subSystemList = retained.ToArray();
        }

        private sealed class Holder
        {
            private readonly OnityTaskPlayerLoopBenchmarkRunner m_owner;
            internal readonly Action Callback;
            internal OnityTask Task;
            internal bool HasTask;
            internal bool Registered;
            internal bool Consumed;
            internal Exception Failure;

            internal Holder(OnityTaskPlayerLoopBenchmarkRunner owner)
            {
                m_owner = owner;
                Callback = Consume;
            }

            internal void Reset()
            {
                Task = default;
                HasTask = false;
                Registered = false;
                Consumed = false;
                Failure = null;
            }

            private void Consume()
            {
                if (Consumed)
                {
                    return;
                }
                Consumed = true;
                try
                {
                    Task.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }
                finally
                {
                    Task = default;
                    HasTask = false;
                    m_owner.m_consumed++;
                }
            }

            internal void Cleanup()
            {
                if (!HasTask || Consumed || Registered)
                {
                    return;
                }
                if (Task.IsCompleted)
                {
                    Consume();
                }
                else
                {
                    Task.GetAwaiter().UnsafeOnCompleted(Callback);
                    Registered = true;
                }
            }
        }

        private struct Window
        {
            internal long Bytes;
            internal bool Valid;
            internal double Milliseconds;
            internal int Consumed;
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string suite = "timing";
            public bool completed;
            public string failure;
            public string generatedAtUtc;
            public OnityTaskBenchmarkEnvironment environment;
            public int waitsPerCohort = k_cohortSize;
            public int warmupCohorts = k_warmups;
            public int samples = k_samples;
            public int cohortsPerSample = k_runs;
            public string boundary = "Before(actual Onity Update): counter begin, 128 Yield(Update,None) enqueues and cached native registrations; "
                + "actual Update cancellation scan/drain/GetResult/pool returns; adjacent After: counter end. "
                + "Holders/delegates prepared beforehand; assertions/reporting afterward. No Task bridges, async helpers, worker work, "
                + "Unity frame latency comparison, UniTask comparison or general scheduler superiority claim.";
            public bool counterCalibrated;
            public string counterKind;
            public string counterDescription;
            public string counterRejections;
            public string originalCollectorMode;
            public long counterCalibrationBytes;
            public long counterEmptyBytes;
            public long bracketEmptyBytes;
            public bool bracketEmptyValid;
            public long bracketPositiveBytes;
            public bool bracketPositiveValid;
            public bool bracketControlsPassed;
            public int validatedWarmupWaits;
            public int validatedMeasuredWaits;
            public double[] sampleMilliseconds = new double[k_samples];
            public long[] sampleRawBytes = new long[k_samples];
            public bool[] sampleAllocationValid = new bool[k_samples];
            public int[] sampleConsumed = new int[k_samples];
            public double meanMilliseconds;
            public double medianMilliseconds;
            public double nanosecondsPerWait;
            public int validAllocationSamples;
            public bool allocationVerified;
            public double bytesPerWait = -1;
            public string allocationUnavailableReason;
            public string allocationInterpretation;
        }
    }
}
