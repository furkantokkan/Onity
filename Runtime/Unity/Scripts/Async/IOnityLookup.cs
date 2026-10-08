using System.Collections.Generic;

namespace Onity.Unity.Async
{
    /// <summary>
    /// A read-only collection of keys, each mapped to the sequence of elements stored under it. The
    /// asynchronous <c>ToLookupAsync</c> operators return it; the type stands in for
    /// <c>System.Linq.ILookup</c>, which Onity does not depend on. Enumerating it yields one
    /// <see cref="IOnityGrouping{TKey, TElement}"/> per distinct key, in first-seen key order.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <typeparam name="TElement">Element type.</typeparam>
    public interface IOnityLookup<TKey, TElement> : IEnumerable<IOnityGrouping<TKey, TElement>>
    {
        /// <summary>Gets the number of distinct keys, which is the number of groupings.</summary>
        int Count { get; }

        /// <summary>Gets the elements stored under a key.</summary>
        /// <param name="key">Key to look up. A null key is supported when elements were stored under it.</param>
        /// <returns>The elements in the order they were added, or an empty sequence when the key is absent.</returns>
        IEnumerable<TElement> this[TKey key] { get; }

        /// <summary>Determines whether at least one element is stored under a key.</summary>
        /// <param name="key">Key to find. A null key is supported.</param>
        /// <returns>True when the key is present.</returns>
        bool Contains(TKey key);
    }
}
