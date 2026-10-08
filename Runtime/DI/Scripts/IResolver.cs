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

        /// <summary>Resolves an identified binding; null selects the unkeyed binding.</summary>
        /// <typeparam name="TService">Service type.</typeparam>
        /// <param name="id">Binding identifier, compared by value.</param>
        /// <returns>Resolved service instance.</returns>
        TService Resolve<TService>(object id);

        /// <summary>Resolves an identified binding; null selects the unkeyed binding.</summary>
        /// <param name="serviceType">Service type.</param>
        /// <param name="id">Binding identifier, compared by value.</param>
        /// <returns>Resolved service instance.</returns>
        object Resolve(Type serviceType, object id);

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

        /// <summary>Attempts to resolve an identified binding without implicitly constructing a keyed service.</summary>
        /// <typeparam name="TService">Service type.</typeparam>
        /// <param name="id">Binding identifier; null selects the unkeyed binding.</param>
        /// <param name="instance">Resolved instance when successful.</param>
        /// <returns>True when found; construction and ambiguity errors still throw.</returns>
        bool TryResolve<TService>(object id, out TService instance);

        /// <summary>Attempts to resolve an identified binding without implicitly constructing a keyed service.</summary>
        /// <param name="serviceType">Service type.</param>
        /// <param name="id">Binding identifier; null selects the unkeyed binding.</param>
        /// <param name="instance">Resolved instance when successful.</param>
        /// <returns>True when found; construction and ambiguity errors still throw.</returns>
        bool TryResolve(Type serviceType, object id, out object instance);

        /// <summary>
        /// Injects dependencies into an existing instance.
        /// </summary>
        /// <param name="target">Target instance.</param>
        void Inject(object target);
    }
}
