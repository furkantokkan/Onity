using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Pooled source for <c>OnityTask.WhenAny(OnityTask&lt;T&gt; leftTask, OnityTask rightTask)</c>.
    /// The array <c>WhenAny</c> base registers both inputs in order, picks the first observed
    /// terminal input, keeps observing the loser and returns the source to the pool once the output
    /// is released and both inputs are settled.
    /// </summary>
    /// <typeparam name="T">Result type of the left input.</typeparam>
    internal sealed class OnityWhenAnyLeftRightTaskSource<T> :
        OnityWhenAnyArrayTaskSourceBase<(bool hasResultLeft, T result)>,
        IOnityPooledRunner<OnityWhenAnyLeftRightTaskSource<T>>
    {
        private const int k_inputCount = 2;
        private const int k_leftIndex = 0;

        private static OnityRunnerPool<OnityWhenAnyLeftRightTaskSource<T>> s_pool;

        private OnityWhenAnyLeftRightTaskSource<T> m_nextPooled;
        private OnityTask<T> m_left;
        private OnityTask m_right;

        private OnityWhenAnyLeftRightTaskSource() : base(k_inputCount)
        {
        }

        ref OnityWhenAnyLeftRightTaskSource<T> IOnityPooledRunner<OnityWhenAnyLeftRightTaskSource<T>>.NextPooled =>
            ref m_nextPooled;

        internal static OnityWhenAnyLeftRightTaskSource<T> Rent(OnityTask<T> left, OnityTask right)
        {
            // A contended rent allocates instead of waiting.
            if (!s_pool.TryPop(out OnityWhenAnyLeftRightTaskSource<T> source))
            {
                source = new OnityWhenAnyLeftRightTaskSource<T>();
            }

            source.m_left = left;
            source.m_right = right;
            source.Initialize(k_inputCount);
            return source;
        }

        protected override void RegisterInput(int index, Action callback)
        {
            if (index == k_leftIndex)
            {
                m_left.RegisterWhenAnyObserver(callback);
            }
            else
            {
                m_right.RegisterWhenAnyObserver(callback);
            }
        }

        protected override OnityTaskSourceStatus ReadOutcome(
            int index,
            out (bool hasResultLeft, T result) result,
            out Exception fault,
            out CancellationToken cancellationToken)
        {
            if (index == k_leftIndex)
            {
                OnityTaskSourceStatus status = m_left.ReadWhenAnyOutcome(
                    out T value, out fault, out cancellationToken);
                result = (true, value);
                return status;
            }

            result = (false, default);
            return m_right.ReadWhenAnyOutcome(out fault, out cancellationToken);
        }

        protected override void ClearInput(int index)
        {
            if (index == k_leftIndex)
            {
                m_left = default;
            }
            else
            {
                m_right = default;
            }
        }

        protected override void ClearInputs(int count)
        {
            m_left = default;
            m_right = default;
        }

        protected override void ReturnToPool()
        {
            // A contended or full return lets the source be collected.
            s_pool.TryPush(this, OnityTaskSettings.s_sourcePoolCapacity);
        }
    }
}
