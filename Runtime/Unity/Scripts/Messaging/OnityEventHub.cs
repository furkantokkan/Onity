using System;
using System.Collections.Generic;
using Onity.Messaging;
using Onity.Reactive;

namespace Onity.Unity.Messaging
{
    /// <summary>
    /// Central event access point over the scoped message broker.
    /// </summary>
    public sealed class OnityEventHub
    {
        private readonly IMessageBroker m_broker;
        private readonly Dictionary<Type, object> m_streamMap;
        private readonly object m_gate;

        /// <summary>
        /// Initializes a new event hub over the provided broker scope.
        /// </summary>
        /// <param name="broker">Scoped message broker.</param>
        public OnityEventHub(IMessageBroker broker)
        {
            m_broker = broker ?? throw new ArgumentNullException(nameof(broker));
            m_streamMap = new Dictionary<Type, object>(32);
            m_gate = new object();
        }

        /// <summary>
        /// Publishes one message into the current scope.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="message">Published payload.</param>
        public void Publish<TMessage>(TMessage message)
        {
            m_broker.Publish(message);
        }

        /// <summary>
        /// Subscribes to one message type in the current scope.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="handler">Message callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public IDisposable Subscribe<TMessage>(MessageHandler<TMessage> handler)
        {
            return m_broker.Subscribe(handler);
        }

        /// <summary>
        /// Observes one message type as an Onity observable stream.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <returns>Observable stream for the message type.</returns>
        public IOnityObservable<TMessage> Observe<TMessage>()
        {
            Type messageType = typeof(TMessage);

            lock (m_gate)
            {
                if (m_streamMap.TryGetValue(messageType, out object existingStream))
                {
                    return (IOnityObservable<TMessage>)existingStream;
                }

                IOnityObservable<TMessage> stream = m_broker.Observe<TMessage>();
                m_streamMap.Add(messageType, stream);
                return stream;
            }
        }
    }
}
