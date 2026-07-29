using System;
using Onity.Messaging;
using Onity.Reactive;

namespace Onity.Unity.Messaging
{
    /// <summary>
    /// Reactive bridges for broker-backed message subscriptions.
    /// </summary>
    public static class OnityMessageReactiveExtensions
    {
        /// <summary>
        /// Observes a broker channel as an Onity observable stream.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="broker">Target broker.</param>
        /// <returns>Observable stream for the message type.</returns>
        public static IOnityObservable<TMessage> Observe<TMessage>(this IMessageBroker broker)
        {
            if (broker == null)
            {
                throw new ArgumentNullException(nameof(broker));
            }

            return broker.GetSubscriber<TMessage>().Observe();
        }

        /// <summary>
        /// Observes a subscriber channel as an Onity observable stream.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="subscriber">Typed subscriber.</param>
        /// <returns>Observable stream for the message type.</returns>
        public static IOnityObservable<TMessage> Observe<TMessage>(this ISubscriber<TMessage> subscriber)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            return new OnityObservable<TMessage>(
                observer => subscriber.Subscribe(message => observer(message)));
        }
    }
}
