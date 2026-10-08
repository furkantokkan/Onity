namespace Onity.Unity.Async
{
    /// <summary>
    /// What a bounded buffer does with a new item when it is full. A synchronous publisher cannot wait
    /// for space, so the buffer has to decide immediately.
    /// </summary>
    public enum OnityBufferOverflow
    {
        /// <summary>
        /// Rejects the new item and stops accepting more; the consumer drains the accepted items and
        /// then the stream faults with an <see cref="System.InvalidOperationException" />.
        /// </summary>
        Fault = 0,

        /// <summary>
        /// Drops the oldest buffered item to make room for the new one.
        /// </summary>
        DropOldest = 1,

        /// <summary>
        /// Drops the new item and keeps the buffered ones.
        /// </summary>
        DropNewest = 2
    }
}
