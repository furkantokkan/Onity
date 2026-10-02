using System;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Awaitable returned by <see cref="OnityTask.SwitchToMainThread(CancellationToken)"/>.
    /// Awaiting it on Unity's main thread completes synchronously without a frame delay, on a path
    /// designed not to allocate. Awaiting it on another thread queues the continuation for Unity's
    /// main thread, where it resumes during the Update phase of a following player-loop frame.
    /// </summary>
    /// <remarks>
    /// The value can be awaited any number of times. Cancellation is observed when the await
    /// completes: <see cref="OnityTaskThreadSwitchAwaiter.GetResult"/> throws
    /// <see cref="OperationCanceledException"/> on the destination thread when the token is
    /// canceled, including a token that was already canceled when the switch was requested.
    /// Outside Play Mode the Editor resumes queued continuations from <c>EditorApplication.update</c>
    /// while it is not compiling or importing assets. A continuation queued during an earlier
    /// session, such as a previous Play Mode session or a player that started quitting, is
    /// discarded instead of resuming in the next session.
    /// </remarks>
    public readonly struct OnityTaskThreadSwitch
    {
        private readonly CancellationToken m_cancellationToken;
        private readonly int m_session;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityTaskThreadSwitch(CancellationToken cancellationToken, int session)
        {
            m_cancellationToken = cancellationToken;
            m_session = session;
        }

        /// <summary>
        /// Returns the awaiter for this switch.
        /// </summary>
        /// <returns>Switch awaiter.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OnityTaskThreadSwitchAwaiter GetAwaiter()
        {
            return new OnityTaskThreadSwitchAwaiter(m_cancellationToken, m_session);
        }
    }

    /// <summary>
    /// Awaiter for <see cref="OnityTaskThreadSwitch"/>.
    /// </summary>
    public readonly struct OnityTaskThreadSwitchAwaiter : ICriticalNotifyCompletion
    {
        private readonly CancellationToken m_cancellationToken;
        private readonly int m_session;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityTaskThreadSwitchAwaiter(CancellationToken cancellationToken, int session)
        {
            m_cancellationToken = cancellationToken;
            m_session = session;
        }

        /// <summary>
        /// True when the current thread is already Unity's main thread, so the await completes
        /// synchronously without queuing.
        /// </summary>
        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => OnityTaskMainThreadDispatcher.IsMainThread;
        }

        /// <summary>
        /// Completes the switch. Throws <see cref="OperationCanceledException"/> with the original
        /// token when cancellation was requested.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetResult()
        {
            m_cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Queues the continuation for Unity's main thread. The execution context is not captured
        /// here; compiler-generated async methods flow it through their own builders.
        /// </summary>
        /// <param name="continuation">Continuation callback.</param>
        public void OnCompleted(Action continuation)
        {
            OnityTaskMainThreadDispatcher.Enqueue(continuation, m_session);
        }

        /// <summary>
        /// Queues the continuation for Unity's main thread without flowing execution context.
        /// </summary>
        /// <param name="continuation">Continuation callback.</param>
        public void UnsafeOnCompleted(Action continuation)
        {
            OnityTaskMainThreadDispatcher.Enqueue(continuation, m_session);
        }
    }

    /// <summary>
    /// Owns the main-thread continuation queue behind <see cref="OnityTaskThreadSwitch"/>.
    /// Any thread can enqueue. Only Unity's main thread drains: <see cref="OnityTaskRunner"/> drains
    /// at the end of its Update, after the frame sources tick, in Play Mode and players, and the
    /// Editor update loop drains outside Play Mode.
    /// Each Play Mode session and the player lifetime form a session; continuations stamped with an
    /// ended session are discarded rather than resumed in a later one.
    /// </summary>
    internal static class OnityTaskMainThreadDispatcher
    {
        private readonly struct QueuedContinuation
        {
            public readonly Action Continuation;
            public readonly int Session;

            public QueuedContinuation(Action continuation, int session)
            {
                Continuation = continuation;
                Session = session;
            }
        }

        private const int k_initialQueueCapacity = 64;

        private static readonly object s_gate = new object();
        private static readonly SendOrPostCallback s_ensureRunnerCallback = EnsureRunnerPosted;

        // Double-buffered arrays: workers append to the pending buffer under the gate, and a drain
        // swaps the buffers so it can run the batch without holding the gate.
        private static QueuedContinuation[] s_pending = new QueuedContinuation[k_initialQueueCapacity];
        private static QueuedContinuation[] s_draining = new QueuedContinuation[k_initialQueueCapacity];
        private static int s_pendingCount;
        private static SynchronizationContext s_mainThreadContext;
        private static int s_mainThreadId;
        private static int s_session = 1;
        private static int s_isPlaying;
        private static int s_isShutDown;
        private static int s_runnerMissing;
        private static int s_runnerRequestPending;
        private static int s_initialized;
        private static int s_drainState;

        /// <summary>
        /// Session number stamped on new switch awaitables.
        /// </summary>
        public static int Session => Volatile.Read(ref s_session);

        /// <summary>
        /// True on Unity's main thread once a Unity load hook captured it.
        /// </summary>
        public static bool IsMainThread
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Thread.CurrentThread.ManagedThreadId == s_mainThreadId;
        }

        /// <summary>
        /// Number of continuations waiting for the next drain. Diagnostics only.
        /// </summary>
        public static int PendingCount
        {
            get
            {
                lock (s_gate)
                {
                    return s_pendingCount;
                }
            }
        }

        /// <summary>
        /// Queues a continuation for the main thread. Safe to call from any thread; it never runs
        /// the continuation inline and never touches Unity objects.
        /// </summary>
        /// <param name="continuation">Continuation to run on the main thread.</param>
        /// <param name="session">Session captured when the switch was requested.</param>
        public static void Enqueue(Action continuation, int session)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            lock (s_gate)
            {
                if (s_isShutDown != 0)
                {
                    return;
                }

                int count = s_pendingCount;
                if (count == s_pending.Length)
                {
                    QueuedContinuation[] grown = new QueuedContinuation[count * 2];
                    Array.Copy(s_pending, grown, count);
                    s_pending = grown;
                }

                s_pending[count] = new QueuedContinuation(continuation, session);
                s_pendingCount = count + 1;
            }

            if (Volatile.Read(ref s_runnerMissing) != 0 && Volatile.Read(ref s_isPlaying) != 0)
            {
                RequestRunner();
            }
        }

        /// <summary>
        /// Runs the continuations that were queued before this call. Continuations queued while
        /// draining wait for the next drain. Main thread only.
        /// </summary>
        public static void Drain()
        {
            if (Interlocked.CompareExchange(ref s_drainState, 1, 0) != 0)
            {
                return;
            }

            try
            {
                QueuedContinuation[] batch;
                int count;
                lock (s_gate)
                {
                    count = s_pendingCount;
                    if (count == 0)
                    {
                        return;
                    }

                    batch = s_pending;
                    s_pending = s_draining;
                    s_pendingCount = 0;
                    s_draining = batch;
                }

                // Sessions change only from Unity load hooks, Play Mode transitions and quitting,
                // never synchronously inside a drained continuation, so one read covers the batch.
                int session = Volatile.Read(ref s_session);
                for (int i = 0; i < count; i++)
                {
                    ref readonly QueuedContinuation queued = ref batch[i];

                    // Session zero comes from a default awaitable value or an IL2CPP runner pool
                    // return; it always runs.
                    if (queued.Session != session && queued.Session != 0)
                    {
                        continue;
                    }

                    try
                    {
                        queued.Continuation();
                    }
                    catch (Exception exception)
                    {
                        ReportContinuationFailure(exception);
                    }
                }

                Array.Clear(batch, 0, count);
            }
            finally
            {
                Volatile.Write(ref s_drainState, 0);
            }
        }

        private static void ReportContinuationFailure(Exception exception)
        {
            try
            {
                Debug.LogException(exception);
            }
            catch (Exception)
            {
                // A failing exception message must not drop the rest of the batch.
            }
        }

        /// <summary>
        /// Records that the player-loop runner exists, so worker enqueues stop requesting one.
        /// </summary>
        public static void NotifyRunnerCreated()
        {
            Volatile.Write(ref s_runnerMissing, 0);
        }

        /// <summary>
        /// Records that the player-loop runner was destroyed. The next worker enqueue during a
        /// session requests a replacement through the captured synchronization context.
        /// </summary>
        public static void NotifyRunnerDestroyed()
        {
            Volatile.Write(ref s_runnerMissing, 1);
            RequestRunnerForPendingContinuations();
        }

        private static void RequestRunnerForPendingContinuations()
        {
            if (Volatile.Read(ref s_isPlaying) != 0
                && Volatile.Read(ref s_isShutDown) == 0
                && PendingCount > 0)
            {
                RequestRunner();
            }
        }

        private static void RequestRunner()
        {
            SynchronizationContext context = Volatile.Read(ref s_mainThreadContext);
            if (context == null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref s_runnerRequestPending, 1, 0) != 0)
            {
                return;
            }

            try
            {
                context.Post(s_ensureRunnerCallback, null);
            }
            catch (Exception)
            {
                // The context can be unavailable while the domain shuts down.
                Volatile.Write(ref s_runnerRequestPending, 0);
            }
        }

        private static void EnsureRunnerPosted(object state)
        {
            Volatile.Write(ref s_runnerRequestPending, 0);
            EnsureRunner();
        }

        private static void EnsureRunner()
        {
            if (Volatile.Read(ref s_isPlaying) == 0 || Volatile.Read(ref s_isShutDown) != 0)
            {
                return;
            }

            OnityTaskRunner.EnsureCreated();
        }

        private static void CaptureMainThread()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            SynchronizationContext context = SynchronizationContext.Current;
            if (context != null)
            {
                Volatile.Write(ref s_mainThreadContext, context);
            }
        }

        private static void BeginSession()
        {
            lock (s_gate)
            {
                s_session++;
                ClearPendingUnsafe();
                s_isShutDown = 0;
            }
        }

        private static void EndSession(bool shutDown)
        {
            Volatile.Write(ref s_isPlaying, 0);
            lock (s_gate)
            {
                s_session++;
                ClearPendingUnsafe();
                if (shutDown)
                {
                    s_isShutDown = 1;
                }
            }
        }

        private static void ClearPendingUnsafe()
        {
            Array.Clear(s_pending, 0, s_pendingCount);
            s_pendingCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRuntime()
        {
            CaptureMainThread();
            if (Interlocked.Exchange(ref s_initialized, 1) != 0)
            {
                // A session already ran in this domain: Edit Mode, or an earlier Play Mode session
                // with domain reload disabled. Its queued continuations are stale. A fresh player
                // domain keeps session 1 so awaits requested before this hook still resume.
                BeginSession();
            }

            Volatile.Write(ref s_isPlaying, 1);
            Volatile.Write(ref s_runnerMissing, OnityTaskRunner.IsAlive ? 0 : 1);
#if !UNITY_EDITOR
            Application.quitting -= HandleApplicationQuitting;
            Application.quitting += HandleApplicationQuitting;
#endif
            if (Volatile.Read(ref s_runnerMissing) != 0)
            {
                RequestRunnerForPendingContinuations();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeSceneLoad()
        {
            CaptureMainThread();
            if (Volatile.Read(ref s_mainThreadContext) == null)
            {
                // Without a synchronization context, worker threads cannot request the runner later.
                EnsureRunner();
            }
        }

#if !UNITY_EDITOR
        private static void HandleApplicationQuitting()
        {
            EndSession(true);
        }
#endif

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            CaptureMainThread();
            Volatile.Write(ref s_initialized, 1);
            UnityEditor.EditorApplication.update -= DrainFromEditor;
            UnityEditor.EditorApplication.update += DrainFromEditor;
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void DrainFromEditor()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
                || UnityEditor.EditorApplication.isCompiling
                || UnityEditor.EditorApplication.isUpdating)
            {
                return;
            }

            Drain();
        }

        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                EndSession(false);
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                // Awaitables created during the Play Mode teardown window are stale as well.
                BeginSession();
            }
        }
#endif
    }
}
