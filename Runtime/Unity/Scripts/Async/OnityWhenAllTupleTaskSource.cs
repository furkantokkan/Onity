using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Indexed view of the inputs a composition source holds, used to reject a single-consumer
    /// task passed twice before any input is claimed.
    /// </summary>
    internal interface IOnityCompositionInputs
    {
        bool TryGetInputIdentity(int index, out OnityWhenAnyInputIdentity identity);
    }

    /// <summary>
    /// Pooled native source shared by the generated tuple <c>WhenAll</c> overloads. It observes every
    /// input once through the same observer and outcome readers as <c>WhenAny</c>, waits for all of
    /// them, and publishes the first fault in argument order ahead of the first cancellation in
    /// argument order, or the result tuple.
    /// </summary>
    /// <remarks>
    /// The source returns to its pool only after the output was released and every input was
    /// observed. A registration failure settles that input as a fault and keeps the instance out of
    /// the pool, so a callback that a custom source accepted before throwing can still consume it.
    /// </remarks>
    /// <typeparam name="TOutput">Result tuple type.</typeparam>
    internal abstract class OnityWhenAllTupleTaskSourceBase<TOutput> :
        OnityTaskSourceBase<TOutput>,
        IOnityCompositionInputs
    {
        private readonly Action[] m_callbacks;
        private readonly Exception[] m_faults;
        private readonly CancellationToken[] m_cancellationTokens;
        private readonly bool[] m_canceled;
        private readonly bool[] m_accounted;
        private readonly bool[] m_observed;
        private readonly int m_count;
        private int m_remaining;
        private int m_activeCallbacks;
        private bool m_registrationPinned;
        private bool m_outputReleased;
        private bool m_poolable;
        private bool m_returned;

        protected OnityWhenAllTupleTaskSourceBase(int count)
        {
            m_count = count;
            m_callbacks = new Action[count];
            m_faults = new Exception[count];
            m_cancellationTokens = new CancellationToken[count];
            m_canceled = new bool[count];
            m_accounted = new bool[count];
            m_observed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                m_callbacks[i] = () => CompleteInput(index);
            }
        }

        public abstract bool TryGetInputIdentity(int index, out OnityWhenAnyInputIdentity identity);

        /// <summary>Starts a new output cycle for the inputs the subclass just stored.</summary>
        protected void Initialize()
        {
            Reset(default);
            m_remaining = m_count;
            m_activeCallbacks = 0;
            m_registrationPinned = true;
            m_outputReleased = false;
            m_poolable = true;
            m_returned = false;
        }

        /// <summary>Registers every input in argument order; already completed inputs settle inline.</summary>
        internal void Start()
        {
            try
            {
                for (int i = 0; i < m_count; i++)
                {
                    try
                    {
                        RegisterInput(i, m_callbacks[i]);
                    }
                    catch (Exception exception)
                    {
                        CompleteRegistrationFailure(i, exception);
                    }
                }
            }
            finally
            {
                lock (this)
                {
                    m_registrationPinned = false;
                    ReturnIfReady();
                }
            }
        }

        protected override void ReleaseSource()
        {
            lock (this)
            {
                // Every caller reached this through the compare-and-swap that claimed the release and
                // retired the token version, so the output is already unreachable for stale values.
                m_outputReleased = true;
                ReturnIfReady();
            }
        }

        protected abstract void RegisterInput(int index, Action callback);

        /// <summary>Reads, and so consumes, one terminal input, storing a successful result.</summary>
        protected abstract OnityTaskSourceStatus ReadInput(
            int index, out Exception fault, out CancellationToken cancellationToken);

        protected abstract TOutput CreateResult();

        /// <summary>Drops the reference to one observed input.</summary>
        protected abstract void ClearInput(int index);

        /// <summary>Drops every input and stored result before the source is pooled.</summary>
        protected abstract void ClearInputs();

        protected abstract void ReturnToPool();

        private void CompleteInput(int index)
        {
            lock (this)
            {
                if (m_observed[index])
                {
                    return;
                }

                m_observed[index] = true;
                m_activeCallbacks++;
            }

            try
            {
                Exception fault;
                CancellationToken cancellationToken;
                OnityTaskSourceStatus status;
                try
                {
                    status = ReadInput(index, out fault, out cancellationToken);
                    if (status == OnityTaskSourceStatus.Pending
                        || (status == OnityTaskSourceStatus.Faulted && fault == null))
                    {
                        status = OnityTaskSourceStatus.Faulted;
                        fault = new InvalidOperationException("WhenAll input did not provide a terminal outcome.");
                    }
                }
                catch (Exception exception)
                {
                    status = OnityTaskSourceStatus.Faulted;
                    fault = exception;
                    cancellationToken = default;
                }

                Settle(index, status, fault, cancellationToken);
            }
            finally
            {
                lock (this)
                {
                    ClearInput(index);
                    m_activeCallbacks--;
                    ReturnIfReady();
                }
            }
        }

        private void CompleteRegistrationFailure(int index, Exception exception)
        {
            lock (this)
            {
                // A custom source may accept the callback and then throw. Never reuse this instance,
                // so a late callback can still observe the input.
                m_poolable = false;
                m_activeCallbacks++;
            }

            try
            {
                Settle(index, OnityTaskSourceStatus.Faulted, exception, default);
            }
            finally
            {
                lock (this)
                {
                    m_activeCallbacks--;
                    ReturnIfReady();
                }
            }
        }

        private void Settle(
            int index,
            OnityTaskSourceStatus status,
            Exception fault,
            CancellationToken cancellationToken)
        {
            bool publish;
            lock (this)
            {
                if (m_accounted[index])
                {
                    return;
                }

                m_accounted[index] = true;
                m_faults[index] = fault;
                m_canceled[index] = fault == null && status == OnityTaskSourceStatus.Canceled;
                m_cancellationTokens[index] = cancellationToken;
                publish = --m_remaining == 0;
            }

            if (publish)
            {
                Publish();
            }
        }

        private void Publish()
        {
            // Every slot is settled, and the caller's active callback keeps the outcome fields
            // alive until publication finishes outside the lock.
            for (int i = 0; i < m_count; i++)
            {
                if (m_faults[i] != null)
                {
                    TrySetException(m_faults[i]);
                    return;
                }
            }

            for (int i = 0; i < m_count; i++)
            {
                if (m_canceled[i])
                {
                    TrySetCanceled(new OperationCanceledException(m_cancellationTokens[i]));
                    return;
                }
            }

            TrySetResult(CreateResult());
        }

        private void ReturnIfReady()
        {
            if (m_returned || !m_poolable || !m_outputReleased || m_registrationPinned
                || m_remaining != 0 || m_activeCallbacks != 0)
            {
                return;
            }

            m_returned = true;
            Array.Clear(m_faults, 0, m_count);
            Array.Clear(m_cancellationTokens, 0, m_count);
            Array.Clear(m_canceled, 0, m_count);
            Array.Clear(m_accounted, 0, m_count);
            Array.Clear(m_observed, 0, m_count);
            ClearInputs();
            ReturnToPool();
        }
    }
}
