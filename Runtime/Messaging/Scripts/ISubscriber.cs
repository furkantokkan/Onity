using System;

namespace Onity.Messaging
{
    /// <summary>
    /// Subscribes to broker messages.
    /// </summary>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public interface ISubscriber<TMessage>
    {
        /// <summary>
        /// Subscribes to message notifications.
        /// </summary>
        /// <param name="handler">Notification callback.</param>
        /// <returns>Disposable subscription token.</returns>
        IDisposable Subscribe(MessageHandler<TMessage> handler);
    }
}
