using Onity.DI;
using Onity.Messaging;

namespace Onity.Unity.Messaging
{
    /// <summary>
    /// Convenience extensions for registering typed message channels in a container.
    /// </summary>
    public static class OnityMessageBindingExtensions
    {
        /// <summary>
        /// Binds typed publisher and subscriber interfaces for a message type.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="container">Target container.</param>
        public static void BindMessageChannel<TMessage>(this OnityContainer container)
        {
            IMessageBroker broker = container.Resolve<IMessageBroker>();
            container.BindInstance<IPublisher<TMessage>>(broker.GetPublisher<TMessage>());
            container.BindInstance<ISubscriber<TMessage>>(broker.GetSubscriber<TMessage>());
        }
    }
}
