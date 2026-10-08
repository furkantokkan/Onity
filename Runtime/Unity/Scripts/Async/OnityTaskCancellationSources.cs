using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // Unpooled state stays alive independently of output consumption. In particular,
    // an external winner must not discard the producer's eventual fault observer.
    internal sealed class OnityCancellationRegistration
    {
        private static readonly Action<object> s_cancel = state =>
            ((OnityCancellationRegistration)state).Cancel();

        private readonly object m_gate = new object();
        private Action m_cancel;
        private CancellationTokenRegistration m_registration;
        private bool m_initializing = true;
        private bool m_cleanupRequested;
        private bool m_detached;
        private int m_activeCallbacks;
        private int m_winner;
        private int m_producerObserved;

        internal OnityCancellationRegistration(Action cancel)
        {
            m_cancel = cancel;
        }

        internal bool TryWin()
        {
            return Interlocked.CompareExchange(ref m_winner, 1, 0) == 0;
        }

        internal bool TryObserveProducer()
        {
            return Interlocked.Exchange(ref m_producerObserved, 1) == 0;
        }

        internal void Register(CancellationToken token)
        {
            CancellationTokenRegistration registration;
            if (ExecutionContext.IsFlowSuppressed())
            {
                registration = token.Register(s_cancel, this, useSynchronizationContext: false);
            }
            else
            {
                AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                try
                {
                    registration = token.Register(s_cancel, this, useSynchronizationContext: false);
                }
                finally
                {
                    flow.Undo();
                }
            }

            lock (m_gate)
            {
                m_registration = registration;
            }
        }

        internal void FinishInitialization()
        {
            CancellationTokenRegistration registration;
            bool dispose;
            lock (m_gate)
            {
                m_initializing = false;
                dispose = Detach(out registration);
            }

            if (dispose)
            {
                registration.Dispose();
            }
        }

        internal void RequestCleanup()
        {
            CancellationTokenRegistration registration;
            bool dispose;
            lock (m_gate)
            {
                m_cleanupRequested = true;
                dispose = Detach(out registration);
            }

            if (dispose)
            {
                registration.Dispose();
            }
        }

        private void Cancel()
        {
            Action cancel;
            lock (m_gate)
            {
                m_activeCallbacks++;
                cancel = m_cancel;
            }

            try
            {
                cancel?.Invoke();
            }
            finally
            {
                CancellationTokenRegistration registration;
                bool dispose;
                lock (m_gate)
                {
                    m_activeCallbacks--;
                    dispose = Detach(out registration);
                }

                if (dispose)
                {
                    registration.Dispose();
                }
            }
        }

        // Called only under m_gate. Disposal and native publication never hold it.
        private bool Detach(out CancellationTokenRegistration registration)
        {
            registration = default;
            if (!m_cleanupRequested || m_initializing || m_activeCallbacks != 0 || m_detached)
            {
                return false;
            }

            m_detached = true;
            registration = m_registration;
            m_registration = default;
            m_cancel = null;
            return true;
        }
    }

    internal sealed class OnityExternalCancellationSource : OnityTaskSourceBase
    {
        private OnityTask m_input;
        private readonly CancellationToken m_token;
        private readonly OnityCancellationRegistration m_registration;

        private OnityExternalCancellationSource(OnityTask input, CancellationToken token)
        {
            Reset(default);
            m_input = input;
            m_token = token;
            m_registration = new OnityCancellationRegistration(Cancel);
        }

        internal static OnityTask Create(
            OnityTask input, CancellationToken token, Exception initialFault = null)
        {
            var source = new OnityExternalCancellationSource(input, token);
            var task = new OnityTask(source);
            source.Start(initialFault);
            return task;
        }

        private void Start(Exception initialFault)
        {
            try
            {
                if (initialFault != null)
                {
                    FailRegistration(initialFault);
                }

                if (m_token.IsCancellationRequested)
                {
                    Cancel();
                }

                try
                {
                    m_registration.Register(m_token);
                }
                catch (Exception exception)
                {
                    FailRegistration(exception);
                }

                try
                {
                    m_input.RegisterWhenAnyObserver(ObserveProducer);
                }
                catch (Exception exception)
                {
                    FailRegistration(exception);
                }
            }
            finally
            {
                m_registration.FinishInitialization();
            }
        }

        private void Cancel()
        {
            if (m_registration.TryWin())
            {
                try
                {
                    TrySetCanceled(new OperationCanceledException(m_token));
                }
                finally
                {
                    m_registration.RequestCleanup();
                }
            }
        }

        private void FailRegistration(Exception exception)
        {
            if (m_registration.TryWin())
            {
                TrySetException(exception);
                m_registration.RequestCleanup();
            }
        }

        private void ObserveProducer()
        {
            if (!m_registration.TryObserveProducer())
            {
                return;
            }

            bool won = m_registration.TryWin();
            try
            {
                OnityTaskSourceStatus status = m_input.ReadWhenAnyOutcome(
                    out Exception fault, out CancellationToken token);
                if (won)
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
            }
            catch (Exception exception)
            {
                if (won)
                {
                    TrySetException(exception);
                }
            }
            finally
            {
                m_input = default;
                m_registration.RequestCleanup();
            }
        }

        protected override void ReleaseSource()
        {
            // The unpooled producer observer remains valid after output release.
        }
    }

    internal sealed class OnityExternalCancellationSource<T> : OnityTaskSourceBase<T>
    {
        private OnityTask<T> m_input;
        private readonly CancellationToken m_token;
        private readonly OnityCancellationRegistration m_registration;

        private OnityExternalCancellationSource(OnityTask<T> input, CancellationToken token)
        {
            Reset(default);
            m_input = input;
            m_token = token;
            m_registration = new OnityCancellationRegistration(Cancel);
        }

        internal static OnityTask<T> Create(
            OnityTask<T> input, CancellationToken token, Exception initialFault = null)
        {
            var source = new OnityExternalCancellationSource<T>(input, token);
            var task = new OnityTask<T>(source);
            source.Start(initialFault);
            return task;
        }

        private void Start(Exception initialFault)
        {
            try
            {
                if (initialFault != null)
                {
                    FailRegistration(initialFault);
                }

                if (m_token.IsCancellationRequested)
                {
                    Cancel();
                }

                try
                {
                    m_registration.Register(m_token);
                }
                catch (Exception exception)
                {
                    FailRegistration(exception);
                }

                try
                {
                    m_input.RegisterWhenAnyObserver(ObserveProducer);
                }
                catch (Exception exception)
                {
                    FailRegistration(exception);
                }
            }
            finally
            {
                m_registration.FinishInitialization();
            }
        }

        private void Cancel()
        {
            if (m_registration.TryWin())
            {
                try
                {
                    TrySetCanceled(new OperationCanceledException(m_token));
                }
                finally
                {
                    m_registration.RequestCleanup();
                }
            }
        }

        private void FailRegistration(Exception exception)
        {
            if (m_registration.TryWin())
            {
                TrySetException(exception);
                m_registration.RequestCleanup();
            }
        }

        private void ObserveProducer()
        {
            if (!m_registration.TryObserveProducer())
            {
                return;
            }

            bool won = m_registration.TryWin();
            try
            {
                OnityTaskSourceStatus status = m_input.ReadWhenAnyOutcome(
                    out T result, out Exception fault, out CancellationToken token);
                if (won)
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
            }
            catch (Exception exception)
            {
                if (won)
                {
                    TrySetException(exception);
                }
            }
            finally
            {
                m_input = default;
                m_registration.RequestCleanup();
            }
        }

        protected override void ReleaseSource()
        {
            // The unpooled producer observer remains valid after output release.
        }
    }

    internal sealed class OnityCancellationSuppressionSource : OnityTaskSourceBase<bool>
    {
        private OnityTask m_input;
        private int m_winner;
        private int m_producerObserved;

        private OnityCancellationSuppressionSource(OnityTask input)
        {
            Reset(default);
            m_input = input;
        }

        internal static OnityTask<bool> Create(OnityTask input)
        {
            Exception initialFault = null;
            try
            {
                if (input.IsCompleted)
                {
                    OnityTaskSourceStatus status = input.ReadWhenAnyOutcome(
                        out Exception fault, out CancellationToken ignored);
                    if (fault == null && status != OnityTaskSourceStatus.Faulted)
                    {
                        return OnityTask<bool>.FromResult(status == OnityTaskSourceStatus.Canceled);
                    }

                    var failed = new OnityCancellationSuppressionSource(default);
                    failed.TrySetException(fault);
                    return new OnityTask<bool>(failed);
                }
            }
            catch (Exception exception)
            {
                initialFault = exception;
            }

            var source = new OnityCancellationSuppressionSource(input);
            var task = new OnityTask<bool>(source);
            if (initialFault != null)
            {
                source.m_winner = 1;
                source.TrySetException(initialFault);
            }

            try
            {
                input.RegisterWhenAnyObserver(source.ObserveProducer);
            }
            catch (Exception exception)
            {
                if (Interlocked.CompareExchange(ref source.m_winner, 1, 0) == 0)
                {
                    source.TrySetException(exception);
                }
            }

            return task;
        }

        private void ObserveProducer()
        {
            if (Interlocked.Exchange(ref m_producerObserved, 1) != 0)
            {
                return;
            }

            bool won = Interlocked.CompareExchange(ref m_winner, 1, 0) == 0;
            try
            {
                OnityTaskSourceStatus status = m_input.ReadWhenAnyOutcome(
                    out Exception fault, out CancellationToken ignored);
                if (won)
                {
                    if (fault != null || status == OnityTaskSourceStatus.Faulted)
                    {
                        TrySetException(fault);
                    }
                    else
                    {
                        TrySetResult(status == OnityTaskSourceStatus.Canceled);
                    }
                }
            }
            catch (Exception exception)
            {
                if (won)
                {
                    TrySetException(exception);
                }
            }
            finally
            {
                m_input = default;
            }
        }

        protected override void ReleaseSource()
        {
        }
    }

    internal sealed class OnityCancellationSuppressionSource<T>
        : OnityTaskSourceBase<(bool isCanceled, T result)>
    {
        private OnityTask<T> m_input;
        private int m_winner;
        private int m_producerObserved;

        private OnityCancellationSuppressionSource(OnityTask<T> input)
        {
            Reset(default);
            m_input = input;
        }

        internal static OnityTask<(bool isCanceled, T result)> Create(OnityTask<T> input)
        {
            Exception initialFault = null;
            try
            {
                if (input.IsCompleted)
                {
                    OnityTaskSourceStatus status = input.ReadWhenAnyOutcome(
                        out T result, out Exception fault, out CancellationToken ignored);
                    if (fault == null && status != OnityTaskSourceStatus.Faulted)
                    {
                        bool canceled = status == OnityTaskSourceStatus.Canceled;
                        return OnityTask<(bool isCanceled, T result)>.FromResult(
                            (canceled, canceled ? default : result));
                    }

                    var failed = new OnityCancellationSuppressionSource<T>(default);
                    failed.TrySetException(fault);
                    return new OnityTask<(bool isCanceled, T result)>(failed);
                }
            }
            catch (Exception exception)
            {
                initialFault = exception;
            }

            var source = new OnityCancellationSuppressionSource<T>(input);
            var task = new OnityTask<(bool isCanceled, T result)>(source);
            if (initialFault != null)
            {
                source.m_winner = 1;
                source.TrySetException(initialFault);
            }

            try
            {
                input.RegisterWhenAnyObserver(source.ObserveProducer);
            }
            catch (Exception exception)
            {
                if (Interlocked.CompareExchange(ref source.m_winner, 1, 0) == 0)
                {
                    source.TrySetException(exception);
                }
            }

            return task;
        }

        private void ObserveProducer()
        {
            if (Interlocked.Exchange(ref m_producerObserved, 1) != 0)
            {
                return;
            }

            bool won = Interlocked.CompareExchange(ref m_winner, 1, 0) == 0;
            try
            {
                OnityTaskSourceStatus status = m_input.ReadWhenAnyOutcome(
                    out T result, out Exception fault, out CancellationToken ignored);
                if (won)
                {
                    if (fault != null || status == OnityTaskSourceStatus.Faulted)
                    {
                        TrySetException(fault);
                    }
                    else
                    {
                        bool canceled = status == OnityTaskSourceStatus.Canceled;
                        TrySetResult((canceled, canceled ? default : result));
                    }
                }
            }
            catch (Exception exception)
            {
                if (won)
                {
                    TrySetException(exception);
                }
            }
            finally
            {
                m_input = default;
            }
        }

        protected override void ReleaseSource()
        {
        }
    }
}
