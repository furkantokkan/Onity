using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>A reusable description of independent asynchronous enumerations.</summary>
    /// <typeparam name="T">Covariant item type.</typeparam>
    /// <remarks>Supports await foreach through the native pattern. Compiler-generated
    /// async iterator methods cannot return this custom interface.</remarks>
    public interface IOnityAsyncEnumerable<out T>
    {
        /// <summary>Creates an independent enumerator with the supplied cooperative token.</summary>
        /// <param name="cancellationToken">Token forwarded to this enumeration.</param>
        /// <returns>A caller-owned enumerator.</returns>
        IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
    }
}
