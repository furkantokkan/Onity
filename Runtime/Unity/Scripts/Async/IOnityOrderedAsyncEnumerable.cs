using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// A sorted asynchronous stream that can take further sort keys. <c>OrderBy</c> and <c>OrderByDescending</c>
    /// return it; <c>ThenBy</c> and <c>ThenByDescending</c> add keys through <see cref="CreateOrderedEnumerable{TKey}(Func{TElement, TKey}, IComparer{TKey}, bool)"/>.
    /// </summary>
    /// <typeparam name="TElement">Element type.</typeparam>
    public interface IOnityOrderedAsyncEnumerable<TElement> : IOnityAsyncEnumerable<TElement>
    {
        /// <summary>Creates a stream sorted by the existing keys, then by a synchronous secondary key.</summary>
        /// <typeparam name="TKey">Secondary key type.</typeparam>
        /// <param name="keySelector">Secondary key selector.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="descending">True to sort this key in descending order.</param>
        /// <returns>A new ordered description; this one is unchanged.</returns>
        IOnityOrderedAsyncEnumerable<TElement> CreateOrderedEnumerable<TKey>(Func<TElement, TKey> keySelector,
            IComparer<TKey> comparer, bool descending);

        /// <summary>Creates a stream sorted by the existing keys, then by an asynchronous secondary key.</summary>
        /// <typeparam name="TKey">Secondary key type.</typeparam>
        /// <param name="keySelector">Secondary key selector, awaited once per element.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="descending">True to sort this key in descending order.</param>
        /// <returns>A new ordered description; this one is unchanged.</returns>
        IOnityOrderedAsyncEnumerable<TElement> CreateOrderedEnumerable<TKey>(Func<TElement, OnityTask<TKey>> keySelector,
            IComparer<TKey> comparer, bool descending);

        /// <summary>Creates a stream sorted by the existing keys, then by a token-aware asynchronous secondary key.</summary>
        /// <typeparam name="TKey">Secondary key type.</typeparam>
        /// <param name="keySelector">Secondary key selector receiving a token owned by the enumeration.</param>
        /// <param name="comparer">Key comparer; null selects the default comparer.</param>
        /// <param name="descending">True to sort this key in descending order.</param>
        /// <returns>A new ordered description; this one is unchanged.</returns>
        IOnityOrderedAsyncEnumerable<TElement> CreateOrderedEnumerable<TKey>(
            Func<TElement, CancellationToken, OnityTask<TKey>> keySelector, IComparer<TKey> comparer, bool descending);
    }
}
