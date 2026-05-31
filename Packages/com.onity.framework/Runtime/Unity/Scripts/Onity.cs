using System;
using Onity.Messaging;
using Onity.Reactive;
using Onity.Unity.Contexts;
using Onity.Unity.Messaging;
using Onity.Unity.Reactive;
using UnityEngine;

namespace Onity.Unity
{
    /// <summary>
    /// Unity-facing shorthand for commonly used Onity services.
    /// </summary>
    public static class Onity
    {
        /// <summary>
        /// Publishes a message through the default active Onity context.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="message">Published payload.</param>
        public static void Publish<TMessage>(TMessage message)
        {
            ResolveDefaultEventHub().Publish(message);
        }

        /// <summary>
        /// Publishes a message through the nearest context for the owner component.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="owner">Component used to choose the nearest context.</param>
        /// <param name="message">Published payload.</param>
        public static void Publish<TMessage>(Component owner, TMessage message)
        {
            ResolveEventHub(owner).Publish(message);
        }

        /// <summary>
        /// Subscribes through the default active Onity context.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="handler">Message callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<TMessage>(MessageHandler<TMessage> handler)
        {
            return ResolveDefaultEventHub().Subscribe(handler);
        }

        /// <summary>
        /// Subscribes through the nearest context for the owner component and disposes on destroy.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="owner">Component used to choose the nearest context and lifetime.</param>
        /// <param name="handler">Message callback.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable Subscribe<TMessage>(
            Component owner,
            MessageHandler<TMessage> handler)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            return ResolveEventHub(owner).Subscribe(handler).AddTo(owner);
        }

        /// <summary>
        /// Observes a message stream from the default active Onity context.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <returns>Observable stream for the message type.</returns>
        public static IOnityObservable<TMessage> Observe<TMessage>()
        {
            return ResolveDefaultEventHub().Observe<TMessage>();
        }

        /// <summary>
        /// Observes a message stream from the nearest context for the owner component.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="owner">Component used to choose the nearest context.</param>
        /// <returns>Observable stream for the message type.</returns>
        public static IOnityObservable<TMessage> Observe<TMessage>(Component owner)
        {
            return ResolveEventHub(owner).Observe<TMessage>();
        }

        /// <summary>
        /// Tries to resolve the event hub from the default active Onity context.
        /// </summary>
        /// <param name="eventHub">Resolved event hub when available.</param>
        /// <returns>True when an event hub is available.</returns>
        public static bool TryGetEventHub(out OnityEventHub eventHub)
        {
            return OnityContext.TryResolveDefault(out eventHub);
        }

        /// <summary>
        /// Tries to resolve the event hub nearest to an owner component.
        /// </summary>
        /// <param name="owner">Component used to choose the nearest context.</param>
        /// <param name="eventHub">Resolved event hub when available.</param>
        /// <returns>True when an event hub is available.</returns>
        public static bool TryGetEventHub(Component owner, out OnityEventHub eventHub)
        {
            if (owner == null)
            {
                eventHub = null;
                return false;
            }

            if (OnityContext.TryResolveNearest(owner, out eventHub))
            {
                return true;
            }

            return OnityContext.TryResolveDefault(out eventHub);
        }

        /// <summary>
        /// Resolves the event hub from the default active Onity context.
        /// </summary>
        /// <returns>Resolved event hub.</returns>
        public static OnityEventHub GetEventHub()
        {
            return ResolveDefaultEventHub();
        }

        /// <summary>
        /// Resolves the event hub nearest to an owner component.
        /// </summary>
        /// <param name="owner">Component used to choose the nearest context.</param>
        /// <returns>Resolved event hub.</returns>
        public static OnityEventHub GetEventHub(Component owner)
        {
            return ResolveEventHub(owner);
        }

        private static OnityEventHub ResolveDefaultEventHub()
        {
            if (TryGetEventHub(out OnityEventHub eventHub))
            {
                return eventHub;
            }

            throw new InvalidOperationException(
                "Onity event access requires an active ProjectContext, SceneContext, or GameObjectContext.");
        }

        private static OnityEventHub ResolveEventHub(Component owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (TryGetEventHub(owner, out OnityEventHub eventHub))
            {
                return eventHub;
            }

            throw new InvalidOperationException(
                "Onity event access requires an active OnityContext near the owner component.");
        }
    }
}
