using System;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// A one-shot or periodic timer that runs a callback on Unity's main thread at a PlayerLoop timing;
    /// the equivalent of UniTask's <c>PlayerLoopTimer</c>. It backs the timed
    /// <see cref="OnityCancellationTokenSourceExtensions.CancelAfterSlim(CancellationTokenSource, TimeSpan, OnityDelayType, OnityPlayerLoopTiming)"/>
    /// and the <see cref="OnityTimeoutController"/> clocks.
    /// </summary>
    /// <remarks>
    /// The timer runs while an active Play/player session drains its timing; <see cref="Restart()"/>
    /// requires one and may be called from any thread. A session exit stops a running timer; a later
    /// <see cref="Restart()"/> starts it again. A callback exception is logged and does not stop a
    /// periodic timer. Call <see cref="Restart()"/>, <see cref="Stop"/> and <see cref="Dispose"/> from one
    /// thread at a time.
    /// </remarks>
    public sealed class OnityPlayerLoopTimer : IDisposable, IOnityRetirablePlayerLoopItem
    {
        private readonly bool m_periodic;
        private readonly OnityDelayType m_delayType;
        private readonly OnityPlayerLoopTiming m_timing;
        private readonly CancellationToken m_cancellationToken;
        private readonly Action<object> m_callback;
        private readonly object m_state;

        private TimeSpan m_interval;
        private OnityLoopClock m_clock;
        private volatile bool m_running;
        private volatile bool m_tryStop;
        private volatile bool m_disposed;
        private int m_restarts;

        private OnityPlayerLoopTimer(
            TimeSpan interval, bool periodic, OnityDelayType delayType, OnityPlayerLoopTiming timing,
            CancellationToken cancellationToken, Action<object> callback, object state)
        {
            m_interval = interval;
            m_periodic = periodic;
            m_delayType = delayType;
            m_timing = timing;
            m_cancellationToken = cancellationToken;
            m_callback = callback;
            m_state = state;
        }

        /// <summary>True while the timer is queued on its timing.</summary>
        public bool IsRunning => m_running;

        /// <summary>Creates a stopped timer; call <see cref="Restart()"/> to start it.</summary>
        /// <param name="interval">Nonnegative interval.</param>
        /// <param name="periodic">True to run the callback at every interval, false to run it once.</param>
        /// <param name="delayType">Clock that measures the interval.</param>
        /// <param name="playerLoopTiming">PlayerLoop timing that measures the interval and runs the callback.</param>
        /// <param name="cancellationToken">Token that stops the timer at its next drain.</param>
        /// <param name="timerCallback">Callback, run on Unity's main thread.</param>
        /// <param name="state">State passed to the callback.</param>
        /// <returns>The timer.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="timerCallback"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The interval is negative, or the clock or the
        /// timing is not defined.</exception>
        public static OnityPlayerLoopTimer Create(
            TimeSpan interval,
            bool periodic,
            OnityDelayType delayType,
            OnityPlayerLoopTiming playerLoopTiming,
            CancellationToken cancellationToken,
            Action<object> timerCallback,
            object state)
        {
            if (timerCallback == null)
            {
                throw new ArgumentNullException(nameof(timerCallback));
            }

            if (interval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            if ((uint)delayType > (uint)OnityDelayType.Realtime)
            {
                throw new ArgumentOutOfRangeException(nameof(delayType));
            }

            if ((uint)playerLoopTiming > (uint)OnityPlayerLoopTiming.LastTimeUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(playerLoopTiming));
            }

            return new OnityPlayerLoopTimer(
                interval, periodic, delayType, playerLoopTiming, cancellationToken, timerCallback, state);
        }

        /// <summary>Creates a timer and starts it; see <see cref="Create"/>.</summary>
        /// <param name="interval">Nonnegative interval.</param>
        /// <param name="periodic">True to run the callback at every interval, false to run it once.</param>
        /// <param name="delayType">Clock that measures the interval.</param>
        /// <param name="playerLoopTiming">PlayerLoop timing that measures the interval and runs the callback.</param>
        /// <param name="cancellationToken">Token that stops the timer at its next drain.</param>
        /// <param name="timerCallback">Callback, run on Unity's main thread.</param>
        /// <param name="state">State passed to the callback.</param>
        /// <returns>The running timer.</returns>
        public static OnityPlayerLoopTimer StartNew(
            TimeSpan interval,
            bool periodic,
            OnityDelayType delayType,
            OnityPlayerLoopTiming playerLoopTiming,
            CancellationToken cancellationToken,
            Action<object> timerCallback,
            object state)
        {
            OnityPlayerLoopTimer timer = Create(
                interval, periodic, delayType, playerLoopTiming, cancellationToken, timerCallback, state);
            timer.Restart();
            return timer;
        }

        /// <summary>Starts the timer, or restarts its interval when it runs.</summary>
        /// <exception cref="ObjectDisposedException">The timer is disposed.</exception>
        /// <exception cref="InvalidOperationException">No Play/player session is accepting work.</exception>
        public void Restart()
        {
            RestartCore(null);
        }

        /// <summary>Starts the timer with a new interval, or restarts it with that interval when it runs.</summary>
        /// <param name="interval">Nonnegative interval.</param>
        /// <exception cref="ObjectDisposedException">The timer is disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">No Play/player session is accepting work.</exception>
        public void Restart(TimeSpan interval)
        {
            if (interval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            RestartCore(interval);
        }

        /// <summary>Stops the timer at its next drain; <see cref="Restart()"/> starts it again.</summary>
        public void Stop()
        {
            m_tryStop = true;
        }

        /// <summary>Stops the timer at its next drain for good.</summary>
        public void Dispose()
        {
            m_disposed = true;
        }

        private void RestartCore(TimeSpan? interval)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(OnityPlayerLoopTimer));
            }

            if (interval.HasValue)
            {
                m_interval = interval.Value;
            }

            m_clock.Start(m_interval, m_delayType);
            Interlocked.Increment(ref m_restarts);
            m_tryStop = false;
            if (!m_running)
            {
                OnityTaskPlayerLoop.AddAction(m_timing, this);
                m_running = true;
            }
        }

        bool IOnityPlayerLoopItem.MoveNext()
        {
            if (m_disposed || m_tryStop || m_cancellationToken.IsCancellationRequested)
            {
                m_running = false;
                return false;
            }

            if (!m_clock.Advance())
            {
                return true;
            }

            int restarts = Volatile.Read(ref m_restarts);
            try
            {
                m_callback(m_state);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (m_periodic)
            {
                m_clock.Start(m_interval, m_delayType);
                return true;
            }

            if (Volatile.Read(ref m_restarts) != restarts && !m_disposed && !m_tryStop)
            {
                // The callback restarted this one-shot timer; it stays queued with its new interval.
                return true;
            }

            m_running = false;
            return false;
        }

        void IOnityRetirablePlayerLoopItem.Retire(Exception failure)
        {
            m_running = false;
        }
    }
}
