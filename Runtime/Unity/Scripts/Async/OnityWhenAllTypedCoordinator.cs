using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    // Only exact, nonpooled completion sources enter this path. Their callbacks and
    // outcome reads remain shareable if another consumer creates an AsTask bridge.
    internal sealed class OnityWhenAllTypedCoordinator<T> : IOnityPooledRunner<OnityWhenAllTypedCoordinator<T>>
    {
        private const int k_maxInputs = 16;
        private static OnityRunnerPool<OnityWhenAllTypedCoordinator<T>> s_pool;

        private readonly OnityTask<T>[] m_inputs = new OnityTask<T>[k_maxInputs];
        private readonly Action[] m_callbacks = new Action[k_maxInputs];
        private readonly Exception[] m_faults = new Exception[k_maxInputs];
        private readonly CancellationToken[] m_cancellationTokens = new CancellationToken[k_maxInputs];
        private readonly bool[] m_canceled = new bool[k_maxInputs];
        private readonly bool[] m_settled = new bool[k_maxInputs];
        private OnityWhenAllTypedCoordinator<T> m_nextPooled;
        private TaskCompletionSource<T[]> m_completion;
        private T[] m_results;
        private int m_count;
        private int m_remaining;
        private int m_activeCallbacks;
        private bool m_registrationPinned;
        private bool m_returned;
        private bool m_factoryFailed;

        private OnityWhenAllTypedCoordinator()
        {
            for (int i = 0; i < k_maxInputs; i++)
            {
                int index = i;
                m_callbacks[i] = () => CompleteInput(index);
            }
        }

        ref OnityWhenAllTypedCoordinator<T> IOnityPooledRunner<OnityWhenAllTypedCoordinator<T>>.NextPooled =>
            ref m_nextPooled;

        internal static bool TryRent(OnityTask<T>[] inputs, out OnityWhenAllTypedCoordinator<T> coordinator)
        {
            coordinator = null;
            if (!IsEligible(inputs, inputs.Length))
            {
                return false;
            }

            // A contended rent allocates instead of waiting.
            if (!s_pool.TryPop(out OnityWhenAllTypedCoordinator<T> rented))
            {
                rented = new OnityWhenAllTypedCoordinator<T>();
            }

            lock (rented)
            {
                rented.m_returned = false;
                rented.m_count = inputs.Length;
                Array.Copy(inputs, rented.m_inputs, inputs.Length);
                // The caller may mutate its array. Validate the complete owned snapshot,
                // then never read that caller array after a callback can run.
                if (!IsEligible(rented.m_inputs, rented.m_count))
                {
                    rented.ReturnIfReady();
                    return false;
                }

                rented.m_remaining = rented.m_count;
                rented.m_registrationPinned = true;
            }

            coordinator = rented;
            return true;
        }

        private static bool IsEligible(OnityTask<T>[] inputs, int count)
        {
            if (count < 1 || count > k_maxInputs)
            {
                return false;
            }

            bool pending = false;
            for (int i = 0; i < count; i++)
            {
                if (!inputs[i].IsTypedCoordinatorEligible)
                {
                    return false;
                }

                pending |= !inputs[i].IsCompleted;
                for (int j = 0; j < i; j++)
                {
                    if (inputs[i].SharesTypedCoordinatorSourceWith(inputs[j]))
                    {
                        return false;
                    }
                }
            }

            return pending;
        }

        internal Task<T[]> Start()
        {
            Task<T[]> output;
            try
            {
                // This array belongs to the retained output task, never to the pool.
                m_results = new T[m_count];
                m_completion = OnityTaskCompletionSource<T[]>.CreateTaskBridge();
                output = m_completion.Task;
            }
            catch
            {
                lock (this)
                {
                    m_remaining = 0;
                    m_registrationPinned = false;
                    ReturnIfReady();
                }

                throw;
            }

            Exception registrationFailure = null;
            try
            {
                for (int i = 0; i < m_count; i++)
                {
                    try
                    {
                        OnityTaskAwaiter<T> awaiter = m_inputs[i].GetAwaiter();
                        if (awaiter.IsCompleted)
                        {
                            m_callbacks[i]();
                        }
                        else
                        {
                            awaiter.UnsafeOnCompleted(m_callbacks[i]);
                        }
                    }
                    catch (Exception exception)
                    {
                        registrationFailure = registrationFailure ?? exception;
                        lock (this)
                        {
                            m_factoryFailed = true;
                        }

                        // A throwing registration did not accept its callback. Continue
                        // registering later inputs so all accepted work is still observed.
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

            if (registrationFailure != null)
            {
                ExceptionDispatchInfo.Capture(registrationFailure).Throw();
            }

            return output;
        }

        private void CompleteInput(int index)
        {
            OnityTask<T> input;
            lock (this)
            {
                m_activeCallbacks++;
                input = m_inputs[index];
            }

            try
            {
                T result;
                Exception fault;
                CancellationToken cancellationToken;
                OnityTaskSourceStatus status;
                try
                {
                    status = input.ReadTypedCoordinatorOutcome(out result, out fault, out cancellationToken);
                    if (status == OnityTaskSourceStatus.Pending)
                    {
                        status = OnityTaskSourceStatus.Faulted;
                        fault = new InvalidOperationException("OnityTask is not completed.");
                    }
                }
                catch (Exception exception)
                {
                    result = default;
                    fault = exception;
                    cancellationToken = default;
                    status = OnityTaskSourceStatus.Faulted;
                }

                Settle(index, result, fault, status == OnityTaskSourceStatus.Canceled, cancellationToken);
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

        private void CompleteRegistrationFailure(int index, Exception fault)
        {
            // Registration remains pinned throughout this call and its publication.
            Settle(index, default, fault, false, default);
        }

        private void Settle(int index, T result, Exception fault, bool canceled, CancellationToken cancellationToken)
        {
            TaskCompletionSource<T[]> completion = null;
            lock (this)
            {
                if (m_settled[index])
                {
                    return;
                }

                m_settled[index] = true;
                m_results[index] = result;
                m_faults[index] = fault;
                m_canceled[index] = canceled;
                m_cancellationTokens[index] = cancellationToken;
                if (--m_remaining == 0)
                {
                    completion = m_completion;
                }
            }

            if (completion != null)
            {
                Publish(completion);
            }
        }

        private void Publish(TaskCompletionSource<T[]> completion)
        {
            // All outcome slots are terminal. The last callback or registration pin
            // keeps these fields alive until publication finishes outside the lock.
            int faultCount = 0;
            int firstCanceled = -1;
            for (int i = 0; i < m_count; i++)
            {
                if (m_faults[i] != null)
                {
                    faultCount++;
                }
                if (firstCanceled < 0 && m_canceled[i])
                {
                    firstCanceled = i;
                }
            }

            if (faultCount > 0)
            {
                Exception[] faults = new Exception[faultCount];
                int next = 0;
                for (int i = 0; i < m_count; i++)
                {
                    if (m_faults[i] != null)
                    {
                        faults[next++] = m_faults[i];
                    }
                }

                // The enumerable overload retains original exceptions and nested
                // AggregateExceptions in input order; faulted OCE stays a fault.
                completion.TrySetException(faults);
                if (m_factoryFailed)
                {
                    _ = completion.Task.Exception;
                }
            }
            else if (firstCanceled >= 0)
            {
                completion.TrySetCanceled(m_cancellationTokens[firstCanceled]);
            }
            else
            {
                completion.TrySetResult(m_results);
            }
        }

        private void ReturnIfReady()
        {
            if (m_returned || m_registrationPinned || m_remaining != 0 || m_activeCallbacks != 0)
            {
                return;
            }

            m_returned = true;
            Array.Clear(m_inputs, 0, m_count);
            Array.Clear(m_faults, 0, m_count);
            Array.Clear(m_cancellationTokens, 0, m_count);
            Array.Clear(m_canceled, 0, m_count);
            Array.Clear(m_settled, 0, m_count);
            m_results = null;
            m_completion = null;
            m_count = 0;
            m_factoryFailed = false;
            // The caller holds lock(this); the gate never waits, so a contended or full return
            // lets the coordinator be collected.
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }
}
