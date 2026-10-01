using System;
using Onity.Core;

namespace Onity.DI
{
    /// <summary>
    /// Fluent builder for a single contract binding.
    /// </summary>
    /// <typeparam name="TContract">Contract type.</typeparam>
    public sealed class TypeBindingBuilder<TContract>
    {
        private readonly OnityContainer m_container;
        private Type m_implementationType;
        private bool m_isBound;
        private bool m_isNonLazyRegistered;
        private readonly bool m_replace;
        private object m_id;
        private Type m_consumerType;
        private OnityContainer.IBakedProvider m_provider;

        internal TypeBindingBuilder(OnityContainer container, bool replace = false, object id = null)
        {
            m_container = container;
            m_replace = replace;
            m_id = id;
            m_implementationType = typeof(TContract);
            m_isBound = false;
            m_isNonLazyRegistered = false;
        }

        /// <summary>
        /// Sets the concrete implementation for this contract.
        /// </summary>
        /// <typeparam name="TConcrete">Concrete type.</typeparam>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> To<TConcrete>()
            where TConcrete : TContract
        {
            ValidateNotBound();
            m_implementationType = typeof(TConcrete);
            return this;
        }

        /// <summary>Sets an identifier before registering the lifetime. Null selects the unkeyed binding.</summary>
        /// <param name="id">Stable binding identifier, compared by value.</param>
        /// <returns>Current builder.</returns>
        public TypeBindingBuilder<TContract> WithId(object id)
        {
            ValidateNotBound();
            m_id = id;
            return this;
        }

        /// <summary>Restricts injection to this consumer type and its derived types. Call before the lifetime.</summary>
        /// <typeparam name="TConsumer">Consuming implementation type or interface.</typeparam>
        /// <returns>Current builder.</returns>
        public TypeBindingBuilder<TContract> WhenInjectedInto<TConsumer>()
        {
            ValidateNotBound();
            m_consumerType = typeof(TConsumer);
            return this;
        }

        /// <summary>
        /// Registers this binding as singleton.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> AsSingle()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                typeof(TContract), m_implementationType, BindingLifetime.Singleton, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Registers this binding as transient.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> AsTransient()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                typeof(TContract), m_implementationType, BindingLifetime.Transient, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>Reuses one instance per resolving container scope.</summary>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> AsScoped()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                typeof(TContract), m_implementationType, BindingLifetime.Scoped, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Exports this contract from a child container installed once per requesting scope.
        /// The binding inside the child chooses the exported service lifetime.
        /// </summary>
        /// <param name="install">Registers the contract inside the child container.</param>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> FromSubContainerResolve(Action<OnityContainer> install)
        {
            ValidateNotBound();

            if (m_implementationType != typeof(TContract))
            {
                throw new OnityBindingException("To and FromSubContainerResolve cannot be combined.");
            }

            m_provider = m_container.RegisterSubContainer(typeof(TContract), install, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Resolves this contract during container build.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> NonLazy()
        {
            if (m_isBound == false)
            {
                throw new OnityBindingException(
                    $"Select a lifetime or {nameof(FromSubContainerResolve)} before {nameof(NonLazy)}.");
            }

            if (m_isNonLazyRegistered)
            {
                return this;
            }

            if (m_consumerType != null)
            {
                throw new OnityBindingException("NonLazy requires an unconditional binding.");
            }

            m_container.RegisterNonLazy(m_provider);
            m_isNonLazyRegistered = true;
            return this;
        }

        private void ValidateNotBound()
        {
            if (m_isBound)
            {
                throw new OnityBindingException("Configure the binding before selecting a lifetime.");
            }
        }
    }
}
