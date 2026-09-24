namespace Onity.Messaging
{
    /// <summary>
    /// Publishes messages to subscribers in the same broker scope.
    /// </summary>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public interface IPublisher<TMessage>
    {
        /// <summary>
        /// Publishes a message.
        /// </summary>
        /// <param name="message">Message value.</param>
        void Publish(TMessage message);
    }
}
