using System;
using System.Diagnostics;
using System.IO;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Scripting;

namespace Onity.Benchmarks
{
    /// <summary>Measures only warmed synchronous finite-stream moves and Current reads.</summary>
    public static class OnityAsyncEnumerableBenchmarkRunner
    {
        private const int k_items = 4096;
        private const int k_warmups = 3;
        private const int k_runs = 2;
        private const int k_samples = 8;
        private const int k_positiveBytes = 65536;
        private static readonly Func<int, int> s_select = value => value * 2;
        private static readonly Func<int, bool> s_where = value => (value & 3) == 0;

        /// <summary>Writes the finite allocation report and invokes the normal Player completion callback.</summary>
        /// <param name="path">JSON output path.</param>
        /// <param name="completed">Report and failure callback.</param>
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
                    throw new InvalidOperationException("Finite stream measurements require a Release Player.");
                }
                // An accumulated frame counter cannot measure this synchronous bracket.
                counter = OnityBenchmarkAllocationCounter.Create(false, true);
                GarbageCollector.GCMode = collector;
                report.counterKind = counter.Kind;
                report.counterDescription = counter.Description;
                report.counterRejections = counter.RejectedCandidates;
                report.counterCalibrationBytes = counter.CalibrationBytes;
                report.counterEmptyBytes = counter.EmptyDeltaBytes;
                report.counterCalibrated = counter.IsAvailable;
                report.originalCollectorMode = collector.ToString();
                report.scenarios = new[]
                {
                    Measure("Range", false, counter, collector),
                    Measure("Select/Where/Take", true, counter, collector)
                };
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

        private static Scenario Measure(string name, bool pipeline,
            OnityBenchmarkAllocationCounter counter, GarbageCollector.Mode collector)
        {
            var scenario = new Scenario
            {
                name = name,
                expectedChecksum = pipeline ? 33546240 : 8386560,
                expectedPrimingValue = pipeline ? -4 : -1,
                sourceStart = pipeline ? -2 : -1,
                sourceCount = pipeline ? k_items * 4 + 2 : k_items + 1,
                takeCount = pipeline ? k_items + 1 : 0,
                samples = new Sample[k_runs * k_samples]
            };
            // Description, delegates, enumerator allocation and explicit final disposal are outside slices.
            IOnityAsyncEnumerable<int> description = pipeline
                ? OnityAsyncEnumerable.Range(scenario.sourceStart, scenario.sourceCount)
                    .Select(s_select).Where(s_where).Take(scenario.takeCount)
                : OnityAsyncEnumerable.Range(scenario.sourceStart, scenario.sourceCount);
            for (int warmup = 0; warmup < k_warmups; warmup++)
            {
                var iterator = description.GetAsyncEnumerator();
                var sample = new Sample();
                try
                {
                    Prime(iterator, sample, scenario.expectedPrimingValue);
                    Traverse(iterator, sample);
                    Validate(sample, scenario.expectedChecksum);
                    scenario.warmedItems += sample.successfulMoves;
                    scenario.warmedPrimingItems += sample.primingMoves;
                }
                finally
                {
                    Dispose(iterator);
                }
            }
            Control(counter, collector, false, out scenario.emptyBytes, out scenario.emptyValid);
            Control(counter, collector, true, out scenario.positiveBytes, out scenario.positiveValid);
            scenario.controlsPassed = counter.IsAvailable && scenario.emptyValid && scenario.emptyBytes == 0
                && scenario.positiveValid && scenario.positiveBytes >= k_positiveBytes;
            if (!scenario.controlsPassed)
            {
                throw new InvalidOperationException("Finite bracket allocation controls did not pass: " + name);
            }
            bool zeros = true;
            for (int run = 0; run < k_runs; run++)
            {
                for (int index = 0; index < k_samples; index++)
                {
                    var iterator = description.GetAsyncEnumerator();
                    var sample = new Sample { run = run, index = index };
                    scenario.samples[run * k_samples + index] = sample;
                    bool opened = false;
                    try
                    {
                        // Acquire the complete lazy operator chain and read its first Current
                        // before opening the allocation/timing slice.
                        Prime(iterator, sample, scenario.expectedPrimingValue);
                        long start = counter.Begin();
                        opened = true;
                        long ticks = Stopwatch.GetTimestamp();
                        Traverse(iterator, sample);
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
                        Dispose(iterator);
                    }
                    Validate(sample, scenario.expectedChecksum);
                    if (!sample.allocationValid)
                    {
                        throw new InvalidOperationException("Finite allocation window was interrupted: " + name);
                    }
                    zeros &= sample.rawBytes == 0;
                    scenario.measuredItems += sample.successfulMoves;
                    scenario.measuredPrimingItems += sample.primingMoves;
                }
            }
            scenario.allRawSamplesZero = zeros;
            scenario.zeroAllocationProven = zeros && counter.Kind == OnityBenchmarkAllocationCounter.k_kindPerThread;
            return scenario;
        }

        private static void Prime(IOnityAsyncEnumerator<int> iterator, Sample sample, int expectedValue)
        {
            OnityTask<bool> move = iterator.MoveNextAsync();
            if (!move.IsCompletedSuccessfully || !move.GetAwaiter().GetResult())
            {
                throw new InvalidOperationException("Finite priming did not produce an inline item.");
            }
            sample.primingMoves++;
            sample.primingValue = iterator.Current;
            sample.primingCurrentReads++;
            if (sample.primingValue != expectedValue)
            {
                throw new InvalidOperationException("Finite priming produced the wrong sentinel item.");
            }
        }

        private static void Traverse(IOnityAsyncEnumerator<int> iterator, Sample sample)
        {
            while (true)
            {
                OnityTask<bool> move = iterator.MoveNextAsync();
                sample.moveCalls++;
                if (!move.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException("Finite work stopped being inline success.");
                }
                if (!move.GetAwaiter().GetResult())
                {
                    sample.endMoves++;
                    break;
                }
                sample.successfulMoves++;
                sample.checksum += iterator.Current;
                sample.currentReads++;
                if (sample.successfulMoves > k_items)
                {
                    throw new InvalidOperationException("Finite work exceeded its expected item count.");
                }
            }
        }

        private static void Validate(Sample sample, long checksum)
        {
            if (sample.successfulMoves != k_items || sample.currentReads != k_items
                || sample.endMoves != 1 || sample.moveCalls != k_items + 1 || sample.checksum != checksum
                || sample.primingMoves != 1 || sample.primingCurrentReads != 1)
            {
                throw new InvalidOperationException("Finite work invocation counts or checksum differ from the contract.");
            }
        }

        private static void Dispose(IOnityAsyncEnumerator<int> iterator)
        {
            OnityTask cleanup = iterator.DisposeAsync();
            if (!cleanup.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Finite cleanup stopped being inline success.");
            }
            cleanup.GetAwaiter().GetResult();
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
            public int itemsPerWindow = k_items;
            public int warmups = k_warmups;
            public int runs = k_runs;
            public int samplesPerRun = k_samples;
            public int primingItemsPerWindow = 1;
            public string scope = "One successful move and Current read before each slice primes lazy upstream acquisition. Measured: 4096 subsequent synchronous MoveNextAsync/GetResult/Current items, final false and automatic Take cleanup. Construction, priming and explicit final Dispose excluded. HeapDelta is retained heap evidence, not an allocated-bytes proof.";
            public Scenario[] scenarios;
        }

        [Serializable]
        private sealed class Scenario
        {
            public string name;
            public long expectedChecksum;
            public int expectedPrimingValue;
            public int sourceStart;
            public int sourceCount;
            public int takeCount;
            public int warmedItems;
            public int measuredItems;
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
            public int moveCalls;
            public int successfulMoves;
            public int currentReads;
            public int endMoves;
            public long checksum;
            public int primingMoves;
            public int primingCurrentReads;
            public int primingValue;
        }
    }
}
