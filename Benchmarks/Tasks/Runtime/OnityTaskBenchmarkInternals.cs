using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Onity.Unity.Async;

namespace Onity.Benchmarks
{
    /// <summary>
    /// The only file through which the primary, builderlifecycle and throughput suites and the shared
    /// environment capture touch Onity internals. Every hook binds by reflection, is optional and
    /// describes what it bound, so a runtime redesign degrades a hook to "absent" instead of failing a
    /// suite. Internal members read here (keep them, or re-provide them and update this file):
    /// <list type="bullet">
    /// <item><c>Onity.Unity.Async.OnityTaskMainThreadDispatcher</c>: <c>static void Drain()</c>,
    /// <c>static int PendingCount { get; }</c>, <c>static int s_drainState</c>.</item>
    /// <item><c>Onity.Unity.Async.OnityTaskRunner</c>: <c>static s_instance</c> and the instance lists
    /// <c>m_updateSources</c>, <c>m_fixedUpdateSources</c>, <c>m_lateUpdateSources</c>.</item>
    /// <item><c>Onity.Unity.Async.OnityTaskPlayerLoop</c>: <c>static int PendingWorkCount { get; }</c>
    /// (queued waits, worker inboxes and timers; preferred), else <c>static s_phases</c> (elements with a
    /// <c>Pending</c> collection) and <c>static s_timers</c>.</item>
    /// <item><c>Onity.Unity.Async.OnityAsyncExecutionContext</c>: <c>s_fastCapture</c> and
    /// <c>s_runPreservingContext</c> (environment evidence only).</item>
    /// </list>
    /// Pinned UniTask internals are bound by the suites themselves; they cannot change underneath them.
    /// </summary>
    internal static class OnityTaskBenchmarkInternals
    {
        private const BindingFlags k_static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags k_instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string k_dispatcherType = "Onity.Unity.Async.OnityTaskMainThreadDispatcher";
        private const string k_runnerType = "Onity.Unity.Async.OnityTaskRunner";
        private const string k_contextType = "Onity.Unity.Async.OnityAsyncExecutionContext";

        /// <summary>
        /// Binds Onity's main-thread deferred-work queue. The drain is null when the runtime has no such
        /// queue; callers then run an empty pass. Pending and drain-state reads report -1 when absent.
        /// </summary>
        internal static DeferredReturnQueue BindDeferredReturnQueue()
        {
            return new DeferredReturnQueue(typeof(OnityTask).Assembly.GetType(k_dispatcherType, false));
        }

        /// <summary>
        /// Binds read-only probes of Onity's pending frame, PlayerLoop, timer and dispatcher work, used
        /// outside timings to check that the library is idle at a sample end. Missing members are skipped
        /// and listed in the description.
        /// </summary>
        internal static PendingWorkProbe BindPendingWorkProbe()
        {
            return new PendingWorkProbe();
        }

        /// <summary>Reports which execution-context path the runtime selected; never throws.</summary>
        /// <param name="path">InternalPair, PublicFallback or Unknown.</param>
        /// <param name="evidence">What was observed.</param>
        internal static void CaptureExecutionContextPath(out string path, out string evidence)
        {
            path = "Unknown";
            try
            {
                Type type = typeof(OnityTask).Assembly.GetType(k_contextType, false);
                if (type == null)
                {
                    evidence = k_contextType + " not found in this runtime.";
                    return;
                }

                // Initialize the probe before any smoke case suppresses flow. Read the fields actually used by
                // Capture/TryRunPreservingContext; an unused getter may be stripped.
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                FieldInfo capture = type.GetField("s_fastCapture", BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo run = type.GetField("s_runPreservingContext", BindingFlags.Static | BindingFlags.NonPublic);
                if (capture == null || run == null)
                {
                    evidence = "Runtime capture/run fields could not be inspected.";
                    return;
                }

                bool hasCapture = capture.GetValue(null) != null;
                bool hasRun = run.GetValue(null) != null;
                path = hasCapture && hasRun ? "InternalPair" : "PublicFallback";
                evidence = "s_fastCapture=" + hasCapture + "; s_runPreservingContext=" + hasRun
                    + "; runtime probe initialized before suppression";
            }
            catch (Exception exception)
            {
                path = "Unknown";
                evidence = "Execution-context probe failed: " + exception.GetType().Name + ": " + exception.Message;
            }
        }

        /// <summary>Onity's main-thread deferred-work queue as bound at run start.</summary>
        internal sealed class DeferredReturnQueue
        {
            private readonly Func<int> m_pendingCount;
            private readonly FieldInfo m_drainState;

            internal DeferredReturnQueue(Type type)
            {
                if (type == null)
                {
                    Description = "absent: " + k_dispatcherType + " not found; the Onity return pass is an empty measured call.";
                    return;
                }

                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                MethodInfo drain = type.GetMethod("Drain", k_static, null, Type.EmptyTypes, null);
                if (drain != null && drain.ReturnType == typeof(void))
                {
                    Drain = (Action)Delegate.CreateDelegate(typeof(Action), drain, false);
                }

                MethodInfo pending = type.GetProperty("PendingCount", k_static)?.GetGetMethod(true);
                if (pending != null && pending.IsStatic && pending.ReturnType == typeof(int))
                {
                    m_pendingCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), pending, false);
                }

                FieldInfo state = type.GetField("s_drainState", k_static);
                if (state != null && state.FieldType == typeof(int))
                {
                    m_drainState = state;
                }

                Description = (Drain != null ? type.FullName + ".Drain bound (cached Action)" : "Drain absent (empty pass)")
                    + "; PendingCount " + (m_pendingCount != null ? "bound" : "absent")
                    + "; s_drainState " + (m_drainState != null ? "bound" : "absent")
                    + "; queue state is read outside timings and is not asserted before the first pass.";
            }

            /// <summary>Production drain, or null when the runtime has no such queue.</summary>
            internal Action Drain { get; }

            /// <summary>What was bound, for the report.</summary>
            internal string Description { get; }

            /// <summary>Pending entries, or -1 when the count is not observable.</summary>
            internal int ReadPending()
            {
                return m_pendingCount == null ? -1 : m_pendingCount();
            }

            /// <summary>Drain state (0 idle), or -1 when not observable.</summary>
            internal int ReadDrainState()
            {
                return m_drainState == null ? -1 : (int)m_drainState.GetValue(null);
            }

            /// <summary>True when every observable read shows an empty, idle queue.</summary>
            internal bool IsIdle()
            {
                return ReadPending() <= 0 && ReadDrainState() <= 0;
            }
        }

        /// <summary>Read-only probes of Onity's pending work.</summary>
        internal sealed class PendingWorkProbe
        {
            private static readonly string[] s_runnerListNames = { "m_updateSources", "m_fixedUpdateSources", "m_lateUpdateSources" };

            private readonly Func<int> m_dispatcherPending;
            private readonly Func<int> m_schedulerPending;
            private readonly FieldInfo m_runnerInstance;
            private readonly FieldInfo[] m_runnerLists;
            private readonly FieldInfo m_phases;
            private readonly FieldInfo m_phasePending;
            private readonly FieldInfo m_timers;

            internal PendingWorkProbe()
            {
                Assembly onity = typeof(OnityTask).Assembly;
                string bound = string.Empty;
                string missing = string.Empty;

                Type dispatcher = onity.GetType(k_dispatcherType, false);
                MethodInfo pending = dispatcher?.GetProperty("PendingCount", k_static)?.GetGetMethod(true);
                if (pending != null && pending.IsStatic && pending.ReturnType == typeof(int))
                {
                    m_dispatcherPending = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), pending, false);
                    bound += "dispatcher.PendingCount; ";
                }
                else
                {
                    missing += "dispatcher.PendingCount; ";
                }

                Type runner = onity.GetType(k_runnerType, false);
                m_runnerInstance = runner?.GetField("s_instance", k_static);
                FieldInfo[] lists = new FieldInfo[s_runnerListNames.Length];
                int found = 0;
                for (int i = 0; i < s_runnerListNames.Length; i++)
                {
                    FieldInfo field = m_runnerInstance == null ? null : runner.GetField(s_runnerListNames[i], k_instance);
                    if (field != null && typeof(ICollection).IsAssignableFrom(field.FieldType))
                    {
                        lists[found++] = field;
                        bound += "OnityTaskRunner." + s_runnerListNames[i] + "; ";
                    }
                    else
                    {
                        missing += "OnityTaskRunner." + s_runnerListNames[i] + "; ";
                    }
                }
                Array.Resize(ref lists, found);
                m_runnerLists = lists;

                Type loop = typeof(OnityTaskPlayerLoop);
                MethodInfo schedulerPending = loop.GetProperty("PendingWorkCount", k_static)?.GetGetMethod(true);
                if (schedulerPending != null && schedulerPending.IsStatic && schedulerPending.ReturnType == typeof(int))
                {
                    // The PERF-7 scheduler sums its queues, worker inboxes and timers itself.
                    m_schedulerPending = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), schedulerPending, false);
                    bound += "OnityTaskPlayerLoop.PendingWorkCount; ";
                }

                FieldInfo phases = m_schedulerPending != null ? null : loop.GetField("s_phases", k_static);
                FieldInfo phasePending = phases?.FieldType.GetElementType()?.GetField("Pending", k_instance);
                if (phasePending != null && typeof(ICollection).IsAssignableFrom(phasePending.FieldType))
                {
                    m_phases = phases;
                    m_phasePending = phasePending;
                    bound += "OnityTaskPlayerLoop.s_phases[].Pending; ";
                }
                else if (m_schedulerPending == null)
                {
                    missing += "OnityTaskPlayerLoop.s_phases[].Pending; ";
                }

                FieldInfo timers = m_schedulerPending != null ? null : loop.GetField("s_timers", k_static);
                if (timers != null && typeof(ICollection).IsAssignableFrom(timers.FieldType))
                {
                    m_timers = timers;
                    bound += "OnityTaskPlayerLoop.s_timers; ";
                }
                else if (m_schedulerPending == null)
                {
                    missing += "OnityTaskPlayerLoop.s_timers; ";
                }

                IsAvailable = bound.Length != 0;
                Description = "bound: " + (bound.Length == 0 ? "none" : bound.TrimEnd(' ', ';'))
                    + (missing.Length == 0 ? string.Empty : "; absent: " + missing.TrimEnd(' ', ';'));
            }

            /// <summary>True when at least one probe bound.</summary>
            internal bool IsAvailable { get; }

            /// <summary>Bound and absent probes, for the report.</summary>
            internal string Description { get; }

            /// <summary>Sums the pending entries of every bound probe; allocates, so call it outside timings.</summary>
            /// <param name="detail">Non-zero probes, or empty.</param>
            internal int Read(out string detail)
            {
                int total = 0;
                detail = string.Empty;
                if (m_dispatcherPending != null)
                {
                    int count = m_dispatcherPending();
                    total += count;
                    if (count != 0)
                    {
                        detail += "dispatcher=" + count + " ";
                    }
                }

                if (m_schedulerPending != null)
                {
                    int count = m_schedulerPending();
                    total += count;
                    if (count != 0)
                    {
                        detail += "playerLoop=" + count + " ";
                    }
                }

                object runner = m_runnerInstance?.GetValue(null);
                for (int i = 0; runner != null && i < m_runnerLists.Length; i++)
                {
                    if (m_runnerLists[i].GetValue(runner) is ICollection list && list.Count != 0)
                    {
                        total += list.Count;
                        detail += m_runnerLists[i].Name + "=" + list.Count + " ";
                    }
                }

                if (m_phases != null && m_phases.GetValue(null) is Array phases)
                {
                    for (int i = 0; i < phases.Length; i++)
                    {
                        if (m_phasePending.GetValue(phases.GetValue(i)) is ICollection pending && pending.Count != 0)
                        {
                            total += pending.Count;
                            detail += "playerLoopPhase" + i + "=" + pending.Count + " ";
                        }
                    }
                }

                if (m_timers != null && m_timers.GetValue(null) is ICollection timers && timers.Count != 0)
                {
                    total += timers.Count;
                    detail += "timers=" + timers.Count + " ";
                }

                detail = detail.Trim();
                return total;
            }
        }
    }
}
