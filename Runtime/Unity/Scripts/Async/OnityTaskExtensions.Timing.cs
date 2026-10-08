using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// UniTask-shaped timeouts measured with an <see cref="OnityDelayType"/> clock at a PlayerLoop timing.
    /// </summary>
    public static partial class OnityTaskExtensions
    {
        /// <summary>
        /// Faults with <see cref="TimeoutException"/> when a timer measured at the selected timing wins before
        /// the producer; the equivalent of UniTask's <c>Timeout(TimeSpan, DelayType, PlayerLoopTiming,
        /// CancellationTokenSource)</c>. Producer outcomes keep their status and identity.
        /// </summary>
        /// <param name="task">Producer to observe; it is not canceled unless
        /// <paramref name="taskCancellationTokenSource"/> is given.</param>
        /// <param name="timeout">Nonnegative timeout; a pending zero times out immediately.</param>
        /// <param name="delayType">Clock that measures the timeout.</param>
        /// <param name="timeoutCheckTiming">PlayerLoop timing that measures the timeout.</param>
        /// <param name="taskCancellationTokenSource">Optional source canceled when the timeout wins, before
        /// the timeout is published.</param>
        /// <returns>The original completed task, or a single-consumer native wrapper.</returns>
        /// <remarks>Positive pending creation requires the main thread and an accepting Play/player session.
        /// Wrapping claims a single-consumer input; do not consume it again.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is negative, or the clock or the timing is
        /// not defined.</exception>
        public static OnityTask Timeout(
            this OnityTask task,
            TimeSpan timeout,
            OnityDelayType delayType = OnityDelayType.DeltaTime,
            OnityPlayerLoopTiming timeoutCheckTiming = OnityPlayerLoopTiming.Update,
            CancellationTokenSource taskCancellationTokenSource = null)
        {
            ValidateTimedTimeout(timeout, delayType, timeoutCheckTiming);
            try
            {
                if (task.IsCompleted)
                {
                    return task;
                }
            }
            catch (Exception exception)
            {
                return OnityTimeoutTaskSource.Create(
                    task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource, exception);
            }

            return OnityTimeoutTaskSource.Create(task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource);
        }

        /// <summary>
        /// Faults with <see cref="TimeoutException"/> when a timer measured at the selected timing wins before
        /// the producer; see <see cref="Timeout(OnityTask, TimeSpan, OnityDelayType, OnityPlayerLoopTiming, CancellationTokenSource)"/>.
        /// </summary>
        /// <typeparam name="T">Producer result type.</typeparam>
        /// <param name="task">Producer to observe.</param>
        /// <param name="timeout">Nonnegative timeout; a pending zero times out immediately.</param>
        /// <param name="delayType">Clock that measures the timeout.</param>
        /// <param name="timeoutCheckTiming">PlayerLoop timing that measures the timeout.</param>
        /// <param name="taskCancellationTokenSource">Optional source canceled when the timeout wins.</param>
        /// <returns>The original completed task, or a single-consumer native wrapper.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is negative, or the clock or the timing is
        /// not defined.</exception>
        public static OnityTask<T> Timeout<T>(
            this OnityTask<T> task,
            TimeSpan timeout,
            OnityDelayType delayType = OnityDelayType.DeltaTime,
            OnityPlayerLoopTiming timeoutCheckTiming = OnityPlayerLoopTiming.Update,
            CancellationTokenSource taskCancellationTokenSource = null)
        {
            ValidateTimedTimeout(timeout, delayType, timeoutCheckTiming);
            try
            {
                if (task.IsCompleted)
                {
                    return task;
                }
            }
            catch (Exception exception)
            {
                return OnityTimeoutTaskSource<T>.Create(
                    task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource, exception);
            }

            return OnityTimeoutTaskSource<T>.Create(
                task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource);
        }

        /// <summary>
        /// Returns true only when a timer measured at the selected timing wins; producer faults still throw.
        /// The equivalent of UniTask's <c>TimeoutWithoutException(TimeSpan, DelayType, PlayerLoopTiming,
        /// CancellationTokenSource)</c>.
        /// </summary>
        /// <param name="task">Producer to consume.</param>
        /// <param name="timeout">Nonnegative timeout; a pending zero times out immediately.</param>
        /// <param name="delayType">Clock that measures the timeout.</param>
        /// <param name="timeoutCheckTiming">PlayerLoop timing that measures the timeout.</param>
        /// <param name="taskCancellationTokenSource">Optional source canceled when the timeout wins.</param>
        /// <returns>True for a timeout, false for producer success; producer cancellation and faults keep
        /// their status and identity.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is negative, or the clock or the timing is
        /// not defined.</exception>
        public static OnityTask<bool> TimeoutWithoutException(
            this OnityTask task,
            TimeSpan timeout,
            OnityDelayType delayType = OnityDelayType.DeltaTime,
            OnityPlayerLoopTiming timeoutCheckTiming = OnityPlayerLoopTiming.Update,
            CancellationTokenSource taskCancellationTokenSource = null)
        {
            ValidateTimedTimeout(timeout, delayType, timeoutCheckTiming);
            try
            {
                if (task.IsCompleted)
                {
                    OnityTaskSourceStatus status = task.ReadWhenAnyOutcome(
                        out Exception fault, out CancellationToken token);
                    return OnityTimeoutFlagTaskSource.FromOutcome(status, fault, token);
                }
            }
            catch (Exception exception)
            {
                return OnityTimeoutFlagTaskSource.Create(
                    task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource, exception);
            }

            return OnityTimeoutFlagTaskSource.Create(
                task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource);
        }

        /// <summary>
        /// Returns a timeout flag only when a timer measured at the selected timing wins; see
        /// <see cref="TimeoutWithoutException(OnityTask, TimeSpan, OnityDelayType, OnityPlayerLoopTiming, CancellationTokenSource)"/>.
        /// </summary>
        /// <typeparam name="T">Producer result type.</typeparam>
        /// <param name="task">Producer to consume.</param>
        /// <param name="timeout">Nonnegative timeout; a pending zero times out immediately.</param>
        /// <param name="delayType">Clock that measures the timeout.</param>
        /// <param name="timeoutCheckTiming">PlayerLoop timing that measures the timeout.</param>
        /// <param name="taskCancellationTokenSource">Optional source canceled when the timeout wins.</param>
        /// <returns>The producer result with isTimeout false, or default with isTimeout true.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is negative, or the clock or the timing is
        /// not defined.</exception>
        public static OnityTask<(bool isTimeout, T result)> TimeoutWithoutException<T>(
            this OnityTask<T> task,
            TimeSpan timeout,
            OnityDelayType delayType = OnityDelayType.DeltaTime,
            OnityPlayerLoopTiming timeoutCheckTiming = OnityPlayerLoopTiming.Update,
            CancellationTokenSource taskCancellationTokenSource = null)
        {
            ValidateTimedTimeout(timeout, delayType, timeoutCheckTiming);
            try
            {
                if (task.IsCompleted)
                {
                    OnityTaskSourceStatus status = task.ReadWhenAnyOutcome(
                        out T result, out Exception fault, out CancellationToken token);
                    return OnityTimeoutFlagTaskSource<T>.FromOutcome(status, result, fault, token);
                }
            }
            catch (Exception exception)
            {
                return OnityTimeoutFlagTaskSource<T>.Create(
                    task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource, exception);
            }

            return OnityTimeoutFlagTaskSource<T>.Create(
                task, timeout, delayType, timeoutCheckTiming, taskCancellationTokenSource);
        }

        private static void ValidateTimedTimeout(
            TimeSpan timeout, OnityDelayType delayType, OnityPlayerLoopTiming timing)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            if ((uint)delayType > (uint)OnityDelayType.Realtime)
            {
                throw new ArgumentOutOfRangeException(nameof(delayType));
            }

            if ((uint)timing > (uint)OnityPlayerLoopTiming.LastTimeUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(timing));
            }
        }
    }
}
