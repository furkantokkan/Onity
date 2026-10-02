using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // Reads the whole source into a lookup (first-seen key order, null keys allowed), disposes the
    // source, then yields one grouping or result per key.
    internal sealed class OnityGroupByEnumerator<TSource, TKey, TElement, TResult> : OnityStagedAsyncEnumerator<TResult>
    {
        private enum Stage
        {
            Move,
            Key,
            Element,
            Release,
            Groups,
            Result
        }

        private readonly IOnityAsyncEnumerable<TSource> m_sourceDescription;
        private readonly OnityStreamFunc<TSource, TKey> m_keySelector;
        private readonly OnityStreamFunc<TSource, TElement> m_elementSelector;
        private readonly OnityStreamFunc<TKey, IEnumerable<TElement>, TResult> m_resultSelector;
        private readonly IEqualityComparer<TKey> m_comparer;
        private readonly Action<bool> m_storeMoved;
        private readonly Action<TKey> m_storeKey;
        private readonly Action<TElement> m_storeElement;
        private readonly Action<TResult> m_storeResult;
        private OnityStageEnumerator<TSource> m_source;
        private OnityLookup<TKey, TElement> m_lookup;
        private IEnumerator<IOnityGrouping<TKey, TElement>> m_groups;
        private IOnityGrouping<TKey, TElement> m_group;
        private TSource m_item;
        private TKey m_key;
        private TElement m_element;
        private TResult m_result;
        private Stage m_stage;
        private bool m_moved;

        internal OnityGroupByEnumerator(IOnityAsyncEnumerable<TSource> source, OnityStreamFunc<TSource, TKey> keySelector,
            OnityStreamFunc<TSource, TElement> elementSelector,
            OnityStreamFunc<TKey, IEnumerable<TElement>, TResult> resultSelector, IEqualityComparer<TKey> comparer,
            CancellationToken token) : base(token)
        {
            m_sourceDescription = source;
            m_keySelector = keySelector;
            m_elementSelector = elementSelector;
            m_resultSelector = resultSelector;
            m_comparer = comparer;
            m_storeMoved = moved => m_moved = moved;
            m_storeKey = key => m_key = key;
            m_storeElement = element => m_element = element;
            m_storeResult = result => m_result = result;
        }

        protected override OnityTask<bool> StepCore()
        {
            switch (m_stage)
            {
                case Stage.Move:
                    m_source ??= Acquire(m_sourceDescription);
                    m_lookup ??= new OnityLookup<TKey, TElement>(m_comparer);
                    return OnityStageTasks.Continue(m_source.MoveNextAsync(), m_storeMoved);
                case Stage.Key:
                    return Await(m_keySelector, m_item, m_storeKey);
                case Stage.Element:
                    return Await(m_elementSelector, m_item, m_storeElement);
                case Stage.Release:
                    return Release(m_source);
                case Stage.Groups:
                    return m_groups.MoveNext() ? OnityStageTasks.True : OnityStageTasks.False;
                default:
                    return Await(m_resultSelector, m_group.Key, m_group, m_storeResult);
            }
        }

        protected override bool AdvanceCore(out TResult current)
        {
            current = default;
            switch (m_stage)
            {
                case Stage.Move:
                    if (!m_moved)
                    {
                        m_stage = Stage.Release;
                        return false;
                    }
                    m_item = m_source.Current;
                    if (!m_keySelector.IsSync)
                    {
                        m_stage = Stage.Key;
                        return false;
                    }
                    m_key = m_keySelector.Invoke(m_item);
                    AddOrAwaitElement();
                    return false;
                case Stage.Key:
                    AddOrAwaitElement();
                    return false;
                case Stage.Element:
                    Add(m_element);
                    return false;
                case Stage.Release:
                    CompleteRelease();
                    m_source = null;
                    m_groups = m_lookup.GetEnumerator();
                    m_stage = Stage.Groups;
                    return false;
                case Stage.Groups:
                    m_group = m_groups.Current;
                    if (!m_resultSelector.IsSync)
                    {
                        m_stage = Stage.Result;
                        return false;
                    }
                    current = m_resultSelector.Invoke(m_group.Key, m_group);
                    return true;
                default:
                    current = m_result;
                    m_result = default;
                    m_stage = Stage.Groups;
                    return true;
            }
        }

        private void AddOrAwaitElement()
        {
            if (m_elementSelector.IsSync)
            {
                Add(m_elementSelector.Invoke(m_item));
            }
            else
            {
                m_stage = Stage.Element;
            }
        }

        private void Add(TElement element)
        {
            m_lookup.Add(m_key, element);
            m_item = default;
            m_key = default;
            m_element = default;
            m_stage = Stage.Move;
        }

        protected override void OnFinish()
        {
            m_source = null;
            m_lookup = null;
            m_groups = null;
            m_group = null;
            m_item = default;
            m_key = default;
            m_element = default;
            m_result = default;
        }
    }

    // Join and GroupJoin: reads the inner source into a lookup (null keys never match), disposes it,
    // then streams the outer source and yields its matches (Join) or one result per outer item
    // (GroupJoin).
    internal sealed class OnityJoinEnumerator<TOuter, TInner, TKey, TResult> : OnityStagedAsyncEnumerator<TResult>
    {
        private enum Stage
        {
            InnerMove,
            InnerKey,
            InnerRelease,
            OuterMove,
            OuterKey,
            Matches,
            Result
        }

        private readonly IOnityAsyncEnumerable<TOuter> m_outerDescription;
        private readonly IOnityAsyncEnumerable<TInner> m_innerDescription;
        private readonly OnityStreamFunc<TOuter, TKey> m_outerKeySelector;
        private readonly OnityStreamFunc<TInner, TKey> m_innerKeySelector;
        private readonly OnityStreamFunc<TOuter, TInner, TResult> m_joinSelector;
        private readonly OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult> m_groupSelector;
        private readonly bool m_groupJoin;
        private readonly IEqualityComparer<TKey> m_comparer;
        private readonly Action<bool> m_storeMoved;
        private readonly Action<TKey> m_storeKey;
        private readonly Action<TResult> m_storeResult;
        private OnityStageEnumerator<TOuter> m_outer;
        private OnityStageEnumerator<TInner> m_inner;
        private OnityLookup<TKey, TInner> m_lookup;
        private IEnumerator<TInner> m_matches;
        private IEnumerable<TInner> m_group;
        private TOuter m_outerItem;
        private TInner m_innerItem;
        private TKey m_key;
        private TResult m_result;
        private Stage m_stage;
        private bool m_moved;

        internal OnityJoinEnumerator(IOnityAsyncEnumerable<TOuter> outer, IOnityAsyncEnumerable<TInner> inner,
            OnityStreamFunc<TOuter, TKey> outerKeySelector, OnityStreamFunc<TInner, TKey> innerKeySelector,
            OnityStreamFunc<TOuter, TInner, TResult> joinSelector,
            OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult> groupSelector, bool groupJoin,
            IEqualityComparer<TKey> comparer, CancellationToken token) : base(token)
        {
            m_outerDescription = outer;
            m_innerDescription = inner;
            m_outerKeySelector = outerKeySelector;
            m_innerKeySelector = innerKeySelector;
            m_joinSelector = joinSelector;
            m_groupSelector = groupSelector;
            m_groupJoin = groupJoin;
            m_comparer = comparer;
            m_storeMoved = moved => m_moved = moved;
            m_storeKey = key => m_key = key;
            m_storeResult = result => m_result = result;
        }

        protected override OnityTask<bool> StepCore()
        {
            switch (m_stage)
            {
                case Stage.InnerMove:
                    m_inner ??= Acquire(m_innerDescription);
                    m_lookup ??= new OnityLookup<TKey, TInner>(m_comparer);
                    return OnityStageTasks.Continue(m_inner.MoveNextAsync(), m_storeMoved);
                case Stage.InnerKey:
                    return Await(m_innerKeySelector, m_innerItem, m_storeKey);
                case Stage.InnerRelease:
                    return Release(m_inner);
                case Stage.OuterMove:
                    return MoveOuter();
                case Stage.OuterKey:
                    return Await(m_outerKeySelector, m_outerItem, m_storeKey);
                case Stage.Matches:
                    if (m_matches != null && m_matches.MoveNext())
                    {
                        m_innerItem = m_matches.Current;
                        return OnityStageTasks.True;
                    }
                    m_matches = null;
                    m_stage = Stage.OuterMove;
                    return MoveOuter();
                default:
                    return m_groupJoin
                        ? Await(m_groupSelector, m_outerItem, m_group, m_storeResult)
                        : Await(m_joinSelector, m_outerItem, m_innerItem, m_storeResult);
            }
        }

        private OnityTask<bool> MoveOuter()
        {
            m_outer ??= Acquire(m_outerDescription);
            return m_outer.MoveNextAsync();
        }

        protected override bool AdvanceCore(out TResult current)
        {
            current = default;
            switch (m_stage)
            {
                case Stage.InnerMove:
                    if (!m_moved)
                    {
                        m_stage = Stage.InnerRelease;
                        return false;
                    }
                    m_innerItem = m_inner.Current;
                    if (!m_innerKeySelector.IsSync)
                    {
                        m_stage = Stage.InnerKey;
                        return false;
                    }
                    AddInner(m_innerKeySelector.Invoke(m_innerItem));
                    return false;
                case Stage.InnerKey:
                    AddInner(m_key);
                    return false;
                case Stage.InnerRelease:
                    CompleteRelease();
                    m_inner = null;
                    m_stage = Stage.OuterMove;
                    return false;
                case Stage.OuterMove:
                    m_outerItem = m_outer.Current;
                    if (!m_outerKeySelector.IsSync)
                    {
                        m_stage = Stage.OuterKey;
                        return false;
                    }
                    return Match(m_outerKeySelector.Invoke(m_outerItem), out current);
                case Stage.OuterKey:
                    return Match(m_key, out current);
                case Stage.Matches:
                    if (!m_joinSelector.IsSync)
                    {
                        m_stage = Stage.Result;
                        return false;
                    }
                    current = m_joinSelector.Invoke(m_outerItem, m_innerItem);
                    return true;
                default:
                    current = m_result;
                    m_result = default;
                    m_group = null;
                    m_stage = m_groupJoin ? Stage.OuterMove : Stage.Matches;
                    return true;
            }
        }

        private void AddInner(TKey key)
        {
            if (key != null)
            {
                m_lookup.Add(key, m_innerItem);
            }
            m_innerItem = default;
            m_key = default;
            m_stage = Stage.InnerMove;
        }

        private bool Match(TKey key, out TResult current)
        {
            current = default;
            m_key = default;
            if (!m_groupJoin)
            {
                m_matches = key == null ? null : m_lookup[key].GetEnumerator();
                m_stage = Stage.Matches;
                return false;
            }
            m_group = key == null ? Array.Empty<TInner>() : m_lookup[key];
            if (!m_groupSelector.IsSync)
            {
                m_stage = Stage.Result;
                return false;
            }
            current = m_groupSelector.Invoke(m_outerItem, m_group);
            m_group = null;
            m_stage = Stage.OuterMove;
            return true;
        }

        protected override void OnFinish()
        {
            m_outer = null;
            m_inner = null;
            m_lookup = null;
            m_matches = null;
            m_group = null;
            m_outerItem = default;
            m_innerItem = default;
            m_key = default;
            m_result = default;
        }
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- GroupBy ----------------------------------------------------------------------

        /// <summary>Groups the items by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TSource>> GroupBy<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, IOnityGrouping<TKey, TSource>>.Sync(
                    OnityGroupingProjection<TKey, TSource>.Instance), null);
        }

        /// <summary>Groups the items by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TSource>> GroupBy<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, IOnityGrouping<TKey, TSource>>.Sync(
                    OnityGroupingProjection<TKey, TSource>.Instance), comparer);
        }

        /// <summary>Groups the selected elements by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TElement>.Sync(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, IOnityGrouping<TKey, TElement>>.Sync(
                    OnityGroupingProjection<TKey, TElement>.Instance), null);
        }

        /// <summary>Groups the selected elements by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TElement>.Sync(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, IOnityGrouping<TKey, TElement>>.Sync(
                    OnityGroupingProjection<TKey, TElement>.Instance), comparer);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per group, in first-seen key order.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupBy<TSource, TKey, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TKey, IEnumerable<TSource>, TResult> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, TResult>.Sync(resultSelector), null);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per group, in first-seen key order.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupBy<TSource, TKey, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TKey, IEnumerable<TSource>, TResult> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, TResult>.Sync(resultSelector), comparer);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per group, in first-seen key order.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupBy<TSource, TKey, TElement, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector,
            Func<TKey, IEnumerable<TElement>, TResult> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TElement>.Sync(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, TResult>.Sync(resultSelector), null);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector invoked sequentially.</param>
        /// <param name="elementSelector">Element selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per group, in first-seen key order.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupBy<TSource, TKey, TElement, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector,
            Func<TKey, IEnumerable<TElement>, TResult> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Sync(keySelector),
                OnityStreamFunc<TSource, TElement>.Sync(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, TResult>.Sync(resultSelector), comparer);
        }

        /// <summary>Groups the items by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TSource>> GroupByAwait<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, IOnityGrouping<TKey, TSource>>.Sync(
                    OnityGroupingProjection<TKey, TSource>.Instance), null);
        }

        /// <summary>Groups the items by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TSource>> GroupByAwait<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, IOnityGrouping<TKey, TSource>>.Sync(
                    OnityGroupingProjection<TKey, TSource>.Instance), comparer);
        }

        /// <summary>Groups the selected elements by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited sequentially.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TElement>> GroupByAwait<TSource, TKey, TElement>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector,
            Func<TSource, OnityTask<TElement>> elementSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TElement>.Await(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, IOnityGrouping<TKey, TElement>>.Sync(
                    OnityGroupingProjection<TKey, TElement>.Instance), null);
        }

        /// <summary>Groups the selected elements by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited sequentially.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TElement>> GroupByAwait<TSource, TKey, TElement>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector,
            Func<TSource, OnityTask<TElement>> elementSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TElement>.Await(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, IOnityGrouping<TKey, TElement>>.Sync(
                    OnityGroupingProjection<TKey, TElement>.Instance), comparer);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per group, in first-seen key order.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwait<TSource, TKey, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector,
            Func<TKey, IEnumerable<TSource>, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, TResult>.Await(resultSelector), null);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per group, in first-seen key order.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwait<TSource, TKey, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector,
            Func<TKey, IEnumerable<TSource>, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, TResult>.Await(resultSelector), comparer);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per group, in first-seen key order.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwait<TSource, TKey, TElement, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector,
            Func<TSource, OnityTask<TElement>> elementSelector,
            Func<TKey, IEnumerable<TElement>, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TElement>.Await(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, TResult>.Await(resultSelector), null);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited sequentially.</param>
        /// <param name="elementSelector">Element selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per group, in first-seen key order.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwait<TSource, TKey, TElement, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<TKey>> keySelector,
            Func<TSource, OnityTask<TElement>> elementSelector,
            Func<TKey, IEnumerable<TElement>, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Await(keySelector),
                OnityStreamFunc<TSource, TElement>.Await(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, TResult>.Await(resultSelector), comparer);
        }

        /// <summary>Groups the items by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TSource>> GroupByAwaitWithCancellation<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, IOnityGrouping<TKey, TSource>>.Sync(
                    OnityGroupingProjection<TKey, TSource>.Instance), null);
        }

        /// <summary>Groups the items by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TSource>> GroupByAwaitWithCancellation<TSource, TKey>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, IOnityGrouping<TKey, TSource>>.Sync(
                    OnityGroupingProjection<TKey, TSource>.Instance), comparer);
        }

        /// <summary>Groups the selected elements by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <param name="elementSelector">Element selector awaited with an owned token.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TElement>> GroupByAwaitWithCancellation<TSource, TKey, TElement>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            Func<TSource, CancellationToken, OnityTask<TElement>> elementSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TElement>.Cancel(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, IOnityGrouping<TKey, TElement>>.Sync(
                    OnityGroupingProjection<TKey, TElement>.Instance), null);
        }

        /// <summary>Groups the selected elements by key, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <param name="elementSelector">Element selector awaited with an owned token.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<IOnityGrouping<TKey, TElement>> GroupByAwaitWithCancellation<TSource, TKey, TElement>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            Func<TSource, CancellationToken, OnityTask<TElement>> elementSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TElement>.Cancel(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, IOnityGrouping<TKey, TElement>>.Sync(
                    OnityGroupingProjection<TKey, TElement>.Instance), comparer);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per group, in first-seen key order.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwaitWithCancellation<TSource, TKey, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            Func<TKey, IEnumerable<TSource>, CancellationToken, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, TResult>.Cancel(resultSelector), null);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per group, in first-seen key order.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwaitWithCancellation<TSource, TKey, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            Func<TKey, IEnumerable<TSource>, CancellationToken, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TSource>.Sync(OnityLinqIdentity<TSource>.Instance),
                OnityStreamFunc<TKey, IEnumerable<TSource>, TResult>.Cancel(resultSelector), comparer);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <param name="elementSelector">Element selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per group, in first-seen key order.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwaitWithCancellation<TSource, TKey, TElement, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            Func<TSource, CancellationToken, OnityTask<TElement>> elementSelector,
            Func<TKey, IEnumerable<TElement>, CancellationToken, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TElement>.Cancel(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, TResult>.Cancel(resultSelector), null);
        }

        /// <summary>Groups the items by key and projects every group, after reading the whole source.</summary>
        /// <typeparam name="TSource">Item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TElement">Element type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="keySelector">Key selector awaited with an owned token.</param>
        /// <param name="elementSelector">Element selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per group, in first-seen key order.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the whole source into Onity's lookup (null keys
        /// allowed) and disposes the source; groups then follow in first-seen key order with elements in
        /// source order. Groupings are <see cref="IOnityGrouping{TKey, TElement}"/>, not System.Linq types.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupByAwaitWithCancellation<TSource, TKey, TElement, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<TKey>> keySelector,
            Func<TSource, CancellationToken, OnityTask<TElement>> elementSelector,
            Func<TKey, IEnumerable<TElement>, CancellationToken, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(keySelector, nameof(keySelector));
            OnityLinqCore.NotNull(elementSelector, nameof(elementSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateGroupBy(source, OnityStreamFunc<TSource, TKey>.Cancel(keySelector),
                OnityStreamFunc<TSource, TElement>.Cancel(elementSelector),
                OnityStreamFunc<TKey, IEnumerable<TElement>, TResult>.Cancel(resultSelector), comparer);
        }

        private static IOnityAsyncEnumerable<TResult> CreateGroupBy<TSource, TKey, TElement, TResult>(
            IOnityAsyncEnumerable<TSource> source, OnityStreamFunc<TSource, TKey> keySelector,
            OnityStreamFunc<TSource, TElement> elementSelector,
            OnityStreamFunc<TKey, IEnumerable<TElement>, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            return new OnityLinqDescription<TSource, TResult>(source,
                (upstream, token) => new OnityGroupByEnumerator<TSource, TKey, TElement, TResult>(
                    upstream, keySelector, elementSelector, resultSelector, comparer, token));
        }

        // ---- Join --------------------------------------------------------------------------------

        /// <summary>Correlates outer and inner items that share a key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector invoked sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per matching pair.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order, each followed by its
        /// matches in inner order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Sync(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Sync(innerKeySelector),
                OnityStreamFunc<TOuter, TInner, TResult>.Sync(resultSelector), default, false, null);
        }

        /// <summary>Correlates outer and inner items that share a key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector invoked sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per matching pair.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order, each followed by its
        /// matches in inner order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Sync(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Sync(innerKeySelector),
                OnityStreamFunc<TOuter, TInner, TResult>.Sync(resultSelector), default, false, comparer);
        }

        /// <summary>Correlates outer and inner items that share a key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per matching pair.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order, each followed by its
        /// matches in inner order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> JoinAwait<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, OnityTask<TKey>> outerKeySelector,
            Func<TInner, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, TInner, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Await(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Await(innerKeySelector),
                OnityStreamFunc<TOuter, TInner, TResult>.Await(resultSelector), default, false, null);
        }

        /// <summary>Correlates outer and inner items that share a key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per matching pair.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order, each followed by its
        /// matches in inner order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> JoinAwait<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, OnityTask<TKey>> outerKeySelector,
            Func<TInner, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, TInner, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Await(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Await(innerKeySelector),
                OnityStreamFunc<TOuter, TInner, TResult>.Await(resultSelector), default, false, comparer);
        }

        /// <summary>Correlates outer and inner items that share a key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited with an owned token.</param>
        /// <param name="innerKeySelector">Inner key selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per matching pair.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order, each followed by its
        /// matches in inner order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> JoinAwaitWithCancellation<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, CancellationToken, OnityTask<TKey>> outerKeySelector,
            Func<TInner, CancellationToken, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, TInner, CancellationToken, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Cancel(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Cancel(innerKeySelector),
                OnityStreamFunc<TOuter, TInner, TResult>.Cancel(resultSelector), default, false, null);
        }

        /// <summary>Correlates outer and inner items that share a key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited with an owned token.</param>
        /// <param name="innerKeySelector">Inner key selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per matching pair.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order, each followed by its
        /// matches in inner order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> JoinAwaitWithCancellation<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, CancellationToken, OnityTask<TKey>> outerKeySelector,
            Func<TInner, CancellationToken, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, TInner, CancellationToken, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Cancel(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Cancel(innerKeySelector),
                OnityStreamFunc<TOuter, TInner, TResult>.Cancel(resultSelector), default, false, comparer);
        }

        // ---- GroupJoin ---------------------------------------------------------------------------

        /// <summary>Correlates every outer item with the group of inner items that share its key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector invoked sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per outer item with its (possibly empty)
        /// group.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupJoin<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, IEnumerable<TInner>, TResult> resultSelector)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Sync(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Sync(innerKeySelector),
                default, OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult>.Sync(resultSelector),
                true, null);
        }

        /// <summary>Correlates every outer item with the group of inner items that share its key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector invoked sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector invoked sequentially.</param>
        /// <param name="resultSelector">Projection invoked sequentially once per outer item with its (possibly empty)
        /// group.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupJoin<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, IEnumerable<TInner>, TResult> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Sync(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Sync(innerKeySelector),
                default, OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult>.Sync(resultSelector),
                true, comparer);
        }

        /// <summary>Correlates every outer item with the group of inner items that share its key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per outer item with its (possibly empty)
        /// group.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupJoinAwait<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, OnityTask<TKey>> outerKeySelector,
            Func<TInner, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, IEnumerable<TInner>, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Await(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Await(innerKeySelector),
                default, OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult>.Await(resultSelector),
                true, null);
        }

        /// <summary>Correlates every outer item with the group of inner items that share its key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited sequentially.</param>
        /// <param name="innerKeySelector">Inner key selector awaited sequentially.</param>
        /// <param name="resultSelector">Projection awaited sequentially once per outer item with its (possibly empty)
        /// group.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupJoinAwait<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, OnityTask<TKey>> outerKeySelector,
            Func<TInner, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, IEnumerable<TInner>, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Await(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Await(innerKeySelector),
                default, OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult>.Await(resultSelector),
                true, comparer);
        }

        /// <summary>Correlates every outer item with the group of inner items that share its key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited with an owned token.</param>
        /// <param name="innerKeySelector">Inner key selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per outer item with its (possibly empty)
        /// group.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupJoinAwaitWithCancellation<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, CancellationToken, OnityTask<TKey>> outerKeySelector,
            Func<TInner, CancellationToken, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, IEnumerable<TInner>, CancellationToken, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Cancel(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Cancel(innerKeySelector),
                default, OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult>.Cancel(resultSelector),
                true, null);
        }

        /// <summary>Correlates every outer item with the group of inner items that share its key.</summary>
        /// <typeparam name="TOuter">Outer item type.</typeparam>
        /// <typeparam name="TInner">Inner item type.</typeparam>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="outer">Outer description, streamed.</param>
        /// <param name="inner">Inner description, read completely first.</param>
        /// <param name="outerKeySelector">Outer key selector awaited with an owned token.</param>
        /// <param name="innerKeySelector">Inner key selector awaited with an owned token.</param>
        /// <param name="resultSelector">Projection awaited with an owned token once per outer item with its (possibly empty)
        /// group.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <returns>A lazy description. The first move reads the inner source into a lookup and disposes it
        /// before the outer source is acquired; outer items then stream in order. A null key never
        /// matches, as in System.Linq.</returns>
        /// <exception cref="ArgumentNullException">A source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> GroupJoinAwaitWithCancellation<TOuter, TInner, TKey, TResult>(
            this IOnityAsyncEnumerable<TOuter> outer,
            IOnityAsyncEnumerable<TInner> inner,
            Func<TOuter, CancellationToken, OnityTask<TKey>> outerKeySelector,
            Func<TInner, CancellationToken, OnityTask<TKey>> innerKeySelector,
            Func<TOuter, IEnumerable<TInner>, CancellationToken, OnityTask<TResult>> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            OnityLinqCore.NotNull(outer, nameof(outer));
            OnityLinqCore.NotNull(inner, nameof(inner));
            OnityLinqCore.NotNull(outerKeySelector, nameof(outerKeySelector));
            OnityLinqCore.NotNull(innerKeySelector, nameof(innerKeySelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateJoin(outer, inner, OnityStreamFunc<TOuter, TKey>.Cancel(outerKeySelector),
                OnityStreamFunc<TInner, TKey>.Cancel(innerKeySelector),
                default, OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult>.Cancel(resultSelector),
                true, comparer);
        }

        private static IOnityAsyncEnumerable<TResult> CreateJoin<TOuter, TInner, TKey, TResult>(
            IOnityAsyncEnumerable<TOuter> outer, IOnityAsyncEnumerable<TInner> inner,
            OnityStreamFunc<TOuter, TKey> outerKeySelector, OnityStreamFunc<TInner, TKey> innerKeySelector,
            OnityStreamFunc<TOuter, TInner, TResult> joinSelector,
            OnityStreamFunc<TOuter, IEnumerable<TInner>, TResult> groupSelector, bool groupJoin,
            IEqualityComparer<TKey> comparer)
        {
            return new OnityLinqDescription<TOuter, TResult>(outer,
                (upstream, token) => new OnityJoinEnumerator<TOuter, TInner, TKey, TResult>(upstream, inner,
                    outerKeySelector, innerKeySelector, joinSelector, groupSelector, groupJoin, comparer, token));
        }
    }

    internal static class OnityGroupingProjection<TKey, TElement>
    {
        // The grouping overloads pass the lookup's own grouping object through the result selector.
        internal static readonly Func<TKey, IEnumerable<TElement>, IOnityGrouping<TKey, TElement>> Instance =
            (key, elements) => (IOnityGrouping<TKey, TElement>)elements;
    }
}
