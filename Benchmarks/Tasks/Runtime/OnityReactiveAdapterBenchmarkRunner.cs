using System;
using System.Diagnostics;
using System.IO;
using Onity.Reactive;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Scripting;

namespace Onity.Benchmarks
{
    /// <summary>Bounded synchronous pending-delivery evidence for both reactive adapter directions.</summary>
    public static class OnityReactiveAdapterBenchmarkRunner
    {
        private const int k_items = 4096;
        private const int k_warmups = 3;
        private const int k_runs = 2;
        private const int k_samples = 8;
        private const int k_positiveBytes = 65536;

        /// <summary>Runs prepared pending-delivery windows and writes raw allocation evidence.</summary>
        /// <param name="path">JSON output path.</param>
        /// <param name="completed">Output path and failure callback.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            var report = new Report();
            Exception failure = null;
            bool flow = OnityTask.FlowExecutionContext;
            bool tracking = OnityTaskTracker.IsEnabled;
            bool stacks = OnityTaskTracker.EnableStackTrace;
            Action<Exception> handler = OnityObservableExceptionHandler.Handler;
            GarbageCollector.Mode collector = GarbageCollector.GCMode;
            OnityBenchmarkAllocationCounter counter = null;
            Exception unexpected = null;
            try
            {
                // Measured at the library default (flow off); the caller's setting is restored below.
                OnityTask.FlowExecutionContext = false;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                OnityObservableExceptionHandler.Handler = error => unexpected = unexpected ?? error;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                if (Application.isEditor || report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Reactive adapter evidence requires a Release Player.");
                }
                counter = OnityBenchmarkAllocationCounter.Create(false, true);
                GarbageCollector.GCMode = collector;
                report.counterKind = counter.Kind;
                report.counterDescription = counter.Description;
                report.counterRejections = counter.RejectedCandidates;
                report.counterCalibrationBytes = counter.CalibrationBytes;
                report.counterEmptyBytes = counter.EmptyDeltaBytes;
                report.counterCalibrated = counter.IsAvailable;
                report.originalCollectorMode = collector.ToString();
                report.scenarios = new[] { Measure(false, counter, collector), Measure(true, counter, collector) };
                if (unexpected != null)
                {
                    throw new InvalidOperationException("Unexpected reactive cleanup report.", unexpected);
                }
                report.completed = true;
            }
            catch (Exception exception)
            {
                failure = exception;
                report.failure = exception.ToString();
            }
            finally
            {
                counter?.Dispose();
                GarbageCollector.GCMode = collector;
                OnityObservableExceptionHandler.Handler = handler;
                OnityTask.FlowExecutionContext = flow;
                OnityTaskTracker.IsEnabled = tracking;
                OnityTaskTracker.EnableStackTrace = stacks;
            }
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            try
            {
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            completed?.Invoke(path, failure);
        }

        private static Scenario Measure(bool reverse, OnityBenchmarkAllocationCounter counter, GarbageCollector.Mode collector)
        {
            var scenario = new Scenario { name = reverse ? "PullToPush" : "PushToPull", samples = new Sample[k_runs * k_samples] };
            for (int warmup = 0; warmup < k_warmups; warmup++)
            {
                using (Window window = Create(reverse))
                {
                    var sample = new Sample();
                    window.Prime(sample);
                    window.Work(sample);
                    Validate(sample);
                    scenario.warmedItems += sample.deliveries;
                    scenario.warmedPrimingItems += sample.primingItems;
                }
            }
            Control(counter, collector, false, out scenario.emptyBytes, out scenario.emptyValid);
            Control(counter, collector, true, out scenario.positiveBytes, out scenario.positiveValid);
            scenario.controlsPassed = counter.IsAvailable && scenario.emptyValid && scenario.emptyBytes == 0
                && scenario.positiveValid && scenario.positiveBytes >= k_positiveBytes;
            if (!scenario.controlsPassed)
            {
                throw new InvalidOperationException("Reactive adapter bracket controls failed.");
            }
            for (int run = 0; run < k_runs; run++)
            {
                for (int index = 0; index < k_samples; index++)
                {
                    Window window = Create(reverse);
                    var sample = new Sample { run = run, index = index };
                    scenario.samples[run * k_samples + index] = sample;
                    bool opened = false;
                    try
                    {
                        window.Prime(sample);
                        long start = counter.Begin();
                        opened = true;
                        long ticks = Stopwatch.GetTimestamp();
                        window.Work(sample);
                        long stopped = Stopwatch.GetTimestamp();
                        sample.rawBytes = counter.End(start, out bool valid);
                        opened = false;
                        sample.allocationValid = valid && sample.rawBytes >= 0;
                        sample.milliseconds = (stopped - ticks) * 1000d / Stopwatch.Frequency;
                    }
                    finally
                    {
                        if (opened)
                        {
                            counter.EndSlice();
                        }
                        GarbageCollector.GCMode = collector;
                        window.Dispose();
                    }
                    Validate(sample);
                    if (!sample.allocationValid)
                    {
                        throw new InvalidOperationException("Reactive adapter allocation window was interrupted.");
                    }
                    scenario.measuredItems += sample.deliveries;
                    scenario.measuredPrimingItems += sample.primingItems;
                }
            }
            return scenario;
        }

        private static Window Create(bool reverse) => reverse ? (Window)new PullWindow() : new PushWindow();

        private static void Validate(Sample sample)
        {
            if (sample.inputCalls != k_items || sample.deliveries != k_items || sample.moveCalls != k_items
                || sample.currentReads != k_items || sample.checksum != 8386560 || sample.failures != 0
                || sample.primingItems != 1 || sample.primingValue != -1 || !sample.finalPending)
            {
                throw new InvalidOperationException("Reactive adapter counts, priming or pending-cycle protocol failed.");
            }
        }

        private static void Control(OnityBenchmarkAllocationCounter counter, GarbageCollector.Mode collector,
            bool positive, out long bytes, out bool valid)
        {
            byte[] retained = null;
            bool opened = false;
            try
            {
                long start = counter.Begin();
                opened = true;
                if (positive)
                {
                    retained = new byte[k_positiveBytes];
                }
                bytes = counter.End(start, out valid);
                opened = false;
            }
            finally
            {
                if (opened)
                {
                    counter.EndSlice();
                }
                GarbageCollector.GCMode = collector;
                GC.KeepAlive(retained);
            }
        }

        private abstract class Window : IDisposable
        {
            internal abstract void Prime(Sample sample);
            internal abstract void Work(Sample sample);
            public abstract void Dispose();
        }

        private sealed class PushWindow : Window
        {
            private readonly Subject<int> m_subject = new Subject<int>();
            private readonly IOnityAsyncEnumerator<int> m_iterator;
            private OnityTask<bool> m_pending;
            internal PushWindow() => m_iterator = m_subject.AsOnityAsyncEnumerable(1).GetAsyncEnumerator();
            internal override void Prime(Sample sample)
            {
                m_pending = m_iterator.MoveNextAsync();
                if (m_pending.IsCompleted)
                {
                    throw new InvalidOperationException("Push priming move must wait for delivery.");
                }
                m_subject.OnNext(-1);
                if (!m_pending.IsCompleted || !m_pending.GetAwaiter().GetResult() || m_iterator.Current != -1)
                {
                    throw new InvalidOperationException("Push priming delivery failed.");
                }
                sample.primingItems = 1;
                sample.primingValue = -1;
            }
            internal override void Work(Sample sample)
            {
                for (int value = 0; value < k_items; value++)
                {
                    m_pending = m_iterator.MoveNextAsync();
                    sample.moveCalls++;
                    if (m_pending.IsCompleted)
                    {
                        sample.failures++;
                        break;
                    }
                    m_subject.OnNext(value);
                    sample.inputCalls++;
                    if (!m_pending.IsCompleted || !m_pending.GetAwaiter().GetResult())
                    {
                        sample.failures++;
                        break;
                    }
                    int item = m_iterator.Current;
                    sample.currentReads++;
                    sample.deliveries++;
                    sample.checksum += item;
                    if (item != value)
                    {
                        sample.failures++;
                    }
                }
                // The last successful delivery leaves no buffered replay item.
                // Its next pending move is prepared outside the measured work.
                sample.finalPending = true;
            }
            public override void Dispose()
            {
                try
                {
                    m_iterator.DisposeAsync().GetAwaiter().GetResult();
                    if (m_pending.IsCompleted)
                    {
                        m_pending.GetAwaiter().GetResult();
                    }
                }
                finally
                {
                    m_subject.Dispose();
                }
            }
        }

        private sealed class PullWindow : Window
        {
            private readonly Producer m_source = new Producer();
            private readonly ValueSink m_sink = new ValueSink();
            private readonly IDisposable m_subscription;
            internal PullWindow() => m_subscription = m_source.AsObservable().Subscribe(m_sink);
            internal override void Prime(Sample sample)
            {
                if (m_source.Next != 0 || m_source.Slots[0].Task.IsCompleted)
                {
                    throw new InvalidOperationException("Pull priming move must wait for delivery.");
                }
                m_source.Slots[0].TrySetResult(true);
                if (m_sink.Count != 1 || m_sink.Last != -1 || m_source.Next != 1 || m_source.Slots[1].Task.IsCompleted)
                {
                    throw new InvalidOperationException("Pull priming delivery failed.");
                }
                sample.primingItems = 1;
                sample.primingValue = -1;
            }
            internal override void Work(Sample sample)
            {
                for (int value = 0; value < k_items; value++)
                {
                    bool accepted = m_source.Slots[value + 1].TrySetResult(true);
                    sample.inputCalls++;
                    if (!accepted || m_sink.Count != value + 2 || m_sink.Last != value
                        || m_source.Next != value + 2 || m_source.Slots[value + 2].Task.IsCompleted)
                    {
                        sample.failures++;
                        break;
                    }
                    sample.deliveries++;
                    sample.checksum += m_sink.Last;
                }
                sample.moveCalls = m_source.Moves - 2;
                sample.currentReads = m_source.CurrentReads - 1;
                sample.finalPending = m_source.Next == k_items + 1 && !m_source.Slots[k_items + 1].Task.IsCompleted;
            }
            public override void Dispose()
            {
                try
                {
                    m_subscription.Dispose();
                }
                finally
                {
                    m_source.DisposeAsync().GetAwaiter().GetResult();
                }
                if (m_source.Disposals != 1 || m_sink.Disposals != 1 || !m_source.Slots[m_source.Next].Task.IsCompleted)
                {
                    throw new InvalidOperationException("Pull teardown failed to retire its final pending move.");
                }
            }
        }

        private sealed class Producer : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            private readonly object m_gate = new object();
            private bool m_closed;
            private OnityTaskCompletionSource<bool> m_held;
            internal readonly OnityTaskCompletionSource<bool>[] Slots = new OnityTaskCompletionSource<bool>[k_items + 2];
            internal int Next = -1;
            internal int Moves;
            internal int CurrentReads;
            internal int Disposals;
            internal Producer()
            {
                for (int index = 0; index < Slots.Length; index++)
                {
                    Slots[index] = new OnityTaskCompletionSource<bool>();
                }
            }
            public int Current
            {
                get
                {
                    CurrentReads++;
                    return Next - 1;
                }
            }
            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default) => this;
            public OnityTask<bool> MoveNextAsync()
            {
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return OnityTask<bool>.FromResult(false);
                    }
                    Moves++;
                    m_held = Slots[++Next];
                    return m_held.Task;
                }
            }
            public OnityTask DisposeAsync()
            {
                OnityTaskCompletionSource<bool> held;
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return OnityTask.Completed;
                    }
                    m_closed = true;
                    Disposals++;
                    held = m_held;
                    m_held = null;
                }
                held?.TrySetResult(false);
                return OnityTask.Completed;
            }
        }

        private sealed class ValueSink : OnityObserver<int>
        {
            internal int Count;
            internal int Last;
            internal int Disposals;
            protected override void OnNextCore(int value)
            {
                Count++;
                Last = value;
            }
            protected override void OnDisposed() => Disposals++;
        }

        [Serializable]
        private sealed class Report
        {
            public OnityTaskBenchmarkEnvironment environment;
            public string generatedAtUtc;
            public bool completed;
            public string failure;
            public string counterKind;
            public string counterDescription;
            public string counterRejections;
            public long counterCalibrationBytes;
            public long counterEmptyBytes;
            public bool counterCalibrated;
            public string originalCollectorMode;
            public int itemsPerWindow = k_items;
            public int warmups = k_warmups;
            public int runs = k_runs;
            public int samplesPerRun = k_samples;
            public int preparedProducerSlots = k_items + 2;
            public string scope = "One process/backend; two internal runs of eight pending-delivery windows. Push uses real Subject capacity1 plus native Move/publication/Current; reverse includes adapter observation, observer OnNext and next native Move registration. Setup, sentinel priming, producer slot allocation, final disposal and validation excluded. Harness loop/scalar counts included. No speed or zero-allocation claim; HeapDelta is retained-heap evidence only.";
            public Scenario[] scenarios;
        }

        [Serializable]
        private sealed class Scenario
        {
            public string name;
            public long warmedItems;
            public long measuredItems;
            public int warmedPrimingItems;
            public int measuredPrimingItems;
            public long emptyBytes;
            public bool emptyValid;
            public long positiveBytes;
            public bool positiveValid;
            public bool controlsPassed;
            public Sample[] samples;
        }

        [Serializable]
        private sealed class Sample
        {
            public int run;
            public int index;
            public long rawBytes;
            public bool allocationValid;
            public double milliseconds;
            public int inputCalls;
            public int deliveries;
            public int moveCalls;
            public int currentReads;
            public long checksum;
            public int failures;
            public int primingItems;
            public int primingValue;
            public bool finalPending;
        }
    }
}
