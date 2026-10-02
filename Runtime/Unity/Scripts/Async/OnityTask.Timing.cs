using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Timed and polled waits on a selected PlayerLoop timing: UniTask's <c>Delay</c> with a
    /// <c>DelayType</c>, <c>WaitForSeconds</c>, <c>WaitUntil</c>/<c>WaitWhile</c> with a timing or a
    /// state, <c>WaitUntilCanceled</c> and <c>WaitUntilValueChanged</c>.
    /// </summary>
    /// <remarks>
    /// These overloads run on Onity's PlayerLoop nodes: they require Unity's main thread and an active
    /// Play/player session (the float-seconds <c>Delay</c> and the token-only <c>WaitUntil</c> also serve
    /// Edit Mode). A pending wait is a pooled single-consumer task, polled after the continuations of its
    /// timing; a timing other than the eager three installs its node on first use. A flag-only
    /// cancellation is published by the next Update drain; <c>cancelImmediately</c> publishes it on the
    /// canceling thread, which then runs the continuation. A session exit cancels pending waits.
    /// Integer milliseconds overloads are deliberately absent: Onity's numeric <c>Delay</c> takes
    /// seconds, so <c>Delay(TimeSpan, ...)</c> carries UniTask's millisecond overloads.
    /// </remarks>
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Awaits a delay measured with scaled or unscaled frame time at the selected timing; the equivalent
        /// of UniTask's <c>Delay(TimeSpan, bool ignoreTimeScale, PlayerLoopTiming, CancellationToken, bool)</c>.
        /// </summary>
        /// <param name="delay">Nonnegative delay; zero completes immediately.</param>
        /// <param name="ignoreTimeScale">True to measure unscaled frame time.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures and resumes the wait.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative, or the timing
        /// is not defined.</exception>
        /// <exception cref="InvalidOperationException">Not on the main thread of an active session.</exception>
        public static OnityTask Delay(
            TimeSpan delay,
            bool ignoreTimeScale,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            return Delay(
                delay,
                ignoreTimeScale ? OnityDelayType.UnscaledDeltaTime : OnityDelayType.DeltaTime,
                delayTiming,
                cancellationToken,
                cancelImmediately);
        }

        /// <summary>
        /// Awaits a delay measured with the selected clock at the selected timing; the equivalent of
        /// UniTask's <c>Delay(TimeSpan, DelayType, PlayerLoopTiming, CancellationToken, bool)</c>. The frame
        /// clocks start counting at the frame after this call.
        /// </summary>
        /// <param name="delay">Nonnegative delay; zero completes immediately.</param>
        /// <param name="delayType">Clock that measures the delay.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures and resumes the wait.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative, or the clock
        /// or the timing is not defined.</exception>
        /// <exception cref="InvalidOperationException">Not on the main thread of an active session.</exception>
        public static OnityTask Delay(
            TimeSpan delay,
            OnityDelayType delayType,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            ValidateDelayType(delayType);
            OnityTaskPlayerLoop.ValidateLoopWait(delayTiming);
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            if (delay == TimeSpan.Zero)
            {
                return Completed;
            }

            return OnityLoopDelaySource.Start(delay, delayType, delayTiming, cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Awaits a number of seconds at the selected timing; the equivalent of UniTask's
        /// <c>WaitForSeconds(float, ...)</c>.
        /// </summary>
        /// <param name="duration">Finite, nonnegative seconds; zero completes immediately.</param>
        /// <param name="ignoreTimeScale">True to measure unscaled frame time.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures and resumes the wait.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative, NaN or
        /// infinite.</exception>
        public static OnityTask WaitForSeconds(
            float duration,
            bool ignoreTimeScale = false,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            return Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale, delayTiming, cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Awaits a whole number of seconds at the selected timing; the equivalent of UniTask's
        /// <c>WaitForSeconds(int, ...)</c>.
        /// </summary>
        /// <param name="duration">Nonnegative seconds; zero completes immediately.</param>
        /// <param name="ignoreTimeScale">True to measure unscaled frame time.</param>
        /// <param name="delayTiming">PlayerLoop timing that measures and resumes the wait.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
        public static OnityTask WaitForSeconds(
            int duration,
            bool ignoreTimeScale = false,
            OnityPlayerLoopTiming delayTiming = OnityPlayerLoopTiming.Update,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            if (duration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            return Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale, delayTiming, cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Awaits until <paramref name="predicate"/> returns true, polled at the selected timing; a predicate
        /// that is already true completes immediately, as in <see cref="WaitUntil(Func{bool}, CancellationToken)"/>.
        /// </summary>
        /// <param name="predicate">Condition; an exception it throws while polled faults the task.</param>
        /// <param name="timing">PlayerLoop timing that polls the condition.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public static OnityTask WaitUntil(
            Func<bool> predicate,
            OnityPlayerLoopTiming timing,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return WaitForPredicate(predicate, OnityLoopPredicates.Invoke, true, timing, cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Awaits while <paramref name="predicate"/> returns true, polled at the selected timing; a predicate
        /// that is already false completes immediately.
        /// </summary>
        /// <param name="predicate">Condition; an exception it throws while polled faults the task.</param>
        /// <param name="timing">PlayerLoop timing that polls the condition.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public static OnityTask WaitWhile(
            Func<bool> predicate,
            OnityPlayerLoopTiming timing,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return WaitForPredicate(predicate, OnityLoopPredicates.Invoke, false, timing, cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Awaits until <paramref name="predicate"/> returns true for <paramref name="state"/>, polled at the
        /// selected timing, without a capturing closure; the equivalent of UniTask's
        /// <c>WaitUntil&lt;T&gt;(T, Func&lt;T, bool&gt;, ...)</c>.
        /// </summary>
        /// <typeparam name="TState">State type.</typeparam>
        /// <param name="state">State passed to the predicate.</param>
        /// <param name="predicate">Condition; an exception it throws while polled faults the task.</param>
        /// <param name="timing">PlayerLoop timing that polls the condition.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public static OnityTask WaitUntil<TState>(
            TState state,
            Func<TState, bool> predicate,
            OnityPlayerLoopTiming timing = OnityPlayerLoopTiming.Update,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return WaitForPredicate(state, predicate, true, timing, cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Awaits while <paramref name="predicate"/> returns true for <paramref name="state"/>, polled at the
        /// selected timing, without a capturing closure.
        /// </summary>
        /// <typeparam name="TState">State type.</typeparam>
        /// <param name="state">State passed to the predicate.</param>
        /// <param name="predicate">Condition; an exception it throws while polled faults the task.</param>
        /// <param name="timing">PlayerLoop timing that polls the condition.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Completion task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public static OnityTask WaitWhile<TState>(
            TState state,
            Func<TState, bool> predicate,
            OnityPlayerLoopTiming timing = OnityPlayerLoopTiming.Update,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            return WaitForPredicate(state, predicate, false, timing, cancellationToken, cancelImmediately);
        }

        /// <summary>
        /// Completes successfully at the first drain of <paramref name="timing"/> after
        /// <paramref name="cancellationToken"/> is canceled; the equivalent of UniTask's
        /// <c>WaitUntilCanceled</c>. A token that is already canceled completes immediately; one that cannot
        /// be canceled waits until the session ends.
        /// </summary>
        /// <param name="cancellationToken">Token to observe; its cancellation is the success.</param>
        /// <param name="timing">PlayerLoop timing that resumes the wait.</param>
        /// <param name="completeImmediately">True to complete on the canceling thread instead.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask WaitUntilCanceled(
            CancellationToken cancellationToken,
            OnityPlayerLoopTiming timing = OnityPlayerLoopTiming.Update,
            bool completeImmediately = false)
        {
            OnityTaskPlayerLoop.ValidateLoopWait(timing);
            if (cancellationToken.IsCancellationRequested)
            {
                return Completed;
            }

            return OnityLoopCanceledSource.Start(cancellationToken, timing, completeImmediately);
        }

        /// <summary>
        /// Polls <paramref name="monitorFunction"/> at the selected timing and completes with the first value
        /// that differs from the value read by this call; the equivalent of UniTask's
        /// <c>WaitUntilValueChanged</c>. A plain target is held weakly and a Unity object strongly; a
        /// collected or destroyed target cancels the task.
        /// </summary>
        /// <typeparam name="T">Monitored object type.</typeparam>
        /// <typeparam name="U">Monitored value type.</typeparam>
        /// <param name="target">Monitored object.</param>
        /// <param name="monitorFunction">Value reader; it runs once now and at each poll, and an exception it
        /// throws while polled faults the task.</param>
        /// <param name="monitorTiming">PlayerLoop timing that polls the value.</param>
        /// <param name="equalityComparer">Comparer; the default comparer when null.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="cancelImmediately">True to publish the cancellation on the canceling thread.</param>
        /// <returns>Task with the changed value.</returns>
        /// <exception cref="ArgumentNullException">The target or the monitor is null.</exception>
        public static OnityTask<U> WaitUntilValueChanged<T, U>(
            T target,
            Func<T, U> monitorFunction,
            OnityPlayerLoopTiming monitorTiming = OnityPlayerLoopTiming.Update,
            IEqualityComparer<U> equalityComparer = null,
            CancellationToken cancellationToken = default,
            bool cancelImmediately = false)
            where T : class
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (monitorFunction == null)
            {
                throw new ArgumentNullException(nameof(monitorFunction));
            }

            OnityTaskPlayerLoop.ValidateLoopWait(monitorTiming);
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<U>.FromCanceled(cancellationToken);
            }

            U current = monitorFunction(target);
            return OnityLoopValueChangedSource<T, U>.Start(
                target, monitorFunction, current, equalityComparer ?? EqualityComparer<U>.Default,
                monitorTiming, cancellationToken, cancelImmediately);
        }

        private static OnityTask WaitForPredicate<TState>(
            TState state, Func<TState, bool> predicate, bool completeWhen, OnityPlayerLoopTiming timing,
            CancellationToken cancellationToken, bool cancelImmediately)
        {
            OnityTaskPlayerLoop.ValidateLoopWait(timing);
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            if (predicate(state) == completeWhen)
            {
                return Completed;
            }

            return OnityLoopPredicateSource<TState>.Start(
                state, predicate, completeWhen, timing, cancellationToken, cancelImmediately);
        }

        private static void ValidateDelayType(OnityDelayType delayType)
        {
            if ((uint)delayType > (uint)OnityDelayType.Realtime)
            {
                throw new ArgumentOutOfRangeException(nameof(delayType));
            }
        }
    }
}
