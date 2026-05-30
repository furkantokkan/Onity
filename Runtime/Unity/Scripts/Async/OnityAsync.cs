using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;
using Onity.Reactive;
using Onity.Unity.Reactive;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Lightweight async helpers inspired by common Unity task workflows.
    /// </summary>
    public static class OnityAsync
    {
        /// <summary>
        /// Awaits one rendered frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task NextFrameAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.NextFrameAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable.EveryUpdate().ToTask(cancellationToken),
                "OnityAsync.NextFrameAsync");
        }

        /// <summary>
        /// Awaits one fixed update frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task NextFixedFrameAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.NextFixedFrameAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable.EveryFixedUpdate().ToTask(cancellationToken),
                "OnityAsync.NextFixedFrameAsync");
        }

        /// <summary>
        /// Awaits a delay in seconds.
        /// </summary>
        /// <param name="delaySeconds">Delay duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task DelayAsync(
            float delaySeconds,
            bool useUnscaledTime = false,
            CancellationToken cancellationToken = default)
        {
            if (delaySeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.DelayAsync");
            }

            if (delaySeconds <= 0f)
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.DelayAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable.Timer(delaySeconds, useUnscaledTime).ToTask(cancellationToken),
                "OnityAsync.DelayAsync");
        }

        /// <summary>
        /// Awaits a delay using a time provider.
        /// </summary>
        /// <param name="delay">Delay duration.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task DelayAsync(
            TimeSpan delay,
            OnityTimeProvider timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.DelayAsync(TimeSpan)");
            }

            if (delay == TimeSpan.Zero)
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.DelayAsync(TimeSpan)");
            }

            OnityTimeProvider resolvedTimeProvider = timeProvider ?? OnityTimeProvider.System;

            return OnityTaskTracker.Track(
                resolvedTimeProvider.DelayAsync(delay, cancellationToken),
                "OnityAsync.DelayAsync(TimeSpan)");
        }

        /// <summary>
        /// Awaits until predicate returns true.
        /// </summary>
        /// <param name="predicate">Predicate callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task WaitUntilAsync(
            Func<bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.WaitUntilAsync");
            }

            if (predicate())
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.WaitUntilAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable
                    .EveryUpdate(cancellationToken)
                    .Where(_ => predicate())
                    .ToTask(cancellationToken),
                "OnityAsync.WaitUntilAsync");
        }

        /// <summary>
        /// Awaits while predicate remains true.
        /// </summary>
        /// <param name="predicate">Predicate callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task WaitWhileAsync(
            Func<bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.WaitWhileAsync");
            }

            if (predicate() == false)
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.WaitWhileAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable
                    .EveryUpdate(cancellationToken)
                    .Where(_ => predicate() == false)
                    .ToTask(cancellationToken),
                "OnityAsync.WaitWhileAsync");
        }

        /// <summary>
        /// Returns a completed task for Unit payload streams.
        /// </summary>
        /// <param name="observable">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task AwaitObservable(
            IOnityObservable<Unit> observable,
            CancellationToken cancellationToken = default)
        {
            if (observable == null)
            {
                throw new ArgumentNullException(nameof(observable));
            }

            return OnityTaskTracker.Track(
                observable.ToTask(cancellationToken),
                "OnityAsync.AwaitObservable");
        }

        /// <summary>
        /// Awaits completion of all tasks.
        /// </summary>
        /// <param name="tasks">Task list.</param>
        /// <returns>Completion task.</returns>
        public static Task WhenAll(params Task[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAll(tasks),
                "OnityAsync.WhenAll");
        }

        /// <summary>
        /// Awaits completion of all typed tasks.
        /// </summary>
        /// <typeparam name="T">Task result type.</typeparam>
        /// <param name="tasks">Task list.</param>
        /// <returns>Task array result.</returns>
        public static Task<T[]> WhenAll<T>(params Task<T>[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAll(tasks),
                "OnityAsync.WhenAll<T>");
        }

        /// <summary>
        /// Awaits the first completed task from list.
        /// </summary>
        /// <param name="tasks">Task list.</param>
        /// <returns>Winner task.</returns>
        public static Task<Task> WhenAny(params Task[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAny(tasks),
                "OnityAsync.WhenAny");
        }

        /// <summary>
        /// Awaits the first completed typed task from list.
        /// </summary>
        /// <typeparam name="T">Task result type.</typeparam>
        /// <param name="tasks">Task list.</param>
        /// <returns>Winner task.</returns>
        public static Task<Task<T>> WhenAny<T>(params Task<T>[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAny(tasks),
                "OnityAsync.WhenAny<T>");
        }
    }
}
