using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // Normalized one-argument delegate: exactly one of the three shapes is set. A default value
    // means "not supplied" (for example no predicate). Passed by value so no wrapper is allocated.
    internal readonly struct OnityLinqFunc<T, TResult>
    {
        private readonly Func<T, TResult> m_sync;
        private readonly Func<T, OnityTask<TResult>> m_await;
        private readonly Func<T, CancellationToken, OnityTask<TResult>> m_cancel;

        private OnityLinqFunc(Func<T, TResult> sync, Func<T, OnityTask<TResult>> awaiting,
            Func<T, CancellationToken, OnityTask<TResult>> cancel)
        {
            m_sync = sync;
            m_await = awaiting;
            m_cancel = cancel;
        }

        internal bool IsDefined => m_sync != null || m_await != null || m_cancel != null;

        internal static OnityLinqFunc<T, TResult> FromSync(Func<T, TResult> func) =>
            new OnityLinqFunc<T, TResult>(func, null, null);

        internal static OnityLinqFunc<T, TResult> FromAwait(Func<T, OnityTask<TResult>> func) =>
            new OnityLinqFunc<T, TResult>(null, func, null);

        internal static OnityLinqFunc<T, TResult> FromCancel(Func<T, CancellationToken, OnityTask<TResult>> func) =>
            new OnityLinqFunc<T, TResult>(null, null, func);

        internal OnityTask<TResult> Invoke(T item, CancellationToken token)
        {
            if (m_sync != null)
            {
                return OnityTask<TResult>.FromResult(m_sync(item));
            }
            if (m_await != null)
            {
                return m_await(item);
            }
            return m_cancel(item, token);
        }
    }

    // Normalized two-argument delegate (accumulators).
    internal readonly struct OnityLinqFunc2<T1, T2, TResult>
    {
        private readonly Func<T1, T2, TResult> m_sync;
        private readonly Func<T1, T2, OnityTask<TResult>> m_await;
        private readonly Func<T1, T2, CancellationToken, OnityTask<TResult>> m_cancel;

        private OnityLinqFunc2(Func<T1, T2, TResult> sync, Func<T1, T2, OnityTask<TResult>> awaiting,
            Func<T1, T2, CancellationToken, OnityTask<TResult>> cancel)
        {
            m_sync = sync;
            m_await = awaiting;
            m_cancel = cancel;
        }

        internal static OnityLinqFunc2<T1, T2, TResult> FromSync(Func<T1, T2, TResult> func) =>
            new OnityLinqFunc2<T1, T2, TResult>(func, null, null);

        internal static OnityLinqFunc2<T1, T2, TResult> FromAwait(Func<T1, T2, OnityTask<TResult>> func) =>
            new OnityLinqFunc2<T1, T2, TResult>(null, func, null);

        internal static OnityLinqFunc2<T1, T2, TResult> FromCancel(
            Func<T1, T2, CancellationToken, OnityTask<TResult>> func) =>
            new OnityLinqFunc2<T1, T2, TResult>(null, null, func);

        internal OnityTask<TResult> Invoke(T1 first, T2 second, CancellationToken token)
        {
            if (m_sync != null)
            {
                return OnityTask<TResult>.FromResult(m_sync(first, second));
            }
            if (m_await != null)
            {
                return m_await(first, second);
            }
            return m_cancel(first, second, token);
        }
    }

    internal static class OnityLinqIdentity<T>
    {
        internal static readonly Func<T, T> Instance = value => value;
    }

    internal static class OnityLinqCore
    {
        internal static void NotNull(object value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
        }

        internal static IOnityAsyncEnumerator<T> Acquire<T>(IOnityAsyncEnumerable<T> source, CancellationToken token)
        {
            IOnityAsyncEnumerator<T> enumerator = source.GetAsyncEnumerator(token);
            if (enumerator == null)
            {
                throw new InvalidOperationException("The upstream description returned a null enumerator.");
            }
            return enumerator;
        }

        internal static Exception NoElements() =>
            new InvalidOperationException("The asynchronous stream contains no items.");

        internal static Exception MoreThanOneElement() =>
            new InvalidOperationException("The asynchronous stream contains more than one matching item.");
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- Count / LongCount ------------------------------------------------------------

        /// <summary>Counts every item of the stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The item count. Cleanup runs on every exit and its failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        /// <exception cref="OverflowException">The count exceeds int.MaxValue (reported by the task).</exception>
        public static OnityTask<int> CountAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return CountCore(source, default, cancellationToken);
        }

        /// <summary>Counts the items accepted by a synchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The accepted item count; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<int> CountAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CountCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken);
        }

        /// <summary>Counts the items accepted by an asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The accepted item count; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<int> CountAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CountCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken);
        }

        /// <summary>Counts the items accepted by an asynchronous predicate that receives the token.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The accepted item count; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<int> CountAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return CountCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken);
        }

        /// <summary>Counts every item of the stream as a 64-bit value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The item count; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<long> LongCountAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return LongCountCore(source, default, cancellationToken);
        }

        /// <summary>Counts the items accepted by a synchronous predicate as a 64-bit value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The accepted item count; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<long> LongCountAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LongCountCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken);
        }

        /// <summary>Counts the items accepted by an asynchronous predicate as a 64-bit value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The accepted item count; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<long> LongCountAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LongCountCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken);
        }

        /// <summary>Counts the items accepted by an asynchronous token-aware predicate as a 64-bit value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The accepted item count; cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<long> LongCountAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LongCountCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken);
        }

        private static async OnityTask<int> CountCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, bool> predicate, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                int count = 0;
                while (await enumerator.MoveNextAsync())
                {
                    if (!predicate.IsDefined || await predicate.Invoke(enumerator.Current, cancellationToken))
                    {
                        checked
                        {
                            count++;
                        }
                    }
                }
                return count;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<long> LongCountCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, bool> predicate, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                long count = 0;
                while (await enumerator.MoveNextAsync())
                {
                    if (!predicate.IsDefined || await predicate.Invoke(enumerator.Current, cancellationToken))
                    {
                        checked
                        {
                            count++;
                        }
                    }
                }
                return count;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // ---- Any / All / Contains ---------------------------------------------------------

        /// <summary>Tests whether the stream yields at least one item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>True when an item exists; the enumeration stops after the first item and cleanup runs.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<bool> AnyAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return AnyCore(source, default, cancellationToken);
        }

        /// <summary>Tests whether any item satisfies a synchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>True at the first accepted item, which stops the enumeration.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<bool> AnyAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return AnyCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken);
        }

        /// <summary>Tests whether any item satisfies an asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>True at the first accepted item, which stops the enumeration.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<bool> AnyAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return AnyCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken);
        }

        /// <summary>Tests whether any item satisfies an asynchronous token-aware predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>True at the first accepted item, which stops the enumeration.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<bool> AnyAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return AnyCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken);
        }

        /// <summary>Tests whether every item satisfies a synchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>False at the first rejected item, which stops the enumeration; true for an empty stream.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<bool> AllAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return AllCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken);
        }

        /// <summary>Tests whether every item satisfies an asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>False at the first rejected item, which stops the enumeration; true for an empty stream.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<bool> AllAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return AllCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken);
        }

        /// <summary>Tests whether every item satisfies an asynchronous token-aware predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>False at the first rejected item, which stops the enumeration; true for an empty stream.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<bool> AllAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return AllCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken);
        }

        /// <summary>Tests whether the stream contains a value using the default equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="value">Value to find.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>True at the first equal item, which stops the enumeration.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<bool> ContainsAsync<T>(this IOnityAsyncEnumerable<T> source, T value,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return ContainsCore(source, value, EqualityComparer<T>.Default, cancellationToken);
        }

        /// <summary>Tests whether the stream contains a value using a supplied equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="value">Value to find.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>True at the first equal item, which stops the enumeration.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<bool> ContainsAsync<T>(this IOnityAsyncEnumerable<T> source, T value,
            IEqualityComparer<T> comparer, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return ContainsCore(source, value, comparer ?? EqualityComparer<T>.Default, cancellationToken);
        }

        private static async OnityTask<bool> AnyCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, bool> predicate, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    if (!predicate.IsDefined || await predicate.Invoke(enumerator.Current, cancellationToken))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<bool> AllCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, bool> predicate, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    if (!await predicate.Invoke(enumerator.Current, cancellationToken))
                    {
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<bool> ContainsCore<T>(IOnityAsyncEnumerable<T> source, T value,
            IEqualityComparer<T> comparer, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    if (comparer.Equals(enumerator.Current, value))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // ---- Aggregate --------------------------------------------------------------------

        /// <summary>Folds the stream with a synchronous accumulator seeded by the first item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="accumulator">Accumulator invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The folded value; an empty stream faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or accumulator is null.</exception>
        public static OnityTask<T> AggregateAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, T, T> accumulator, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            return AggregateCore(source, OnityLinqFunc2<T, T, T>.FromSync(accumulator), cancellationToken);
        }

        /// <summary>Folds the stream with a synchronous accumulator and an explicit seed.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TAccumulate">Accumulator type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="seed">Initial accumulator value.</param>
        /// <param name="accumulator">Accumulator invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The folded value; an empty stream returns the seed.</returns>
        /// <exception cref="ArgumentNullException">Source or accumulator is null.</exception>
        public static OnityTask<TAccumulate> AggregateAsync<T, TAccumulate>(this IOnityAsyncEnumerable<T> source,
            TAccumulate seed, Func<TAccumulate, T, TAccumulate> accumulator, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            return AggregateCore(source, seed,
                OnityLinqFunc2<TAccumulate, T, TAccumulate>.FromSync(accumulator), cancellationToken);
        }

        /// <summary>Folds the stream with a synchronous accumulator and maps the result.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TAccumulate">Accumulator type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="seed">Initial accumulator value.</param>
        /// <param name="accumulator">Accumulator invoked sequentially.</param>
        /// <param name="resultSelector">Maps the final accumulator value.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The mapped value.</returns>
        /// <exception cref="ArgumentNullException">Source, accumulator or result selector is null.</exception>
        public static OnityTask<TResult> AggregateAsync<T, TAccumulate, TResult>(
            this IOnityAsyncEnumerable<T> source, TAccumulate seed, Func<TAccumulate, T, TAccumulate> accumulator,
            Func<TAccumulate, TResult> resultSelector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return AggregateCore(source, seed, OnityLinqFunc2<TAccumulate, T, TAccumulate>.FromSync(accumulator),
                OnityLinqFunc<TAccumulate, TResult>.FromSync(resultSelector), cancellationToken);
        }

        /// <summary>Folds the stream with an asynchronous accumulator seeded by the first item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="accumulator">Accumulator awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The folded value; an empty stream faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or accumulator is null.</exception>
        public static OnityTask<T> AggregateAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, T, OnityTask<T>> accumulator, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            return AggregateCore(source, OnityLinqFunc2<T, T, T>.FromAwait(accumulator), cancellationToken);
        }

        /// <summary>Folds the stream with an asynchronous accumulator and an explicit seed.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TAccumulate">Accumulator type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="seed">Initial accumulator value.</param>
        /// <param name="accumulator">Accumulator awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The folded value; an empty stream returns the seed.</returns>
        /// <exception cref="ArgumentNullException">Source or accumulator is null.</exception>
        public static OnityTask<TAccumulate> AggregateAwaitAsync<T, TAccumulate>(
            this IOnityAsyncEnumerable<T> source, TAccumulate seed,
            Func<TAccumulate, T, OnityTask<TAccumulate>> accumulator, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            return AggregateCore(source, seed,
                OnityLinqFunc2<TAccumulate, T, TAccumulate>.FromAwait(accumulator), cancellationToken);
        }

        /// <summary>Folds the stream with an asynchronous accumulator and maps the result asynchronously.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TAccumulate">Accumulator type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="seed">Initial accumulator value.</param>
        /// <param name="accumulator">Accumulator awaited sequentially.</param>
        /// <param name="resultSelector">Asynchronous mapping of the final accumulator value.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The mapped value.</returns>
        /// <exception cref="ArgumentNullException">Source, accumulator or result selector is null.</exception>
        public static OnityTask<TResult> AggregateAwaitAsync<T, TAccumulate, TResult>(
            this IOnityAsyncEnumerable<T> source, TAccumulate seed,
            Func<TAccumulate, T, OnityTask<TAccumulate>> accumulator,
            Func<TAccumulate, OnityTask<TResult>> resultSelector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return AggregateCore(source, seed, OnityLinqFunc2<TAccumulate, T, TAccumulate>.FromAwait(accumulator),
                OnityLinqFunc<TAccumulate, TResult>.FromAwait(resultSelector), cancellationToken);
        }

        /// <summary>Folds the stream with an asynchronous token-aware accumulator seeded by the first item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="accumulator">Accumulator awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The folded value; an empty stream faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or accumulator is null.</exception>
        public static OnityTask<T> AggregateAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, T, CancellationToken, OnityTask<T>> accumulator, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            return AggregateCore(source, OnityLinqFunc2<T, T, T>.FromCancel(accumulator), cancellationToken);
        }

        /// <summary>Folds the stream with an asynchronous token-aware accumulator and an explicit seed.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TAccumulate">Accumulator type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="seed">Initial accumulator value.</param>
        /// <param name="accumulator">Accumulator awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The folded value; an empty stream returns the seed.</returns>
        /// <exception cref="ArgumentNullException">Source or accumulator is null.</exception>
        public static OnityTask<TAccumulate> AggregateAwaitWithCancellationAsync<T, TAccumulate>(
            this IOnityAsyncEnumerable<T> source, TAccumulate seed,
            Func<TAccumulate, T, CancellationToken, OnityTask<TAccumulate>> accumulator,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            return AggregateCore(source, seed,
                OnityLinqFunc2<TAccumulate, T, TAccumulate>.FromCancel(accumulator), cancellationToken);
        }

        /// <summary>Folds the stream with an asynchronous token-aware accumulator and result selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TAccumulate">Accumulator type.</typeparam>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="seed">Initial accumulator value.</param>
        /// <param name="accumulator">Accumulator awaited sequentially with the enumeration token.</param>
        /// <param name="resultSelector">Asynchronous mapping that also receives the token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The mapped value.</returns>
        /// <exception cref="ArgumentNullException">Source, accumulator or result selector is null.</exception>
        public static OnityTask<TResult> AggregateAwaitWithCancellationAsync<T, TAccumulate, TResult>(
            this IOnityAsyncEnumerable<T> source, TAccumulate seed,
            Func<TAccumulate, T, CancellationToken, OnityTask<TAccumulate>> accumulator,
            Func<TAccumulate, CancellationToken, OnityTask<TResult>> resultSelector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(accumulator, nameof(accumulator));
            OnityLinqCore.NotNull(resultSelector, nameof(resultSelector));
            return AggregateCore(source, seed, OnityLinqFunc2<TAccumulate, T, TAccumulate>.FromCancel(accumulator),
                OnityLinqFunc<TAccumulate, TResult>.FromCancel(resultSelector), cancellationToken);
        }

        private static async OnityTask<T> AggregateCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc2<T, T, T> accumulator, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    throw OnityLinqCore.NoElements();
                }
                T value = enumerator.Current;
                while (await enumerator.MoveNextAsync())
                {
                    value = await accumulator.Invoke(value, enumerator.Current, cancellationToken);
                }
                return value;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<TAccumulate> AggregateCore<T, TAccumulate>(
            IOnityAsyncEnumerable<T> source, TAccumulate seed,
            OnityLinqFunc2<TAccumulate, T, TAccumulate> accumulator, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                TAccumulate value = seed;
                while (await enumerator.MoveNextAsync())
                {
                    value = await accumulator.Invoke(value, enumerator.Current, cancellationToken);
                }
                return value;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<TResult> AggregateCore<T, TAccumulate, TResult>(
            IOnityAsyncEnumerable<T> source, TAccumulate seed,
            OnityLinqFunc2<TAccumulate, T, TAccumulate> accumulator,
            OnityLinqFunc<TAccumulate, TResult> resultSelector, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                TAccumulate value = seed;
                while (await enumerator.MoveNextAsync())
                {
                    value = await accumulator.Invoke(value, enumerator.Current, cancellationToken);
                }
                return await resultSelector.Invoke(value, cancellationToken);
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // ---- First / Last / Single (+ OrDefault) ------------------------------------------

        /// <summary>Reads the first item accepted by a synchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first accepted item, which stops the enumeration; no match faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> FirstAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return FirstCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken, false);
        }

        /// <summary>Reads the first item accepted by an asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first accepted item; no match faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> FirstAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return FirstCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken, false);
        }

        /// <summary>Reads the first item accepted by an asynchronous token-aware predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first accepted item; no match faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> FirstAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return FirstCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken, false);
        }

        /// <summary>Reads the first item, or the default value for an empty stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first item or default(T); the enumeration stops after the first item.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> FirstOrDefaultAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return FirstCore(source, default, cancellationToken, true);
        }

        /// <summary>Reads the first item accepted by a synchronous predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first accepted item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> FirstOrDefaultAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return FirstCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken, true);
        }

        /// <summary>Reads the first item accepted by an asynchronous predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first accepted item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> FirstOrDefaultAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return FirstCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken, true);
        }

        /// <summary>Reads the first item accepted by an asynchronous token-aware predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first accepted item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> FirstOrDefaultAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return FirstCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken, true);
        }

        /// <summary>Reads the last item of the stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last item; an empty stream faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> LastAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return LastCore(source, default, cancellationToken, false);
        }

        /// <summary>Reads the last item accepted by a synchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last accepted item; no match faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> LastAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LastCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken, false);
        }

        /// <summary>Reads the last item accepted by an asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last accepted item; no match faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> LastAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LastCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken, false);
        }

        /// <summary>Reads the last item accepted by an asynchronous token-aware predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last accepted item; no match faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> LastAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LastCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken, false);
        }

        /// <summary>Reads the last item, or the default value for an empty stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> LastOrDefaultAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return LastCore(source, default, cancellationToken, true);
        }

        /// <summary>Reads the last item accepted by a synchronous predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last accepted item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> LastOrDefaultAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LastCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken, true);
        }

        /// <summary>Reads the last item accepted by an asynchronous predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last accepted item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> LastOrDefaultAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LastCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken, true);
        }

        /// <summary>Reads the last item accepted by an asynchronous token-aware predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The last accepted item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> LastOrDefaultAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return LastCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken, true);
        }

        /// <summary>Reads the only item of the stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single item; an empty stream or a second item faults with InvalidOperationException
        /// (the enumeration stops at the second item).</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> SingleAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return SingleCore(source, default, cancellationToken, false);
        }

        /// <summary>Reads the only item accepted by a synchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single accepted item; none or several fault with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> SingleAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return SingleCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken, false);
        }

        /// <summary>Reads the only item accepted by an asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single accepted item; none or several fault with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> SingleAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return SingleCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken, false);
        }

        /// <summary>Reads the only item accepted by an asynchronous token-aware predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single accepted item; none or several fault with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> SingleAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return SingleCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken, false);
        }

        /// <summary>Reads the only item, or the default value for an empty stream.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single item or default(T); a second item faults with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> SingleOrDefaultAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return SingleCore(source, default, cancellationToken, true);
        }

        /// <summary>Reads the only item accepted by a synchronous predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single accepted item or default(T); several matches fault with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> SingleOrDefaultAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, bool> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return SingleCore(source, OnityLinqFunc<T, bool>.FromSync(predicate), cancellationToken, true);
        }

        /// <summary>Reads the only item accepted by an asynchronous predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single accepted item or default(T); several matches fault with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> SingleOrDefaultAwaitAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return SingleCore(source, OnityLinqFunc<T, bool>.FromAwait(predicate), cancellationToken, true);
        }

        /// <summary>Reads the only item accepted by an asynchronous token-aware predicate, or the default value.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The single accepted item or default(T); several matches fault with InvalidOperationException.</returns>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static OnityTask<T> SingleOrDefaultAwaitWithCancellationAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(predicate, nameof(predicate));
            return SingleCore(source, OnityLinqFunc<T, bool>.FromCancel(predicate), cancellationToken, true);
        }

        /// <summary>Reads the item at a zero-based index.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="index">Zero-based index.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The item; a stream shorter than the index faults with ArgumentOutOfRangeException.
        /// The enumeration stops at the item.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Index is negative.</exception>
        public static OnityTask<T> ElementAtAsync<T>(this IOnityAsyncEnumerable<T> source, int index,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return ElementAtCore(source, index, cancellationToken, false);
        }

        /// <summary>Reads the item at a zero-based index, or the default value when out of range.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="index">Zero-based index; negative returns default(T) without enumerating.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The item or default(T).</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> ElementAtOrDefaultAsync<T>(this IOnityAsyncEnumerable<T> source, int index,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            if (index < 0)
            {
                return OnityTask<T>.FromResult(default);
            }
            return ElementAtCore(source, index, cancellationToken, true);
        }

        private static async OnityTask<T> FirstCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, bool> predicate, CancellationToken cancellationToken, bool orDefault)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    T value = enumerator.Current;
                    if (!predicate.IsDefined || await predicate.Invoke(value, cancellationToken))
                    {
                        return value;
                    }
                }
                if (orDefault)
                {
                    return default;
                }
                throw OnityLinqCore.NoElements();
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<T> LastCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, bool> predicate, CancellationToken cancellationToken, bool orDefault)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                T last = default;
                bool found = false;
                while (await enumerator.MoveNextAsync())
                {
                    T value = enumerator.Current;
                    if (!predicate.IsDefined || await predicate.Invoke(value, cancellationToken))
                    {
                        last = value;
                        found = true;
                    }
                }
                if (found || orDefault)
                {
                    return last;
                }
                throw OnityLinqCore.NoElements();
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<T> SingleCore<T>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, bool> predicate, CancellationToken cancellationToken, bool orDefault)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                T single = default;
                bool found = false;
                while (await enumerator.MoveNextAsync())
                {
                    T value = enumerator.Current;
                    if (!predicate.IsDefined || await predicate.Invoke(value, cancellationToken))
                    {
                        if (found)
                        {
                            throw OnityLinqCore.MoreThanOneElement();
                        }
                        single = value;
                        found = true;
                    }
                }
                if (found || orDefault)
                {
                    return single;
                }
                throw OnityLinqCore.NoElements();
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        private static async OnityTask<T> ElementAtCore<T>(IOnityAsyncEnumerable<T> source, int index,
            CancellationToken cancellationToken, bool orDefault)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                int position = 0;
                while (await enumerator.MoveNextAsync())
                {
                    if (position++ == index)
                    {
                        return enumerator.Current;
                    }
                }
                if (orDefault)
                {
                    return default;
                }
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        // ---- SequenceEqual ----------------------------------------------------------------

        /// <summary>Compares two streams item by item with the default equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">First upstream description.</param>
        /// <param name="second">Second upstream description.</param>
        /// <param name="cancellationToken">Cooperative token forwarded to both enumerations.</param>
        /// <returns>True when both streams yield equal items in the same order. The streams are advanced
        /// alternately, never concurrently, and both are disposed on every exit.</returns>
        /// <exception cref="ArgumentNullException">First or second is null.</exception>
        public static OnityTask<bool> SequenceEqualAsync<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            return SequenceEqualCore(first, second, EqualityComparer<T>.Default, cancellationToken);
        }

        /// <summary>Compares two streams item by item with a supplied equality comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="first">First upstream description.</param>
        /// <param name="second">Second upstream description.</param>
        /// <param name="comparer">Equality comparer; null selects the default comparer.</param>
        /// <param name="cancellationToken">Cooperative token forwarded to both enumerations.</param>
        /// <returns>True when both streams yield equal items in the same order.</returns>
        /// <exception cref="ArgumentNullException">First or second is null.</exception>
        public static OnityTask<bool> SequenceEqualAsync<T>(this IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second, IEqualityComparer<T> comparer,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(first, nameof(first));
            OnityLinqCore.NotNull(second, nameof(second));
            return SequenceEqualCore(first, second, comparer ?? EqualityComparer<T>.Default, cancellationToken);
        }

        private static async OnityTask<bool> SequenceEqualCore<T>(IOnityAsyncEnumerable<T> first,
            IOnityAsyncEnumerable<T> second, IEqualityComparer<T> comparer, CancellationToken cancellationToken)
        {
            IOnityAsyncEnumerator<T> left = OnityLinqCore.Acquire(first, cancellationToken);
            try
            {
                IOnityAsyncEnumerator<T> right = OnityLinqCore.Acquire(second, cancellationToken);
                try
                {
                    while (true)
                    {
                        bool hasLeft = await left.MoveNextAsync();
                        bool hasRight = await right.MoveNextAsync();
                        if (hasLeft != hasRight)
                        {
                            return false;
                        }
                        if (!hasLeft)
                        {
                            return true;
                        }
                        if (!comparer.Equals(left.Current, right.Current))
                        {
                            return false;
                        }
                    }
                }
                finally
                {
                    await right.DisposeAsync();
                }
            }
            finally
            {
                await left.DisposeAsync();
            }
        }

        // ---- Min / Max (comparer based) ---------------------------------------------------

        /// <summary>Finds the smallest item using the default comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The smallest item; an empty stream returns default(T) as UniTask does.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> MinAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return ExtremumCore(source, OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance),
                cancellationToken, false);
        }

        /// <summary>Finds the smallest selected value using the default comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TResult">Selected value type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The smallest value; an empty stream returns default(TResult).</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static OnityTask<TResult> MinAsync<T, TResult>(this IOnityAsyncEnumerable<T> source,
            Func<T, TResult> selector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return ExtremumCore(source, OnityLinqFunc<T, TResult>.FromSync(selector), cancellationToken, false);
        }

        /// <summary>Finds the smallest value produced by an asynchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TResult">Selected value type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The smallest value; an empty stream returns default(TResult).</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static OnityTask<TResult> MinAwaitAsync<T, TResult>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<TResult>> selector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return ExtremumCore(source, OnityLinqFunc<T, TResult>.FromAwait(selector), cancellationToken, false);
        }

        /// <summary>Finds the smallest value produced by an asynchronous token-aware selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TResult">Selected value type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The smallest value; an empty stream returns default(TResult).</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static OnityTask<TResult> MinAwaitWithCancellationAsync<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TResult>> selector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return ExtremumCore(source, OnityLinqFunc<T, TResult>.FromCancel(selector), cancellationToken, false);
        }

        /// <summary>Finds the largest item using the default comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The largest item; an empty stream returns default(T) as UniTask does.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> MaxAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return ExtremumCore(source, OnityLinqFunc<T, T>.FromSync(OnityLinqIdentity<T>.Instance),
                cancellationToken, true);
        }

        /// <summary>Finds the largest selected value using the default comparer.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TResult">Selected value type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector invoked sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The largest value; an empty stream returns default(TResult).</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static OnityTask<TResult> MaxAsync<T, TResult>(this IOnityAsyncEnumerable<T> source,
            Func<T, TResult> selector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return ExtremumCore(source, OnityLinqFunc<T, TResult>.FromSync(selector), cancellationToken, true);
        }

        /// <summary>Finds the largest value produced by an asynchronous selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TResult">Selected value type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector awaited sequentially.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The largest value; an empty stream returns default(TResult).</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static OnityTask<TResult> MaxAwaitAsync<T, TResult>(this IOnityAsyncEnumerable<T> source,
            Func<T, OnityTask<TResult>> selector, CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return ExtremumCore(source, OnityLinqFunc<T, TResult>.FromAwait(selector), cancellationToken, true);
        }

        /// <summary>Finds the largest value produced by an asynchronous token-aware selector.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <typeparam name="TResult">Selected value type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector awaited sequentially with the enumeration token.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The largest value; an empty stream returns default(TResult).</returns>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static OnityTask<TResult> MaxAwaitWithCancellationAsync<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TResult>> selector,
            CancellationToken cancellationToken = default)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(selector, nameof(selector));
            return ExtremumCore(source, OnityLinqFunc<T, TResult>.FromCancel(selector), cancellationToken, true);
        }

        private static async OnityTask<TResult> ExtremumCore<T, TResult>(IOnityAsyncEnumerable<T> source,
            OnityLinqFunc<T, TResult> selector, CancellationToken cancellationToken, bool max)
        {
            IOnityAsyncEnumerator<T> enumerator = OnityLinqCore.Acquire(source, cancellationToken);
            try
            {
                Comparer<TResult> comparer = Comparer<TResult>.Default;
                TResult value = default;
                bool hasValue = false;
                while (await enumerator.MoveNextAsync())
                {
                    TResult candidate = await selector.Invoke(enumerator.Current, cancellationToken);
                    if (!hasValue)
                    {
                        value = candidate;
                        hasValue = true;
                        continue;
                    }
                    int order = comparer.Compare(value, candidate);
                    if (max ? order < 0 : order > 0)
                    {
                        value = candidate;
                    }
                }
                return value;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }
    }
}
