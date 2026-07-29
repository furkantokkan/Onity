using System;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.DOTS
{
    /// <summary>
    /// Async helpers for waiting on DOTS integer event accumulator values.
    /// </summary>
    public static class OnityDotsIntEventAsync
    {
        /// <summary>
        /// Waits until accumulator value reaches or exceeds target.
        /// </summary>
        /// <param name="minimumValue">Target accumulator value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Accumulator value observed at completion.</returns>
        public static async Task<int> WaitForAccumulatorAtLeastAsync(
            int minimumValue,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<int>(cancellationToken);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (OnityDotsIntEventBridge.TryGetAccumulatedValue(out int value) && value >= minimumValue)
                {
                    return value;
                }

                await Task.Yield();
            }
        }

        /// <summary>
        /// Waits until accumulator value changes from the previous snapshot.
        /// </summary>
        /// <param name="previousValue">Previous snapshot value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Updated accumulator value.</returns>
        public static async Task<int> WaitForAccumulatorChangeAsync(
            int previousValue,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<int>(cancellationToken);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (OnityDotsIntEventBridge.TryGetAccumulatedValue(out int value) && value != previousValue)
                {
                    return value;
                }

                await Task.Yield();
            }
        }

        /// <summary>
        /// Waits until accumulator value differs from current snapshot.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Updated accumulator value.</returns>
        public static Task<int> WaitForNextAccumulatorUpdateAsync(CancellationToken cancellationToken = default)
        {
            if (OnityDotsIntEventBridge.TryGetAccumulatedValue(out int current))
            {
                return WaitForAccumulatorChangeAsync(current, cancellationToken);
            }

            return WaitForAccumulatorChangeAsync(0, cancellationToken);
        }
    }
}
