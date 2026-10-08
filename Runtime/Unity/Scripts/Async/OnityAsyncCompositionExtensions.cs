using Onity.DI;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Single-line shared-instance bindings for Onity's native async primitives, the Unity-side
    /// counterpart of the <c>Onity.Composition</c> helpers.
    /// </summary>
    public static class OnityAsyncCompositionExtensions
    {
        /// <summary>
        /// Registers a shared <see cref="OnityAsyncReactiveProperty{T}" /> so the concrete property,
        /// <see cref="IOnityAsyncReactiveProperty{T}" /> and <see cref="IOnityReadOnlyAsyncReactiveProperty{T}" />
        /// resolve to one instance.
        /// </summary>
        /// <typeparam name="T">Property value type.</typeparam>
        /// <param name="container">Target container.</param>
        /// <param name="initialValue">Initial value of the property.</param>
        /// <returns>The shared property, so callers can read or seed it inline.</returns>
        /// <remarks>
        /// The helper creates the property, so the container owns it: when the container is disposed
        /// (after its <see cref="OnityContainer.LifetimeToken" /> is canceled), the property is
        /// disposed, which cancels its pending <c>WaitAsync</c> calls and ends its enumerators.
        /// </remarks>
        public static OnityAsyncReactiveProperty<T> BindAsyncReactiveProperty<T>(
            this OnityContainer container,
            T initialValue)
        {
            OnityAsyncReactiveProperty<T> property = new OnityAsyncReactiveProperty<T>(initialValue);
            container.BindInstance<OnityAsyncReactiveProperty<T>>(property);
            container.BindInstance<IOnityAsyncReactiveProperty<T>>(property);
            container.BindInstance<IOnityReadOnlyAsyncReactiveProperty<T>>(property);
            property.AddTo(container);
            return property;
        }
    }
}
