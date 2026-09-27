using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Profiling;

namespace Onity.Benchmarks
{
    /// <summary>Bounded Development Player lifecycle attribution, separate from Release timing.</summary>
    public sealed class OnityTaskFullCycleProfileRunner : MonoBehaviour
    {
        private const int k_batches = 2;
        private const int k_timeoutFrames = 240;
        private const int k_profilerMemoryBytes = 512 * 1024 * 1024;
        private string m_output;
        private string m_rawPath;
        private int m_concurrency;
        private bool m_flow;
        private bool m_drain;
        private Action<string, Exception> m_completed;
        private OnityTaskAwaiter<int>[] m_onity;
        private UniTask<int>.Awaiter[] m_uniTask;
        private bool m_originalFlow;
        private bool m_originalTracking;
        private bool m_originalStackTrace;
        private int m_originalCapacity;
        private bool m_hasSettings;

        /// <summary>Starts one concurrency/flow/reuse diagnostic cohort containing both libraries.</summary>
        /// <param name="output">JSON report path. Raw profiler data replaces its extension with .raw.</param>
        /// <param name="completed">Callback receiving the report path and any failure.</param>
        public static void Run(string output, Action<string, Exception> completed)
        {
            if (!Debug.isDebugBuild)
            {
                throw new InvalidOperationException("fullcycle requires a Development Player with deep profiling support.");
            }
#if !ENABLE_IL2CPP
            throw new InvalidOperationException("fullcycle requires the diagnostic IL2CPP Player.");
#else
            string concurrency = GetArgument("-onityTaskProfileConcurrency");
            string flow = GetArgument("-onityTaskProfileFlow");
            string reuse = GetArgument("-onityTaskProfileReuse");
            if ((concurrency != "128" && concurrency != "4096") || (flow != "on" && flow != "off")
                || (reuse != "immediate" && reuse != "drain"))
            {
                throw new ArgumentException("fullcycle requires concurrency 128|4096, flow on|off, reuse immediate|drain.");
            }

            GameObject runnerObject = new GameObject("Onity Full Cycle Profile Runner");
            DontDestroyOnLoad(runnerObject);
            OnityTaskFullCycleProfileRunner runner = runnerObject.AddComponent<OnityTaskFullCycleProfileRunner>();
            runner.m_output = Path.GetFullPath(output);
            runner.m_rawPath = Path.ChangeExtension(runner.m_output, ".raw");
            runner.m_concurrency = int.Parse(concurrency);
            runner.m_flow = flow == "on";
            runner.m_drain = reuse == "drain";
            runner.m_completed = completed;
            runner.m_onity = new OnityTaskAwaiter<int>[runner.m_concurrency];
            runner.m_uniTask = new UniTask<int>.Awaiter[runner.m_concurrency];
#endif
        }

        private IEnumerator Start()
        {
            Exception failure = null;
            ProfileReport report = new ProfileReport
            {
                concurrency = m_concurrency,
                flowExecutionContext = m_flow,
                reuse = m_drain ? "drain" : "immediate",
                rawProfile = m_rawPath,
                warmupBatchesPerLibrary = k_batches,
                capturedBatchesPerLibrary = k_batches,
                terminalDrainFrames = 2,
                profilerBufferBytes = k_profilerMemoryBytes,
                attributionOnly = true,
                windows = new ProfileWindow[2]
            };
            m_originalFlow = OnityTask.FlowExecutionContext;
            m_originalTracking = OnityTaskTracker.IsEnabled;
            m_originalStackTrace = OnityTaskTracker.EnableStackTrace;
            m_originalCapacity = OnityTask.RunnerPoolCapacity;
            m_hasSettings = true;
            try
            {
                OnityTask.FlowExecutionContext = m_flow;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                OnityTask.RunnerPoolCapacity = 128;
                report.environment = OnityTaskBenchmarkEnvironment.Capture();
                if (report.environment.executionContextPath == "Unknown")
                {
                    throw new InvalidOperationException("Profile execution-context path could not be proven.");
                }
                Profiler.enabled = false;
                Profiler.logFile = m_rawPath;
                Profiler.enableBinaryLog = true;
                Profiler.maxUsedMemory = k_profilerMemoryBytes;
                Profiler.SetAreaEnabled(ProfilerArea.CPU, true);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            IEnumerator routine = ProfileLibraries(report);
            try
            {
                while (failure == null)
                {
                    bool next = false;
                    try
                    {
                        next = routine.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }

                    if (failure != null || !next)
                    {
                        break;
                    }

                    yield return null;
                }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                StopProfile();
                RestoreSettings();
            }

            report.completed = failure == null;
            report.failure = failure?.ToString();
            report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            try
            {
                if (failure == null && (!File.Exists(m_rawPath) || new FileInfo(m_rawPath).Length == 0))
                {
                    throw new IOException("Profiler did not produce nonempty raw data: " + m_rawPath);
                }

                File.WriteAllText(m_output, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                m_completed?.Invoke(m_output, failure);
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        private IEnumerator ProfileLibraries(ProfileReport report)
        {
            for (int library = 0; library < 2; library++)
            {
                string label = library == 0 ? "Onity" : "UniTask";
                string prefix = "Onity.FullCycle." + label;
                string scheduleMarker = prefix + ".Schedule";
                string consumeMarker = prefix + ".Consume";
                ProfileWindow window = new ProfileWindow
                {
                    library = label,
                    beginMarker = prefix + ".Begin",
                    endMarker = prefix + ".End"
                };
                report.windows[library] = window;
                OnityTaskBenchmarkPlayerRunner.WriteStartupMarker("profile-warmup", label);
                for (int batch = 0; batch < k_batches; batch++)
                {
                    Schedule(library);
                    int remaining = k_timeoutFrames;
                    do
                    {
                        yield return null;
                        if (--remaining == 0)
                        {
                            throw new TimeoutException(label + " fullcycle warmup did not complete.");
                        }
                    }
                    while (!IsCompleted(library));
                    Consume(library);
                    yield return null;
                    yield return null;
                }

                OnityTaskBenchmarkPlayerRunner.WriteStartupMarker("profile-capture-start", label);
                Profiler.enabled = true;
                // Start in a fresh recorded frame. Instantaneous stamps delimit the capture;
                // sample scopes never cross coroutine yields.
                yield return null;
                window.beginPlayerFrame = Time.frameCount;
                Profiler.BeginSample(window.beginMarker);
                Profiler.EndSample();
                for (int batch = 0; batch < k_batches; batch++)
                {
                    Profiler.BeginSample(scheduleMarker);
                    try
                    {
                        Schedule(library);
                    }
                    finally
                    {
                        Profiler.EndSample();
                    }

                    int remaining = k_timeoutFrames;
                    do
                    {
                        yield return null;
                        if (--remaining == 0)
                        {
                            throw new TimeoutException(label + " fullcycle capture did not complete.");
                        }
                    }
                    while (!IsCompleted(library));
                    Profiler.BeginSample(consumeMarker);
                    try
                    {
                        Consume(library);
                        window.completedBatches++;
                        window.consumedOperations += m_concurrency;
                    }
                    finally
                    {
                        Profiler.EndSample();
                    }

                    // Immediate mode deliberately schedules batch two in this same MoveNext.
                    // Drain mode gives both libraries two actual Update phases before re-rent.
                    if (batch + 1 < k_batches && m_drain)
                    {
                        yield return null;
                        yield return null;
                    }
                }

                yield return null;
                yield return null;
                window.endPlayerFrame = Time.frameCount;
                Profiler.BeginSample(window.endMarker);
                Profiler.EndSample();
                // Flush the frame containing End before disabling capture.
                yield return null;
                yield return null;
                Profiler.enabled = false;
                window.completed = true;
                OnityTaskBenchmarkPlayerRunner.WriteStartupMarker("profile-capture-completed", label);
            }
        }

        private void Schedule(int library)
        {
            for (int i = 0; i < m_concurrency; i++)
            {
                if (library == 0)
                {
                    m_onity[i] = OnityNextFrameAsync().GetAwaiter();
                }
                else
                {
                    m_uniTask[i] = UniTaskNextFrameAsync().GetAwaiter();
                }
            }
        }

        private bool IsCompleted(int library)
        {
            for (int i = 0; i < m_concurrency; i++)
            {
                if (library == 0 ? !m_onity[i].IsCompleted : !m_uniTask[i].IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }

        private void Consume(int library)
        {
            for (int i = 0; i < m_concurrency; i++)
            {
                if (library == 0)
                {
                    if (m_onity[i].GetResult() != 42)
                    {
                        throw new InvalidOperationException("Onity fullcycle result mismatch.");
                    }
                    m_onity[i] = default;
                }
                else
                {
                    if (m_uniTask[i].GetResult() != 42)
                    {
                        throw new InvalidOperationException("UniTask fullcycle result mismatch.");
                    }
                    m_uniTask[i] = default;
                }
            }
        }

        private static async OnityTask<int> OnityNextFrameAsync()
        {
            await OnityTask.NextFrame();
            Profiler.BeginSample("Onity.FullCycle.Onity.Resumed");
            Profiler.EndSample();
            return 42;
        }

        private static async UniTask<int> UniTaskNextFrameAsync()
        {
            await UniTask.NextFrame();
            Profiler.BeginSample("Onity.FullCycle.UniTask.Resumed");
            Profiler.EndSample();
            return 42;
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static void StopProfile()
        {
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.logFile = string.Empty;
        }

        private void RestoreSettings()
        {
            if (!m_hasSettings)
            {
                return;
            }

            OnityTask.FlowExecutionContext = m_originalFlow;
            OnityTaskTracker.IsEnabled = m_originalTracking;
            OnityTaskTracker.EnableStackTrace = m_originalStackTrace;
            OnityTask.RunnerPoolCapacity = m_originalCapacity;
            m_hasSettings = false;
        }

        private void OnDestroy()
        {
            StopProfile();
            RestoreSettings();
        }

        [Serializable]
        private sealed class ProfileReport
        {
            public int schemaVersion = 1;
            public string generatedAtUtc;
            public bool attributionOnly;
            public bool completed;
            public string failure;
            public int concurrency;
            public bool flowExecutionContext;
            public string reuse;
            public int warmupBatchesPerLibrary;
            public int capturedBatchesPerLibrary;
            public int terminalDrainFrames;
            public long profilerBufferBytes;
            public string rawProfile;
            public OnityTaskBenchmarkEnvironment environment;
            public ProfileWindow[] windows;
        }

        [Serializable]
        private sealed class ProfileWindow
        {
            public string library;
            public string beginMarker;
            public string endMarker;
            public int beginPlayerFrame;
            public int endPlayerFrame;
            public bool completed;
            public int completedBatches;
            public int consumedOperations;
        }
    }
}
