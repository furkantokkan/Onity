using System;

namespace Onity.Messaging
{
    /// <summary>
    /// Convenience helpers for broker-style publish and subscribe calls.
    /// </summary>
    public static class MessageBrokerExtensions
    {
        /// <summary>
        /// Publishes a message through the broker without resolving the typed publisher manually.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="broker">Target broker.</param>
        /// <param name="message">Published payload.</param>
        public static void Publish<TMessage>(this IMessageBroker broker, TMessage message)
        {
            if (broker == null)
            {
                throw new ArgumentNullException(nameof(broker));
            }

            broker.GetPublisher<TMessage>().Publish(message);
        }

        /// <summary>
        /// Subscribes to a broker channel without resolving the typed subscriber manually.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="broker">Target broker.</param>
        /// <param name="handler">Message callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<TMessage>(
            this IMessageBroker broker,
            MessageHandler<TMessage> handler)
        {
            if (broker == null)
            {
                throw new ArgumentNullException(nameof(broker));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return broker.GetSubscriber<TMessage>().Subscribe(handler);
        }
    }
}
