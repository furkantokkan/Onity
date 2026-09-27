using System;
using System.Diagnostics;
using System.IO;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Scripting;

namespace Onity.Benchmarks
{
    /// <summary>Isolated warmed buffered TryWrite/TryRead allocation evidence, without pending waiters.</summary>
    public static class OnityChannelBenchmarkRunner
    {
        private const int k_cycles = 4096;
        private const int k_warmups = 3;
        private const int k_runs = 2;
        private const int k_samples = 8;
        private const int k_positiveBytes = 65536;

        /// <summary>Writes two bounded channel scenarios and invokes the Player completion callback.</summary>
        /// <param name="path">JSON report path.</param>
        /// <param name="completed">Report path and failure callback.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            var report = new Report();
            Exception failure = null;
            bool flow = OnityTask.FlowExecutionContext;
            bool tracker = OnityTaskTracker.IsEnabled;
            bool stackTrace = OnityTaskTracker.EnableStackTrace;
            GarbageCollector.Mode collector = GarbageCollector.GCMode;
            OnityBenchmarkAllocationCounter counter = null;
            try
            {
                OnityTask.FlowExecutionContext = true;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                if (Application.isEditor || report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Channel measurements require a Release Player.");
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
                report.scenarios = new[] { Measure(1, counter, collector), Measure(128, counter, collector) };
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
                OnityTask.FlowExecutionContext = flow;
                OnityTaskTracker.IsEnabled = tracker;
                OnityTaskTracker.EnableStackTrace = stackTrace;
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

        private static Scenario Measure(int capacity, OnityBenchmarkAllocationCounter counter,
            GarbageCollector.Mode collector)
        {
            int items = k_cycles * capacity;
            var scenario = new Scenario
            {
                capacity = capacity,
                expectedItemsPerWindow = items,
                expectedChecksum = (long)items * (items - 1) / 2,
                expectedPrimingChecksum = -(long)capacity * (capacity + 1) / 2,
                samples = new Sample[k_runs * k_samples]
            };
            for (int warmup = 0; warmup < k_warmups; warmup++)
            {
                var channel = OnityChannel.CreateBounded<int>(capacity);
                var sample = new Sample();
                try
                {
                    Prime(channel, sample, capacity);
                    Work(channel.Reader, channel.Writer, capacity, sample);
                    Validate(sample, scenario);
                    scenario.warmedItems += sample.successfulReads;
                    scenario.warmedPrimingItems += sample.primingReads;
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }
            Control(counter, collector, false, out scenario.emptyBytes, out scenario.emptyValid);
            Control(counter, collector, true, out scenario.positiveBytes, out scenario.positiveValid);
            scenario.controlsPassed = counter.IsAvailable && scenario.emptyValid && scenario.emptyBytes == 0
                && scenario.positiveValid && scenario.positiveBytes >= k_positiveBytes;
            if (!scenario.controlsPassed)
            {
                throw new InvalidOperationException("Channel bracket controls failed at capacity " + capacity);
            }
            bool zeros = true;
            for (int run = 0; run < k_runs; run++)
            {
                for (int index = 0; index < k_samples; index++)
                {
                    var channel = OnityChannel.CreateBounded<int>(capacity);
                    OnityChannelReader<int> reader = channel.Reader;
                    OnityChannelWriter<int> writer = channel.Writer;
                    var sample = new Sample { run = run, index = index };
                    scenario.samples[run * k_samples + index] = sample;
                    bool opened = false;
                    try
                    {
                        Prime(channel, sample, capacity);
                        long start = counter.Begin();
                        opened = true;
                        long ticks = Stopwatch.GetTimestamp();
                        Work(reader, writer, capacity, sample);
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
                        channel.Writer.TryComplete();
                    }
                    Validate(sample, scenario);
                    if (!sample.allocationValid)
                    {
                        throw new InvalidOperationException("Channel window was interrupted at capacity " + capacity);
                    }
                    zeros &= sample.rawBytes == 0;
                    scenario.measuredItems += sample.successfulReads;
                    scenario.measuredPrimingItems += sample.primingReads;
                }
            }
            scenario.allRawSamplesZero = zeros;
            scenario.zeroAllocationProven = zeros && counter.Kind == OnityBenchmarkAllocationCounter.k_kindPerThread;
            return scenario;
        }

        private static void Prime(OnityChannel<int> channel, Sample sample, int capacity)
        {
            for (int index = 0; index < capacity; index++)
            {
                if (!channel.Writer.TryWrite(-index - 1))
                {
                    throw new InvalidOperationException("Channel priming write failed.");
                }
                sample.primingWrites++;
            }
            for (int index = 0; index < capacity; index++)
            {
                if (!channel.Reader.TryRead(out int value) || value != -index - 1)
                {
                    throw new InvalidOperationException("Channel priming FIFO read failed.");
                }
                sample.primingReads++;
                sample.primingChecksum += value;
            }
        }

        private static void Work(OnityChannelReader<int> reader, OnityChannelWriter<int> writer,
            int capacity, Sample sample)
        {
            // The measured bracket contains only the buffered API calls, loop and scalar bookkeeping.
            for (int cycle = 0; cycle < k_cycles; cycle++)
            {
                int first = cycle * capacity;
                for (int index = 0; index < capacity; index++)
                {
                    sample.writeCalls++;
                    if (writer.TryWrite(first + index))
                    {
                        sample.successfulWrites++;
                    }
                    else
                    {
                        sample.failedOperations++;
                    }
                }
                for (int index = 0; index < capacity; index++)
                {
                    sample.readCalls++;
                    if (reader.TryRead(out int value))
                    {
                        sample.successfulReads++;
                        sample.checksum += value;
                        if (value != first + index)
                        {
                            sample.failedOperations++;
                        }
                    }
                    else
                    {
                        sample.failedOperations++;
                    }
                }
            }
        }

        private static void Validate(Sample sample, Scenario scenario)
        {
            int count = scenario.expectedItemsPerWindow;
            if (sample.writeCalls != count || sample.readCalls != count || sample.successfulWrites != count
                || sample.successfulReads != count || sample.failedOperations != 0 || sample.checksum != scenario.expectedChecksum
                || sample.primingWrites != scenario.capacity || sample.primingReads != scenario.capacity
                || sample.primingChecksum != scenario.expectedPrimingChecksum)
            {
                throw new InvalidOperationException("Channel counts/FIFO/checksum failed outside measurement.");
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
            public int cyclesPerWindow = k_cycles;
            public int warmups = k_warmups;
            public int runs = k_runs;
            public int samplesPerRun = k_samples;
            public string scope = "One process/backend; two internal runs of eight synchronous windows. Warm buffered TryWrite/TryRead only, with loop/scalar counts. Construction, full fill/drain priming, closure, validation and reporting excluded. No pending waiters, token registrations, unbounded growth or library comparison. HeapDelta is retained-heap evidence, not allocated-byte proof.";
            public Scenario[] scenarios;
        }

        [Serializable]
        private sealed class Scenario
        {
            public int capacity;
            public int expectedItemsPerWindow;
            public long expectedChecksum;
            public long expectedPrimingChecksum;
            public long warmedItems;
            public long measuredItems;
            public int warmedPrimingItems;
            public int measuredPrimingItems;
            public long emptyBytes;
            public bool emptyValid;
            public long positiveBytes;
            public bool positiveValid;
            public bool controlsPassed;
            public bool allRawSamplesZero;
            public bool zeroAllocationProven;
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
            public int writeCalls;
            public int readCalls;
            public int successfulWrites;
            public int successfulReads;
            public int failedOperations;
            public long checksum;
            public int primingWrites;
            public int primingReads;
            public long primingChecksum;
        }
    }
}
