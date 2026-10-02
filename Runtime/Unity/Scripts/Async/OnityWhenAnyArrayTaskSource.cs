using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Onity.Unity.Async
{
    internal readonly struct OnityWhenAnyInputIdentity : IEquatable<OnityWhenAnyInputIdentity>
    {
        private readonly object m_source;
        private readonly int m_token;

        internal OnityWhenAnyInputIdentity(object source, int token)
        {
            m_source = source;
            m_token = token;
        }

        public bool Equals(OnityWhenAnyInputIdentity other)
        {
            return ReferenceEquals(m_source, other.m_source) && m_token == other.m_token;
        }

        public override bool Equals(object other)
        {
            return other is OnityWhenAnyInputIdentity identity && Equals(identity);
        }

        public override int GetHashCode()
        {
            return unchecked(RuntimeHelpers.GetHashCode(m_source) * 397 ^ m_token);
        }

        internal static void Validate(OnityTask[] inputs, int count)
        {
            HashSet<OnityWhenAnyInputIdentity> seen = null;
            for (int i = 0; i < count; i++)
            {
                if (!inputs[i].TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity identity))
                {
                    continue;
                }

                if (count > 16)
                {
                    if (seen == null)
                    {
                        seen = new HashSet<OnityWhenAnyInputIdentity>();
                    }

                    if (!seen.Add(identity))
                    {
                        ThrowDuplicate();
                    }
                }
                else
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (inputs[j].TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity previous)
                            && identity.Equals(previous))
                        {
                            ThrowDuplicate();
                        }
                    }
                }
            }
        }

        internal static void Validate<T>(OnityTask<T>[] inputs, int count)
        {
            HashSet<OnityWhenAnyInputIdentity> seen = null;
            for (int i = 0; i < count; i++)
            {
                if (!inputs[i].TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity identity))
                {
                    continue;
                }

                if (count > 16)
                {
                    if (seen == null)
                    {
                        seen = new HashSet<OnityWhenAnyInputIdentity>();
                    }

                    if (!seen.Add(identity))
                    {
                        ThrowDuplicate();
                    }
                }
                else
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (inputs[j].TryGetWhenAnyIdentity(out OnityWhenAnyInputIdentity previous)
                            && identity.Equals(previous))
                        {
                            ThrowDuplicate();
                        }
                    }
                }
            }
        }

        private static void ThrowDuplicate()
        {
            throw new ArgumentException("A single-consumer OnityTask cannot be passed to WhenAny twice.", "tasks");
        }
    }

    internal abstract class OnityWhenAnyArrayTaskSourceBase<TOutput> : OnityTaskSourceBase<TOutput>
    {
        protected const int k_retainedInputs = 16;
        protected const int k_maxPoolSize = 256;
        protected const int k_retainedMediumInputs = 32;
        protected const int k_maxMediumPoolSize = 128;
        private readonly Action[] m_callbacks;
        private readonly bool[] m_accounted;
        private readonly bool[] m_observed;
        private int m_count;
        private int m_remaining;
        private int m_activeCallbacks;
        private int m_winner;
        private bool m_registrationPinned;
        private bool m_outputReleased;
        private bool m_poolable;
        private bool m_returned;

        protected OnityWhenAnyArrayTaskSourceBase(int capacity)
        {
            m_callbacks = new Action[capacity];
            m_accounted = new bool[capacity];
            m_observed = new bool[capacity];
            for (int i = 0; i < capacity; i++)
            {
                int index = i;
                m_callbacks[i] = () => CompleteInput(index);
            }
        }

        protected void Initialize(int count)
        {
            Reset(default);
            m_count = count;
            m_remaining = count;
            m_activeCallbacks = 0;
            m_winner = 0;
            m_registrationPinned = true;
            m_outputReleased = false;
            m_poolable = count <= k_retainedMediumInputs;
            m_returned = false;
        }

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

        private void CompleteInput(int index)
        {
            bool winner;
            lock (this)
            {
                if (m_observed[index])
                {
                    return;
                }

                m_observed[index] = true;
                m_activeCallbacks++;
                winner = m_winner == 0;
                if (winner)
                {
                    m_winner = index + 1;
                }
                if (!m_accounted[index])
                {
                    m_accounted[index] = true;
                    m_remaining--;
                }
            }

            try
            {
                TOutput result;
                Exception fault;
                CancellationToken cancellationToken;
                OnityTaskSourceStatus status;
                try
                {
                    status = ReadOutcome(index, out result, out fault, out cancellationToken);
                    if (status == OnityTaskSourceStatus.Pending
                        || (status == OnityTaskSourceStatus.Faulted && fault == null))
                    {
                        fault = new InvalidOperationException("WhenAny input did not provide a terminal outcome.");
                    }
                }
                catch (Exception exception)
                {
                    result = default;
                    fault = exception;
                    cancellationToken = default;
                    status = OnityTaskSourceStatus.Faulted;
                }

                if (winner)
                {
                    if (fault != null)
                    {
                        TrySetException(fault);
                    }
                    else if (status == OnityTaskSourceStatus.Canceled)
                    {
                        TrySetCanceled(new OperationCanceledException(cancellationToken));
                    }
                    else
                    {
                        TrySetResult(result);
                    }
                }
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
            bool winner;
            lock (this)
            {
                // Custom sources may accept the callback and then throw. Never reuse
                // this instance; retain that input until a possible late callback observes it.
                m_poolable = false;
                m_activeCallbacks++;
                winner = m_winner == 0;
                if (winner)
                {
                    m_winner = index + 1;
                }
                if (!m_accounted[index])
                {
                    m_accounted[index] = true;
                    m_remaining--;
                }
            }

            try
            {
                if (winner)
                {
                    TrySetException(exception);
                }
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

        private void ReturnIfReady()
        {
            if (m_returned || !m_poolable || !m_outputReleased || m_registrationPinned
                || m_remaining != 0 || m_activeCallbacks != 0)
            {
                return;
            }

            m_returned = true;
            Array.Clear(m_accounted, 0, m_count);
            Array.Clear(m_observed, 0, m_count);
            ClearInputs(m_count);
            m_count = 0;
            ReturnToPool();
        }

        protected abstract void RegisterInput(int index, Action callback);
        protected abstract OnityTaskSourceStatus ReadOutcome(
            int index, out TOutput result, out Exception fault, out CancellationToken cancellationToken);
        protected abstract void ClearInput(int index);
        protected abstract void ClearInputs(int count);
        protected abstract void ReturnToPool();
    }

    internal sealed class OnityWhenAnyArrayTaskSource :
        OnityWhenAnyArrayTaskSourceBase<int>, IOnityPooledRunner<OnityWhenAnyArrayTaskSource>
    {
        private static OnityRunnerPool<OnityWhenAnyArrayTaskSource> s_pool;
        private static OnityRunnerPool<OnityWhenAnyArrayTaskSource> s_mediumPool;
        private readonly OnityTask[] m_inputs;
        private OnityWhenAnyArrayTaskSource m_nextPooled;

        private OnityWhenAnyArrayTaskSource(int capacity) : base(capacity)
        {
            m_inputs = new OnityTask[capacity];
        }

        ref OnityWhenAnyArrayTaskSource IOnityPooledRunner<OnityWhenAnyArrayTaskSource>.NextPooled =>
            ref m_nextPooled;

        internal static OnityWhenAnyArrayTaskSource Rent(OnityTask[] inputs)
        {
            OnityWhenAnyArrayTaskSource source;
            if (inputs.Length > k_retainedMediumInputs)
            {
                source = new OnityWhenAnyArrayTaskSource(inputs.Length);
            }
            else
            {
                // A contended rent allocates instead of waiting.
                bool small = inputs.Length <= k_retainedInputs;
                if (!(small ? s_pool.TryPop(out source) : s_mediumPool.TryPop(out source)))
                {
                    source = new OnityWhenAnyArrayTaskSource(small ? k_retainedInputs : k_retainedMediumInputs);
                }
            }

            try
            {
                // Copy exactly once before inspecting identities or exposing a task token.
                Array.Copy(inputs, source.m_inputs, inputs.Length);
                OnityWhenAnyInputIdentity.Validate(source.m_inputs, inputs.Length);
                source.Initialize(inputs.Length);
            }
            catch
            {
                source.ClearInputs(inputs.Length);
                if (source.m_inputs.Length <= k_retainedMediumInputs)
                {
                    source.ReturnToPool();
                }

                throw;
            }
            return source;
        }

        protected override void RegisterInput(int index, Action callback)
        {
            m_inputs[index].RegisterWhenAnyObserver(callback);
        }

        protected override OnityTaskSourceStatus ReadOutcome(
            int index, out int result, out Exception fault, out CancellationToken cancellationToken)
        {
            result = index;
            return m_inputs[index].ReadWhenAnyOutcome(out fault, out cancellationToken);
        }

        protected override void ClearInput(int index)
        {
            m_inputs[index] = default;
        }

        protected override void ClearInputs(int count)
        {
            Array.Clear(m_inputs, 0, count);
        }

        protected override void ReturnToPool()
        {
            if (m_inputs.Length > k_retainedMediumInputs)
            {
                return;
            }

            // The active count was cleared before this call; immutable capacity identifies its bucket.
            // A contended or full return lets the source be collected.
            if (m_inputs.Length <= k_retainedInputs)
            {
                s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
            }
            else
            {
                s_mediumPool.TryPush(this, k_maxMediumPoolSize);
            }
        }
    }

    internal sealed class OnityWhenAnyArrayTaskSource<T> :
        OnityWhenAnyArrayTaskSourceBase<(int winnerIndex, T result)>,
        IOnityPooledRunner<OnityWhenAnyArrayTaskSource<T>>
    {
        private static OnityRunnerPool<OnityWhenAnyArrayTaskSource<T>> s_pool;
        private static OnityRunnerPool<OnityWhenAnyArrayTaskSource<T>> s_mediumPool;
        private readonly OnityTask<T>[] m_inputs;
        private OnityWhenAnyArrayTaskSource<T> m_nextPooled;

        private OnityWhenAnyArrayTaskSource(int capacity) : base(capacity)
        {
            m_inputs = new OnityTask<T>[capacity];
        }

        ref OnityWhenAnyArrayTaskSource<T> IOnityPooledRunner<OnityWhenAnyArrayTaskSource<T>>.NextPooled =>
            ref m_nextPooled;

        internal static OnityWhenAnyArrayTaskSource<T> Rent(OnityTask<T>[] inputs)
        {
            OnityWhenAnyArrayTaskSource<T> source;
            if (inputs.Length > k_retainedMediumInputs)
            {
                source = new OnityWhenAnyArrayTaskSource<T>(inputs.Length);
            }
            else
            {
                // A contended rent allocates instead of waiting.
                bool small = inputs.Length <= k_retainedInputs;
                if (!(small ? s_pool.TryPop(out source) : s_mediumPool.TryPop(out source)))
                {
                    source = new OnityWhenAnyArrayTaskSource<T>(small ? k_retainedInputs : k_retainedMediumInputs);
                }
            }

            try
            {
                Array.Copy(inputs, source.m_inputs, inputs.Length);
                OnityWhenAnyInputIdentity.Validate(source.m_inputs, inputs.Length);
                source.Initialize(inputs.Length);
            }
            catch
            {
                source.ClearInputs(inputs.Length);
                if (source.m_inputs.Length <= k_retainedMediumInputs)
                {
                    source.ReturnToPool();
                }

                throw;
            }
            return source;
        }

        protected override void RegisterInput(int index, Action callback)
        {
            m_inputs[index].RegisterWhenAnyObserver(callback);
        }

        protected override OnityTaskSourceStatus ReadOutcome(
            int index, out (int winnerIndex, T result) result, out Exception fault, out CancellationToken cancellationToken)
        {
            OnityTaskSourceStatus status = m_inputs[index].ReadWhenAnyOutcome(
                out T value, out fault, out cancellationToken);
            result = (index, value);
            return status;
        }

        protected override void ClearInput(int index)
        {
            m_inputs[index] = default;
        }

        protected override void ClearInputs(int count)
        {
            Array.Clear(m_inputs, 0, count);
        }

        protected override void ReturnToPool()
        {
            if (m_inputs.Length > k_retainedMediumInputs)
            {
                return;
            }

            // The active count was cleared before this call; immutable capacity identifies its bucket.
            // A contended or full return lets the source be collected.
            if (m_inputs.Length <= k_retainedInputs)
            {
                s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
            }
            else
            {
                s_mediumPool.TryPush(this, k_maxMediumPoolSize);
            }
        }
    }
}
