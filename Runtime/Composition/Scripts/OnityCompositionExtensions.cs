using Onity.DI;
using Onity.Messaging;
using Onity.Reactive;

namespace Onity.Composition
{
    /// <summary>
    /// Fluent feature-installer helpers that compose the Onity DI, Reactive, and
    /// Messaging pillars into single-line shared-instance bindings.
    /// </summary>
    /// <remarks>
    /// Every method here builds one primitive (a <see cref="ReactiveProperty{T}"/>,
    /// a <see cref="Subject{T}"/>, or a <see cref="MessageChannel{T}"/>) and registers
    /// that single object against each contract it satisfies via
    /// <see cref="OnityContainer.BindInstance{TContract}"/>. Because a value binding
    /// wraps the exact instance handed to it, every contract resolves to the same
    /// shared object. This is the documented way to share one instance across several
    /// contracts; two separate <c>Bind&lt;IFoo&gt;().To&lt;C&gt;()</c> calls would
    /// instead produce distinct singletons. Binding is allocation-free beyond the one
    /// primitive being registered.
    /// </remarks>
    public static class OnityCompositionExtensions
    {
        /// <summary>
        /// Registers a shared <see cref="ReactiveProperty{T}"/> so both
        /// <see cref="IReadOnlyReactiveProperty{T}"/> and the concrete
        /// <see cref="ReactiveProperty{T}"/> resolve to one instance.
        /// </summary>
        /// <typeparam name="T">Property value type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <param name="initialValue">Initial value seeded into the property.</param>
        /// <returns>The shared reactive property, so callers can read or seed it inline.</returns>
        public static ReactiveProperty<T> BindReactiveProperty<T>(this OnityContainer container, T initialValue)
        {
            ReactiveProperty<T> property = new ReactiveProperty<T>(initialValue);
            container.BindInstance<ReactiveProperty<T>>(property);
            container.BindInstance<IReadOnlyReactiveProperty<T>>(property);
            return property;
        }

        /// <summary>
        /// Registers a shared <see cref="Subject{T}"/> resolvable by its concrete type.
        /// </summary>
        /// <typeparam name="T">Subject value type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <returns>The shared subject, so callers can publish to it inline.</returns>
        public static Subject<T> BindSubject<T>(this OnityContainer container)
        {
            Subject<T> subject = new Subject<T>();
            container.BindInstance<Subject<T>>(subject);
            return subject;
        }

        /// <summary>
        /// Declares a typed message by registering a shared <see cref="MessageChannel{T}"/>
        /// against <see cref="IPublisher{T}"/>, <see cref="ISubscriber{T}"/>, and the
        /// concrete channel, so injecting any of the three resolves to one channel and a
        /// published message reaches every subscriber.
        /// </summary>
        /// <typeparam name="T">Message type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <returns>The shared message channel, so callers can publish or subscribe inline.</returns>
        public static MessageChannel<T> DeclareMessage<T>(this OnityContainer container)
        {
            MessageChannel<T> channel = new MessageChannel<T>();
            container.BindInstance<MessageChannel<T>>(channel);
            container.BindInstance<IPublisher<T>>(channel);
            container.BindInstance<ISubscriber<T>>(channel);
            return channel;
        }
    }
}
