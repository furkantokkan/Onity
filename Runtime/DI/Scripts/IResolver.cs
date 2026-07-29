using System;

namespace Onity.DI
{
    /// <summary>
    /// Resolves and injects dependencies from a container scope.
    /// </summary>
    public interface IResolver
    {
        /// <summary>
        /// Resolves a service by generic type.
        /// </summary>
        /// <typeparam name="TService">Service type.</typeparam>
        /// <returns>Resolved service instance.</returns>
        TService Resolve<TService>();

        /// <summary>
        /// Resolves a service by runtime type.
        /// </summary>
        /// <param name="serviceType">Service type.</param>
        /// <returns>Resolved service instance.</returns>
        object Resolve(Type serviceType);

        /// <summary>
        /// Attempts to resolve a service by generic type.
        /// </summary>
        /// <typeparam name="TService">Service type.</typeparam>
        /// <param name="instance">Resolved instance when successful.</param>
        /// <returns>True when resolved; otherwise false.</returns>
        bool TryResolve<TService>(out TService instance);

        /// <summary>
        /// Attempts to resolve a service by runtime type.
        /// </summary>
        /// <param name="serviceType">Service type.</param>
        /// <param name="instance">Resolved instance when successful.</param>
        /// <returns>True when resolved; otherwise false.</returns>
        bool TryResolve(Type serviceType, out object instance);

        /// <summary>
        /// Injects dependencies into an existing instance.
        /// </summary>
        /// <param name="target">Target instance.</param>
        void Inject(object target);
    }
}
