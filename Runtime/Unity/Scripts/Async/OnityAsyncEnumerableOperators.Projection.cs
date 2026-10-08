using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityOfTypeEnumerator<TResult> : OnitySourceAsyncEnumerator<object, TResult>
    {
        internal OnityOfTypeEnumerator(IOnityAsyncEnumerable<object> source, CancellationToken token)
            : base(source, token)
        {
        }

        protected override bool ReadCurrentCore(out TResult current)
        {
            object value = UpstreamCurrent;
            if (!IsClosing && value is TResult typed)
            {
                current = typed;
                return true;
            }
            current = default;
            return false;
        }
    }

    internal sealed class OnityCastEnumerator<TResult> : OnitySourceAsyncEnumerator<object, TResult>
    {
        internal OnityCastEnumerator(IOnityAsyncEnumerable<object> source, CancellationToken token)
            : base(source, token)
        {
        }

        protected override bool ReadCurrentCore(out TResult current)
        {
            object value = UpstreamCurrent;
            if (IsClosing)
            {
                current = default;
                return false;
            }
            current = (TResult)value;
            return true;
        }
    }

    // Side-effect observer. Callbacks run outside lifecycle locks, in item order. A callback that throws
    // (including OperationCanceledException) first reports to onError, then faults the stream; an onError
    // that throws replaces the original failure. Upstream failures reach onError before upstream cleanup.
    internal sealed class OnityDoEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly Action<T> m_onNext;
        private readonly Action<Exception> m_onError;
        private readonly Action m_onCompleted;

        internal OnityDoEnumerator(IOnityAsyncEnumerable<T> source, Action<T> onNext, Action<Exception> onError,
            Action onCompleted, CancellationToken token) : base(source, token)
        {
            m_onNext = onNext;
            m_onError = onError;
            m_onCompleted = onCompleted;
        }

        protected override OnityTask<bool> MoveCore()
        {
            OnityTask<bool> move = base.MoveCore();
            return m_onError == null ? move : ObserveUpstream(move);
        }

        private OnityTask<bool> ObserveUpstream(OnityTask<bool> move)
        {
            if (move.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome = OnityAsyncStreamOutcome.Read(move);
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    return OnityTask<bool>.FromResult(outcome.Value);
                }
                var failed = new OnityTaskCompletionSource<bool>();
                Publish(failed, Notify(outcome));
                return failed.Task;
            }
            var output = new OnityTaskCompletionSource<bool>();
            new OnityAsyncStreamMoveObserver(move, outcome => Publish(output, Notify(outcome))).Register();
            return output.Task;
        }

        private static void Publish(OnityTaskCompletionSource<bool> output, OnityAsyncStreamOutcome outcome)
        {
            outcome.Publish(output, outcome.Value);
        }

        // Reports a failed or canceled upstream move; a throwing callback replaces the outcome.
        private OnityAsyncStreamOutcome Notify(OnityAsyncStreamOutcome outcome)
        {
            if (outcome.Status == OnityTaskSourceStatus.Succeeded)
            {
                return outcome;
            }
            Exception error = outcome.Status == OnityTaskSourceStatus.Faulted
                ? outcome.Fault : new OperationCanceledException(outcome.Token);
            try
            {
                m_onError(error);
            }
            catch (Exception callbackFailure)
            {
                return OnityAsyncStreamOutcome.Failure(callbackFailure);
            }
            return outcome;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            if (IsClosing)
            {
                return false;
            }
            if (m_onNext != null)
            {
                try
                {
                    m_onNext(current);
                }
                catch (Exception exception)
                {
                    Exception reported = ReportFailure(exception);
                    if (ReferenceEquals(reported, exception))
                    {
                        throw;
                    }
                    throw reported;
                }
            }
            return true;
        }

        protected override bool ReadEndCore(out T current)
        {
            current = default;
            if (m_onCompleted != null && !IsClosing)
            {
                try
                {
                    m_onCompleted();
                }
                catch (Exception exception)
                {
                    Exception reported = ReportFailure(exception);
                    if (ReferenceEquals(reported, exception))
                    {
                        throw;
                    }
                    throw reported;
                }
            }
            return false;
        }

        // Returns the exception that should fault the stream.
        private Exception ReportFailure(Exception exception)
        {
            if (m_onError != null)
            {
                try
                {
                    m_onError(exception);
                }
                catch (Exception callbackFailure)
                {
                    return callbackFailure;
                }
            }
            return exception;
        }
    }

    internal sealed class OnityDefaultIfEmptyEnumerator<T> : OnitySourceAsyncEnumerator<T, T>
    {
        private readonly T m_defaultValue;
        private bool m_anyItem;

        internal OnityDefaultIfEmptyEnumerator(IOnityAsyncEnumerable<T> source, T defaultValue,
            CancellationToken token) : base(source, token)
        {
            m_defaultValue = defaultValue;
        }

        protected override bool ReadCurrentCore(out T current)
        {
            current = UpstreamCurrent;
            if (IsClosing)
            {
                return false;
            }
            m_anyItem = true;
            return true;
        }

        // The upstream end of an empty stream yields the default value as the last item.
        protected override bool ReadEndCore(out T current)
        {
            if (m_anyItem || IsClosing)
            {
                current = default;
                return false;
            }
            current = m_defaultValue;
            return true;
        }
    }

    internal sealed class OnityPairwiseEnumerator<T> : OnitySourceAsyncEnumerator<T, (T Previous, T Current)>
    {
        private T m_previous;
        private bool m_hasPrevious;

        internal OnityPairwiseEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
            : base(source, token)
        {
        }

        protected override bool ReadCurrentCore(out (T Previous, T Current) current)
        {
            T value = UpstreamCurrent;
            current = default;
            if (IsClosing)
            {
                return false;
            }
            if (!m_hasPrevious)
            {
                m_previous = value;
                m_hasPrevious = true;
                return false;
            }
            current = (m_previous, value);
            m_previous = value;
            return true;
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_previous = default;
        }
    }

    // Non-overlapping buffers: a full buffer is published at once and the trailing partial buffer is the
    // last item. The published list belongs to the consumer; the enumerator starts a new list afterwards.
    internal sealed class OnityBufferEnumerator<T> : OnitySourceAsyncEnumerator<T, IList<T>>
    {
        private const int k_maxInitialCapacity = 32;

        private readonly int m_count;
        private List<T> m_buffer;

        internal OnityBufferEnumerator(IOnityAsyncEnumerable<T> source, int count, CancellationToken token)
            : base(source, token)
        {
            m_count = count;
        }

        protected override bool ReadCurrentCore(out IList<T> current)
        {
            T value = UpstreamCurrent;
            current = null;
            if (IsClosing)
            {
                return false;
            }
            m_buffer ??= new List<T>(Math.Min(m_count, k_maxInitialCapacity));
            m_buffer.Add(value);
            if (m_buffer.Count < m_count)
            {
                return false;
            }
            current = m_buffer;
            m_buffer = null;
            return true;
        }

        protected override bool ReadEndCore(out IList<T> current)
        {
            if (m_buffer != null && m_buffer.Count > 0 && !IsClosing)
            {
                current = m_buffer;
                m_buffer = null;
                return true;
            }
            current = null;
            return false;
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_buffer = null;
        }
    }

    // Pulls the upstream to its end inside MoveCore so a buffered tail can be served after the end. The
    // upstream is disposed before the last tail item is published; an empty tail ends the stream normally.
    internal abstract class OnityDrainAsyncEnumerator<T, TResult> : OnitySourceAsyncEnumerator<T, TResult>
    {
        private bool m_drained;

        protected OnityDrainAsyncEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
            : base(source, token)
        {
        }

        protected bool Drained => m_drained;
        protected abstract bool HasTail { get; }
        protected override bool EndAfterItem => m_drained && !HasTail;

        protected override OnityTask<bool> MoveCore()
        {
            if (m_drained)
            {
                return OnityTask<bool>.FromResult(HasTail);
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
            return HasTail;
        }
    }

    // Overlapping or gapped buffers: a new buffer starts every skip items, each item joins every open
    // buffer, a buffer is published when it holds count items, and the open partial buffers are published
    // in start order after the upstream end.
    internal sealed class OnityBufferSkipEnumerator<T> : OnityDrainAsyncEnumerator<T, IList<T>>
    {
        private const int k_maxInitialCapacity = 32;

        private readonly int m_count;
        private readonly int m_skip;
        private readonly Queue<List<T>> m_buffers = new Queue<List<T>>();
        private int m_untilNextBuffer;

        internal OnityBufferSkipEnumerator(IOnityAsyncEnumerable<T> source, int count, int skip,
            CancellationToken token) : base(source, token)
        {
            m_count = count;
            m_skip = skip;
        }

        protected override bool HasTail => m_buffers.Count > 0;

        protected override bool ReadCurrentCore(out IList<T> current)
        {
            current = null;
            if (Drained)
            {
                current = m_buffers.Dequeue();
                return true;
            }
            T value = UpstreamCurrent;
            if (IsClosing)
            {
                return false;
            }
            if (m_untilNextBuffer == 0)
            {
                m_buffers.Enqueue(new List<T>(Math.Min(m_count, k_maxInitialCapacity)));
                m_untilNextBuffer = m_skip;
            }
            m_untilNextBuffer--;
            foreach (List<T> buffer in m_buffers)
            {
                buffer.Add(value);
            }
            if (m_buffers.Count > 0 && m_buffers.Peek().Count == m_count)
            {
                current = m_buffers.Dequeue();
                return true;
            }
            return false;
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_buffers.Clear();
        }
    }

    internal sealed class OnityReverseEnumerator<T> : OnityDrainAsyncEnumerator<T, T>
    {
        private readonly List<T> m_items = new List<T>();

        internal OnityReverseEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
            : base(source, token)
        {
        }

        protected override bool HasTail => m_items.Count > 0;

        protected override bool ReadCurrentCore(out T current)
        {
            if (Drained)
            {
                int last = m_items.Count - 1;
                current = m_items[last];
                m_items.RemoveAt(last);
                return true;
            }
            T value = UpstreamCurrent;
            current = default;
            if (IsClosing)
            {
                return false;
            }
            m_items.Add(value);
            return false;
        }

        protected override void FinishCleanupCore()
        {
            base.FinishCleanupCore();
            m_items.Clear();
        }
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- OfType / Cast ----------------------------------------------------------------

        /// <summary>Yields only the items that are instances of a type.</summary>
        /// <typeparam name="TResult">Type to keep.</typeparam>
        /// <param name="source">Upstream description; a stream of a reference type converts through covariance.</param>
        /// <returns>A lazy description; null items and items of other types are skipped.</returns>
        /// <remarks>A stream of a value type is not convertible to <c>IOnityAsyncEnumerable&lt;object&gt;</c>;
        /// project it with <c>Select(value =&gt; (object)value)</c> first.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<TResult> OfType<TResult>(this IOnityAsyncEnumerable<object> source)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<object, TResult>(source,
                (upstream, token) => new OnityOfTypeEnumerator<TResult>(upstream, token));
        }

        /// <summary>Casts every item to a type.</summary>
        /// <typeparam name="TResult">Target type.</typeparam>
        /// <param name="source">Upstream description; a stream of a reference type converts through covariance.</param>
        /// <returns>A lazy description; an item that is not a <typeparamref name="TResult"/> faults the stream
        /// with <see cref="InvalidCastException"/>, and a null item faults it when the target is a value type.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<TResult> Cast<TResult>(this IOnityAsyncEnumerable<object> source)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<object, TResult>(source,
                (upstream, token) => new OnityCastEnumerator<TResult>(upstream, token));
        }

        // ---- Do ---------------------------------------------------------------------------

        /// <summary>Invokes an action for each item as a side effect and yields the items unchanged.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="onNext">Action invoked for each item before it is yielded; may be null.</param>
        /// <returns>A lazy description. An action that throws, including OperationCanceledException, faults
        /// the stream.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Do<T>(this IOnityAsyncEnumerable<T> source, Action<T> onNext)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return CreateDoDescription(source, onNext, null, null);
        }

        /// <summary>Invokes callbacks for items and failures as side effects and yields the items unchanged.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="onNext">Action invoked for each item before it is yielded; may be null.</param>
        /// <param name="onError">Callback for a failure of the upstream or of <paramref name="onNext"/>; may be null.</param>
        /// <returns>A lazy description. A canceled upstream move reports an OperationCanceledException to
        /// <paramref name="onError"/>; the original outcome is then propagated. An <paramref name="onError"/>
        /// that throws replaces the failure.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Do<T>(
            this IOnityAsyncEnumerable<T> source, Action<T> onNext, Action<Exception> onError)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return CreateDoDescription(source, onNext, onError, null);
        }

        /// <summary>Invokes callbacks for items and normal completion as side effects.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="onNext">Action invoked for each item before it is yielded; may be null.</param>
        /// <param name="onCompleted">Callback invoked once when the upstream ends normally, before upstream
        /// cleanup and before the end is published; may be null.</param>
        /// <returns>A lazy description. A callback that throws faults the stream.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Do<T>(
            this IOnityAsyncEnumerable<T> source, Action<T> onNext, Action onCompleted)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return CreateDoDescription(source, onNext, null, onCompleted);
        }

        /// <summary>Invokes callbacks for items, failures and normal completion as side effects.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="onNext">Action invoked for each item before it is yielded; may be null.</param>
        /// <param name="onError">Callback for a failure of the upstream or of a callback; may be null.</param>
        /// <param name="onCompleted">Callback invoked once when the upstream ends normally; may be null.</param>
        /// <returns>A lazy description. Callbacks run outside lifecycle locks and in item order. Failures
        /// reach <paramref name="onError"/> before upstream cleanup, then the original failure (or the
        /// exception thrown by <paramref name="onError"/>) is published after cleanup.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Do<T>(this IOnityAsyncEnumerable<T> source, Action<T> onNext,
            Action<Exception> onError, Action onCompleted)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return CreateDoDescription(source, onNext, onError, onCompleted);
        }

        /// <summary>Forwards items, failures and normal completion to an observer as side effects.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="observer">Observer notified through OnNext, OnError and OnCompleted.</param>
        /// <returns>A lazy description with the semantics of the callback overload.</returns>
        /// <exception cref="ArgumentNullException">Source or observer is null.</exception>
        public static IOnityAsyncEnumerable<T> Do<T>(this IOnityAsyncEnumerable<T> source, IObserver<T> observer)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(observer, nameof(observer));
            return CreateDoDescription(source, observer.OnNext, observer.OnError, observer.OnCompleted);
        }

        private static IOnityAsyncEnumerable<T> CreateDoDescription<T>(IOnityAsyncEnumerable<T> source,
            Action<T> onNext, Action<Exception> onError, Action onCompleted)
        {
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityDoEnumerator<T>(upstream, onNext, onError, onCompleted, token));
        }

        // ---- DefaultIfEmpty ---------------------------------------------------------------

        /// <summary>Yields default(T) when the stream is empty.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <returns>A lazy description; a non-empty stream is yielded unchanged.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> DefaultIfEmpty<T>(this IOnityAsyncEnumerable<T> source)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityDefaultIfEmptyEnumerator<T>(upstream, default, token));
        }

        /// <summary>Yields a supplied value when the stream is empty.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="defaultValue">Value yielded as the only item of an empty stream.</param>
        /// <returns>A lazy description; upstream is disposed before the default value is published.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> DefaultIfEmpty<T>(
            this IOnityAsyncEnumerable<T> source, T defaultValue)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityDefaultIfEmptyEnumerator<T>(upstream, defaultValue, token));
        }

        // ---- Pairwise ---------------------------------------------------------------------

        /// <summary>Pairs each item with the item before it.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <returns>A lazy description of (previous, current) pairs; a stream with fewer than two items is
        /// empty.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<(T Previous, T Current)> Pairwise<T>(
            this IOnityAsyncEnumerable<T> source)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, (T Previous, T Current)>(source,
                (upstream, token) => new OnityPairwiseEnumerator<T>(upstream, token));
        }

        // ---- Buffer -----------------------------------------------------------------------

        /// <summary>Groups items into consecutive lists of a fixed size.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="count">Positive list size.</param>
        /// <returns>A lazy description; every list but the last holds exactly <paramref name="count"/> items
        /// and the last holds the remaining items (an empty stream yields nothing). Each published list is
        /// owned by the consumer; upstream is disposed before the last list is published.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Count is not positive.</exception>
        public static IOnityAsyncEnumerable<IList<T>> Buffer<T>(this IOnityAsyncEnumerable<T> source, int count)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            return new OnityLinqDescription<T, IList<T>>(source,
                (upstream, token) => new OnityBufferEnumerator<T>(upstream, count, token));
        }

        /// <summary>Groups items into lists of a fixed size that start at a fixed interval.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="count">Positive list size.</param>
        /// <param name="skip">Positive number of items between the starts of consecutive lists; a value
        /// below <paramref name="count"/> makes lists overlap and a larger value leaves gaps.</param>
        /// <returns>A lazy description; a list is published when it holds <paramref name="count"/> items and
        /// the lists still open at the upstream end are published in start order. Each published list is owned
        /// by the consumer; upstream is disposed before the last list is published.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Count or skip is not positive.</exception>
        public static IOnityAsyncEnumerable<IList<T>> Buffer<T>(
            this IOnityAsyncEnumerable<T> source, int count, int skip)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (skip <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip));
            }
            return new OnityLinqDescription<T, IList<T>>(source,
                (upstream, token) => new OnityBufferSkipEnumerator<T>(upstream, count, skip, token));
        }

        // ---- Reverse ----------------------------------------------------------------------

        /// <summary>Yields the items in reverse order after the whole stream was read.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description; it must be finite.</param>
        /// <returns>A lazy description that drains upstream into memory on the first move and then serves
        /// the items last to first; upstream is disposed before the last item is published.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> Reverse<T>(this IOnityAsyncEnumerable<T> source)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => new OnityReverseEnumerator<T>(upstream, token));
        }
    }
}
