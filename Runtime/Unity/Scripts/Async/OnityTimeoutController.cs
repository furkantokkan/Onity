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
        private readonly object m_gate = new object();
        private readonly CancellationToken m_externalCancellationToken;
        private readonly OnityTimeProvider m_timeProvider;

        private CancellationTokenSource m_timeoutCancellationTokenSource;
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

            _ = RunTimeoutAsync(timeoutVersion, timeoutCancellationTokenSource, timeout);
            return timeoutCancellationTokenSource.Token;
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

