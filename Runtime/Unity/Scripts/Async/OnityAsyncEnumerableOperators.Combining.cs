using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityAppendEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly T m_element;

        internal OnityAppendEnumerator(IOnityAsyncEnumerable<T> source, T element, CancellationToken token)
            : base(source, token)
        {
            m_element = element;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            return !IsClosing;
        }

        // The upstream is disposed before the appended item is published.
        protected override bool ReadEndCore(out T current)
        {
            current = m_element;
            return true;
        }
    }

    internal sealed class OnityPrependEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly T m_element;
        private readonly CancellationToken m_token;
        private bool m_prepended;
        private bool m_yieldElement;

        internal OnityPrependEnumerator(IOnityAsyncEnumerable<T> source, T element, CancellationToken token)
            : base(source, token)
        {
            m_element = element;
            m_token = token;
        }

        protected override OnityTask<bool> MoveCore()
        {
            if (!m_prepended)
            {
                if (IsClosing)
                {
                    return OnityStageTasks.False;
                }
                if (m_token.IsCancellationRequested)
                {
                    return OnityStageTasks.Failed(OnityStageTasks.Canceled(m_token));
                }
                m_prepended = true;
                m_yieldElement = true;
                return OnityStageTasks.True;
            }
            m_yieldElement = false;
            return base.MoveCore();
        }

        protected override bool ReadCurrentCore(out T current)
        {
            if (m_yieldElement)
            {
                current = m_element;
                return true;
            }
            current = UpstreamCurrent;
            return !IsClosing;
        }
    }

    // Enumerates the sources one after another; each source is disposed before the next one is
    // acquired, and only the last source's end ends the stream.
    internal sealed class OnityConcatEnumerator<T> : OnityStagedAsyncEnumerator<T>
    {
        private enum Stage
        {
            Move,
            Release
        }

        private readonly IOnityAsyncEnumerable<T>[] m_sources;
        private readonly Action<bool> m_storeMoved;
        private OnityStageEnumerator<T> m_current;
        private Stage m_stage;
        private int m_index;
        private bool m_moved;

        internal OnityConcatEnumerator(IOnityAsyncEnumerable<T>[] sources, CancellationToken token) : base(token)
        {
            m_sources = sources;
            m_storeMoved = moved => m_moved = moved;
        }

        private bool IsLast => m_index == m_sources.Length - 1;

        protected override OnityTask<bool> StepCore()
        {
            if (m_stage == Stage.Release)
            {
                return Release(m_current);
            }
            m_current ??= Acquire(m_sources[m_index]);
            return IsLast
                ? m_current.MoveNextAsync()
                : OnityStageTasks.Continue(m_current.MoveNextAsync(), m_storeMoved);
        }

        protected override bool AdvanceCore(out T current)
        {
            current = default;
            if (m_stage == Stage.Release)
            {
                CompleteRelease();
                m_current = null;
                m_index++;
                m_stage = Stage.Move;
                return false;
            }
            if (IsLast || m_moved)
            {
                current = m_current.Current;
                return true;
            }
            m_stage = Stage.Release;
            return false;
        }

        protected override void OnFinish()
        {
            m_current = null;
        }
    }

    // Moves every source that is idle each time the consumer asks for an item and relays items in
    // arrival order; each source has at most one outstanding move.
    internal sealed class OnityMergeEnumerator<T> : OnityMultiSourceAsyncEnumerator<T>
    {
        private int m_ended;

        private OnityMergeEnumerator(OnityStreamSlot[] slots, CancellationToken token) : base(slots, 0, token)
        {
        }

        internal static OnityMergeEnumerator<T> Create(IOnityAsyncEnumerable<T>[] sources, CancellationToken token)
        {
            var slots = new OnityStreamSlot[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                slots[i] = new OnityStreamSlot<T>(sources[i]);
            }
            return new OnityMergeEnumerator<T>(slots, token);
        }

        protected override void OnRequest()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                RequestMove(GetSlot(i));
            }
        }

        protected override void OnItem(OnityStreamSlot slot) => Emit(((OnityStreamSlot<T>)slot).Value);

        protected override void OnEnd(OnityStreamSlot slot)
        {
            if (++m_ended == SlotCount)
            {
                Complete();
            }
        }
    }

    internal sealed class OnityMergeAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T>[] m_sources;

        internal OnityMergeAsyncEnumerable(IOnityAsyncEnumerable<T>[] sources)
        {
            m_sources = sources;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return OnityMergeEnumerator<T>.Create(m_sources, cancellationToken);
        }

        // Validates and snapshots the sources so later changes to the caller's array have no effect.
        internal static IOnityAsyncEnumerable<T> Create(IOnityAsyncEnumerable<T>[] sources, string name)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(name);
            }
            if (sources.Length == 0)
            {
                throw new ArgumentException("No source async enumerable to merge.", name);
            }
            var snapshot = new IOnityAsyncEnumerable<T>[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                snapshot[i] = sources[i] ?? throw new ArgumentException("A merged source is null.", name);
            }
            return new OnityMergeAsyncEnumerable<T>(snapshot);
        }
    }

    internal sealed class OnityConcatAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T>[] m_sources;

        internal OnityConcatAsyncEnumerable(IOnityAsyncEnumerable<T>[] sources)
        {
            m_sources = sources;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new OnityConcatEnumerator<T>(m_sources, cancellationToken);
        }
    }

    // Moves the first source, then the second, and combines the pair; the stream ends as soon as
    // either source ends (the second is not moved after the first ended).
    internal sealed class OnityZipEnumerator<TFirst, TSecond, TResult> : OnityStagedAsyncEnumerator<TResult>
    {
        private enum Stage
        {
            First,
            Second,
            Select
        }

        private readonly IOnityAsyncEnumerable<TFirst> m_firstSource;
        private readonly IOnityAsyncEnumerable<TSecond> m_secondSource;
        private readonly OnityStreamFunc<TFirst, TSecond, TResult> m_selector;
        private readonly Action<TResult> m_storeResult;
        private OnityStageEnumerator<TFirst> m_first;
        private OnityStageEnumerator<TSecond> m_second;
        private TFirst m_firstValue;
        private TSecond m_secondValue;
        private TResult m_result;
        private Stage m_stage;

        internal OnityZipEnumerator(IOnityAsyncEnumerable<TFirst> first, IOnityAsyncEnumerable<TSecond> second,
            OnityStreamFunc<TFirst, TSecond, TResult> selector, CancellationToken token) : base(token)
        {
            m_firstSource = first;
            m_secondSource = second;
            m_selector = selector;
            m_storeResult = value => m_result = value;
        }

        protected override bool DisposeInAcquisitionOrder => true;

        protected override OnityTask<bool> StepCore()
        {
            if (m_stage == Stage.First)
            {
                if (m_first == null)
                {
                    m_first = Acquire(m_firstSource);
                    m_second = Acquire(m_secondSource);
                }
                return m_first.MoveNextAsync();
            }
            if (m_stage == Stage.Second)
            {
                return m_second.MoveNextAsync();
            }
            return Await(m_selector, m_firstValue, m_secondValue, m_storeResult);
        }

        protected override bool AdvanceCore(out TResult current)
        {
            current = default;
            if (m_stage == Stage.First)
            {
                m_firstValue = m_first.Current;
                m_stage = Stage.Second;
                return false;
            }
            if (m_stage == Stage.Second)
            {
                m_secondValue = m_second.Current;
                if (!m_selector.IsSync)
                {
                    m_stage = Stage.Select;
                    return false;
                }
                current = m_selector.Invoke(m_firstValue, m_secondValue);
            }
            else
            {
                current = m_result;
                m_result = default;
            }
            m_firstValue = default;
            m_secondValue = default;
            m_stage = Stage.First;
            return true;
        }

        protected override void OnFinish()
        {
            m_firstValue = default;
            m_secondValue = default;
            m_result = default;
        }
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- Append / Prepend / Concat ------------------------------------------------------

        /// <summary>Yields the source, then one more item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="element">Item yielded after the source ends normally.</param>
        /// <returns>A lazy description; the upstream is disposed before the appended item is published.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Append<T>(this IOnityAsyncEnumerable<T> source, T element)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityAppendEnumerator<T>(upstream, element, token));
        }

        /// <summary>Yields one item, then the source.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="element">Item yielded first.</param>
        /// <returns>A lazy description; the source is acquired at the second move, and a canceled
        /// enumeration token cancels the first move.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Prepend<T>(this IOnityAsyncEnumerable<T> source, T element)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityPrependEnumerator<T>(upstream, element, token));
        }

        /// <summary>Yields the first source, then the second.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">Source enumerated first.</param>
        /// <param name="second">Source enumerated after the first ends normally.</param>
        /// <returns>A lazy description. The first source is disposed (and its disposal awaited) before the
        /// second is acquired; a fault of either source ends the stream.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Concat<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            return new OnityConcatAsyncEnumerable<T>(new[] { first, second });
        }

        // ---- Merge -------------------------------------------------------------------------

        /// <summary>Interleaves two sources in arrival order.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source.</param>
        /// <returns>A lazy description; see
        /// <see cref="OnityAsyncEnumerable.Merge{T}(IOnityAsyncEnumerable{T}[])"/>.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Merge<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            return new OnityMergeAsyncEnumerable<T>(new[] { first, second });
        }

        /// <summary>Interleaves three sources in arrival order.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source.</param>
        /// <param name="third">Third source.</param>
        /// <returns>A lazy description; see
        /// <see cref="OnityAsyncEnumerable.Merge{T}(IOnityAsyncEnumerable{T}[])"/>.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<T> Merge<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second, IOnityAsyncEnumerable<T> third)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            OnityLinqCore.NotNull(third, nameof(third));
            return new OnityMergeAsyncEnumerable<T>(new[] { first, second, third });
        }

        /// <summary>Interleaves a sequence of sources in arrival order.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="sources">Sources, copied when this method is called.</param>
        /// <returns>A lazy description; see
        /// <see cref="OnityAsyncEnumerable.Merge{T}(IOnityAsyncEnumerable{T}[])"/>.</returns>
        /// <exception cref="ArgumentNullException">The sequence is null.</exception>
        /// <exception cref="ArgumentException">The sequence is empty or contains null.</exception>
        public static IOnityAsyncEnumerable<T> Merge<T>(this IEnumerable<IOnityAsyncEnumerable<T>> sources)
        {
            OnityLinqCore.NotNull(sources, nameof(sources));
            if (sources is IOnityAsyncEnumerable<T>[] array)
            {
                return OnityMergeAsyncEnumerable<T>.Create(array, nameof(sources));
            }
            var list = new List<IOnityAsyncEnumerable<T>>();
            foreach (IOnityAsyncEnumerable<T> source in sources)
            {
                list.Add(source);
            }
            return OnityMergeAsyncEnumerable<T>.Create(list.ToArray(), nameof(sources));
        }

        // ---- Zip ---------------------------------------------------------------------------

        /// <summary>Pairs the items of two sources by position.</summary>
        /// <typeparam name="TFirst">First item type.</typeparam>
        /// <typeparam name="TSecond">Second item type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source.</param>
        /// <returns>A lazy description of named tuples with the semantics of the selector overload.</returns>
        /// <exception cref="ArgumentNullException">A source is null.</exception>
        public static IOnityAsyncEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(
            this IOnityAsyncEnumerable<TFirst> first, IOnityAsyncEnumerable<TSecond> second)
        {
            return Zip(first, second, (x, y) => (x, y));
        }

        /// <summary>Combines the items of two sources by position.</summary>
        /// <typeparam name="TFirst">First item type.</typeparam>
        /// <typeparam name="TSecond">Second item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source.</param>
        /// <param name="resultSelector">Selector invoked outside lifecycle locks; exceptions, including
        /// OCE, are faults.</param>
        /// <returns>A lazy description. Each move moves the first source and then the second, so the
        /// second source is not moved once the first has ended. The stream ends when either source ends.
        /// Both sources are acquired at the first move and disposed in argument order.</returns>
        /// <exception cref="ArgumentNullException">A source or the selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> Zip<TFirst, TSecond, TResult>(
            this IOnityAsyncEnumerable<TFirst> first, IOnityAsyncEnumerable<TSecond> second,
            Func<TFirst, TSecond, TResult> resultSelector)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return CreateZip(first, second, OnityStreamFunc<TFirst, TSecond, TResult>.Sync(resultSelector));
        }

        /// <summary>Combines the items of two sources by position with an asynchronous selector.</summary>
        /// <typeparam name="TFirst">First item type.</typeparam>
        /// <typeparam name="TSecond">Second item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source.</param>
        /// <param name="selector">Selector awaited after both items arrived.</param>
        /// <returns>A lazy description with the ordering and disposal of the synchronous Zip.</returns>
        /// <exception cref="ArgumentNullException">A source or the selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> ZipAwait<TFirst, TSecond, TResult>(
            this IOnityAsyncEnumerable<TFirst> first, IOnityAsyncEnumerable<TSecond> second,
            Func<TFirst, TSecond, OnityTask<TResult>> selector)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateZip(first, second, OnityStreamFunc<TFirst, TSecond, TResult>.Await(selector));
        }

        /// <summary>Combines the items of two sources by position with a token-aware asynchronous selector.</summary>
        /// <typeparam name="TFirst">First item type.</typeparam>
        /// <typeparam name="TSecond">Second item type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="first">First source.</param>
        /// <param name="second">Second source.</param>
        /// <param name="selector">Selector receiving a token owned by the enumeration, canceled when it is
        /// disposed; disposal waits for a pending selector.</param>
        /// <returns>A lazy description with the ordering and disposal of the synchronous Zip.</returns>
        /// <exception cref="ArgumentNullException">A source or the selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> ZipAwaitWithCancellation<TFirst, TSecond, TResult>(
            this IOnityAsyncEnumerable<TFirst> first, IOnityAsyncEnumerable<TSecond> second,
            Func<TFirst, TSecond, CancellationToken, OnityTask<TResult>> selector)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return CreateZip(first, second, OnityStreamFunc<TFirst, TSecond, TResult>.Cancel(selector));
        }

        private static IOnityAsyncEnumerable<TResult> CreateZip<TFirst, TSecond, TResult>(
            IOnityAsyncEnumerable<TFirst> first, IOnityAsyncEnumerable<TSecond> second,
            OnityStreamFunc<TFirst, TSecond, TResult> selector)
        {
            return new OnityLinqDescription<TFirst, TResult>(first,
                (upstream, token) => new OnityZipEnumerator<TFirst, TSecond, TResult>(
                    upstream, second, selector, token));
        }
    }
}
