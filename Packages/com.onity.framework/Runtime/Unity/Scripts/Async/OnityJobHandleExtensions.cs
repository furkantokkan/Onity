using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Jobs;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Observes Unity jobs through native Onity tasks without taking ownership of their containers.
    /// </summary>
    public static class OnityJobHandleExtensions
    {
        /// <summary>
        /// Accepts the obligation to call Complete on a job handle and returns a native Onity task.
        /// Call this method on Unity's main thread while the current session accepts jobs.
        /// </summary>
        /// <remarks>
        /// Completed and default handles call Complete inline. Pending handles are observed during
        /// Update and completed before successful task publication, even if the task is never awaited.
        /// Runner destruction and session teardown complete accepted jobs before publishing cancellation;
        /// Complete failures fault the task. Teardown can block until the jobs finish.
        /// The caller retains ownership of all native memory and must not dispose or modify it while
        /// jobs use it. This adapter does not cancel jobs or dispose containers. A pending task is
        /// single-consumer; call Preserve once before sharing it with multiple consumers.
        /// Rejection transfers no obligation: the caller must still complete the rejected handle.
        /// </remarks>
        /// <param name="handle">Scheduled job or combined dependency to complete.</param>
        /// <returns>Task completed only after Complete has been called on the accepted handle.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called off Unity's main thread, during retirement, or while the session is closed.
        /// </exception>
        public static OnityTask AsOnityTask(this JobHandle handle)
        {
            return OnityJobHandleRegistry.Register(handle);
        }

        /// <summary>
        /// Yields to the next drain of <paramref name="waitTiming"/>, then calls Complete on the handle, which
        /// blocks until the job finishes; the equivalent of UniTask's <c>JobHandle.WaitAsync</c>. The handle
        /// is completed even when the yield fails, for example because the session ended, so the completion
        /// obligation is never dropped. Requires an active Play/player session.
        /// </summary>
        /// <param name="handle">Scheduled job or combined dependency to complete.</param>
        /// <param name="waitTiming">PlayerLoop timing to complete the handle at.</param>
        /// <param name="cancellationToken">Token observed after Complete; it cannot stop the job.</param>
        /// <returns>Task completed after Complete returned.</returns>
        public static async OnityTask WaitAsync(
            this JobHandle handle,
            OnityPlayerLoopTiming waitTiming,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await OnityTask.Yield(waitTiming);
            }
            finally
            {
                handle.Complete();
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    internal static class OnityJobHandleRegistry
    {
        private struct Entry
        {
            public JobHandle Handle;
            public OnityJobHandleTaskSource Source;
        }

        private static List<Entry> s_pending = new List<Entry>(64);
        private static List<Entry> s_spare = new List<Entry>(64);
        private static int s_mainThreadId;
        private static int s_epoch;
        private static int s_sessionEpoch;
        private static bool s_initialized;
        private static bool s_runtimeSession;
        private static bool s_accepting;
        private static bool s_retiring;
        private static bool s_pumping;
        private static bool s_shuttingDown;
        private static bool s_newBatches;

        internal static OnityTask Register(JobHandle handle)
        {
            ValidateRegistration();
            bool completed;
            try
            {
                completed = handle.IsCompleted;
            }
            catch (Exception exception)
            {
                // A failed status query must not drop the accepted completion obligation.
                Exception failure = CompleteHandle(ref handle) ?? exception;
                return OnityTask.FromException(failure);
            }

            if (completed)
            {
                Exception failure = CompleteHandle(ref handle);
                return failure == null ? OnityTask.Completed : OnityTask.FromException(failure);
            }

            // Runner creation precedes acceptance. Recheck in case Unity callbacks closed the session.
            OnityTaskRunner.EnsureCreated();
            ValidateRegistration();
            OnityJobHandleTaskSource source = OnityJobHandleTaskSource.Rent();
            OnityTask task = new OnityTask(source);
            s_pending.Add(new Entry { Handle = handle, Source = source });
            s_newBatches = true;
            return task;
        }

        internal static void Update()
        {
            // Edit Mode has its own Editor update pump; ExecuteAlways must not poll a second time.
            if (Application.isPlaying)
            {
                Pump();
            }
        }

        internal static void RetireRunner()
        {
            bool reopen = s_accepting;
            int sessionEpoch = s_sessionEpoch;
            s_accepting = false;
            Retire();
            if (reopen && sessionEpoch == s_sessionEpoch && !s_shuttingDown && !s_retiring)
            {
                s_accepting = true;
            }
        }

        private static void ValidateRegistration()
        {
            if (Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
            {
                throw new InvalidOperationException("JobHandle.AsOnityTask must be called on Unity's main thread.");
            }

            if (!s_accepting || s_retiring || s_shuttingDown)
            {
                throw new InvalidOperationException("The Onity job registry is not accepting handles in this session.");
            }
        }

        private static void Pump()
        {
            if (!s_accepting || s_retiring || s_pumping)
            {
                return;
            }

            s_pumping = true;
            int epoch = s_epoch;
            try
            {
                if (s_newBatches)
                {
                    s_newBatches = false;
                    JobHandle.ScheduleBatchedJobs();
                }

                // New registrations from continuations wait for the next pump.
                for (int i = s_pending.Count - 1; i >= 0 && epoch == s_epoch; i--)
                {
                    Entry entry = s_pending[i];
                    bool completed;
                    Exception statusFailure = null;
                    try
                    {
                        completed = entry.Handle.IsCompleted;
                    }
                    catch (Exception exception)
                    {
                        completed = true;
                        statusFailure = exception;
                    }

                    if (!completed)
                    {
                        continue;
                    }

                    int last = s_pending.Count - 1;
                    s_pending[i] = s_pending[last];
                    s_pending.RemoveAt(last);
                    Settle(entry, false, statusFailure);
                }
            }
            finally
            {
                s_pumping = false;
            }
        }

        private static void Retire()
        {
            if (s_retiring)
            {
                return;
            }

            s_retiring = true;
            s_epoch++;
            s_newBatches = false;
            List<Entry> entries = s_pending;
            s_pending = s_spare;
            s_spare = null;
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    entries[i] = default;
                    Settle(entry, true, null);
                }
            }
            finally
            {
                entries.Clear();
                s_spare = entries;
                s_retiring = false;
            }
        }

        private static void Settle(Entry entry, bool canceled, Exception statusFailure)
        {
            OnityJobHandleTaskSource source = entry.Source;
            Exception failure = CompleteHandle(ref entry.Handle) ?? statusFailure;
            entry = default;
            try
            {
                // Publication can consume and re-rent source synchronously. Never touch it afterwards.
                source.Publish(canceled, failure);
            }
            catch (Exception exception)
            {
                // Even a throwing continuation/logger must not abandon other accepted handles.
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                }
            }
        }

        private static Exception CompleteHandle(ref JobHandle handle)
        {
            try
            {
                handle.Complete();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
            finally
            {
                handle = default;
            }
        }

        private static void CloseSession()
        {
            s_accepting = false;
            s_sessionEpoch++;
            Retire();
        }

        private static void CloseApplication()
        {
            s_shuttingDown = true;
            CloseSession();
        }

        private static void BeginSession(bool runtimeSession, bool force)
        {
            if (s_shuttingDown || s_retiring)
            {
                return;
            }

            if (!force && s_initialized && s_accepting && s_runtimeSession == runtimeSession)
            {
                // Subsystem registration opened Play before Awake. EnteredPlayMode is redundant.
                return;
            }

            s_accepting = false;
            int sessionEpoch = ++s_sessionEpoch;
            Retire();
            if (sessionEpoch == s_sessionEpoch && !s_shuttingDown)
            {
                s_runtimeSession = runtimeSession;
                s_initialized = true;
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
            BeginSession(true, true);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            UnityEditor.EditorApplication.update -= UpdateFromEditor;
            UnityEditor.EditorApplication.update += UpdateFromEditor;
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            UnityEditor.EditorApplication.quitting -= CloseApplication;
            UnityEditor.EditorApplication.quitting += CloseApplication;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= CloseApplication;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += CloseApplication;
            BeginSession(Application.isPlaying, false);
        }

        private static void UpdateFromEditor()
        {
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
                && !UnityEditor.EditorApplication.isCompiling && !UnityEditor.EditorApplication.isUpdating)
            {
                Pump();
            }
        }

        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode
                || state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                CloseSession();
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                BeginSession(false, false);
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            {
                BeginSession(true, false);
            }
        }
#endif
    }

    internal sealed class OnityJobHandleTaskSource :
        OnityTaskSourceBase, IOnityPooledRunner<OnityJobHandleTaskSource>
    {
        private static OnityRunnerPool<OnityJobHandleTaskSource> s_pool;

        private OnityJobHandleTaskSource m_nextPooled;

        ref OnityJobHandleTaskSource IOnityPooledRunner<OnityJobHandleTaskSource>.NextPooled => ref m_nextPooled;

        internal static OnityJobHandleTaskSource Rent()
        {
            // A contended rent allocates instead of waiting.
            if (!s_pool.TryPop(out OnityJobHandleTaskSource source))
            {
                source = new OnityJobHandleTaskSource();
            }

            source.Reset(default);
            return source;
        }

        internal void Publish(bool canceled, Exception failure)
        {
            if (failure != null)
            {
                TrySetException(failure);
            }
            else if (canceled)
            {
                TrySetCanceled();
            }
            else
            {
                TrySetResult();
            }
        }

        protected override void ReleaseSource()
        {
            // The compare-and-swap that claimed the release already retired the token version.
            // A contended or full return lets the source be collected.
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }
}
