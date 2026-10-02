using System;

namespace Onity.Unity.Async
{
    /// <summary>
    /// A shared ("hot") asynchronous stream: one enumeration of an underlying source, started by
    /// <see cref="Connect"/>, whose items are delivered to every enumerator of this stream.
    /// <c>Publish</c> returns it.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public interface IOnityConnectableAsyncEnumerable<out T> : IOnityAsyncEnumerable<T>
    {
        /// <summary>Starts the shared enumeration of the underlying source, once.</summary>
        /// <returns>The connection. Disposing it stops the shared enumeration; later calls return the same
        /// connection and never restart the source.</returns>
        IDisposable Connect();
    }
}
