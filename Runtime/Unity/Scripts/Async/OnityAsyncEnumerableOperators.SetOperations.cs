using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // Intersect and Except: reads the second source into a set, disposes it, then streams the first
    // source and yields each distinct item that is (Intersect) or is not (Except) in the set.
    internal sealed class OnitySetOperationEnumerator<T> : OnityStagedAsyncEnumerator<T>
    {
        private enum Stage
        {
            BuildMove,
            BuildRelease,
            Move
        }

        private readonly IOnityAsyncEnumerable<T> m_firstDescription;
        private readonly IOnityAsyncEnumerable<T> m_secondDescription;
        private readonly IEqualityComparer<T> m_comparer;
        private readonly bool m_except;
        private readonly Action<bool> m_storeMoved;
        private OnityStageEnumerator<T> m_first;
        private OnityStageEnumerator<T> m_second;
        private HashSet<T> m_set;
        private Stage m_stage;
        private bool m_moved;

        internal OnitySetOperationEnumerator(IOnityAsyncEnumerable<T> first, IOnityAsyncEnumerable<T> second,
            IEqualityComparer<T> comparer, bool except, CancellationToken token) : base(token)
        {
            m_firstDescription = first;
            m_secondDescription = second;
            m_comparer = comparer;
            m_except = except;
            m_storeMoved = moved => m_moved = moved;
        }

        protected override OnityTask<bool> StepCore()
        {
            switch (m_stage)
            {
                case Stage.BuildMove:
                    m_second ??= Acquire(m_secondDescription);
                    m_set ??= new HashSet<T>(m_comparer);
                    return OnityStageTasks.Continue(m_second.MoveNextAsync(), m_storeMoved);
                case Stage.BuildRelease:
                    return Release(m_second);
                default:
                    m_first ??= Acquire(m_firstDescription);
                    return m_first.MoveNextAsync();
            }
        }

        protected override bool AdvanceCore(out T current)
        {
            current = default;
            switch (m_stage)
            {
                case Stage.BuildMove:
                    if (m_moved)
                    {
                        m_set.Add(m_second.Current);
                        return false;
                    }
                    m_stage = Stage.BuildRelease;
                    return false;
                case Stage.BuildRelease:
                    CompleteRelease();
                    m_second = null;
                    m_stage = Stage.Move;
                    return false;
                default:
                    T item = m_first.Current;
                    if (m_except ? m_set.Add(item) : m_set.Remove(item))
                    {
                        current = item;
                        return true;
                    }
                    return false;
            }
        }

        protected override void OnFinish()
        {
            m_first = null;
            m_second = null;
            m_set = null;
        }
    }

    internal sealed class OnitySetOperationAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_first;
        private readonly IOnityAsyncEnumerable<T> m_second;
        private readonly IEqualityComparer<T> m_comparer;
        private readonly bool m_except;

        internal OnitySetOperationAsyncEnumerable(IOnityAsyncEnumerable<T> first, IOnityAsyncEnumerable<T> second,
            IEqualityComparer<T> comparer, bool except)
        {
            m_first = first;
            m_second = second;
            m_comparer = comparer;
            m_except = except;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnitySetOperationEnumerator<T>(m_first, m_second, m_comparer, m_except, cancellationToken);
        }
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- Union / Intersect / Except -----------------------------------------------------

        /// <summary>Yields the distinct items of the first source, then those of the second source.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source, acquired after the first is disposed.</param>
        /// <returns>A lazy description equal to <c>first.Concat(second).Distinct()</c>, as in UniTask.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Union<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second)
        {
            return Union(first, second, null);
        }

        /// <summary>Yields the distinct items of both sources with a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source, acquired after the first is disposed.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <returns>A lazy description equal to <c>first.Concat(second).Distinct(comparer)</c>.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Union<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second, IEqualityComparer<T> comparer)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            return first.Concat(second).Distinct(comparer);
        }

        /// <summary>Yields the distinct items of the first source that also occur in the second.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">Streamed source.</param>
        /// <param name="second">Source read completely first.</param>
        /// <returns>A lazy description with the semantics of the comparer overload.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Intersect<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second)
        {
            return Intersect(first, second, null);
        }

        /// <summary>Yields the distinct items of the first source that also occur in the second.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">Streamed source.</param>
        /// <param name="second">Source read completely first.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the second source into a set and disposes it
        /// before the first source is acquired; items of the first source then stream in order, each
        /// matching value once.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Intersect<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second, IEqualityComparer<T> comparer)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            return new OnitySetOperationAsyncEnumerable<T>(first, second, comparer, false);
        }

        /// <summary>Yields the distinct items of the first source that do not occur in the second.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">Streamed source.</param>
        /// <param name="second">Source read completely first.</param>
        /// <returns>A lazy description with the semantics of the comparer overload.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Except<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second)
        {
            return Except(first, second, null);
        }

        /// <summary>Yields the distinct items of the first source that do not occur in the second.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">Streamed source.</param>
        /// <param name="second">Source read completely first.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the second source into a set and disposes it
        /// before the first source is acquired; items of the first source then stream in order, each
        /// value once.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Except<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second, IEqualityComparer<T> comparer)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            return new OnitySetOperationAsyncEnumerable<T>(first, second, comparer, true);
        }
    }
}
