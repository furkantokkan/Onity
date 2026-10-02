namespace Onity.Unity.Async
{
    public static partial class OnityAsyncEnumerable
    {
        /// <summary>Interleaves several sources into one stream in arrival order.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="sources">Nonempty sources, copied when this method is called.</param>
        /// <returns>A lazy description with independent enumerations.</returns>
        /// <remarks>Each move moves every source that has no outstanding move and has not ended, then
        /// relays the first arriving item; items that arrive while nobody is waiting are queued in
        /// arrival order (at most one per source, since a source is moved again only on a later move).
        /// Sources are acquired at the first move with the enumeration token. The stream ends when every
        /// source has ended. The first fault or cancellation is relayed after the items that arrived
        /// before it; later items and faults are observed and dropped. Every source is disposed exactly
        /// once, in argument order, before the end or fault is published, and its pending move is settled
        /// by that disposal; the first disposal failure takes precedence. Moves of a source may start on
        /// the thread that completed another source, so merge sources with compatible thread affinity.
        /// A canceled enumeration token cancels the next move.</remarks>
        /// <exception cref="System.ArgumentNullException">The array is null.</exception>
        /// <exception cref="System.ArgumentException">The array is empty or contains null.</exception>
        public static IOnityAsyncEnumerable<T> Merge<T>(params IOnityAsyncEnumerable<T>[] sources)
        {
            return OnityMergeAsyncEnumerable<T>.Create(sources, nameof(sources));
        }
    }
}
