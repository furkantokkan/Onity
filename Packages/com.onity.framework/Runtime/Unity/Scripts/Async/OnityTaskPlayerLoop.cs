using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Onity.Unity.Async
{
    /// <summary>PlayerLoop phases supported by explicit Onity task waits.</summary>
    public enum OnityPlayerLoopTiming
    {
        /// <summary>Immediately after Update.ScriptRunBehaviourUpdate.</summary>
        Update,
        /// <summary>Immediately after FixedUpdate.ScriptRunBehaviourFixedUpdate.</summary>
        FixedUpdate,
        /// <summary>Immediately after PreLateUpdate.ScriptRunBehaviourLateUpdate.</summary>
        LateUpdate
    }

    /// <summary>Owns the explicit task timing nodes independently of the legacy runner.</summary>
    public static class OnityTaskPlayerLoop
    {
        private sealed class UpdateMarker
        {
        }

        private sealed class FixedUpdateMarker
        {
        }

        private sealed class LateUpdateMarker
        {
        }

        private sealed class Phase
        {
            internal List<OnityPlayerLoopTaskSource> Pending =
                new List<OnityPlayerLoopTaskSource>(32);
            internal List<OnityPlayerLoopTaskSource> Spare =
                new List<OnityPlayerLoopTaskSource>(32);
            internal ulong Pass;
            internal bool Draining;
        }

        internal sealed class TimeoutEntry
        {
            internal readonly int Generation;
            internal readonly int Epoch;
            internal readonly ulong RegisteredPass;
            internal readonly double StartTime;
            internal readonly double Duration;
            internal readonly bool UseUnscaledTime;
            internal IOnityTimeoutTimerSink Sink;
            internal int StopRequested;

            internal TimeoutEntry(
                int generation, int epoch, ulong pass, double startTime, double duration,
                bool useUnscaledTime, IOnityTimeoutTimerSink sink)
            {
                Generation = generation;
                Epoch = epoch;
                RegisteredPass = pass;
                StartTime = startTime;
                Duration = duration;
                UseUnscaledTime = useUnscaledTime;
                Sink = sink;
            }
        }

        private static readonly Phase[] s_phases = { new Phase(), new Phase(), new Phase() };
        private static List<TimeoutEntry> s_timers = new List<TimeoutEntry>(32);
        private static List<TimeoutEntry> s_spareTimers = new List<TimeoutEntry>(32);
        private static ulong s_timerPass;
        private static int s_timerGeneration;
        private static readonly PlayerLoopSystem.UpdateFunction s_update = DrainUpdate;
        private static readonly PlayerLoopSystem.UpdateFunction s_fixedUpdate = () => Drain(1);
        private static readonly PlayerLoopSystem.UpdateFunction s_lateUpdate = () => Drain(2);
        private static int s_mainThreadId;
        private static int s_epoch;
        private static bool s_sessionOpen;
        private static bool s_accepting;
        private static bool s_installed;
        private static bool s_retiring;
        private static bool s_shuttingDown;
        private static OnityEndOfFrameRunner s_endOfFrameRunner;
        private static bool s_retiringEndOfFrame;

        /// <summary>
        /// Installs or repairs Onity's nodes in the current PlayerLoop, retaining pending waits.
        /// </summary>
        /// <remarks>Requires Unity's main thread and active Play/player execution. Call
        /// after an external whole-loop replacement. Missing anchors fault pending waits
        /// and reject new acceptance; restore the anchors and explicitly retry this method.
        /// This method does not restore a cached or default loop.</remarks>
        /// <exception cref="InvalidOperationException">The session is unavailable or
        /// the current PlayerLoop does not contain all required anchors exactly once.</exception>
        public static void Initialize()
        {
            ValidateContext();
            try
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                RemoveOwned(ref loop);
                ValidateAnchor(loop, typeof(UnityEngine.PlayerLoop.Update),
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
                ValidateAnchor(loop, typeof(UnityEngine.PlayerLoop.FixedUpdate),
                    typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate));
                ValidateAnchor(loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate),
                    typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate));
                if (!InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                    new PlayerLoopSystem { type = typeof(UpdateMarker), updateDelegate = s_update })
                    || !InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
                        new PlayerLoopSystem { type = typeof(FixedUpdateMarker), updateDelegate = s_fixedUpdate })
                    || !InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate),
                        new PlayerLoopSystem { type = typeof(LateUpdateMarker), updateDelegate = s_lateUpdate }))
                {
                    throw new InvalidOperationException("Onity timing nodes could not be inserted.");
                }
                PlayerLoop.SetPlayerLoop(loop);
                s_installed = true;
                s_accepting = true;
            }
            catch (Exception exception)
            {
                s_accepting = false;
                s_installed = false;
                s_epoch++;
                RemoveNodes();
                Retire(exception);
                throw;
            }
        }

        internal static OnityTask Schedule(
            OnityPlayerLoopTiming timing, int frameCount, bool yieldOnly, CancellationToken token)
        {
            if ((uint)timing > (uint)OnityPlayerLoopTiming.LateUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(timing));
            }
            if (frameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }

            ValidateContext();
            if (token.IsCancellationRequested)
            {
                return OnityTask.FromCanceled(token);
            }
            if (!yieldOnly && frameCount == 0)
            {
                return OnityTask.Completed;
            }
            if (!s_accepting)
            {
                throw new InvalidOperationException("The Onity timing loop requires a successful explicit repair.");
            }
            if (!s_installed)
            {
                Initialize();
            }

            Phase phase = s_phases[(int)timing];
            OnityPlayerLoopTaskSource source = OnityPlayerLoopTaskSource.Rent(
                phase.Pass, unchecked((uint)Time.frameCount), frameCount, token);
            OnityTask task = new OnityTask(source);
            phase.Pending.Add(source);
            return task;
        }

        private static void ValidateContext()
        {
            if (Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
            {
                throw new InvalidOperationException("Onity timing waits require Unity's main thread.");
            }
            if (!Application.isPlaying || !s_sessionOpen || s_retiring || s_shuttingDown)
            {
                throw new InvalidOperationException("Onity timing waits require an active Play/player session.");
            }
        }

        internal static OnityTask ScheduleEndOfFrame(CancellationToken token)
        {
            ValidateContext();
            if (!s_accepting || s_retiringEndOfFrame)
            {
                throw new InvalidOperationException("The Onity end-of-frame owner is not accepting waits.");
            }
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                throw new PlatformNotSupportedException("End-of-frame waits are unavailable in Editor batch mode.");
            }
#endif
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                throw new PlatformNotSupportedException("End-of-frame waits require a graphics device.");
            }
            if (token.IsCancellationRequested)
            {
                return OnityTask.FromCanceled(token);
            }
            if (!s_installed)
            {
                Initialize();
            }

            if (s_endOfFrameRunner == null)
            {
                var host = new GameObject("Onity End Of Frame")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                UnityEngine.Object.DontDestroyOnLoad(host);
                OnityEndOfFrameRunner runner = host.AddComponent<OnityEndOfFrameRunner>();
                s_endOfFrameRunner = runner;
                try
                {
                    runner.StartPump();
                }
                catch
                {
                    RetireEndOfFrame(runner);
                    throw;
                }
            }
            return s_endOfFrameRunner.Schedule(token);
        }

        internal static void RetireEndOfFrame(OnityEndOfFrameRunner runner)
        {
            if (!ReferenceEquals(runner, s_endOfFrameRunner) || s_retiringEndOfFrame)
            {
                return;
            }

            s_retiringEndOfFrame = true;
            s_endOfFrameRunner = null;
            GameObject host = runner.gameObject;
            try
            {
                List<OnityEndOfFrameTaskSource> pending = runner.Detach();
                OnityEndOfFrameRunner.RetirePending(pending, null);
            }
            finally
            {
                try
                {
                    DestroyEndOfFrameHost(host);
                }
                finally
                {
                    s_retiringEndOfFrame = false;
                }
            }
        }

        private static void DestroyEndOfFrameHost(GameObject host)
        {
            if (host != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(host);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
        }

        internal static TimeoutEntry RegisterTimeout(
            float seconds, bool useUnscaledTime, IOnityTimeoutTimerSink sink)
        {
            ValidateContext();
            if (!s_accepting)
            {
                throw new InvalidOperationException("The Onity timeout timer requires a successful explicit loop repair.");
            }
            if (!s_installed)
            {
                Initialize();
            }

            int generation = unchecked(++s_timerGeneration);
            if (generation == 0)
            {
                generation = unchecked(++s_timerGeneration);
            }
            double now = useUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
            var entry = new TimeoutEntry(
                generation, s_epoch, s_timerPass, now, (double)seconds, useUnscaledTime, sink);
            // No callbacks occur here. The caller assigns this identity before observing its producer.
            s_timers.Add(entry);
            return entry;
        }

        internal static void StopTimeout(TimeoutEntry entry, int generation)
        {
            // May run on a producer worker. Do not inspect Unity/session/queue state.
            if (entry != null && entry.Generation == generation)
            {
                Volatile.Write(ref entry.StopRequested, 1);
            }
        }

        private static void DrainUpdate()
        {
            Phase update = s_phases[0];
            if (!s_accepting || update.Draining)
            {
                return;
            }

            update.Draining = true;
            update.Pass++;
            s_timerPass++;
            int epoch = s_epoch;
            // Snapshot all bounds before publishing cancellation continuations.
            int updateCount = update.Pending.Count;
            int fixedCount = s_phases[1].Pending.Count;
            int lateCount = s_phases[2].Pending.Count;
            int timerCount = s_timers.Count;
            try
            {
                CancelPending(update, updateCount, epoch);
                if (epoch == s_epoch)
                {
                    CancelPending(s_phases[1], fixedCount, epoch);
                }
                if (epoch == s_epoch)
                {
                    CancelPending(s_phases[2], lateCount, epoch);
                }
                if (epoch == s_epoch)
                {
                    s_endOfFrameRunner?.CancelPending();
                }
                if (epoch == s_epoch)
                {
                    CompletePending(update, update.Pending.Count, epoch);
                }
                if (epoch == s_epoch)
                {
                    DrainTimers(timerCount, epoch);
                }
            }
            finally
            {
                update.Draining = false;
            }
        }

        private static void Drain(int index)
        {
            Phase phase = s_phases[index];
            if (!s_accepting || phase.Draining)
            {
                return;
            }

            phase.Draining = true;
            phase.Pass++;
            int epoch = s_epoch;
            try
            {
                CompletePending(phase, phase.Pending.Count, epoch);
            }
            finally
            {
                phase.Draining = false;
            }
        }

        private static void CancelPending(Phase phase, int count, int epoch)
        {
            for (int i = count - 1; i >= 0 && epoch == s_epoch; i--)
            {
                OnityPlayerLoopTaskSource source = phase.Pending[i];
                if (source.IsCancellationFlagged)
                {
                    RemoveAt(phase.Pending, i);
                    Publish(source, true, null);
                }
            }
        }

        private static void CompletePending(Phase phase, int count, int epoch)
        {
            uint frame = unchecked((uint)Time.frameCount);
            for (int i = count - 1; i >= 0 && epoch == s_epoch; i--)
            {
                OnityPlayerLoopTaskSource source = phase.Pending[i];
                bool canceled = source.IsCancellationFlagged;
                if (source.RegisteredPass != phase.Pass && (canceled
                    || unchecked(frame - source.RegisteredFrame) >= source.MinimumFrames))
                {
                    RemoveAt(phase.Pending, i);
                    Publish(source, canceled, null);
                }
            }
        }

        private static void RemoveAt(List<OnityPlayerLoopTaskSource> queue, int index)
        {
            int last = queue.Count - 1;
            queue[index] = queue[last];
            queue.RemoveAt(last);
        }

        private static void DrainTimers(int count, int epoch)
        {
            if (count == 0)
            {
                return;
            }

            double unscaled = Time.unscaledTimeAsDouble;
            double scaled = Time.timeAsDouble;
            for (int i = count - 1; i >= 0 && epoch == s_epoch; i--)
            {
                TimeoutEntry entry = s_timers[i];
                if (entry.RegisteredPass == s_timerPass)
                {
                    continue;
                }
                bool stopped = Volatile.Read(ref entry.StopRequested) != 0;
                double elapsed = (entry.UseUnscaledTime ? unscaled : scaled) - entry.StartTime;
                if (stopped || elapsed >= entry.Duration)
                {
                    int last = s_timers.Count - 1;
                    s_timers[i] = s_timers[last];
                    s_timers.RemoveAt(last);
                    AcknowledgeTimer(entry, stopped ? OnityTimeoutTimerOutcome.Stopped
                        : OnityTimeoutTimerOutcome.Expired, null);
                }
            }
        }

        private static void AcknowledgeTimer(
            TimeoutEntry entry, OnityTimeoutTimerOutcome outcome, Exception failure)
        {
            IOnityTimeoutTimerSink sink = entry.Sink;
            int generation = entry.Generation;
            entry.Sink = null;
            try
            {
                sink?.Acknowledge(entry, generation, outcome, failure);
            }
            catch (Exception exception)
            {
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Publish(OnityPlayerLoopTaskSource source, bool canceled, Exception failure)
        {
            try
            {
                source.Publish(canceled, failure);
            }
            catch (Exception exception)
            {
                // A throwing continuation/logger must not abandon other accepted waits.
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Retire(Exception failure)
        {
            if (s_retiring)
            {
                return;
            }

            s_retiring = true;
            // Detach every phase before any continuation can inspect this owner.
            for (int i = 0; i < s_phases.Length; i++)
            {
                Phase phase = s_phases[i];
                List<OnityPlayerLoopTaskSource> pending = phase.Pending;
                phase.Pending = phase.Spare;
                phase.Spare = pending;
            }
            List<TimeoutEntry> timers = s_timers;
            s_timers = s_spareTimers;
            s_spareTimers = timers;
            OnityEndOfFrameRunner endOfFrameRunner = s_endOfFrameRunner;
            GameObject endOfFrameHost = endOfFrameRunner != null ? endOfFrameRunner.gameObject : null;
            s_endOfFrameRunner = null;
            List<OnityEndOfFrameTaskSource> endOfFramePending = endOfFrameRunner?.Detach();
            try
            {
                for (int i = 0; i < s_phases.Length; i++)
                {
                    List<OnityPlayerLoopTaskSource> pending = s_phases[i].Spare;
                    for (int j = pending.Count - 1; j >= 0; j--)
                    {
                        OnityPlayerLoopTaskSource source = pending[j];
                        pending[j] = null;
                        Publish(source, failure == null, failure);
                    }
                    pending.Clear();
                }
                for (int i = timers.Count - 1; i >= 0; i--)
                {
                    TimeoutEntry entry = timers[i];
                    timers.RemoveAt(i);
                    AcknowledgeTimer(entry, OnityTimeoutTimerOutcome.SessionEnded, failure);
                }
                OnityEndOfFrameRunner.RetirePending(endOfFramePending, failure);
            }
            finally
            {
                try
                {
                    DestroyEndOfFrameHost(endOfFrameHost);
                }
                finally
                {
                    s_retiring = false;
                }
            }
        }

        private static void CloseSession()
        {
            s_sessionOpen = false;
            s_accepting = false;
            s_installed = false;
            s_epoch++;
            RemoveNodes();
            Retire(null);
        }

        private static void CloseApplication()
        {
            s_shuttingDown = true;
            CloseSession();
        }

        private static void BeginSession(bool force)
        {
            if (s_shuttingDown || s_retiring || (!force && s_sessionOpen))
            {
                return;
            }

            int expectedEpoch = unchecked(s_epoch + 1);
            CloseSession();
            if (s_epoch == expectedEpoch && !s_shuttingDown && !s_retiring)
            {
                foreach (Phase phase in s_phases)
                {
                    phase.Pass = 0;
                }
                s_timerPass = 0;
                s_sessionOpen = true;
                s_accepting = true;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRuntime()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
#if !UNITY_EDITOR
            Application.quitting -= CloseApplication;
            Application.quitting += CloseApplication;
#endif
            BeginSession(true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallRuntime()
        {
            Initialize();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            UnityEditor.EditorApplication.quitting -= CloseApplication;
            UnityEditor.EditorApplication.quitting += CloseApplication;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= CloseApplication;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += CloseApplication;
            if (Application.isPlaying)
            {
                BeginSession(false);
            }
        }

        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode
                || state == UnityEditor.PlayModeStateChange.ExitingPlayMode
                || state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                CloseSession();
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            {
                // Subsystem registration and BeforeSceneLoad already accepted Awake work.
                BeginSession(false);
            }
        }
#endif

        private static bool IsOwned(Type type)
        {
            return type == typeof(UpdateMarker) || type == typeof(FixedUpdateMarker)
                || type == typeof(LateUpdateMarker);
        }

        private static void RemoveOwned(ref PlayerLoopSystem system)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return;
            }
            var retained = new List<PlayerLoopSystem>(children.Length);
            for (int i = 0; i < children.Length; i++)
            {
                if (IsOwned(children[i].type))
                {
                    continue;
                }
                PlayerLoopSystem child = children[i];
                RemoveOwned(ref child);
                retained.Add(child);
            }
            system.subSystemList = retained.ToArray();
        }

        private static void RemoveNodes()
        {
            try
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                RemoveOwned(ref loop);
                PlayerLoop.SetPlayerLoop(loop);
            }
            catch (Exception exception)
            {
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void ValidateAnchor(PlayerLoopSystem system, Type parent, Type anchor)
        {
            if (CountAnchor(system, anchor) != 1 || CountPlacement(system, parent, anchor) != 1)
            {
                throw new InvalidOperationException("Onity timing anchor missing or duplicated: " + anchor.FullName);
            }
        }

        private static int CountPlacement(PlayerLoopSystem system, Type parent, Type anchor)
        {
            int count = 0;
            if (system.subSystemList != null)
            {
                foreach (PlayerLoopSystem child in system.subSystemList)
                {
                    if (system.type == parent && child.type == anchor)
                    {
                        count++;
                    }
                    count += CountPlacement(child, parent, anchor);
                }
            }
            return count;
        }

        private static int CountAnchor(PlayerLoopSystem system, Type anchor)
        {
            int count = system.type == anchor ? 1 : 0;
            if (system.subSystemList != null)
            {
                foreach (PlayerLoopSystem child in system.subSystemList)
                {
                    count += CountAnchor(child, anchor);
                }
            }
            return count;
        }

        private static bool InsertAfter(ref PlayerLoopSystem system, Type anchor, PlayerLoopSystem node)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return false;
            }
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == anchor)
                {
                    var expanded = new PlayerLoopSystem[children.Length + 1];
                    Array.Copy(children, 0, expanded, 0, i + 1);
                    expanded[i + 1] = node;
                    Array.Copy(children, i + 1, expanded, i + 2, children.Length - i - 1);
                    system.subSystemList = expanded;
                    return true;
                }
                if (InsertAfter(ref children[i], anchor, node))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
