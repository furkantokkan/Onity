using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Reactive;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Reusable timeout controller similar to UniTask timeout patterns.
    /// </summary>
    public sealed class OnityTimeoutController : IDisposable
    {
        private static readonly Action<object> s_timerElapsed = state => ((OnityTimeoutController)state).OnTimerElapsed();

        private readonly object m_gate = new object();
        private readonly CancellationToken m_externalCancellationToken;
        private readonly OnityTimeProvider m_timeProvider;
        private readonly bool m_usePlayerLoop;
        private readonly OnityDelayType m_delayType;
        private readonly OnityPlayerLoopTiming m_delayTiming;

        private CancellationTokenSource m_timeoutCancellationTokenSource;
        private OnityPlayerLoopTimer m_timer;
        private int m_timeoutVersion;
        private bool m_isDisposed;
        private bool m_isTimeout;

        /// <summary>
        /// Initializes a timeout controller.
        /// </summary>
        /// <param name="externalCancellationToken">Optional external cancellation token.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        public OnityTimeoutController(
            CancellationToken externalCancellationToken = default,
            OnityTimeProvider timeProvider = null)
        {
            m_externalCancellationToken = externalCancellationToken;
            m_timeProvider = timeProvider ?? OnityTimeProvider.System;
        }

        /// <summary>
        /// Initializes a timeout controller whose timeouts are measured on a PlayerLoop timing; the
        /// equivalent of UniTask's <c>TimeoutController(DelayType, PlayerLoopTiming)</c>. Its
        /// <see cref="Timeout(TimeSpan)"/> requires an active Play/player session.
        /// </summary>
        /// <param name="delayType">Clock that measures each timeout.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures each timeout.</param>
        /// <exception cref="ArgumentOutOfRangeException">The clock or the timing is not defined.</exception>
        public OnityTimeoutController(
            OnityDelayType delayType,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update)
            : this(default(CancellationToken), delayType, delayTiming)
        {
        }

        /// <summary>
        /// Initializes a PlayerLoop timeout controller whose tokens are also canceled by
        /// <paramref name="linkCancellationTokenSource"/>; the equivalent of UniTask's
        /// <c>TimeoutController(CancellationTokenSource, DelayType, PlayerLoopTiming)</c>.
        /// </summary>
        /// <param name="linkCancellationTokenSource">Source linked into every timeout token.</param>
        /// <param name="delayType">Clock that measures each timeout.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures each timeout.</param>
        /// <exception cref="ArgumentNullException"><paramref name="linkCancellationTokenSource"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The clock or the timing is not defined.</exception>
        public OnityTimeoutController(
            CancellationTokenSource linkCancellationTokenSource,
            OnityDelayType delayType = OnityDelayType.DeltaTime,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update)
            : this(
                (linkCancellationTokenSource ?? throw new ArgumentNullException(nameof(linkCancellationTokenSource))).Token,
                delayType,
                delayTiming)
        {
        }

        private OnityTimeoutController(
            CancellationToken externalCancellationToken, OnityDelayType delayType, OnityPlayerLoopTiming delayTiming)
        {
            if ((uint)delayType > (uint)OnityDelayType.Realtime)
            {
                throw new ArgumentOutOfRangeException(nameof(delayType));
            }

            if ((uint)delayTiming > (uint)OnityPlayerLoopTiming.LastTimeUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(delayTiming));
            }

            m_externalCancellationToken = externalCancellationToken;
            m_usePlayerLoop = true;
            m_delayType = delayType;
            m_delayTiming = delayTiming;
        }

        /// <summary>
        /// Starts or restarts a timeout of a number of milliseconds; see <see cref="Timeout(TimeSpan)"/>.
        /// </summary>
        /// <param name="millisecondsTimeout">Nonnegative timeout in milliseconds.</param>
        /// <returns>Linked cancellation token.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is negative.</exception>
        public CancellationToken Timeout(int millisecondsTimeout)
        {
            if (millisecondsTimeout < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));
            }

            return Timeout(TimeSpan.FromMilliseconds(millisecondsTimeout));
        }

        /// <summary>
        /// Returns whether last timeout request ended by timeout.
        /// </summary>
        /// <returns>True when timeout expired.</returns>
        public bool IsTimeout()
        {
            lock (m_gate)
            {
                return m_isTimeout;
            }
        }

        /// <summary>
        /// Starts/restarts timeout and returns cancellation token to pass into async calls.
        /// </summary>
        /// <param name="timeout">Timeout duration.</param>
        /// <returns>Linked cancellation token.</returns>
        public CancellationToken Timeout(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            CancellationTokenSource timeoutCancellationTokenSource;
            int timeoutVersion;

            lock (m_gate)
            {
                ThrowIfDisposed_NoLock();
                m_isTimeout = false;
                m_timeoutVersion++;
                DisposeCurrentTimeout_NoLock();

                timeoutCancellationTokenSource = m_externalCancellationToken.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(m_externalCancellationToken)
                    : new CancellationTokenSource();

                m_timeoutCancellationTokenSource = timeoutCancellationTokenSource;
                timeoutVersion = m_timeoutVersion;
            }

            if (timeout == TimeSpan.Zero)
            {
                MarkTimedOut(timeoutVersion, timeoutCancellationTokenSource);
                return timeoutCancellationTokenSource.Token;
            }

            if (m_usePlayerLoop)
            {
                StartTimer(timeout);
                return timeoutCancellationTokenSource.Token;
            }

            _ = RunTimeoutAsync(timeoutVersion, timeoutCancellationTokenSource, timeout);
            return timeoutCancellationTokenSource.Token;
        }

        private void StartTimer(TimeSpan timeout)
        {
            lock (m_gate)
            {
                ThrowIfDisposed_NoLock();
                if (m_timer == null)
                {
                    m_timer = OnityPlayerLoopTimer.Create(
                        timeout, false, m_delayType, m_delayTiming, m_externalCancellationToken, s_timerElapsed, this);
                }

                m_timer.Restart(timeout);
            }
        }

        private void OnTimerElapsed()
        {
            CancellationTokenSource timeoutCancellationTokenSource;
            int timeoutVersion;
            lock (m_gate)
            {
                timeoutCancellationTokenSource = m_timeoutCancellationTokenSource;
                timeoutVersion = m_timeoutVersion;
            }

            if (timeoutCancellationTokenSource != null)
            {
                MarkTimedOut(timeoutVersion, timeoutCancellationTokenSource);
            }
        }

        /// <summary>
        /// Stops active timeout and clears timeout state.
        /// </summary>
        public void Reset()
        {
            lock (m_gate)
            {
                ThrowIfDisposed_NoLock();
                m_isTimeout = false;
                m_timeoutVersion++;
                DisposeCurrentTimeout_NoLock();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_isDisposed)
                {
                    return;
                }

                m_isDisposed = true;
                m_isTimeout = false;
                m_timeoutVersion++;
                DisposeCurrentTimeout_NoLock();
            }
        }

        private async Task RunTimeoutAsync(
            int timeoutVersion,
            CancellationTokenSource timeoutCancellationTokenSource,
            TimeSpan timeout)
        {
            try
            {
                await m_timeProvider.DelayAsync(timeout, timeoutCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            MarkTimedOut(timeoutVersion, timeoutCancellationTokenSource);
        }

        private void MarkTimedOut(int timeoutVersion, CancellationTokenSource timeoutCancellationTokenSource)
        {
            lock (m_gate)
            {
                if (m_isDisposed)
                {
                    return;
                }

                if (m_timeoutVersion != timeoutVersion)
                {
                    return;
                }

                if (ReferenceEquals(m_timeoutCancellationTokenSource, timeoutCancellationTokenSource) == false)
                {
                    return;
                }

                m_isTimeout = true;
            }

            if (timeoutCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            timeoutCancellationTokenSource.Cancel();
        }

        private void DisposeCurrentTimeout_NoLock()
        {
            if (m_isDisposed)
            {
                m_timer?.Dispose();
            }
            else
            {
                m_timer?.Stop();
            }

            CancellationTokenSource timeoutCancellationTokenSource = m_timeoutCancellationTokenSource;
            m_timeoutCancellationTokenSource = null;

            if (timeoutCancellationTokenSource == null)
            {
                return;
            }

            timeoutCancellationTokenSource.Cancel();
            timeoutCancellationTokenSource.Dispose();
        }

        private void ThrowIfDisposed_NoLock()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OnityTimeoutController));
            }
        }
    }
}

