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
    /// a <see cref="Subject{T}"/>, a <see cref="MessageChannel{T}"/>, or an
    /// <see cref="AsyncMessageChannel{T}"/>) and registers that single object against
    /// each contract it satisfies via <see cref="OnityContainer.BindInstance{TContract}"/>.
    /// Because a value binding wraps the exact instance handed to it, every contract
    /// resolves to the same shared object. This is the documented way to share one
    /// instance across several contracts; two separate <c>Bind&lt;IFoo&gt;().To&lt;C&gt;()</c>
    /// calls would instead produce distinct singletons. Binding is allocation-free beyond
    /// the one primitive being registered and its scope registration.
    /// <para>
    /// The helper creates the primitive, so the container owns it: it is disposed when the
    /// container is disposed, after the container's <see cref="OnityContainer.LifetimeToken"/>
    /// is canceled (see <see cref="OnityScopeLifetimeExtensions.AddTo{TDisposable}"/>). An
    /// instance you create yourself and pass to <c>BindInstance</c> stays caller-owned.
    /// </para>
    /// </remarks>
    public static class OnityCompositionExtensions
    {
        /// <summary>
        /// Registers a shared <see cref="ReactiveProperty{T}"/> so both
        /// <see cref="IReadOnlyReactiveProperty{T}"/> and the concrete
        /// <see cref="ReactiveProperty{T}"/> resolve to one instance. The container
        /// disposes the property when it is disposed.
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
            property.AddTo(container);
            return property;
        }

        /// <summary>
        /// Registers a shared <see cref="Subject{T}"/> resolvable by its concrete type. The
        /// container disposes the subject when it is disposed.
        /// </summary>
        /// <typeparam name="T">Subject value type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <returns>The shared subject, so callers can publish to it inline.</returns>
        public static Subject<T> BindSubject<T>(this OnityContainer container)
        {
            Subject<T> subject = new Subject<T>();
            container.BindInstance<Subject<T>>(subject);
            subject.AddTo(container);
            return subject;
        }

        /// <summary>
        /// Declares a typed message by registering a shared <see cref="MessageChannel{T}"/>
        /// against <see cref="IPublisher{T}"/>, <see cref="ISubscriber{T}"/>, and the
        /// concrete channel, so injecting any of the three resolves to one channel and a
        /// published message reaches every subscriber. The container disposes the channel
        /// when it is disposed.
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
            channel.AddTo(container);
            return channel;
        }

        /// <summary>
        /// Declares a typed awaitable message by registering a shared
        /// <see cref="AsyncMessageChannel{T}"/> against <see cref="IAsyncPublisher{T}"/>,
        /// <see cref="IAsyncSubscriber{T}"/>, and the concrete channel, so injecting any of the
        /// three resolves to one channel. The container disposes the channel when it is disposed.
        /// </summary>
        /// <typeparam name="T">Message type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <returns>The shared async message channel, so callers can publish or subscribe inline.</returns>
        /// <remarks>
        /// <see cref="AsyncMessageChannel{T}.PublishAsync"/> awaits each handler in turn. A handler
        /// that awaits the scope token (an injected <see cref="IOnityScopeLifetime"/>) is canceled
        /// when the scope ends, so a publish waiting on it ends canceled instead of hanging.
        /// </remarks>
        public static AsyncMessageChannel<T> DeclareAsyncMessage<T>(this OnityContainer container)
        {
            AsyncMessageChannel<T> channel = new AsyncMessageChannel<T>();
            container.BindInstance<AsyncMessageChannel<T>>(channel);
            container.BindInstance<IAsyncPublisher<T>>(channel);
            container.BindInstance<IAsyncSubscriber<T>>(channel);
            channel.AddTo(container);
            return channel;
        }
    }
}
