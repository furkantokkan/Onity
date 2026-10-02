using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // Insertion-ordered lookup supporting a null key (a Dictionary cannot hold one).
    internal sealed class OnityLookup<TKey, TElement> : IOnityLookup<TKey, TElement>
    {
        private readonly Dictionary<TKey, OnityGrouping<TKey, TElement>> m_map;
        private readonly List<OnityGrouping<TKey, TElement>> m_groups = new List<OnityGrouping<TKey, TElement>>();
        private OnityGrouping<TKey, TElement> m_nullGroup;

        internal OnityLookup(IEqualityComparer<TKey> comparer)
        {
            m_map = new Dictionary<TKey, OnityGrouping<TKey, TElement>>(comparer);
        }

        public int Count => m_groups.Count;

        public IEnumerable<TElement> this[TKey key]
        {
            get
            {
                OnityGrouping<TKey, TElement> group = Find(key);
                return group != null ? (IEnumerable<TElement>)group : Array.Empty<TElement>();
            }
        }

        internal void Add(TKey key, TElement element)
        {
            OnityGrouping<TKey, TElement> group = Find(key);
            if (group == null)
            {
                group = new OnityGrouping<TKey, TElement>(key);
                if (key == null)
                {
                    m_nullGroup = group;
                }
                else
                {
                    m_map.Add(key, group);
                }
                m_groups.Add(group);
            }
            group.Add(element);
        }

        public bool Contains(TKey key) => Find(key) != null;

        public IEnumerator<IOnityGrouping<TKey, TElement>> GetEnumerator()
        {
            for (int i = 0; i < m_groups.Count; i++)
            {
                yield return m_groups[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private OnityGrouping<TKey, TElement> Find(TKey key)
        {
            if (key == null)
            {
                return m_nullGroup;
            }
            m_map.TryGetValue(key, out OnityGrouping<TKey, TElement> group);
            return group;
        }
    }

    internal sealed class OnityGrouping<TKey, TElement> : IOnityGrouping<TKey, TElement>
    {
        private readonly List<TElement> m_items = new List<TElement>();

        internal OnityGrouping(TKey key)
        {
            Key = key;
        }

        public TKey Key { get; }

        internal void Add(TElement element) => m_items.Add(element);

        public IEnumerator<TElement> GetEnumerator() => m_items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => m_items.GetEnumerator();
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- ToList / ToHashSet -----------------------------------------------------------

        /// <summary>Collects every item into a new list and awaits cleanup on every exit.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Ordered items. Storage allocates; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<List<T>> ToListAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return ToListCore(source, cancellationToken);
        }

        /// <summary>Collects every item into a hash set using the default equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The distinct items; duplicates are ignored.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<HashSet<T>> ToHashSetAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return ToHashSetCore(source, null, cancellationToken);
        }

        /// <summary>Collects every item into a hash set using a supplied equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The distinct items; duplicates are ignored.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<HashSet<T>> ToHashSetAsync<T>(this IOnityAsyncEnumerable<T> source,
            IEqualityComparer<T> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return ToHashSetCore(source, comparer, cancellationToken);
        }

        private static async OnityTask<List<T>> ToListCore<T>(
            IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                var list = new List<T>();
                while (await enumerator.MoveNextAsync())
                {
                    list.Add(enumerator.Current);
                }
                return list;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<HashSet<T>> ToHashSetCore<T>(IOnityAsyncEnumerable<T> source,
            IEqualityComparer<T> comparer, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                var set = new HashSet<T>(comparer);
                while (await enumerator.MoveNextAsync())
                {
                    set.Add(enumerator.Current);
                }
                return set;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // ---- ToDictionary -----------------------------------------------------------------

        /// <summary>Collects items into a dictionary keyed by a synchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task with the Dictionary exception.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), null, cancellationToken);
        }

        /// <summary>Collects items into a dictionary keyed by a synchronous selector and comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), comparer, cancellationToken);
        }

        /// <summary>Collects selected elements into a dictionary keyed by a synchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<Dictionary<TKey, TElement>> ToDictionaryAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, Func<T, TElement> elementSelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, TElement>.FromSync(elementSelector), null, cancellationToken);
        }

        /// <summary>Collects selected elements into a dictionary using key and element selectors and a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<Dictionary<TKey, TElement>> ToDictionaryAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, Func<T, TElement> elementSelector,
            IEqualityComparer<TKey> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, TElement>.FromSync(elementSelector), comparer, cancellationToken);
        }

        /// <summary>Collects items into a dictionary keyed by an asynchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<Dictionary<TKey, T>> ToDictionaryAwaitAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), null, cancellationToken);
        }

        /// <summary>Collects items into a dictionary keyed by an asynchronous selector and comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<Dictionary<TKey, T>> ToDictionaryAwaitAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), comparer, cancellationToken);
        }

        /// <summary>Collects asynchronously selected elements into a dictionary keyed by an asynchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<Dictionary<TKey, TElement>> ToDictionaryAwaitAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            Func<T, OnityTask<TElement>> elementSelector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, TElement>.FromAwait(elementSelector), null, cancellationToken);
        }

        /// <summary>Collects asynchronously selected elements into a dictionary using a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<Dictionary<TKey, TElement>> ToDictionaryAwaitAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            Func<T, OnityTask<TElement>> elementSelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, TElement>.FromAwait(elementSelector), comparer, cancellationToken);
        }

        /// <summary>Collects items into a dictionary keyed by an asynchronous token-aware selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<Dictionary<TKey, T>> ToDictionaryAwaitWithCancellationAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), null, cancellationToken);
        }

        /// <summary>Collects items into a dictionary keyed by an asynchronous token-aware selector and comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<Dictionary<TKey, T>> ToDictionaryAwaitWithCancellationAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), comparer, cancellationToken);
        }

        /// <summary>Collects token-aware asynchronously selected elements into a dictionary.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector with the token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<Dictionary<TKey, TElement>> ToDictionaryAwaitWithCancellationAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            Func<T, CancellationToken, OnityTask<TElement>> elementSelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, TElement>.FromCancel(elementSelector), null, cancellationToken);
        }

        /// <summary>Collects token-aware asynchronously selected elements into a dictionary using a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector with the token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The dictionary; a null or duplicate key faults the task.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<Dictionary<TKey, TElement>> ToDictionaryAwaitWithCancellationAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            Func<T, CancellationToken, OnityTask<TElement>> elementSelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToDictionaryCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, TElement>.FromCancel(elementSelector), comparer, cancellationToken);
        }

        private static async OnityTask<Dictionary<TKey, TElement>> ToDictionaryCore<T, TKey, TElement>(
            IOnityAsyncEnumerable<T> source, OnityLinqFunc<T, TKey> keySelector,
            OnityLinqFunc<T, TElement> elementSelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                var dictionary = new Dictionary<TKey, TElement>(comparer);
                while (await enumerator.MoveNextAsync())
                {
                    T item = enumerator.Current;
                    TKey key = await keySelector.Invoke(item, cancellationToken);
                    TElement element = await elementSelector.Invoke(item, cancellationToken);
                    dictionary.Add(key, element);
                }
                return dictionary;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // ---- ToLookup ---------------------------------------------------------------------

        /// <summary>Groups items into a lookup keyed by a synchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>
        /// An <see cref="IOnityLookup{TKey, TElement}"/> (Onity's own type, not a System.Linq one) that keeps
        /// first-seen key order and per-key item order; null keys are supported.
        /// </returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, T>> ToLookupAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), null, cancellationToken);
        }

        /// <summary>Groups items into a lookup keyed by a synchronous selector and comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key item order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, T>> ToLookupAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), comparer, cancellationToken);
        }

        /// <summary>Groups selected elements into a lookup keyed by a synchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key element order.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, TElement>> ToLookupAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, Func<T, TElement> elementSelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, TElement>.FromSync(elementSelector), null, cancellationToken);
        }

        /// <summary>Groups selected elements into a lookup using key and element selectors and a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key element order.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, TElement>> ToLookupAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, TKey> keySelector, Func<T, TElement> elementSelector,
            IEqualityComparer<TKey> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromSync(keySelector),
                OnityLinqFunc<T, TElement>.FromSync(elementSelector), comparer, cancellationToken);
        }

        /// <summary>Groups items into a lookup keyed by an asynchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key item order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, T>> ToLookupAwaitAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), null, cancellationToken);
        }

        /// <summary>Groups items into a lookup keyed by an asynchronous selector and comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key item order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, T>> ToLookupAwaitAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), comparer, cancellationToken);
        }

        /// <summary>Groups asynchronously selected elements into a lookup keyed by an asynchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key element order.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, TElement>> ToLookupAwaitAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            Func<T, OnityTask<TElement>> elementSelector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, TElement>.FromAwait(elementSelector), null, cancellationToken);
        }

        /// <summary>Groups asynchronously selected elements into a lookup using a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key element order.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, TElement>> ToLookupAwaitAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, OnityTask<TKey>> keySelector,
            Func<T, OnityTask<TElement>> elementSelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromAwait(keySelector),
                OnityLinqFunc<T, TElement>.FromAwait(elementSelector), comparer, cancellationToken);
        }

        /// <summary>Groups items into a lookup keyed by an asynchronous token-aware selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key item order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, T>> ToLookupAwaitWithCancellationAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), null, cancellationToken);
        }

        /// <summary>Groups items into a lookup keyed by an asynchronous token-aware selector and comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key item order.</returns>
        /// <exception cref="ArgumentNullException">Source or key selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, T>> ToLookupAwaitWithCancellationAsync<T, TKey>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance), comparer, cancellationToken);
        }

        /// <summary>Groups token-aware asynchronously selected elements into a lookup.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector with the token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key element order.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, TElement>> ToLookupAwaitWithCancellationAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            Func<T, CancellationToken, OnityTask<TElement>> elementSelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, TElement>.FromCancel(elementSelector), null, cancellationToken);
        }

        /// <summary>Groups token-aware asynchronously selected elements into a lookup using a comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially with the enumeration token.</param>
        /// <param name="elementSelector">Element selector awaited after the key selector with the token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>A lookup preserving first-seen key order and per-key element order.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static OnityTask<IOnityLookup<TKey, TElement>> ToLookupAwaitWithCancellationAsync<T, TKey, TElement>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TKey>> keySelector,
            Func<T, CancellationToken, OnityTask<TElement>> elementSelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return ToLookupCore(source, OnityLinqFunc<T, TKey>.FromCancel(keySelector),
                OnityLinqFunc<T, TElement>.FromCancel(elementSelector), comparer, cancellationToken);
        }

        private static async OnityTask<IOnityLookup<TKey, TElement>> ToLookupCore<T, TKey, TElement>(
            IOnityAsyncEnumerable<T> source, OnityLinqFunc<T, TKey> keySelector,
            OnityLinqFunc<T, TElement> elementSelector, IEqualityComparer<TKey> comparer,
            CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                var lookup = new OnityLookup<TKey, TElement>(comparer);
                while (await enumerator.MoveNextAsync())
                {
                    T item = enumerator.Current;
                    TKey key = await keySelector.Invoke(item, cancellationToken);
                    TElement element = await elementSelector.Invoke(item, cancellationToken);
                    lookup.Add(key, element);
                }
                return lookup;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // ---- ForEach ----------------------------------------------------------------------

        /// <summary>Runs a synchronous action for every item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="action">Action invoked sequentially on the continuation context of each move.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Completion; cleanup runs on every exit and its failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAsync<T>(this IOnityAsyncEnumerable<T> source, Action<T> action,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(action, nameof(action));
            return ForEachCore(source, action, null, cancellationToken);
        }

        /// <summary>Runs a synchronous indexed action for every item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="action">Action receiving the item and its zero-based index.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Completion; cleanup runs on every exit and its failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAsync<T>(this IOnityAsyncEnumerable<T> source, Action<T, int> action,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(action, nameof(action));
            return ForEachCore(source, null, action, cancellationToken);
        }

        /// <summary>Awaits an asynchronous action for every item, one at a time.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="action">Asynchronous action.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Completion with the same ownership and cleanup rules as the token-aware ForEachAsync.</returns>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAsync<T>(this IOnityAsyncEnumerable<T> source, Func<T, OnityTask> action,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(action, nameof(action));
            return OnityAsyncEnumerableExtensions.ForEachAsync(source, (item, token) => action(item), cancellationToken);
        }

        /// <summary>Awaits an asynchronous action for every item, one at a time.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="action">Asynchronous action.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Completion with the same ownership and cleanup rules as the token-aware ForEachAsync.</returns>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask> action, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(action, nameof(action));
            return OnityAsyncEnumerableExtensions.ForEachAsync(source, (item, token) => action(item), cancellationToken);
        }

        /// <summary>Awaits an asynchronous indexed action for every item, one at a time.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="action">Asynchronous action receiving the item and its zero-based index.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Completion with the same ownership and cleanup rules as the token-aware ForEachAsync.</returns>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, int, OnityTask> action, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(action, nameof(action));
            int index = 0;
            return OnityAsyncEnumerableExtensions.ForEachAsync(source,
                (item, token) => action(item, checked(index++)), cancellationToken);
        }

        /// <summary>Awaits a token-aware asynchronous action for every item, one at a time.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="action">Asynchronous action receiving an owned cooperative lifetime token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Completion; identical to the existing token-aware ForEachAsync.</returns>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask> action, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(action, nameof(action));
            return OnityAsyncEnumerableExtensions.ForEachAsync(source, action, cancellationToken);
        }

        /// <summary>Awaits a token-aware asynchronous indexed action for every item, one at a time.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="action">Asynchronous action receiving the item, its zero-based index and an owned token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Completion; the same rules as the token-aware ForEachAsync.</returns>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, int, CancellationToken, OnityTask> action, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(action, nameof(action));
            int index = 0;
            return OnityAsyncEnumerableExtensions.ForEachAsync(source,
                (item, token) => action(item, checked(index++), token), cancellationToken);
        }

        private static async OnityTask ForEachCore<T>(IOnityAsyncEnumerable<T> source, Action<T> action,
            Action<T, int> indexedAction, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                int index = 0;
                while (await enumerator.MoveNextAsync())
                {
                    if (action != null)
                    {
                        action(enumerator.Current);
                    }
                    else
                    {
                        indexedAction(enumerator.Current, checked(index++));
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }
    }
}
