using System;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Cancellation, timeout and fire-and-forget task helpers.
    /// </summary>
    public static partial class OnityTaskExtensions
    {
        /// <summary>Faults with TimeoutException if a private timer wins before the producer.</summary>
        /// <param name="task">Producer to observe without canceling it.</param>
        /// <param name="seconds">Finite, nonnegative duration; a pending zero times out immediately.</param>
        /// <param name="useUnscaledTime">Use unscaled frame time; scaled time pauses at timeScale zero.</param>
        /// <returns>The original completed task, or a single-consumer native wrapper.</returns>
        /// <remarks>Positive pending creation requires the main thread and an accepting Play/player
        /// session. Wrapping claims a single-consumer input; do not consume it again, even after
        /// timeout. Pending producer observation is retained until it completes. Producer completion
        /// may publish on its thread; timer/session outcomes publish on main. Pending wrappers allocate.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">Seconds is negative, NaN or infinite.</exception>
        public static OnityTask Timeout(this OnityTask task, float seconds, bool useUnscaledTime = true)
        {
            ValidateTimeoutSeconds(seconds);
            try
            {
                if (task.IsCompleted)
                {
                    return task;
                }
            }
            catch (Exception exception)
            {
                return OnityTimeoutTaskSource.Create(task, seconds, useUnscaledTime, exception);
            }
            return OnityTimeoutTaskSource.Create(task, seconds, useUnscaledTime);
        }

        /// <summary>Faults with TimeoutException if a private timer wins before the producer.</summary>
        /// <typeparam name="T">Producer result type.</typeparam>
        /// <param name="task">Producer to observe without canceling it.</param>
        /// <param name="seconds">Finite, nonnegative duration; a pending zero times out immediately.</param>
        /// <param name="useUnscaledTime">Use unscaled frame time; scaled time pauses at timeScale zero.</param>
        /// <returns>The original completed task, or a single-consumer native wrapper.</returns>
        /// <remarks>Positive pending creation requires the main thread and an accepting Play/player
        /// session. Wrapping claims a single-consumer input; do not consume it again, even after
        /// timeout. Pending producer observation is retained until it completes. Producer completion
        /// may publish on its thread; timer/session outcomes publish on main. Pending wrappers allocate.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">Seconds is negative, NaN or infinite.</exception>
        public static OnityTask<T> Timeout<T>(
            this OnityTask<T> task, float seconds, bool useUnscaledTime = true)
        {
            ValidateTimeoutSeconds(seconds);
            try
            {
                if (task.IsCompleted)
                {
                    return task;
                }
            }
            catch (Exception exception)
            {
                return OnityTimeoutTaskSource<T>.Create(task, seconds, useUnscaledTime, exception);
            }
            return OnityTimeoutTaskSource<T>.Create(task, seconds, useUnscaledTime);
        }

        /// <summary>Returns a timeout flag only when the private timer wins; producer faults still throw.</summary>
        /// <param name="task">Producer to consume without canceling it.</param>
        /// <param name="seconds">Finite, nonnegative duration; a pending zero times out immediately.</param>
        /// <param name="useUnscaledTime">Use unscaled frame time; scaled time pauses at timeScale zero.</param>
        /// <returns>True for a private timeout, false for producer success; producer cancellation
        /// retains its token and faults retain their original exception.</returns>
        /// <remarks>Positive pending creation requires the main thread and an accepting Play/player
        /// session. Do not consume a claimed single-consumer input again. Native outputs are
        /// single-consumer; completed success is inline. An indefinitely pending loser retains
        /// its observer. Producer publication may run on its thread; pending wrappers allocate.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">Seconds is negative, NaN or infinite.</exception>
        public static OnityTask<bool> TimeoutWithoutException(
            this OnityTask task, float seconds, bool useUnscaledTime = true)
        {
            ValidateTimeoutSeconds(seconds);
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
                return OnityTimeoutFlagTaskSource.Create(task, seconds, useUnscaledTime, exception);
            }
            return OnityTimeoutFlagTaskSource.Create(task, seconds, useUnscaledTime);
        }

        /// <summary>Returns a timeout flag only when the private timer wins; producer faults still throw.</summary>
        /// <typeparam name="T">Producer result type.</typeparam>
        /// <param name="task">Producer to consume without canceling it.</param>
        /// <param name="seconds">Finite, nonnegative duration; a pending zero times out immediately.</param>
        /// <param name="useUnscaledTime">Use unscaled frame time; scaled time pauses at timeScale zero.</param>
        /// <returns>A successful producer result with isTimeout false, or default result with
        /// isTimeout true; producer cancellation and faults preserve their status and identity.</returns>
        /// <remarks>Positive pending creation requires the main thread and an accepting Play/player
        /// session. Do not consume a claimed single-consumer input again. Native outputs are
        /// single-consumer; completed success is inline. An indefinitely pending loser retains
        /// its observer. Producer publication may run on its thread; pending wrappers allocate.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">Seconds is negative, NaN or infinite.</exception>
        public static OnityTask<(bool isTimeout, T result)> TimeoutWithoutException<T>(
            this OnityTask<T> task, float seconds, bool useUnscaledTime = true)
        {
            ValidateTimeoutSeconds(seconds);
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
                return OnityTimeoutFlagTaskSource<T>.Create(task, seconds, useUnscaledTime, exception);
            }
            return OnityTimeoutFlagTaskSource<T>.Create(task, seconds, useUnscaledTime);
        }

        private static void ValidateTimeoutSeconds(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
        }

        /// <summary>
        /// Cancels the returned pending task when the external token is canceled,
        /// without canceling the producer. The producer is still observed.
        /// </summary>
        /// <param name="task">Producer task.</param>
        /// <param name="cancellationToken">External cancellation token.</param>
        /// <returns>The original task when already complete or the token cannot cancel;
        /// otherwise a single-consumer task whose first observed outcome wins.</returns>
        /// <remarks>Wrapping claims a pending single-consumer input; do not consume the
        /// original again, even after external cancellation. An indefinitely pending
        /// producer retains its observer. Completion may run on the producer or
        /// canceling thread.</remarks>
        public static OnityTask AttachExternalCancellation(
            this OnityTask task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return task;
            }

            try
            {
                if (task.IsCompleted)
                {
                    return task;
                }
            }
            catch (Exception exception)
            {
                return OnityExternalCancellationSource.Create(task, cancellationToken, exception);
            }

            return OnityExternalCancellationSource.Create(task, cancellationToken);
        }

        /// <summary>
        /// Cancels the returned pending task when the external token is canceled,
        /// without canceling the producer. The producer is still observed.
        /// </summary>
        /// <typeparam name="T">Producer result type.</typeparam>
        /// <param name="task">Producer task.</param>
        /// <param name="cancellationToken">External cancellation token.</param>
        /// <returns>The original task when already complete or the token cannot cancel;
        /// otherwise a single-consumer task whose first observed outcome wins.</returns>
        /// <remarks>Wrapping claims a pending single-consumer input; do not consume the
        /// original again, even after external cancellation. An indefinitely pending
        /// producer retains its observer. Completion may run on the producer or
        /// canceling thread.</remarks>
        public static OnityTask<T> AttachExternalCancellation<T>(
            this OnityTask<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return task;
            }

            try
            {
                if (task.IsCompleted)
                {
                    return task;
                }
            }
            catch (Exception exception)
            {
                return OnityExternalCancellationSource<T>.Create(task, cancellationToken, exception);
            }

            return OnityExternalCancellationSource<T>.Create(task, cancellationToken);
        }

        /// <summary>
        /// Converts actual cancellation into a successful result without changing faults.
        /// </summary>
        /// <param name="task">Task to consume.</param>
        /// <returns>True for cancellation and false for success. A faulted
        /// OperationCanceledException remains a fault.</returns>
        /// <remarks>Consumes the input; do not consume a single-consumer input again.
        /// Native wrapper outputs are single-consumer; completed success/cancellation
        /// results are inline. Pending completion may publish on the producer thread.</remarks>
        public static OnityTask<bool> SuppressCancellationThrow(this OnityTask task)
        {
            return OnityCancellationSuppressionSource.Create(task);
        }

        /// <summary>
        /// Converts actual cancellation into a successful result without changing faults.
        /// </summary>
        /// <typeparam name="T">Producer result type.</typeparam>
        /// <param name="task">Task to consume.</param>
        /// <returns>The result with isCanceled false, or default result with isCanceled
        /// true. A faulted OperationCanceledException remains a fault.</returns>
        /// <remarks>Consumes the input; do not consume a single-consumer input again.
        /// Native wrapper outputs are single-consumer; completed success/cancellation
        /// results are inline. Pending completion may publish on the producer thread.</remarks>
        public static OnityTask<(bool isCanceled, T result)> SuppressCancellationThrow<T>(
            this OnityTask<T> task)
        {
            return OnityCancellationSuppressionSource<T>.Create(task);
        }

        /// <summary>
        /// Executes task without awaiting and routes exceptions to the callback or, without one, to
        /// <see cref="OnityTaskScheduler"/>, which drops an <see cref="OperationCanceledException"/>
        /// unless <see cref="OnityTaskScheduler.PropagateOperationCanceledException"/> is set.
        /// </summary>
        /// <param name="task">Task instance.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static async void Forget(this Task task, Action<Exception> exceptionHandler = null)
        {
            if (task == null)
            {
                return;
            }

            task = OnityTaskTracker.Track(task, "OnityTaskExtensions.Forget");

            try
            {
                await task;
            }
            catch (Exception exception)
            {
                if (exceptionHandler != null)
                {
                    exceptionHandler(exception);
                    return;
                }

                OnityTaskScheduler.PublishUnobservedException(exception);
            }
        }

        /// <summary>
        /// Executes generic task without awaiting and routes exceptions to the callback or, without
        /// one, to <see cref="OnityTaskScheduler"/>, which drops an
        /// <see cref="OperationCanceledException"/> unless
        /// <see cref="OnityTaskScheduler.PropagateOperationCanceledException"/> is set.
        /// </summary>
        /// <typeparam name="T">Task result type.</typeparam>
        /// <param name="task">Task instance.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static async void Forget<T>(this Task<T> task, Action<Exception> exceptionHandler = null)
        {
            if (task == null)
            {
                return;
            }

            task = OnityTaskTracker.Track(task, "OnityTaskExtensions.Forget<T>");

            try
            {
                await task;
            }
            catch (Exception exception)
            {
                if (exceptionHandler != null)
                {
                    exceptionHandler(exception);
                    return;
                }

                OnityTaskScheduler.PublishUnobservedException(exception);
            }
        }
    }
}
