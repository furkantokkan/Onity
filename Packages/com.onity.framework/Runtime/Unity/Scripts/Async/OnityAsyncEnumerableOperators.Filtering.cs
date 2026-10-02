using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityLinqDescription<T, TResult> : IOnityAsyncEnumerable<TResult>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly Func<IOnityAsyncEnumerable<T>, CancellationToken, IOnityAsyncEnumerator<TResult>> m_factory;

        internal OnityLinqDescription(IOnityAsyncEnumerable<T> source,
            Func<IOnityAsyncEnumerable<T>, CancellationToken, IOnityAsyncEnumerator<TResult>> factory)
        {
            m_source = source;
            m_factory = factory;
        }

        public IOnityAsyncEnumerator<TResult> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return m_factory(m_source, cancellationToken);
        }
    }

    internal sealed class OnitySkipEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly int m_count;
        private int m_skipped;

        internal OnitySkipEnumerator(IOnityAsyncEnumerable<T> source, int count, CancellationToken token)
            : base(source, token)
        {
            m_count = count;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            if (IsClosing)
            {
                return false;
            }
            if (m_skipped < m_count)
            {
                m_skipped++;
                return false;
            }
            return true;
        }
    }

    internal sealed class OnitySkipLastEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Queue<T> m_queue = new Queue<T>();
        private readonly int m_count;

        internal OnitySkipLastEnumerator(IOnityAsyncEnumerable<T> source, int count, CancellationToken token)
            : base(source, token)
        {
            m_count = count;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            T value = UpstreamCurrent;
            current = default;
            if (IsClosing)
            {
                return false;
            }
            m_queue.Enqueue(value);
            if (m_queue.Count > m_count)
            {
                current = m_queue.Dequeue();
                return true;
            }
            return false;
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_queue.Clear();
        }
    }

    // Buffers the last items while upstream runs, then serves them from the buffer. The upstream
    // end is consumed inside MoveCore, so the base never sees it as terminal while items remain.
    internal sealed class OnityTakeLastEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Queue<T> m_queue = new Queue<T>();
        private readonly int m_count;
        private bool m_drained;

        internal OnityTakeLastEnumerator(IOnityAsyncEnumerable<T> source, int count, CancellationToken token)
            : base(source, token)
        {
            m_count = count;
        }

        protected override bool EndAfterItem => m_drained && m_queue.Count == 0;

        protected override OnityTask<bool> MoveCore()
        {
            if (m_drained)
            {
                return OnityTask<bool>.FromResult(m_queue.Count > 0);
            }
            return Drain(base.MoveCore());
        }

        private async OnityTask<bool> Drain(OnityTask<bool> upstreamMove)
        {
            if (await upstreamMove)
            {
                return true;
            }
            m_drained = true;
            return m_queue.Count > 0;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            if (m_drained)
            {
                current = m_queue.Dequeue();
                return true;
            }
            T value = UpstreamCurrent;
            current = default;
            if (IsClosing)
            {
                return false;
            }
            if (m_queue.Count == m_count)
            {
                m_queue.Dequeue();
            }
            m_queue.Enqueue(value);
            return false;
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_queue.Clear();
        }
    }

    internal sealed class OnitySkipWhileEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Func<T, int, bool> m_predicate;
        private bool m_skipping = true;
        private int m_index;

        internal OnitySkipWhileEnumerator(IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate,
            CancellationToken token) : base(source, token)
        {
            m_predicate = predicate;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            if (IsClosing)
            {
                return false;
            }
            if (m_skipping)
            {
                if (m_predicate(current, checked(m_index++)))
                {
                    return false;
                }
                m_skipping = false;
            }
            return true;
        }
    }

    internal sealed class OnityTakeWhileEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Func<T, int, bool> m_predicate;
        private bool m_ended;
        private int m_index;

        internal OnityTakeWhileEnumerator(IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate,
            CancellationToken token) : base(source, token)
        {
            m_predicate = predicate;
        }

        // A failed predicate rejects the item and ends the stream on the next move, which
        // disposes upstream before the end is published and never pulls another item.
        protected override OnityTask<bool> MoveCore()
        {
            return m_ended ? OnityTask<bool>.FromResult(false) : base.MoveCore();
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            if (IsClosing)
            {
                return false;
            }
            if (!m_predicate(current, checked(m_index++)))
            {
                m_ended = true;
                return false;
            }
            return true;
        }
    }

    internal sealed class OnityIndexedWhereEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Func<T, int, bool> m_predicate;
        private int m_index;

        internal OnityIndexedWhereEnumerator(IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate,
            CancellationToken token) : base(source, token)
        {
            m_predicate = predicate;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            return !IsClosing && m_predicate(current, checked(m_index++));
        }
    }

    internal sealed class OnityIndexedSelectEnumerator<T, TResult> : OnitySourceAsyncEnumerator<T, TResult>
    {
        private readonly Func<T, int, TResult> m_selector;
        private int m_index;

        internal OnityIndexedSelectEnumerator(IOnityAsyncEnumerable<T> source, Func<T, int, TResult> selector,
            CancellationToken token) : base(source, token)
        {
            m_selector = selector;
        }

        protected override bool ReadCurrentCore(out TResult current)
        {
            T value = UpstreamCurrent;
            if (IsClosing)
            {
                current = default;
                return false;
            }
            current = m_selector(value, checked(m_index++));
            return true;
        }
    }

    internal sealed class OnityDistinctEnumerator<T, TKey> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Func<T, TKey> m_keySelector;
        private HashSet<TKey> m_seen;

        internal OnityDistinctEnumerator(IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector,
            IEqualityComparer<TKey> comparer, CancellationToken token) : base(source, token)
        {
            m_keySelector = keySelector;
            m_seen = new HashSet<TKey>(comparer);
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            return !IsClosing && m_seen.Add(m_keySelector(current));
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_seen.Clear();
        }
    }

    internal sealed class OnityDistinctUntilChangedEnumerator<T, TKey> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Func<T, TKey> m_keySelector;
        private readonly IEqualityComparer<TKey> m_comparer;
        private TKey m_previous;
        private bool m_hasPrevious;

        internal OnityDistinctUntilChangedEnumerator(IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector,
            IEqualityComparer<TKey> comparer, CancellationToken token) : base(source, token)
        {
            m_keySelector = keySelector;
            m_comparer = comparer ?? EqualityComparer<TKey>.Default;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            if (IsClosing)
            {
                return false;
            }
            TKey key = m_keySelector(current);
            if (m_hasPrevious && m_comparer.Equals(m_previous, key))
            {
                return false;
            }
            m_previous = key;
            m_hasPrevious = true;
            return true;
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_previous = default;
        }
    }

    // Await-family enumerators reuse the lifecycle owned by OnityAwaitAsyncEnumerator: one pending
    // delegate at a time, a lazily created owned token, and awaited cleanup of upstream and token.
    internal abstract class OnityLinqAwaitEnumerator<T, TDelegate, TOutput>
        : OnityAwaitAsyncEnumerator<T, TDelegate, TOutput>
    {
        private readonly Func<T, int, CancellationToken, OnityTask<TDelegate>> m_func;
        private int m_index;

        protected OnityLinqAwaitEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<TDelegate>> func, CancellationToken token)
            : base(source, token)
        {
            m_func = func;
        }

        protected override OnityTask<TDelegate> Invoke(T item, CancellationToken token)
        {
            return m_func(item, checked(m_index++), token);
        }
    }

    internal sealed class OnitySkipWhileAwaitEnumerator<T> : OnityLinqAwaitEnumerator<T, bool, T>
    {
        private bool m_skipping = true;

        internal OnitySkipWhileAwaitEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<bool>> predicate, CancellationToken token)
            : base(source, predicate, token)
        {
        }

        protected override bool Select(T item, bool result, out T selected)
        {
            selected = default;
            if (m_skipping)
            {
                if (result)
                {
                    return false;
                }
                m_skipping = false;
            }
            selected = item;
            return true;
        }
    }

    internal sealed class OnityTakeWhileAwaitEnumerator<T> : OnityLinqAwaitEnumerator<T, bool, T>
    {
        private bool m_ended;

        internal OnityTakeWhileAwaitEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<bool>> predicate, CancellationToken token)
            : base(source, predicate, token)
        {
        }

        protected override OnityTask<bool> MoveCore()
        {
            return m_ended ? OnityTask<bool>.FromResult(false) : base.MoveCore();
        }

        protected override bool Select(T item, bool result, out T selected)
        {
            selected = default;
            if (!result)
            {
                m_ended = true;
                return false;
            }
            selected = item;
            return true;
        }
    }

    internal sealed class OnityIndexedSelectAwaitEnumerator<T, TResult> : OnityLinqAwaitEnumerator<T, TResult, TResult>
    {
        internal OnityIndexedSelectAwaitEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<TResult>> selector, CancellationToken token)
            : base(source, selector, token)
        {
        }

        protected override bool Select(T item, TResult result, out TResult selected)
        {
            selected = result;
            return true;
        }
    }

    internal sealed class OnityIndexedWhereAwaitEnumerator<T> : OnityLinqAwaitEnumerator<T, bool, T>
    {
        internal OnityIndexedWhereAwaitEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<bool>> predicate, CancellationToken token)
            : base(source, predicate, token)
        {
        }

        protected override bool Select(T item, bool result, out T selected)
        {
            selected = result ? item : default;
            return result;
        }
    }

    internal sealed class OnityDistinctAwaitEnumerator<T, TKey> : OnityLinqAwaitEnumerator<T, TKey, T>
    {
        private readonly HashSet<TKey> m_seen;

        internal OnityDistinctAwaitEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<TKey>> keySelector, IEqualityComparer<TKey> comparer,
            CancellationToken token) : base(source, keySelector, token)
        {
            m_seen = new HashSet<TKey>(comparer);
        }

        protected override bool Select(T item, TKey result, out T selected)
        {
            selected = item;
            return m_seen.Add(result);
        }
    }

    internal sealed class OnityDistinctUntilChangedAwaitEnumerator<T, TKey> : OnityLinqAwaitEnumerator<T, TKey, T>
    {
        private readonly IEqualityComparer<TKey> m_comparer;
        private TKey m_previous;
        private bool m_hasPrevious;

        internal OnityDistinctUntilChangedAwaitEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<TKey>> keySelector, IEqualityComparer<TKey> comparer,
            CancellationToken token) : base(source, keySelector, token)
        {
            m_comparer = comparer ?? EqualityComparer<TKey>.Default;
        }

        protected override bool Select(T item, TKey result, out T selected)
        {
            selected = item;
            if (m_hasPrevious && m_comparer.Equals(m_previous, result))
            {
                return false;
            }
            m_previous = result;
            m_hasPrevious = true;
            return true;
        }
    }

    /// <summary>Paging, filtering, aggregation and materialization operators for native asynchronous streams.</summary>
    /// <remarks>Operators returning a stream are lazy descriptions with independent, unpooled enumerators
    /// built on the same lifecycle as <see cref="OnityAsyncEnumerableExtensions"/>: argument validation is
    /// synchronous, delegates run outside lifecycle locks, thrown OperationCanceledException from a
    /// delegate is a fault while a returned canceled task keeps its status, and upstream cleanup runs once
    /// (before the last item for early-ending operators). Terminal operators are implemented as
    /// <c>async OnityTask</c> methods that enumerate sequentially and always await enumerator disposal
    /// (cleanup failure takes precedence over an earlier failure). Nothing adds a thread or context hop:
    /// continuations run where the upstream task completes, so Unity-bound sources (EveryUpdate and other
    /// PlayerLoop sources) must be consumed on the main thread. Enumerators, pending observers and result
    /// storage allocate; await-family operators additionally own a lazily created cancellation token source.
    /// Names follow UniTask 2.5.11 (<c>XxxAwait</c>, <c>XxxAwaitWithCancellation</c>, terminal <c>XxxAsync</c>).
    /// The existing token-aware <c>SelectAwait</c>, <c>WhereAwait</c> and <c>ForEachAsync</c> keep their
    /// shape; their index-taking siblings are reachable through the <c>AwaitWithCancellation</c> names to avoid
    /// lambda ambiguity with the existing token-taking overloads.</remarks>
    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- Skip / SkipLast / TakeLast ---------------------------------------------------

        /// <summary>Skips the first items of the stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="count">Number of leading items to skip; zero or less skips nothing.</param>
        /// <returns>A lazy description; nothing is skipped when <paramref name="count"/> is not positive.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Skip<T>(this IOnityAsyncEnumerable<T> source, int count)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (count <= 0)
            {
                return source;
            }
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnitySkipEnumerator<T>(upstream, count, token));
        }

        /// <summary>Drops the last items of the stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="count">Number of trailing items to drop; zero or less drops nothing.</param>
        /// <returns>A lazy description buffering at most <paramref name="count"/> items.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipLast<T>(this IOnityAsyncEnumerable<T> source, int count)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (count <= 0)
            {
                return source;
            }
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnitySkipLastEnumerator<T>(upstream, count, token));
        }

        /// <summary>Yields only the last items of the stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="count">Number of trailing items to keep; zero or less yields an empty stream.</param>
        /// <returns>A lazy description that drains upstream before yielding the buffered tail; upstream is
        /// disposed before the last tail item is published.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeLast<T>(this IOnityAsyncEnumerable<T> source, int count)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (count <= 0)
            {
                return OnityAsyncEnumerable.Empty<T>();
            }
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityTakeLastEnumerator<T>(upstream, count, token));
        }

        // ---- SkipWhile --------------------------------------------------------------------

        /// <summary>Skips leading items while a synchronous predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked until it first returns false.</param>
        /// <returns>A lazy description yielding the first rejected item and everything after it.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipWhile<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, bool> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateSkipWhile(source, (item, index) => predicate(item));
        }

        /// <summary>Skips leading items while a synchronous indexed predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving the item and its zero-based index.</param>
        /// <returns>A lazy description yielding the first rejected item and everything after it.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipWhile<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateSkipWhile(source, predicate);
        }

        /// <summary>Skips leading items while an asynchronous predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <returns>A lazy description yielding the first rejected item and everything after it.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipWhileAwait<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateSkipWhileAwait(source, (item, index, token) => predicate(item));
        }

        /// <summary>Skips leading items while an asynchronous indexed predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving the item and its zero-based index.</param>
        /// <returns>A lazy description yielding the first rejected item and everything after it.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipWhileAwait<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateSkipWhileAwait(source, (item, index, token) => predicate(item, index));
        }

        /// <summary>Skips leading items while a token-aware asynchronous predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy description yielding the first rejected item and everything after it.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipWhileAwaitWithCancellation<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateSkipWhileAwait(source, (item, index, token) => predicate(item, token));
        }

        /// <summary>Skips leading items while a token-aware asynchronous indexed predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving the item, its zero-based index and an owned token.</param>
        /// <returns>A lazy description yielding the first rejected item and everything after it.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipWhileAwaitWithCancellation<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, CancellationToken, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateSkipWhileAwait(source, predicate);
        }

        private static IOnityAsyncEnumerable<T> CreateSkipWhile<T>(
            IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnitySkipWhileEnumerator<T>(upstream, predicate, token));
        }

        private static IOnityAsyncEnumerable<T> CreateSkipWhileAwait<T>(
            IOnityAsyncEnumerable<T> source, Func<T, int, CancellationToken, OnityTask<bool>> predicate)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnitySkipWhileAwaitEnumerator<T>(upstream, predicate, token));
        }

        // ---- TakeWhile --------------------------------------------------------------------

        /// <summary>Yields leading items while a synchronous predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate; the first false result ends the stream without pulling more items.</param>
        /// <returns>A lazy description; upstream is disposed before the end is published.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeWhile<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, bool> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateTakeWhile(source, (item, index) => predicate(item));
        }

        /// <summary>Yields leading items while a synchronous indexed predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving the item and its zero-based index.</param>
        /// <returns>A lazy description; upstream is disposed before the end is published.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeWhile<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateTakeWhile(source, predicate);
        }

        /// <summary>Yields leading items while an asynchronous predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <returns>A lazy description; upstream is disposed before the end is published.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeWhileAwait<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateTakeWhileAwait(source, (item, index, token) => predicate(item));
        }

        /// <summary>Yields leading items while an asynchronous indexed predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving the item and its zero-based index.</param>
        /// <returns>A lazy description; upstream is disposed before the end is published.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeWhileAwait<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateTakeWhileAwait(source, (item, index, token) => predicate(item, index));
        }

        /// <summary>Yields leading items while a token-aware asynchronous predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy description; upstream is disposed before the end is published.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeWhileAwaitWithCancellation<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateTakeWhileAwait(source, (item, index, token) => predicate(item, token));
        }

        /// <summary>Yields leading items while a token-aware asynchronous indexed predicate holds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving the item, its zero-based index and an owned token.</param>
        /// <returns>A lazy description; upstream is disposed before the end is published.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeWhileAwaitWithCancellation<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, CancellationToken, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CreateTakeWhileAwait(source, predicate);
        }

        private static IOnityAsyncEnumerable<T> CreateTakeWhile<T>(
            IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityTakeWhileEnumerator<T>(upstream, predicate, token));
        }

        private static IOnityAsyncEnumerable<T> CreateTakeWhileAwait<T>(
            IOnityAsyncEnumerable<T> source, Func<T, int, CancellationToken, OnityTask<bool>> predicate)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityTakeWhileAwaitEnumerator<T>(upstream, predicate, token));
        }

        // ---- Select / Where (indexed and missing await shapes) ---------------------------

        /// <summary>Maps items with a synchronous selector that also receives the zero-based index.</summary>
        /// <typeparam name="T">Input item type.</typeparam>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector invoked sequentially outside lifecycle locks.</param>
        /// <returns>A lazy mapped description; selector exceptions, including OCE, are faults.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> Select<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, TResult> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return new OnityLinqDescription<T, TResult>(source,
                (upstream, token) => new OnityIndexedSelectEnumerator<T, TResult>(upstream, selector, token));
        }

        /// <summary>Maps items with an asynchronous selector that ignores the lifetime token.</summary>
        /// <typeparam name="T">Input item type.</typeparam>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector awaited sequentially.</param>
        /// <returns>A lazy mapped description with the lifecycle of the token-aware SelectAwait.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectAwait<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TResult>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return OnityAsyncEnumerableExtensions.SelectAwait(source, (item, token) => selector(item));
        }

        /// <summary>Maps items with a token-aware asynchronous selector.</summary>
        /// <typeparam name="T">Input item type.</typeparam>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy mapped description; identical to the existing token-aware SelectAwait.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectAwaitWithCancellation<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TResult>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return OnityAsyncEnumerableExtensions.SelectAwait(source, selector);
        }

        /// <summary>Maps items with a token-aware asynchronous selector that also receives the index.</summary>
        /// <typeparam name="T">Input item type.</typeparam>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector receiving the item, its zero-based index and an owned token.</param>
        /// <returns>A lazy mapped description with the lifecycle of the token-aware SelectAwait.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectAwaitWithCancellation<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, CancellationToken, OnityTask<TResult>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return new OnityLinqDescription<T, TResult>(source,
                (upstream, token) => new OnityIndexedSelectAwaitEnumerator<T, TResult>(upstream, selector, token));
        }

        /// <summary>Filters items with a synchronous predicate that also receives the zero-based index.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially outside lifecycle locks.</param>
        /// <returns>A lazy filtered description without recursive synchronous moves.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> Where<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, bool> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityIndexedWhereEnumerator<T>(upstream, predicate, token));
        }

        /// <summary>Filters items with an asynchronous predicate that ignores the lifetime token.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <returns>A lazy filtered description with the lifecycle of the token-aware WhereAwait.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> WhereAwait<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return OnityAsyncEnumerableExtensions.WhereAwait(source, (item, token) => predicate(item));
        }

        /// <summary>Filters items with a token-aware asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy filtered description; identical to the existing token-aware WhereAwait.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> WhereAwaitWithCancellation<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return OnityAsyncEnumerableExtensions.WhereAwait(source, predicate);
        }

        /// <summary>Filters items with a token-aware asynchronous predicate that also receives the index.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate receiving the item, its zero-based index and an owned token.</param>
        /// <returns>A lazy filtered description with the lifecycle of the token-aware WhereAwait.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> WhereAwaitWithCancellation<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, int, CancellationToken, OnityTask<bool>> predicate)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityIndexedWhereAwaitEnumerator<T>(upstream, predicate, token));
        }

        // ---- Distinct ---------------------------------------------------------------------

        /// <summary>Removes duplicate items using the default equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <returns>A lazy description; every enumeration keeps its own set of seen items.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Distinct<T>(this IOnityAsyncEnumerable<T> source)
        {
            return Distinct(source, (IEqualityComparer<T>)null);
        }

        /// <summary>Removes duplicate items using a supplied equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <returns>A lazy description; every enumeration keeps its own set of seen items.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Distinct<T>(
            this IOnityAsyncEnumerable<T> source, IEqualityComparer<T> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return CreateDistinct(source, OnityLinqIdentity<T>.Instance, comparer);
        }

        /// <summary>Removes items whose key was already seen, using the default key comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially outside lifecycle locks.</param>
        /// <returns>A lazy description yielding the first item of each key.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> Distinct<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinct(source, keySelector, null);
        }

        /// <summary>Removes items whose key was already seen, using a supplied key comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially outside lifecycle locks.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description yielding the first item of each key.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> Distinct<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinct(source, keySelector, comparer);
        }

        /// <summary>Removes items whose asynchronously selected key was already seen.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <returns>A lazy description yielding the first item of each key.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctAwait<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctAwait(source, (item, index, token) => keySelector(item), null);
        }

        /// <summary>Removes items whose asynchronously selected key was already seen, with a key comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description yielding the first item of each key.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctAwait<T, TKey>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<TKey>> keySelector, IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctAwait(source, (item, index, token) => keySelector(item), comparer);
        }

        /// <summary>Removes items whose token-aware asynchronously selected key was already seen.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy description yielding the first item of each key.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctAwaitWithCancellation<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctAwait(source, (item, index, token) => keySelector(item, token), null);
        }

        /// <summary>Removes items whose token-aware asynchronously selected key was already seen, with a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving an owned cooperative lifetime token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description yielding the first item of each key.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctAwaitWithCancellation<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctAwait(source, (item, index, token) => keySelector(item, token), comparer);
        }

        private static IOnityAsyncEnumerable<T> CreateDistinct<T, TKey>(IOnityAsyncEnumerable<T> source,
            Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityDistinctEnumerator<T, TKey>(upstream, keySelector, comparer, token));
        }

        private static IOnityAsyncEnumerable<T> CreateDistinctAwait<T, TKey>(IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask<TKey>> keySelector, IEqualityComparer<TKey> comparer)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityDistinctAwaitEnumerator<T, TKey>(upstream, keySelector, comparer, token));
        }

        // ---- DistinctUntilChanged ---------------------------------------------------------

        /// <summary>Drops items equal to the previous item, using the default equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChanged<T>(this IOnityAsyncEnumerable<T> source)
        {
            return DistinctUntilChanged(source, (IEqualityComparer<T>)null);
        }

        /// <summary>Drops items equal to the previous item, using a supplied equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChanged<T>(
            this IOnityAsyncEnumerable<T> source, IEqualityComparer<T> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return CreateDistinctUntilChanged(source, OnityLinqIdentity<T>.Instance, comparer);
        }

        /// <summary>Drops items whose key equals the previous key, using the default key comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially outside lifecycle locks.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChanged<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctUntilChanged(source, keySelector, null);
        }

        /// <summary>Drops items whose key equals the previous key, using a supplied key comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially outside lifecycle locks.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChanged<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctUntilChanged(source, keySelector, comparer);
        }

        /// <summary>Drops items whose asynchronously selected key equals the previous key.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChangedAwait<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctUntilChangedAwait(source, (item, index, token) => keySelector(item), null);
        }

        /// <summary>Drops items whose asynchronously selected key equals the previous key, with a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChangedAwait<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctUntilChangedAwait(source, (item, index, token) => keySelector(item), comparer);
        }

        /// <summary>Drops items whose token-aware asynchronously selected key equals the previous key.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChangedAwaitWithCancellation<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctUntilChangedAwait(source, (item, index, token) => keySelector(item, token), null);
        }

        /// <summary>Drops items whose token-aware asynchronously selected key equals the previous key, with a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving an owned cooperative lifetime token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description; the first item is always yielded.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityAsyncEnumerable<T> DistinctUntilChangedAwaitWithCancellation<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateDistinctUntilChangedAwait(source, (item, index, token) => keySelector(item, token), comparer);
        }

        private static IOnityAsyncEnumerable<T> CreateDistinctUntilChanged<T, TKey>(
            IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityDistinctUntilChangedEnumerator<T, TKey>(
                    upstream, keySelector, comparer, token));
        }

        private static IOnityAsyncEnumerable<T> CreateDistinctUntilChangedAwait<T, TKey>(
            IOnityAsyncEnumerable<T> source, Func<T, int, CancellationToken, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityDistinctUntilChangedAwaitEnumerator<T, TKey>(
                    upstream, keySelector, comparer, token));
        }
    }
}
