using System;
using System.Collections.Generic;
using System.Threading;
using Onity.Core;

namespace Onity.Unity.Async
{
    /// <summary>
    /// PlayerLoop timing streams with UniTask's shapes: <c>EveryUpdate(timing)</c>, <c>Timer</c>,
    /// <c>Interval</c>, <c>TimerFrame</c>, <c>IntervalFrame</c> and <c>EveryValueChanged</c>.
    /// </summary>
    /// <remarks>
    /// The streams are inert descriptions; each enumeration owns its waits. Every active move waits on
    /// Onity's PlayerLoop nodes, so it requires the main thread and an accepting Play/player session.
    /// A zero duration or frame count waits for the next drain of the timing instead of completing
    /// inline, so an <c>await foreach</c> never spins within one frame. <c>cancelImmediately</c> publishes
    /// a cancellation of a pending move on the canceling thread.
    /// </remarks>
    public static partial class OnityAsyncEnumerable
    {
        /// <summary>
        /// Creates an on-demand stream yielding <see cref="Unit.Default"/> at every drain of
        /// <paramref name="updateTiming"/>; the equivalent of UniTask's <c>EveryUpdate(PlayerLoopTiming, bool)</c>.
        /// </summary>
        /// <param name="updateTiming">PlayerLoop timing of each item.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The timing is not defined.</exception>
        public static IOnityAsyncEnumerable<Unit> EveryUpdate(
            OnityPlayerLoopTiming updateTiming, bool cancelImmediately = false)
        {
            OnityLoopStreams.ValidateTiming(updateTiming);
            return new OnityLoopStreams.FrameDescription(0, 0, true, updateTiming, cancelImmediately);
        }

        /// <summary>
        /// Creates a stream that yields once after <paramref name="dueTime"/>; the equivalent of UniTask's
        /// <c>Timer(TimeSpan, ...)</c>.
        /// </summary>
        /// <param name="dueTime">Nonnegative delay before the item.</param>
        /// <param name="updateTiming">PlayerLoop timing that measures the delay.</param>
        /// <param name="ignoreTimeScale">True to measure unscaled frame time.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The delay is negative or the timing is not defined.</exception>
        public static IOnityAsyncEnumerable<Unit> Timer(
            TimeSpan dueTime,
            OnityPlayerLoopTiming updateTiming = OnityPlayerLoopTiming.Update,
            bool ignoreTimeScale = false,
            bool cancelImmediately = false)
        {
            OnityLoopStreams.ValidateDuration(dueTime, nameof(dueTime));
            OnityLoopStreams.ValidateTiming(updateTiming);
            return new OnityLoopStreams.TimeDescription(
                dueTime, TimeSpan.Zero, false, updateTiming, ignoreTimeScale, cancelImmediately);
        }

        /// <summary>
        /// Creates a stream that yields after <paramref name="dueTime"/> and then every
        /// <paramref name="period"/>; the equivalent of UniTask's <c>Timer(TimeSpan, TimeSpan, ...)</c>.
        /// </summary>
        /// <param name="dueTime">Nonnegative delay before the first item.</param>
        /// <param name="period">Nonnegative delay between the next items.</param>
        /// <param name="updateTiming">PlayerLoop timing that measures the delays.</param>
        /// <param name="ignoreTimeScale">True to measure unscaled frame time.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A delay is negative or the timing is not defined.</exception>
        public static IOnityAsyncEnumerable<Unit> Timer(
            TimeSpan dueTime,
            TimeSpan period,
            OnityPlayerLoopTiming updateTiming = OnityPlayerLoopTiming.Update,
            bool ignoreTimeScale = false,
            bool cancelImmediately = false)
        {
            OnityLoopStreams.ValidateDuration(dueTime, nameof(dueTime));
            OnityLoopStreams.ValidateDuration(period, nameof(period));
            OnityLoopStreams.ValidateTiming(updateTiming);
            return new OnityLoopStreams.TimeDescription(
                dueTime, period, true, updateTiming, ignoreTimeScale, cancelImmediately);
        }

        /// <summary>
        /// Creates a stream that yields every <paramref name="period"/>; the equivalent of UniTask's
        /// <c>Interval</c>.
        /// </summary>
        /// <param name="period">Nonnegative delay before each item.</param>
        /// <param name="updateTiming">PlayerLoop timing that measures the delays.</param>
        /// <param name="ignoreTimeScale">True to measure unscaled frame time.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The period is negative or the timing is not defined.</exception>
        public static IOnityAsyncEnumerable<Unit> Interval(
            TimeSpan period,
            OnityPlayerLoopTiming updateTiming = OnityPlayerLoopTiming.Update,
            bool ignoreTimeScale = false,
            bool cancelImmediately = false)
        {
            return Timer(period, period, updateTiming, ignoreTimeScale, cancelImmediately);
        }

        /// <summary>
        /// Creates a stream that yields once after <paramref name="dueTimeFrameCount"/> rendered frames; the
        /// equivalent of UniTask's <c>TimerFrame(int, ...)</c>.
        /// </summary>
        /// <param name="dueTimeFrameCount">Nonnegative frame count before the item.</param>
        /// <param name="updateTiming">PlayerLoop timing that counts the frames.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The count is negative or the timing is not defined.</exception>
        public static IOnityAsyncEnumerable<Unit> TimerFrame(
            int dueTimeFrameCount,
            OnityPlayerLoopTiming updateTiming = OnityPlayerLoopTiming.Update,
            bool cancelImmediately = false)
        {
            OnityLoopStreams.ValidateFrames(dueTimeFrameCount, nameof(dueTimeFrameCount));
            OnityLoopStreams.ValidateTiming(updateTiming);
            return new OnityLoopStreams.FrameDescription(dueTimeFrameCount, 0, false, updateTiming, cancelImmediately);
        }

        /// <summary>
        /// Creates a stream that yields after <paramref name="dueTimeFrameCount"/> rendered frames and then
        /// every <paramref name="periodFrameCount"/>; the equivalent of UniTask's <c>TimerFrame(int, int, ...)</c>.
        /// </summary>
        /// <param name="dueTimeFrameCount">Nonnegative frame count before the first item.</param>
        /// <param name="periodFrameCount">Nonnegative frame count between the next items.</param>
        /// <param name="updateTiming">PlayerLoop timing that counts the frames.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A count is negative or the timing is not defined.</exception>
        public static IOnityAsyncEnumerable<Unit> TimerFrame(
            int dueTimeFrameCount,
            int periodFrameCount,
            OnityPlayerLoopTiming updateTiming = OnityPlayerLoopTiming.Update,
            bool cancelImmediately = false)
        {
            OnityLoopStreams.ValidateFrames(dueTimeFrameCount, nameof(dueTimeFrameCount));
            OnityLoopStreams.ValidateFrames(periodFrameCount, nameof(periodFrameCount));
            OnityLoopStreams.ValidateTiming(updateTiming);
            return new OnityLoopStreams.FrameDescription(
                dueTimeFrameCount, periodFrameCount, true, updateTiming, cancelImmediately);
        }

        /// <summary>
        /// Creates a stream that yields every <paramref name="intervalFrameCount"/> rendered frames; the
        /// equivalent of UniTask's <c>IntervalFrame</c>.
        /// </summary>
        /// <param name="intervalFrameCount">Nonnegative frame count before each item.</param>
        /// <param name="updateTiming">PlayerLoop timing that counts the frames.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The count is negative or the timing is not defined.</exception>
        public static IOnityAsyncEnumerable<Unit> IntervalFrame(
            int intervalFrameCount,
            OnityPlayerLoopTiming updateTiming = OnityPlayerLoopTiming.Update,
            bool cancelImmediately = false)
        {
            return TimerFrame(intervalFrameCount, intervalFrameCount, updateTiming, cancelImmediately);
        }

        /// <summary>
        /// Creates a stream that yields the current value of <paramref name="propertySelector"/> first and then
        /// every value that differs from the previous item, polled at <paramref name="monitorTiming"/>; the
        /// equivalent of UniTask's <c>EveryValueChanged</c>. A plain target is held weakly and a Unity object
        /// strongly; the stream ends when the target is collected or destroyed.
        /// </summary>
        /// <typeparam name="TTarget">Monitored object type.</typeparam>
        /// <typeparam name="TProperty">Monitored value type.</typeparam>
        /// <param name="target">Monitored object.</param>
        /// <param name="propertySelector">Value reader; an exception it throws faults the move.</param>
        /// <param name="monitorTiming">PlayerLoop timing that polls the value.</param>
        /// <param name="equalityComparer">Comparer; the default comparer when null.</param>
        /// <param name="cancelImmediately">True to publish a cancellation on the canceling thread.</param>
        /// <returns>A reusable stream description.</returns>
        /// <exception cref="ArgumentNullException">The target or the selector is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The timing is not defined.</exception>
        public static IOnityAsyncEnumerable<TProperty> EveryValueChanged<TTarget, TProperty>(
            TTarget target,
            Func<TTarget, TProperty> propertySelector,
            OnityPlayerLoopTiming monitorTiming = OnityPlayerLoopTiming.Update,
            IEqualityComparer<TProperty> equalityComparer = null,
            bool cancelImmediately = false)
            where TTarget : class
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (propertySelector == null)
            {
                throw new ArgumentNullException(nameof(propertySelector));
            }

            OnityLoopStreams.ValidateTiming(monitorTiming);
            return new OnityLoopStreams.ValueChangedDescription<TTarget, TProperty>(
                target, propertySelector, monitorTiming, equalityComparer ?? EqualityComparer<TProperty>.Default,
                cancelImmediately);
        }
    }

    /// <summary>Descriptions and enumerators of the PlayerLoop timing streams.</summary>
    internal static class OnityLoopStreams
    {
        internal static void ValidateTiming(OnityPlayerLoopTiming timing)
        {
            if ((uint)timing > (uint)OnityPlayerLoopTiming.LastTimeUpdate)
            {
                throw new ArgumentOutOfRangeException(nameof(timing));
            }
        }

        internal static void ValidateDuration(TimeSpan duration, string name)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        internal static void ValidateFrames(int frames, string name)
        {
            if (frames < 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        /// <summary>Frame-counted stream: EveryUpdate (zero frames), TimerFrame and IntervalFrame.</summary>
        internal sealed class FrameDescription : IOnityAsyncEnumerable<Unit>
        {
            private readonly int m_dueFrames;
            private readonly int m_periodFrames;
            private readonly bool m_periodic;
            private readonly OnityPlayerLoopTiming m_timing;
            private readonly bool m_cancelImmediately;

            internal FrameDescription(
                int dueFrames, int periodFrames, bool periodic, OnityPlayerLoopTiming timing, bool cancelImmediately)
            {
                m_dueFrames = dueFrames;
                m_periodFrames = periodFrames;
                m_periodic = periodic;
                m_timing = timing;
                m_cancelImmediately = cancelImmediately;
            }

            public IOnityAsyncEnumerator<Unit> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new Enumerator(this, cancellationToken);
            }

            private sealed class Enumerator : StreamEnumerator<Unit>
            {
                private readonly FrameDescription m_owner;
                private bool m_started;

                internal Enumerator(FrameDescription owner, CancellationToken callerToken) : base(callerToken)
                {
                    m_owner = owner;
                }

                protected override bool HasNextStep => !m_started || m_owner.m_periodic;

                protected override OnityTask CreateWait(CancellationToken token)
                {
                    int frames = m_started ? m_owner.m_periodFrames : m_owner.m_dueFrames;
                    m_started = true;
                    // Zero frames waits for the next drain, so a periodic stream cannot spin inline.
                    return OnityTaskPlayerLoop.Schedule(
                        m_owner.m_timing, frames, frames == 0, token, m_owner.m_cancelImmediately);
                }

                protected override bool CompleteStep(out Unit current)
                {
                    current = Unit.Default;
                    return true;
                }
            }
        }

        /// <summary>Time-measured stream: Timer and Interval.</summary>
        internal sealed class TimeDescription : IOnityAsyncEnumerable<Unit>
        {
            private readonly TimeSpan m_dueTime;
            private readonly TimeSpan m_period;
            private readonly bool m_periodic;
            private readonly OnityPlayerLoopTiming m_timing;
            private readonly OnityDelayType m_delayType;
            private readonly bool m_cancelImmediately;

            internal TimeDescription(
                TimeSpan dueTime, TimeSpan period, bool periodic, OnityPlayerLoopTiming timing,
                bool ignoreTimeScale, bool cancelImmediately)
            {
                m_dueTime = dueTime;
                m_period = period;
                m_periodic = periodic;
                m_timing = timing;
                m_delayType = ignoreTimeScale ? OnityDelayType.UnscaledDeltaTime : OnityDelayType.DeltaTime;
                m_cancelImmediately = cancelImmediately;
            }

            public IOnityAsyncEnumerator<Unit> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new Enumerator(this, cancellationToken);
            }

            private sealed class Enumerator : StreamEnumerator<Unit>
            {
                private readonly TimeDescription m_owner;
                private bool m_started;

                internal Enumerator(TimeDescription owner, CancellationToken callerToken) : base(callerToken)
                {
                    m_owner = owner;
                }

                protected override bool HasNextStep => !m_started || m_owner.m_periodic;

                protected override OnityTask CreateWait(CancellationToken token)
                {
                    TimeSpan delay = m_started ? m_owner.m_period : m_owner.m_dueTime;
                    m_started = true;
                    if (delay == TimeSpan.Zero)
                    {
                        // A zero delay waits for the next drain, so a periodic stream cannot spin inline.
                        return OnityTaskPlayerLoop.Schedule(m_owner.m_timing, 0, true, token, m_owner.m_cancelImmediately);
                    }

                    return OnityTask.Delay(delay, m_owner.m_delayType, m_owner.m_timing, token, m_owner.m_cancelImmediately);
                }

                protected override bool CompleteStep(out Unit current)
                {
                    current = Unit.Default;
                    return true;
                }
            }
        }

        /// <summary>Value-change stream: EveryValueChanged.</summary>
        internal sealed class ValueChangedDescription<TTarget, TProperty> : IOnityAsyncEnumerable<TProperty>
            where TTarget : class
        {
            private readonly TTarget m_unityTarget;
            private readonly WeakReference<TTarget> m_weakTarget;
            private readonly Func<TTarget, TProperty> m_selector;
            private readonly OnityPlayerLoopTiming m_timing;
            private readonly IEqualityComparer<TProperty> m_comparer;
            private readonly bool m_cancelImmediately;

            internal ValueChangedDescription(
                TTarget target, Func<TTarget, TProperty> selector, OnityPlayerLoopTiming timing,
                IEqualityComparer<TProperty> comparer, bool cancelImmediately)
            {
                if (target is UnityEngine.Object)
                {
                    m_unityTarget = target;
                }
                else
                {
                    m_weakTarget = new WeakReference<TTarget>(target, false);
                }

                m_selector = selector;
                m_timing = timing;
                m_comparer = comparer;
                m_cancelImmediately = cancelImmediately;
            }

            public IOnityAsyncEnumerator<TProperty> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new Enumerator(this, cancellationToken);
            }

            private bool TryGetTarget(out TTarget target)
            {
                target = m_unityTarget;
                if (target != null)
                {
                    return (target as UnityEngine.Object) != null;
                }

                return m_weakTarget != null && m_weakTarget.TryGetTarget(out target);
            }

            private sealed class Enumerator : StreamEnumerator<TProperty>
            {
                private static readonly Func<Enumerator, bool> s_changed = enumerator => enumerator.CaptureChange();

                private readonly ValueChangedDescription<TTarget, TProperty> m_owner;
                private TProperty m_last;
                private bool m_started;
                private bool m_ended;

                internal Enumerator(ValueChangedDescription<TTarget, TProperty> owner, CancellationToken callerToken)
                    : base(callerToken)
                {
                    m_owner = owner;
                }

                protected override bool HasNextStep => !m_ended;

                protected override OnityTask CreateWait(CancellationToken token)
                {
                    if (!m_started)
                    {
                        // The first item is the current value, read when the move completes.
                        m_started = true;
                        if (!m_owner.TryGetTarget(out TTarget target))
                        {
                            m_ended = true;
                        }
                        else
                        {
                            m_last = m_owner.m_selector(target);
                        }

                        return OnityTask.CompletedTask;
                    }

                    return OnityTask.WaitUntil(this, s_changed, m_owner.m_timing, token, m_owner.m_cancelImmediately);
                }

                protected override bool CompleteStep(out TProperty current)
                {
                    current = m_last;
                    return !m_ended;
                }

                private bool CaptureChange()
                {
                    if (!m_owner.TryGetTarget(out TTarget target))
                    {
                        m_ended = true;
                        return true;
                    }

                    TProperty value = m_owner.m_selector(target);
                    if (m_owner.m_comparer.Equals(m_last, value))
                    {
                        return false;
                    }

                    m_last = value;
                    return true;
                }
            }
        }

        /// <summary>
        /// Enumerator of the timing streams: each move waits on one PlayerLoop wait created for the move,
        /// with the token lifetime and cleanup quiescence of <c>EveryUpdate()</c>. A move whose wait succeeds
        /// yields the item that <see cref="CompleteStep"/> produces, or ends the stream when it returns false.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        internal abstract class StreamEnumerator<T> : OnityAsyncEnumeratorBase<T>
        {
            private readonly object m_gate = new object();
            private readonly CancellationToken m_callerToken;
            private readonly Action<OnityAsyncStreamOutcome> m_onQuiescence;
            private OnityAsyncStreamTokenLifetime m_lifetime;
            private OnityTaskCompletionSource m_cleanup;
            private OnityAsyncStreamOutcome m_cleanupOutcome;
            private T m_current;
            private bool m_waitPending;
            private bool m_quiescent;
            private bool m_finalizing;

            protected StreamEnumerator(CancellationToken callerToken)
            {
                m_callerToken = callerToken;
                m_onQuiescence = OnQuiescence;
            }

            /// <summary>False once the stream has no further item to wait for.</summary>
            protected abstract bool HasNextStep { get; }

            /// <summary>Creates the wait of the next move; it validates the thread and session.</summary>
            protected abstract OnityTask CreateWait(CancellationToken token);

            /// <summary>Produces the item after a successful wait; false ends the stream.</summary>
            protected abstract bool CompleteStep(out T current);

            protected override OnityTask<bool> MoveCore()
            {
                if (IsClosing || !HasNextStep)
                {
                    return OnityTask<bool>.FromResult(false);
                }

                if (m_lifetime == null)
                {
                    m_lifetime = new OnityAsyncStreamTokenLifetime();
                    m_lifetime.Initialize(m_callerToken);
                }

                if (IsClosing)
                {
                    return OnityTask<bool>.FromResult(false);
                }

                OnityTask wait = CreateWait(m_lifetime.Token);
                lock (m_gate)
                {
                    m_waitPending = true;
                }

                if (wait.IsCompleted)
                {
                    OnityAsyncStreamOutcome outcome;
                    try
                    {
                        outcome = OnityAsyncStreamOutcome.Read(wait);
                    }
                    catch (Exception exception)
                    {
                        outcome = OnityAsyncStreamOutcome.Failure(exception);
                    }

                    OnWaitObserved();
                    outcome = Settle(outcome);
                    if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                    {
                        return OnityTask<bool>.FromResult(outcome.Value);
                    }

                    var failed = new OnityTaskCompletionSource<bool>();
                    outcome.Publish(failed, false);
                    return failed.Task;
                }

                var output = new OnityTaskCompletionSource<bool>();
                new OnityAsyncStreamCleanupObserver(wait, observed =>
                {
                    OnWaitObserved();
                    OnityAsyncStreamOutcome settled = Settle(observed);
                    settled.Publish(output, settled.Value);
                }).Register();
                return output.Task;
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = m_current;
                return true;
            }

            protected override OnityTask CleanupCore()
            {
                if (m_lifetime == null)
                {
                    return OnityTask.Completed;
                }

                OnityTask task;
                lock (m_gate)
                {
                    if (m_cleanup != null)
                    {
                        return m_cleanup.Task;
                    }

                    m_cleanup = new OnityTaskCompletionSource();
                    task = m_cleanup.Task;
                }

                try
                {
                    OnityTask quiescence = m_lifetime.Close();
                    if (quiescence.IsCompleted)
                    {
                        OnQuiescence(OnityAsyncStreamOutcome.Read(quiescence));
                    }
                    else
                    {
                        new OnityAsyncStreamCleanupObserver(quiescence, m_onQuiescence).Register();
                    }
                }
                catch (Exception exception)
                {
                    OnQuiescence(OnityAsyncStreamOutcome.Failure(exception));
                }

                return task;
            }

            /// <summary>Turns a successful wait into the move result through <see cref="CompleteStep"/>.</summary>
            private OnityAsyncStreamOutcome Settle(OnityAsyncStreamOutcome outcome)
            {
                if (outcome.Status != OnityTaskSourceStatus.Succeeded)
                {
                    return outcome;
                }

                try
                {
                    bool hasItem = CompleteStep(out T current);
                    m_current = current;
                    return OnityAsyncStreamOutcome.Success(hasItem);
                }
                catch (Exception exception)
                {
                    return OnityAsyncStreamOutcome.Failure(exception);
                }
            }

            private void OnWaitObserved()
            {
                lock (m_gate)
                {
                    m_waitPending = false;
                }

                FinishCleanup();
            }

            private void OnQuiescence(OnityAsyncStreamOutcome outcome)
            {
                lock (m_gate)
                {
                    m_quiescent = true;
                    m_cleanupOutcome = outcome;
                }

                FinishCleanup();
            }

            private void FinishCleanup()
            {
                OnityTaskCompletionSource output;
                OnityAsyncStreamOutcome outcome;
                lock (m_gate)
                {
                    if (m_cleanup == null || !m_quiescent || m_waitPending || m_finalizing)
                    {
                        return;
                    }

                    m_finalizing = true;
                    output = m_cleanup;
                    outcome = m_cleanupOutcome;
                }

                try
                {
                    m_lifetime.Dispose();
                }
                catch (Exception exception)
                {
                    outcome = OnityAsyncStreamOutcome.Failure(exception);
                }

                outcome.Publish<bool>(output, true);
            }
        }
    }
}
