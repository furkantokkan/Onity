using System;
using Onity.Messaging;
using Onity.Reactive;
using UnityEngine;

namespace Onity.Unity.Messaging
{
    /// <summary>
    /// Component-scoped event shortcuts for choosing the nearest Onity context.
    /// </summary>
    public static class OnityEventComponentExtensions
    {
        /// <summary>
        /// Publishes a message through the nearest context for the owner component.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="owner">Component used to choose the nearest context.</param>
        /// <param name="message">Published payload.</param>
        public static void Publish<TMessage>(this Component owner, TMessage message)
        {
            global::Onity.Unity.Onity.Publish(owner, message);
        }

        /// <summary>
        /// Subscribes through the nearest context for the owner component and disposes on destroy.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="owner">Component used to choose the nearest context and lifetime.</param>
        /// <param name="handler">Message callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<TMessage>(
            this Component owner,
            MessageHandler<TMessage> handler)
        {
            return global::Onity.Unity.Onity.Subscribe(owner, handler);
        }

        /// <summary>
        /// Observes a message stream from the nearest context for the owner component.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="owner">Component used to choose the nearest context.</param>
        /// <returns>Observable stream for the message type.</returns>
        public static IOnityObservable<TMessage> Observe<TMessage>(this Component owner)
        {
            return global::Onity.Unity.Onity.Observe<TMessage>(owner);
        }

        /// <summary>
        /// Resolves the event hub nearest to the owner component.
        /// </summary>
        /// <param name="owner">Component used to choose the nearest context.</param>
        /// <returns>Resolved event hub.</returns>
        public static OnityEventHub GetEventHub(this Component owner)
        {
            return global::Onity.Unity.Onity.GetEventHub(owner);
        }
    }
}
