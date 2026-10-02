using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // Flattens one inner stream per outer item, sequentially: the inner stream is acquired after its
    // collection selector ran, enumerated to its end and disposed (awaited) before the outer stream
    // moves again. Only the outer end ends the stream.
    internal sealed class OnitySelectManyEnumerator<TSource, TCollection, TResult> : OnityStagedAsyncEnumerator<TResult>
    {
        private enum Stage
        {
            Outer,
            Collection,
            Inner,
            Result,
            ReleaseInner
        }

        private readonly IOnityAsyncEnumerable<TSource> m_source;
        private readonly Func<TSource, int, IOnityAsyncEnumerable<TCollection>> m_collection;
        private readonly Func<TSource, int, CancellationToken, OnityTask<IOnityAsyncEnumerable<TCollection>>>
            m_awaitCollection;
        private readonly bool m_collectionUsesToken;
        private readonly OnityStreamFunc<TSource, TCollection, TResult> m_resultSelector;
        private readonly Action<IOnityAsyncEnumerable<TCollection>> m_storeCollection;
        private readonly Action<bool> m_storeInnerMoved;
        private readonly Action<TResult> m_storeResult;
        private OnityStageEnumerator<TSource> m_outer;
        private OnityStageEnumerator<TCollection> m_inner;
        private IOnityAsyncEnumerable<TCollection> m_selected;
        private TSource m_item;
        private TCollection m_innerItem;
        private TResult m_result;
        private Stage m_stage;
        private int m_index;
        private int m_itemIndex;
        private bool m_innerMoved;

        internal OnitySelectManyEnumerator(IOnityAsyncEnumerable<TSource> source,
            Func<TSource, int, IOnityAsyncEnumerable<TCollection>> collection,
            Func<TSource, int, CancellationToken, OnityTask<IOnityAsyncEnumerable<TCollection>>> awaitCollection,
            bool collectionUsesToken, OnityStreamFunc<TSource, TCollection, TResult> resultSelector,
            CancellationToken token) : base(token)
        {
            m_source = source;
            m_collection = collection;
            m_awaitCollection = awaitCollection;
            m_collectionUsesToken = collectionUsesToken;
            m_resultSelector = resultSelector;
            m_storeCollection = selected => m_selected = selected;
            m_storeInnerMoved = moved => m_innerMoved = moved;
            m_storeResult = result => m_result = result;
        }

        protected override OnityTask<bool> StepCore()
        {
            switch (m_stage)
            {
                case Stage.Outer:
                    m_outer ??= Acquire(m_source);
                    return m_outer.MoveNextAsync();
                case Stage.Collection:
                    CancellationToken token = m_collectionUsesToken ? DelegateToken : Token;
                    if (token.IsCancellationRequested)
                    {
                        return OnityStageTasks.Failed(OnityStageTasks.Canceled(token));
                    }
                    return OnityStageTasks.Continue(m_awaitCollection(m_item, m_itemIndex, token), m_storeCollection);
                case Stage.Inner:
                    return OnityStageTasks.Continue(m_inner.MoveNextAsync(), m_storeInnerMoved);
                case Stage.Result:
                    return Await(m_resultSelector, m_item, m_innerItem, m_storeResult);
                default:
                    return Release(m_inner);
            }
        }

        protected override bool AdvanceCore(out TResult current)
        {
            current = default;
            switch (m_stage)
            {
                case Stage.Outer:
                    m_item = m_outer.Current;
                    m_itemIndex = checked(m_index++);
                    if (m_collection == null)
                    {
                        m_stage = Stage.Collection;
                        return false;
                    }
                    BeginInner(m_collection(m_item, m_itemIndex));
                    return false;
                case Stage.Collection:
                    IOnityAsyncEnumerable<TCollection> selected = m_selected;
                    m_selected = null;
                    BeginInner(selected);
                    return false;
                case Stage.Inner:
                    if (!m_innerMoved)
                    {
                        m_stage = Stage.ReleaseInner;
                        return false;
                    }
                    m_innerItem = m_inner.Current;
                    if (!m_resultSelector.IsSync)
                    {
                        m_stage = Stage.Result;
                        return false;
                    }
                    current = m_resultSelector.Invoke(m_item, m_innerItem);
                    m_innerItem = default;
                    return true;
                case Stage.Result:
                    current = m_result;
                    m_result = default;
                    m_innerItem = default;
                    m_stage = Stage.Inner;
                    return true;
                default:
                    CompleteRelease();
                    m_inner = null;
                    m_item = default;
                    m_stage = Stage.Outer;
                    return false;
            }
        }

        private void BeginInner(IOnityAsyncEnumerable<TCollection> selected)
        {
            if (selected == null)
            {
                throw new InvalidOperationException("The collection selector returned a null stream.");
            }
            m_inner = Acquire(selected);
            m_stage = Stage.Inner;
        }

        protected override void OnFinish()
        {
            m_outer = null;
            m_inner = null;
            m_selected = null;
            m_item = default;
            m_innerItem = default;
            m_result = default;
        }
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- SelectMany --------------------------------------------------------------------

        /// <summary>Flattens one inner stream per item, one inner stream at a time.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TResult">Inner item type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="selector">Selects the inner stream of an item; a null stream is a fault.</param>
        /// <returns>A lazy description. Each inner stream is acquired with the enumeration token, read
        /// to its end and disposed (awaited) before the outer stream moves again; only the outer end ends
        /// the stream. Faults of the outer stream, an inner stream, an inner disposal or a delegate fault
        /// the stream. Upstreams are disposed inner first, then outer.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectMany<TSource, TResult>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, IOnityAsyncEnumerable<TResult>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateSelectMany(source, (item, index) => selector(item), null, false,
                SelectManyIdentity<TSource, TResult>());
        }

        /// <summary>Flattens one inner stream per item, with the item's zero-based index.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TResult">Inner item type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="selector">Selects the inner stream of an item and its index.</param>
        /// <returns>A lazy description with the semantics of the non-indexed overload.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectMany<TSource, TResult>(
            this IOnityAsyncEnumerable<TSource> source, Func<TSource, int, IOnityAsyncEnumerable<TResult>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateSelectMany(source, selector, null, false, SelectManyIdentity<TSource, TResult>());
        }

        /// <summary>Flattens one inner stream per item and projects each pair.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TCollection">Inner item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="collectionSelector">Selects the inner stream of an item.</param>
        /// <param name="resultSelector">Projects an outer item and one of its inner items.</param>
        /// <returns>A lazy description with the semantics of the single-selector overload.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectMany<TSource, TCollection, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, IOnityAsyncEnumerable<TCollection>> collectionSelector,
            Func<TSource, TCollection, TResult> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(collectionSelector, nameof(collectionSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateSelectMany(source, (item, index) => collectionSelector(item), null, false,
                OnityStreamFunc<TSource, TCollection, TResult>.Sync(resultSelector));
        }

        /// <summary>Flattens one inner stream per indexed item and projects each pair.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TCollection">Inner item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="collectionSelector">Selects the inner stream of an item and its index.</param>
        /// <param name="resultSelector">Projects an outer item and one of its inner items.</param>
        /// <returns>A lazy description with the semantics of the single-selector overload.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectMany<TSource, TCollection, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, int, IOnityAsyncEnumerable<TCollection>> collectionSelector,
            Func<TSource, TCollection, TResult> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(collectionSelector, nameof(collectionSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateSelectMany(source, collectionSelector, null, false,
                OnityStreamFunc<TSource, TCollection, TResult>.Sync(resultSelector));
        }

        /// <summary>Flattens one asynchronously selected inner stream per item.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TResult">Inner item type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="selector">Selector awaited for each item.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwait<TSource, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<IOnityAsyncEnumerable<TResult>>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateSelectMany(source, null, (item, index, token) => selector(item), false,
                SelectManyIdentity<TSource, TResult>());
        }

        /// <summary>Flattens one asynchronously selected inner stream per indexed item.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TResult">Inner item type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="selector">Selector awaited for each item and its index.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwait<TSource, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, int, OnityTask<IOnityAsyncEnumerable<TResult>>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateSelectMany(source, null, (item, index, token) => selector(item, index), false,
                SelectManyIdentity<TSource, TResult>());
        }

        /// <summary>Flattens asynchronously selected inner streams and projects each pair asynchronously.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TCollection">Inner item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="collectionSelector">Selector awaited for each outer item.</param>
        /// <param name="resultSelector">Projection awaited for each inner item.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwait<TSource, TCollection, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, OnityTask<IOnityAsyncEnumerable<TCollection>>> collectionSelector,
            Func<TSource, TCollection, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(collectionSelector, nameof(collectionSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateSelectMany(source, null, (item, index, token) => collectionSelector(item), false,
                OnityStreamFunc<TSource, TCollection, TResult>.Await(resultSelector));
        }

        /// <summary>Flattens asynchronously selected inner streams of indexed items and projects each pair
        /// asynchronously.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TCollection">Inner item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="collectionSelector">Selector awaited for each outer item and its index.</param>
        /// <param name="resultSelector">Projection awaited for each inner item.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwait<TSource, TCollection, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, int, OnityTask<IOnityAsyncEnumerable<TCollection>>> collectionSelector,
            Func<TSource, TCollection, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(collectionSelector, nameof(collectionSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateSelectMany(source, null, (item, index, token) => collectionSelector(item, index), false,
                OnityStreamFunc<TSource, TCollection, TResult>.Await(resultSelector));
        }

        /// <summary>Flattens inner streams selected by a token-aware asynchronous selector.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TResult">Inner item type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="selector">Selector receiving a token owned by the enumeration; it is canceled when
        /// the enumeration is disposed, and disposal waits for a pending selector.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwaitWithCancellation<TSource, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<IOnityAsyncEnumerable<TResult>>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateSelectMany(source, null, (item, index, token) => selector(item, token), true,
                SelectManyIdentity<TSource, TResult>());
        }

        /// <summary>Flattens inner streams selected by a token-aware asynchronous selector of indexed items.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TResult">Inner item type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="selector">Selector receiving the item, its index and a token owned by the enumeration.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwaitWithCancellation<TSource, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, int, CancellationToken, OnityTask<IOnityAsyncEnumerable<TResult>>> selector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateSelectMany(source, null, selector, true, SelectManyIdentity<TSource, TResult>());
        }

        /// <summary>Flattens inner streams and projects each pair with token-aware asynchronous selectors.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TCollection">Inner item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="collectionSelector">Selector receiving a token owned by the enumeration.</param>
        /// <param name="resultSelector">Projection receiving the same owned token.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwaitWithCancellation<TSource, TCollection, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, CancellationToken, OnityTask<IOnityAsyncEnumerable<TCollection>>> collectionSelector,
            Func<TSource, TCollection, CancellationToken, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(collectionSelector, nameof(collectionSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateSelectMany(source, null, (item, index, token) => collectionSelector(item, token), true,
                OnityStreamFunc<TSource, TCollection, TResult>.Cancel(resultSelector));
        }

        /// <summary>Flattens inner streams of indexed items and projects each pair with token-aware
        /// asynchronous selectors.</summary>
        /// <typeparam name="TSource">Outer item type.</typeparam>
        /// <typeparam name="TCollection">Inner item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Outer description.</param>
        /// <param name="collectionSelector">Selector receiving the item, its index and an owned token.</param>
        /// <param name="resultSelector">Projection receiving the same owned token.</param>
        /// <returns>A lazy description with the semantics of the synchronous overload.</returns>
        /// <exception cref="ArgumentNullException">Source or a selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectManyAwaitWithCancellation<TSource, TCollection, TResult>(
            this IOnityAsyncEnumerable<TSource> source,
            Func<TSource, int, CancellationToken, OnityTask<IOnityAsyncEnumerable<TCollection>>> collectionSelector,
            Func<TSource, TCollection, CancellationToken, OnityTask<TResult>> resultSelector)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(collectionSelector, nameof(collectionSelector));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateSelectMany(source, null, collectionSelector, true,
                OnityStreamFunc<TSource, TCollection, TResult>.Cancel(resultSelector));
        }

        private static OnityStreamFunc<TSource, TResult, TResult> SelectManyIdentity<TSource, TResult>()
        {
            return OnityStreamFunc<TSource, TResult, TResult>.Sync(OnitySelectManyIdentity<TSource, TResult>.Instance);
        }

        private static IOnityAsyncEnumerable<TResult> CreateSelectMany<TSource, TCollection, TResult>(
            IOnityAsyncEnumerable<TSource> source, Func<TSource, int, IOnityAsyncEnumerable<TCollection>> collection,
            Func<TSource, int, CancellationToken, OnityTask<IOnityAsyncEnumerable<TCollection>>> awaitCollection,
            bool collectionUsesToken, OnityStreamFunc<TSource, TCollection, TResult> resultSelector)
        {
            return new OnityLinqDescription<TSource, TResult>(source,
                (upstream, token) => new OnitySelectManyEnumerator<TSource, TCollection, TResult>(
                    upstream, collection, awaitCollection, collectionUsesToken, resultSelector, token));
        }
    }

    internal static class OnitySelectManyIdentity<TSource, TResult>
    {
        internal static readonly Func<TSource, TResult, TResult> Instance = (item, inner) => inner;
    }
}
