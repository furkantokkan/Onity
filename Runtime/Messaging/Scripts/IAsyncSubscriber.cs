using System;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Messaging
{
    /// <summary>
    /// Subscribes to broker messages with an awaitable handler. Handlers receive a
    /// cancellation token and return a <see cref="ValueTask"/> so delivery can be awaited.
    /// </summary>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public interface IAsyncSubscriber<TMessage>
    {
        /// <summary>
        /// Subscribes to awaitable message notifications.
        /// </summary>
        /// <param name="handler">Asynchronous notification callback.</param>
        /// <returns>Disposable subscription token.</returns>
        IDisposable Subscribe(Func<TMessage, CancellationToken, ValueTask> handler);
    }
}
