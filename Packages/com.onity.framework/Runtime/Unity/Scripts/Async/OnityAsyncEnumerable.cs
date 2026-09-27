using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>Finite native asynchronous stream descriptions.</summary>
    public static class OnityAsyncEnumerable
    {
        /// <summary>Creates an on-demand stream yielding Unit.Default once per accepted Update wait.</summary>
        /// <returns>An inert reusable description with no buffering or replay while idle.</returns>
        /// <remarks>Creation and idle disposal need no Unity access. Each active move uses
        /// Yield(Update), requiring the main thread and an accepting Play/player session.
        /// Reentrant moves wait for a later Update pass. Pending work and owned tokens allocate.</remarks>
        public static IOnityAsyncEnumerable<Onity.Core.Unit> EveryUpdate()
        {
            return OnityEveryUpdateAsyncEnumerable.Instance;
        }

        /// <summary>Creates an empty stream without acquiring upstream resources.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <returns>A reusable empty description.</returns>
        public static IOnityAsyncEnumerable<T> Empty<T>()
        {
            return EmptyDescription<T>.Instance;
        }

        /// <summary>Creates a stream containing one item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="value">Item to yield.</param>
        /// <returns>A reusable single-item description.</returns>
        public static IOnityAsyncEnumerable<T> Return<T>(T value)
        {
            return new ReturnDescription<T>(value);
        }

        /// <summary>Creates a finite ascending integer stream.</summary>
        /// <param name="start">First integer.</param>
        /// <param name="count">Nonnegative item count.</param>
        /// <returns>A reusable range description; zero count is empty for every start.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Count is negative or the last item exceeds int.MaxValue.</exception>
        public static IOnityAsyncEnumerable<int> Range(int start, int count)
        {
            if (count < 0 || (count > 0 && (long)start + count - 1 > int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            return count == 0 ? Empty<int>() : new RangeDescription(start, count);
        }

        private sealed class EmptyDescription<T> : IOnityAsyncEnumerable<T>
        {
            internal static readonly EmptyDescription<T> Instance = new EmptyDescription<T>();

            public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new EmptyEnumerator<T>();
            }
        }

        private sealed class EmptyEnumerator<T> : OnityAsyncEnumeratorBase<T>
        {
            protected override OnityTask<bool> MoveCore() => OnityTask<bool>.FromResult(false);
            protected override bool ReadCurrentCore(out T current)
            {
                current = default;
                return false;
            }
        }

        private sealed class ReturnDescription<T> : IOnityAsyncEnumerable<T>
        {
            private readonly T m_value;

            internal ReturnDescription(T value)
            {
                m_value = value;
            }

            public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new ReturnEnumerator<T>(m_value, cancellationToken);
            }
        }

        private sealed class ReturnEnumerator<T> : OnityAsyncEnumeratorBase<T>
        {
            private readonly T m_value;
            private readonly CancellationToken m_token;
            private bool m_advanced;

            internal ReturnEnumerator(T value, CancellationToken token)
            {
                m_value = value;
                m_token = token;
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (m_advanced)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_token.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_token);
                }
                m_advanced = true;
                return OnityTask<bool>.FromResult(true);
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = m_value;
                return true;
            }
        }

        private sealed class RangeDescription : IOnityAsyncEnumerable<int>
        {
            private readonly int m_start;
            private readonly int m_count;

            internal RangeDescription(int start, int count)
            {
                m_start = start;
                m_count = count;
            }

            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new RangeEnumerator(m_start, m_count, cancellationToken);
            }
        }

        private sealed class RangeEnumerator : OnityAsyncEnumeratorBase<int>
        {
            private readonly int m_start;
            private readonly int m_count;
            private readonly CancellationToken m_token;
            private int m_index;

            internal RangeEnumerator(int start, int count, CancellationToken token)
            {
                m_start = start;
                m_count = count;
                m_token = token;
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (m_index == m_count)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_token.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_token);
                }
                m_index++;
                return OnityTask<bool>.FromResult(true);
            }

            protected override bool ReadCurrentCore(out int current)
            {
                current = (int)((long)m_start + m_index - 1);
                return true;
            }
        }
    }
}
