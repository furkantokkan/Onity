using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>Result of one poll of a PlayerLoop wait.</summary>
    internal enum OnityLoopPollOutcome
    {
        Pending,
        Completed,
        Canceled
    }

    /// <summary>
    /// Clock of the timed waits and timers, with UniTask's <c>DelayType</c> semantics: the frame clocks
    /// accumulate the frame delta at each drain from the frame after the start; the real-time clock
    /// compares <see cref="Stopwatch"/> timestamps.
    /// </summary>
    internal struct OnityLoopClock
    {
        private OnityDelayType m_type;
        private float m_durationSeconds;
        private float m_elapsed;
        private int m_initialFrame;
        private long m_startTimestamp;
        private long m_durationTimestamps;

        /// <summary>Starts the clock; any thread (a worker start counts the current frame).</summary>
        internal void Start(TimeSpan duration, OnityDelayType type)
        {
            m_type = type;
            m_elapsed = 0f;
            if (type == OnityDelayType.Realtime)
            {
                m_startTimestamp = Stopwatch.GetTimestamp();
                m_durationTimestamps = (long)(duration.TotalSeconds * Stopwatch.Frequency);
            }
            else
            {
                m_durationSeconds = (float)duration.TotalSeconds;
                m_initialFrame = OnityTaskPlayerLoop.IsMainThread ? Time.frameCount : -1;
            }
        }

        /// <summary>Advances the clock at one drain; true once the duration elapsed. Main thread.</summary>
        internal bool Advance()
        {
            if (m_type == OnityDelayType.Realtime)
            {
                return Stopwatch.GetTimestamp() - m_startTimestamp >= m_durationTimestamps;
            }

            if (m_elapsed == 0f && m_initialFrame == Time.frameCount)
            {
                // The frame that started the clock does not count.
                return false;
            }

            m_elapsed += m_type == OnityDelayType.DeltaTime ? Time.deltaTime : Time.unscaledDeltaTime;
            return m_elapsed >= m_durationSeconds;
        }
    }

    /// <summary>
    /// Base of the untyped pooled waits polled at each drain of one timing. A cycle starts on the main
    /// thread, which queues it with its version. A flag-only cancellation is published by the next Update
    /// drain whatever the timing; an immediate one by the canceling thread through the version-checked
    /// claim. Every main-thread publication disposes the registration first, which waits for an immediate
    /// cancellation in flight, so the registration only ever fires inside its own pending cycle.
    /// </summary>
    internal abstract class OnityLoopWaitSource : OnityTaskSourceBase, IOnityLoopWait
    {
        private static readonly Action<object> s_flag = FlagFromToken;
        private static readonly Action<object> s_immediate = CancelImmediatelyFromToken;

        private CancellationTokenRegistration m_registration;
        private CancellationToken m_token;
        private int m_canceled;

        public bool IsCancellationFlagged => !CancellationCompletes && Volatile.Read(ref m_canceled) != 0;

        /// <summary>True when the token's cancellation completes the wait successfully.</summary>
        private protected virtual bool CancellationCompletes => false;

        public bool IsAwaitingPublication(int version)
        {
            return Version == version && IsPending;
        }

        /// <summary>
        /// Starts a cycle and queues it. The caller has validated the timing and the main-thread session
        /// context and has prepared the derived fields.
        /// </summary>
        private protected OnityTask StartCycle(
            OnityPlayerLoopTiming timing, CancellationToken token, bool cancelImmediately)
        {
            Reset(default);
            m_canceled = 0;
            m_token = token;
            int version = Version;
            try
            {
                if (token.CanBeCanceled)
                {
                    m_registration = OnityLoopWaitRegistration.Register(
                        token, cancelImmediately ? s_immediate : s_flag, this);
                }

                OnityTaskPlayerLoop.AddLoopWait(timing, this, version);
            }
            catch
            {
                m_registration.Dispose();
                m_registration = default;
                m_token = default;
                ReleaseSource();
                throw;
            }

            return new OnityTask(this, version);
        }

        /// <summary>Polls the wait on the main thread.</summary>
        private protected abstract OnityLoopPollOutcome PollCore();

        bool IOnityLoopWait.Poll(int version)
        {
            if (Volatile.Read(ref m_canceled) != 0)
            {
                if (CancellationCompletes)
                {
                    PublishSuccess(version);
                }
                else
                {
                    PublishCanceled(version);
                }

                return false;
            }

            OnityLoopPollOutcome outcome;
            try
            {
                outcome = PollCore();
            }
            catch (Exception exception)
            {
                PublishFailure(version, exception);
                return false;
            }

            if (outcome == OnityLoopPollOutcome.Pending)
            {
                return true;
            }

            if (outcome == OnityLoopPollOutcome.Completed)
            {
                PublishSuccess(version);
            }
            else
            {
                PublishCanceled(version);
            }

            return false;
        }

        void IOnityLoopWait.PublishCancellation(int version)
        {
            PublishCanceled(version);
        }

        void IOnityLoopWait.Retire(int version, Exception failure)
        {
            if (failure != null)
            {
                PublishFailure(version, failure);
            }
            else
            {
                PublishCanceled(version);
            }
        }

        private void PublishSuccess(int version)
        {
            EndRegistration();
            TrySetOwnedResult(version);
            // Do not access fields here: publication may consume and re-rent this object.
        }

        private void PublishCanceled(int version)
        {
            CancellationToken token = EndRegistration();
            TrySetCanceled(new OperationCanceledException(token), version);
        }

        private void PublishFailure(int version, Exception failure)
        {
            EndRegistration();
            TrySetException(failure, version);
        }

        private CancellationToken EndRegistration()
        {
            CancellationToken token = m_token;
            if (token.CanBeCanceled)
            {
                m_registration.Dispose();
                m_registration = default;
                m_token = default;
            }

            return token;
        }

        private static void FlagFromToken(object state)
        {
            OnityLoopWaitSource source = (OnityLoopWaitSource)state;
            Volatile.Write(ref source.m_canceled, 1);
            if (!source.CancellationCompletes)
            {
                OnityTaskPlayerLoop.NotifyCancellationRequested();
            }
        }

        private static void CancelImmediatelyFromToken(object state)
        {
            OnityLoopWaitSource source = (OnityLoopWaitSource)state;
            int version = source.Version;
            if (source.CancellationCompletes)
            {
                source.TrySetOwnedResult(version);
            }
            else
            {
                source.TrySetCanceled(new OperationCanceledException(source.m_token), version);
            }
        }
    }

    /// <summary>Typed form of <see cref="OnityLoopWaitSource"/>.</summary>
    /// <typeparam name="TResult">Result type.</typeparam>
    internal abstract class OnityLoopWaitSource<TResult> : OnityTaskSourceBase<TResult>, IOnityLoopWait
    {
        private static readonly Action<object> s_flag = FlagFromToken;
        private static readonly Action<object> s_immediate = CancelImmediatelyFromToken;

        private CancellationTokenRegistration m_registration;
        private CancellationToken m_token;
        private int m_canceled;

        public bool IsCancellationFlagged => Volatile.Read(ref m_canceled) != 0;

        public bool IsAwaitingPublication(int version)
        {
            return Version == version && IsPending;
        }

        /// <summary>See <see cref="OnityLoopWaitSource"/>.</summary>
        private protected OnityTask<TResult> StartCycle(
            OnityPlayerLoopTiming timing, CancellationToken token, bool cancelImmediately)
        {
            Reset(default);
            m_canceled = 0;
            m_token = token;
            int version = Version;
            try
            {
                if (token.CanBeCanceled)
                {
                    m_registration = OnityLoopWaitRegistration.Register(
                        token, cancelImmediately ? s_immediate : s_flag, this);
                }

                OnityTaskPlayerLoop.AddLoopWait(timing, this, version);
            }
            catch
            {
                m_registration.Dispose();
                m_registration = default;
                m_token = default;
                ReleaseSource();
                throw;
            }

            return new OnityTask<TResult>(this, version);
        }

        /// <summary>Polls the wait on the main thread; sets the result when it completes.</summary>
        private protected abstract OnityLoopPollOutcome PollCore(out TResult result);

        bool IOnityLoopWait.Poll(int version)
        {
            if (Volatile.Read(ref m_canceled) != 0)
            {
                PublishCanceled(version);
                return false;
            }

            OnityLoopPollOutcome outcome;
            TResult result;
            try
            {
                outcome = PollCore(out result);
            }
            catch (Exception exception)
            {
                EndRegistration();
                TrySetException(exception, version);
                return false;
            }

            if (outcome == OnityLoopPollOutcome.Pending)
            {
                return true;
            }

            if (outcome == OnityLoopPollOutcome.Completed)
            {
                EndRegistration();
                TrySetResult(result, version);
            }
            else
            {
                PublishCanceled(version);
            }

            return false;
        }

        void IOnityLoopWait.PublishCancellation(int version)
        {
            PublishCanceled(version);
        }

        void IOnityLoopWait.Retire(int version, Exception failure)
        {
            if (failure != null)
            {
                EndRegistration();
                TrySetException(failure, version);
            }
            else
            {
                PublishCanceled(version);
            }
        }

        private void PublishCanceled(int version)
        {
            CancellationToken token = EndRegistration();
            TrySetCanceled(new OperationCanceledException(token), version);
        }

        private CancellationToken EndRegistration()
        {
            CancellationToken token = m_token;
            if (token.CanBeCanceled)
            {
                m_registration.Dispose();
                m_registration = default;
                m_token = default;
            }

            return token;
        }

        private static void FlagFromToken(object state)
        {
            Volatile.Write(ref ((OnityLoopWaitSource<TResult>)state).m_canceled, 1);
            OnityTaskPlayerLoop.NotifyCancellationRequested();
        }

        private static void CancelImmediatelyFromToken(object state)
        {
            OnityLoopWaitSource<TResult> source = (OnityLoopWaitSource<TResult>)state;
            source.TrySetCanceled(new OperationCanceledException(source.m_token), source.Version);
        }
    }

    /// <summary>
    /// Adapter that runs a token-less predicate through the state form of the predicate wait. It lives
    /// outside <see cref="OnityTask"/>, which keeps no static initializers.
    /// </summary>
    internal static class OnityLoopPredicates
    {
        internal static readonly Func<Func<bool>, bool> Invoke = predicate => predicate();
    }

    /// <summary>Registers a wait's token callback without flowing the execution context.</summary>
    internal static class OnityLoopWaitRegistration
    {
        internal static CancellationTokenRegistration Register(
            CancellationToken token, Action<object> callback, object state)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return token.Register(callback, state, false);
            }

            AsyncFlowControl flow = ExecutionContext.SuppressFlow();
            try
            {
                return token.Register(callback, state, false);
            }
            finally
            {
                flow.Undo();
            }
        }
    }

    /// <summary>A timed delay polled at one timing (<c>Delay(TimeSpan, OnityDelayType, ...)</c>).</summary>
    internal sealed class OnityLoopDelaySource : OnityLoopWaitSource, IOnityPooledRunner<OnityLoopDelaySource>
    {
        private static OnityRunnerPool<OnityLoopDelaySource> s_pool;

        private OnityLoopDelaySource m_nextPooled;
        private OnityLoopClock m_clock;

        ref OnityLoopDelaySource IOnityPooledRunner<OnityLoopDelaySource>.NextPooled => ref m_nextPooled;

        internal static OnityTask Start(
            TimeSpan delay, OnityDelayType delayType, OnityPlayerLoopTiming timing,
            CancellationToken token, bool cancelImmediately)
        {
            if (!s_pool.TryPop(out OnityLoopDelaySource source))
            {
                source = new OnityLoopDelaySource();
            }

            source.m_clock.Start(delay, delayType);
            return source.StartCycle(timing, token, cancelImmediately);
        }

        private protected override OnityLoopPollOutcome PollCore()
        {
            return m_clock.Advance() ? OnityLoopPollOutcome.Completed : OnityLoopPollOutcome.Pending;
        }

        protected override void ReleaseSource()
        {
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }

    /// <summary>
    /// A predicate polled at one timing (<c>WaitUntil</c>/<c>WaitWhile</c> with a timing or a state). A
    /// predicate exception faults the wait.
    /// </summary>
    /// <typeparam name="TState">State passed to the predicate.</typeparam>
    internal sealed class OnityLoopPredicateSource<TState> :
        OnityLoopWaitSource, IOnityPooledRunner<OnityLoopPredicateSource<TState>>
    {
        private static OnityRunnerPool<OnityLoopPredicateSource<TState>> s_pool;

        private OnityLoopPredicateSource<TState> m_nextPooled;
        private TState m_state;
        private Func<TState, bool> m_predicate;
        private bool m_completeWhen;

        ref OnityLoopPredicateSource<TState> IOnityPooledRunner<OnityLoopPredicateSource<TState>>.NextPooled =>
            ref m_nextPooled;

        /// <param name="completeWhen">The predicate value that completes the wait: true for WaitUntil.</param>
        internal static OnityTask Start(
            TState state, Func<TState, bool> predicate, bool completeWhen, OnityPlayerLoopTiming timing,
            CancellationToken token, bool cancelImmediately)
        {
            if (!s_pool.TryPop(out OnityLoopPredicateSource<TState> source))
            {
                source = new OnityLoopPredicateSource<TState>();
            }

            source.m_state = state;
            source.m_predicate = predicate;
            source.m_completeWhen = completeWhen;
            return source.StartCycle(timing, token, cancelImmediately);
        }

        private protected override OnityLoopPollOutcome PollCore()
        {
            return m_predicate(m_state) == m_completeWhen ? OnityLoopPollOutcome.Completed : OnityLoopPollOutcome.Pending;
        }

        protected override void ReleaseSource()
        {
            m_state = default;
            m_predicate = null;
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }

    /// <summary>
    /// Completes successfully at the first drain of its timing after a token is canceled, or on the
    /// canceling thread when started with immediate completion (<c>WaitUntilCanceled</c>).
    /// </summary>
    internal sealed class OnityLoopCanceledSource :
        OnityLoopWaitSource, IOnityPooledRunner<OnityLoopCanceledSource>
    {
        private static OnityRunnerPool<OnityLoopCanceledSource> s_pool;

        private OnityLoopCanceledSource m_nextPooled;

        ref OnityLoopCanceledSource IOnityPooledRunner<OnityLoopCanceledSource>.NextPooled => ref m_nextPooled;

        private protected override bool CancellationCompletes => true;

        internal static OnityTask Start(
            CancellationToken watched, OnityPlayerLoopTiming timing, bool completeImmediately)
        {
            if (!s_pool.TryPop(out OnityLoopCanceledSource source))
            {
                source = new OnityLoopCanceledSource();
            }

            return source.StartCycle(timing, watched, completeImmediately);
        }

        private protected override OnityLoopPollOutcome PollCore()
        {
            // The cancellation flag, checked before each poll, completes the wait.
            return OnityLoopPollOutcome.Pending;
        }

        protected override void ReleaseSource()
        {
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }

    /// <summary>
    /// Polls a monitored value at one timing and completes with the first value that differs from the
    /// initial one (<c>WaitUntilValueChanged</c>). A destroyed Unity object or a collected plain target
    /// cancels the wait; plain targets are held weakly, Unity objects strongly, as in UniTask.
    /// </summary>
    /// <typeparam name="TTarget">Monitored object type.</typeparam>
    /// <typeparam name="TValue">Monitored value type.</typeparam>
    internal sealed class OnityLoopValueChangedSource<TTarget, TValue> :
        OnityLoopWaitSource<TValue>, IOnityPooledRunner<OnityLoopValueChangedSource<TTarget, TValue>>
        where TTarget : class
    {
        private static OnityRunnerPool<OnityLoopValueChangedSource<TTarget, TValue>> s_pool;

        private OnityLoopValueChangedSource<TTarget, TValue> m_nextPooled;
        private TTarget m_unityTarget;
        private WeakReference<TTarget> m_weakTarget;
        private Func<TTarget, TValue> m_monitor;
        private IEqualityComparer<TValue> m_comparer;
        private TValue m_current;

        ref OnityLoopValueChangedSource<TTarget, TValue>
            IOnityPooledRunner<OnityLoopValueChangedSource<TTarget, TValue>>.NextPooled => ref m_nextPooled;

        internal static OnityTask<TValue> Start(
            TTarget target, Func<TTarget, TValue> monitor, TValue current, IEqualityComparer<TValue> comparer,
            OnityPlayerLoopTiming timing, CancellationToken token, bool cancelImmediately)
        {
            if (!s_pool.TryPop(out OnityLoopValueChangedSource<TTarget, TValue> source))
            {
                source = new OnityLoopValueChangedSource<TTarget, TValue>();
            }

            if (target is UnityEngine.Object)
            {
                source.m_unityTarget = target;
            }
            else if (source.m_weakTarget == null)
            {
                source.m_weakTarget = new WeakReference<TTarget>(target, false);
            }
            else
            {
                source.m_weakTarget.SetTarget(target);
            }

            source.m_monitor = monitor;
            source.m_comparer = comparer;
            source.m_current = current;
            return source.StartCycle(timing, token, cancelImmediately);
        }

        private protected override OnityLoopPollOutcome PollCore(out TValue result)
        {
            result = default;
            TTarget target = m_unityTarget;
            if (target != null)
            {
                if ((target as UnityEngine.Object) == null)
                {
                    return OnityLoopPollOutcome.Canceled;
                }
            }
            else if (m_weakTarget == null || !m_weakTarget.TryGetTarget(out target))
            {
                return OnityLoopPollOutcome.Canceled;
            }

            TValue next = m_monitor(target);
            if (m_comparer.Equals(m_current, next))
            {
                return OnityLoopPollOutcome.Pending;
            }

            result = next;
            return OnityLoopPollOutcome.Completed;
        }

        protected override void ReleaseSource()
        {
            m_unityTarget = null;
            m_weakTarget?.SetTarget(null);
            m_monitor = null;
            m_comparer = null;
            m_current = default;
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }
}
