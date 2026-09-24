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

