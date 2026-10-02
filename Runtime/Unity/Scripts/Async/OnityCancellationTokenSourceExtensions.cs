using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;
using Onity.Reactive;

namespace Onity.Unity.Async
{
    /// <summary>
    /// CancellationTokenSource helper extensions for Unity-friendly timeout scheduling.
    /// </summary>
    public static class OnityCancellationTokenSourceExtensions
    {
        /// <summary>
        /// Cancels the token source after timeout using <see cref="OnityTimeProvider" />.
        /// </summary>
        /// <param name="cancellationTokenSource">Token source to cancel.</param>
        /// <param name="timeout">Timeout duration.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <returns>Disposable timer handle that can stop timeout scheduling.</returns>
        public static IDisposable CancelAfterSlim(
            this CancellationTokenSource cancellationTokenSource,
            TimeSpan timeout,
            OnityTimeProvider timeProvider = null)
        {
            if (cancellationTokenSource == null)
            {
                throw new ArgumentNullException(nameof(cancellationTokenSource));
            }

            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            if (cancellationTokenSource.IsCancellationRequested)
            {
                return DisposableAction.Empty;
            }

            if (timeout == TimeSpan.Zero)
            {
                cancellationTokenSource.Cancel();
                return DisposableAction.Empty;
            }

            OnityTimeProvider resolvedTimeProvider = timeProvider ?? OnityTimeProvider.System;
            CancellationTokenSource timerCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);

            _ = CancelAfterDelayAsync(
                cancellationTokenSource,
                timerCancellationTokenSource,
                resolvedTimeProvider,
                timeout);

            return new DisposableAction(
                () =>
                {
                    timerCancellationTokenSource.Cancel();
                    timerCancellationTokenSource.Dispose();
                });
        }

        /// <summary>
        /// Cancels the token source after a delay measured on a PlayerLoop timing; the equivalent of
        /// UniTask's <c>CancelAfterSlim(TimeSpan, DelayType, PlayerLoopTiming)</c>. Requires an active
        /// Play/player session; a session exit stops the timer without canceling.
        /// </summary>
        /// <param name="cancellationTokenSource">Token source to cancel.</param>
        /// <param name="delay">Nonnegative delay; zero cancels now.</param>
        /// <param name="delayType">Clock that measures the delay.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures the delay and cancels.</param>
        /// <returns>Timer handle; disposing it stops the pending cancellation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cancellationTokenSource"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The delay is negative, or the clock or the timing is
        /// not defined.</exception>
        public static IDisposable CancelAfterSlim(
            this CancellationTokenSource cancellationTokenSource,
            TimeSpan delay,
            OnityDelayType delayType,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update)
        {
            if (cancellationTokenSource == null)
            {
                throw new ArgumentNullException(nameof(cancellationTokenSource));
            }

            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            if ((uint)delayType > (uint)OnityDelayType.Realtime)
            {
                throw new ArgumentOutOfRangeException(nameof(delayType));
            }

            if ((uint)delayTiming > (uint)OnityPlayerLoopTiming.LastTimeUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(delayTiming));
            }

            if (cancellationTokenSource.IsCancellationRequested)
            {
                return DisposableAction.Empty;
            }

            if (delay == TimeSpan.Zero)
            {
                cancellationTokenSource.Cancel();
                return DisposableAction.Empty;
            }

            return OnityPlayerLoopTimer.StartNew(
                delay, false, delayType, delayTiming, cancellationTokenSource.Token, s_cancelSource,
                cancellationTokenSource);
        }

        /// <summary>
        /// Cancels the token source after a number of milliseconds measured on a PlayerLoop timing; the
        /// equivalent of UniTask's <c>CancelAfterSlim(int, DelayType, PlayerLoopTiming)</c>.
        /// </summary>
        /// <param name="cancellationTokenSource">Token source to cancel.</param>
        /// <param name="millisecondsDelay">Nonnegative delay in milliseconds; zero cancels now.</param>
        /// <param name="delayType">Clock that measures the delay.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures the delay and cancels.</param>
        /// <returns>Timer handle; disposing it stops the pending cancellation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cancellationTokenSource"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsDelay"/> is negative.</exception>
        public static IDisposable CancelAfterSlim(
            this CancellationTokenSource cancellationTokenSource,
            int millisecondsDelay,
            OnityDelayType delayType = OnityDelayType.DeltaTime,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update)
        {
            if (millisecondsDelay < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
            }

            return CancelAfterSlim(
                cancellationTokenSource, TimeSpan.FromMilliseconds(millisecondsDelay), delayType, delayTiming);
        }

        private static readonly Action<object> s_cancelSource = CancelSource;

        private static void CancelSource(object state)
        {
            try
            {
                ((CancellationTokenSource)state).Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The owner disposed the source; nothing is left to cancel.
            }
        }

        private static async Task CancelAfterDelayAsync(
            CancellationTokenSource cancellationTokenSource,
            CancellationTokenSource timerCancellationTokenSource,
            OnityTimeProvider timeProvider,
            TimeSpan timeout)
        {
            try
            {
                await timeProvider.DelayAsync(timeout, timerCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            cancellationTokenSource.Cancel();
        }
    }
}

