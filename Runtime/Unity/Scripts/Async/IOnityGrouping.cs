using System.Collections.Generic;

namespace Onity.Unity.Async
{
    /// <summary>
    /// A sequence of elements that share one key. The asynchronous <c>ToLookupAsync</c> operators hand
    /// these out; the type stands in for <c>System.Linq.IGrouping</c>, which Onity does not depend on.
    /// </summary>
    /// <typeparam name="TKey">Covariant key type.</typeparam>
    /// <typeparam name="TElement">Covariant element type.</typeparam>
    public interface IOnityGrouping<out TKey, out TElement> : IEnumerable<TElement>
    {
        /// <summary>Gets the key shared by every element of this grouping.</summary>
        TKey Key { get; }
    }
}
