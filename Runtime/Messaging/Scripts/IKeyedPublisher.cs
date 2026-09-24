namespace Onity.Messaging
{
    /// <summary>
    /// Publishes messages to subscribers registered for a matching key.
    /// </summary>
    /// <typeparam name="TKey">Routing key type.</typeparam>
    /// <typeparam name="TMessage">Message type.</typeparam>
    public interface IKeyedPublisher<TKey, TMessage>
    {
        /// <summary>
        /// Publishes a message to subscribers registered for the given key.
        /// </summary>
        /// <param name="key">Routing key.</param>
        /// <param name="message">Message value.</param>
        void Publish(TKey key, TMessage message);
    }
}
