namespace Onity.Messaging
{
    /// <summary>
    /// Provides typed publishers and subscribers per message channel.
    /// </summary>
    public interface IMessageBroker
    {
        /// <summary>
        /// Gets a typed publisher.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <returns>Publisher instance.</returns>
        IPublisher<TMessage> GetPublisher<TMessage>();

        /// <summary>
        /// Gets a typed subscriber.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <returns>Subscriber instance.</returns>
        ISubscriber<TMessage> GetSubscriber<TMessage>();
    }
}
