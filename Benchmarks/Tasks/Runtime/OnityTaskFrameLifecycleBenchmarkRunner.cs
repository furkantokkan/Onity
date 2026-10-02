using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Scripting;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Measures complete frame-driven async lifecycles of OnityTask and the pinned UniTask under the
    /// real PlayerLoop. Every PlayerLoop system either library runs is bracketed by timestamp markers;
    /// the harness schedules and consumes only inside its own timed systems, subtracts empty control
    /// frames, and checks the result against a frame-level sanity bracket around the whole loop.
    /// </summary>
    public sealed class OnityTaskFrameLifecycleBenchmarkRunner : MonoBehaviour
    {
        private const int k_maxCohort = 4096;
        private const int k_warmups = 3;
        private const int k_samples = 8;
        private const int k_largeCohortSamples = 6;
        private const int k_controlFrames = 8;
        private const int k_maxFrames = 16;
        private const int k_idleFrames = 3;
        private const int k_defaultRunnerPoolCapacity = 128;
        private const int k_delayFrames = 3;
        private const int k_waitUntilFrames = 3;
        private const int k_typedResult = 42;
        private const float k_captureDeltaTime = 0.02f;
        private const float k_delaySeconds = 0.05f;
        private const double k_sanityTolerance = 0.05;
        private const string k_armsArgument = "-onityTaskFrameLifecycleArms";
        private const string k_onityMarkerPrefix = "Onity.Unity.Async.OnityTaskPlayerLoop+";

        private enum Workload
        {
            NextFrame,
            Yield,
            DelayFrames,
            Delay,
            WaitUntil
        }

        private enum TokenMode
        {
            None,
            Registered,
            Cancel50
        }

        private enum Stage
        {
            Idle,
            Control,
            Cohort,
            Finishing
        }

        private sealed class FrameBeginMarker
        {
        }

        private sealed class ScheduleMarker
        {
        }

        private sealed class ConsumeMarker
        {
        }

        private sealed class BeforeMarker
        {
        }

        private sealed class AfterMarker
        {
        }

        private static readonly TimeSpan s_uniDelay = TimeSpan.FromSeconds(k_delaySeconds);
        private static readonly PlayerLoopSystem.UpdateFunction s_frameBegin = FrameBeginSystem;
        private static readonly PlayerLoopSystem.UpdateFunction s_schedule = ScheduleSystem;
        private static readonly PlayerLoopSystem.UpdateFunction s_consume = ConsumeSystem;
        private static OnityTaskFrameLifecycleBenchmarkRunner s_active;

        private readonly OnityTask[] m_onity = new OnityTask[k_maxCohort];
        private readonly OnityTask<int>[] m_onityTyped = new OnityTask<int>[k_maxCohort];
        private readonly UniTask[] m_uni = new UniTask[k_maxCohort];
        private readonly UniTask<int>[] m_uniTyped = new UniTask<int>[k_maxCohort];
        private readonly CancellationToken[] m_tokens = new CancellationToken[k_maxCohort];
        private readonly List<Arm> m_armReports = new List<Arm>(32);

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
        private float m_oldCaptureDeltaTime;
        private GarbageCollector.Mode m_oldGcMode;
        private OnityBenchmarkAllocationCounter m_counter;

        // Bracket state; arrays are sized once at install.
        private Type[] m_bracketTypes;
        private string[] m_bracketNames;
        private string[] m_bracketPhases;
        private bool[] m_bracketIsOnity;
        private bool[] m_bracketOncePerFrame;
        private long[] m_bracketStart;
        private long[] m_bracketFrameTicks;
        private int[] m_bracketFrameCalls;
        private long[] m_bracketTotalCalls;
        private bool[] m_bracketOpen;
        private string[] m_installedLoop;
        private int m_unresolvedBracketEvents;
        private int m_bracketCallAnomalies;
        private bool m_bracketCountingStarted;
        private int m_measuredFrames;
        // Harness entry points must each run exactly once per frame (measured, see EndFrame).
        private int m_harnessCallAnomalies;
        private int m_frameBeginCallsThisFrame;
        private int m_scheduleCallsThisFrame;
        private int m_consumeCallsThisFrame;

        // Frame state.
        private bool m_frameOpen;
        private long m_frameBeginTicks;
        private long m_scheduleFrameTicks;
        private long m_consumeFrameTicks;
        private bool m_consumedThisFrame;
        private int m_frameCounter;
        private int m_waitTarget = int.MaxValue;
        private Func<bool> m_counterReady;

        // Arm and cohort state.
        private ArmDefinition[] m_arms;
        private int m_armIndex;
        private ArmDefinition m_arm;
        private Arm m_armReport;
        private bool m_matchedPolicy;
        private int m_cohortIndex;
        private int m_totalCohorts;
        private int m_framesPerCohort;
        private Stage m_stage;
        private int m_frameIndex;
        private bool m_isWarmup;
        private bool m_onityActive;
        private Sample m_sample;
        private int m_count;
        private int m_scheduled;
        private int m_expectedSuccess;
        private int m_expectedCanceled;
        private int m_finished;
        private int m_succeeded;
        private int m_canceled;
        private int m_failed;
        private long m_resultSum;
        private Exception m_firstFailure;
        private int m_consumeFrame;
        private CancellationTokenSource m_cancelSource;
        private CancellationTokenSource m_liveSource;
        private long m_sliceStart;
        private bool m_sliceOpen;
        private int m_validatedCohorts;
        private int m_expectedCohorts;
        private int m_registrationChecks;
        private int m_registrationLeaks;
        private int m_maxOnityPendingAfterConsume;
        private int m_maxUniPendingAfterConsume;

        // Reflection bindings, read only outside measured windows (Onity pending also after warmup consumes).
        private Func<int> m_onityPendingCount;
        private FieldInfo m_onityDrainState;
        private FieldInfo m_onityRunnerInstance;
        private FieldInfo[] m_onityRunnerLists;
        private FieldInfo m_onityCancellationRequests;
        private FieldInfo m_onityPhases;
        private FieldInfo m_onityPhasePending;
        private FieldInfo m_onityTimers;
        private Array m_uniRunners;
        private Array m_uniYielders;
        private FieldInfo m_uniRunnerTail;
        private FieldInfo m_uniRunnerRunning;
        private FieldInfo m_uniRunnerWaitQueue;
        private FieldInfo m_uniQueueSize;
        private FieldInfo m_uniActionCount;
        private FieldInfo m_uniWaitingCount;
        private FieldInfo m_uniDequing;
        private PropertyInfo m_sourcePoolCapacity;
        private int m_oldSourcePoolCapacity;

        /// <summary>Runs the Release Player frame-lifecycle suite and writes its versioned JSON report.</summary>
        /// <param name="path">Output JSON path.</param>
        /// <param name="completed">Receives the report path and any failure after cleanup.</param>
        public static void Run(string path, Action<string, Exception> completed)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Frame-lifecycle output path is required.", nameof(path));
            }
            if (s_active != null)
            {
                throw new InvalidOperationException("A frame-lifecycle benchmark is already running.");
            }

            GameObject owner = new GameObject("Onity Frame Lifecycle Benchmark");
            DontDestroyOnLoad(owner);
            OnityTaskFrameLifecycleBenchmarkRunner runner = owner.AddComponent<OnityTaskFrameLifecycleBenchmarkRunner>();
            runner.m_path = Path.GetFullPath(path);
            runner.m_completed = completed;
            s_active = runner;
        }

        // The harness defines no Update, FixedUpdate or LateUpdate: it is inert in ScriptRunBehaviour*.
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
                m_oldCaptureDeltaTime = Time.captureDeltaTime;
                m_oldGcMode = GarbageCollector.GCMode;
                m_hasSettings = true;
                if (m_oldRunnerCapacity != k_defaultRunnerPoolCapacity)
                {
                    throw new InvalidOperationException("Frame lifecycle requires the default Onity runner retention 128.");
                }

                OnityTask.FlowExecutionContext = false;
                OnityTaskTracker.IsEnabled = false;
                OnityTaskTracker.EnableStackTrace = false;
                Time.captureDeltaTime = k_captureDeltaTime;
                m_report.onityFlowExecutionContext = OnityTask.FlowExecutionContext;
                m_report.environment = OnityTaskBenchmarkEnvironment.Capture();
                if (Application.isEditor || m_report.environment.isDevelopment)
                {
                    throw new InvalidOperationException("Frame lifecycle requires a non-development Release Player.");
                }

                m_counterReady = IsCounterReady;
                m_arms = SelectArms(m_report);
                BindDiagnostics();
                m_counter = OnityBenchmarkAllocationCounter.Create(false, true);
                RestoreCollector();
                m_report.counterKind = m_counter.Kind;
                m_report.counterDescription = m_counter.Description;
                m_report.counterRejections = m_counter.RejectedCandidates;
                m_report.counterCalibrationBytes = m_counter.CalibrationBytes;
                m_report.counterEmptyBytes = m_counter.EmptyDeltaBytes;
                m_report.counterCalibrated = m_counter.IsAvailable && m_counter.SupportsCrossFrameSlices;
                m_report.onitySourcePoolCapacityAvailable = m_sourcePoolCapacity != null;
                InstallSystems();
                CheckQueuesEmpty("before the first arm");
                m_stage = Stage.Idle;
                m_frameIndex = 0;
            }
            catch (Exception exception)
            {
                m_failure = exception;
                RequestFinish();
            }
        }

        private bool IsCounterReady()
        {
            return m_frameCounter >= m_waitTarget;
        }
        private static ArmDefinition[] CreateArms()
        {
            return new[]
            {
                new ArmDefinition("nextframe-1x128", "G2", Workload.NextFrame, false, 1, 128, TokenMode.None),
                new ArmDefinition("nextframe-3x128", "G2", Workload.NextFrame, false, 3, 128, TokenMode.None),
                new ArmDefinition("yield-1x128", "G2", Workload.Yield, false, 1, 128, TokenMode.None),
                new ArmDefinition("yield-3x128", "G2", Workload.Yield, false, 3, 128, TokenMode.None),
                new ArmDefinition("nextframe-typed-1x128", "reported", Workload.NextFrame, true, 1, 128, TokenMode.None),
                new ArmDefinition("nextframe-1x4096", "reported", Workload.NextFrame, false, 1, 4096, TokenMode.None),
                new ArmDefinition("nextframe-3x4096", "reported", Workload.NextFrame, false, 3, 4096, TokenMode.None),
                new ArmDefinition("yield-1x4096", "reported", Workload.Yield, false, 1, 4096, TokenMode.None),
                new ArmDefinition("yield-3x4096", "reported", Workload.Yield, false, 3, 4096, TokenMode.None),
                new ArmDefinition("delayframes3-none", "G3", Workload.DelayFrames, false, 1, 128, TokenMode.None),
                new ArmDefinition("delayframes3-registered", "G3", Workload.DelayFrames, false, 1, 128, TokenMode.Registered),
                new ArmDefinition("delayframes3-cancel50", "G3", Workload.DelayFrames, false, 1, 128, TokenMode.Cancel50),
                new ArmDefinition("delay50ms-none", "G3", Workload.Delay, false, 1, 128, TokenMode.None),
                new ArmDefinition("delay50ms-registered", "G3", Workload.Delay, false, 1, 128, TokenMode.Registered),
                new ArmDefinition("delay50ms-cancel50", "G3", Workload.Delay, false, 1, 128, TokenMode.Cancel50),
                new ArmDefinition("waituntil-none", "G3", Workload.WaitUntil, false, 1, 128, TokenMode.None),
                new ArmDefinition("waituntil-registered", "G3", Workload.WaitUntil, false, 1, 128, TokenMode.Registered),
                new ArmDefinition("waituntil-cancel50", "G3", Workload.WaitUntil, false, 1, 128, TokenMode.Cancel50)
            };
        }

        private static ArmDefinition[] SelectArms(Report report)
        {
            ArmDefinition[] all = CreateArms();
            report.predeclaredArms = new string[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                report.predeclaredArms[i] = all[i].Id;
            }

            string filter = GetArgumentValue(k_armsArgument);
            report.armFilter = string.IsNullOrWhiteSpace(filter) ? string.Empty : filter.Trim();
            if (report.armFilter.Length == 0)
            {
                report.selectedArms = report.predeclaredArms;
                return all;
            }

            string[] requested = report.armFilter.Split(',');
            var selected = new List<ArmDefinition>(requested.Length);
            for (int i = 0; i < all.Length; i++)
            {
                for (int j = 0; j < requested.Length; j++)
                {
                    if (string.Equals(requested[j].Trim(), all[i].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        selected.Add(all[i]);
                        break;
                    }
                }
            }
            if (selected.Count == 0 || selected.Count != requested.Length)
            {
                throw new ArgumentException("Unknown or duplicate frame-lifecycle arm in " + k_armsArgument + ": " + filter);
            }

            report.selectedArms = new string[selected.Count];
            for (int i = 0; i < selected.Count; i++)
            {
                report.selectedArms[i] = selected[i].Id;
            }
            return selected.ToArray();
        }

        private void BindDiagnostics()
        {
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assembly onity = typeof(OnityTask).Assembly;
            Type dispatcher = onity.GetType("Onity.Unity.Async.OnityTaskMainThreadDispatcher", true);
            MethodInfo pending = dispatcher.GetProperty("PendingCount", staticFlags)?.GetGetMethod();
            m_onityDrainState = dispatcher.GetField("s_drainState", staticFlags);
            Type runner = onity.GetType("Onity.Unity.Async.OnityTaskRunner", true);
            MethodInfo ensureRunner = runner.GetMethod("EnsureCreated", staticFlags, null, Type.EmptyTypes, null);
            m_onityRunnerInstance = runner.GetField("s_instance", staticFlags);
            m_onityCancellationRequests = runner.GetField("s_cancellationRequestCount", staticFlags);
            m_onityRunnerLists = new[]
            {
                runner.GetField("m_updateSources", instanceFlags),
                runner.GetField("m_fixedUpdateSources", instanceFlags),
                runner.GetField("m_lateUpdateSources", instanceFlags)
            };
            Type loop = typeof(OnityTaskPlayerLoop);
            m_onityPhases = loop.GetField("s_phases", staticFlags);
            m_onityTimers = loop.GetField("s_timers", staticFlags);
            Type phase = loop.GetNestedType("Phase", BindingFlags.NonPublic);
            m_onityPhasePending = phase?.GetField("Pending", instanceFlags);
            if (pending == null || pending.ReturnType != typeof(int) || m_onityDrainState == null || ensureRunner == null
                || m_onityRunnerInstance == null || m_onityCancellationRequests == null || m_onityRunnerLists[0] == null
                || m_onityRunnerLists[1] == null || m_onityRunnerLists[2] == null || m_onityPhases == null
                || m_onityTimers == null || m_onityPhasePending == null)
            {
                throw new InvalidOperationException("Onity queue diagnostics unavailable or AOT stripped.");
            }
            m_onityPendingCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), pending, true);
            // Create the legacy runner before any window so no cold host creation is measured.
            ensureRunner.Invoke(null, null);
            if (m_onityRunnerInstance.GetValue(null) == null)
            {
                throw new InvalidOperationException("Onity task runner could not be created before measurement.");
            }

            m_uniRunners = typeof(PlayerLoopHelper).GetField("runners", staticFlags)?.GetValue(null) as Array;
            m_uniYielders = typeof(PlayerLoopHelper).GetField("yielders", staticFlags)?.GetValue(null) as Array;
            object runnerSample = m_uniRunners?.GetValue((int)PlayerLoopTiming.Update);
            object queueSample = m_uniYielders?.GetValue((int)PlayerLoopTiming.LastPostLateUpdate);
            if (runnerSample == null || queueSample == null)
            {
                throw new InvalidOperationException("Pinned UniTask runners/yielders are not injected.");
            }
            Type runnerType = runnerSample.GetType();
            Type queueType = queueSample.GetType();
            m_uniRunnerTail = runnerType.GetField("tail", instanceFlags);
            m_uniRunnerRunning = runnerType.GetField("running", instanceFlags);
            m_uniRunnerWaitQueue = runnerType.GetField("waitQueue", instanceFlags);
            object waitQueue = m_uniRunnerWaitQueue?.GetValue(runnerSample);
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

            // Matched retention needs the PERF-7 source cap; without it 4096 arms run at policy default.
            m_sourcePoolCapacity = typeof(OnityTask).GetProperty("SourcePoolCapacity", BindingFlags.Static | BindingFlags.Public);
            if (m_sourcePoolCapacity != null && (m_sourcePoolCapacity.PropertyType != typeof(int)
                || !m_sourcePoolCapacity.CanRead || !m_sourcePoolCapacity.CanWrite))
            {
                m_sourcePoolCapacity = null;
            }
        }

        private void InstallSystems()
        {
            OnityTaskPlayerLoop.Initialize();
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveHarnessSystems(ref loop);
            var types = new List<Type>(48);
            var phases = new List<string>(48);
            var markers = new List<BracketMarker>(48);
            InsertBrackets(ref loop, null, types, phases, markers);
            int count = types.Count;
            m_bracketTypes = types.ToArray();
            m_bracketPhases = phases.ToArray();
            m_bracketNames = new string[count];
            m_bracketIsOnity = new bool[count];
            m_bracketOncePerFrame = new bool[count];
            m_bracketStart = new long[count];
            m_bracketFrameTicks = new long[count];
            m_bracketFrameCalls = new int[count];
            m_bracketTotalCalls = new long[count];
            m_bracketOpen = new bool[count];
            for (int i = 0; i < count; i++)
            {
                m_bracketNames[i] = m_bracketPhases[i] + "/" + m_bracketTypes[i].Name;
                m_bracketIsOnity[i] = IsOnityOwned(m_bracketTypes[i]);
                m_bracketOncePerFrame[i] = m_bracketPhases[i] != "FixedUpdate";
            }

            PlayerLoopSystem[] roots = loop.subSystemList;
            int update = FindPhase(roots, typeof(UnityEngine.PlayerLoop.Update));
            int last = roots.Length - 1;
            if (update < 0 || roots[last].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
            {
                throw new InvalidOperationException("PlayerLoop lacks Update or does not end with PostLateUpdate.");
            }
            roots[0].subSystemList = Insert(roots[0].subSystemList, 0,
                new PlayerLoopSystem { type = typeof(FrameBeginMarker), updateDelegate = s_frameBegin });
            roots[update].subSystemList = Insert(roots[update].subSystemList, 0,
                new PlayerLoopSystem { type = typeof(ScheduleMarker), updateDelegate = s_schedule });
            PlayerLoopSystem[] tail = roots[last].subSystemList;
            roots[last].subSystemList = Insert(tail, tail == null ? 0 : tail.Length,
                new PlayerLoopSystem { type = typeof(ConsumeMarker), updateDelegate = s_consume });
            loop.subSystemList = roots;
            PlayerLoop.SetPlayerLoop(loop);
            m_installed = true;
            ValidateRequiredSystems();
            PlayerLoopSystem installed = PlayerLoop.GetCurrentPlayerLoop();
            m_installedLoop = DescribeLoop(installed);
            m_report.installedLoop = m_installedLoop;
            m_report.harnessSystemsInstalled = CountHarnessSystems(installed);
            m_report.harnessSystemsExpected = 3 + 2 * count;
            m_report.harnessDeclaresUnityUpdate = DeclaresUnityUpdate(GetType());
        }

        private static int CountHarnessSystems(PlayerLoopSystem system)
        {
            int count = IsHarnessType(system.type) ? 1 : 0;
            PlayerLoopSystem[] children = system.subSystemList;
            for (int i = 0; children != null && i < children.Length; i++)
            {
                count += CountHarnessSystems(children[i]);
            }
            return count;
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

        private static bool IsOnityOwned(Type type)
        {
            return type.Assembly == typeof(OnityTask).Assembly
                || type == typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate)
                || type == typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate)
                || type == typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate);
        }

        private static bool IsBracketed(Type type)
        {
            return type != null && (type.Assembly == typeof(UniTask).Assembly || IsOnityOwned(type));
        }

        private static void InsertBrackets(ref PlayerLoopSystem system, string phase,
            List<Type> types, List<string> phases, List<BracketMarker> markers)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return;
            }
            var expanded = new List<PlayerLoopSystem>(children.Length + 8);
            for (int i = 0; i < children.Length; i++)
            {
                PlayerLoopSystem child = children[i];
                InsertBrackets(ref child, child.type == null ? phase : child.type.Name, types, phases, markers);
                if (phase != null && IsBracketed(child.type))
                {
                    var marker = new BracketMarker(types.Count);
                    markers.Add(marker);
                    types.Add(child.type);
                    phases.Add(phase);
                    expanded.Add(new PlayerLoopSystem { type = typeof(BeforeMarker), updateDelegate = marker.Before });
                    expanded.Add(child);
                    expanded.Add(new PlayerLoopSystem { type = typeof(AfterMarker), updateDelegate = marker.After });
                }
                else
                {
                    expanded.Add(child);
                }
            }
            system.subSystemList = expanded.ToArray();
        }

        private void ValidateRequiredSystems()
        {
            Type[] required =
            {
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
                typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate),
                typeof(OnityTask).Assembly.GetType(k_onityMarkerPrefix + "UpdateMarker", true),
                typeof(OnityTask).Assembly.GetType(k_onityMarkerPrefix + "FixedUpdateMarker", true),
                typeof(OnityTask).Assembly.GetType(k_onityMarkerPrefix + "LateUpdateMarker", true),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldUpdate),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerUpdate),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldUpdate),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastUpdate),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldPostLateUpdate),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerPostLateUpdate),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldPostLateUpdate),
                typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastPostLateUpdate)
            };
            m_report.requiredSystems = new string[required.Length];
            for (int i = 0; i < required.Length; i++)
            {
                int found = 0;
                for (int j = 0; j < m_bracketTypes.Length; j++)
                {
                    if (m_bracketTypes[j] == required[i])
                    {
                        found++;
                    }
                }
                m_report.requiredSystems[i] = required[i].FullName + " x" + found;
                if (found != 1)
                {
                    throw new InvalidOperationException("Required library system is not bracketed exactly once: "
                        + required[i].FullName);
                }
            }
            m_report.requiredSystemsValidated = true;
        }

        private static int FindPhase(PlayerLoopSystem[] roots, Type phase)
        {
            for (int i = 0; roots != null && i < roots.Length; i++)
            {
                if (roots[i].type == phase)
                {
                    return i;
                }
            }
            return -1;
        }

        private static PlayerLoopSystem[] Insert(PlayerLoopSystem[] systems, int index, PlayerLoopSystem node)
        {
            int length = systems == null ? 0 : systems.Length;
            var expanded = new PlayerLoopSystem[length + 1];
            if (length > 0)
            {
                Array.Copy(systems, 0, expanded, 0, index);
                Array.Copy(systems, index, expanded, index + 1, length - index);
            }
            expanded[index] = node;
            return expanded;
        }

        private static bool IsHarnessType(Type type)
        {
            return type == typeof(FrameBeginMarker) || type == typeof(ScheduleMarker) || type == typeof(ConsumeMarker)
                || type == typeof(BeforeMarker) || type == typeof(AfterMarker);
        }

        private static void RemoveHarnessSystems(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
            {
                return;
            }
            var retained = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (PlayerLoopSystem childValue in system.subSystemList)
            {
                if (IsHarnessType(childValue.type))
                {
                    continue;
                }
                PlayerLoopSystem child = childValue;
                RemoveHarnessSystems(ref child);
                retained.Add(child);
            }
            system.subSystemList = retained.ToArray();
        }

        private static string[] DescribeLoop(PlayerLoopSystem loop)
        {
            var names = new List<string>(160);
            if (loop.subSystemList != null)
            {
                foreach (PlayerLoopSystem phase in loop.subSystemList)
                {
                    if (phase.subSystemList == null)
                    {
                        continue;
                    }
                    foreach (PlayerLoopSystem child in phase.subSystemList)
                    {
                        names.Add(phase.type?.Name + "/" + child.type?.Name);
                    }
                }
            }
            return names.ToArray();
        }
        private static void FrameBeginSystem()
        {
            OnityTaskFrameLifecycleBenchmarkRunner runner = s_active;
            if (runner != null && runner.m_installed)
            {
                runner.BeginFrame();
            }
        }

        private static void ScheduleSystem()
        {
            OnityTaskFrameLifecycleBenchmarkRunner runner = s_active;
            if (runner != null && runner.m_installed)
            {
                runner.RunSchedule();
            }
        }

        private static void ConsumeSystem()
        {
            OnityTaskFrameLifecycleBenchmarkRunner runner = s_active;
            if (runner != null && runner.m_installed)
            {
                runner.RunConsume();
            }
        }

        private void BeginFrame()
        {
            if (!m_bracketCountingStarted)
            {
                // SetPlayerLoop ran mid-frame from Start; the install frame's partial markers are discarded and reported.
                m_bracketCountingStarted = true;
                m_report.installFrameDiscardedEvents = m_unresolvedBracketEvents + m_bracketCallAnomalies;
                m_unresolvedBracketEvents = 0;
                m_bracketCallAnomalies = 0;
                Array.Clear(m_bracketOpen, 0, m_bracketOpen.Length);
                Array.Clear(m_bracketTotalCalls, 0, m_bracketTotalCalls.Length);
                ResetFrame();
            }
            if (m_frameOpen)
            {
                // The previous frame never reached the consume system.
                m_unresolvedBracketEvents++;
            }
            m_frameBeginCallsThisFrame++;
            m_frameOpen = true;
            m_frameBeginTicks = Stopwatch.GetTimestamp();
        }

        private void BeginBracket(int index)
        {
            if (m_bracketOpen[index])
            {
                m_unresolvedBracketEvents++;
            }
            m_bracketOpen[index] = true;
            m_bracketStart[index] = Stopwatch.GetTimestamp();
        }

        private void EndBracket(int index)
        {
            long now = Stopwatch.GetTimestamp();
            if (!m_bracketOpen[index])
            {
                m_unresolvedBracketEvents++;
                return;
            }
            m_bracketOpen[index] = false;
            m_bracketFrameTicks[index] += now - m_bracketStart[index];
            m_bracketFrameCalls[index]++;
        }

        private void RunSchedule()
        {
            m_scheduleCallsThisFrame++;
            long start = Stopwatch.GetTimestamp();
            try
            {
                m_frameCounter++;
                if (m_stage == Stage.Cohort)
                {
                    if (m_frameIndex == 0)
                    {
                        m_waitTarget = m_frameCounter + k_waitUntilFrames;
                        if (m_onityActive)
                        {
                            ScheduleOnity();
                        }
                        else
                        {
                            ScheduleUni();
                        }
                    }
                    else if (m_frameIndex == 1 && m_arm.Token == TokenMode.Cancel50)
                    {
                        // Registered Onity callbacks run here, inside the timed harness system.
                        m_cancelSource.Cancel();
                    }
                }
            }
            catch (Exception exception)
            {
                m_failure = m_failure ?? exception;
            }
            m_scheduleFrameTicks += Stopwatch.GetTimestamp() - start;
        }

        private void RunConsume()
        {
            m_consumeCallsThisFrame++;
            long start = Stopwatch.GetTimestamp();
            try
            {
                if (m_stage == Stage.Cohort && m_consumeFrame < 0 && m_scheduled != 0 && m_finished >= m_expectedSuccess)
                {
                    if (m_onityActive)
                    {
                        ConsumeOnity();
                    }
                    else
                    {
                        ConsumeUni();
                    }
                    m_consumeFrame = m_frameIndex;
                    m_consumedThisFrame = true;
                }
            }
            catch (Exception exception)
            {
                m_failure = m_failure ?? exception;
            }
            long end = Stopwatch.GetTimestamp();
            m_consumeFrameTicks += end - start;
            long whole = end - m_frameBeginTicks;
            try
            {
                EndFrame(whole);
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

        // Untimed tail: runs after the sanity bracket closed. Allocation-free inside measured windows.
        private void EndFrame(long wholeTicks)
        {
            if (!m_frameOpen)
            {
                ResetFrame();
                return;
            }
            m_frameOpen = false;
            if (m_frameBeginCallsThisFrame != 1 || m_scheduleCallsThisFrame != 1 || m_consumeCallsThisFrame != 1)
            {
                m_harnessCallAnomalies++;
            }
            bool recordBrackets = m_stage == Stage.Cohort && !m_isWarmup;
            long onityTicks = 0;
            long uniTicks = 0;
            for (int i = 0; i < m_bracketFrameTicks.Length; i++)
            {
                if (m_bracketOpen[i])
                {
                    m_unresolvedBracketEvents++;
                    m_bracketOpen[i] = false;
                }
                if (m_bracketOncePerFrame[i] && m_bracketFrameCalls[i] != 1)
                {
                    m_bracketCallAnomalies++;
                }
                m_bracketTotalCalls[i] += m_bracketFrameCalls[i];
                long ticks = m_bracketFrameTicks[i];
                if (m_bracketIsOnity[i])
                {
                    onityTicks += ticks;
                }
                else
                {
                    uniTicks += ticks;
                }
                if (recordBrackets)
                {
                    m_sample.bracketTicks[i] += ticks;
                }
            }
            m_measuredFrames++;
            long activeTicks = m_onityActive ? onityTicks : uniTicks;
            long inactiveTicks = m_onityActive ? uniTicks : onityTicks;
            long harnessTicks = m_scheduleFrameTicks + m_consumeFrameTicks;
            if (m_stage == Stage.Control)
            {
                int index = m_frameIndex;
                m_sample.controlLibraryTicks[index] = activeTicks + harnessTicks;
                m_sample.controlAllTicks[index] = onityTicks + uniTicks + harnessTicks;
                m_sample.controlWholeTicks[index] = wholeTicks;
                if (index + 1 == k_controlFrames)
                {
                    EndSlice(out m_sample.controlHeapBytes, out m_sample.controlHeapValid);
                    BeginSlice();
                    m_stage = Stage.Cohort;
                    m_frameIndex = 0;
                }
                else
                {
                    m_frameIndex = index + 1;
                }
            }
            else if (m_stage == Stage.Cohort)
            {
                RecordCohortFrame(activeTicks, inactiveTicks, wholeTicks);
            }
            else if (m_stage == Stage.Idle)
            {
                m_frameIndex++;
                if (m_frameIndex >= k_idleFrames)
                {
                    StartNextArm();
                }
            }
            ResetFrame();
        }

        private void RecordCohortFrame(long activeTicks, long inactiveTicks, long wholeTicks)
        {
            int frame = m_frameIndex;
            m_sample.frameLibraryTicks[frame] = activeTicks;
            m_sample.frameInactiveTicks[frame] = inactiveTicks;
            m_sample.frameScheduleTicks[frame] = m_scheduleFrameTicks;
            m_sample.frameConsumeTicks[frame] = m_consumeFrameTicks;
            m_sample.frameWholeTicks[frame] = wholeTicks;
            if (m_consumedThisFrame && m_isWarmup)
            {
                // Return-queue evidence is read only in warmups so sample windows stay allocation-free.
                m_sample.onityPendingAfterConsume = m_onityPendingCount();
                m_sample.uniTaskPendingAfterConsume = ReadUniPending((int)PlayerLoopTiming.LastPostLateUpdate);
            }
            m_consumedThisFrame = false;
            bool end = m_isWarmup
                ? m_consumeFrame >= 0 && frame == m_consumeFrame + 1
                : frame == m_framesPerCohort - 1;
            if (end)
            {
                EndSlice(out m_sample.heapBytes, out m_sample.heapValid);
                FinishCohort(frame + 1);
            }
            else if (frame + 1 >= k_maxFrames)
            {
                throw new InvalidOperationException("Cohort did not complete within " + k_maxFrames + " frames: "
                    + m_arm.Id + " finished=" + m_finished + "/" + m_expectedSuccess);
            }
            else
            {
                m_frameIndex = frame + 1;
            }
        }

        private void ResetFrame()
        {
            Array.Clear(m_bracketFrameTicks, 0, m_bracketFrameTicks.Length);
            Array.Clear(m_bracketFrameCalls, 0, m_bracketFrameCalls.Length);
            m_scheduleFrameTicks = 0;
            m_consumeFrameTicks = 0;
            m_frameBeginCallsThisFrame = 0;
            m_scheduleCallsThisFrame = 0;
            m_consumeCallsThisFrame = 0;
        }

        private void BeginSlice()
        {
            if (m_report.counterCalibrated)
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
        private void StartNextArm()
        {
            if (m_armIndex >= m_arms.Length)
            {
                RequestFinish();
                return;
            }

            if (m_armIndex == 0)
            {
                // Frames before the first arm are idle. Anomalies seen there are discarded from the pass/fail counters
                // and reported, so only frames from the first arm on can fail the boundary checklist.
                m_report.armStartDiscardedEvents = m_unresolvedBracketEvents + m_bracketCallAnomalies
                    + m_harnessCallAnomalies;
                m_unresolvedBracketEvents = 0;
                m_bracketCallAnomalies = 0;
                m_harnessCallAnomalies = 0;
            }

            m_arm = m_arms[m_armIndex];
            int samples = m_arm.Cohort > k_defaultRunnerPoolCapacity ? k_largeCohortSamples : k_samples;
            m_matchedPolicy = m_arm.Cohort > k_defaultRunnerPoolCapacity && m_sourcePoolCapacity != null;
            if (m_matchedPolicy)
            {
                m_oldSourcePoolCapacity = (int)m_sourcePoolCapacity.GetValue(null);
                OnityTask.RunnerPoolCapacity = m_arm.Cohort;
                m_sourcePoolCapacity.SetValue(null, m_arm.Cohort);
            }
            m_armReport = new Arm
            {
                id = m_arm.Id,
                gate = m_arm.Gate,
                workload = m_arm.Workload.ToString(),
                shape = m_arm.Typed ? "Typed int" : "Untyped",
                tokenMode = m_arm.Token.ToString(),
                sequentialSuspensions = m_arm.Suspensions,
                cohortSize = m_arm.Cohort,
                policy = m_matchedPolicy ? "matched" : "default",
                onityRunnerPoolCapacity = OnityTask.RunnerPoolCapacity,
                warmupCohorts = k_warmups,
                sampleCohorts = samples,
                onity = CreateLibrary("OnityTask", samples),
                uniTask = CreateLibrary("UniTask", samples)
            };
            m_count = m_arm.Cohort;
            m_cohortIndex = 0;
            m_totalCohorts = 2 * (k_warmups + samples);
            m_expectedCohorts += m_totalCohorts;
            m_framesPerCohort = 0;
            PrepareCohort();
        }

        private static LibraryResult CreateLibrary(string library, int samples)
        {
            return new LibraryResult
            {
                library = library,
                warmups = new Sample[k_warmups],
                samples = new Sample[samples]
            };
        }

        // Allocations here are outside every measured window and before the next slice begins.
        private void PrepareCohort()
        {
            int pass = m_cohortIndex / 2;
            int turn = m_cohortIndex % 2;
            m_isWarmup = pass < k_warmups;
            int index = m_isWarmup ? pass : pass - k_warmups;
            m_onityActive = ((index + turn) & 1) == 0;
            m_sample = new Sample(m_bracketTypes.Length)
            {
                index = index,
                libraryOrder = turn,
                warmup = m_isWarmup,
                operations = m_count
            };
            m_scheduled = 0;
            m_finished = 0;
            m_succeeded = 0;
            m_canceled = 0;
            m_failed = 0;
            m_resultSum = 0;
            m_firstFailure = null;
            m_consumeFrame = -1;
            m_consumedThisFrame = false;
            m_waitTarget = int.MaxValue;
            m_cancelSource = null;
            m_liveSource = null;
            if (m_arm.Token != TokenMode.None)
            {
                m_liveSource = new CancellationTokenSource();
            }
            if (m_arm.Token == TokenMode.Cancel50)
            {
                m_cancelSource = new CancellationTokenSource();
            }
            m_expectedCanceled = m_arm.Token == TokenMode.Cancel50 ? (m_count + 1) / 2 : 0;
            m_expectedSuccess = m_count - m_expectedCanceled;
            for (int i = 0; i < m_count; i++)
            {
                m_tokens[i] = m_arm.Token == TokenMode.None ? CancellationToken.None
                    : m_arm.Token == TokenMode.Cancel50 && (i & 1) == 0 ? m_cancelSource.Token
                    : m_liveSource.Token;
            }
            m_stage = Stage.Control;
            m_frameIndex = 0;
            BeginSlice();
        }

        private void FinishCohort(int frames)
        {
            Sample sample = m_sample;
            sample.frames = frames;
            sample.consumeFrame = m_consumeFrame;
            sample.succeeded = m_succeeded;
            sample.canceled = m_canceled;
            sample.resultSum = m_resultSum;
            string library = m_onityActive ? "OnityTask" : "UniTask";
            if (m_failed != 0)
            {
                throw new InvalidOperationException(library + " cohort faulted in " + m_arm.Id, m_firstFailure);
            }
            if (m_consumeFrame < 0 || m_consumeFrame + 1 >= frames
                || m_succeeded + m_canceled != m_count || m_canceled != m_expectedCanceled
                || m_succeeded != m_expectedSuccess || m_finished != m_expectedSuccess
                || (m_arm.Typed && m_resultSum != (long)k_typedResult * m_count))
            {
                throw new InvalidOperationException(library + " golden failed in " + m_arm.Id + ": consumeFrame="
                    + m_consumeFrame + " frames=" + frames + " succeeded=" + m_succeeded + " canceled=" + m_canceled
                    + " finished=" + m_finished + " resultSum=" + m_resultSum);
            }
            if (!m_isWarmup && frames != m_framesPerCohort)
            {
                throw new InvalidOperationException("Sample frame count differs from the arm frame count.");
            }
            CheckQueuesEmpty(library + " " + m_arm.Id + " cohort end");
            sample.queuesEmptyAtEnd = true;
            CheckRegistrations(sample);
            ClearCohort();
            if (m_isWarmup && (sample.onityPendingAfterConsume > m_maxOnityPendingAfterConsume))
            {
                m_maxOnityPendingAfterConsume = sample.onityPendingAfterConsume;
            }
            if (m_isWarmup && sample.uniTaskPendingAfterConsume > m_maxUniPendingAfterConsume)
            {
                m_maxUniPendingAfterConsume = sample.uniTaskPendingAfterConsume;
            }
            Summarize(sample);
            sample.validated = true;
            m_validatedCohorts++;
            LibraryResult target = m_onityActive ? m_armReport.onity : m_armReport.uniTask;
            if (m_isWarmup)
            {
                target.warmups[sample.index] = sample;
            }
            else
            {
                target.samples[sample.index] = sample;
            }

            m_cohortIndex++;
            if (m_cohortIndex == 2 * k_warmups)
            {
                m_framesPerCohort = FramesFromWarmups();
                m_armReport.framesPerCohort = m_framesPerCohort;
            }
            if (m_cohortIndex == m_totalCohorts)
            {
                FinishArm();
                m_armIndex++;
                m_stage = Stage.Idle;
                m_frameIndex = 0;
            }
            else
            {
                PrepareCohort();
            }
        }

        // The first warmup pair may include cold pool and JIT work; later warmups fix the window.
        private int FramesFromWarmups()
        {
            int frames = 2;
            for (int i = 1; i < k_warmups; i++)
            {
                frames = Math.Max(frames, m_armReport.onity.warmups[i].consumeFrame + 2);
                frames = Math.Max(frames, m_armReport.uniTask.warmups[i].consumeFrame + 2);
            }
            return frames;
        }

        private void CheckRegistrations(Sample sample)
        {
            sample.registrationCheck = "not applicable (no token)";
            if (m_liveSource != null)
            {
                // A live registration left after the window would run Onity's cancel callback here.
                int before = (int)m_onityCancellationRequests.GetValue(null);
                m_liveSource.Cancel();
                int after = (int)m_onityCancellationRequests.GetValue(null);
                m_registrationChecks++;
                if (after != before)
                {
                    m_registrationLeaks++;
                    throw new InvalidOperationException("A cancellation registration outlived the measured frames in " + m_arm.Id);
                }
                sample.registrationCheck = "passed: canceling the live token after the window ran no registered callback";
                m_liveSource.Dispose();
            }
            m_cancelSource?.Dispose();
            m_liveSource = null;
            m_cancelSource = null;
        }

        private void ClearCohort()
        {
            Array.Clear(m_onity, 0, m_count);
            Array.Clear(m_onityTyped, 0, m_count);
            Array.Clear(m_uni, 0, m_count);
            Array.Clear(m_uniTyped, 0, m_count);
            Array.Clear(m_tokens, 0, m_count);
            m_scheduled = 0;
        }
        private static void Summarize(Sample sample)
        {
            int frames = sample.frames;
            sample.frameLibraryTicks = Trim(sample.frameLibraryTicks, frames);
            sample.frameInactiveTicks = Trim(sample.frameInactiveTicks, frames);
            sample.frameScheduleTicks = Trim(sample.frameScheduleTicks, frames);
            sample.frameConsumeTicks = Trim(sample.frameConsumeTicks, frames);
            sample.frameWholeTicks = Trim(sample.frameWholeTicks, frames);
            sample.controlMedianLibraryTicks = Median(sample.controlLibraryTicks);
            sample.controlMedianAllTicks = Median(sample.controlAllTicks);
            sample.controlMedianWholeTicks = Median(sample.controlWholeTicks);
            double factor = 1000000000d / Stopwatch.Frequency / sample.operations;
            double schedule = 0;
            double completion = 0;
            double returned = 0;
            double trailing = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                long library = sample.frameLibraryTicks[frame] + sample.frameScheduleTicks[frame]
                    + sample.frameConsumeTicks[frame];
                sample.libraryTicks += library;
                sample.allTicks += library + sample.frameInactiveTicks[frame];
                sample.wholeTicks += sample.frameWholeTicks[frame];
                double net = library - sample.controlMedianLibraryTicks;
                if (frame == 0)
                {
                    schedule += net;
                }
                else if (frame <= sample.consumeFrame)
                {
                    completion += net;
                }
                else if (frame == sample.consumeFrame + 1)
                {
                    returned += net;
                }
                else
                {
                    trailing += net;
                }
            }
            sample.netLibraryTicks = sample.libraryTicks - frames * sample.controlMedianLibraryTicks;
            sample.netAllTicks = sample.allTicks - frames * sample.controlMedianAllTicks;
            sample.netWholeTicks = sample.wholeTicks - frames * sample.controlMedianWholeTicks;
            sample.nanosecondsPerOperation = sample.netLibraryTicks * factor;
            sample.scheduleFrameNanosecondsPerOperation = schedule * factor;
            sample.completionFramesNanosecondsPerOperation = completion * factor;
            sample.returnFrameNanosecondsPerOperation = returned * factor;
            sample.trailingFramesNanosecondsPerOperation = trailing * factor;
            sample.rawBytesPerOperation = sample.heapValid ? (double)sample.heapBytes / sample.operations : -1;
            // Charge the library only for bytes above the per-frame control rate of the preceding control block.
            sample.bytesValid = sample.heapValid && sample.controlHeapValid;
            sample.bytesPerOperation = sample.bytesValid
                ? (sample.heapBytes - (double)sample.controlHeapBytes * frames / k_controlFrames) / sample.operations
                : -1;
        }

        private void FinishArm()
        {
            try
            {
                SummarizeLibrary(m_armReport.onity);
                SummarizeLibrary(m_armReport.uniTask);
                int samples = m_armReport.sampleCohorts;
                m_armReport.pairRatios = new double[samples];
                for (int i = 0; i < samples; i++)
                {
                    double uni = m_armReport.uniTask.samples[i].nanosecondsPerOperation;
                    m_armReport.pairRatios[i] = uni > 0 ? m_armReport.onity.samples[i].nanosecondsPerOperation / uni : -1;
                }
                m_armReport.medianPairRatio = Median(m_armReport.pairRatios);
                m_armReport.ratioOfMedians = m_armReport.uniTask.medianNanosecondsPerOperation > 0
                    ? m_armReport.onity.medianNanosecondsPerOperation / m_armReport.uniTask.medianNanosecondsPerOperation
                    : -1;
                m_armReport.sameFrameCountPerLibrary = true;
                for (int i = 0; i < samples; i++)
                {
                    m_armReport.sameFrameCountPerLibrary &= m_armReport.onity.samples[i].frames == m_framesPerCohort
                        && m_armReport.uniTask.samples[i].frames == m_framesPerCohort;
                }
                m_armReport.validated = m_armReport.sameFrameCountPerLibrary;
            }
            finally
            {
                if (m_matchedPolicy)
                {
                    OnityTask.RunnerPoolCapacity = m_oldRunnerCapacity;
                    m_sourcePoolCapacity.SetValue(null, m_oldSourcePoolCapacity);
                    m_matchedPolicy = false;
                }
                m_armReports.Add(m_armReport);
            }
        }

        private static void SummarizeLibrary(LibraryResult result)
        {
            int count = result.samples.Length;
            double[] values = new double[count];
            double sum = 0;
            double netAll = 0;
            double netWhole = 0;
            bool bytesValid = true;
            double[] bytes = new double[count];
            int[] consumeFrames = new int[count];
            for (int i = 0; i < count; i++)
            {
                Sample sample = result.samples[i];
                values[i] = sample.nanosecondsPerOperation;
                sum += values[i];
                netAll += sample.netAllTicks;
                netWhole += sample.netWholeTicks;
                bytes[i] = sample.bytesPerOperation;
                bytesValid &= sample.bytesValid;
                consumeFrames[i] = sample.consumeFrame;
            }
            result.medianNanosecondsPerOperation = Median(values);
            result.meanNanosecondsPerOperation = sum / count;
            Array.Sort(values);
            result.minNanosecondsPerOperation = values[0];
            result.maxNanosecondsPerOperation = values[count - 1];
            result.medianBytesPerOperation = bytesValid ? Median(bytes) : -1;
            Array.Sort(consumeFrames);
            result.medianConsumeFrame = consumeFrames[count / 2];
            result.sanityNetBracketTicks = netAll;
            result.sanityNetWholeTicks = netWhole;
            result.sanityDeviation = netAll > 0 ? Math.Abs(netWhole - netAll) / netAll : -1;
            result.sanityWithinTolerance = result.sanityDeviation >= 0 && result.sanityDeviation <= k_sanityTolerance;
        }

        private static long[] Trim(long[] values, int length)
        {
            var trimmed = new long[length];
            Array.Copy(values, trimmed, length);
            return trimmed;
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

        private void ScheduleOnity()
        {
            int count = m_count;
            int steps = m_arm.Suspensions;
            switch (m_arm.Workload)
            {
                case Workload.NextFrame:
                    if (m_arm.Typed)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            m_onityTyped[i] = OnityNextFrameTyped(steps);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            m_onity[i] = OnityNextFrame(steps);
                        }
                    }
                    break;
                case Workload.Yield:
                    for (int i = 0; i < count; i++)
                    {
                        m_onity[i] = OnityYield(steps);
                    }
                    break;
                case Workload.DelayFrames:
                    for (int i = 0; i < count; i++)
                    {
                        m_onity[i] = OnityDelayFrames(steps, m_tokens[i]);
                    }
                    break;
                case Workload.Delay:
                    for (int i = 0; i < count; i++)
                    {
                        m_onity[i] = OnityDelay(steps, m_tokens[i]);
                    }
                    break;
                default:
                    for (int i = 0; i < count; i++)
                    {
                        m_onity[i] = OnityWaitUntil(steps, m_tokens[i]);
                    }
                    break;
            }
            m_scheduled = count;
        }

        private void ScheduleUni()
        {
            int count = m_count;
            int steps = m_arm.Suspensions;
            switch (m_arm.Workload)
            {
                case Workload.NextFrame:
                    if (m_arm.Typed)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            m_uniTyped[i] = UniNextFrameTyped(steps);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            m_uni[i] = UniNextFrame(steps);
                        }
                    }
                    break;
                case Workload.Yield:
                    for (int i = 0; i < count; i++)
                    {
                        m_uni[i] = UniYield(steps);
                    }
                    break;
                case Workload.DelayFrames:
                    for (int i = 0; i < count; i++)
                    {
                        m_uni[i] = UniDelayFrames(steps, m_tokens[i]);
                    }
                    break;
                case Workload.Delay:
                    for (int i = 0; i < count; i++)
                    {
                        m_uni[i] = UniDelay(steps, m_tokens[i]);
                    }
                    break;
                default:
                    for (int i = 0; i < count; i++)
                    {
                        m_uni[i] = UniWaitUntil(steps, m_tokens[i]);
                    }
                    break;
            }
            m_scheduled = count;
        }

        private void ConsumeOnity()
        {
            bool typed = m_arm.Typed;
            for (int i = 0; i < m_scheduled; i++)
            {
                try
                {
                    if (typed)
                    {
                        m_resultSum += m_onityTyped[i].GetAwaiter().GetResult();
                    }
                    else
                    {
                        m_onity[i].GetAwaiter().GetResult();
                    }
                    m_succeeded++;
                }
                catch (OperationCanceledException)
                {
                    m_canceled++;
                }
                catch (Exception exception)
                {
                    m_failed++;
                    m_firstFailure = m_firstFailure ?? exception;
                }
            }
        }

        private void ConsumeUni()
        {
            bool typed = m_arm.Typed;
            for (int i = 0; i < m_scheduled; i++)
            {
                try
                {
                    if (typed)
                    {
                        m_resultSum += m_uniTyped[i].GetAwaiter().GetResult();
                    }
                    else
                    {
                        m_uni[i].GetAwaiter().GetResult();
                    }
                    m_succeeded++;
                }
                catch (OperationCanceledException)
                {
                    m_canceled++;
                }
                catch (Exception exception)
                {
                    m_failed++;
                    m_firstFailure = m_firstFailure ?? exception;
                }
            }
        }

        // Workers: each library's default API shape; the only harness work inside is one counter increment.
        private async OnityTask OnityNextFrame(int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await OnityTask.NextFrame();
            }
            m_finished++;
        }

        private async OnityTask<int> OnityNextFrameTyped(int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await OnityTask.NextFrame();
            }
            m_finished++;
            return k_typedResult;
        }

        private async OnityTask OnityYield(int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await OnityTask.Yield(OnityPlayerLoopTiming.Update);
            }
            m_finished++;
        }

        private async OnityTask OnityDelayFrames(int steps, CancellationToken token)
        {
            for (int step = 0; step < steps; step++)
            {
                await OnityTask.DelayFrames(k_delayFrames, token);
            }
            m_finished++;
        }

        private async OnityTask OnityDelay(int steps, CancellationToken token)
        {
            for (int step = 0; step < steps; step++)
            {
                // Named arguments keep the TimeSpan/OnityTimeProvider overload (Onity.Reactive) out of resolution.
                await OnityTask.Delay(delaySeconds: k_delaySeconds, cancellationToken: token);
            }
            m_finished++;
        }

        private async OnityTask OnityWaitUntil(int steps, CancellationToken token)
        {
            for (int step = 0; step < steps; step++)
            {
                await OnityTask.WaitUntil(m_counterReady, token);
            }
            m_finished++;
        }

        private async UniTask UniNextFrame(int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await UniTask.NextFrame();
            }
            m_finished++;
        }

        private async UniTask<int> UniNextFrameTyped(int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await UniTask.NextFrame();
            }
            m_finished++;
            return k_typedResult;
        }

        private async UniTask UniYield(int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            m_finished++;
        }

        private async UniTask UniDelayFrames(int steps, CancellationToken token)
        {
            for (int step = 0; step < steps; step++)
            {
                await UniTask.DelayFrame(k_delayFrames, PlayerLoopTiming.Update, token);
            }
            m_finished++;
        }

        private async UniTask UniDelay(int steps, CancellationToken token)
        {
            for (int step = 0; step < steps; step++)
            {
                await UniTask.Delay(s_uniDelay, false, PlayerLoopTiming.Update, token);
            }
            m_finished++;
        }

        private async UniTask UniWaitUntil(int steps, CancellationToken token)
        {
            for (int step = 0; step < steps; step++)
            {
                await UniTask.WaitUntil(m_counterReady, PlayerLoopTiming.Update, token);
            }
            m_finished++;
        }
        private int ReadUniPending(int timing)
        {
            object queue = m_uniYielders.GetValue(timing);
            return queue == null ? 0 : (int)m_uniActionCount.GetValue(queue) + (int)m_uniWaitingCount.GetValue(queue);
        }

        private void CheckQueuesEmpty(string where)
        {
            string problem = null;
            if (m_onityPendingCount() != 0 || (int)m_onityDrainState.GetValue(null) != 0)
            {
                problem = "Onity deferred-return dispatcher not empty and idle";
            }
            object runner = m_onityRunnerInstance.GetValue(null);
            for (int i = 0; problem == null && runner != null && i < m_onityRunnerLists.Length; i++)
            {
                if (m_onityRunnerLists[i].GetValue(runner) is ICollection list && list.Count != 0)
                {
                    problem = "Onity task runner source list " + m_onityRunnerLists[i].Name + " holds " + list.Count;
                }
            }
            if (problem == null && m_onityPhases.GetValue(null) is Array phases)
            {
                for (int i = 0; i < phases.Length && problem == null; i++)
                {
                    if (m_onityPhasePending.GetValue(phases.GetValue(i)) is ICollection pending && pending.Count != 0)
                    {
                        problem = "Onity PlayerLoop phase " + i + " holds " + pending.Count;
                    }
                }
            }
            if (problem == null && m_onityTimers.GetValue(null) is ICollection timers && timers.Count != 0)
            {
                problem = "Onity timeout timers hold " + timers.Count;
            }
            for (int i = 0; problem == null && i < m_uniRunners.Length; i++)
            {
                object uniRunner = m_uniRunners.GetValue(i);
                if (uniRunner == null)
                {
                    continue;
                }
                object waitQueue = m_uniRunnerWaitQueue.GetValue(uniRunner);
                if ((int)m_uniRunnerTail.GetValue(uniRunner) != 0 || (bool)m_uniRunnerRunning.GetValue(uniRunner)
                    || (waitQueue != null && (int)m_uniQueueSize.GetValue(waitQueue) != 0))
                {
                    problem = "UniTask PlayerLoopRunner " + (PlayerLoopTiming)i + " not empty and idle";
                }
            }
            for (int i = 0; problem == null && i < m_uniYielders.Length; i++)
            {
                object queue = m_uniYielders.GetValue(i);
                if (queue != null && (ReadUniPending(i) != 0 || (bool)m_uniDequing.GetValue(queue)))
                {
                    problem = "UniTask ContinuationQueue " + (PlayerLoopTiming)i + " not empty and idle";
                }
            }
            if (problem != null)
            {
                throw new InvalidOperationException("Library queues not empty at " + where + ": " + problem);
            }
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

        // Teardown runs from a coroutine one frame later, never from inside a bracketed system.
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
                RemoveSystems();
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            try
            {
                m_report.sceneBehaviours = DescribeBehaviours();
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }
            RestoreSettings();
            if (failure == null && m_loggedError != null)
            {
                failure = new InvalidOperationException("Logged error during frame lifecycle: " + m_loggedError);
            }
            if (failure == null && m_armReports.Count != (m_arms == null ? -1 : m_arms.Length))
            {
                failure = new InvalidOperationException("Not every selected frame-lifecycle arm completed.");
            }

            m_report.arms = m_armReports.ToArray();
            m_report.validatedCohorts = m_validatedCohorts;
            m_report.expectedCohorts = m_expectedCohorts;
            m_report.goldensPassed = failure == null && m_validatedCohorts == m_expectedCohorts && m_expectedCohorts > 0;
            m_report.brackets = DescribeBrackets();
            m_report.boundaryChecklist = BuildChecklist(failure == null);
            if (failure == null && !m_report.boundaryChecklist.passed)
            {
                failure = new InvalidOperationException("Boundary checklist failed: " + m_report.boundaryChecklist.summary);
            }
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

        private BoundaryChecklist BuildChecklist(bool runPassed)
        {
            var checklist = new BoundaryChecklist();
            double worst = 0;
            bool sanity = m_armReports.Count > 0;
            bool frames = m_armReports.Count > 0;
            bool returns = m_armReports.Count > 0;
            int deferredLibraries = 0;
            foreach (Arm arm in m_armReports)
            {
                sanity &= arm.onity.sanityWithinTolerance && arm.uniTask.sanityWithinTolerance;
                worst = Math.Max(worst, Math.Max(arm.onity.sanityDeviation, arm.uniTask.sanityDeviation));
                frames &= arm.sameFrameCountPerLibrary;
                returns &= AreReturnsMeasured(arm.onity, true, ref deferredLibraries)
                    && AreReturnsMeasured(arm.uniTask, false, ref deferredLibraries);
            }
            bool resolved = m_unresolvedBracketEvents == 0 && m_bracketCallAnomalies == 0 && m_bracketTotalCalls != null;
            for (int i = 0; resolved && i < m_bracketTotalCalls.Length; i++)
            {
                resolved &= m_bracketTotalCalls[i] > 0;
            }
            checklist.everyBracketResolved = resolved && m_report.requiredSystemsValidated;
            checklist.unresolvedBracketEvents = m_unresolvedBracketEvents;
            checklist.bracketCallAnomalies = m_bracketCallAnomalies;
            checklist.measuredFrames = m_measuredFrames;
            // Measured: queues empty at every cohort end, a positive control-subtracted return frame inside the
            // bracketed systems wherever a library deferred returns past consumption, and no work outside brackets.
            checklist.returnPassesInsideBrackets = runPassed && returns && sanity;
            checklist.returnPassesEvidence = "Each cohort window ends one frame after consumption and every Onity and UniTask "
                + "queue was verified empty and idle at every cohort end. Libraries with returns pending after warmup "
                + "consumption (Onity IL2CPP Drain in Update/ScriptRunBehaviourUpdate, UniTask LastPostLateUpdate queue) "
                + "must show a positive median control-subtracted return-frame cost in their bracketed systems ("
                + deferredLibraries + " arm/library pairs deferred); the sanity bracket must also hold. Max pending after "
                + "warmup consumption: Onity=" + m_maxOnityPendingAfterConsume + ", UniTask LastPostLateUpdate="
                + m_maxUniPendingAfterConsume + ".";
            checklist.noLibraryWorkOutsideBrackets = sanity;
            checklist.worstSanityDeviation = worst;
            checklist.sanityEvidence = "Per arm and library, the control-subtracted whole-loop delta (first TimeUpdate system to "
                + "the end of the consume system) is compared with the control-subtracted sum of every bracket; tolerance "
                + k_sanityTolerance.ToString("P0") + ".";
            checklist.sameFrameCountPerLibrary = frames;
            checklist.cancellationRegistrationsDisposedInsideMeasuredFrames = m_registrationLeaks == 0;
            checklist.cancellationEvidence = "After each token cohort window, the live token is canceled outside timing; "
                + "Onity's cancellation-request counter must not move (" + m_registrationChecks + " checks, "
                + m_registrationLeaks + " leaks). The 50% tokens are canceled inside the timed schedule system of frame 1. "
                + "UniTask uses its default cancelImmediately=false and registers no callback.";
            checklist.harnessCallAnomalies = m_harnessCallAnomalies;
            checklist.harnessInertOutsideBrackets = !m_report.harnessDeclaresUnityUpdate
                && m_report.harnessSystemsInstalled == m_report.harnessSystemsExpected
                && m_harnessCallAnomalies == 0 && m_measuredFrames > 0;
            checklist.harnessEvidence = "Structural, checked at install: the harness MonoBehaviour declares Update/FixedUpdate/"
                + "LateUpdate=" + m_report.harnessDeclaresUnityUpdate + "; harness PlayerLoop systems "
                + m_report.harnessSystemsInstalled + " of expected " + m_report.harnessSystemsExpected + " (frame begin, "
                + "schedule, consume, one Before/After pair per bracket). Measured, every frame from the first arm: the frame-"
                + "begin, schedule and consume systems each ran exactly once (" + m_harnessCallAnomalies + " anomalies over "
                + m_measuredFrames + " frames counted from the first full frame after install; " + m_report.armStartDiscardedEvents
                + " pre-arm events discarded). By design, scheduling, cancel and consumption run only in the timed schedule "
                + "(first Update system) and consume (last PostLateUpdate system) systems; bookkeeping runs after the sanity "
                + "bracket closes; teardown runs after the last window. Work outside every bracket is detected by the "
                + "whole-loop sanity bracket.";
            checklist.passed = checklist.everyBracketResolved && checklist.returnPassesInsideBrackets
                && checklist.noLibraryWorkOutsideBrackets && checklist.sameFrameCountPerLibrary
                && checklist.cancellationRegistrationsDisposedInsideMeasuredFrames && checklist.harnessInertOutsideBrackets;
            checklist.summary = "resolved=" + checklist.everyBracketResolved + " returns=" + checklist.returnPassesInsideBrackets
                + " sanity=" + checklist.noLibraryWorkOutsideBrackets + " (worst " + worst.ToString("F4") + ") frames="
                + checklist.sameFrameCountPerLibrary + " registrations=" + checklist.cancellationRegistrationsDisposedInsideMeasuredFrames
                + " harness=" + checklist.harnessInertOutsideBrackets;
            return checklist;
        }

        // A library that left returns pending after consumption must pay for them in the bracketed return frame.
        private static bool AreReturnsMeasured(LibraryResult result, bool onity, ref int deferredLibraries)
        {
            bool deferred = false;
            foreach (Sample warmup in result.warmups)
            {
                if (warmup != null && (onity ? warmup.onityPendingAfterConsume : warmup.uniTaskPendingAfterConsume) > 0)
                {
                    deferred = true;
                }
            }
            bool emptyAtEnd = true;
            var returned = new double[result.samples.Length];
            for (int i = 0; i < returned.Length; i++)
            {
                emptyAtEnd &= result.samples[i] != null && result.samples[i].queuesEmptyAtEnd;
                returned[i] = result.samples[i] == null ? 0 : result.samples[i].returnFrameNanosecondsPerOperation;
            }
            if (!deferred)
            {
                return emptyAtEnd;
            }
            deferredLibraries++;
            return emptyAtEnd && Median(returned) > 0;
        }

        private BracketInfo[] DescribeBrackets()
        {
            if (m_bracketTypes == null)
            {
                return new BracketInfo[0];
            }
            var brackets = new BracketInfo[m_bracketTypes.Length];
            for (int i = 0; i < brackets.Length; i++)
            {
                brackets[i] = new BracketInfo
                {
                    index = i,
                    name = m_bracketNames[i],
                    systemType = m_bracketTypes[i].FullName,
                    phase = m_bracketPhases[i],
                    owner = m_bracketIsOnity[i] ? "OnityTask" : "UniTask",
                    oncePerFrame = m_bracketOncePerFrame[i],
                    totalCalls = m_bracketTotalCalls[i]
                };
            }
            return brackets;
        }

        private static string[] DescribeBehaviours()
        {
            MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            var names = new string[behaviours.Length];
            for (int i = 0; i < behaviours.Length; i++)
            {
                names[i] = behaviours[i].GetType().FullName + " on " + behaviours[i].gameObject.name;
            }
            return names;
        }

        private void RemoveSystems()
        {
            if (!m_installed)
            {
                return;
            }
            m_installed = false;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveHarnessSystems(ref loop);
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
            if (m_matchedPolicy)
            {
                m_sourcePoolCapacity.SetValue(null, m_oldSourcePoolCapacity);
                m_matchedPolicy = false;
            }
            if (m_hasSettings)
            {
                RestoreCollector();
                OnityTask.FlowExecutionContext = m_oldFlow;
                OnityTaskTracker.IsEnabled = m_oldTracker;
                OnityTaskTracker.EnableStackTrace = m_oldStackTrace;
                OnityTask.RunnerPoolCapacity = m_oldRunnerCapacity;
                Time.captureDeltaTime = m_oldCaptureDeltaTime;
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
                RemoveSystems();
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

        private static string GetArgumentValue(string argumentName)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
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
            internal readonly string Gate;
            internal readonly Workload Workload;
            internal readonly bool Typed;
            internal readonly int Suspensions;
            internal readonly int Cohort;
            internal readonly TokenMode Token;

            internal ArmDefinition(string id, string gate, Workload workload, bool typed, int suspensions, int cohort,
                TokenMode token)
            {
                Id = id;
                Gate = gate;
                Workload = workload;
                Typed = typed;
                Suspensions = suspensions;
                Cohort = cohort;
                Token = token;
            }
        }

        private sealed class BracketMarker
        {
            private readonly int m_index;
            internal readonly PlayerLoopSystem.UpdateFunction Before;
            internal readonly PlayerLoopSystem.UpdateFunction After;

            internal BracketMarker(int index)
            {
                m_index = index;
                Before = OnBefore;
                After = OnAfter;
            }

            private void OnBefore()
            {
                OnityTaskFrameLifecycleBenchmarkRunner runner = s_active;
                if (runner != null && runner.m_installed)
                {
                    runner.BeginBracket(m_index);
                }
            }

            private void OnAfter()
            {
                OnityTaskFrameLifecycleBenchmarkRunner runner = s_active;
                if (runner != null && runner.m_installed)
                {
                    runner.EndBracket(m_index);
                }
            }
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public string suite = "framelifecycle";
            public string title = "Frame lifecycle: async methods awaiting real PlayerLoop waits (G2, G3)";
            public string measurementScope = "Complete lifecycles under the real PlayerLoop: schedule N async workers, every "
                + "suspension and resumption, timed-wait polling, cancellation, one native GetResult consumption and both "
                + "libraries' runner returns (Mono inline; IL2CPP Onity Drain / UniTask LastPostLateUpdate queue in the "
                + "return frame). Library ticks per frame = sum of the active library's bracketed systems plus the harness "
                + "schedule and consume systems.";
            public string accounting = "Per sample: 8 empty control frames precede the cohort; cost per op = (sum over the "
                + "cohort's frames - frames x median control frame) / ops. Every sample of an arm spans the same frame count "
                + "for both libraries (fixed from warmups 2 and 3). Breakdown: schedule frame 0, completion frames 1..consume, "
                + "return frame consume+1, trailing frames.";
            public string settingsPolicy = "Onity FlowExecutionContext=false (UniTask semantics) and tracker off, set before "
                + "the run and restored after; Onity runner retention 128 required; Time.captureDeltaTime=0.02 so Delay(0.05 s) "
                + "spans a deterministic frame count in both libraries while frames stay uncapped; UniTask pinned 2.5.11 with "
                + "its unbounded pool and default cancelImmediately=false. 4096 arms use policy 'matched' only when "
                + "OnityTask.SourcePoolCapacity exists, otherwise policy 'default'.";
            public string order = "Three warmup cohorts and eight sample cohorts (six at 4096) per library, interleaved A,B,B,A "
                + "by sample index; libraryOrder is kept per sample.";
            public string allocation = "Calibrated counter slice per control block and per cohort window (Mono per-thread "
                + "counter, IL2CPP HeapDelta with the collector disabled inside the slice). bytesPerOperation = (cohort "
                + "bytes - frames x control bytes / 8 control frames) / ops when both slices are valid (bytesValid), else "
                + "-1; rawBytesPerOperation = cohort bytes / ops without control subtraction. No zero-allocation claim "
                + "from HeapDelta.";
            public bool completed;
            public string failure;
            public string loggedError;
            public string generatedAtUtc;
            public OnityTaskBenchmarkEnvironment environment;
            public bool isIl2Cpp = IsIl2Cpp;
            public long stopwatchFrequency = Stopwatch.Frequency;
            public bool onityFlowExecutionContext;
            public int onityRunnerPoolCapacity = k_defaultRunnerPoolCapacity;
            public bool onitySourcePoolCapacityAvailable;
            public float captureDeltaTime = k_captureDeltaTime;
            public float delaySeconds = k_delaySeconds;
            public int delayFrames = k_delayFrames;
            public int waitUntilFrames = k_waitUntilFrames;
            public int warmupCohorts = k_warmups;
            public int sampleCohorts = k_samples;
            public int largeCohortSampleCohorts = k_largeCohortSamples;
            public int controlFramesPerSample = k_controlFrames;
            public double sanityTolerance = k_sanityTolerance;
            public string armFilter;
            public string[] predeclaredArms;
            public string[] selectedArms;
            public bool counterCalibrated;
            public string counterKind;
            public string counterDescription;
            public string counterRejections;
            public long counterCalibrationBytes;
            public long counterEmptyBytes;
            public string[] requiredSystems;
            public bool requiredSystemsValidated;
            public BracketInfo[] brackets;
            public string[] installedLoop;
            public int installFrameDiscardedEvents;
            public int armStartDiscardedEvents;
            public int harnessSystemsInstalled;
            public int harnessSystemsExpected;
            public bool harnessDeclaresUnityUpdate;
            public string[] sceneBehaviours;
            public int validatedCohorts;
            public int expectedCohorts;
            public bool goldensPassed;
            public BoundaryChecklist boundaryChecklist;
            public Arm[] arms;
        }

        [Serializable]
        private sealed class BoundaryChecklist
        {
            public bool passed;
            public string summary;
            public bool everyBracketResolved;
            public int unresolvedBracketEvents;
            public int bracketCallAnomalies;
            public int measuredFrames;
            public bool returnPassesInsideBrackets;
            public string returnPassesEvidence;
            public bool noLibraryWorkOutsideBrackets;
            public double worstSanityDeviation;
            public string sanityEvidence;
            public bool sameFrameCountPerLibrary;
            public bool cancellationRegistrationsDisposedInsideMeasuredFrames;
            public string cancellationEvidence;
            public bool harnessInertOutsideBrackets;
            public int harnessCallAnomalies;
            public string harnessEvidence;
        }

        [Serializable]
        private sealed class BracketInfo
        {
            public int index;
            public string name;
            public string systemType;
            public string phase;
            public string owner;
            public bool oncePerFrame;
            public long totalCalls;
        }

        [Serializable]
        private sealed class Arm
        {
            public string id;
            public string gate;
            public string workload;
            public string shape;
            public string tokenMode;
            public int sequentialSuspensions;
            public int cohortSize;
            public string policy;
            public int onityRunnerPoolCapacity;
            public int warmupCohorts;
            public int sampleCohorts;
            public int framesPerCohort;
            public bool sameFrameCountPerLibrary;
            public bool validated;
            public double[] pairRatios;
            public double medianPairRatio;
            public double ratioOfMedians;
            public LibraryResult onity;
            public LibraryResult uniTask;
        }

        [Serializable]
        private sealed class LibraryResult
        {
            public string library;
            public double medianNanosecondsPerOperation;
            public double meanNanosecondsPerOperation;
            public double minNanosecondsPerOperation;
            public double maxNanosecondsPerOperation;
            public double medianBytesPerOperation;
            public int medianConsumeFrame;
            public double sanityNetBracketTicks;
            public double sanityNetWholeTicks;
            public double sanityDeviation;
            public bool sanityWithinTolerance;
            public Sample[] warmups;
            public Sample[] samples;
        }

        [Serializable]
        private sealed class Sample
        {
            public int index;
            public int libraryOrder;
            public bool warmup;
            public int operations;
            public int frames;
            public int consumeFrame;
            public int succeeded;
            public int canceled;
            public long resultSum;
            public bool validated;
            public bool queuesEmptyAtEnd;
            public string registrationCheck;
            public int onityPendingAfterConsume = -1;
            public int uniTaskPendingAfterConsume = -1;
            public double nanosecondsPerOperation;
            public double scheduleFrameNanosecondsPerOperation;
            public double completionFramesNanosecondsPerOperation;
            public double returnFrameNanosecondsPerOperation;
            public double trailingFramesNanosecondsPerOperation;
            public long libraryTicks;
            public long allTicks;
            public long wholeTicks;
            public long netLibraryTicks;
            public long netAllTicks;
            public long netWholeTicks;
            public long controlMedianLibraryTicks;
            public long controlMedianAllTicks;
            public long controlMedianWholeTicks;
            public long heapBytes = -1;
            public bool heapValid;
            public bool bytesValid;
            public double bytesPerOperation = -1;
            public double rawBytesPerOperation = -1;
            public long controlHeapBytes = -1;
            public bool controlHeapValid;
            public long[] frameLibraryTicks;
            public long[] frameInactiveTicks;
            public long[] frameScheduleTicks;
            public long[] frameConsumeTicks;
            public long[] frameWholeTicks;
            public long[] controlLibraryTicks;
            public long[] controlAllTicks;
            public long[] controlWholeTicks;
            public long[] bracketTicks;

            internal Sample(int brackets)
            {
                frameLibraryTicks = new long[k_maxFrames];
                frameInactiveTicks = new long[k_maxFrames];
                frameScheduleTicks = new long[k_maxFrames];
                frameConsumeTicks = new long[k_maxFrames];
                frameWholeTicks = new long[k_maxFrames];
                controlLibraryTicks = new long[k_controlFrames];
                controlAllTicks = new long[k_controlFrames];
                controlWholeTicks = new long[k_controlFrames];
                bracketTicks = new long[brackets];
            }
        }
    }
}
