using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>Measures synchronous frame/delay readiness scanning without changing the task runtime.</summary>
    public sealed class OnityTaskReadinessBenchmarkRunner : MonoBehaviour
    {
        private const int k_capacity = 4096;
        private const int k_samples = 8;
        private const int k_warmups = 2;
        private const int k_pending = 1;
        private const int k_canceled = 2;
        private const int k_waitNext = 4;
        private const int k_unscaled = 8;
        private const int k_managedProof = 0x4D414E47;
        private const int k_burstProof = 0x42555253;
        private readonly Record[] m_input = new Record[k_capacity];
        private readonly Record[] m_updated = new Record[k_capacity];
        private readonly Record[] m_expected = new Record[k_capacity];
        private readonly Token[] m_ready = new Token[k_capacity];
        private readonly Token[] m_expectedReady = new Token[k_capacity];
        private readonly Sink[] m_sinks = new Sink[k_capacity];
        private NativeArray<Record> m_nativeInput;
        private NativeArray<Record> m_nativeUpdated;
        private NativeArray<Token> m_nativeReady;
        private NativeArray<int> m_nativeHeader;
        private PlainJob m_plainJob;
        private BurstJob m_burstJob;
        private Snapshot m_snapshot;
        private Action[] m_work;
        private OnityBenchmarkAllocationCounter m_counter;
        private bool m_allocationAvailable;
        private int m_count;
        private int m_readyCount;
        private int m_expectedCount;
        private int m_proof;
        private int m_dispatchedCount;
        private long m_dispatchChecksum;
        private long m_controlChecksum;
        private string m_path;
        private Action<string, Exception> m_completed;

        /// <summary>Starts the Release Player readiness feasibility suite and writes its report.</summary>
        /// <param name="path">Output JSON path.</param>
        /// <param name="completed">Receives the report path and any failure after cleanup.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Readiness benchmark output path is required.", nameof(path));
            }
            GameObject owner = new GameObject("Onity Readiness Feasibility Benchmark");
            DontDestroyOnLoad(owner);
            OnityTaskReadinessBenchmarkRunner runner = owner.AddComponent<OnityTaskReadinessBenchmarkRunner>();
            runner.m_path = Path.GetFullPath(path);
            runner.m_completed = completed;
        }

        private IEnumerator Start()
        {
            Report report = new Report();
            Exception failure = null;
            try
            {
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                report.platform = Application.platform.ToString();
                report.unityVersion = Application.unityVersion;
                report.burstAssembly = typeof(BurstCompiler).Assembly.FullName;
                report.burstEnabled = BurstCompiler.IsEnabled;
                if (Application.isEditor || report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Readiness benchmark requires a non-development Release Player.");
                }
                m_nativeInput = new NativeArray<Record>(k_capacity, Allocator.Persistent);
                m_nativeUpdated = new NativeArray<Record>(k_capacity, Allocator.Persistent);
                m_nativeReady = new NativeArray<Token>(k_capacity, Allocator.Persistent);
                m_nativeHeader = new NativeArray<int>(2, Allocator.Persistent);
                report.recordBytes = Marshal.SizeOf(typeof(Record));
                report.tokenBytes = Marshal.SizeOf(typeof(Token));
                report.allocatedNativeBytes = k_capacity * (2L * report.recordBytes + report.tokenBytes) + 2L * sizeof(int);
                m_work = new Action[]
                {
                    RunManaged, RunPlain, RunBurst,
                    RunManagedDispatch, RunPlainDispatch, RunBurstDispatch,
                    RunManagedDispatch, RunPlainCombined, RunBurstCombined
                };
                m_counter = OnityBenchmarkAllocationCounter.Create(false, false);
                m_allocationAvailable = m_counter.IsAvailable && m_counter.Kind == OnityBenchmarkAllocationCounter.k_kindPerThread;
                report.allocationAvailable = m_allocationAvailable;
                report.allocationCounterKind = m_counter.Kind;
                report.allocationCounter = m_counter.Description;
                report.allocationCalibrationBytes = m_counter.CalibrationBytes;
                report.allocationEmptyBytes = m_counter.EmptyDeltaBytes;
                report.allocationRejectionReasons = m_counter.RejectedCandidates;
                report.allocationScope = m_allocationAvailable
                    ? "Calibrated current-thread managed bytes around the synchronous batch; excludes retained native buffers."
                    : "Unavailable: only a calibrated current-thread counter can isolate these batch allocations; no zero-allocation claim.";
                CheckBoundaries(report);
                CheckGenerationRejection(report);
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
            int[] counts = { 1, 32, 128, 4096 };
            for (int size = 0; size < counts.Length; size++)
            {
                for (int kind = 0; kind < 2; kind++)
                {
                    for (int density = 0; density < 3; density++)
                    {
                        PrepareCase(counts[size], kind, density);
                        int caseIndex = (size * 2 + kind) * 3 + density;
                        int iterations = Math.Max(16, Math.Min(2048, 32768 / m_count));
                        CaseResult result = new CaseResult
                        {
                            index = caseIndex,
                            count = m_count,
                            kind = kind == 0 ? "Frame" : "Delay",
                            density = density == 0 ? "All pending" : density == 1 ? "Every eighth ready" : "All ready",
                            expectedReadyCount = m_expectedCount,
                            expectedChecksum = ExpectedChecksum(),
                            currentFrame = m_snapshot.CurrentFrame,
                            deltaTime = m_snapshot.Delta,
                            unscaledDeltaTime = m_snapshot.UnscaledDelta,
                            iterationsPerBatch = iterations,
                            metrics = new Metric[9],
                            controls = new Sample[k_samples]
                        };
                        report.cases[caseIndex] = result;
                        for (int window = 0; window < 3; window++)
                        {
                            for (int variant = 0; variant < 3; variant++)
                            {
                                result.metrics[window * 3 + variant] = new Metric
                                {
                                    variant = VariantName(variant),
                                    window = WindowName(window),
                                    expectedProof = variant == 2 ? k_burstProof : k_managedProof,
                                    warmups = new Sample[k_warmups],
                                    samples = new Sample[k_samples]
                                };
                            }
                            for (int warmup = 0; warmup < k_warmups; warmup++)
                            {
                                for (int order = 0; order < 3; order++)
                                {
                                    int variant = (warmup + order) % 3;
                                    result.metrics[window * 3 + variant].warmups[warmup]
                                        = MeasureBatch(window, variant, iterations, warmup, order);
                                }
                            }
                            for (int sample = 0; sample < k_samples; sample++)
                            {
                                if (window == 0)
                                {
                                    result.controls[sample] = MeasureControl(iterations, sample);
                                }
                                for (int order = 0; order < 3; order++)
                                {
                                    int variant = (sample + order) % 3;
                                    result.metrics[window * 3 + variant].samples[sample]
                                        = MeasureBatch(window, variant, iterations, sample, order);
                                }
                                yield return null;
                            }
                        }
                    }
                }
            }
        }

        private Sample MeasureBatch(int window, int variant, int iterations, int index, int order)
        {
            Array.Clear(m_sinks, 0, m_sinks.Length);
            for (int i = 0; i < m_count; i++)
            {
                m_sinks[i].Generation = m_input[i].CurrentGeneration;
            }
            // Poison before the batch so proof must be produced by its invocation, not an earlier variant.
            m_proof = 0;
            m_nativeHeader[1] = 0;
            Action work = m_work[window * 3 + variant];
            Sample sample = new Sample { index = index, order = order, iterations = iterations };
            long bytes = m_allocationAvailable ? m_counter.Read() : 0;
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                work();
            }
            long stopped = Stopwatch.GetTimestamp();
            sample.managedBytes = m_allocationAvailable ? m_counter.Read() - bytes : -1;
            sample.allocationValid = m_allocationAvailable && sample.managedBytes >= 0;
            sample.batchTicks = stopped - started;
            SetTimes(sample);
            sample.checksum = Validate(variant, window != 0, window == 2 && variant != 0);
            sample.readyCount = variant == 0 ? m_readyCount : m_nativeHeader[0];
            sample.proof = variant == 0 ? m_proof : m_nativeHeader[1];
            sample.dispatchCount = window == 0 ? 0 : m_dispatchedCount;
            sample.dispatchChecksum = window == 0 ? 0 : m_dispatchChecksum;
            sample.validated = true;
            return sample;
        }

        private Sample MeasureControl(int iterations, int index)
        {
            long checksum = 0;
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                checksum += i & 1;
            }
            long stopped = Stopwatch.GetTimestamp();
            m_controlChecksum = checksum;
            Sample sample = new Sample
            {
                index = index, iterations = iterations, batchTicks = stopped - started,
                checksum = m_controlChecksum, managedBytes = -1, validated = checksum == iterations / 2
            };
            if (!sample.validated)
            {
                throw new InvalidOperationException("Readiness control checksum failed.");
            }
            SetTimes(sample);
            return sample;
        }

        private void SetTimes(Sample sample)
        {
            sample.nanosecondsPerInvocation = sample.batchTicks * 1000000000d / Stopwatch.Frequency / sample.iterations;
            sample.nanosecondsPerInputElement = sample.nanosecondsPerInvocation / m_count;
        }

        private void PrepareCase(int count, int kind, int density)
        {
            m_count = count;
            m_snapshot = new Snapshot { CurrentFrame = 100, Delta = 0.25f, UnscaledDelta = 0.5f };
            m_expectedCount = 0;
            for (int i = 0; i < count; i++)
            {
                bool ready = density == 2 || (density == 1 && i % 8 == 0);
                bool unscaled = (i & 1) != 0;
                Record record = new Record
                {
                    Slot = i, Generation = 7, CurrentGeneration = 7, Kind = kind, StartFrame = 99,
                    RemainingFrames = ready ? 1 : 2, RemainingSeconds = ready ? 0.25f : 1.5f,
                    Flags = k_pending | k_waitNext | (unscaled ? k_unscaled : 0)
                };
                m_input[i] = record;
                // These expected states are derived from the known fixture values, not the kernel's readiness predicate.
                Record expected = record;
                if (kind == 0)
                {
                    expected.RemainingFrames = ready ? 0 : 1;
                }
                else
                {
                    expected.RemainingSeconds = ready ? (unscaled ? -0.25f : 0f) : (unscaled ? 1f : 1.25f);
                }
                if (ready)
                {
                    expected.Flags &= ~k_pending;
                }
                m_expected[i] = expected;
            }
            for (int i = count - 1; i >= 0; i--)
            {
                if (density == 2 || (density == 1 && i % 8 == 0))
                {
                    m_expectedReady[m_expectedCount++] = new Token { Slot = i, Generation = 7, Outcome = 1 };
                }
            }
            SetJobs();
        }

        private void SetJobs()
        {
            Stage();
            m_plainJob = new PlainJob
            {
                Input = m_nativeInput, Updated = m_nativeUpdated, Ready = m_nativeReady,
                Header = m_nativeHeader, Snapshot = m_snapshot, Count = m_count
            };
            m_burstJob = new BurstJob
            {
                Input = m_nativeInput, Updated = m_nativeUpdated, Ready = m_nativeReady,
                Header = m_nativeHeader, Snapshot = m_snapshot, Count = m_count
            };
        }

        private void RunManaged()
        {
            ScanManaged(m_input, m_updated, m_ready, m_snapshot, m_count, out m_readyCount, out m_proof);
        }

        internal static void ScanManaged(Record[] input, Record[] updated, Token[] ready,
            Snapshot snapshot, int count, out int outputCount, out int proof)
        {
            proof = GetProof();
            int readyCount = 0;
            for (int i = count - 1; i >= 0; i--)
            {
                Record record = input[i];
                int outcome = Update(ref record, snapshot);
                updated[i] = record;
                if (outcome != 0)
                {
                    ready[readyCount++] = new Token { Slot = record.Slot, Generation = record.Generation, Outcome = outcome };
                }
            }
            outputCount = readyCount;
        }

        private void RunPlain() => m_plainJob.Run();
        private void RunBurst() => m_burstJob.Run();

        private void RunManagedDispatch()
        {
            RunManaged();
            DispatchManaged();
        }

        private void RunPlainDispatch()
        {
            RunPlain();
            DispatchNative();
        }

        private void RunBurstDispatch()
        {
            RunBurst();
            DispatchNative();
        }

        private void RunPlainCombined()
        {
            Stage();
            RunPlain();
            Transfer();
            DispatchManaged();
        }

        private void RunBurstCombined()
        {
            Stage();
            RunBurst();
            Transfer();
            DispatchManaged();
        }

        private void Stage()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_nativeInput[i] = m_input[i];
            }
        }

        private void Transfer()
        {
            for (int i = 0; i < m_count; i++)
            {
                m_updated[i] = m_nativeUpdated[i];
            }
            m_readyCount = m_nativeHeader[0];
            for (int i = 0; i < m_readyCount; i++)
            {
                m_ready[i] = m_nativeReady[i];
            }
        }

        private void DispatchManaged()
        {
            m_dispatchedCount = 0;
            m_dispatchChecksum = 0;
            for (int i = 0; i < m_readyCount; i++)
            {
                Dispatch(m_ready[i], i);
            }
        }

        private void DispatchNative()
        {
            m_dispatchedCount = 0;
            m_dispatchChecksum = 0;
            int count = m_nativeHeader[0];
            for (int i = 0; i < count; i++)
            {
                Dispatch(m_nativeReady[i], i);
            }
        }

        private void Dispatch(Token token, int order)
        {
            if (m_sinks[token.Slot].Generation != token.Generation)
            {
                return;
            }
            // Synthetic preallocated sinks are overwritten each invocation; no actual source or callback is completed.
            m_sinks[token.Slot].Completed = 1;
            m_sinks[token.Slot].Outcome = token.Outcome;
            m_sinks[token.Slot].Order = order;
            m_dispatchedCount++;
            m_dispatchChecksum += TokenChecksum(token, order);
        }

        private static int Update(ref Record record, Snapshot snapshot)
        {
            if (record.Generation != record.CurrentGeneration || (record.Flags & k_pending) == 0)
            {
                return 0;
            }
            if ((record.Flags & k_canceled) != 0)
            {
                record.Flags &= ~k_pending;
                return 2;
            }
            if ((record.Flags & k_waitNext) != 0 && snapshot.CurrentFrame == record.StartFrame)
            {
                return 0;
            }
            if (record.Kind == 0)
            {
                record.RemainingFrames--;
                if (record.RemainingFrames > 0)
                {
                    return 0;
                }
            }
            else
            {
                record.RemainingSeconds -= (record.Flags & k_unscaled) != 0 ? snapshot.UnscaledDelta : snapshot.Delta;
                if (record.RemainingSeconds > 0f)
                {
                    return 0;
                }
            }
            record.Flags &= ~k_pending;
            return 1;
        }

        private static void ScanNative(NativeArray<Record> input, NativeArray<Record> updated,
            NativeArray<Token> ready, NativeArray<int> header, Snapshot snapshot, int count)
        {
            header[1] = GetProof();
            int readyCount = 0;
            for (int i = count - 1; i >= 0; i--)
            {
                Record record = input[i];
                int outcome = Update(ref record, snapshot);
                updated[i] = record;
                if (outcome != 0)
                {
                    ready[readyCount++] = new Token { Slot = record.Slot, Generation = record.Generation, Outcome = outcome };
                }
            }
            header[0] = readyCount;
        }

        private static int GetProof()
        {
            bool managed = false;
            MarkManaged(ref managed);
            return managed ? k_managedProof : k_burstProof;
        }

        [BurstDiscard]
        private static void MarkManaged(ref bool managed)
        {
            managed = true;
        }

        private long Validate(int variant, bool dispatch, bool transferred)
        {
            bool native = variant != 0;
            int proof = native ? m_nativeHeader[1] : m_proof;
            int count = native ? m_nativeHeader[0] : m_readyCount;
            if (proof != (variant == 2 ? k_burstProof : k_managedProof) || count != m_expectedCount
                || (transferred && m_readyCount != count))
            {
                throw new InvalidOperationException("Readiness Burst proof or output count failed for " + VariantName(variant));
            }
            long checksum = 0;
            for (int i = 0; i < m_count; i++)
            {
                Record actual = native ? m_nativeUpdated[i] : m_updated[i];
                if (!Equal(actual, m_expected[i]) || !Equal(m_input[i], m_nativeInput[i])
                    || (transferred && !Equal(m_updated[i], m_expected[i])))
                {
                    throw new InvalidOperationException("Readiness updated state or immutable input mismatch at slot " + i);
                }
                checksum += RecordChecksum(actual);
                if (dispatch && m_sinks[i].Completed != ((m_expected[i].Flags & k_pending) == 0
                    && m_expected[i].Generation == m_expected[i].CurrentGeneration
                    && (m_input[i].Flags & k_pending) != 0 ? 1 : 0))
                {
                    throw new InvalidOperationException("Readiness sink completion mismatch at slot " + i);
                }
            }
            long tokenChecksum = 0;
            for (int i = 0; i < count; i++)
            {
                Token actual = native ? m_nativeReady[i] : m_ready[i];
                Token expected = m_expectedReady[i];
                if (actual.Slot != expected.Slot || actual.Generation != expected.Generation || actual.Outcome != expected.Outcome
                    || (transferred && (m_ready[i].Slot != expected.Slot || m_ready[i].Generation != expected.Generation
                        || m_ready[i].Outcome != expected.Outcome))
                    || (i > 0 && actual.Slot >= (native ? m_nativeReady[i - 1].Slot : m_ready[i - 1].Slot)))
                {
                    throw new InvalidOperationException("Readiness token order, generation, outcome, or uniqueness failed.");
                }
                tokenChecksum += TokenChecksum(actual, i);
                if (dispatch && (m_sinks[actual.Slot].Outcome != actual.Outcome || m_sinks[actual.Slot].Order != i))
                {
                    throw new InvalidOperationException("Readiness synthetic sink outcome/order failed.");
                }
            }
            if (dispatch && (m_dispatchedCount != count || m_dispatchChecksum != tokenChecksum))
            {
                throw new InvalidOperationException("Readiness synthetic dispatch count/checksum failed.");
            }
            checksum += tokenChecksum;
            if (checksum != ExpectedChecksum())
            {
                throw new InvalidOperationException("Readiness checksum failed.");
            }
            return checksum;
        }

        private long ExpectedChecksum()
        {
            long checksum = 0;
            for (int i = 0; i < m_count; i++)
            {
                checksum += RecordChecksum(m_expected[i]);
            }
            for (int i = 0; i < m_expectedCount; i++)
            {
                checksum += TokenChecksum(m_expectedReady[i], i);
            }
            return checksum;
        }

        private static bool Equal(Record left, Record right)
        {
            return left.Slot == right.Slot && left.Generation == right.Generation && left.CurrentGeneration == right.CurrentGeneration
                && left.Kind == right.Kind && left.StartFrame == right.StartFrame && left.RemainingFrames == right.RemainingFrames
                && left.RemainingSeconds == right.RemainingSeconds && left.Flags == right.Flags;
        }

        private static long RecordChecksum(Record record)
        {
            return record.Slot * 31L + record.Generation * 37L + record.CurrentGeneration * 41L + record.Kind * 43L
                + record.StartFrame * 47L + record.RemainingFrames * 53L + (long)(record.RemainingSeconds * 1024f) * 59L
                + record.Flags * 61L;
        }

        private static long TokenChecksum(Token token, int order)
        {
            return token.Slot * 67L + token.Generation * 71L + token.Outcome * 73L + order * 79L;
        }

        private void CheckBoundaries(Report report)
        {
            // Handwritten expected states cover numerical boundaries and precedence independently of Update.
            m_count = 10;
            m_snapshot = new Snapshot { CurrentFrame = 100, Delta = 0f, UnscaledDelta = 0.5f };
            for (int i = 0; i < m_count; i++)
            {
                m_input[i] = new Record
                {
                    Slot = i, Generation = 7, CurrentGeneration = 7, StartFrame = 99,
                    RemainingFrames = 1, RemainingSeconds = 0.5f, Flags = k_pending
                };
            }
            m_input[0].Flags |= k_waitNext;
            m_input[0].StartFrame = 100;
            m_input[1].Kind = 1;
            m_input[2].Kind = 1;
            m_input[2].Flags |= k_unscaled;
            m_input[3].Flags |= k_waitNext | k_canceled;
            m_input[3].StartFrame = 100;
            m_input[4].Generation = 6;
            m_input[4].Flags |= k_canceled;
            m_input[5].Flags = k_canceled;
            m_input[6].RemainingFrames = 2;
            m_input[7].Kind = 1;
            m_input[7].RemainingSeconds = 0.25f;
            m_input[7].Flags |= k_unscaled;
            m_input[8].Kind = 1;
            m_input[8].Flags |= k_waitNext | k_unscaled;
            m_input[8].StartFrame = 100;
            m_input[9].RemainingFrames = 0;
            Array.Copy(m_input, m_expected, m_count);
            m_expected[2].RemainingSeconds = 0f;
            m_expected[2].Flags &= ~k_pending;
            m_expected[3].Flags &= ~k_pending;
            m_expected[6].RemainingFrames = 1;
            m_expected[7].RemainingSeconds = -0.25f;
            m_expected[7].Flags &= ~k_pending;
            m_expected[9].RemainingFrames = -1;
            m_expected[9].Flags &= ~k_pending;
            m_expectedCount = 4;
            m_expectedReady[0] = new Token { Slot = 9, Generation = 7, Outcome = 1 };
            m_expectedReady[1] = new Token { Slot = 7, Generation = 7, Outcome = 1 };
            m_expectedReady[2] = new Token { Slot = 3, Generation = 7, Outcome = 2 };
            m_expectedReady[3] = new Token { Slot = 2, Generation = 7, Outcome = 1 };
            SetJobs();
            for (int window = 0; window < 3; window++)
            {
                for (int variant = 0; variant < 3; variant++)
                {
                    report.boundaries[window * 3 + variant] = MeasureBatch(window, variant, 1, 0, variant);
                }
            }
            report.boundariesPassed = true;
        }

        private static string VariantName(int variant)
        {
            return variant == 0 ? "Managed C# arrays" : variant == 1 ? "Non-Burst IJob.Run NativeArrays" : "Burst IJob.Run NativeArrays";
        }

        private void CheckGenerationRejection(Report report)
        {
            PrepareCase(1, 0, 2);
            for (int variant = 0; variant < 3; variant++)
            {
                m_proof = 0;
                m_nativeHeader[1] = 0;
                m_sinks[0] = new Sink { Generation = 7, Completed = 13, Outcome = 17, Order = 19 };
                // This check is untimed: first produce and validate the generation-7 ready token.
                m_work[variant]();
                Validate(variant, false, false);
                Token token = variant == 0 ? m_ready[0] : m_nativeReady[0];
                if (token.Slot != 0 || token.Generation != 7 || token.Outcome != 1)
                {
                    throw new InvalidOperationException("Generation-rejection golden did not produce its valid token.");
                }

                // Simulate a changed synthetic sink generation after scanning but before dispatch.
                m_sinks[0].Generation = 8;
                Sink before = m_sinks[0];
                if (variant == 0)
                {
                    DispatchManaged();
                }
                else
                {
                    DispatchNative();
                }
                Sink after = m_sinks[0];
                if (after.Generation != before.Generation || after.Completed != before.Completed
                    || after.Outcome != before.Outcome || after.Order != before.Order
                    || m_dispatchedCount != 0 || m_dispatchChecksum != 0)
                {
                    throw new InvalidOperationException("Post-scan generation rejection failed for " + VariantName(variant));
                }
                report.generationRejectionVariantsPassed[variant] = true;
            }
            report.generationRejectionPassed = true;
        }

        private static string WindowName(int window)
        {
            return window == 0 ? "Kernel call only" : window == 1 ? "Kernel + synthetic managed dispatch"
                : "Managed-to-native staging + kernel + updated-state/token transfer + synthetic managed dispatch";
        }

        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            if (m_nativeInput.IsCreated)
            {
                m_nativeInput.Dispose();
            }
            if (m_nativeUpdated.IsCreated)
            {
                m_nativeUpdated.Dispose();
            }
            if (m_nativeReady.IsCreated)
            {
                m_nativeReady.Dispose();
            }
            if (m_nativeHeader.IsCreated)
            {
                m_nativeHeader.Dispose();
            }
            m_counter?.Dispose();
            m_counter = null;
        }

        internal struct Record
        {
            public int Slot;
            public int Generation;
            public int CurrentGeneration;
            public int Kind;
            public int StartFrame;
            public int RemainingFrames;
            public float RemainingSeconds;
            public int Flags;
        }

        internal struct Token
        {
            public int Slot;
            public int Generation;
            public int Outcome;
        }

        private struct Sink
        {
            public int Generation;
            public int Completed;
            public int Outcome;
            public int Order;
        }

        internal struct Snapshot
        {
            public int CurrentFrame;
            public float Delta;
            public float UnscaledDelta;
        }

        private struct PlainJob : IJob
        {
            [ReadOnly] public NativeArray<Record> Input;
            [WriteOnly] public NativeArray<Record> Updated;
            [WriteOnly] public NativeArray<Token> Ready;
            [WriteOnly] public NativeArray<int> Header;
            public Snapshot Snapshot;
            public int Count;
            public void Execute() => ScanNative(Input, Updated, Ready, Header, Snapshot, Count);
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
        internal struct BurstJob : IJob
        {
            [ReadOnly] public NativeArray<Record> Input;
            [WriteOnly] public NativeArray<Record> Updated;
            [WriteOnly] public NativeArray<Token> Ready;
            [WriteOnly] public NativeArray<int> Header;
            public Snapshot Snapshot;
            public int Count;
            public void Execute() => ScanNative(Input, Updated, Ready, Header, Snapshot, Count);
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string title = "Update readiness synchronous Burst feasibility";
            public string measurementScope = "Blittable frame/delay records scanned in descending slot order. Both Jobs variants include IJob.Run. "
                + "Kernel-only, kernel+synthetic generation-checked managed sinks, and combined staging/transfer/dispatch windows are separate. "
                + "Managed baseline needs no representation copies. Common delegate/loop/Stopwatch harness costs are included; control is unsubtracted. "
                + "No actual OnityTask source, callback, cancellation token, Unity PlayerLoop, native lifetime, or full async cycle is measured.";
            public string numericalRules = "Generation mismatch/nonpending ignored; cancellation precedes start-frame skip; decrement frame count or selected float delta; "
                + "ready when remaining <= 0; FloatMode.Strict; no absolute deadlines or fast math.";
            public string immutableInput = "Input remains fixed per invocation; separate updated-state and descending ready-token outputs are overwritten. "
                + "Combined native windows copy managed input and transfer all updated records plus ready tokens on EVERY invocation.";
            public string proofScope = "BurstDiscard sentinel written once at each kernel invocation start; checked outside timed batch.";
            public string controlScope = "Empty invocation-count loop with retained checksum and same Stopwatch brackets; unsubtracted.";
            public string order = "Variant order rotates by sample/warmup index modulo three; each raw sample retains its order.";
            public int caseCount = 24;
            public int variants = 3;
            public int windows = 3;
            public int samplesPerMetric = k_samples;
            public int warmupsPerMetric = k_warmups;
            public int retainedNativeRecordCapacity = k_capacity;
            public int retainedNativeTokenCapacity = k_capacity;
            public int recordBytes;
            public int tokenBytes;
            public long allocatedNativeBytes;
            public long nativeTimingArrayBytes = 0;
            public string nativeMemoryScope = "Persistent capacity: input+updated record arrays, ready-token array, two-int count/proof header. "
                + "Payload bytes only; allocator overhead excluded. Timing/report arrays are managed and allocated outside windows.";
            public long stopwatchFrequency = Stopwatch.Frequency;
            public bool completed;
            public string failure;
            public string generatedAtUtc;
            public string platform;
            public string unityVersion;
            public string burstAssembly;
            public bool burstEnabled;
            public string burstFlags = "CompileSynchronously=true; FloatMode.Strict; synchronous IJob.Run; no scheduled jobs.";
            public OnityTaskBenchmarkEnvironment environment;
            public bool allocationAvailable;
            public string allocationScope;
            public string allocationCounterKind;
            public string allocationCounter;
            public long allocationCalibrationBytes;
            public long allocationEmptyBytes;
            public string allocationRejectionReasons;
            public bool boundariesPassed;
            public bool generationRejectionPassed;
            public bool[] generationRejectionVariantsPassed = new bool[3];
            public string generationRejectionScope = "Untimed synthetic sink check for managed, non-Burst native, and Burst native outputs: "
                + "produce a valid generation-7 ready token, change the sink generation to 8 before dispatch, "
                + "then require unchanged completion/outcome/order fields and zero dispatch count/checksum. No pooled-source lifetime is exercised.";
            public string boundaryScope = "Ten handwritten slots: same-frame Frame/Delay skip, scaled pause, unscaled exact/overshoot, cancellation-before-skip, "
                + "stale-canceled ignored, nonpending-canceled ignored, frame pending/zero boundary, descending unique generation-tagged tokens. "
                + "Snapshot currentFrame=100, scaledDelta=0, unscaledDelta=0.5; expected ready slots descending [9,7,3,2], outcomes [1,1,2,1], generation=7.";
            public Sample[] boundaries = new Sample[9];
            public CaseResult[] cases = new CaseResult[24];
        }

        [Serializable]
        private sealed class CaseResult
        {
            public int index;
            public int count;
            public string kind;
            public string density;
            public int expectedReadyCount;
            public long expectedChecksum;
            public int iterationsPerBatch;
            public int currentFrame;
            public float deltaTime;
            public float unscaledDeltaTime;
            public Metric[] metrics;
            public Sample[] controls;
        }

        [Serializable]
        private sealed class Metric
        {
            public string variant;
            public string window;
            public int expectedProof;
            public Sample[] warmups;
            public Sample[] samples;
        }

        [Serializable]
        private sealed class Sample
        {
            public int index;
            public int order;
            public int iterations;
            public long batchTicks;
            public double nanosecondsPerInvocation;
            public double nanosecondsPerInputElement;
            public long managedBytes;
            public bool allocationValid;
            public int proof;
            public int readyCount;
            public long checksum;
            public int dispatchCount;
            public long dispatchChecksum;
            public bool validated;
        }
    }
}
