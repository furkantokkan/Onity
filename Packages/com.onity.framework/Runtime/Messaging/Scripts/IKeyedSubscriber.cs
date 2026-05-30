using System;

namespace Onity.Messaging
{
    /// <summary>
    /// Subscribes to keyed broker messages for a specific key.
    /// </summary>
    /// <typeparam name="TKey">Routing key type.</typeparam>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public interface IKeyedSubscriber<TKey, TMessage>
    {
        /// <summary>
        /// Subscribes to message notifications routed to the given key.
        /// </summary>
        /// <param name="key">Routing key.</param>
        /// <param name="handler">Notification callback.</param>
        /// <returns>Disposable subscription token.</returns>
        IDisposable Subscribe(TKey key, MessageHandler<TMessage> handler);
    }
}
