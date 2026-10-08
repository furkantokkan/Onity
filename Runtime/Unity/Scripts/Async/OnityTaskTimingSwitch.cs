using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Awaitable returned by <see cref="OnityTask.SwitchToMainThread(OnityPlayerLoopTiming, CancellationToken)"/>:
    /// resumes on Unity's main thread at the next drain of a PlayerLoop timing; the equivalent of UniTask's
    /// <c>SwitchToMainThread(PlayerLoopTiming, CancellationToken)</c>. Awaiting it on the main thread
    /// completes synchronously without a frame delay.
    /// </summary>
    /// <remarks>
    /// A worker's continuation is queued on the timing's node, so the await requires an active
    /// Play/player session; outside one, the await faults with <see cref="InvalidOperationException"/>
    /// (<see cref="OnityTask.SwitchToMainThread(CancellationToken)"/> also serves Edit Mode). Cancellation
    /// is observed when the await completes, on the main thread. A session exit before the drain makes
    /// the await throw <see cref="OperationCanceledException"/>.
    /// </remarks>
    public readonly struct OnityTimingSwitch
    {
        private readonly OnityPlayerLoopTiming m_timing;
        private readonly CancellationToken m_cancellationToken;

        internal OnityTimingSwitch(OnityPlayerLoopTiming timing, CancellationToken cancellationToken)
        {
            m_timing = timing;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>Returns the awaiter for this switch.</summary>
        /// <returns>Switch awaiter.</returns>
        public OnityTimingSwitchAwaiter GetAwaiter()
        {
            return new OnityTimingSwitchAwaiter(m_timing, m_cancellationToken, OnityTaskPlayerLoop.s_epoch);
        }
    }

    /// <summary>Awaiter of <see cref="OnityTimingSwitch"/>.</summary>
    public readonly struct OnityTimingSwitchAwaiter : ICriticalNotifyCompletion
    {
        private readonly OnityPlayerLoopTiming m_timing;
        private readonly CancellationToken m_cancellationToken;
        private readonly int m_epoch;

        internal OnityTimingSwitchAwaiter(OnityPlayerLoopTiming timing, CancellationToken cancellationToken, int epoch)
        {
            m_timing = timing;
            m_cancellationToken = cancellationToken;
            m_epoch = epoch;
        }

        /// <summary>True on Unity's main thread, where the switch completes synchronously.</summary>
        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => OnityTaskPlayerLoop.IsMainThread;
        }

        /// <summary>
        /// Completes the switch. Throws the retirement outcome when the session ended before the drain,
        /// then <see cref="OperationCanceledException"/> when the token is canceled.
        /// </summary>
        public void GetResult()
        {
            if (m_epoch != OnityTaskPlayerLoop.s_epoch)
            {
                OnityTaskPlayerLoop.ThrowRetired();
            }

            m_cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>Queues the continuation for the next drain of the timing.</summary>
        /// <param name="continuation">Continuation callback.</param>
        /// <exception cref="ArgumentNullException"><paramref name="continuation"/> is null.</exception>
        public void OnCompleted(Action continuation)
        {
            OnityTaskPlayerLoop.EnqueueYield(m_timing, continuation);
        }

        /// <summary>Queues the continuation for the next drain of the timing.</summary>
        /// <param name="continuation">Continuation callback.</param>
        /// <exception cref="ArgumentNullException"><paramref name="continuation"/> is null.</exception>
        public void UnsafeOnCompleted(Action continuation)
        {
            OnityTaskPlayerLoop.EnqueueYield(m_timing, continuation);
        }
    }

    /// <summary>
    /// Scope returned by <see cref="OnityTask.ReturnToMainThread(CancellationToken)"/> and
    /// <see cref="OnityTask.ReturnToMainThread(OnityPlayerLoopTiming, CancellationToken)"/>. Used with
    /// <c>await using</c>, it resumes on Unity's main thread when the scope closes, so code that switched
    /// to a worker inside the scope returns to the main thread; the equivalent of UniTask's
    /// <c>ReturnToMainThread</c>.
    /// </summary>
    public readonly struct OnityReturnToMainThread
    {
        private readonly OnityPlayerLoopTiming m_timing;
        private readonly bool m_hasTiming;
        private readonly int m_session;
        private readonly CancellationToken m_cancellationToken;

        /// <param name="session">The switch-queue session for an untimed scope, or the PlayerLoop epoch
        /// for a timed one, captured when the scope opened.</param>
        internal OnityReturnToMainThread(
            OnityPlayerLoopTiming timing, bool hasTiming, int session, CancellationToken cancellationToken)
        {
            m_timing = timing;
            m_hasTiming = hasTiming;
            m_session = session;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Closes the scope. Awaiting the result continues inline on the main thread and otherwise resumes
        /// there: at the next drain of the scope's timing, or through the main-thread switch queue when
        /// the scope has no timing. A scope opened in an earlier session does not resume in a later one.
        /// </summary>
        /// <returns>Awaitable that resumes on the main thread.</returns>
        public OnityReturnToMainThreadAwaiter DisposeAsync()
        {
            return new OnityReturnToMainThreadAwaiter(m_timing, m_hasTiming, m_session, m_cancellationToken);
        }
    }

    /// <summary>Awaiter that resumes on Unity's main thread when an <see cref="OnityReturnToMainThread"/> scope closes.</summary>
    public readonly struct OnityReturnToMainThreadAwaiter : ICriticalNotifyCompletion
    {
        private readonly OnityPlayerLoopTiming m_timing;
        private readonly bool m_hasTiming;
        private readonly int m_session;
        private readonly CancellationToken m_cancellationToken;

        internal OnityReturnToMainThreadAwaiter(
            OnityPlayerLoopTiming timing, bool hasTiming, int session, CancellationToken cancellationToken)
        {
            m_timing = timing;
            m_hasTiming = hasTiming;
            m_session = session;
            m_cancellationToken = cancellationToken;
        }

        /// <summary>True on Unity's main thread, where the scope closes synchronously.</summary>
        public bool IsCompleted => m_hasTiming ? OnityTaskPlayerLoop.IsMainThread : OnityTaskMainThreadDispatcher.IsMainThread;

        /// <summary>Returns this awaiter, so the scope's <c>DisposeAsync()</c> result can be awaited.</summary>
        /// <returns>This awaiter.</returns>
        public OnityReturnToMainThreadAwaiter GetAwaiter()
        {
            return this;
        }

        /// <summary>
        /// Completes the return. Throws the retirement outcome when a timed scope's session ended before
        /// the drain, then <see cref="OperationCanceledException"/> when the token is canceled.
        /// </summary>
        public void GetResult()
        {
            if (m_hasTiming && m_session != OnityTaskPlayerLoop.s_epoch)
            {
                OnityTaskPlayerLoop.ThrowRetired();
            }

            m_cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>Queues the continuation for Unity's main thread.</summary>
        /// <param name="continuation">Continuation callback.</param>
        /// <exception cref="ArgumentNullException"><paramref name="continuation"/> is null.</exception>
        public void OnCompleted(Action continuation)
        {
            Queue(continuation);
        }

        /// <summary>Queues the continuation for Unity's main thread.</summary>
        /// <param name="continuation">Continuation callback.</param>
        /// <exception cref="ArgumentNullException"><paramref name="continuation"/> is null.</exception>
        public void UnsafeOnCompleted(Action continuation)
        {
            Queue(continuation);
        }

        private void Queue(Action continuation)
        {
            if (m_hasTiming)
            {
                OnityTaskPlayerLoop.EnqueueYield(m_timing, continuation);
            }
            else
            {
                OnityTaskMainThreadDispatcher.Enqueue(continuation, m_session);
            }
        }
    }
}
