using System.Threading;
using System.Threading.Tasks;

namespace Onity.Messaging
{
    /// <summary>
    /// Publishes messages to awaitable subscribers in the same broker scope.
    /// </summary>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public interface IAsyncPublisher<TMessage>
    {
        /// <summary>
        /// Publishes a message and awaits its delivery to all subscribers.
        /// </summary>
        /// <param name="message">Message value.</param>
        /// <param name="ct">Token that cancels delivery.</param>
        /// <returns>Task that completes when delivery finishes.</returns>
        ValueTask PublishAsync(TMessage message, CancellationToken ct);
    }
}
