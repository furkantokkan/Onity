using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Scripting;

namespace Onity.Benchmarks
{
    /// <summary>
    /// End-to-end throughput under the real PlayerLoop. Per library, N concurrent loops each await the
    /// library's default NextFrame or Yield K times. A harness PlayerLoop system placed first in the frame,
    /// outside both libraries' systems, schedules, polls one completion counter and consumes; elapsed time
    /// runs from the start of the scheduling frame to the frame-start point at which every loop's task has
    /// been consumed, minus the median of three adjacent idle control windows of the same frame count.
    /// The result includes engine frame overhead only through that control subtraction.
    /// </summary>
    public sealed class OnityTaskThroughputBenchmarkRunner : MonoBehaviour
    {
        private const int k_awaitsPerLoop = 16;
        private const int k_maxLoops = 4096;
        private const int k_warmups = 2;
        private const int k_samples = 8;
        private const int k_selfTestWarmups = 1;
        private const int k_selfTestSamples = 2;
        private const int k_controlWindows = 3;
        private const int k_idleFramesBeforeFirstArm = 3;
        private const int k_typedResult = 42;

        private enum Workload
        {
            NextFrame,
            Yield
        }

        private enum Stage
        {
            Idle,
            Control,
            Cohort,
            Trailing,
            Finishing
        }

        private sealed class FrameStartMarker
        {
        }

        private static readonly PlayerLoopSystem.UpdateFunction s_frameStart = FrameStartSystem;
        private static OnityTaskThroughputBenchmarkRunner s_active;

        // Incremented once by every loop body of both libraries when its last await returned.
        private static int s_finished;

        private readonly OnityTask[] m_onity = new OnityTask[k_maxLoops];
        private readonly OnityTask<int>[] m_onityTyped = new OnityTask<int>[k_maxLoops];
        private readonly UniTask[] m_uni = new UniTask[k_maxLoops];
        private readonly UniTask<int>[] m_uniTyped = new UniTask<int>[k_maxLoops];
        private readonly long[] m_controlTicks = new long[k_controlWindows];
        private readonly List<Arm> m_armReports = new List<Arm>(8);

        private string m_path;
        private Action<string, Exception> m_completed;
        private Report m_report;
        private Exception m_failure;
        private string m_loggedError;
        private bool m_listening;
        private bool m_installed;
        private bool m_hasSettings;
        private bool m_oldFlow;
        private bool m_oldTracker;
        private bool m_oldStackTrace;
        private int m_oldRunnerCapacity;
        private GarbageCollector.Mode m_oldGcMode;
        private OnityBenchmarkAllocationCounter m_counter;
        private bool m_counterCrossFrame;
        private long m_sliceStart;
        private bool m_sliceOpen;
        private OnityTaskBenchmarkInternals.PendingWorkProbe m_onityProbe;

        // Pinned UniTask 2.5.11 queue diagnostics, read only outside timings.
        private FieldInfo m_uniRunnersField;
        private FieldInfo m_uniYieldersField;
        private FieldInfo m_uniRunnerTail;
        private FieldInfo m_uniRunnerRunning;
        private FieldInfo m_uniRunnerWaitQueue;
        private FieldInfo m_uniQueueSize;
        private FieldInfo m_uniActionCount;
        private FieldInfo m_uniWaitingCount;
        private FieldInfo m_uniDequing;

        private bool m_selfTest;
        private int m_warmups;
        private int m_samples;
        private ArmDefinition[] m_arms;
        private int m_armIndex;
        private ArmDefinition m_arm;
        private Arm m_armReport;
        private OnityTaskRetentionScope m_retention;
        private int m_armFrames;
        private int m_cohortIndex;
        private int m_totalCohorts;

        private Stage m_stage;
        private int m_idleFrames;
        private int m_lastFrameCount = -1;
        private int m_frameStartCalls;
        private int m_frameStartAnomalies;
        private int m_framesPerWindow;
        private int m_controlFrame;
        private long m_windowStart;
        private int m_cohortFrame;
        private int m_maxCohortFrames;
        private long m_cohortStart;
        private long m_cohortEnd;
        private int m_cohortFrames;
        private bool m_isWarmup;
        private int m_index;
        private int m_turn;
        private bool m_onityActive;
        private Sample m_sample;
        private int m_count;
        private int m_scheduled;
        private int m_consumed;
        private int m_loopsAtConsume;
        private long m_resultSum;
        private int m_exceptions;
        private Exception m_firstException;
        private int m_validatedCohorts;
        private int m_expectedCohorts;

        /// <summary>Runs the Release Player throughput suite and writes its versioned JSON report.</summary>
        /// <param name="path">Output JSON path.</param>
        /// <param name="completed">Receives the report path and any failure after cleanup.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Throughput output path is required.", nameof(path));
            }
            if (s_active != null)
            {
                throw new InvalidOperationException("A throughput benchmark is already running.");
            }

            GameObject owner = new GameObject("Onity Throughput Benchmark");
            DontDestroyOnLoad(owner);
            OnityTaskThroughputBenchmarkRunner runner = owner.AddComponent<OnityTaskThroughputBenchmarkRunner>();
            runner.m_path = Path.GetFullPath(path);
            runner.m_completed = completed;
            s_active = runner;
        }

        // The harness declares no Update, FixedUpdate or LateUpdate: all of its work runs in its own
        // frame-start PlayerLoop system, so ScriptRunBehaviourUpdate holds only the libraries' work.
        private void Start()
        {
            m_report = new Report();
            try
            {
                Application.logMessageReceived += OnLog;
                m_listening = true;
                m_oldFlow = OnityTask.FlowExecutionContext;
                m_oldTracker = OnityTaskTracker.IsEnabled;
                m_oldStackTrace = OnityTaskTracker.EnableStackTrace;
                m_oldRunnerCapacity = OnityTask.RunnerPoolCapacity;
                m_oldGcMode = GarbageCollector.GCMode;
                m_hasSettings = true;
                OnityTask.FlowExecutionContext = false;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;

                m_selfTest = OnityTaskBenchmarkOptions.IsSelfTest();
                m_warmups = m_selfTest ? k_selfTestWarmups : k_warmups;
                m_samples = m_selfTest ? k_selfTestSamples : k_samples;
                m_report.selfTest = m_selfTest;
                m_report.warmupsPerLibrary = m_warmups;
                m_report.samplesPerLibrary = m_samples;
                m_report.onityFlowExecutionContext = OnityTask.FlowExecutionContext;
                m_report.defaultRunnerPoolCapacity = m_oldRunnerCapacity;
                m_report.sourcePoolCapacityAvailable = OnityTaskBenchmarkOptions.SourcePoolCapacityAvailable;
                m_report.defaultSourcePoolCapacity = OnityTaskBenchmarkOptions.ReadSourcePoolCapacity();
                m_report.environment = OnityTaskBenchmarkEnvironment.Capture();
                if (Application.isEditor || m_report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Throughput requires a non-development Release Player.");
                }

                m_arms = CreateArms();
                m_report.arms = new Arm[0];
                m_report.predeclaredArms = new string[m_arms.Length];
                for (int i = 0; i < m_arms.Length; i++)
                {
                    m_report.predeclaredArms[i] = m_arms[i].Id;
                }

                BindUniTaskDiagnostics();
                m_onityProbe = OnityTaskBenchmarkInternals.BindPendingWorkProbe();
                m_report.onityIdleProbe = m_onityProbe.Description;
                m_report.onityIdleCheck = m_onityProbe.IsAvailable ? "verified through bound probes" : "unavailable: no probe bound";

                m_counter = OnityBenchmarkAllocationCounter.Create(false, true);
                RestoreCollector();
                m_counterCrossFrame = m_counter.IsAvailable && m_counter.SupportsCrossFrameSlices;
                m_report.counterKind = m_counter.Kind;
                m_report.counterDescription = m_counter.Description;
                m_report.counterRejections = m_counter.RejectedCandidates;
                m_report.counterCalibrationBytes = m_counter.CalibrationBytes;
                m_report.counterEmptyBytes = m_counter.EmptyDeltaBytes;
                m_report.counterCalibrated = m_counterCrossFrame;

                InstallSystem();
                CheckIdle("before the first arm");
                m_stage = Stage.Idle;
            }
            catch (Exception exception)
            {
                m_failure = exception;
                RequestFinish();
            }
        }

        private static ArmDefinition[] CreateArms()
        {
            return new[]
            {
                new ArmDefinition("nextframe-n1024", Workload.NextFrame, false, 1024),
                new ArmDefinition("nextframe-n4096", Workload.NextFrame, false, 4096),
                new ArmDefinition("yield-n1024", Workload.Yield, false, 1024),
                new ArmDefinition("yield-n4096", Workload.Yield, false, 4096),
                new ArmDefinition("nextframe-typed-n1024", Workload.NextFrame, true, 1024)
            };
        }

        private void BindUniTaskDiagnostics()
        {
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            m_uniRunnersField = typeof(PlayerLoopHelper).GetField("runners", staticFlags);
            m_uniYieldersField = typeof(PlayerLoopHelper).GetField("yielders", staticFlags);
            Array runners = m_uniRunnersField?.GetValue(null) as Array;
            Array yielders = m_uniYieldersField?.GetValue(null) as Array;
            object runner = runners != null && runners.Length > (int)PlayerLoopTiming.Update
                ? runners.GetValue((int)PlayerLoopTiming.Update) : null;
            object queue = yielders != null && yielders.Length > (int)PlayerLoopTiming.Update
                ? yielders.GetValue((int)PlayerLoopTiming.Update) : null;
            if (runner == null || queue == null)
            {
                throw new InvalidOperationException("Pinned UniTask Update runner/yielder is not injected.");
            }

            Type runnerType = runner.GetType();
            Type queueType = queue.GetType();
            m_uniRunnerTail = runnerType.GetField("tail", instanceFlags);
            m_uniRunnerRunning = runnerType.GetField("running", instanceFlags);
            m_uniRunnerWaitQueue = runnerType.GetField("waitQueue", instanceFlags);
            object waitQueue = m_uniRunnerWaitQueue?.GetValue(runner);
            m_uniQueueSize = waitQueue?.GetType().GetField("size", instanceFlags);
            m_uniActionCount = queueType.GetField("actionListCount", instanceFlags);
            m_uniWaitingCount = queueType.GetField("waitingListCount", instanceFlags);
            m_uniDequing = queueType.GetField("dequing", instanceFlags);
            if (runnerType.FullName != "Cysharp.Threading.Tasks.Internal.PlayerLoopRunner"
                || queueType.FullName != "Cysharp.Threading.Tasks.Internal.ContinuationQueue"
                || m_uniRunnerTail == null || m_uniRunnerRunning == null || m_uniQueueSize == null
                || m_uniActionCount == null || m_uniWaitingCount == null || m_uniDequing == null)
            {
                throw new InvalidOperationException("Pinned UniTask queue diagnostics unavailable or AOT stripped.");
            }

            m_report.uniTaskIdleProbe = "PlayerLoopHelper.runners[*] tail/running/waitQueue.size and yielders[*] "
                + "actionListCount/waitingListCount/dequing (pinned 2.5.11).";
        }

        private void InstallSystem()
        {
            // Install Onity's explicit timing nodes before measuring so no window pays for their insertion.
            OnityTaskPlayerLoop.Initialize();
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveHarnessSystem(ref loop);
            PlayerLoopSystem[] roots = loop.subSystemList;
            if (roots == null || roots.Length == 0)
            {
                throw new InvalidOperationException("The PlayerLoop has no phases.");
            }

            roots[0].subSystemList = Prepend(roots[0].subSystemList,
                new PlayerLoopSystem { type = typeof(FrameStartMarker), updateDelegate = s_frameStart });
            loop.subSystemList = roots;
            PlayerLoop.SetPlayerLoop(loop);
            m_installed = true;

            PlayerLoopSystem installed = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] phases = installed.subSystemList;
            if (phases == null || phases.Length == 0 || phases[0].subSystemList == null
                || phases[0].subSystemList.Length == 0 || phases[0].subSystemList[0].type != typeof(FrameStartMarker))
            {
                throw new InvalidOperationException("The frame-start system is not the first system of the PlayerLoop.");
            }

            m_report.rootPhases = new string[phases.Length];
            for (int i = 0; i < phases.Length; i++)
            {
                m_report.rootPhases[i] = phases[i].type?.Name;
            }

            PlayerLoopSystem[] first = phases[0].subSystemList;
            int shown = Math.Min(first.Length, 6);
            m_report.firstPhaseSystems = new string[shown];
            for (int i = 0; i < shown; i++)
            {
                m_report.firstPhaseSystems[i] = first[i].type?.FullName;
            }

            m_report.frameStartSystem = phases[0].type?.Name + "[0]";
            m_report.harnessSystemsInstalled = CountHarnessSystems(installed);
            m_report.harnessDeclaresUnityUpdate = DeclaresUnityUpdate(GetType());
            if (m_report.harnessSystemsInstalled != 1 || m_report.harnessDeclaresUnityUpdate)
            {
                throw new InvalidOperationException("Exactly one frame-start harness system and no harness Update are required.");
            }
        }

        private static PlayerLoopSystem[] Prepend(PlayerLoopSystem[] systems, PlayerLoopSystem node)
        {
            int length = systems == null ? 0 : systems.Length;
            var expanded = new PlayerLoopSystem[length + 1];
            expanded[0] = node;
            if (length > 0)
            {
                Array.Copy(systems, 0, expanded, 1, length);
            }
            return expanded;
        }

        private static int CountHarnessSystems(PlayerLoopSystem system)
        {
            int count = system.type == typeof(FrameStartMarker) ? 1 : 0;
            PlayerLoopSystem[] children = system.subSystemList;
            for (int i = 0; children != null && i < children.Length; i++)
            {
                count += CountHarnessSystems(children[i]);
            }
            return count;
        }

        private static void RemoveHarnessSystem(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
            {
                return;
            }
            var retained = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (PlayerLoopSystem childValue in system.subSystemList)
            {
                if (childValue.type == typeof(FrameStartMarker))
                {
                    continue;
                }
                PlayerLoopSystem child = childValue;
                RemoveHarnessSystem(ref child);
                retained.Add(child);
            }
            system.subSystemList = retained.ToArray();
        }

        private static bool DeclaresUnityUpdate(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                if (current.GetMethod("Update", flags) != null || current.GetMethod("FixedUpdate", flags) != null
                    || current.GetMethod("LateUpdate", flags) != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void FrameStartSystem()
        {
            OnityTaskThroughputBenchmarkRunner runner = s_active;
            if (runner != null && runner.m_installed)
            {
                runner.OnFrameStart();
            }
        }

        private void OnFrameStart()
        {
            // First instruction of the frame: every window boundary uses this reading.
            long entry = Stopwatch.GetTimestamp();
            int frame = Time.frameCount;
            m_frameStartCalls++;
            if (m_lastFrameCount >= 0 && frame != m_lastFrameCount + 1)
            {
                m_frameStartAnomalies++;
            }
            m_lastFrameCount = frame;
            if (m_stage == Stage.Finishing)
            {
                return;
            }

            try
            {
                switch (m_stage)
                {
                    case Stage.Idle:
                        if (++m_idleFrames >= k_idleFramesBeforeFirstArm)
                        {
                            // Anomalies around the mid-frame install are discarded and reported.
                            m_report.installDiscardedAnomalies = m_frameStartAnomalies;
                            m_frameStartAnomalies = 0;
                            StartArm();
                        }
                        break;
                    case Stage.Control:
                        OnControlFrame(entry);
                        break;
                    case Stage.Cohort:
                        OnCohortFrame(entry);
                        break;
                    case Stage.Trailing:
                        OnTrailingFrame(entry);
                        break;
                }
            }
            catch (Exception exception)
            {
                m_failure = m_failure ?? exception;
            }

            if ((m_failure != null || m_loggedError != null) && m_stage != Stage.Finishing)
            {
                RequestFinish();
            }
        }

        private void StartArm()
        {
            m_arm = m_arms[m_armIndex];
            m_count = m_arm.Loops;
            m_retention = OnityTaskRetentionScope.Raise(m_count);
            m_armFrames = 0;
            m_cohortIndex = 0;
            m_totalCohorts = 2 * (m_warmups + m_samples);
            m_expectedCohorts += m_totalCohorts;
            m_maxCohortFrames = 2 * k_awaitsPerLoop + 16;
            m_armReport = new Arm
            {
                id = m_arm.Id,
                displayName = (m_arm.Workload == Workload.NextFrame ? "NextFrame" : "Yield")
                    + (m_arm.Typed ? "<int>" : string.Empty) + " loops N=" + m_count + " K=" + k_awaitsPerLoop,
                workload = m_arm.Workload.ToString(),
                shape = m_arm.Typed ? "Typed int" : "Untyped",
                loops = m_count,
                awaitsPerLoop = k_awaitsPerLoop,
                awaitsPerCohort = m_count * k_awaitsPerLoop,
                retentionPolicy = "matched",
                onityRunnerPoolCapacity = m_retention.RunnerPoolCapacity,
                onitySourcePoolCapacity = m_retention.SourcePoolCapacity,
                sourcePoolCapacityMatched = m_retention.SourcePoolMatched,
                warmupsPerLibrary = m_warmups,
                samplesPerLibrary = m_samples,
                onity = CreateLibrary("OnityTask", m_warmups, m_samples),
                uniTask = CreateLibrary("UniTask", m_warmups, m_samples)
            };
            BeginCohort();
        }

        private static LibraryResult CreateLibrary(string library, int warmups, int samples)
        {
            return new LibraryResult
            {
                library = library,
                warmups = new Sample[warmups],
                samples = new Sample[samples]
            };
        }

        // Runs in a frame that belongs to no window: collection, bookkeeping and slice setup.
        private void BeginCohort()
        {
            int pass = m_cohortIndex / 2;
            m_turn = m_cohortIndex % 2;
            m_isWarmup = pass < m_warmups;
            m_index = m_isWarmup ? pass : pass - m_warmups;
            m_onityActive = ((m_index + m_turn) & 1) == 0;
            m_framesPerWindow = m_armFrames > 0 ? m_armFrames : k_awaitsPerLoop + 1;
            m_sample = new Sample
            {
                index = m_index,
                libraryOrder = m_turn,
                warmup = m_isWarmup,
                loops = m_count,
                awaits = m_count * k_awaitsPerLoop,
                controlFrames = m_framesPerWindow
            };
            m_scheduled = 0;
            m_consumed = 0;
            m_loopsAtConsume = 0;
            m_resultSum = 0;
            m_exceptions = 0;
            m_firstException = null;
            s_finished = 0;
            Array.Clear(m_controlTicks, 0, m_controlTicks.Length);
            ForceFullGc();
            m_controlFrame = 0;
            m_stage = Stage.Control;
            BeginSlice();
        }

        private void OnControlFrame(long entry)
        {
            int frame = m_controlFrame;
            int length = m_framesPerWindow;
            if (frame > 0 && frame % length == 0)
            {
                m_controlTicks[frame / length - 1] = entry - m_windowStart;
            }
            if (frame < k_controlWindows * length)
            {
                if (frame % length == 0)
                {
                    m_windowStart = entry;
                }
                m_controlFrame = frame + 1;
                return;
            }

            // Gap frame between the last control window and the scheduling frame: switch allocation slices.
            EndSlice(out m_sample.controlHeapBytes, out m_sample.controlHeapValid);
            BeginSlice();
            m_cohortFrame = 0;
            m_stage = Stage.Cohort;
        }

        private void OnCohortFrame(long entry)
        {
            if (m_cohortFrame == 0)
            {
                m_cohortStart = entry;
                m_sample.scheduleFrameCount = Time.frameCount;
                Schedule();
                m_cohortFrame = 1;
                return;
            }

            if (s_finished >= m_count)
            {
                m_loopsAtConsume = s_finished;
                Consume();
                m_cohortEnd = Stopwatch.GetTimestamp();
                m_sample.consumeFrameCount = Time.frameCount;
                m_cohortFrames = m_cohortFrame;
                EndSlice(out m_sample.heapBytes, out m_sample.heapValid);
                m_stage = Stage.Trailing;
                return;
            }

            m_cohortFrame++;
            if (m_cohortFrame > m_maxCohortFrames)
            {
                throw new InvalidOperationException((m_onityActive ? "OnityTask" : "UniTask") + " cohort did not complete within "
                    + m_maxCohortFrames + " frames in " + m_arm.Id + ": finished=" + s_finished + "/" + m_count);
            }
        }

        private void OnTrailingFrame(long entry)
        {
            m_sample.trailingTicks = entry - m_cohortEnd;
            FinishCohort();
            m_cohortIndex++;
            if (m_cohortIndex < m_totalCohorts)
            {
                BeginCohort();
                return;
            }

            FinishArm();
            m_armIndex++;
            if (m_armIndex < m_arms.Length)
            {
                StartArm();
            }
            else
            {
                RequestFinish();
            }
        }

        private void Schedule()
        {
            int count = m_count;
            int awaits = k_awaitsPerLoop;
            if (m_onityActive)
            {
                if (m_arm.Typed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        m_onityTyped[i] = OnityNextFrameTypedLoop(awaits);
                    }
                }
                else if (m_arm.Workload == Workload.NextFrame)
                {
                    for (int i = 0; i < count; i++)
                    {
                        m_onity[i] = OnityNextFrameLoop(awaits);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        m_onity[i] = OnityYieldLoop(awaits);
                    }
                }
            }
            else if (m_arm.Typed)
            {
                for (int i = 0; i < count; i++)
                {
                    m_uniTyped[i] = UniNextFrameTypedLoop(awaits);
                }
            }
            else if (m_arm.Workload == Workload.NextFrame)
            {
                for (int i = 0; i < count; i++)
                {
                    m_uni[i] = UniNextFrameLoop(awaits);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    m_uni[i] = UniYieldLoop(awaits);
                }
            }
            m_scheduled = count;
        }

        // Timed: one native GetResult per loop. A failure stops the timed loop; cleanup settles the rest.
        private void Consume()
        {
            int count = m_scheduled;
            try
            {
                if (m_onityActive)
                {
                    if (m_arm.Typed)
                    {
                        long sum = 0;
                        for (int i = 0; i < count; i++)
                        {
                            sum += m_onityTyped[i].GetAwaiter().GetResult();
                        }
                        m_resultSum = sum;
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            m_onity[i].GetAwaiter().GetResult();
                        }
                    }
                }
                else if (m_arm.Typed)
                {
                    long sum = 0;
                    for (int i = 0; i < count; i++)
                    {
                        sum += m_uniTyped[i].GetAwaiter().GetResult();
                    }
                    m_resultSum = sum;
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        m_uni[i].GetAwaiter().GetResult();
                    }
                }
                m_consumed = count;
            }
            catch (Exception exception)
            {
                m_exceptions++;
                m_firstException = m_firstException ?? exception;
            }
        }

        // Identical loop bodies; each library uses its default PlayerLoop timing (Update).
        private static async OnityTask OnityNextFrameLoop(int awaits)
        {
            for (int i = 0; i < awaits; i++)
            {
                await OnityTask.NextFrame();
            }
            s_finished++;
        }

        private static async UniTask UniNextFrameLoop(int awaits)
        {
            for (int i = 0; i < awaits; i++)
            {
                await UniTask.NextFrame();
            }
            s_finished++;
        }

        private static async OnityTask OnityYieldLoop(int awaits)
        {
            for (int i = 0; i < awaits; i++)
            {
                await OnityTask.Yield();
            }
            s_finished++;
        }

        private static async UniTask UniYieldLoop(int awaits)
        {
            for (int i = 0; i < awaits; i++)
            {
                await UniTask.Yield();
            }
            s_finished++;
        }

        private static async OnityTask<int> OnityNextFrameTypedLoop(int awaits)
        {
            for (int i = 0; i < awaits; i++)
            {
                await OnityTask.NextFrame();
            }
            s_finished++;
            return k_typedResult;
        }

        private static async UniTask<int> UniNextFrameTypedLoop(int awaits)
        {
            for (int i = 0; i < awaits; i++)
            {
                await UniTask.NextFrame();
            }
            s_finished++;
            return k_typedResult;
        }

        // Untimed: goldens, idle checks and arithmetic for the cohort that ended in the previous frame.
        private void FinishCohort()
        {
            Sample sample = m_sample;
            string library = m_onityActive ? "OnityTask" : "UniTask";
            sample.frames = m_cohortFrames;
            sample.loopsCompleted = m_loopsAtConsume;
            sample.consumed = m_consumed;
            sample.resultSum = m_resultSum;
            sample.exceptions = m_exceptions;
            sample.elapsedTicks = m_cohortEnd - m_cohortStart;
            sample.controlWindowTicks = (long[])m_controlTicks.Clone();
            if (m_exceptions != 0 || m_consumed != m_count)
            {
                SettleUnconsumed();
                throw new InvalidOperationException(library + " cohort faulted in " + m_arm.Id + ": consumed="
                    + m_consumed + "/" + m_count, m_firstException);
            }
            ClearCohort();
            if (m_loopsAtConsume != m_count || s_finished != m_count
                || (m_arm.Typed && m_resultSum != (long)k_typedResult * m_count))
            {
                throw new InvalidOperationException(library + " golden failed in " + m_arm.Id + ": loops=" + m_loopsAtConsume
                    + " finishedNow=" + s_finished + " resultSum=" + m_resultSum);
            }
            if (sample.consumeFrameCount - sample.scheduleFrameCount != sample.frames)
            {
                throw new InvalidOperationException("Frame accounting mismatch in " + m_arm.Id + ".");
            }

            CheckIdle(library + " " + m_arm.Id + " sample end");
            sample.uniTaskIdle = true;
            sample.onityIdle = m_onityProbe.IsAvailable ? "verified" : "unavailable";

            if (m_isWarmup)
            {
                // The latest warmup sets the control window length; it is fixed once samples start, and every
                // sample of both libraries must then complete in exactly that many frames.
                m_armFrames = sample.frames;
                m_armReport.framesPerCohort = m_armFrames;
            }
            sample.framesMatchArm = sample.frames == m_armFrames;

            double factor = 1000000000d / Stopwatch.Frequency / sample.awaits;
            sample.controlMedianTicks = Median(sample.controlWindowTicks);
            sample.controlTicksPerFrame = (double)sample.controlMedianTicks / sample.controlFrames;
            sample.controlFraction = sample.elapsedTicks > 0
                ? sample.controlTicksPerFrame * sample.frames / sample.elapsedTicks : -1;
            sample.netTicks = sample.elapsedTicks - sample.controlTicksPerFrame * sample.frames;
            sample.nanosecondsPerAwait = sample.netTicks * factor;
            sample.trailingNetTicks = sample.trailingTicks - sample.controlTicksPerFrame;
            sample.trailingNanosecondsPerAwait = sample.trailingNetTicks * factor;
            sample.rawBytesPerAwait = sample.heapValid ? (double)sample.heapBytes / sample.awaits : -1;
            sample.bytesValid = sample.heapValid && sample.controlHeapValid;
            sample.bytesPerAwait = sample.bytesValid
                ? (sample.heapBytes - (double)sample.controlHeapBytes * sample.frames
                    / (k_controlWindows * sample.controlFrames)) / sample.awaits
                : -1;
            sample.validated = true;
            m_validatedCohorts++;

            LibraryResult target = m_onityActive ? m_armReport.onity : m_armReport.uniTask;
            if (m_isWarmup)
            {
                target.warmups[m_index] = sample;
            }
            else
            {
                target.samples[m_index] = sample;
            }
        }

        private void FinishArm()
        {
            try
            {
                SummarizeLibrary(m_armReport.onity);
                SummarizeLibrary(m_armReport.uniTask);
                m_armReport.framesMatched = true;
                for (int i = 0; i < m_samples; i++)
                {
                    m_armReport.framesMatched &= m_armReport.onity.samples[i].framesMatchArm
                        && m_armReport.uniTask.samples[i].framesMatchArm;
                }
                m_armReport.ratioOfMedians = m_armReport.uniTask.medianNanosecondsPerAwait > 0
                    ? m_armReport.onity.medianNanosecondsPerAwait / m_armReport.uniTask.medianNanosecondsPerAwait : -1;
                m_armReport.validated = true;
            }
            finally
            {
                m_retention?.Restore();
                m_retention = null;
                m_armReports.Add(m_armReport);
            }
        }

        private static void SummarizeLibrary(LibraryResult result)
        {
            int count = result.samples.Length;
            double[] values = new double[count];
            double[] bytes = new double[count];
            double[] rawBytes = new double[count];
            double[] trailing = new double[count];
            double[] frames = new double[count];
            double sum = 0;
            bool bytesValid = true;
            bool rawValid = true;
            for (int i = 0; i < count; i++)
            {
                Sample sample = result.samples[i];
                values[i] = sample.nanosecondsPerAwait;
                sum += values[i];
                bytes[i] = sample.bytesPerAwait;
                rawBytes[i] = sample.rawBytesPerAwait;
                trailing[i] = sample.trailingNanosecondsPerAwait;
                frames[i] = sample.frames;
                bytesValid &= sample.bytesValid;
                rawValid &= sample.heapValid;
            }
            result.medianNanosecondsPerAwait = Median(values);
            result.meanNanosecondsPerAwait = sum / count;
            result.medianTrailingNanosecondsPerAwait = Median(trailing);
            result.medianBytesPerAwait = bytesValid ? Median(bytes) : -1;
            result.medianRawBytesPerAwait = rawValid ? Median(rawBytes) : -1;
            result.medianFramesToComplete = (int)Median(frames);
            Array.Sort(values);
            result.minNanosecondsPerAwait = values[0];
            result.maxNanosecondsPerAwait = values[count - 1];
        }

        private void CheckIdle(string where)
        {
            string problem = ReadUniTaskProblem();
            if (problem == null && m_onityProbe != null && m_onityProbe.IsAvailable)
            {
                int pending = m_onityProbe.Read(out string detail);
                if (pending != 0)
                {
                    problem = "Onity holds pending work: " + detail;
                }
            }
            if (problem != null)
            {
                throw new InvalidOperationException("Library not idle at " + where + ": " + problem);
            }
        }

        private string ReadUniTaskProblem()
        {
            Array runners = m_uniRunnersField.GetValue(null) as Array;
            for (int i = 0; runners != null && i < runners.Length; i++)
            {
                object runner = runners.GetValue(i);
                if (runner == null)
                {
                    continue;
                }
                object waitQueue = m_uniRunnerWaitQueue.GetValue(runner);
                if ((int)m_uniRunnerTail.GetValue(runner) != 0 || (bool)m_uniRunnerRunning.GetValue(runner)
                    || (waitQueue != null && (int)m_uniQueueSize.GetValue(waitQueue) != 0))
                {
                    return "UniTask PlayerLoopRunner " + (PlayerLoopTiming)i + " not empty and idle";
                }
            }

            Array yielders = m_uniYieldersField.GetValue(null) as Array;
            for (int i = 0; yielders != null && i < yielders.Length; i++)
            {
                object queue = yielders.GetValue(i);
                if (queue != null && ((int)m_uniActionCount.GetValue(queue) != 0
                    || (int)m_uniWaitingCount.GetValue(queue) != 0 || (bool)m_uniDequing.GetValue(queue)))
                {
                    return "UniTask ContinuationQueue " + (PlayerLoopTiming)i + " not empty and idle";
                }
            }
            return null;
        }

        private void SettleUnconsumed()
        {
            for (int i = m_consumed; i < m_scheduled; i++)
            {
                try
                {
                    if (m_onityActive)
                    {
                        if (m_arm.Typed)
                        {
                            m_onityTyped[i].GetAwaiter().GetResult();
                        }
                        else
                        {
                            m_onity[i].GetAwaiter().GetResult();
                        }
                    }
                    else if (m_arm.Typed)
                    {
                        m_uniTyped[i].GetAwaiter().GetResult();
                    }
                    else
                    {
                        m_uni[i].GetAwaiter().GetResult();
                    }
                }
                catch (Exception)
                {
                    // The original failure is reported; this only releases what can be released.
                }
            }
            ClearCohort();
        }

        private void ClearCohort()
        {
            Array.Clear(m_onity, 0, m_scheduled);
            Array.Clear(m_onityTyped, 0, m_scheduled);
            Array.Clear(m_uni, 0, m_scheduled);
            Array.Clear(m_uniTyped, 0, m_scheduled);
            m_scheduled = 0;
        }

        private void BeginSlice()
        {
            if (m_counterCrossFrame)
            {
                m_sliceStart = m_counter.Begin();
                m_sliceOpen = true;
            }
        }

        private void EndSlice(out long bytes, out bool valid)
        {
            bytes = -1;
            valid = false;
            if (m_sliceOpen)
            {
                m_sliceOpen = false;
                bytes = m_counter.End(m_sliceStart, out valid);
                RestoreCollector();
                valid &= bytes >= 0;
            }
        }

        private static long Median(long[] values)
        {
            var sorted = (long[])values.Clone();
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            return (sorted.Length & 1) == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        private static double Median(double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            return (sorted.Length & 1) == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void RequestFinish()
        {
            if (m_stage == Stage.Finishing)
            {
                return;
            }
            m_stage = Stage.Finishing;
            StartCoroutine(FinishAfterFrame());
        }

        // Teardown runs from a coroutine one frame later, never from inside the PlayerLoop system.
        private IEnumerator FinishAfterFrame()
        {
            yield return null;
            Finish();
        }

        private void Finish()
        {
            Exception failure = m_failure;
            try
            {
                if (m_scheduled != 0)
                {
                    SettleUnconsumed();
                }
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            try
            {
                RemoveSystem();
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            RestoreSettings();
            if (failure == null && m_loggedError != null)
            {
                failure = new InvalidOperationException("Logged error during throughput: " + m_loggedError);
            }
            if (failure == null && m_armReports.Count != (m_arms == null ? -1 : m_arms.Length))
            {
                failure = new InvalidOperationException("Not every throughput arm completed.");
            }
            if (failure == null && m_frameStartAnomalies != 0)
            {
                failure = new InvalidOperationException("The frame-start system skipped or repeated "
                    + m_frameStartAnomalies + " frame(s).");
            }

            m_report.arms = m_armReports.ToArray();
            m_report.frameStartCalls = m_frameStartCalls;
            m_report.frameStartAnomalies = m_frameStartAnomalies;
            m_report.validatedCohorts = m_validatedCohorts;
            m_report.expectedCohorts = m_expectedCohorts;
            m_report.goldensPassed = failure == null && m_validatedCohorts == m_expectedCohorts && m_expectedCohorts > 0;
            bool framesMatched = m_armReports.Count > 0;
            foreach (Arm arm in m_armReports)
            {
                framesMatched &= arm.framesMatched;
            }
            m_report.framesMatched = framesMatched;
            m_report.completed = failure == null;
            m_report.failure = failure?.ToString();
            m_report.loggedError = m_loggedError;
            m_report.generatedAtUtc = DateTime.UtcNow.ToString("O");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_path));
                File.WriteAllText(m_path, JsonUtility.ToJson(m_report, true));
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
                if (ReferenceEquals(s_active, this))
                {
                    s_active = null;
                }
                Destroy(gameObject);
            }
        }

        private void RemoveSystem()
        {
            if (!m_installed)
            {
                return;
            }
            m_installed = false;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveHarnessSystem(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
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
            if (m_sliceOpen)
            {
                m_sliceOpen = false;
                m_counter?.EndSlice();
            }
            m_retention?.Restore();
            m_retention = null;
            if (m_hasSettings)
            {
                RestoreCollector();
                OnityTask.FlowExecutionContext = m_oldFlow;
                OnityTaskTracker.IsEnabled = m_oldTracker;
                OnityTaskTracker.EnableStackTrace = m_oldStackTrace;
                OnityTask.RunnerPoolCapacity = m_oldRunnerCapacity;
                m_hasSettings = false;
            }
            if (m_listening)
            {
                Application.logMessageReceived -= OnLog;
                m_listening = false;
            }
            m_counter?.Dispose();
            m_counter = null;
        }

        private void OnDestroy()
        {
            try
            {
                RemoveSystem();
            }
            finally
            {
                RestoreSettings();
                if (ReferenceEquals(s_active, this))
                {
                    s_active = null;
                }
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (m_loggedError == null && (type == LogType.Error || type == LogType.Exception))
            {
                m_loggedError = condition + "\n" + stackTrace;
            }
        }

        private static bool IsIl2Cpp
        {
            get
            {
#if ENABLE_IL2CPP
                return true;
#else
                return false;
#endif
            }
        }

        private sealed class ArmDefinition
        {
            internal readonly string Id;
            internal readonly Workload Workload;
            internal readonly bool Typed;
            internal readonly int Loops;

            internal ArmDefinition(string id, Workload workload, bool typed, int loops)
            {
                Id = id;
                Workload = workload;
                Typed = typed;
                Loops = loops;
            }
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string suite = "throughput";
            public string title = "Throughput: concurrent async loops awaiting real PlayerLoop frames";
            public string measurementScope = "End to end under the real PlayerLoop: N concurrent async loops per library each "
                + "await the library's default NextFrame or Yield (Update timing) K times; scheduling, every suspension and "
                + "resumption, builder completion, one native GetResult per loop and inline returns are timed. Elapsed = "
                + "Stopwatch ticks from the harness frame-start system of the scheduling frame to the point, in the frame-start "
                + "system, where the last loop's task has been consumed. The window contains whole engine frames, so the median "
                + "of three adjacent idle control windows (same frame count, no work, both libraries' idle systems running) is "
                + "subtracted per frame; ns per await = (elapsed - control) / (N x K). Engine frame overhead and its jitter are "
                + "included except through that subtraction. Work a library defers past the consuming frame-start is not in "
                + "the window; it is reported as the trailing-frame diagnostic and both libraries must be idle one frame later.";
            public string fairness = "Identical loop bodies (one shared counter increment per loop); each library's default "
                + "timing; harness scheduling, polling and consumption only in its own system placed first in the first "
                + "PlayerLoop phase, outside both libraries' systems; libraries interleaved A,B,B,A; ForceFullGc outside "
                + "timing; FlowExecutionContext=false; Onity retention matched to the cohort (runner and, when the runtime "
                + "declares it, source capacity raised before warmup and restored after).";
            public string allocation = "Calibrated cross-frame counter slices per control block (three windows) and per "
                + "cohort window; bytesPerAwait = (cohort bytes - control bytes x frames / control frames) / awaits when both "
                + "slices are valid, else -1; rawBytesPerAwait = cohort bytes / awaits. HeapDelta disables the collector "
                + "inside slices; zero is not proof of zero allocation.";
            public bool selfTest;
            public bool completed;
            public string failure;
            public string loggedError;
            public string generatedAtUtc;
            public OnityTaskBenchmarkEnvironment environment;
            public bool isIl2Cpp = IsIl2Cpp;
            public long stopwatchFrequency = Stopwatch.Frequency;
            public bool onityFlowExecutionContext;
            public int awaitsPerLoop = k_awaitsPerLoop;
            public int warmupsPerLibrary;
            public int samplesPerLibrary;
            public int controlWindowsPerSample = k_controlWindows;
            public string retentionPolicy = "matched";
            public int defaultRunnerPoolCapacity;
            public bool sourcePoolCapacityAvailable;
            public int defaultSourcePoolCapacity;
            public bool counterCalibrated;
            public string counterKind;
            public string counterDescription;
            public string counterRejections;
            public long counterCalibrationBytes;
            public long counterEmptyBytes;
            public string frameStartSystem;
            public string[] rootPhases;
            public string[] firstPhaseSystems;
            public int harnessSystemsInstalled;
            public bool harnessDeclaresUnityUpdate;
            public int installDiscardedAnomalies;
            public int frameStartCalls;
            public int frameStartAnomalies;
            public string onityIdleProbe;
            public string onityIdleCheck;
            public string uniTaskIdleProbe;
            public string[] predeclaredArms;
            public int validatedCohorts;
            public int expectedCohorts;
            public bool goldensPassed;
            public bool framesMatched;
            public Arm[] arms;
        }

        [Serializable]
        private sealed class Arm
        {
            public string id;
            public string displayName;
            public string workload;
            public string shape;
            public int loops;
            public int awaitsPerLoop;
            public int awaitsPerCohort;
            public string retentionPolicy;
            public int onityRunnerPoolCapacity;
            public int onitySourcePoolCapacity;
            public bool sourcePoolCapacityMatched;
            public int warmupsPerLibrary;
            public int samplesPerLibrary;
            public int framesPerCohort;
            public bool framesMatched;
            public bool validated;
            public double ratioOfMedians;
            public LibraryResult onity;
            public LibraryResult uniTask;
        }

        [Serializable]
        private sealed class LibraryResult
        {
            public string library;
            public double medianNanosecondsPerAwait;
            public double meanNanosecondsPerAwait;
            public double minNanosecondsPerAwait;
            public double maxNanosecondsPerAwait;
            public double medianTrailingNanosecondsPerAwait;
            public double medianBytesPerAwait;
            public double medianRawBytesPerAwait;
            public int medianFramesToComplete;
            public Sample[] warmups;
            public Sample[] samples;
        }

        [Serializable]
        private sealed class Sample
        {
            public int index;
            public int libraryOrder;
            public bool warmup;
            public int loops;
            public int awaits;
            public int frames;
            public bool framesMatchArm;
            public int scheduleFrameCount;
            public int consumeFrameCount;
            public int loopsCompleted;
            public int consumed;
            public long resultSum;
            public int exceptions;
            public bool uniTaskIdle;
            public string onityIdle;
            public long elapsedTicks;
            public int controlFrames;
            public long[] controlWindowTicks;
            public long controlMedianTicks;
            public double controlTicksPerFrame;
            public double controlFraction;
            public double netTicks;
            public double nanosecondsPerAwait;
            public long trailingTicks;
            public double trailingNetTicks;
            public double trailingNanosecondsPerAwait;
            public long heapBytes = -1;
            public bool heapValid;
            public long controlHeapBytes = -1;
            public bool controlHeapValid;
            public bool bytesValid;
            public double bytesPerAwait = -1;
            public double rawBytesPerAwait = -1;
            public bool validated;
        }
    }
}
