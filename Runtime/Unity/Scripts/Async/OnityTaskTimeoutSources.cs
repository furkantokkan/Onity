using System;
using System.Threading;

namespace Onity.Unity.Async
{
    internal enum OnityTimeoutTimerOutcome
    {
        Expired,
        Stopped,
        SessionEnded
    }

    internal interface IOnityTimeoutTimerSink
    {
        void Acknowledge(OnityTaskPlayerLoop.TimeoutEntry entry, int generation,
            OnityTimeoutTimerOutcome outcome, Exception failure);
    }

    internal interface IOnityTimeoutInput<T>
    {
        void Register(Action callback);
        OnityTaskSourceStatus Read(out T result, out Exception fault, out CancellationToken token);
    }

    internal struct OnityTimeoutInput : IOnityTimeoutInput<bool>
    {
        private OnityTask m_task;

        internal OnityTimeoutInput(OnityTask task)
        {
            m_task = task;
        }

        public void Register(Action callback)
        {
            m_task.RegisterWhenAnyObserver(callback);
        }

        public OnityTaskSourceStatus Read(
            out bool result, out Exception fault, out CancellationToken token)
        {
            result = false;
            return m_task.ReadWhenAnyOutcome(out fault, out token);
        }
    }

    internal struct OnityTimeoutInput<T> : IOnityTimeoutInput<T>
    {
        private OnityTask<T> m_task;

        internal OnityTimeoutInput(OnityTask<T> task)
        {
            m_task = task;
        }

        public void Register(Action callback)
        {
            m_task.RegisterWhenAnyObserver(callback);
        }

        public OnityTaskSourceStatus Read(
            out T result, out Exception fault, out CancellationToken token)
        {
            return m_task.ReadWhenAnyOutcome(out result, out fault, out token);
        }
    }

    internal interface IOnityTimeoutOutput<T>
    {
        void PublishProducer(OnityTaskSourceStatus status, T result,
            Exception fault, CancellationToken token);
        void PublishTimeout();
        void PublishSessionEnd(Exception failure);
    }

    // This state is deliberately unpooled. Output consumption cannot release an
    // outstanding producer observer or a timer awaiting its main-thread acknowledgment.
    internal sealed class OnityTimeoutObserver<TInput, T> : IOnityTimeoutTimerSink
        where TInput : struct, IOnityTimeoutInput<T>
    {
        private readonly IOnityTimeoutOutput<T> m_output;
        private readonly Action m_observeProducer;
        private TInput m_input;
        private OnityTaskPlayerLoop.TimeoutEntry m_timer;
        private CancellationTokenSource m_taskCancellation;
        private int m_timerGeneration;
        private int m_winner;
        private int m_producerObserved;

        internal OnityTimeoutObserver(TInput input, IOnityTimeoutOutput<T> output)
        {
            m_input = input;
            m_output = output;
            m_observeProducer = ObserveProducer;
        }

        internal void Start(float seconds, bool useUnscaledTime, Exception initialFault)
        {
            if (initialFault != null)
            {
                PublishFailure(initialFault);
            }
            else if (seconds == 0)
            {
                if (TryWin())
                {
                    m_output.PublishTimeout();
                }
            }
            else
            {
                // Registration invokes no callbacks. Validate and store its identity
                // before claiming the producer; rejected context leaves it untouched.
                OnityTaskPlayerLoop.TimeoutEntry entry = OnityTaskPlayerLoop.RegisterTimeout(
                    seconds, useUnscaledTime, this);
                m_timerGeneration = entry.Generation;
                Volatile.Write(ref m_timer, entry);
            }

            ObserveInput();
        }

        /// <summary>
        /// Starts with a timer measured with <paramref name="delayType"/> at <paramref name="timing"/>. A
        /// timeout first cancels <paramref name="taskCancellation"/>, when given, then publishes.
        /// </summary>
        internal void Start(
            TimeSpan timeout, OnityDelayType delayType, OnityPlayerLoopTiming timing,
            CancellationTokenSource taskCancellation, Exception initialFault)
        {
            m_taskCancellation = taskCancellation;
            if (initialFault != null)
            {
                PublishFailure(initialFault);
            }
            else if (timeout == TimeSpan.Zero)
            {
                if (TryWin())
                {
                    CancelTask();
                    m_output.PublishTimeout();
                }
            }
            else
            {
                // As above: registration invokes no callbacks and validates before the producer is claimed.
                OnityTaskPlayerLoop.TimeoutEntry entry = OnityTaskPlayerLoop.RegisterTimeout(
                    timeout, delayType, timing, this);
                m_timerGeneration = entry.Generation;
                Volatile.Write(ref m_timer, entry);
            }

            ObserveInput();
        }

        private void CancelTask()
        {
            CancellationTokenSource taskCancellation = m_taskCancellation;
            m_taskCancellation = null;
            if (taskCancellation == null)
            {
                return;
            }

            try
            {
                taskCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The owner disposed the source; there is nothing left to cancel.
            }
        }

        private void ObserveInput()
        {
            TInput input = m_input;
            try
            {
                input.Register(m_observeProducer);
            }
            catch (Exception exception)
            {
                // An unknown source may have accepted the callback before throwing.
                // Keep its input for that callback, even when failure already won.
                PublishFailure(exception);
            }
        }

        private bool TryWin()
        {
            return Interlocked.CompareExchange(ref m_winner, 1, 0) == 0;
        }

        private void PublishFailure(Exception exception)
        {
            try
            {
                bool won = TryWin();
                StopTimer();
                if (won)
                {
                    m_output.PublishProducer(OnityTaskSourceStatus.Faulted, default,
                        exception, default);
                }
            }
            finally
            {
                StopTimer();
            }
        }

        private void ObserveProducer()
        {
            if (Interlocked.Exchange(ref m_producerObserved, 1) != 0)
            {
                return;
            }

            bool won = TryWin();
            StopTimer();
            try
            {
                OnityTaskSourceStatus status = m_input.Read(
                    out T result, out Exception fault, out CancellationToken token);
                if (won)
                {
                    m_output.PublishProducer(status, result, fault, token);
                }
            }
            catch (Exception exception)
            {
                if (won)
                {
                    m_output.PublishProducer(OnityTaskSourceStatus.Faulted, default,
                        exception, default);
                }
            }
            finally
            {
                m_input = default;
                StopTimer();
            }
        }

        private void StopTimer()
        {
            OnityTaskPlayerLoop.StopTimeout(Volatile.Read(ref m_timer), m_timerGeneration);
        }

        public void Acknowledge(OnityTaskPlayerLoop.TimeoutEntry entry, int generation,
            OnityTimeoutTimerOutcome outcome, Exception failure)
        {
            if (generation != m_timerGeneration ||
                !ReferenceEquals(Interlocked.CompareExchange(ref m_timer, null, entry), entry))
            {
                return;
            }

            // The owner and observer have both cleared the timer reference before
            // publication can synchronously invoke user continuations.
            if (outcome == OnityTimeoutTimerOutcome.Stopped || !TryWin())
            {
                return;
            }
            if (outcome == OnityTimeoutTimerOutcome.Expired)
            {
                CancelTask();
                m_output.PublishTimeout();
            }
            else
            {
                m_output.PublishSessionEnd(failure);
            }
        }
    }

    internal sealed class OnityTimeoutTaskSource : OnityTaskSourceBase, IOnityTimeoutOutput<bool>
    {
        private OnityTimeoutObserver<OnityTimeoutInput, bool> m_observer;

        private OnityTimeoutTaskSource()
        {
            Reset(default);
        }

        internal static OnityTask Create(OnityTask task, float seconds,
            bool useUnscaledTime, Exception initialFault = null)
        {
            var source = new OnityTimeoutTaskSource();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput, bool>(
                new OnityTimeoutInput(task), source);
            var output = new OnityTask(source);
            source.m_observer.Start(seconds, useUnscaledTime, initialFault);
            return output;
        }

        internal static OnityTask Create(OnityTask task, TimeSpan timeout, OnityDelayType delayType,
            OnityPlayerLoopTiming timing, CancellationTokenSource taskCancellation, Exception initialFault = null)
        {
            var source = new OnityTimeoutTaskSource();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput, bool>(
                new OnityTimeoutInput(task), source);
            var output = new OnityTask(source);
            source.m_observer.Start(timeout, delayType, timing, taskCancellation, initialFault);
            return output;
        }

        public void PublishProducer(OnityTaskSourceStatus status, bool result,
            Exception fault, CancellationToken token)
        {
            if (fault != null || status == OnityTaskSourceStatus.Faulted)
            {
                TrySetException(fault);
            }
            else if (status == OnityTaskSourceStatus.Canceled)
            {
                TrySetCanceled(new OperationCanceledException(token));
            }
            else
            {
                TrySetResult();
            }
        }

        public void PublishTimeout()
        {
            TrySetException(new TimeoutException("The OnityTask timeout elapsed."));
        }

        public void PublishSessionEnd(Exception failure)
        {
            if (failure != null)
            {
                TrySetException(failure);
            }
            else
            {
                TrySetCanceled();
            }
        }

        protected override void ReleaseSource()
        {
        }
    }

    internal sealed class OnityTimeoutTaskSource<T> : OnityTaskSourceBase<T>, IOnityTimeoutOutput<T>
    {
        private OnityTimeoutObserver<OnityTimeoutInput<T>, T> m_observer;

        private OnityTimeoutTaskSource()
        {
            Reset(default);
        }

        internal static OnityTask<T> Create(OnityTask<T> task, float seconds,
            bool useUnscaledTime, Exception initialFault = null)
        {
            var source = new OnityTimeoutTaskSource<T>();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput<T>, T>(
                new OnityTimeoutInput<T>(task), source);
            var output = new OnityTask<T>(source);
            source.m_observer.Start(seconds, useUnscaledTime, initialFault);
            return output;
        }

        internal static OnityTask<T> Create(OnityTask<T> task, TimeSpan timeout, OnityDelayType delayType,
            OnityPlayerLoopTiming timing, CancellationTokenSource taskCancellation, Exception initialFault = null)
        {
            var source = new OnityTimeoutTaskSource<T>();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput<T>, T>(
                new OnityTimeoutInput<T>(task), source);
            var output = new OnityTask<T>(source);
            source.m_observer.Start(timeout, delayType, timing, taskCancellation, initialFault);
            return output;
        }

        public void PublishProducer(OnityTaskSourceStatus status, T result,
            Exception fault, CancellationToken token)
        {
            if (fault != null || status == OnityTaskSourceStatus.Faulted)
            {
                TrySetException(fault);
            }
            else if (status == OnityTaskSourceStatus.Canceled)
            {
                TrySetCanceled(new OperationCanceledException(token));
            }
            else
            {
                TrySetResult(result);
            }
        }

        public void PublishTimeout()
        {
            TrySetException(new TimeoutException("The OnityTask timeout elapsed."));
        }

        public void PublishSessionEnd(Exception failure)
        {
            if (failure != null)
            {
                TrySetException(failure);
            }
            else
            {
                TrySetCanceled();
            }
        }

        protected override void ReleaseSource()
        {
        }
    }

    internal sealed class OnityTimeoutFlagTaskSource : OnityTaskSourceBase<bool>,
        IOnityTimeoutOutput<bool>
    {
        private OnityTimeoutObserver<OnityTimeoutInput, bool> m_observer;

        private OnityTimeoutFlagTaskSource()
        {
            Reset(default);
        }

        internal static OnityTask<bool> Create(OnityTask task, float seconds,
            bool useUnscaledTime, Exception initialFault = null)
        {
            var source = new OnityTimeoutFlagTaskSource();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput, bool>(
                new OnityTimeoutInput(task), source);
            var output = new OnityTask<bool>(source);
            source.m_observer.Start(seconds, useUnscaledTime, initialFault);
            return output;
        }

        internal static OnityTask<bool> Create(OnityTask task, TimeSpan timeout, OnityDelayType delayType,
            OnityPlayerLoopTiming timing, CancellationTokenSource taskCancellation, Exception initialFault = null)
        {
            var source = new OnityTimeoutFlagTaskSource();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput, bool>(
                new OnityTimeoutInput(task), source);
            var output = new OnityTask<bool>(source);
            source.m_observer.Start(timeout, delayType, timing, taskCancellation, initialFault);
            return output;
        }

        internal static OnityTask<bool> FromOutcome(OnityTaskSourceStatus status,
            Exception fault, CancellationToken token)
        {
            if (status == OnityTaskSourceStatus.Succeeded && fault == null)
            {
                return OnityTask<bool>.FromResult(false);
            }
            var source = new OnityTimeoutFlagTaskSource();
            var output = new OnityTask<bool>(source);
            source.PublishProducer(status, false, fault, token);
            return output;
        }

        public void PublishProducer(OnityTaskSourceStatus status, bool result,
            Exception fault, CancellationToken token)
        {
            if (fault != null || status == OnityTaskSourceStatus.Faulted)
            {
                TrySetException(fault);
            }
            else if (status == OnityTaskSourceStatus.Canceled)
            {
                TrySetCanceled(new OperationCanceledException(token));
            }
            else
            {
                TrySetResult(false);
            }
        }

        public void PublishTimeout()
        {
            TrySetResult(true);
        }

        public void PublishSessionEnd(Exception failure)
        {
            if (failure != null)
            {
                TrySetException(failure);
            }
            else
            {
                TrySetCanceled();
            }
        }

        protected override void ReleaseSource()
        {
        }
    }

    internal sealed class OnityTimeoutFlagTaskSource<T> :
        OnityTaskSourceBase<(bool isTimeout, T result)>, IOnityTimeoutOutput<T>
    {
        private OnityTimeoutObserver<OnityTimeoutInput<T>, T> m_observer;

        private OnityTimeoutFlagTaskSource()
        {
            Reset(default);
        }

        internal static OnityTask<(bool isTimeout, T result)> Create(OnityTask<T> task,
            float seconds, bool useUnscaledTime, Exception initialFault = null)
        {
            var source = new OnityTimeoutFlagTaskSource<T>();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput<T>, T>(
                new OnityTimeoutInput<T>(task), source);
            var output = new OnityTask<(bool isTimeout, T result)>(source);
            source.m_observer.Start(seconds, useUnscaledTime, initialFault);
            return output;
        }

        internal static OnityTask<(bool isTimeout, T result)> Create(OnityTask<T> task, TimeSpan timeout, OnityDelayType delayType,
            OnityPlayerLoopTiming timing, CancellationTokenSource taskCancellation, Exception initialFault = null)
        {
            var source = new OnityTimeoutFlagTaskSource<T>();
            source.m_observer = new OnityTimeoutObserver<OnityTimeoutInput<T>, T>(
                new OnityTimeoutInput<T>(task), source);
            var output = new OnityTask<(bool isTimeout, T result)>(source);
            source.m_observer.Start(timeout, delayType, timing, taskCancellation, initialFault);
            return output;
        }

        internal static OnityTask<(bool isTimeout, T result)> FromOutcome(
            OnityTaskSourceStatus status, T result, Exception fault, CancellationToken token)
        {
            if (status == OnityTaskSourceStatus.Succeeded && fault == null)
            {
                return OnityTask<(bool isTimeout, T result)>.FromResult((false, result));
            }
            var source = new OnityTimeoutFlagTaskSource<T>();
            var output = new OnityTask<(bool isTimeout, T result)>(source);
            source.PublishProducer(status, result, fault, token);
            return output;
        }

        public void PublishProducer(OnityTaskSourceStatus status, T result,
            Exception fault, CancellationToken token)
        {
            if (fault != null || status == OnityTaskSourceStatus.Faulted)
            {
                TrySetException(fault);
            }
            else if (status == OnityTaskSourceStatus.Canceled)
            {
                TrySetCanceled(new OperationCanceledException(token));
            }
            else
            {
                TrySetResult((false, result));
            }
        }

        public void PublishTimeout()
        {
            TrySetResult((true, default));
        }

        public void PublishSessionEnd(Exception failure)
        {
            if (failure != null)
            {
                TrySetException(failure);
            }
            else
            {
                TrySetCanceled();
            }
        }

        protected override void ReleaseSource()
        {
        }
    }
}
