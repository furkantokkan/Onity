namespace Onity.Messaging
{
    /// <summary>
    /// Callback signature for message subscriptions.
    /// </summary>
    /// <typeparam name="TMessage">Message type.</typeparam>
    /// <param name="message">Published message instance.</param>
    public delegate void MessageHandler<TMessage>(TMessage message);
}
