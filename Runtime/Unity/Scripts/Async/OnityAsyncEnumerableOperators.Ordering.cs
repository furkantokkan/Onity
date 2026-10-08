using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // One sort key of an ordered description; it creates the per-enumeration key storage.
    internal abstract class OnityOrderKey<T>
    {
        internal abstract OnityOrderKeyState<T> CreateState(int count);
    }

    internal abstract class OnityOrderKeyState<T>
    {
        internal abstract bool IsSync { get; }
        internal abstract bool UsesToken { get; }
        internal abstract void ComputeAll(List<T> items);
        internal abstract OnityTask<bool> Compute(int index, T item, CancellationToken token);
        internal abstract int Compare(int left, int right);
    }

    internal sealed class OnityOrderKey<T, TKey> : OnityOrderKey<T>
    {
        private readonly OnityStreamFunc<T, TKey> m_selector;
        private readonly IComparer<TKey> m_comparer;
        private readonly bool m_descending;

        internal OnityOrderKey(OnityStreamFunc<T, TKey> selector, IComparer<TKey> comparer, bool descending)
        {
            m_selector = selector;
            m_comparer = comparer ?? Comparer<TKey>.Default;
            m_descending = descending;
        }

        internal override OnityOrderKeyState<T> CreateState(int count) => new State(this, count);

        private sealed class State : OnityOrderKeyState<T>
        {
            private readonly OnityOrderKey<T, TKey> m_owner;
            private readonly TKey[] m_keys;
            private readonly Action<TKey> m_store;
            private int m_pending;

            internal State(OnityOrderKey<T, TKey> owner, int count)
            {
                m_owner = owner;
                m_keys = new TKey[count];
                m_store = key => m_keys[m_pending] = key;
            }

            internal override bool IsSync => m_owner.m_selector.IsSync;
            internal override bool UsesToken => m_owner.m_selector.UsesToken;

            internal override void ComputeAll(List<T> items)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    m_keys[i] = m_owner.m_selector.Invoke(items[i]);
                }
            }

            internal override OnityTask<bool> Compute(int index, T item, CancellationToken token)
            {
                m_pending = index;
                return OnityStageTasks.Continue(m_owner.m_selector.InvokeAsync(item, token), m_store);
            }

            internal override int Compare(int left, int right)
            {
                int comparison = m_owner.m_comparer.Compare(m_keys[left], m_keys[right]);
                if (!m_owner.m_descending)
                {
                    return comparison;
                }
                return comparison < 0 ? 1 : comparison > 0 ? -1 : 0;
            }
        }
    }

    internal sealed class OnityOrderedAsyncEnumerable<T> : IOnityOrderedAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly OnityOrderKey<T>[] m_keys;

        internal OnityOrderedAsyncEnumerable(IOnityAsyncEnumerable<T> source, OnityOrderKey<T>[] keys)
        {
            m_source = source;
            m_keys = keys;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnityOrderByEnumerator<T>(m_source, m_keys, cancellationToken);
        }

        public IOnityOrderedAsyncEnumerable<T> CreateOrderedEnumerable<TKey>(Func<T, TKey> keySelector,
            IComparer<TKey> comparer, bool descending)
        {
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return Then(new OnityOrderKey<T, TKey>(OnityStreamFunc<T, TKey>.Sync(keySelector), comparer, descending));
        }

        public IOnityOrderedAsyncEnumerable<T> CreateOrderedEnumerable<TKey>(Func<T, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer, bool descending)
        {
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return Then(new OnityOrderKey<T, TKey>(OnityStreamFunc<T, TKey>.Await(keySelector), comparer, descending));
        }

        public IOnityOrderedAsyncEnumerable<T> CreateOrderedEnumerable<TKey>(
            Func<T, CancellationToken, OnityTask<TKey>> keySelector, IComparer<TKey> comparer, bool descending)
        {
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return Then(new OnityOrderKey<T, TKey>(OnityStreamFunc<T, TKey>.Cancel(keySelector), comparer, descending));
        }

        internal static IOnityOrderedAsyncEnumerable<T> Create(IOnityAsyncEnumerable<T> source, OnityOrderKey<T> key)
        {
            return new OnityOrderedAsyncEnumerable<T>(source, new[] { key });
        }

        private IOnityOrderedAsyncEnumerable<T> Then(OnityOrderKey<T> key)
        {
            var keys = new OnityOrderKey<T>[m_keys.Length + 1];
            Array.Copy(m_keys, keys, m_keys.Length);
            keys[m_keys.Length] = key;
            return new OnityOrderedAsyncEnumerable<T>(m_source, keys);
        }
    }

    // Reads the whole source, disposes it, computes every key level (primary first, awaiting async
    // selectors one element at a time), sorts stably and yields the sorted items.
    internal sealed class OnityOrderByEnumerator<T> : OnityStagedAsyncEnumerator<T>
    {
        private enum Stage
        {
            Move,
            Release,
            Keys,
            Yield
        }

        private readonly IOnityAsyncEnumerable<T> m_sourceDescription;
        private readonly OnityOrderKey<T>[] m_keys;
        private readonly Action<bool> m_storeMoved;
        private readonly Comparison<int> m_comparison;
        private OnityStageEnumerator<T> m_source;
        private List<T> m_items;
        private OnityOrderKeyState<T>[] m_states;
        private int[] m_map;
        private Stage m_stage;
        private int m_level;
        private int m_index;
        private int m_yield;
        private bool m_moved;

        internal OnityOrderByEnumerator(IOnityAsyncEnumerable<T> source, OnityOrderKey<T>[] keys,
            CancellationToken token) : base(token)
        {
            m_sourceDescription = source;
            m_keys = keys;
            m_storeMoved = moved => m_moved = moved;
            m_comparison = CompareIndexes;
        }

        protected override OnityTask<bool> StepCore()
        {
            switch (m_stage)
            {
                case Stage.Move:
                    m_source ??= Acquire(m_sourceDescription);
                    m_items ??= new List<T>();
                    return OnityStageTasks.Continue(m_source.MoveNextAsync(), m_storeMoved);
                case Stage.Release:
                    return Release(m_source);
                case Stage.Keys:
                    return ComputeKeys();
                default:
                    return m_yield < m_map.Length ? OnityStageTasks.True : OnityStageTasks.False;
            }
        }

        protected override bool AdvanceCore(out T current)
        {
            current = default;
            switch (m_stage)
            {
                case Stage.Move:
                    if (m_moved)
                    {
                        m_items.Add(m_source.Current);
                        return false;
                    }
                    m_stage = Stage.Release;
                    return false;
                case Stage.Release:
                    CompleteRelease();
                    m_source = null;
                    m_states = new OnityOrderKeyState<T>[m_keys.Length];
                    for (int i = 0; i < m_keys.Length; i++)
                    {
                        m_states[i] = m_keys[i].CreateState(m_items.Count);
                    }
                    m_stage = Stage.Keys;
                    return false;
                case Stage.Keys:
                    m_index++;
                    return false;
                default:
                    current = m_items[m_map[m_yield]];
                    m_items[m_map[m_yield]] = default;
                    m_yield++;
                    return true;
            }
        }

        private OnityTask<bool> ComputeKeys()
        {
            while (m_level < m_states.Length)
            {
                OnityOrderKeyState<T> state = m_states[m_level];
                if (state.IsSync)
                {
                    state.ComputeAll(m_items);
                    m_level++;
                    m_index = 0;
                    continue;
                }
                if (m_index < m_items.Count)
                {
                    CancellationToken token = state.UsesToken ? DelegateToken : Token;
                    if (token.IsCancellationRequested)
                    {
                        return OnityStageTasks.Failed(OnityStageTasks.Canceled(token));
                    }
                    return state.Compute(m_index, m_items[m_index], token);
                }
                m_level++;
                m_index = 0;
            }
            m_map = new int[m_items.Count];
            for (int i = 0; i < m_map.Length; i++)
            {
                m_map[i] = i;
            }
            Array.Sort(m_map, m_comparison);
            m_states = null;
            m_stage = Stage.Yield;
            return m_map.Length != 0 ? OnityStageTasks.True : OnityStageTasks.False;
        }

        private int CompareIndexes(int left, int right)
        {
            for (int i = 0; i < m_states.Length; i++)
            {
                int comparison = m_states[i].Compare(left, right);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return left.CompareTo(right);
        }

        protected override void OnFinish()
        {
            m_source = null;
            m_items = null;
            m_states = null;
            m_map = null;
        }
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- OrderBy / OrderByDescending -----------------------------------------------------

        /// <summary>Sorts the stream in ascending key order.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked once per item after the source ended.</param>
        /// <returns>A lazy ordered description. The first move reads the whole source and disposes it, then
        /// computes the keys (primary key first) and sorts stably: items with equal keys keep source order.
        /// Items are yielded from memory.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderBy<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            return OrderBy(source, keySelector, null);
        }

        /// <summary>Sorts the stream in ascending key order with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked once per item after the source ended.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy ordered description with the semantics of the comparer-less overload.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderBy<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateOrdered(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector), comparer, false);
        }

        /// <summary>Sorts the stream in ascending order of an asynchronous key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited once per item, one at a time.</param>
        /// <returns>A lazy ordered description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByAwait<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector)
        {
            return OrderByAwait(source, keySelector, null);
        }

        /// <summary>Sorts the stream in ascending order of an asynchronous key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited once per item, one at a time.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy ordered description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByAwait<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateOrdered(source, OnityStreamFunc<TSource, TKey>.Await(keySelector), comparer, false);
        }

        /// <summary>Sorts the stream in ascending order of a token-aware asynchronous key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving a token owned by the enumeration.</param>
        /// <returns>A lazy ordered description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByAwaitWithCancellation<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, OnityTask<TKey>> keySelector)
        {
            return OrderByAwaitWithCancellation(source, keySelector, null);
        }

        /// <summary>Sorts the stream in ascending order of a token-aware asynchronous key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving a token owned by the enumeration.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy ordered description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByAwaitWithCancellation<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateOrdered(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector), comparer, false);
        }

        /// <summary>Sorts the stream in descending key order.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked once per item after the source ended.</param>
        /// <returns>A lazy ordered description; equal keys keep source order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByDescending<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            return OrderByDescending(source, keySelector, null);
        }

        /// <summary>Sorts the stream in descending key order with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked once per item after the source ended.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy ordered description; equal keys keep source order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByDescending<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateOrdered(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector), comparer, true);
        }

        /// <summary>Sorts the stream in descending order of an asynchronous key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited once per item, one at a time.</param>
        /// <returns>A lazy ordered description; equal keys keep source order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByDescendingAwait<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector)
        {
            return OrderByDescendingAwait(source, keySelector, null);
        }

        /// <summary>Sorts the stream in descending order of an asynchronous key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited once per item, one at a time.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy ordered description; equal keys keep source order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByDescendingAwait<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateOrdered(source, OnityStreamFunc<TSource, TKey>.Await(keySelector), comparer, true);
        }

        /// <summary>Sorts the stream in descending order of a token-aware asynchronous key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving a token owned by the enumeration.</param>
        /// <returns>A lazy ordered description; equal keys keep source order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByDescendingAwaitWithCancellation<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, OnityTask<TKey>> keySelector)
        {
            return OrderByDescendingAwaitWithCancellation(source, keySelector, null);
        }

        /// <summary>Sorts the stream in descending order of a token-aware asynchronous key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector receiving a token owned by the enumeration.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy ordered description; equal keys keep source order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> OrderByDescendingAwaitWithCancellation<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateOrdered(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector), comparer, true);
        }

        // ---- ThenBy / ThenByDescending -------------------------------------------------------

        /// <summary>Adds an ascending secondary key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenBy<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            return ThenBy(source, keySelector, null);
        }

        /// <summary>Adds an ascending secondary key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenBy<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return source.CreateOrderedEnumerable(keySelector, comparer, false);
        }

        /// <summary>Adds an ascending asynchronous secondary key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector awaited once per item.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByAwait<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector)
        {
            return ThenByAwait(source, keySelector, null);
        }

        /// <summary>Adds an ascending asynchronous secondary key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector awaited once per item.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByAwait<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return source.CreateOrderedEnumerable(keySelector, comparer, false);
        }

        /// <summary>Adds an ascending token-aware asynchronous secondary key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector receiving a token owned by the enumeration.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByAwaitWithCancellation<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector)
        {
            return ThenByAwaitWithCancellation(source, keySelector, null);
        }

        /// <summary>Adds an ascending token-aware asynchronous secondary key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector receiving a token owned by the enumeration.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByAwaitWithCancellation<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector, IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return source.CreateOrderedEnumerable(keySelector, comparer, false);
        }

        /// <summary>Adds a descending secondary key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByDescending<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            return ThenByDescending(source, keySelector, null);
        }

        /// <summary>Adds a descending secondary key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByDescending<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return source.CreateOrderedEnumerable(keySelector, comparer, true);
        }

        /// <summary>Adds a descending asynchronous secondary key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector awaited once per item.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByDescendingAwait<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector)
        {
            return ThenByDescendingAwait(source, keySelector, null);
        }

        /// <summary>Adds a descending asynchronous secondary key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector awaited once per item.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByDescendingAwait<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source, Func<TSource, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return source.CreateOrderedEnumerable(keySelector, comparer, true);
        }

        /// <summary>Adds a descending token-aware asynchronous secondary key.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector receiving a token owned by the enumeration.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByDescendingAwaitWithCancellation<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector)
        {
            return ThenByDescendingAwaitWithCancellation(source, keySelector, null);
        }

        /// <summary>Adds a descending token-aware asynchronous secondary key with a comparer.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Ordered description.</param>
        /// <param name="keySelector">Secondary key selector receiving a token owned by the enumeration.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A new ordered description; the original is unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static IOnityOrderedAsyncEnumerable<TSource> ThenByDescendingAwaitWithCancellation<TSource, TKey>(
            this IOnityOrderedAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector, IComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return source.CreateOrderedEnumerable(keySelector, comparer, true);
        }

        private static IOnityOrderedAsyncEnumerable<TSource> CreateOrdered<TSource, TKey>(
            IOnityAsyncEnumerable<TSource> source, OnityStreamFunc<TSource, TKey> keySelector,
            IComparer<TKey> comparer, bool descending)
        {
            return OnityOrderedAsyncEnumerable<TSource>.Create(source,
                new OnityOrderKey<TSource, TKey>(keySelector, comparer, descending));
        }
    }
}
