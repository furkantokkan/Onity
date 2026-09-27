using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Separates serial/Jobs/Burst computation from native job-adapter observation costs.
    /// </summary>
    public sealed class OnityTaskJobBenchmarkRunner : MonoBehaviour
    {
        private const int k_samples = 8;
        private const int k_runs = 2;
        private const int k_batchSize = 64;
        private const uint k_nonBurstProof = 0x4D414E47u;
        private const uint k_burstProof = 0x42555253u;
        private static readonly uint[] s_goldens = { 2780393364u, 649903350u, 1560684188u, 1880411499u };

        private NativeArray<uint> m_input;
        private NativeArray<uint> m_output;
        private NativeArray<uint> m_proof;
        private uint[] m_expected;
        private JobHandle m_handle;
        private bool m_outstanding;
        private bool m_hasSettings;
        private bool m_oldFlow;
        private bool m_oldTracker;
        private bool m_oldStackTrace;
        private int m_oldCapacity;
        private int m_mainThread;
        private string m_path;
        private Action<string, Exception> m_completed;
        private OnityBenchmarkAllocationCounter m_counter;
        private OnityTaskAwaiter m_onity;
        private UniTask.Awaiter m_uniTask;
        private Action m_onityCallback;
        private Action m_uniTaskCallback;
        private bool m_consumed;
        private Exception m_callbackFailure;
        private long m_consumedTimestamp;
        private int m_consumedFrame;

        /// <summary>
        /// Starts the bounded jobs suite and writes its report before invoking completion.
        /// </summary>
        /// <param name="path">Absolute or relative output JSON path.</param>
        /// <param name="completed">Receives the report path and any benchmark failure.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Jobs benchmark output path is required.", nameof(path));
            }

            GameObject owner = new GameObject("Onity Job Benchmark Runner");
            DontDestroyOnLoad(owner);
            OnityTaskJobBenchmarkRunner runner = owner.AddComponent<OnityTaskJobBenchmarkRunner>();
            runner.m_path = Path.GetFullPath(path);
            runner.m_completed = completed;
        }

        private IEnumerator Start()
        {
            Exception failure = null;
            JobReport report = new JobReport();
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
                m_mainThread = Thread.CurrentThread.ManagedThreadId;
                m_onityCallback = ConsumeOnity;
                m_uniTaskCallback = ConsumeUniTask;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                report.isEditor = Application.isEditor;
                report.platform = Application.platform.ToString();
                report.burstAssembly = typeof(BurstCompiler).Assembly.FullName;
                report.burstEnabled = BurstCompiler.IsEnabled;
                if (!report.isEditor && report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Jobs benchmark requires a non-development Release Player.");
                }
                if (report.environment.executionContextPath == "Unknown")
                {
                    throw new InvalidOperationException("Jobs benchmark requires runtime context-path evidence.");
                }

                m_counter = OnityBenchmarkAllocationCounter.Create(false, false);
                report.registrationAllocationAvailable = m_counter.Kind == OnityBenchmarkAllocationCounter.k_kindPerThread;
                report.registrationAllocationCounter = report.registrationAllocationAvailable ? m_counter.Description
                    : "Unavailable: registration requires a calibrated per-thread counter; selected "
                        + m_counter.Kind + " cannot isolate concurrent worker allocation. " + m_counter.RejectedCandidates;
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

        private IEnumerator Measure(JobReport report)
        {
            int[] lengths = { 1024, 65536 };
            for (int size = 0; size < lengths.Length; size++)
            {
                CreateBuffers(lengths[size]);
                for (int variant = 0; variant < 3; variant++)
                {
                    for (int warmup = 0; warmup < 3; warmup++)
                    {
                        MeasureCompute(variant);
                    }
                }

                for (int sample = 0; sample < k_samples; sample++)
                {
                    for (int run = 0; run < k_runs; run++)
                    {
                        for (int turn = 0; turn < 3; turn++)
                        {
                            int variant = (sample + run + turn) % 3;
                            long ticks = MeasureCompute(variant);
                            report.compute[size * 3 + variant].sampleMilliseconds[sample] += Milliseconds(ticks);
                        }
                    }

                    yield return null;
                }

                for (int variant = 0; variant < 3; variant++)
                {
                    ComputeResult result = report.compute[size * 3 + variant];
                    result.length = lengths[size];
                    result.variant = variant == 0 ? "Serial C#" : variant == 1 ? "Jobs without Burst" : "Jobs with Burst";
                    result.expectedProof = variant == 2 ? k_burstProof : k_nonBurstProof;
                    result.validatedExecutions = 3 + k_samples * k_runs;
                    result.meanMillisecondsPerRun = Mean(result.sampleMilliseconds) / k_runs;
                }
            }

            // The second compute size supplies the adapter panel's same 65536-element Burst job.
            for (int cohort = 0; cohort < 2 + k_samples * k_runs; cohort++)
            {
                for (int turn = 0; turn < 2; turn++)
                {
                    int library = (cohort + turn) & 1;
                    AdapterResult result = report.adapters[library];
                    bool eligible = false;
                    while (!eligible && result.exclusions <= 3)
                    {
                        AdapterObservation observation = new AdapterObservation
                        {
                            warmup = cohort < 2,
                            sample = cohort < 2 ? -1 : (cohort - 2) / k_runs,
                            run = cohort < 2 ? cohort : (cohort - 2) % k_runs
                        };
                        IEnumerator attempt = MeasureAdapter(library, observation, report.registrationAllocationAvailable);
                        try
                        {
                            while (attempt.MoveNext())
                            {
                                yield return null;
                            }
                        }
                        finally
                        {
                            (attempt as IDisposable)?.Dispose();
                        }

                        result.observations.Add(observation);
                        result.attempts++;
                        eligible = observation.eligible;
                        if (eligible)
                        {
                            if (observation.warmup)
                            {
                                result.warmups++;
                            }
                            else
                            {
                                result.measured++;
                            }
                        }
                        else
                        {
                            result.exclusions++;
                        }

                        yield return null;
                        yield return null;
                    }

                    if (!eligible)
                    {
                        report.pendingAdapterAvailable = false;
                        report.pendingAdapterUnavailableReason = result.library
                            + " exceeded three extra eligibility attempts; paired pending panel unavailable. "
                            + "Successful raw observations and exclusions are retained.";
                        yield break;
                    }
                }
            }

            for (int library = 0; library < report.adapters.Length; library++)
            {
                AdapterResult result = report.adapters[library];
                if (result.warmups != 2 || result.measured != k_samples * k_runs)
                {
                    throw new InvalidOperationException("Eligible adapter cohort count is incomplete for " + result.library + ".");
                }
            }

            report.pendingAdapterAvailable = true;
        }

        private long MeasureCompute(int variant)
        {
            m_proof[0] = 0;
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (variant == 0)
                {
                    for (int i = 0; i < m_input.Length; i++)
                    {
                        WriteOutput(i, m_input, m_output, m_proof);
                    }
                }
                else
                {
                    Schedule(variant == 2);
                    CompleteOutstanding();
                }
            }
            finally
            {
                CompleteOutstanding();
            }

            long stopped = Stopwatch.GetTimestamp();
            ValidateOutput(variant == 2);
            return stopped - started;
        }

        private IEnumerator MeasureAdapter(int library, AdapterObservation observation, bool allocationAvailable)
        {
            m_proof[0] = 0;
            m_consumed = false;
            m_callbackFailure = null;
            observation.scheduledFrame = Time.frameCount;
            long scheduled = Stopwatch.GetTimestamp();
            try
            {
                Schedule(true);
                JobHandle.ScheduleBatchedJobs();
                observation.handlePending = !m_handle.IsCompleted;
                if (!observation.handlePending)
                {
                    CompleteOutstanding();
                    observation.exclusionReason = "Handle completed before adapter registration.";
                }
                else
                {
                    long bytes = allocationAvailable ? m_counter.Read() : 0;
                    long started = Stopwatch.GetTimestamp();
                    if (library == 0)
                    {
                        m_onity = m_handle.AsOnityTask().GetAwaiter();
                        observation.awaiterPending = !m_onity.IsCompleted;
                        if (observation.awaiterPending)
                        {
                            m_onity.UnsafeOnCompleted(m_onityCallback);
                        }
                    }
                    else
                    {
                        m_uniTask = m_handle.ToUniTask(PlayerLoopTiming.Update).GetAwaiter();
                        observation.awaiterPending = !m_uniTask.IsCompleted;
                        if (observation.awaiterPending)
                        {
                            m_uniTask.UnsafeOnCompleted(m_uniTaskCallback);
                        }
                    }

                    long stopped = Stopwatch.GetTimestamp();
                    observation.registrationMilliseconds = Milliseconds(stopped - started);
                    observation.registrationBytes = allocationAvailable ? m_counter.Read() - bytes : -1;
                    if (!observation.awaiterPending)
                    {
                        if (library == 0)
                        {
                            ConsumeOnity();
                        }
                        else
                        {
                            ConsumeUniTask();
                        }
                        observation.exclusionReason = "Returned awaiter was already complete.";
                    }
                    else
                    {
                        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 30;
                        while (!m_consumed)
                        {
                            if (Stopwatch.GetTimestamp() >= deadline)
                            {
                                throw new TimeoutException("Pending job adapter did not complete within 30 seconds.");
                            }

                            yield return null;
                        }

                        observation.eligible = true;
                    }

                    if (m_callbackFailure != null)
                    {
                        throw new InvalidOperationException("Job adapter native consumption failed.", m_callbackFailure);
                    }

                    observation.completionMilliseconds = Milliseconds(m_consumedTimestamp - scheduled);
                    observation.completedFrame = m_consumedFrame;
                }
            }
            finally
            {
                CompleteOutstanding();
            }

            if (!observation.handlePending)
            {
                observation.completedFrame = Time.frameCount;
                observation.completionMilliseconds = Milliseconds(Stopwatch.GetTimestamp() - scheduled);
            }

            ValidateOutput(true);
        }

        private void ConsumeOnity()
        {
            Consume(0);
        }

        private void ConsumeUniTask()
        {
            Consume(1);
        }

        private void Consume(int library)
        {
            try
            {
                if (m_consumed)
                {
                    throw new InvalidOperationException("A job adapter callback ran twice.");
                }

                if (Thread.CurrentThread.ManagedThreadId != m_mainThread)
                {
                    throw new InvalidOperationException("Job adapter consumption did not run on the main thread.");
                }

                if (library == 0)
                {
                    m_onity.GetResult();
                }
                else
                {
                    m_uniTask.GetResult();
                }
            }
            catch (Exception exception)
            {
                m_callbackFailure = exception;
            }
            finally
            {
                m_consumedTimestamp = Stopwatch.GetTimestamp();
                m_consumedFrame = Thread.CurrentThread.ManagedThreadId == m_mainThread ? Time.frameCount : -1;
                m_consumed = true;
            }
        }

        private void Schedule(bool burst)
        {
            m_handle = burst
                ? new BurstKernelJob { Input = m_input, Output = m_output, Proof = m_proof }.Schedule(m_input.Length, k_batchSize)
                : new PlainKernelJob { Input = m_input, Output = m_output, Proof = m_proof }.Schedule(m_input.Length, k_batchSize);
            m_outstanding = true;
        }

        private void CompleteOutstanding()
        {
            if (!m_outstanding)
            {
                return;
            }

            m_handle.Complete();
            m_outstanding = false;
            m_handle = default;
        }

        private void CreateBuffers(int length)
        {
            CleanupBuffers();
            m_input = new NativeArray<uint>(length, Allocator.Persistent);
            m_output = new NativeArray<uint>(length, Allocator.Persistent);
            m_proof = new NativeArray<uint>(length, Allocator.Persistent);
            m_expected = new uint[length];
            for (int i = 0; i < length; i++)
            {
                uint input = unchecked(0xA341316Cu ^ ((uint)i * 747796405u));
                m_input[i] = input;
                m_expected[i] = Calculate(input);
            }
        }

        private void ValidateOutput(bool burst)
        {
            uint proof = burst ? k_burstProof : k_nonBurstProof;
            if (m_proof[0] != proof)
            {
                throw new InvalidOperationException("Execution did not produce the expected BurstDiscard proof: "
                    + m_proof[0] + " instead of " + proof + ".");
            }

            for (int i = 0; i < m_output.Length; i++)
            {
                if (m_output[i] != m_expected[i] || (i < s_goldens.Length && m_output[i] != s_goldens[i]))
                {
                    throw new InvalidOperationException("Job kernel output mismatch at index " + i + ".");
                }
            }
        }

        private static uint Calculate(uint x)
        {
            unchecked
            {
                for (int round = 0; round < 32; round++)
                {
                    x ^= x << 13;
                    x ^= x >> 17;
                    x ^= x << 5;
                    x += 0x9E3779B9u + (uint)round;
                }
            }

            return x;
        }

        private static void WriteOutput(int index, NativeArray<uint> input, NativeArray<uint> output, NativeArray<uint> proof)
        {
            output[index] = Calculate(input[index]);
            if (index == 0)
            {
                bool nonBurst = false;
                MarkNonBurst(ref nonBurst);
                proof[0] = nonBurst ? k_nonBurstProof : k_burstProof;
            }
        }

        [BurstDiscard]
        private static void MarkNonBurst(ref bool nonBurst)
        {
            nonBurst = true;
        }

        private struct PlainKernelJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<uint> Input;
            [WriteOnly] public NativeArray<uint> Output;
            [WriteOnly] public NativeArray<uint> Proof;

            public void Execute(int index)
            {
                WriteOutput(index, Input, Output, Proof);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct BurstKernelJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<uint> Input;
            [WriteOnly] public NativeArray<uint> Output;
            [WriteOnly] public NativeArray<uint> Proof;

            public void Execute(int index)
            {
                WriteOutput(index, Input, Output, Proof);
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

        private void Cleanup()
        {
            CleanupBuffers();
            m_counter?.Dispose();
            m_counter = null;
        }

        private void CleanupBuffers()
        {
            CompleteOutstanding();
            if (m_input.IsCreated)
            {
                m_input.Dispose();
            }
            if (m_output.IsCreated)
            {
                m_output.Dispose();
            }
            if (m_proof.IsCreated)
            {
                m_proof.Dispose();
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

        private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

        private static double Mean(double[] samples)
        {
            double total = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                total += samples[i];
            }
            return total / samples.Length;
        }

        [Serializable]
        private sealed class JobReport
        {
            public int schemaVersion = 1;
            public string suite = "jobs";
            public bool completed;
            public string failure;
            public string generatedAtUtc;
            public OnityTaskBenchmarkEnvironment environment;
            public bool isEditor;
            public string platform;
            public string burstAssembly;
            public bool burstEnabled;
            public string kernel = "uint xorshift/add; 32 rounds; input 0xA341316C xor index*747796405";
            public int batchSize = k_batchSize;
            public int samples = k_samples;
            public int runsPerSample = k_runs;
            public int computeWarmupsPerVariant = 3;
            public string computeScope = "Serial C# loop or Schedule through Complete; validation excluded. IL2CPP also executes serial/plain C# as native code.";
            public string adapterScope = "One outstanding 65536-element Burst job; registration elapsed vs Schedule-to-native-consumption wall interval. "
                + "Onity MonoBehaviour Update and UniTask injected Update differ; latency is not scheduler CPU superiority. "
                + "Two real drain frames outside measurements. Native callbacks do not capture execution context.";
            public bool registrationAllocationAvailable;
            public string registrationAllocationCounter;
            public string allocationRejectionReasons;
            public string crossWorkerAllocation = "Unavailable: cross-worker lifecycle allocation not measured.";
            public bool pendingAdapterAvailable;
            public string pendingAdapterUnavailableReason;
            public string pendingAdapterEligibilityPolicy = "Both handle and returned awaiter must be incomplete; "
                + "two eligible warmups plus sixteen eligible measured cohorts per library; "
                + "at most three extra attempts per library over the entire panel. "
                + "A fourth exclusion exhausts replacement attempts and makes the paired panel unavailable.";
            public ComputeResult[] compute =
            {
                new ComputeResult(), new ComputeResult(), new ComputeResult(),
                new ComputeResult(), new ComputeResult(), new ComputeResult()
            };
            public AdapterResult[] adapters =
            {
                new AdapterResult { library = "OnityTask" }, new AdapterResult { library = "UniTask" }
            };
        }

        [Serializable]
        private sealed class ComputeResult
        {
            public string variant;
            public int length;
            public uint expectedProof;
            public int validatedExecutions;
            public double meanMillisecondsPerRun;
            public double[] sampleMilliseconds = new double[k_samples];
        }

        [Serializable]
        private sealed class AdapterResult
        {
            public string library;
            public int warmups;
            public int measured;
            public int exclusions;
            public int attempts;
            public List<AdapterObservation> observations = new List<AdapterObservation>(21);
        }

        [Serializable]
        private sealed class AdapterObservation
        {
            public bool warmup;
            public int sample;
            public int run;
            public bool handlePending;
            public bool awaiterPending;
            public bool eligible;
            public string exclusionReason;
            public int scheduledFrame;
            public int completedFrame;
            public double registrationMilliseconds = -1;
            public long registrationBytes = -1;
            public double completionMilliseconds;
        }
    }
}
